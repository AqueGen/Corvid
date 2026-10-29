require("spec.helper")

local Scheduler = LibStub("LibCorvid-1.0").Scheduler

local WCT, PS = 1, 2
local CAST, CHAT, PAGE = 2, 3, 4
local STEP = 1 / 60

local function new()
  local s = Scheduler.new()
  assert(s:Register(WCT, "wct-key"))
  assert(s:Register(PS, "ps-key"))
  return s
end

local function segment(plan, channel)
  for _, e in ipairs(plan) do
    if e.channel == channel then return e end
  end
end

local function types(plan, channel)
  local t = {}
  for i, m in ipairs(segment(plan, channel).messages) do t[i] = m.type end
  return t
end

local function count(plan, type)
  local n, bytes = 0, 0
  for _, e in ipairs(plan) do
    for _, m in ipairs(e.messages) do
      if m.type == type then n, bytes = n + 1, bytes + 3 + #m.body end
    end
  end
  return n, bytes
end

local function run(s, seconds, each)
  local plans = {}
  local renders = 0
  while renders * STEP < seconds do
    local now = renders * STEP
    each(now, renders)
    s:Tick(now, renders)
    if s:WantsFrame(now, renders) then
      s:Preempted()
      local plan = s:Compose(now)
      s:Shown(now, renders)
      plans[#plans + 1] = { at = now, plan = plan }
    end
    renders = renders + 1
  end
  return plans
end

describe("Scheduler", function()
  it("refuses a channel registered twice", function()
    local s = new()
    local ok, err = s:Register(WCT, "other")
    assert.is_nil(ok)
    assert.equals("channel taken", err)
  end)

  it("starts every segment with the channel status", function()
    local s = new()
    s:SetStatus(WCT, "open")
    local plan = s:Compose(0)
    assert.equals(2, #plan)
    assert.same({ channel = WCT, key = "wct-key", messages = { { type = 0, body = "open" } } }, plan[1])
    assert.same({ channel = PS, key = "ps-key", messages = { { type = 0, body = "" } } }, plan[2])
  end)

  it("orders realtime before normal before background", function()
    local s = new()
    s:SetBackground(PS, { { type = PAGE, body = "page" } })
    s:Send(PS, "normal", CHAT, "line")
    s:Send(PS, "realtime", CAST, "cast")
    assert.same({ 0, CAST, CHAT, PAGE }, types(s:Compose(0), PS))
  end)

  it("caps realtime bytes and keeps chat in every plan under a cast flood", function()
    local s = new()
    for i = 1, 10 do s:Send(WCT, "normal", CHAT, string.rep("c", 20)) end
    local plans = run(s, 2, function(_, renders)
      s:Send(PS, "realtime", CAST, string.rep("x", 40))
      if renders % 30 == 0 then s:Send(WCT, "normal", CHAT, string.rep("c", 20)) end
    end)
    assert.is_true(#plans > 20)
    for _, p in ipairs(plans) do
      local casts, bytes = count(p.plan, CAST)
      assert.is_true(casts > 0)
      assert.is_true(bytes <= 134)
      assert.is_true(count(p.plan, CHAT) > 0)
    end
  end)

  it("puts background on screen at least once per second while chat fills every frame", function()
    local s = new()
    s:SetBackground(PS, { { type = PAGE, body = string.rep("a", 30) }, { type = PAGE, body = string.rep("b", 30) } })
    local plans = run(s, 10, function()
      s:Send(WCT, "normal", CHAT, string.rep("c", 73))
    end)
    local last, pages = 0, 0
    for _, p in ipairs(plans) do
      assert.is_true(count(p.plan, CHAT) > 0)
      if count(p.plan, PAGE) > 0 then
        assert.is_true(p.at - last <= 1 + 4 * STEP)
        last, pages = p.at, pages + 1
      end
    end
    assert.is_true(pages >= 9)
    assert.is_true(pages < #plans / 2)
  end)

  it("serves background round-robin once each frame is held", function()
    local s = new()
    s:SetBackground(PS, { { type = PAGE, body = string.rep("a", 120) }, { type = PAGE, body = string.rep("b", 120) } })
    local first = segment(s:Compose(0), PS).messages[2].body
    s:Shown(0, 0)
    s:Tick(0.1, 5)
    local second = segment(s:Compose(0.1), PS).messages[2].body
    assert.are_not.equal(first, second)
  end)

  it("carries a preempted frame's chat into the next plan", function()
    local s = new()
    s:Send(WCT, "normal", CHAT, "hello")
    s:Compose(0)
    s:Shown(0, 0)
    assert.is_false(s:WantsFrame(STEP, 1))
    s:Send(PS, "realtime", CAST, "kick")
    assert.is_true(s:WantsFrame(STEP, 1))
    s:Preempted()
    local plan = s:Compose(STEP)
    assert.same({ 0, CHAT }, types(plan, WCT))
    assert.same({ 0, CAST }, types(plan, PS))
  end)

  it("does not resend a held frame's messages", function()
    local s = new()
    s:Send(WCT, "normal", CHAT, "hello")
    s:Compose(0)
    s:Shown(0, 0)
    s:Tick(0.05, 5)
    assert.is_false(s:WantsFrame(0.05, 5))
    s:Tick(0.06, 5)
    s:Preempted()
    assert.same({ 0 }, types(s:Compose(0.06), WCT))
  end)

  it("refuses a message too big for an empty frame and keeps later plans intact", function()
    local s = new()
    assert.is_false(s:Send(WCT, "normal", CHAT, string.rep("x", 300)))
    assert.same({ 0 }, types(s:Compose(0), WCT))
    assert.is_true(s:Send(WCT, "normal", CHAT, "ok"))
    assert.same({ 0, CHAT }, types(s:Compose(0), WCT))
  end)

  it("refuses a realtime message over the realtime share", function()
    local s = new()
    assert.is_false(s:Send(PS, "realtime", CAST, string.rep("x", 132)))
    assert.is_true(s:Send(PS, "realtime", CAST, string.rep("x", 131)))
  end)

  it("keeps only the newest message for a replace key", function()
    local s = new()
    s:Send(PS, "realtime", CAST, "old", "state")
    s:Send(PS, "realtime", CAST, "other")
    s:Send(PS, "realtime", CAST, "new", "state")
    s:Send(WCT, "realtime", CAST, "own", "state")
    local plan = s:Compose(0)
    assert.same({ { type = 0, body = "" }, { type = CAST, body = "new" }, { type = CAST, body = "other" } }, segment(plan, PS).messages)
    assert.same({ 0, CAST }, types(plan, WCT))
  end)

  it("wants a status-only frame once a second when idle", function()
    local s = new()
    assert.is_true(s:WantsFrame(0, 0))
    s:Compose(0)
    s:Shown(0, 0)
    assert.is_false(s:WantsFrame(0.5, 30))
    assert.is_true(s:WantsFrame(1, 60))
    local plan = s:Compose(1)
    assert.same({ 0 }, types(plan, WCT))
    assert.same({ 0 }, types(plan, PS))
  end)

  it("wants a frame when a status changes", function()
    local s = new()
    s:Compose(0)
    s:Shown(0, 0)
    assert.is_false(s:WantsFrame(0.1, 6))
    s:SetStatus(PS, "\1")
    assert.is_true(s:WantsFrame(0.1, 6))
  end)
end)
