-- Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
require("spec.helper")

local lib = LibStub("LibCorvid-1.0")
local stub = require("spec.wow_stub")

local function login()
  local printed = {}
  local print = _G.print
  _G.print = function(line) printed[#printed + 1] = line end
  local ok, err = pcall(function()
    assert(loadfile("CorvidProbe/Probe.lua"))("CorvidProbe", { corvidKey = ("0123456789abcdef"):rep(4) })
    local frame = stub.created[#stub.created]
    frame:GetScript("OnEvent")(frame, "PLAYER_LOGIN")
  end)
  _G.print = print
  _G.CorvidProbeDB = nil
  assert(ok, err)
  return printed
end

describe("CorvidProbe", function()
  it("prints a passing self-check on login", function()
    assert.are.same({ "CorvidProbe self-check: ok" }, login())
  end)

  it("names every failed check", function()
    local compute, keyId = lib.Crc32.compute, lib.ChaCha20.keyId
    lib.Crc32.compute = function() return -1 end
    lib.ChaCha20.keyId = function() return 0 end
    local ok, printed = pcall(login)
    lib.Crc32.compute, lib.ChaCha20.keyId = compute, keyId
    assert(ok, printed)
    assert.are.same({ "CorvidProbe self-check: FAILED: crc32, keyId" }, printed)
  end)
end)
