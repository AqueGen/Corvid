local stub = { time = 0, ms = 0, msStep = 0, serverTime = 1700000000, random = 0x1234, created = {} }

local Region = {}
Region.__index = Region

local function region(kind, parent)
  return setmetatable({ kind = kind, parent = parent, shown = true, scale = 1, w = 0, h = 0, scripts = {}, events = {} }, Region)
end

function Region:Show() self.shown = true end
function Region:Hide() self.shown = false end
function Region:IsShown() return self.shown end
function Region:SetShown(shown) self.shown = not not shown end
function Region:SetSize(w, h) self.w, self.h = w, h end
function Region:GetWidth() return self.w end
function Region:GetHeight() return self.h end
function Region:ClearAllPoints() self.point = nil end
function Region:SetPoint(point, rel, relPoint, x, y) self.point = { point, rel, relPoint, x or 0, y or 0 } end
function Region:SetAllPoints() self.point = { "TOPLEFT", self.parent, "TOPLEFT", 0, 0 } end
function Region:GetPoint() return unpack(self.point) end
function Region:GetLeft() return self.point[2]:GetLeft() + self.point[4] end
function Region:GetTop() return self.point[2]:GetTop() + self.point[5] end
function Region:SetScale(scale) self.scale = scale end
function Region:GetScale() return self.scale end
function Region:GetEffectiveScale() return self.scale * (self.parent and self.parent:GetEffectiveScale() or 1) end
function Region:SetColorTexture(...) self.color = { ... } end
function Region:SetFrameStrata(strata) self.strata = strata end
function Region:GetFrameStrata() return self.strata end
function Region:SetFrameLevel(level) self.level = level end
function Region:GetFrameLevel() return self.level end
function Region:SetMovable(movable) self.movable = movable end
function Region:SetClampedToScreen(clamped) self.clamped = clamped end
function Region:RegisterForDrag(button) self.dragButton = button end
function Region:EnableMouse(enabled) self.mouse = enabled end
function Region:StartMoving() self.moving = true end
function Region:StopMovingOrSizing() self.moving = false end
function Region:SetScript(name, fn) self.scripts[name] = fn end
function Region:GetScript(name) return self.scripts[name] end
function Region:RegisterEvent(event) self.events[event] = true end
function Region:CreateTexture(_, layer)
  local t = region("Texture", self)
  t.layer = layer
  self.textures = self.textures or {}
  self.textures[#self.textures + 1] = t
  return t
end

function CreateFrame(kind, name, parent)
  local f = region(kind, parent)
  f.name = name
  stub.created[#stub.created + 1] = f
  if name then _G[name] = f end
  return f
end

UIParent = region("Frame")
function UIParent:GetLeft() return 0 end
function UIParent:GetTop() return 768 / self.scale end

PixelUtil = { GetPixelToUIUnitFactor = function() return 768 / 1080 end }

function GetTime() return stub.time end
function GetServerTime() return stub.serverTime end
function IsAltKeyDown() return stub.alt end
function debugprofilestop()
  stub.ms = stub.ms + stub.msStep
  return stub.ms
end

local random = math.random
math.random = function(a, b)
  if a == 0 and b == 65535 then return stub.random end
  return random(a, b)
end

function stub.reset()
  stub.time, stub.ms, stub.msStep, stub.alt, stub.created = 0, 0, 0, nil, {}
  UIParent.scale = 1
  _G.CorvidMark = nil
end

return stub
