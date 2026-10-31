-- Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
local lib = LibStub("LibCorvid-1.0")
if not lib.loading then return end

local ChaCha20 = {}
lib.ChaCha20 = ChaCha20

local Bits = lib.Bits
local bxor, bor, lshift, rshift = bit.bxor, bit.bor, bit.lshift, bit.rshift
local byte, char, concat = string.byte, string.char, table.concat
local MOD = 4294967296

local function rotl(x, n)
  return bor(lshift(x, n), rshift(x, 32 - n)) % MOD
end

local function quarter(s, a, b, c, d)
  s[a] = (s[a] + s[b]) % MOD
  s[d] = rotl(bxor(s[d], s[a]) % MOD, 16)
  s[c] = (s[c] + s[d]) % MOD
  s[b] = rotl(bxor(s[b], s[c]) % MOD, 12)
  s[a] = (s[a] + s[b]) % MOD
  s[d] = rotl(bxor(s[d], s[a]) % MOD, 8)
  s[c] = (s[c] + s[d]) % MOD
  s[b] = rotl(bxor(s[b], s[c]) % MOD, 7)
end

local function block(key, nonce, counter)
  local init = {
    0x61707865, 0x3320646e, 0x79622d32, 0x6b206574,
    Bits.readU32le(key, 1), Bits.readU32le(key, 5), Bits.readU32le(key, 9), Bits.readU32le(key, 13),
    Bits.readU32le(key, 17), Bits.readU32le(key, 21), Bits.readU32le(key, 25), Bits.readU32le(key, 29),
    counter % MOD, Bits.readU32le(nonce, 1), Bits.readU32le(nonce, 5), Bits.readU32le(nonce, 9),
  }
  local s = {}
  for i = 1, 16 do s[i] = init[i] end
  for _ = 1, 10 do
    quarter(s, 1, 5, 9, 13)
    quarter(s, 2, 6, 10, 14)
    quarter(s, 3, 7, 11, 15)
    quarter(s, 4, 8, 12, 16)
    quarter(s, 1, 6, 11, 16)
    quarter(s, 2, 7, 12, 13)
    quarter(s, 3, 8, 9, 14)
    quarter(s, 4, 5, 10, 15)
  end
  local out = {}
  for i = 1, 16 do out[i] = Bits.u32le(Bits.add32(s[i], init[i])) end
  return concat(out)
end

function ChaCha20.xor(key32, nonce12, counter, data)
  assert(#key32 == 32 and #nonce12 == 12, "ChaCha20 needs a 32-byte key and a 12-byte nonce")
  local out = {}
  for offset = 0, #data - 1, 64 do
    local ks = block(key32, nonce12, counter + offset / 64)
    local n = math.min(64, #data - offset)
    local bytes = {}
    for i = 1, n do
      bytes[i] = char(bxor(byte(data, offset + i), byte(ks, i)))
    end
    out[#out + 1] = concat(bytes)
  end
  return concat(out)
end

function ChaCha20.keyId(key32)
  return byte(block(key32, string.rep("\0", 12), 0), 1)
end
