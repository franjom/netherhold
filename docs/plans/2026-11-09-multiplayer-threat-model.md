# Multiplayer threat model

The decision is ADR-0724. This document holds the threat model behind it, what the code already
does, and the gates that keep it true. It is written before any network code exists, on purpose.

## Scope and assumptions

- **Model:** deterministic lockstep. Peers exchange `PlayerCommand`s per turn, every peer runs the
  whole simulation, and an `InputFrame` is the only thing entering it (`InputFrame.cs`, Q712).
- **Planned:** multiplayer matches on Steam (SDR transport, Steam lobbies, login tickets), with
  no player host. A relay brokers each match without game data: it orders commands, owns the
  clock, compares hashes and stores journals, but never simulates. Owner decisions are at the end.
- **Out of scope:** single-player save tampering against yourself, and piracy/DRM.

## Assets

| asset | why it matters |
|---|---|
| player machines | a remote crash or code execution is the worst outcome |
| match integrity | illegal commands, faked results, desync abuse |
| hidden information | enemy dungeon layout under fog of war |
| player identity and IP address | impersonation, harassment, DDoS |
| backend accounts, rankings, secrets | account takeover, ladder fraud |

## Trust boundaries

1. **Network to process:** every byte from a peer, relay or backend is untrusted.
2. **Peer to simulation:** a decoded command is a *request*. It is not a fact until the
   simulation has validated it.
3. **Transferred content:** maps, mods and saves received from a peer are untrusted files,
   parsed by the same readers as local files (`WadArchive`, `KwdReader`, `TqiDecoder`, ...).
4. **Client to backend:** the backend trusts nothing the client says about itself.
5. **Peer state:** a snapshot from a peer (rejoin, resync) and a state hash a peer reports are
   claims, accepted only when they agree with the majority.

## STRIDE

| threat | example | control (ADR-0724 rule) |
|---|---|---|
| **S**poofing | peer sends commands with another player's `PlayerId` | R2: the session layer stamps the sender's id; a command whose id differs is dropped |
| **S**poofing | client claims an account it does not own | R8: identity from a platform ticket verified by the backend |
| **T**ampering | command for a creature, room or spell the sender does not own | R3: deterministic validation in the simulation |
| **T**ampering | cheat or AI-only command from a human peer | R4: network allow-list of command tags |
| **T**ampering | modified packets in flight | R7: authenticated, encrypted transport |
| **T**ampering | forged possession pose (teleport, speed hack) | R4: refused until R3 validates a pose |
| **T**ampering | alliance forced on a player who did not agree | R3: consent checked in the simulation |
| **R**epudiation | loser disputes a result | R6: hashed input journal per match, kept by the relay |
| **R**epudiation | peer reports wrong hashes to force resyncs | R6: consensus; a peer that keeps disagreeing is dropped |
| **I**nformation disclosure | map hack (whole state on every client) | accepted for lockstep (R9) |
| **I**nformation disclosure | opponent's IP address | R7: relay by default |
| **D**enial of service | malformed packet crashes the decoder | R1: bounded decoder, fuzzed |
| **D**enial of service | malicious map crashes a peer at load | R5: file parsers fuzzed, transfer verified |
| **D**enial of service | command flood, faked lag | R1 rate limits, R6 turn deadlines |
| **D**enial of service | a command that throws in its handler, on every peer alike | R3: handlers never throw; a failing command is dropped deterministically |
| **D**enial of service | hostile snapshot at rejoin | R1: `WorldSnapshot.Read` bounded and fuzzed |
| **E**levation of privilege | code execution through deserialization | R1: no polymorphic or reflective deserialization |
| **E**levation of privilege | path traversal from a transferred file name | R5: never use a path from the wire |

## What the code already has

- **Good:** commands are typed records with an explicit binary codec (`CommandCodec.cs`), not
  reflection-based serialization. The simulation reads nothing but `InputFrame`s and hashes
  inputs and state, so desync detection is already possible.
- **Good:** no `unsafe` code; `AllowUnsafeBlocks` is off in Unity; no P/Invoke in `src/`.
- **Good:** the editor-only git query in `DungeonRunner.InputJournal.cs` runs only when
  `Application.isEditor` and takes fixed arguments.
- **Closed, R1 (codec):** `CommandCodec.Read` returns a command or throws `InvalidDataException`
  (malformed) or `EndOfStreamException` (cut short; journal readers rely on it). Tile lists and
  sell waves are capped at `CommandCodec.MaxTiles`, and a list cannot claim more bytes than
  remain. Guarded by `CommandCodecHostileInputTests`, a seeded fuzz of random and mutated
  input with an allocation bound. A network layer maps both exceptions to one rejection.
- **Closed, R1 (journals):** `InputJournal` and `FrameInputJournal` read every count and record
  length through `StreamCount.Read`, which refuses one the remaining bytes cannot hold, and map
  malformed string lengths to `InvalidDataException`. Guarded by tests that overwrite every word
  after the header with a huge or negative value under a 1 MB allocation bound.
