local lib = LibStub("LibCorvid-1.0")
if not lib.loading then return end

local ReedSolomon = {}
lib.ReedSolomon = ReedSolomon

local bxor = bit.bxor

local EXP, LOG = {}, {}
do
  local x = 1
  for i = 0, 254 do
    EXP[i] = x
    LOG[x] = i
    x = x * 2
    if x >= 256 then x = bxor(x, 0x11D) end
  end
end

local function mul(a, b)
  if a == 0 or b == 0 then return 0 end
  return EXP[(LOG[a] + LOG[b]) % 255]
end

local generators = {}

local function generator(n)
  local g = generators[n]
  if g then return g end
  g = { 1 }
  for i = 0, n - 1 do
    local root = EXP[i]
    local nextG = {}
    for j = 1, #g + 1 do
      nextG[j] = bxor(g[j] or 0, mul(g[j - 1] or 0, root))
    end
    g = nextG
  end
  generators[n] = g
  return g
end

function ReedSolomon.parity(data, n)
  local g = generator(n)
  local res = {}
  for j = 1, n do res[j] = 0 end
  for i = 1, #data do
    local factor = bxor(data[i], res[1])
    for j = 1, n - 1 do
      res[j] = bxor(res[j + 1], mul(g[j + 1], factor))
    end
    res[n] = mul(g[n + 1], factor)
  end
  return res
end
