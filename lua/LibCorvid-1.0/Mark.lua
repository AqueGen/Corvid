local lib = LibStub("LibCorvid-1.0")
if not lib.loading then return end

local Mark = {}
lib.Mark = Mark

local SIZE = 57
local QUIET = 4
local PIXELS = 2
local SIDE = SIZE + 2 * QUIET
local NEAR, FAR = 7.5, 57.5

local frame = CorvidMark
if not frame then
  frame = CreateFrame("Frame", "CorvidMark", UIParent)
  frame:Hide()
end
Mark.frame = frame
frame:SetFrameStrata("TOOLTIP")
frame:SetFrameLevel(10000)
frame:SetMovable(true)
frame:SetDontSavePosition(true)
frame:SetClampedToScreen(true)
frame:RegisterForDrag("LeftButton")

local function newBuffer()
  local b = CreateFrame("Frame", nil, frame)
  b:SetAllPoints()
  local bg = b:CreateTexture(nil, "BACKGROUND")
  bg:SetAllPoints()
  bg:SetColorTexture(1, 1, 1, 1)
  b.textures, b.runs, b.count = {}, {}, 0
  b:Hide()
  return b
end

frame.buffers = frame.buffers or { newBuffer(), newBuffer() }
frame.front = frame.front or 1

local function pixel()
  return PixelUtil.GetPixelToUIUnitFactor() / frame:GetEffectiveScale()
end

local function snap(v)
  local px = pixel()
  return math.floor(v / px + 0.5) * px
end

local function placeRun(b, i, m)
  local t = b.textures[i]
  if not t then
    t = b:CreateTexture(nil, "ARTWORK")
    t:SetColorTexture(0, 0, 0, 1)
    b.textures[i] = t
  end
  local runs = b.runs
  t:ClearAllPoints()
  t:SetPoint("TOPLEFT", b, "TOPLEFT", (QUIET + runs[3 * i - 2] - 1) * m, -(QUIET + runs[3 * i - 1] - 1) * m)
  t:SetSize(runs[3 * i] * m, m)
  t:Show()
end

local function placeAll(b)
  local m = PIXELS * pixel()
  for i = 1, b.count do placeRun(b, i, m) end
  b.m = m
end

function Mark.layout()
  frame.layoutPixel = pixel()
  local side = SIDE * PIXELS * frame.layoutPixel
  frame:SetSize(side, side)
  frame:ClearAllPoints()
  frame:SetPoint("TOPLEFT", UIParent, "TOPLEFT", snap(lib.pos.x), snap(lib.pos.y))
  placeAll(frame.buffers[frame.front])
end

function Mark.checkScale()
  if pixel() ~= frame.layoutPixel then Mark.layout() end
end

function Mark.back()
  return frame.buffers[3 - frame.front]
end

function Mark.draw(b, matrix, tick)
  local runs, n = b.runs, 0
  for r = 1, SIZE do
    local row, c = matrix[r], 1
    while c <= SIZE do
      if row[c] then
        local start = c
        while c <= SIZE and row[c] do c = c + 1 end
        n = n + 1
        runs[3 * n - 2], runs[3 * n - 1], runs[3 * n] = start, r, c - start
      else
        c = c + 1
      end
    end
  end
  b.count = n
  for i = n + 1, #b.textures do b.textures[i]:Hide() end
  local m = PIXELS * pixel()
  b.m = m
  for i = 1, n do
    placeRun(b, i, m)
    tick()
  end
end

function Mark.show(b)
  if b.m ~= PIXELS * pixel() then placeAll(b) end
  b:Show()
  frame.buffers[frame.front]:Hide()
  frame.front = b == frame.buffers[1] and 1 or 2
end

function Mark.referencePoints()
  local m = PIXELS * pixel()
  local left, top = frame:GetLeft(), frame:GetTop()
  if not left or not top then return nil end
  return left + NEAR * m, top - NEAR * m, left + FAR * m, top - NEAR * m, left + NEAR * m, top - FAR * m
end

local function stopMoving()
  if not frame.moving then return end
  frame.moving = nil
  frame:StopMovingOrSizing()
  Mark.onMoved(frame:GetLeft() - UIParent:GetLeft(), frame:GetTop() - UIParent:GetTop())
end

function Mark.updateMouse()
  local free = not lib.locked or IsAltKeyDown()
  frame:EnableMouse(free)
  if not free then stopMoving() end
end

frame:SetScript("OnDragStart", function()
  if not lib.locked or IsAltKeyDown() then
    frame.moving = true
    frame:StartMoving()
  end
end)

frame:SetScript("OnDragStop", stopMoving)
