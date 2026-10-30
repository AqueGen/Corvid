local lib = LibStub("LibCorvid-1.0")
if not lib.loading then return end

local Frame = {}
lib.Frame = Frame

local Bits, Crc32, ChaCha20 = lib.Bits, lib.Crc32, lib.ChaCha20
local char, rep, concat = string.char, string.rep, table.concat

local FRAME_SIZE = 268
local HEADER = 17
local SEGMENT_OVERHEAD = 4 + 4
local MESSAGE_OVERHEAD = 3
local MAGIC = "\199\29"

Frame.FRAME_SIZE = FRAME_SIZE

function Frame.free(shape)
  local left = FRAME_SIZE - HEADER
  for _, seg in ipairs(shape) do
    left = left - SEGMENT_OVERHEAD
    for _, size in ipairs(seg.sizes) do
      left = left - MESSAGE_OVERHEAD - size
    end
  end
  return left
end

function Frame.build(session8, counter, segments)
  assert(#session8 == 8, "session id must be 8 bytes")
  local shape = {}
  for i, seg in ipairs(segments) do
    local sizes = {}
    for j, m in ipairs(seg.messages) do sizes[j] = #m.body end
    shape[i] = { sizes = sizes }
  end
  if Frame.free(shape) < 0 then return nil, "too big" end

  local nonce = session8 .. Bits.u32le(counter)
  local out = { MAGIC, "\1\0", session8, Bits.u32le(counter), char(#segments) }
  for _, seg in ipairs(segments) do
    local parts = {}
    for j, m in ipairs(seg.messages) do
      parts[j] = char(m.type) .. Bits.u16le(#m.body) .. m.body
    end
    local plain = concat(parts)
    plain = plain .. Bits.u32le(Crc32.compute(plain))
    out[#out + 1] = char(seg.channel, seg.keyId or ChaCha20.keyId(seg.key)) .. Bits.u16le(#plain)
    out[#out + 1] = ChaCha20.xor(seg.key, nonce, 0, plain)
  end
  local frame = concat(out)
  return frame .. rep("\0", FRAME_SIZE - #frame)
end
