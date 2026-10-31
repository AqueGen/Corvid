-- Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
_G.bit = require("bit")
_G.strmatch = string.match
require("spec.wow_stub")

local dir = "LibCorvid-1.0/"
local f = assert(io.open(dir .. "lib.xml", "rb"))
local xml = f:read("*a")
f:close()
for name in xml:gmatch('<Script file="([^"]+)"/>') do
  dofile(dir .. name)
end

LibStub("LibCorvid-1.0").loading = nil
