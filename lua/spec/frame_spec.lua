require("spec.helper")

local lib = LibStub("LibCorvid-1.0")
local Bits, Crc32, ChaCha20, Frame = lib.Bits, lib.Crc32, lib.ChaCha20, lib.Frame

local FIXTURE = "../fixtures/frames.tsv"

local function seq(first, step)
  local t = {}
  for i = 0, 31 do t[i + 1] = string.char((first + step * i) % 256) end
  return table.concat(t)
end

local KEY_A, KEY_B = seq(1, 1), seq(250, 7)
local SESSION = Bits.hexToBytes("0102030405060708")

local function seg(channel, key, ...)
  local messages = {}
  for _, m in ipairs({ ... }) do messages[#messages + 1] = { type = m[1], body = m[2] } end
  return { channel = channel, key = key, messages = messages }
end

local function sizesOf(segments)
  local shape = {}
  for i, s in ipairs(segments) do
    local sizes = {}
    for j, m in ipairs(s.messages) do sizes[j] = #m.body end
    shape[i] = { channel = s.channel, sizes = sizes }
  end
  return shape
end

local CASES = {
  { "status-only", 1, { seg(1, KEY_A, { 0, "\1" }) } },
  { "two-channels", 2, {
    seg(1, KEY_A, { 0, "\1" }, { 1, "hello" }, { 2, "" }),
    seg(7, KEY_B, { 0, "\0" }),
  } },
  { "max-fill", 3, { seg(1, KEY_A, { 0, "\1" }, { 1, string.rep("\170", 236) }) } },
  { "empty", 4, {} },
  { "counter-max", 0xFFFFFFFF, { seg(255, KEY_B, { 0, "\2" }, { 255, "\0\255" }) } },
}

local function describe_(segments)
  local parts = {}
  for i, s in ipairs(segments) do
    local msgs = {}
    for j, m in ipairs(s.messages) do msgs[j] = m.type .. "=" .. Bits.bytesToHex(m.body) end
    parts[i] = s.channel .. ":" .. Bits.bytesToHex(s.key) .. ":" .. table.concat(msgs, ",")
  end
  return table.concat(parts, ";")
end

local function parse(desc)
  local segments = {}
  for chunk in (desc .. ";"):gmatch("([^;]*);") do
    if chunk ~= "" then
      local channel, key, msgs = chunk:match("^(%d+):(%x+):(.*)$")
      local s = { channel = tonumber(channel), key = Bits.hexToBytes(key), messages = {} }
      for m in (msgs .. ","):gmatch("([^,]*),") do
        local t, body = m:match("^(%d+)=(%x*)$")
        s.messages[#s.messages + 1] = { type = tonumber(t), body = Bits.hexToBytes(body) }
      end
      segments[#segments + 1] = s
    end
  end
  return segments
end

local function readFixture()
  local rows = {}
  for line in io.lines(FIXTURE) do
    local name, session, counter, desc, hex = line:match("^([^\t]*)\t([^\t]*)\t([^\t]*)\t([^\t]*)\t([^\t]*)$")
    assert(name, "bad row: " .. line)
    rows[#rows + 1] = { name = name, session = session, counter = tonumber(counter), desc = desc, hex = hex }
  end
  return rows
end

describe("Frame", function()
  it("matches the shared fixture byte for byte", function()
    local fresh = {}
    for _, c in ipairs(CASES) do
      local frame = assert(Frame.build(SESSION, c[2], c[3]))
      assert.are.equal(268, #frame)
      fresh[#fresh + 1] = table.concat({ c[1], Bits.bytesToHex(SESSION), tostring(c[2]), describe_(c[3]), Bits.bytesToHex(frame) }, "\t")
    end
    if os.getenv("UPDATE_FIXTURES") then
      local f = assert(io.open(FIXTURE, "wb"))
      f:write(table.concat(fresh, "\n"), "\n")
      f:close()
    end
    local lines = {}
    for line in io.lines(FIXTURE) do lines[#lines + 1] = line end
    assert.are.same(fresh, lines)
  end)

  it("rebuilds every fixture row from its description", function()
    local rows = readFixture()
    assert.are.equal(#CASES, #rows)
    for _, r in ipairs(rows) do
      local frame = assert(Frame.build(Bits.hexToBytes(r.session), r.counter, parse(r.desc)))
      assert.are.equal(r.hex, Bits.bytesToHex(frame), r.name)
    end
  end)

  it("writes the header constants", function()
    local frame = Frame.build(SESSION, 0x01020304, { seg(9, KEY_A, { 0, "\1" }), seg(8, KEY_B, { 0, "\1" }) })
    assert.are.equal("\199\29\1\0", frame:sub(1, 4))
    assert.are.equal(SESSION, frame:sub(5, 12))
    assert.are.equal("\4\3\2\1", frame:sub(13, 16))
    assert.are.equal(2, frame:byte(17))
    assert.are.equal(9, frame:byte(18))
    assert.are.equal(ChaCha20.keyId(KEY_A), frame:byte(19))
    assert.are.equal(3 + 1 + 4, Bits.readU16le(frame, 20))
  end)

  it("refuses a body one byte over the free space", function()
    local ok = { seg(1, KEY_A, { 0, "\1" }, { 1, string.rep("x", 236) }) }
    assert.are.equal(0, Frame.free(sizesOf(ok)))
    assert.is_truthy(Frame.build(SESSION, 1, ok))
    local over = { seg(1, KEY_A, { 0, "\1" }, { 1, string.rep("x", 237) }) }
    local frame, err = Frame.build(SESSION, 1, over)
    assert.is_nil(frame)
    assert.are.equal("too big", err)
  end)

  it("reports free space for a shape", function()
    assert.are.equal(251, Frame.free({}))
    assert.are.equal(251 - 8 - 3 - 1 - 3 - 5, Frame.free({ { channel = 1, sizes = { 1, 5 } } }))
  end)

  it("decrypts a segment back to messages and a valid CRC", function()
    local segs = { seg(1, KEY_A, { 0, "\1" }, { 3, "abc" }) }
    local frame = Frame.build(SESSION, 77, segs)
    local len = Bits.readU16le(frame, 20)
    local plain = ChaCha20.xor(KEY_A, SESSION .. Bits.u32le(77), 0, frame:sub(22, 21 + len))
    local data, crc = plain:sub(1, -5), Bits.readU32le(plain, #plain - 3)
    assert.are.equal(Crc32.compute(data), crc)
    assert.are.equal("\0\1\0\1\3\3\0abc", data)
  end)

  it("uses a given key id instead of deriving it", function()
    local segs = { seg(1, KEY_A, { 0, "\1" }), seg(7, KEY_B, { 0, "\0" }) }
    local plain = Frame.build(SESSION, 9, segs)
    segs[1].keyId, segs[2].keyId = ChaCha20.keyId(KEY_A), ChaCha20.keyId(KEY_B)
    local keyId, calls = ChaCha20.keyId, 0
    ChaCha20.keyId = function(key)
      calls = calls + 1
      return keyId(key)
    end
    local cached = Frame.build(SESSION, 9, segs)
    ChaCha20.keyId = keyId
    assert.are.equal(0, calls)
    assert.are.equal(plain, cached)
  end)

  it("keeps the CRC for a segment without messages", function()
    local frame = Frame.build(SESSION, 5, { seg(1, KEY_A) })
    assert.are.equal(4, Bits.readU16le(frame, 20))
    local plain = ChaCha20.xor(KEY_A, SESSION .. Bits.u32le(5), 0, frame:sub(22, 25))
    assert.are.equal(Crc32.compute(""), Bits.readU32le(plain, 1))
  end)
end)
