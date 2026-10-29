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
local BACKGROUND_SECONDS = 1
local MESSAGE_OVERHEAD = 3

function Scheduler.new()
  return setmetatable({
    channels = {},
    order = {},
    queues = { realtime = {}, normal = {} },
    statusVersion = 0,
    shownStatusVersion = -1,
    bgNext = 1,
  }, Scheduler)
end

function Scheduler:Register(channel, key)
  if self.channels[channel] then return nil, "channel taken" end
  self.channels[channel] = { key = key, status = "", background = {}, bgIdx = 1 }
  self.order[#self.order + 1] = channel
  table.sort(self.order)
  self.statusVersion = self.statusVersion + 1
  return true
end

function Scheduler:SetStatus(channel, body)
  self.channels[channel].status = body
  self.statusVersion = self.statusVersion + 1
end

function Scheduler:fitsEmpty(channel, size)
  local shape = {}
  for i, ch in ipairs(self.order) do
    shape[i] = { sizes = { #self.channels[ch].status, ch == channel and size or nil } }
  end
  return Frame.free(shape) >= 0
end

function Scheduler:Send(channel, class, type, body, replaceKey)
  if not self.channels[channel] or not self:fitsEmpty(channel, #body) then return false end
  if class == "realtime" and MESSAGE_OVERHEAD + #body > REALTIME_BYTES then return false end
  local message = { channel = channel, type = type, body = body, replaceKey = replaceKey }
  local queue = self.queues[class]
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
  return true
end

function Scheduler:SetBackground(channel, list)
  for _, m in ipairs(list) do
    if not self:fitsEmpty(channel, #m.body) then return false end
  end
  local ch = self.channels[channel]
  ch.background, ch.bgIdx = list, 1
  return true
end

local function release(messages)
  for _, m in ipairs(messages or {}) do m.inflight = nil end
end

function Scheduler:backgroundCursor()
  local idx = {}
  for _, channel in ipairs(self.order) do idx[channel] = self.channels[channel].bgIdx end
  local total = 0
  for _, channel in ipairs(self.order) do total = total + #self.channels[channel].background end
  return { next = self.bgNext, idx = idx, left = total }
end

function Scheduler:peekBackground(cursor)
  if cursor.left == 0 then return nil end
  local n = #self.order
  for k = 0, n - 1 do
    local pos = (cursor.next - 1 + k) % n + 1
    local channel = self.order[pos]
    local list = self.channels[channel].background
    if #list > 0 then return channel, list[(cursor.idx[channel] - 1) % #list + 1], pos end
  end
end

local function advance(cursor, channel, pos, n)
  cursor.idx[channel] = cursor.idx[channel] + 1
  cursor.next = pos % n + 1
  cursor.left = cursor.left - 1
end

function Scheduler:Compose(now)
  if self.composed then release(self.composed.messages) end
  local picked, shape, taken = {}, {}, {}
  for i, channel in ipairs(self.order) do
    picked[channel] = { realtime = {}, normal = {}, background = {} }
    shape[i] = { sizes = { #self.channels[channel].status } }
  end
  local left = Frame.free(shape)

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
      local channel, m, pos = self:peekBackground(cursor)
      if not m or MESSAGE_OVERHEAD + #m.body > left then return end
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
    plan[i] = { channel = channel, key = ch.key, messages = messages }
  end
  self.composed = { messages = taken, cursor = cursor, background = bgShown, statusVersion = self.statusVersion }
  return plan
end

function Scheduler:Shown(now, renders)
  local composed = self.composed
  if not composed then return end
  if self.shown and not self.shown.held then release(self.shown.messages) end
  self.composed = nil
  composed.at, composed.renders = now, renders
  self.shown = composed
  self.shownStatusVersion = composed.statusVersion
  if composed.background then self.lastBgAt = now end
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
  if not shown.held then return self:pending("realtime") end
  return self:pending("realtime") or self:pending("normal")
    or self.statusVersion ~= self.shownStatusVersion
    or now - shown.at >= IDLE_SECONDS
end

function Scheduler:Preempted()
  local shown = self.shown
  if not shown or shown.held then return end
  release(shown.messages)
  self.shown = nil
end
