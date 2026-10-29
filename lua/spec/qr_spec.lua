require("spec.helper")

local lib = LibStub("LibCorvid-1.0")
local Bits = lib.Bits
local Ref = dofile("spec/vendor/qrencode.lua")

local FIXTURE = "../fixtures/qr-v10.txt"
local SIZE = 268

local function upvalue(fn, wanted)
  local i = 1
  while true do
    local name, value = debug.getupvalue(fn, i)
    if not name then error("no upvalue " .. wanted) end
    if name == wanted then return value end
    i = i + 1
  end
end

local function fill(gen)
  local t = {}
  for i = 1, SIZE do t[i] = string.char(gen(i)) end
  return table.concat(t)
end

local function xorshift(seed)
  local x = seed
  return function()
    x = bit.bxor(x, bit.lshift(x, 13))
    x = bit.bxor(x, bit.rshift(x, 17))
    x = bit.bxor(x, bit.lshift(x, 5))
    return bit.band(x, 0xFF)
  end
end

local INPUTS = {
  fill(function() return 0 end),
  fill(function() return 0xFF end),
  fill(function(i) return (i - 1) % 256 end),
  fill(xorshift(1)),
  fill(xorshift(2)),
}

local function rowsOf(matrix)
  local out = {}
  for r = 1, #matrix do
    local row = {}
    for c = 1, #matrix[r] do
      row[c] = matrix[r][c] and "1" or "0"
    end
    out[r] = table.concat(row)
  end
  return out
end

local function referenceRows(input)
  local ok, m = Ref.qrcode_fixed(input, 1, 0)
  assert(ok, m)
  local out = {}
  for y = 1, #m do
    local row = {}
    for x = 1, #m do
      row[x] = m[x][y] > 0 and "1" or "0"
    end
    out[y] = table.concat(row)
  end
  return out
end

local function readFixture()
  local f = assert(io.open(FIXTURE, "rb"))
  local lines = {}
  for line in f:lines() do lines[#lines + 1] = (line:gsub("\r$", "")) end
  f:close()
  return lines
end

describe("ReedSolomon", function()
  it("matches the reference parity of v10-L block 1", function()
    local versionModeLength = upvalue(Ref.qrcode_fixed, "get_version_eclevel_mode_bistringlength")
    local encodeData = upvalue(Ref.qrcode_fixed, "encode_data")
    local addPad = upvalue(Ref.qrcode_fixed, "add_pad_data")
    local ec = upvalue(upvalue(Ref.qrcode_fixed, "arrange_codewords_and_calculate_ec"), "calculate_error_correction")
    local toBytes = upvalue(ec, "convert_bitstring_to_bytes")
    for _, input in ipairs(INPUTS) do
      local version, level, modeBits, mode, lengthBits = versionModeLength(input, 1)
      assert.are.equal(10, version)
      local stream = addPad(version, level, modeBits .. lengthBits .. encodeData(input, mode))
      local block = toBytes(stream:sub(1, 68 * 8))
      assert.are.same(ec(block, 18), lib.ReedSolomon.parity(block, 18))
    end
  end)
end)

describe("Qr10", function()
  it("matches the reference encoder and the shared fixture", function()
    local fresh = {}
    for i, input in ipairs(INPUTS) do
      local rows = rowsOf(lib.Qr10.encode(input))
      assert.are.equal(57, #rows)
      assert.are.same(referenceRows(input), rows, "input " .. i)
      fresh[#fresh + 1] = Bits.bytesToHex(input)
      for r = 1, 57 do fresh[#fresh + 1] = rows[r] end
    end
    if os.getenv("UPDATE_FIXTURES") then
      local f = assert(io.open(FIXTURE, "wb"))
      f:write(table.concat(fresh, "\n"), "\n")
      f:close()
    end
    assert.are.same(fresh, readFixture())
  end)

  it("encodes in chunks with little garbage", function()
    local input = INPUTS[4]
    collectgarbage("collect")
    collectgarbage("stop")
    local before = collectgarbage("count")
    lib.Qr10.encode(input)
    local garbage = collectgarbage("count") - before
    collectgarbage("restart")
    local co = coroutine.create(function()
      lib.Qr10.encode(input, coroutine.yield)
    end)
    local resumes, worst, total = 0, 0, 0
    while coroutine.status(co) ~= "dead" do
      local t0 = os.clock()
      assert(coroutine.resume(co))
      local dt = os.clock() - t0
      resumes = resumes + 1
      total = total + dt
      if dt > worst then worst = dt end
    end
    print(string.format("qr10 resumes=%d total=%.1f ms worst=%.2f ms garbage=%.1f KB", resumes, total * 1000, worst * 1000, garbage))
    assert.is_true(resumes > 1)
    assert.is_true(garbage < 30, string.format("garbage %.1f KB", garbage))
  end)
end)
