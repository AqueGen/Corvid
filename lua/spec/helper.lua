_G.bit = require("bit")
_G.strmatch = string.match

local dir = "LibCorvid-1.0/"
for _, name in ipairs({ "LibStub", "Core", "Bits", "Crc32", "ChaCha20" }) do
  dofile(dir .. name .. ".lua")
end

LibStub("LibCorvid-1.0").loading = nil
