# Corvid

Lua library that encodes small binary frames, encrypts them with ChaCha20 (RFC 8439) under a per-install key, and draws them as a 2D code (QR version 10) in the WoW UI. A .NET library reads and writes the same frames.

- `lua/LibCorvid-1.0` - embeddable WoW addon library (LibStub name `LibCorvid-1.0`).
- `lua/CorvidProbe` - test addon: prints a CRC-32/ChaCha20 self-check on login and draws one fixed message on channel 9.
- `dotnet/Corvid` - .NET reader and writer.
- `fixtures` - shared test vectors (ChaCha20 vectors from RFC 8439, frames, QR matrix).

## Embedding the Lua library

1. Copy `lua/LibCorvid-1.0` into your addon (for example `Libs/LibCorvid-1.0`) and list `Libs\LibCorvid-1.0\lib.xml` in the TOC before your own files. Several addons can embed it; LibStub keeps the newest copy and one shared mark.
2. List `CorvidKey.lua` in the TOC. Your desktop app writes this file (`AddonKeyFile.Write`); it contains `local _, ns = ...` and `ns.corvidKey = "<64 lowercase hex>"`. Do not ship a real key in the addon package.
3. On `PLAYER_LOGIN`, call `lib:Register(channel, ns.corvidKey, yourSavedVariablesTable)`.

## Lua API

`local lib = LibStub("LibCorvid-1.0")`. Every call takes the channel id your addon registered.

