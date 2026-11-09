# Multiplayer threat model

The decision is ADR-0724. This document holds the threat model behind it, what the code already
does, and the gates that keep it true. It is written before any network code exists, on purpose.

## Scope and assumptions

- **Model:** deterministic lockstep. Peers exchange `PlayerCommand`s per turn, every peer runs the
  whole simulation, and an `InputFrame` is the only thing entering it (`InputFrame.cs`, Q712).
- **Planned:** multiplayer matches on Steam (SDR transport, Steam lobbies, login tickets), with
  a relay server that orders commands and referees disputes, and no player host. Owner
  decisions of 2026-11-09 are listed at the end.
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

## STRIDE

| threat | example | control (ADR-0724 rule) |
|---|---|---|
| **S**poofing | peer sends commands with another player's `PlayerId` | R2: the session layer stamps the sender's id; a command whose id differs is dropped |
| **S**poofing | client claims an account it does not own | R8: identity from a platform ticket verified by the backend |
| **T**ampering | command for a creature, room or spell the sender does not own | R3: deterministic validation in the simulation |
| **T**ampering | cheat or AI-only command from a human peer | R4: network allow-list of command tags |
| **T**ampering | modified packets in flight | R7: authenticated, encrypted transport |
| **R**epudiation | loser disputes a result | R6: hashed input journal per match, kept by the host/backend |
| **I**nformation disclosure | map hack (whole state on every client) | accepted for lockstep (R9) |
| **I**nformation disclosure | opponent's IP address | R7: relay by default |
| **D**enial of service | malformed packet crashes the decoder | R1: bounded decoder, fuzzed |
| **D**enial of service | malicious map crashes a peer at load | R5: file parsers fuzzed, transfer verified |
| **D**enial of service | command flood, faked lag | R1 rate limits, R6 turn deadlines |
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
- **Closed, R5 (container and MAP):** `KwdReader.Read` throws only `InvalidDataException`, checks
  each declared size against the stream before allocating, and checks header blocks before
  reading counts. `MapGrid.Parse` sizes the grid in `long`. Guarded by `KwdHostileInputTests`
  (synthetic file in CI; every shipped MAP file when the install is present).
- **Gap, R5:** the other per-level parsers (PLAYERS, THINGS, TRIGGERS, VARIABLES, LEVEL) are
  not fuzzed yet.
- **Gap, R2:** `PlayerId` is read from the payload (`Head`/`Read`), i.e. self-asserted.
- **Gap, R4:** the codec accepts every command, including `GrantCreatureLevelCommand` (AI-only:
  no human action corresponds) and `RevealMapCommand` (cheat/result screen).
- **Unverified, R3:** many handlers refuse invalid commands (ADR-0279, ADR-0312), but nothing yet
  proves *every* handler validates ownership, cost and range against a hostile sender.

## Gates

| gate | when | proves |
|---|---|---|
| NuGet audit (`NuGetAudit`, mode `all`) | every build | no known-vulnerable package |
| .NET security analyzers + CodeQL (C#) | CI | no injection, traversal or unsafe deserialization patterns |
| secret scan (gitleaks) over history | CI, once over full history | no credentials in the repo |
| codec fuzz target | CI (short), nightly (long) | any byte string decodes or throws one controlled exception, bounded memory and time |
| file-parser fuzz targets | CI (short), nightly (long) | same, for every reader reachable from map/mod transfer |
| hostile-command tests | `DK2.Simulation.Tests` | each network-allowed command, sent by the wrong player or with out-of-range values, is refused and the state hash stays equal on all peers |
| single-player through the session | every single-player test and replay | R1-R4 run on every command, not only in matches |
| review checklist | every PR touching codec, session, validation or backend | rules R1-R8 |
| external pentest of the backend | before public matchmaking | backend controls hold |

## Owner decisions (2026-11-09)

1. **Platform:** Steam. Steam networking through SDR, Steam lobbies, tickets verified by the server.
2. **Authority:** no player host, because a departure must not end the match. A relay server
   orders commands and enforces deadlines; a departed keeper goes to the AI or idles.
3. **Desync:** the relay replays the journal from the last agreed checkpoint only on a
   mismatch, then resyncs or kicks the peers that differ. This works for 2 to 4 players and
   for 2-2 splits. Its CPU cost is per dispute, not per match.
4. **Ranked:** not at launch. Journals are kept so a server-side checker can add it later.
5. **Mods:** original maps plus hash- and size-verified custom maps. Arbitrary mods wait for
   fuzzed parsers.

Still open, not blocking: whether a departed keeper is AI-driven or idle, and how the relay is
hosted (Steam game server or our own).
