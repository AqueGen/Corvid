local _, ns = ...

local CHANNEL = 9
local RFC_KEY = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"
local RFC_NONCE = "000000000000004a00000000"
local RFC_PLAIN = "Ladies and Gentl"
local RFC_CIPHER = "6e2e359a2568f98041ba0728dd0d6981"

-- CRC-32 and ChaCha20 handle values >= 2^31; this proves the game's bit library agrees with the specs
local function selfCheck(lib)
  local failed = {}
  if lib.Crc32.compute("123456789") ~= 0xCBF43926 then failed[#failed + 1] = "crc32" end
  if lib.ChaCha20.keyId(string.rep("\0", 32)) ~= 0x76 then failed[#failed + 1] = "keyId" end
  local Bits = lib.Bits
  local cipher = lib.ChaCha20.xor(Bits.hexToBytes(RFC_KEY), Bits.hexToBytes(RFC_NONCE), 1, RFC_PLAIN)
  if Bits.bytesToHex(cipher) ~= RFC_CIPHER then failed[#failed + 1] = "chacha20" end
  if #failed == 0 then
    print("CorvidProbe self-check: ok")
  else
    print("CorvidProbe self-check: FAILED: " .. table.concat(failed, ", "))
  end
end

local frame = CreateFrame("Frame")
frame:RegisterEvent("PLAYER_LOGIN")
frame:SetScript("OnEvent", function()
  CorvidProbeDB = CorvidProbeDB or {}
  local lib = LibStub("LibCorvid-1.0")
  selfCheck(lib)
  lib:Register(CHANNEL, ns.corvidKey, CorvidProbeDB)
  lib:SetStatus(CHANNEL, "probe")
  lib:Send(CHANNEL, "normal", 1, "hello corvid")
end)