- **Closed, R5 (level files):** `KwdReader.Read` throws only `InvalidDataException`, checks
  each declared size against the stream before allocating, and refuses a record count larger
  than the chunk's content bytes. The MAP, PLAYERS, THINGS, TRIGGERS, VARIABLES and LEVEL
  parsers compute sizes in `long` and size lists from the content, not the declared count.
  Guarded by `KwdHostileInputTests` (synthetic file with every record type but LEVEL in CI;
  every shipped level file when the install is present).
- **Gap, R5:** the remaining transferable formats (WAD, textures, meshes, sounds) are not fuzzed;
  they matter once custom maps can carry assets or arbitrary mods are allowed.
- **Closed, R2 and R4 (decoding):** `PeerCommands.Read` decodes a peer's command, refuses one
  whose `PlayerId` is not the authenticated sender, refuses the types no human may send, and
  refuses tile lists and sell waves that leave the map or exceed its tile count. Refused types:
  `GrantCreatureLevelCommand` and `MintWageCommand` (the computer keeper's), `RevealMapCommand`
  (result screen, cheat), `MovePossessedCreatureCommand` (until R3 validates a pose).
  `PeerCommandsTests` fails until every new command type is either allowed or named as refused.
  Journals keep using `CommandCodec.Read`, which accepts all.
- **Gap, R2:** no session layer exists yet to call `PeerCommands.Read` with the transport's
  sender, so single-player does not go through it.
- **Unverified, R3:** many handlers refuse invalid commands (ADR-0279, ADR-0312), but nothing yet
  proves *every* handler validates ownership, cost and range against a hostile sender, or that
  none throws on a hostile value.
- **Gap, R3 (possession):** `PossessionRules.Move` installs any position and heading, with no
  speed, wall or bounds check (`PossessionRules.Movement.cs`). Validation must still allow the
  Creature Teleport form, which has no speed bound.
- **Gap, R3 (alliances):** `AllianceSystem` writes both players' alliance bits from one
  player's `SetAllianceCommand`, so making an alliance needs no consent. DK2 has an offer array
  (`player+0x1B6`, action 118) and its HUD sends "make" only to accept a pending offer, but
  the make handler (`0x00513FF0`) checks nothing. The fix models offers and lets make succeed
  only when the target's offer to the issuer stands, which refuses only what DK2's own HUD
  never sends.
- **Gap, R6 (computer keepers):** `KeeperAIState` and each keeper's random generator are outside
  the snapshot and the hash, so rejoin, resync and replays cannot reproduce them yet.
- **Gap, R1 (snapshots):** `WorldSnapshot.Read` is not fuzzed; it bounds the grid only by
  `width*height <= int.MaxValue`.
- **Gap, determinism:** CI proves Linux and Windows hash alike under CoreCLR; nothing yet compares
  a Unity IL2CPP build.

## Gates

| gate | when | proves |
|---|---|---|
| NuGet audit (`NuGetAudit`, mode `all`) | every build | no known-vulnerable package |
| .NET security analyzers + CodeQL (C#) | CI | no injection, traversal or unsafe deserialization patterns |
| secret scan (gitleaks) over history | CI, once over full history | no credentials in the repo |
| codec fuzz target | CI (short), nightly (long) | any byte string decodes or throws one controlled exception, bounded memory and time |
| file-parser fuzz targets | CI (short), nightly (long) | same, for every reader reachable from map/mod transfer |
| hostile-command tests | `DK2.Simulation.Tests` | each network-allowed command, sent by the wrong player or with out-of-range values, is refused without throwing and the state hash stays equal on all peers |
| IL2CPP hash parity | CI, per release | a Unity player build reaches the same state hashes as CoreCLR |
| single-player through the session | every single-player test and replay | R1-R4 run on every command, not only in matches |
| review checklist | every PR touching codec, session, validation or backend | rules R1-R8 |
| external pentest of the backend | before public matchmaking | backend controls hold |

## Owner decisions (2026-11-09 and 2026-11-10)

1. **Platform:** Steam. Steam networking through SDR, Steam lobbies, tickets verified by the server.
2. **Authority:** no player host, because a departure must not end the match. The relay orders
   commands and enforces deadlines; a departed keeper is handed to the computer keeper.
3. **Game data:** we may not distribute DK2's data, so no server we run simulates. The relay
   is a data-free broker; a player who owns DK2 may self-host an overseer that simulates.
4. **Desync:** settled by consensus (ADR-0724 R6). Two-player matches resync one side and end as
   no contest after repeated desync.
5. **Ranked:** not at launch. Without a server-side simulation it would need consensus plus
   reports, or self-hosted overseers.
6. **Mods:** original maps plus hash- and size-verified custom maps. Arbitrary mods wait for
   fuzzed parsers.

7. **Relay hosting (2026-11-10):** one relay program of our own on the Steamworks game-server API,
   hosted by us in a few regions and runnable by anyone, so multiplayer outlives our servers.
