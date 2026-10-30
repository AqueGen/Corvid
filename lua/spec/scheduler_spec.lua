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
    local last = plans[#plans]
    if last and not last.heldAt and renders - last.renders >= 3 and now - last.at >= 0.06 then
      last.heldAt = now
    end
    if s:WantsFrame(now, renders) then
      s:Preempted()
      local plan = s:Compose(now)
      s:Shown(now, renders)
      plans[#plans + 1] = { at = now, renders = renders, plan = plan }
    end
    renders = renders + 1
  end
  return plans
end

local function backgroundGap(plans, seconds)
  local last, gap = 0, 0
  for _, p in ipairs(plans) do
    if p.heldAt and count(p.plan, PAGE) > 0 then
      gap = math.max(gap, p.heldAt - last)
      last = p.heldAt
    end
  end
  return math.max(gap, seconds - last)
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
    local plans = run(s, 2, function()
      s:Send(PS, "realtime", CAST, string.rep("x", 40))
      s:Send(WCT, "normal", CHAT, string.rep("c", 20))
    end)
    assert.is_true(#plans > 20)
    for _, p in ipairs(plans) do
      local casts, bytes = count(p.plan, CAST)
      assert.is_true(casts > 0)
      assert.is_true(bytes <= 134)
      assert.is_true(count(p.plan, CHAT) > 0)
    end
  end)

  it("keeps delivering realtime messages under a flood of one cast per render", function()
    local s = new()
    local plans = run(s, 2, function(_, renders)
      s:Send(PS, "realtime", CAST, string.format("%040d", renders))
      s:Send(WCT, "normal", CHAT, string.rep("c", 20))
    end)
    local delivered = {}
    for _, p in ipairs(plans) do
      assert.is_true(count(p.plan, CHAT) > 0)
      for _, m in ipairs(segment(p.plan, PS).messages) do
        assert.is_nil(delivered[m.body], "delivered cast resent")
      end
      if p.heldAt then
        for _, m in ipairs(segment(p.plan, PS).messages) do
          if m.type == CAST then delivered[m.body] = true end
        end
      end
    end
    for window = 0, 1.8, 0.1 do
      local n = 0
      for _, p in ipairs(plans) do
        if p.heldAt and p.heldAt >= window and p.heldAt < window + 0.1 and count(p.plan, CAST) > 0 then n = n + 1 end
      end
      assert.is_true(n > 0, "no delivery in window " .. window)
    end
  end)

  it("does not preempt a frame composed after a preemption", function()
    local s = new()
    s:Compose(0)
    s:Shown(0, 0)
    s:Send(PS, "realtime", CAST, "a")
    assert.is_true(s:WantsFrame(STEP, 1))
    s:Preempted()
    s:Compose(STEP)
    s:Shown(STEP, 1)
    s:Send(PS, "realtime", CAST, "b")
    assert.is_false(s:WantsFrame(2 * STEP, 2))
    s:Preempted()
    s:Tick(2 * STEP, 2)
    assert.is_true(s:WantsFrame(5 * STEP, 5))
    assert.same({ 0, CAST }, types(s:Compose(5 * STEP), PS))
  end)

  it("drops queued messages and background items that no longer fit an empty frame", function()
    local s = Scheduler.new()
    s:Register(WCT, "wct-key")
    assert.is_true(s:Send(WCT, "normal", CHAT, string.rep("x", 230)))
    assert.is_true(s:SetBackground(WCT, { { type = PAGE, body = string.rep("p", 100) }, { type = PAGE, body = "small" } }))
    s:Register(PS, "ps-key")
    s:Send(WCT, "normal", CHAT, "next")
    local plan = s:Compose(0)
    assert.same({
      { type = 0, body = "" }, { type = CHAT, body = "next" },
      { type = PAGE, body = string.rep("p", 100) }, { type = PAGE, body = "small" },
    }, segment(plan, WCT).messages)
    assert.equals(1, s.dropped)
  end)

  it("drops a background item once it no longer fits an otherwise-empty frame", function()
    local s = Scheduler.new()
    s:Register(WCT, "wct-key")
    assert.is_true(s:SetBackground(WCT, { { type = PAGE, body = string.rep("p", 230) }, { type = PAGE, body = "small" } }))
    s:Register(PS, "ps-key")
    assert.same({ 0, PAGE }, types(s:Compose(0), WCT))
    assert.equals(1, s.dropped)
  end)

  it("accepts a background item that fits an otherwise-empty frame", function()
    local s = new()
    assert.is_false(s:SetBackground(PS, { { type = PAGE, body = string.rep("p", 227) } }))
    assert.is_true(s:SetBackground(PS, { { type = PAGE, body = string.rep("p", 226) } }))
  end)

  it("keeps a page too big for a full realtime share and shows it when realtime is quiet", function()
    local s = Scheduler.new()
    s:Register(WCT, "wct-key")
    assert.is_true(s:SetBackground(WCT, { { type = PAGE, body = string.rep("p", 100) } }))
    s:Register(PS, "ps-key")
    assert.is_true(s:SetStatus(PS, string.rep("s", 32)))
    assert.is_true(s:Send(PS, "realtime", CAST, string.rep("x", 131)))
    assert.same({ 0 }, types(s:Compose(0), WCT))
    s:Shown(0, 0)
    s:Tick(0.1, 5)
    assert.equals(0, s.dropped)
    assert.same({ 0, PAGE }, types(s:Compose(0.1), WCT))
  end)

  it("does not let a waiting big page hold back another channel's small pages", function()
    local BIG = 6
    local s = new()
    assert.is_true(s:SetStatus(PS, string.rep("s", 32)))
    assert.is_true(s:SetBackground(WCT, { { type = BIG, body = string.rep("p", 100) } }))
    assert.is_true(s:SetBackground(PS, { { type = PAGE, body = string.rep("a", 30) }, { type = PAGE, body = string.rep("b", 30) } }))
    local plans = run(s, 10, function(_, renders)
      s:Send(PS, "realtime", CAST, string.format("%040d", renders))
      s:Send(WCT, "normal", CHAT, string.rep("c", 20))
    end)
    assert.is_true(backgroundGap(plans, 10) <= 1)
    for _, p in ipairs(plans) do
      local seen = {}
      for _, m in ipairs(segment(p.plan, PS).messages) do
        if m.type == PAGE then
          assert.is_nil(seen[m.body], "page twice in one frame")
          seen[m.body] = true
        end
      end
    end
  end)

  it("refuses a bad message type or body", function()
    local s = new()
    for _, t in ipairs({ 0, 256, -1, 1.5, "3", 0 / 0, math.huge }) do
      assert.is_false(s:Send(WCT, "normal", t, "x"), tostring(t))
    end
    assert.is_false(s:Send(WCT, "normal", nil, "x"))
    assert.is_false(s:Send(WCT, "normal", CHAT, 5))
    assert.is_false(s:Send(WCT, "realtime", CHAT, nil))
    assert.is_true(s:Send(WCT, "normal", 1, "x"))
    assert.is_true(s:Send(WCT, "normal", 255, ""))
  end)

  it("refuses a status that is not a string", function()
    local s = new()
    assert.is_false(s:SetStatus(WCT, 5))
    assert.is_false(s:SetStatus(WCT, nil))
    assert.equals("", segment(s:Compose(0), WCT).messages[1].body)
  end)

  it("refuses a background list with a bad item and keeps the old list", function()
    local s = new()
    assert.is_true(s:SetBackground(WCT, { { type = PAGE, body = "ok" } }))
    assert.is_false(s:SetBackground(WCT, "junk"))
    assert.is_false(s:SetBackground(WCT, { "junk" }))
    assert.is_false(s:SetBackground(WCT, { { type = 0, body = "x" } }))
    assert.is_false(s:SetBackground(WCT, { { type = 256, body = "x" } }))
    assert.is_false(s:SetBackground(WCT, { { type = PAGE, body = 7 } }))
    assert.same({ 0, PAGE }, types(s:Compose(0), WCT))
  end)

  it("copies the background list so later edits by the caller change nothing", function()
    local s = new()
    local list = { { type = PAGE, body = "ok" } }
    assert.is_true(s:SetBackground(WCT, list))
    list[1].type = 0
    list[2] = { type = PAGE, body = 9 }
    assert.same({ { type = 0, body = "" }, { type = PAGE, body = "ok" } }, segment(s:Compose(0), WCT).messages)
  end)

  it("keeps at most 64 pending messages per channel and class and drops the oldest", function()
    local s = new()
    for i = 1, 66 do assert.is_true(s:Send(WCT, "normal", CHAT, tostring(i))) end
    for i = 1, 64 do assert.is_true(s:Send(PS, "normal", CHAT, "p" .. i)) end
    assert.equals(2, s.dropped)
    assert.equals("3", segment(s:Compose(0), WCT).messages[2].body)
    for i = 1, 65 do assert.is_true(s:Send(PS, "realtime", CAST, "r" .. i)) end
    assert.equals(3, s.dropped)
    local s2 = new()
    for i = 1, 65 do s2:Send(PS, "realtime", CAST, "r" .. i) end
    assert.equals("r2", segment(s2:Compose(0), PS).messages[2].body)
  end)

  it("carries the cached key id in the plan", function()
    local s = Scheduler.new()
    assert.is_true(s:Register(WCT, "wct-key", 42))
    assert.equals(42, s:Compose(0)[1].keyId)
  end)

  it("delivers background at least once in every second under a cast and chat flood", function()
    local s = new()
    s:SetBackground(PS, { { type = PAGE, body = string.rep("a", 30) }, { type = PAGE, body = string.rep("b", 30) } })
    local plans = run(s, 10, function(_, renders)
      s:Send(PS, "realtime", CAST, string.format("%040d", renders))
      s:Send(WCT, "normal", CHAT, string.rep("c", 20))
    end)
    assert.is_true(backgroundGap(plans, 10) <= 1)
  end)

  it("keeps order when Compose runs over an unheld frame without Preempted", function()
    local s = new()
    s:Send(WCT, "normal", CHAT, "a")
    s:Compose(0)
    s:Shown(0, 0)
    s:Send(WCT, "normal", CHAT, "b")
    local plan = s:Compose(STEP)
    s:Shown(STEP, 1)
    assert.same({ { type = 0, body = "" }, { type = CHAT, body = "a" }, { type = CHAT, body = "b" } }, segment(plan, WCT).messages)
  end)

  it("returns false for unregistered channels and unknown classes", function()
    local s = new()
    assert.is_false(s:SetStatus(9, "x"))
    assert.is_false(s:SetBackground(9, {}))
    assert.is_false(s:Send(9, "normal", CHAT, "x"))
    assert.is_false(s:Send(WCT, "urgent", CHAT, "x"))
  end)

  it("refuses a status longer than 32 bytes and keeps the old one", function()
    local s = new()
    assert.is_true(s:SetStatus(WCT, string.rep("s", 32)))
    assert.is_false(s:SetStatus(WCT, string.rep("t", 33)))
    assert.equals(string.rep("s", 32), segment(s:Compose(0), WCT).messages[1].body)
  end)

  it("shows background at least once per second while chat fills every frame", function()
    local s = new()
    s:SetBackground(PS, { { type = PAGE, body = string.rep("a", 30) }, { type = PAGE, body = string.rep("b", 30) } })
    local plans = run(s, 10, function()
      s:Send(WCT, "normal", CHAT, string.rep("c", 73))
    end)
    local pages = 0
    for _, p in ipairs(plans) do
      assert.is_true(count(p.plan, CHAT) > 0)
      if count(p.plan, PAGE) > 0 then pages = pages + 1 end
    end
    assert.is_true(backgroundGap(plans, 10) <= 1)
    assert.is_true(pages < #plans / 2)
  end)

  it("serves background round-robin once each frame is held", function()
    local s = new()
    s:SetBackground(PS, { { type = PAGE, body = string.rep("a", 90) }, { type = PAGE, body = string.rep("b", 90) } })
    s:Send(WCT, "normal", CHAT, string.rep("c", 120))
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