| Call | Arguments | Returns |
|------|-----------|---------|
| `lib:Register(channel, keyHex, db)` | `channel` integer 0..255, one owner per channel; `keyHex` 64 hex chars; `db` optional SavedVariables table | `true`, or `nil, err` with `err` = `"bad channel"`, `"bad key"` or `"channel taken"` |
| `lib:SetStatus(channel, body)` | `body` string, at most 32 bytes | `true`, or `false` (unregistered channel, not a string, too long; the old status stays) |
| `lib:Send(channel, class, type, body, replaceKey)` | `class` `"realtime"` or `"normal"`; `type` integer 1..255 (0 is the status); `body` string; `replaceKey` optional | `true` when queued, else `false` |
| `lib:SetBackground(channel, list)` | `list` array of `{ type = 1..255, body = string }`; replaces the channel's list, `{}` clears it | `true`, or `false` when any item is invalid or too big (the old list stays) |
| `lib:SetWanted(channel, wanted)` | boolean | nothing; the mark is shown while any registered channel is wanted |
| `lib:SetLocked(locked)` | boolean; locked by default | nothing; a locked mark moves only by dragging with Alt held |
| `lib:SetHint(channel, text)` | `text` string of at most 120 characters, or `nil` to clear | `true`, or `false` (unregistered channel, not a string, too long). Hovering the mark shows a tooltip: "Screen mark", "Alt + drag: move", then each channel's hint in channel order. A locked mark lets clicks through to the game and only reacts to hover. Older library copies lack this call, so guard it: `if lib.SetHint then lib:SetHint(channel, text) end` |
| `lib:ResetPosition()` | - | nothing |
| `lib:ReferencePoints()` | - | `tlX, tlY, trX, trY, blX, blY` (finder centres, same coordinate space as the mark's `GetLeft`/`GetTop`), or `nil` before the mark has a position |
| `lib:Stats()` | - | `{ frames, worstEncodeMs, lastGarbageKB, dropped }` |

Size rules. A frame has 251 bytes after the header. Each registered channel costs 8 bytes plus its status message (3 + status length); every other message costs 3 + body length. Call what is left `free`:

- `Send` refuses a body when `3 + #body > free`; a realtime body must also be at most 131 bytes (realtime takes at most 134 bytes per frame, headers included).
- `SetBackground` refuses an item when `3 + #body > free`.
- Background items rotate round-robin across channels. At least one goes out per second while any list is non-empty, for items that fit beside a full realtime share (`3 + #body <= free - 134`). A bigger item waits for a frame with room and does not hold back other channels' items.
- `replaceKey`: a newer message with the same key on the same channel replaces the pending one, in either class.

`Stats().dropped` counts messages the library discarded after accepting them:

- a pending message or background item that no longer fits an otherwise-empty frame, because another channel registered or a status grew;
- the oldest pending message of a channel and class once more than 64 are pending.

Frame timing: a frame stays up for at least 3 rendered frames and 60 ms; an idle mark still redraws once per second; a new realtime message can cut a frame that is not yet held. After a cut, a message can appear in two consecutive frames, so apps dedupe inside their channel.

The db table gets `db.corvidPos = { x, y, t }`. On Register the newest stored position across all registered addons wins and is written to every registered db.

## Frame layout (protocol 1)

- 268 bytes, zero-padded.
- Header, 17 bytes: magic `C7 1D`, version `1`, flags `0`, session id (8 bytes), frame counter (u32 LE), segment count (u8).
- Segment: channel (u8), key id (u8), ciphertext length (u16 LE), ciphertext.
- Segment plaintext: messages as `type u8, length u16 LE, body`, then CRC-32 (IEEE, reflected 0xEDB88320, init and xorout 0xFFFFFFFF, stored LE) over all message bytes. Type 0 is the channel status and always comes first.
- ChaCha20 per RFC 8439: 32-byte key, nonce = session id + frame counter u32 LE, block counter 0.
- Key id = first keystream byte for the key with an all-zero nonce, block counter 0.
- Picture: QR version 10, EC level L, mask 0, byte mode, 57x57 modules, 2 physical pixels per module, 4-module white quiet zone (130 px side). Reference points are the three finder centres at (7.5, 7.5), (57.5, 7.5), (7.5, 57.5) modules from the top-left corner, quiet zone included.

## .NET API

- `ICaptureSource` - your capture backend: `TryGetClient(out PixelRect client)` returns `false` while the game window does not exist; `Capture(PixelRect rect)` returns a `Bgra` image or `null` (an exception counts as a failed read). `Capture` may return the same buffer on every call or a new one each time: the reader copies what it needs to compare against the next capture.
- `MarkReader(source, new MarkReaderOptions(LockedEveryMs, SearchDelayMs, LockLostMs = 1500, RelockProbeMs = 500))` - call `Step(nowMs)` in a loop: it returns `(MarkRead? Read, int NextDelayMs)`; handle `Read` when it is not null, then wait `NextDelayMs`. While locked it polls every `LockedEveryMs`; while searching it waits `SearchDelayMs(failedSearches)`. While no client exists it polls at `SearchDelayMs(0)`.
- `MarkRead` - `Frame`, the raw `Bytes`, and `Points` (finder centres relative to the client area). `frame.Open(channel, key)` returns `OpenResult(Status, Messages)` with `Status` one of `Ok`, `NoSegment`, `WrongKey`, `BadCrc`, `Malformed`.
- `ChannelKey` - `Generate()`, `FromHex(hex)`, `ToHex()`, `KeyId`.
- `KeyStore.LoadOrCreate(path)` - loads the per-install key (DPAPI, current user) or creates and stores a new one.
- `AddonKeyFile.Write(addonDir, key)` - writes `CorvidKey.lua` into the addon folder.
- `FrameWriter.Write(session8, counter, segments)` - builds a frame; throws `ArgumentException` for an oversized frame, a body over 65535 bytes or a duplicate channel.
- `QrPicture.Encode(frame)`, `QrPicture.Render(matrix)`, `QrPicture.Decode(image)` - build and decode the picture; useful for fake capture sources in tests.

## Tests

- Lua: run `busted` from `lua/` (Lua 5.1 with the `bit` module).
- .NET: `dotnet test dotnet/Corvid.Tests`. `SandboxShotTests` runs only when `CORVID_SANDBOX_SHOT` points at a captured image of the probe mark.

License: MIT.
