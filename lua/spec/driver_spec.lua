require("spec.helper")
local stub = require("spec.wow_stub")

local KEY_HEX = ("0123456789abcdef"):rep(4)
local QUIET, SIZE = 4, 57
local STEP = 1 / 60

local function read(path)
  local f = assert(io.open(path, "rb"))
  local s = f:read("*a")
  f:close()
  return s
end

local function loadCopy(minor)
  local dir = "LibCorvid-1.0/"
  for name in read(dir .. "lib.xml"):gmatch('<Script file="([^"]+)"/>') do
    local src = read(dir .. name)
    if name == "Core.lua" then
      local n
      src, n = src:gsub('NewLibrary%("LibCorvid%-1%.0", 1%)', 'NewLibrary("LibCorvid-1.0", ' .. minor .. ')')
      assert(n == 1)
    end
    assert(loadstring(src, name))()
  end
  return LibStub("LibCorvid-1.0")
end

local function fresh()
  stub.reset()
  _G.LibStub = nil
  return loadCopy(1)
end

local function update(lib, n, elapsed)
  local mark = lib.Mark.frame
  for _ = 1, n do
    stub.time = stub.time + (elapsed or STEP)
    mark:GetScript("OnUpdate")(mark, elapsed or STEP)
  end
end

local function fire(lib, event)
  local mark = lib.Mark.frame
  mark:GetScript("OnEvent")(mark, event)
end

local function pixel()
  return PixelUtil.GetPixelToUIUnitFactor() / UIParent:GetEffectiveScale()
end

local function whole(v, unit)
  local k = v / unit
  return math.abs(k - math.floor(k + 0.5)) < 1e-6
end

local function frontBuffer(lib)
  local shown
  for _, b in ipairs(lib.Mark.frame.buffers) do
    if b:IsShown() then
      assert.is_nil(shown)
      shown = b
    end
  end
  return assert(shown)
end

local function drawnMatrix(buffer)
  local m = 2 * pixel()
  local grid = {}
  for r = 1, SIZE do
    grid[r] = {}
    for c = 1, SIZE do grid[r][c] = false end
  end
  for _, t in ipairs(buffer.textures) do
    if t:IsShown() and t.layer == "ARTWORK" then
      local _, _, _, x, y = t:GetPoint()
      local c = math.floor(x / m + 0.5) - QUIET + 1
      local r = math.floor(-y / m + 0.5) - QUIET + 1
      local len = math.floor(t:GetWidth() / m + 0.5)
      for k = 0, len - 1 do
        assert.is_false(grid[r][c + k])
        grid[r][c + k] = true
      end
    end
  end
  return grid
end

local function expectedMatrix(lib, counter, messages)
  local Bits = lib.Bits
  local session = Bits.u32le(stub.serverTime) .. Bits.u16le(stub.random) .. Bits.u16le(stub.random)
  local frame = assert(lib.Frame.build(session, counter, {
    { channel = 1, key = Bits.hexToBytes(KEY_HEX), messages = messages },
  }))
  local matrix = lib.Qr10.encode(frame)
  local copy = {}
  for r = 1, SIZE do
    copy[r] = {}
    for c = 1, SIZE do copy[r][c] = matrix[r][c] end
  end
  return copy
end

