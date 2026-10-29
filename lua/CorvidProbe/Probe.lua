local _, ns = ...

local CHANNEL = 9

local frame = CreateFrame("Frame")
frame:RegisterEvent("PLAYER_LOGIN")
frame:SetScript("OnEvent", function()
  CorvidProbeDB = CorvidProbeDB or {}
  local lib = LibStub("LibCorvid-1.0")
  lib:Register(CHANNEL, ns.corvidKey, CorvidProbeDB)
  lib:SetStatus(CHANNEL, "probe")
  lib:Send(CHANNEL, "normal", 1, "hello corvid")
end)
