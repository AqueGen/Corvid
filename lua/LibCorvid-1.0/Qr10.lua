local lib = LibStub("LibCorvid-1.0")
if not lib.loading then return end

local Qr10 = {}
lib.Qr10 = Qr10

local band, rshift = bit.band, bit.rshift
local byte = string.byte
local parity = lib.ReedSolomon.parity

local SIZE = 57
local DATA_CODEWORDS = 274
local BLOCK_SIZES = { 68, 68, 69, 69 }
local EC_PER_BLOCK = 18
local FORMAT_BITS = 0x77C4
local VERSION_BITS = 0x0A4D3
local ALIGNMENT = { 6, 28, 50 }

local matrix, isFunction = {}, {}
for r = 0, SIZE - 1 do
  matrix[r + 1], isFunction[r] = {}, {}
  for c = 0, SIZE - 1 do
    matrix[r + 1][c + 1] = false
  end
end

local function setFunction(r, c, dark)
  matrix[r + 1][c + 1] = dark
  isFunction[r][c] = true
end

local function bitOf(value, i)
  return band(rshift(value, i), 1) == 1
end

for _, corner in ipairs({ { 0, 0 }, { 0, SIZE - 7 }, { SIZE - 7, 0 } }) do
  for dr = -1, 7 do
    for dc = -1, 7 do
      local r, c = corner[1] + dr, corner[2] + dc
      if r >= 0 and r < SIZE and c >= 0 and c < SIZE then
        local d = math.max(math.abs(dr - 3), math.abs(dc - 3))
        setFunction(r, c, d ~= 2 and d ~= 4)
      end
    end
  end
end

for i = 8, SIZE - 9 do
  setFunction(6, i, i % 2 == 0)
  setFunction(i, 6, i % 2 == 0)
end

for _, ar in ipairs(ALIGNMENT) do
  for _, ac in ipairs(ALIGNMENT) do
    local onFinder = (ar == 6 and ac ~= 28) or (ac == 6 and ar ~= 28)
    if not onFinder then
      for dr = -2, 2 do
        for dc = -2, 2 do
          setFunction(ar + dr, ac + dc, math.max(math.abs(dr), math.abs(dc)) ~= 1)
        end
      end
    end
  end
end

for i = 0, 5 do setFunction(i, 8, bitOf(FORMAT_BITS, i)) end
setFunction(7, 8, bitOf(FORMAT_BITS, 6))
setFunction(8, 8, bitOf(FORMAT_BITS, 7))
setFunction(8, 7, bitOf(FORMAT_BITS, 8))
for i = 9, 14 do setFunction(8, 14 - i, bitOf(FORMAT_BITS, i)) end
for i = 0, 7 do setFunction(8, SIZE - 1 - i, bitOf(FORMAT_BITS, i)) end
for i = 8, 14 do setFunction(SIZE - 15 + i, 8, bitOf(FORMAT_BITS, i)) end
setFunction(SIZE - 8, 8, true)

for i = 0, 17 do
  local a, b = SIZE - 11 + i % 3, math.floor(i / 3)
  setFunction(b, a, bitOf(VERSION_BITS, i))
  setFunction(a, b, bitOf(VERSION_BITS, i))
end

local orderRow, orderCol = {}, {}
do
  local right = SIZE - 1
  while right >= 1 do
    if right == 6 then right = 5 end
    local upward = band(right + 1, 2) == 0
    for vert = 0, SIZE - 1 do
      local r = upward and SIZE - 1 - vert or vert
      for j = 0, 1 do
        local c = right - j
        if not isFunction[r][c] then
          orderRow[#orderRow + 1] = r + 1
          orderCol[#orderCol + 1] = c + 1
        end
      end
    end
    right = right - 2
  end
end
isFunction = nil

local data, codewords = {}, {}
local blocks = {}
for b = 1, #BLOCK_SIZES do blocks[b] = {} end

local function buildData(frame)
  local n = #frame
  if n > DATA_CODEWORDS - 3 then error("frame too long for qr v10-L") end
  data[1] = 0x40 + rshift(n, 12)
  data[2] = band(rshift(n, 4), 0xFF)
  local prev = band(n, 0xF)
  for i = 1, n do
    local b = byte(frame, i)
    data[i + 2] = prev * 16 + rshift(b, 4)
    prev = band(b, 0xF)
  end
  data[n + 3] = prev * 16
  local pad = 0xEC
  for i = n + 4, DATA_CODEWORDS do
    data[i] = pad
    pad = pad == 0xEC and 0x11 or 0xEC
  end
end

function Qr10.encode(frame, yieldFn)
  buildData(frame)
  if yieldFn then yieldFn() end

  local pos, ecs = 0, {}
  for b = 1, #BLOCK_SIZES do
    local block = blocks[b]
    for i = 1, BLOCK_SIZES[b] do block[i] = data[pos + i] end
    pos = pos + BLOCK_SIZES[b]
    ecs[b] = parity(block, EC_PER_BLOCK)
    if yieldFn then yieldFn() end
  end

  local k = 0
  for i = 1, BLOCK_SIZES[#BLOCK_SIZES] do
    for b = 1, #BLOCK_SIZES do
      if i <= BLOCK_SIZES[b] then
        k = k + 1
        codewords[k] = blocks[b][i]
      end
    end
  end
  for i = 1, EC_PER_BLOCK do
    for b = 1, #BLOCK_SIZES do
      k = k + 1
      codewords[k] = ecs[b][i]
    end
  end

  local m = 0
  for i = 1, k do
    local v = codewords[i]
    for shift = 7, 0, -1 do
      m = m + 1
      local r, c = orderRow[m], orderCol[m]
      matrix[r][c] = (band(rshift(v, shift), 1) == 1) ~= ((r + c) % 2 == 0)
    end
    if yieldFn and i % 32 == 0 then yieldFn() end
  end
  return matrix
end
