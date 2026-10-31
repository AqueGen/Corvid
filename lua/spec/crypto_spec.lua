-- Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
require("spec.helper")

local lib = LibStub("LibCorvid-1.0")
local Bits, Crc32, ChaCha20 = lib.Bits, lib.Crc32, lib.ChaCha20

describe("Bits", function()
  it("wraps add32 at 2^32", function()
    assert.are.equal(0, Bits.add32(0xFFFFFFFF, 1))
  end)

  it("round-trips u32le and u16le", function()
    assert.are.equal(0xDEADBEEF, Bits.readU32le(Bits.u32le(0xDEADBEEF), 1))
    assert.are.equal(0xBEEF, Bits.readU16le(Bits.u16le(0xBEEF), 1))
    assert.are.equal("\1\2\3\4", Bits.u32le(0x04030201))
  end)

  it("converts hex", function()
    assert.are.equal("\0\255", Bits.hexToBytes("00ff"))
    assert.are.equal("00ff", Bits.bytesToHex("\0\255"))
    assert.is_nil(Bits.hexToBytes("0"))
    assert.is_nil(Bits.hexToBytes("zz"))
  end)
end)

describe("Crc32", function()
  it("matches the IEEE check value", function()
    assert.are.equal(0xCBF43926, Crc32.compute("123456789"))
  end)
end)

describe("ChaCha20", function()
  local function rows()
    local out = {}
    for line in io.lines("../fixtures/chacha20.tsv") do
      local name, key, nonce, counter, pt, ct = line:match("^([^\t]+)\t([^\t]+)\t([^\t]+)\t([^\t]+)\t([^\t]+)\t([^\t]+)$")
      out[#out + 1] = { name, Bits.hexToBytes(key), Bits.hexToBytes(nonce), tonumber(counter), Bits.hexToBytes(pt), Bits.hexToBytes(ct) }
    end
    return out
  end

  it("has all RFC 8439 vectors", function()
    assert.are.equal(7, #rows())
  end)

  for i = 1, 7 do
    it("round-trips vector " .. i, function()
      local r = rows()[i]
      assert.are.equal(r[6], ChaCha20.xor(r[2], r[3], r[4], r[5]), r[1])
      assert.are.equal(r[5], ChaCha20.xor(r[2], r[3], r[4], r[6]), r[1])
    end)
  end

  it("derives key id from the first keystream byte", function()
    assert.are.equal(0x76, ChaCha20.keyId(string.rep("\0", 32)))
  end)
end)