describe("Driver", function()
  it("draws the built frame into the back buffer and swaps it in", function()
    local lib = fresh()
    assert.is_true(lib:Register(1, KEY_HEX, {}))
    assert.is_true(lib:SetStatus(1, "\1"))
    local mark = lib.Mark.frame
    assert.are.equal("TOOLTIP", mark:GetFrameStrata())
    assert.are.equal(10000, mark:GetFrameLevel())
    update(lib, 10)
    assert.are.equal(1, lib:Stats().frames)
    assert.are.same(expectedMatrix(lib, 0, { { type = 0, body = "\1" } }), drawnMatrix(frontBuffer(lib)))
  end)

  it("refuses a key that is not 64 hex chars", function()
    local lib = fresh()
    local ok, err = lib:Register(1, KEY_HEX:sub(2), {})
    assert.is_nil(ok)
    assert.are.equal("bad key", err)
    ok, err = lib:Register(1, ("zz"):rep(32), {})
    assert.are.equal("bad key", err)
  end)

  it("refuses a channel registered twice", function()
    local lib = fresh()
    assert.is_true(lib:Register(1, KEY_HEX, {}))
    local ok, err = lib:Register(1, KEY_HEX, {})
    assert.is_nil(ok)
    assert.are.equal("channel taken", err)
  end)

  it("keeps the frame and channels when a newer copy loads after an older one", function()
    local lib = fresh()
    assert.is_true(lib:Register(1, KEY_HEX, {}))
    local before = lib.Mark.frame:GetScript("OnUpdate")
    assert.are.equal(lib, loadCopy(2))
    local marks = 0
    for _, f in ipairs(stub.created) do
      if f.name == "CorvidMark" then marks = marks + 1 end
    end
    assert.are.equal(1, marks)
    assert.are_not.equal(before, lib.Mark.frame:GetScript("OnUpdate"))
    local _, err = lib:Register(1, KEY_HEX, {})
    assert.are.equal("channel taken", err)
    assert.is_true(lib:SetStatus(1, "\2"))
    update(lib, 10)
    assert.are.same(expectedMatrix(lib, 0, { { type = 0, body = "\2" } }), drawnMatrix(frontBuffer(lib)))
  end)

  it("ignores an older copy loaded after a newer one", function()
    stub.reset()
    _G.LibStub = nil
    local lib = loadCopy(2)
    assert.is_true(lib:Register(1, KEY_HEX, {}))
    local scheduler, script, created = lib.Scheduler, lib.Mark.frame:GetScript("OnUpdate"), #stub.created
    loadCopy(1)
    assert.are.equal(scheduler, lib.Scheduler)
    assert.are.equal(script, lib.Mark.frame:GetScript("OnUpdate"))
    assert.are.equal(created, #stub.created)
    assert.is_nil(lib.loading)
  end)

  it("re-snaps position and module size to whole pixels when the scale changes", function()
    local lib = fresh()
    assert.is_true(lib:Register(1, KEY_HEX, { corvidPos = { x = 101.3, y = -77.7, t = 1 } }))
    update(lib, 10)
    local mark = lib.Mark.frame

    local function check()
      local px = pixel()
      local _, _, _, x, y = mark:GetPoint()
      assert.is_true(whole(x, px))
      assert.is_true(whole(y, px))
      assert.is_true(math.abs(mark:GetWidth() - 130 * px) < 1e-9)
      for _, t in ipairs(frontBuffer(lib).textures) do
        if t:IsShown() and t.layer == "ARTWORK" then
          local _, _, _, tx, ty = t:GetPoint()
          assert.is_true(whole(tx, 2 * px))
          assert.is_true(whole(ty, 2 * px))
          assert.is_true(whole(t:GetWidth(), 2 * px))
          assert.is_true(math.abs(t:GetHeight() - 2 * px) < 1e-9)
        end
      end
    end

    UIParent:SetScale(0.8)
    fire(lib, "UI_SCALE_CHANGED")
    check()
    UIParent:SetScale(0.71)
    update(lib, 1, 2.1)
    check()
  end)

  it("places the mark at the newest stored position and moves write every db", function()
    local older = { corvidPos = { x = 100, y = -50, t = 10 } }
    local newer = { corvidPos = { x = 300, y = -200, t = 20 } }
    for _, order in ipairs({ { older, newer }, { newer, older } }) do
      local lib = fresh()
      assert.is_true(lib:Register(1, KEY_HEX, order[1]))
      assert.is_true(lib:Register(2, KEY_HEX, order[2]))
      local _, _, _, x, y = lib.Mark.frame:GetPoint()
      assert.is_true(math.abs(x - 300) <= pixel())
      assert.is_true(math.abs(y + 200) <= pixel())
    end
    stub.serverTime = stub.serverTime + 5
    LibStub("LibCorvid-1.0"):ResetPosition()
    assert.are.same(older.corvidPos, newer.corvidPos)
    assert.are.equal(stub.serverTime, newer.corvidPos.t)
  end)

  it("restarts an unfinished job when a realtime message arrives", function()
    local lib = fresh()
    assert.is_true(lib:Register(1, KEY_HEX, {}))
    assert.is_true(lib:SetStatus(1, "\1"))
    stub.msStep = 0.25
    update(lib, 1)
    assert.are.equal(0, lib:Stats().frames)
    assert.is_true(lib:Send(1, "realtime", 5, "hello"))
    for _ = 1, 2000 do
      if lib:Stats().frames > 0 then break end
      update(lib, 1)
    end
    assert.are.equal(1, lib:Stats().frames)
    local expected = expectedMatrix(lib, 1, { { type = 0, body = "\1" }, { type = 5, body = "hello" } })
    assert.are.same(expected, drawnMatrix(frontBuffer(lib)))
  end)

  it("reports reference points at the finder centres", function()
    local lib = fresh()
    local px = pixel()
    assert.is_true(lib:Register(1, KEY_HEX, { corvidPos = { x = 150 * px, y = -90 * px, t = 1 } }))
    local m = 2 * px
    local left, top = 150 * px, UIParent:GetTop() - 90 * px
    local got = { lib:ReferencePoints() }
    local want = { left + 7.5 * m, top - 7.5 * m, left + 57.5 * m, top - 7.5 * m, left + 7.5 * m, top - 57.5 * m }
    for i = 1, 6 do assert.is_true(math.abs(got[i] - want[i]) < 1e-9, "coordinate " .. i) end
  end)

  it("keeps showing frames under a realtime message every update", function()
    local lib = fresh()
    assert.is_true(lib:Register(1, KEY_HEX, {}))
    stub.msStep = 0.003
    lib:Send(1, "realtime", 5, "x")
    update(lib, 1)
    assert.are.equal(0, lib:Stats().frames)
    local windows, last = {}, 0
    for i = 1, 120 do
      lib:Send(1, "realtime", 5, "x")
      update(lib, 1)
      if i % 12 == 0 then
        local frames = lib:Stats().frames
        windows[#windows + 1] = frames - last
        last = frames
      end
    end
    for i, n in ipairs(windows) do assert.is_true(n >= 1, "window " .. i .. " " .. table.concat(windows, ",")) end
  end)

  it("returns no reference points before the mark has a position", function()
    stub.reset()
    _G.LibStub = nil
    local lib = loadCopy(1)
    assert.is_nil(lib:ReferencePoints())
    assert.is_true(lib.Mark.frame.dontSavePosition)
  end)

  it("refuses a channel outside 0..255", function()
    local lib = fresh()
    for _, channel in ipairs({ -1, 256, 1.5, "1" }) do
      local ok, err = lib:Register(channel, KEY_HEX, {})
      assert.is_nil(ok)
      assert.are.equal("bad channel", err)
    end
    assert.is_true(lib:Register(0, KEY_HEX, {}))
    assert.is_true(lib:Register(255, KEY_HEX, {}))
  end)

  it("ignores a corrupt stored position and still registers", function()
    local lib = fresh()
    assert.is_true(lib:Register(1, KEY_HEX, { corvidPos = { x = 300, y = -200, t = 20 } }))
    for i, bad in ipairs({ { x = "1", y = 2, t = 30 }, { x = 1, y = 2 }, { x = 1, t = 40 }, "junk" }) do
      local channel = 10 + i
      assert.is_true(lib:Register(channel, KEY_HEX, { corvidPos = bad }))
      assert.is_true(lib.wanted[channel])
    end
    local _, _, _, x, y = lib.Mark.frame:GetPoint()
    assert.is_true(math.abs(x - 300) <= pixel())
    assert.is_true(math.abs(y + 200) <= pixel())
  end)

  it("writes the newer position into a db registered later", function()
    local lib = fresh()
    assert.is_true(lib:Register(1, KEY_HEX, { corvidPos = { x = 300, y = -200, t = 20 } }))
    local older, empty = { corvidPos = { x = 100, y = -50, t = 10 } }, {}
    assert.is_true(lib:Register(2, KEY_HEX, older))
    assert.is_true(lib:Register(3, KEY_HEX, empty))
    assert.are.same({ x = 300, y = -200, t = 20 }, older.corvidPos)
    assert.are.same({ x = 300, y = -200, t = 20 }, empty.corvidPos)
  end)

  it("hides the mark while no channel is wanted", function()
    local lib = fresh()
    assert.is_true(lib:Register(1, KEY_HEX, {}))
    assert.is_true(lib.Mark.frame:IsShown())
    lib:SetWanted(1, false)
    assert.is_false(lib.Mark.frame:IsShown())
    lib:SetWanted(1, true)
    assert.is_true(lib.Mark.frame:IsShown())
  end)
end)
