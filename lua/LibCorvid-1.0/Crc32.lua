-- Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
local lib = LibStub("LibCorvid-1.0")
if not lib.loading then return end

local Crc32 = {}
lib.Crc32 = Crc32

local bxor, band, rshift = bit.bxor, bit.band, bit.rshift
local byte = string.byte
local MOD = 4294967296

local tbl = {}
for i = 0, 255 do
  local c = i
  for _ = 1, 8 do
    if band(c, 1) == 1 then
      c = bxor(rshift(c, 1), 0xEDB88320) % MOD
    else
      c = rshift(c, 1)
    end
  end
  tbl[i] = c
end

function Crc32.compute(s)
  local crc = 0xFFFFFFFF
  for i = 1, #s do
    crc = bxor(tbl[band(bxor(crc, byte(s, i)), 0xFF)], rshift(crc, 8)) % MOD
  end
  return bxor(crc, 0xFFFFFFFF) % MOD
end
