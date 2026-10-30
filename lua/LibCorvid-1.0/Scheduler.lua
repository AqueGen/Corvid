local lib = LibStub("LibCorvid-1.0")
if not lib.loading then return end

local Frame = lib.Frame

local Scheduler = {}
Scheduler.__index = Scheduler
lib.Scheduler = Scheduler

local HOLD_RENDERS = 3
local HOLD_SECONDS = 0.06
local REALTIME_BYTES = 134
local IDLE_SECONDS = 1
-- half of the 1 s guarantee, so compose wait + a cut frame + hold still land inside it
local BACKGROUND_SECONDS = 0.5
local MESSAGE_OVERHEAD = 3
local STATUS_BYTES = 32
local PENDING_CAP = 64

function Scheduler.new()
  return setmetatable({
    channels = {},
    order = {},
    queues = { realtime = {}, normal = {} },
    statusVersion = 0,
    shownStatusVersion = -1,
    bgNext = 1,
    dropped = 0,
  }, Scheduler)
end

function Scheduler:Register(channel, key, keyId)
  if self.channels[channel] then return nil, "channel taken" end
  self.channels[channel] = { key = key, keyId = keyId, status = "", background = {}, bgIdx = 1 }
  self.order[#self.order + 1] = channel
  table.sort(self.order)
  self.statusVersion = self.statusVersion + 1
  return true
end

function Scheduler:SetStatus(channel, body)
  if not self.channels[channel] or type(body) ~= "string" or #body > STATUS_BYTES then return false end
  self.channels[channel].status = body
  self.statusVersion = self.statusVersion + 1
  return true
end

function Scheduler:statusFree()
  local shape = {}
  for i, ch in ipairs(self.order) do shape[i] = { sizes = { #self.channels[ch].status } } end
  return Frame.free(shape)
end

local function fits(m, free)
  return MESSAGE_OVERHEAD + #m.body <= free
end

local function valid(msgType, body)
  return type(msgType) == "number" and msgType % 1 == 0 and msgType >= 1 and msgType <= 255 and type(body) == "string"
end

function Scheduler:Send(channel, class, msgType, body, replaceKey)
  local queue = self.queues[class]
  if not queue or not self.channels[channel] or not valid(msgType, body) then return false end
  local message = { channel = channel, type = msgType, body = body, replaceKey = replaceKey }
  if not fits(message, self:statusFree()) then return false end
  if class == "realtime" and not fits(message, REALTIME_BYTES) then return false end
  if replaceKey then
    for _, q in pairs(self.queues) do
      for i, m in ipairs(q) do
        if m.channel == channel and m.replaceKey == replaceKey then
          if q == queue then
            queue[i] = message
            return true
          end
          table.remove(q, i)
          break
        end
      end
    end
  end
  queue[#queue + 1] = message
  local oldest, n = nil, 0
  for i, m in ipairs(queue) do
    if m.channel == channel and not m.inflight then oldest, n = oldest or i, n + 1 end
  end
  if n > PENDING_CAP then
    table.remove(queue, oldest)
    self.dropped = self.dropped + 1
  end
  return true
end

function Scheduler:SetBackground(channel, list)
  local ch = self.channels[channel]
  if not ch or type(list) ~= "table" then return false end
  local free, copy = self:statusFree(), {}
  for i, m in ipairs(list) do
    if type(m) ~= "table" or not valid(m.type, m.body) or not fits(m, free) then return false end
    copy[i] = { type = m.type, body = m.body }
  end
  ch.background, ch.bgIdx = copy, 1
  return true
end

local function release(messages)
  for _, m in ipairs(messages or {}) do m.inflight = nil end
end

function Scheduler:backgroundCursor()
  local idx, used = {}, {}
  for _, channel in ipairs(self.order) do idx[channel], used[channel] = self.channels[channel].bgIdx, 0 end
  return { next = self.bgNext, idx = idx, used = used }
end

-- an item without room waits at its channel's head; other channels' items still go out
function Scheduler:peekBackground(cursor, room)
  local n = #self.order
  for k = 0, n - 1 do
    local pos = (cursor.next - 1 + k) % n + 1
    local channel = self.order[pos]
    local list = self.channels[channel].background
    if #list > cursor.used[channel] then
      local m = list[(cursor.idx[channel] - 1) % #list + 1]
      if fits(m, room) then return channel, m, pos end
    end
  end
end

local function advance(cursor, channel, pos, n)
  cursor.idx[channel] = cursor.idx[channel] + 1
  cursor.used[channel] = cursor.used[channel] + 1
  cursor.next = pos % n + 1
end

function Scheduler:dropUnfit(free)
  for class, q in pairs(self.queues) do
    local kept = {}
    for _, m in ipairs(q) do
      if m.inflight or fits(m, free) then kept[#kept + 1] = m else self.dropped = self.dropped + 1 end
    end
    self.queues[class] = kept
  end
  for _, ch in pairs(self.channels) do
    local kept = {}
    for _, m in ipairs(ch.background) do
      if fits(m, free) then kept[#kept + 1] = m else self.dropped = self.dropped + 1 end
    end
    if #kept < #ch.background then ch.background = kept end
  end
end

function Scheduler:Compose(now)
  if self.composed then release(self.composed.messages) end
  if self.shown and not self.shown.held then self:cut() end
  local picked, taken = {}, {}
  for _, channel in ipairs(self.order) do
    picked[channel] = { realtime = {}, normal = {}, background = {} }
  end
  local left = self:statusFree()
  self:dropUnfit(left)

  local function fill(class, budget)
    for _, m in ipairs(self.queues[class]) do
      if not m.inflight then
        local size = MESSAGE_OVERHEAD + #m.body
        if size > left or size > budget then return end
        left, budget = left - size, budget - size
        m.inflight = true
        taken[#taken + 1] = m
        local list = picked[m.channel][class]
        list[#list + 1] = m
      end
    end
  end

  local cursor = self:backgroundCursor()
  local bgShown = false
  local function background(limit)
    while limit > 0 do
      local channel, m, pos = self:peekBackground(cursor, left)
      if not m then return end
      left = left - MESSAGE_OVERHEAD - #m.body
      local list = picked[channel].background
      list[#list + 1] = m
      advance(cursor, channel, pos, #self.order)
      bgShown = true
      limit = limit - 1
    end
  end

  fill("realtime", REALTIME_BYTES)
  if not self.lastBgAt or now - self.lastBgAt >= BACKGROUND_SECONDS then background(1) end
  fill("normal", math.huge)
  background(math.huge)

  local plan = {}
  for i, channel in ipairs(self.order) do
    local ch, p = self.channels[channel], picked[channel]
    local messages = { { type = 0, body = ch.status } }
    for _, class in ipairs({ "realtime", "normal", "background" }) do
      for _, m in ipairs(p[class]) do messages[#messages + 1] = { type = m.type, body = m.body } end
    end
    plan[i] = { channel = channel, key = ch.key, keyId = ch.keyId, messages = messages }
  end
  self.composed = {
    messages = taken, cursor = cursor, background = bgShown, statusVersion = self.statusVersion, protected = self.wasCut,
  }
  return plan
end

function Scheduler:Shown(now, renders)
  local composed = self.composed
  if not composed then return end
  self.composed, self.wasCut = nil, nil
  composed.at, composed.renders = now, renders
  self.shown = composed
  self.shownStatusVersion = composed.statusVersion
end

function Scheduler:Tick(now, renders)
  local shown = self.shown
  if not shown or shown.held then return end
  if renders - shown.renders < HOLD_RENDERS or now - shown.at < HOLD_SECONDS then return end
  shown.held = true
  for _, m in ipairs(shown.messages) do m.done = true end
  for class, q in pairs(self.queues) do
    local kept = {}
    for _, m in ipairs(q) do
      if not m.done then kept[#kept + 1] = m end
    end
    self.queues[class] = kept
  end
  self.bgNext = shown.cursor.next
  for channel, idx in pairs(shown.cursor.idx) do self.channels[channel].bgIdx = idx end
  if shown.background then self.lastBgAt = now end
end

function Scheduler:pending(class)
  for _, m in ipairs(self.queues[class]) do
    if not m.inflight then return true end
  end
  return false
end

function Scheduler:WantsFrame(now, renders)
  local shown = self.shown
  if not shown then return true end
  self:Tick(now, renders)
  if not shown.held then return not shown.protected and self:pending("realtime") end
  return self:pending("realtime") or self:pending("normal")
    or self.statusVersion ~= self.shownStatusVersion
    or now - shown.at >= IDLE_SECONDS
end

function Scheduler:cut()
  release(self.shown.messages)
  self.shown, self.wasCut = nil, true
end

function Scheduler:Preempted()
  local shown = self.shown
  if not shown or shown.held or shown.protected then return end
  self:cut()
end
