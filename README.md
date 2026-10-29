# Corvid

Lua library that encodes small binary frames, encrypts them with ChaCha20 (RFC 8439) under a per-install key, and draws them as a 2D code. A .NET library reads and writes the same frames.

- `lua/LibCorvid-1.0` - embeddable WoW addon library (LibStub name `LibCorvid-1.0`).
- `fixtures` - shared test vectors (ChaCha20 vectors from RFC 8439).

Run the Lua specs from `lua/` with `busted`.

License: MIT.
