local lib = LibStub("LibCorvid-1.0")
if not lib.loading then return end

local Bits, Frame, Qr10, Mark = lib.Bits, lib.Frame, lib.Qr10, lib.Mark

local BUDGET_MS = 1
local SCALE_CHECK_SECONDS = 2
local KEY_HEX_LENGTH = 64
local DEFAULT_X, DEFAULT_Y = 20, -20

lib.scheduler = setmetatable(lib.scheduler or lib.Scheduler.new(), lib.Scheduler)
lib.dbs = lib.dbs or {}
lib.wanted = lib.wanted or {}
lib.pos = lib.pos or { x = DEFAULT_X, y = DEFAULT_Y }
lib.counter = lib.counter or 0
lib.renders = lib.renders or 0
lib.frames = lib.frames or 0
lib.worstEncodeMs = lib.worstEncodeMs or 0
lib.lastGarbageKB = lib.lastGarbageKB or 0
if lib.locked == nil then lib.locked = true end

local frame = Mark.frame
local job
local resumeStart = 0
local sinceScaleCheck = 0

local function tick()
  if debugprofilestop() - resumeStart >= BUDGET_MS then coroutine.yield() end
end

local function render(plan, buffer)
  local bytes = assert(Frame.build(lib.session, lib.counter, plan))
  lib.counter = lib.counter + 1
  -- Qr10 shares one matrix across encodes; Mark.draw copies it into runs before its first yield.
  Mark.draw(buffer, Qr10.encode(bytes, tick), tick)
end

local function updateShown()
  local wanted = false
  for _, w in pairs(lib.wanted) do wanted = wanted or w end
  frame:SetShown(wanted and not lib.broken)
end

frame:SetScript("OnUpdate", function(_, elapsed)
  lib.renders = lib.renders + 1
  sinceScaleCheck = sinceScaleCheck + elapsed
  if sinceScaleCheck >= SCALE_CHECK_SECONDS then
    sinceScaleCheck = 0
    -- UI scale can change late after login (EllesmereUI); a stale layout puts modules off whole pixels.
    Mark.checkScale()
  end
  local now, s, renders = GetTime(), lib.scheduler, lib.renders
  s:Tick(now, renders)
  if not job and s:WantsFrame(now, renders) then
    s:Preempted()
    job = { co = coroutine.create(render), plan = s:Compose(now), buffer = Mark.back(), ms = 0, kb = collectgarbage("count") }
  end
  if not job then return end
  resumeStart = debugprofilestop()
  local ok, err = coroutine.resume(job.co, job.plan, job.buffer)
  job.ms = job.ms + debugprofilestop() - resumeStart
  if not ok then
    job = nil
    lib.broken = true
    updateShown()
    error(err, 0)
  end
  if coroutine.status(job.co) == "dead" then
    Mark.show(job.buffer)
    s:Shown(now, renders)
    lib.frames = lib.frames + 1
    lib.worstEncodeMs = math.max(lib.worstEncodeMs, job.ms)
    lib.lastGarbageKB = math.max(0, collectgarbage("count") - job.kb)
    job = nil
  end
end)

frame:RegisterEvent("UI_SCALE_CHANGED")
frame:RegisterEvent("DISPLAY_SIZE_CHANGED")
frame:RegisterEvent("MODIFIER_STATE_CHANGED")
frame:SetScript("OnEvent", function(_, event)
  if event == "MODIFIER_STATE_CHANGED" then
    Mark.updateMouse()
  else
    Mark.layout()
  end
end)

local function moveTo(x, y)
  lib.pos = { x = x, y = y, t = GetServerTime() }
  for _, db in ipairs(lib.dbs) do db.corvidPos = { x = x, y = y, t = lib.pos.t } end
  Mark.layout()
end
Mark.onMoved = moveTo

function lib:Register(channel, keyHex, db)
  local key = type(keyHex) == "string" and #keyHex == KEY_HEX_LENGTH and Bits.hexToBytes(keyHex)
  if not key then return nil, "bad key" end
  local ok, err = lib.scheduler:Register(channel, key)
  if not ok then return nil, err end
  lib.session = lib.session
    or Bits.u32le(GetServerTime()) .. Bits.u16le(math.random(0, 65535)) .. Bits.u16le(math.random(0, 65535))
  if db then
    lib.dbs[#lib.dbs + 1] = db
    local p = db.corvidPos
    if p and p.t and (not lib.pos.t or p.t > lib.pos.t) then lib.pos = { x = p.x, y = p.y, t = p.t } end
  end
  lib.wanted[channel] = true
  Mark.layout()
  Mark.updateMouse()
  updateShown()
  return true
end

function lib:SetStatus(channel, body)
  return lib.scheduler:SetStatus(channel, body)
end

function lib:Send(channel, class, type, body, replaceKey)
  local ok = lib.scheduler:Send(channel, class, type, body, replaceKey)
  if ok and class == "realtime" then job = nil end
  return ok
end

function lib:SetBackground(channel, list)
  return lib.scheduler:SetBackground(channel, list)
end

function lib:SetWanted(channel, wanted)
  if lib.wanted[channel] == nil then return end
  lib.wanted[channel] = not not wanted
  updateShown()
end

function lib:ReferencePoints()
  return Mark.referencePoints()
end

function lib:SetLocked(locked)
  lib.locked = not not locked
  Mark.updateMouse()
end

function lib:ResetPosition()
  moveTo(DEFAULT_X, DEFAULT_Y)
end

function lib:Stats()
  return { frames = lib.frames, worstEncodeMs = lib.worstEncodeMs, lastGarbageKB = lib.lastGarbageKB, dropped = lib.scheduler.dropped }
end

lib.loading = nil
