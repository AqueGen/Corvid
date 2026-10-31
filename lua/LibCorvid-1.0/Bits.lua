-- Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
local lib = LibStub("LibCorvid-1.0")
if not lib.loading then return end

local Bits = {}
lib.Bits = Bits

local floor, char, byte = math.floor, string.char, string.byte

function Bits.u16le(n)
  return char(n % 256, floor(n / 256) % 256)
end

function Bits.u32le(n)
  return char(n % 256, floor(n / 256) % 256, floor(n / 65536) % 256, floor(n / 16777216) % 256)
end

function Bits.readU16le(s, i)
  local a, b = byte(s, i, i + 1)
  return a + b * 256
end

function Bits.readU32le(s, i)
  local a, b, c, d = byte(s, i, i + 3)
  return a + b * 256 + c * 65536 + d * 16777216
end

function Bits.add32(a, b)
  return (a + b) % 4294967296
end

function Bits.hexToBytes(hex)
  if #hex % 2 ~= 0 or hex:find("[^0-9a-fA-F]") then return nil end
  return (hex:gsub("%x%x", function(h) return char(tonumber(h, 16)) end))
end

function Bits.bytesToHex(s)
  return (s:gsub(".", function(c) return ("%02x"):format(byte(c)) end))
end
