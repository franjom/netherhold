# Seamless lockstep multiplayer

How multiplayer should feel: no click ever waits visibly, no player's bad connection freezes the
others, and a player who leaves or drops neither ends the match nor stalls it. The rules are
ADR-0724; the threats are in `2026-11-09-multiplayer-threat-model.md`. This plan is the order
of work and the exit test for each step. Two critical reviews (2026-11-10) are folded in.

## Architecture

- **Lockstep.** Every machine runs the whole simulation from the same ordered commands.
- **No server we run holds DK2's data**, which we may not distribute. The official relay is a
  data-free sequencer: it orders commands into turns, owns the turn clock, compares state
  hashes and data fingerprints, and stores the input journal. It never simulates, and it never
  carries world state.
- **Nobody loads another player's state.** Rejoin and resync replay the relay's journal on the
  player's own machine, from a local checkpoint or from tick 0, checked against the agreed
  hashes. A snapshot from a peer would be state injection: in a two-player match the receiver
  would trust its opponent completely, and two colluders outvote one honest player.
- **Desync.** Hashes detect it and the journal locates it. A machine that still disagrees after
  replaying the journal has diverged for good and continues alone, its own fork against
  computer keepers. With three or more players the majority's match goes on, with the diverged
  slot on the caretaker. With two players, or a 2-2 split, there is no majority: both fork and
  the match is no contest. A deliberate wrong hash gains nothing.
- **Nobody supplies the relay's history either.** Every commit carries a hash chained over all
  earlier commits. A new relay instance resumes from the longest prefix every client holds
  identically and drops the tail, so no client can add a command in another player's name. A
  client cannot rewind the match either, because one client's prefix may trail the others' by
  at most N turns, where N is a little more than the commits a client can honestly miss when a
  relay dies:
  - With three or more players, a client more than N turns short is a rejoiner: it gets the
    caretaker and replays up to the others' common prefix, and does not set the resume point.
  - With two players or a 2-2 split, a short honest journal cannot be told from a truncated one,
    nor a long honest one from a forged tail (the chain is unkeyed). If the prefixes differ by
    more than N turns, both fork and the match is no contest.
- **Computer keepers run on every machine.** Their whole state, including each keeper's routing
  locate cache, joins the snapshot and the hash. No machine may act for another keeper.
- **Self-hosted overseer.** A player who owns DK2 may run a server on their own install that
  simulates as an authoritative peer: trusted AI, truth on desync, rejoin from the server.

## Starting point (2026-11-10)

- **The tick is 250 ms** (`SimulationTiming.TicksPerSecond = 4`, DK2's own rate).
- **Click to world change is already slow in single-player:** a click waits for the next tick
  (125 ms on average), and the draw clock runs a tick behind to interpolate (`DungeonRunner.cs`
  around line 1010, ADR-0687, ADR-0694). Multiplayer adds the turn close, the round trip and a
  jitter buffer: roughly 450 to 600 ms. **Instant previews are required, not polish.**
- **Interpolation exists** for creatures, shots, lights and the camera path. Headings snap.
- **Ticks run in batches on a worker thread**; only a batch's first tick carries commands. The
  options menu and losing window focus (`runInBackground: 0`) both stop ticks.
- **At-once feedback** exists for tag tiles, the minimap pick-up, and the slap and spell cursors.
  Pick-up in the world, drop, room, door and trap placement show nothing until the tick.
- **Refused from peers for now:** `MovePossessedCreatureCommand` (any pose is installed) and
  `SetAllianceCommand` (DK2's make handler ignores offers; only its HUD asked for consent).
- **Computer keepers do not run in Unity.** Their state is outside the snapshot and the hash:
  `KeeperAIState`, a private generator, `KeeperStaging`, and history-dependent routing
  (`KeeperRouteCache`, whose answers depend on past queries, and `KeeperAI._directionsFixed`).
- **Game data is outside the hash.** The snapshot omits the database on purpose and the hash
  covers version, world, navigation and the generator. Two DK2 patch levels or an edited
  creature file desync silently.
- **Determinism evidence is thinner than it looks.** CI compares Linux and Windows hashes only on
  synthetic content (`DK2_DIR=/nonexistent`); no real map or keeper run is compared across
  platforms, and nothing compares IL2CPP. There are 13 `[ThreadStatic]` fields in 11 files.
  Some rules take the first match from a database `Dictionary` (`ReaperRules.cs`,
  `HeroPartyRules.cs`, `CrateRules.cs`), which depends on insertion order. The trap sine table
  is built with `Math.Sin` (`TrapRules.cs`).
- **Known costs:** ADR-0578 measured 1.9 ms per tick for computer keepers and 0.65 ms for the
  state hash on one level under CoreCLR.

## Phase 0: measure

**CoreCLR results (2026-11-10).** Scenario `scenarios/perf/level11a_keepers_sizing.yaml`: level
11A, its four computer keepers playing, the human slot idle, 4,800 ticks (20 game minutes),
about 50 creatures. Release build on a Ryzen 7 7735HS laptop with other work running, so the
spread between runs is wide; the faster runs are quoted.

| measure | result | against the 250 ms tick |
|---|---|---|
| tick, live with keepers | median 8 ms, 99th percentile 44 to 58 ms, worst 330 to 400 ms on tick 1 and 49 ms after it (speed-budget run) | median 3 %; only the first tick exceeds a whole tick |
| computer keepers | 4.6 ms a tick for four (about 1.1 ms each) | the largest single cost |
| allocation | 665 KB a tick (2.6 MB a second), 555 KB of it keeper AI | a GC risk under IL2CPP |
| journal replay, no AI, hashing every tick | 5 to 6 ms a tick, about 40 to 50 times real time | 20 minutes replays in about 30 s |
| exact snapshot (`FromBytes`) | 900 KB raw, 40 to 60 KB compressed, write 3 to 4 ms, read 2.5 ms | a checkpoint costs about one tick's median |
| journal | 128 KB for 20 minutes, AI commands included | human commands alone are far smaller |

What follows:

- **Checkpoints are cheap.** Writing one every 30 game seconds costs well under 0.1 ms a tick on
  average. Rejoin after a crash is a local checkpoint plus seconds of replay; a new machine
  replays from tick 0 at about 40 times real time (90 s for an hour's match).
- **Only the first tick is long.** Tick 1 (the first job scans) takes up to 400 ms; the start
  barrier hides it. After it the worst tick was 49 ms in a clean run, though a busy machine showed
  one of 390 ms, so phase 4 must not treat one long tick as a slow machine.
- **The keeper AI is the budget.** It is most of the time and of the allocation, on every
  machine for every keeper. Reducing its allocation is worth doing before IL2CPP numbers.
- **Rollback is out.** A tick plus a restore costs about 10 ms, so re-simulating even two ticks
  inside a 16 ms frame does not fit.
- **`LoadGame` is not a checkpoint.** It reloads as DK2 loads a save (room chains re-added row by
  row), so its hash differs by design; checkpoints use `FromBytes`.
- The snapshot does not yet hold computer keeper state (phase 1), so it will grow.

**Still to measure:** the same run in an IL2CPP player build and on a minimum-spec machine, a
late-game map with more creatures, and click-to-world-change latency in the Unity build.

## Phase 1: session layer and determinism, single-player first

1. `IGameSession`: `Submit(command)`, `TryGetFrame(tick, out InputFrame)`, the local player
   id, and session actions (pause, speed, handover, result screen). `LocalGameSession`
   implements it in process.
2. Every submitted command goes through `CommandCodec` and `PeerCommands.Read`, so R1 to R4 run
   on every single-player command. Pose validation (phase 5) and alliance offers must exist
   before single-player possession and alliances can go through the session.
3. `DungeonRunner` submits instead of filling `_pendingCommands` and takes frames from the
   session. `RevealMapCommand` from the result screen becomes a session action.
4. **Computer keepers move into the session** and run on every machine:
   - Their state joins `WorldSnapshot` and the hash (a `HashVersion` bump): `KeeperAIState`,
     the generator, staging, `_directionsFixed`, and each keeper's routing locate cache. The
     cache's walk is DK2's own (ADR-0486), so it is saved, not reset: behaviour and keeper
     replays stay as they are, a non-deviation. The route mesh is rebuilt whenever its
     navigation bytes change, so saving the cache should suffice; verify that, or save the mesh.
   - **Canonical frame order:** human commands by player, then by sender sequence; then computer
     keeper commands by player. The journal records the merged frame.
   - **A slot that began human** never receives the computer keeper's own perks
     (`GrantCreatureLevelCommand`, `MintWageCommand`), so leaving never pays.
5. **Handlers validate before they mutate.** A throw is a bug: the match pauses, every machine
   writes a dump, and the journal reproduces it. Phase 3 fuzzing removes the causes. IL2CPP
   builds keep null and bounds checks on for the simulation assemblies.
6. **Handover test:** swap a player to the computer keeper at tick T over the campaign replays.
   Define its side effects: the hand's contents dropped at the Heart, a creature still landing
   (`LandingPending`, which counts as held) or still being lifted (ADR-0731) resolved, possession
   ended, a personality chosen for a slot that had none.
7. **Diagnosis from the start:** per-subsystem hashes (tiles, creatures, players, generator,
   navigation, computer keepers) and a dump that names the first differing subsystem and entity.
8. **Data fingerprint:** a hash of the parsed values the simulation reads (database, level data,
   tables), not of file bytes, so differently packaged or localised installs with identical game
   data still match. Two machines with different fingerprints never start a match together.
9. **Determinism hardening:** order database iteration explicitly (or prove it is insertion
   order on every runtime); pin the sine table with a checksum test; audit the 13
   `[ThreadStatic]` fields.
10. **Parity gates:** an IL2CPP player build replays journals and must report the CoreCLR
    hashes. Real-data cross-platform runs need an install, which CI may not hold, so they run on
    a self-hosted runner or a release checklist machine.
11. **Simulated latency** (delay, jitter, loss) on `LocalGameSession`, for phases 2 and 5.

**Exit:** single-player plays as before; replays reproduce their hashes (after the bump); a
campaign level runs with its computer keepers in Unity; **snapshot at tick T, reload, continue
with keepers, equal hashes**; a handover at any tick keeps replays deterministic; IL2CPP matches.

## Phase 2: instant acknowledgement, and the possession prototype

**Pending-command previews.** Each submitted command gets a presentation-only preview, keyed by
its sender sequence number (the client cannot know the tick in advance). The preview is removed
when the turn holding that sequence has run, replaced by the result or a refusal cue.

- **Pick-up:** the creature or gold in the hand at once, hidden at its spot, with the sound. A
  human pick-up is becoming two-step like DK2's hand state 1 (ADR-0731, planned): the creature
  enters the hand at that player's next hand command or a turn later, so the preview lasts until
  then.
- **Drop:** the thing at the drop spot with the drop animation started. A dropped creature lands
  a tick after its drop (ADR-0730), so its preview lasts until the landing.
- **Room, door and trap:** a ghost with the build sound.
- **Slap and spells:** these animate already; add the sound at click time.
- **Refusals and conflicts:** the simulation emits a refusal event per refused command; the
  preview fades with the refusal sound. This covers two players grabbing one creature.

**Possession prototype** under simulated latency, now rather than in phase 5, because its verdict
could change the tick rate everything else is sized on. If first-person play at 4 Hz with
real latency cannot be made to feel right, decide on a finer possession tick or rollback here.

**Exit:** with two ticks of simulated latency, a playtest cannot tell the difference from zero
on pick-up, drop, tag, build and slap; the possession verdict is written down.

## Phase 3: loopback multiplayer, no Steam

An in-process relay and two to four sessions in one process, joined by a fake transport with
latency, jitter, loss and reordering.

- **Agreement** over long seeded matches through every fault injected.
- **Late peer:** its commands land later, in their original order; the others never stall.
- **Departure, caretaker and handover** on announced ticks, with equal hashes.
- **Rejoin by journal replay** from a local checkpoint or tick 0, verified against the agreed
  hashes at every checkpoint, then control returned on an announced tick.
- **Desync:** a forced divergence is detected and located by the per-subsystem hashes. In 3 and 4
  player matches the majority continues with the diverged slot on the caretaker; in 2-player
  and 2-2 matches both fork and the match is no contest.
- **Relay resume:** a new relay instance resumes from the clients' longest common chained prefix.
  A forged tail changes nothing; a truncated journal more than N turns short makes its sender a
  rejoiner (3+ players) or ends the match as no contest (2 players, 2-2). Fault injection sizes N
  from the commits a client honestly misses when the relay dies.
- **Hostile peer (R3):** every allowed command tag fuzzed against random world states, refused
  or applied without throwing, hashes equal.
- **Rate limits and message caps** on the relay (R1); a peer cannot send a map-sized tile list
  every turn.

**Exit:** all of these run in CI as deterministic tests.

## Steam spike, before phase 4

A throwaway prototype to settle what the plan assumes about Steam: SDR message size limits,
whether a relay that anyone runs is reachable through SDR, ticket checks on the Steamworks
game-server API, and lobby behaviour.

## Phase 4: the relay owns time

1. **The relay is a sequencer.** It closes a turn every tick, puts every command that arrived
   in the turn open at that moment, and broadcasts the commit (turn number, ordered frame,
   handovers). There is no shared input delay: one player's bad link affects only that player.
2. **Per-sender sequence numbers** keep each sender's commands in order; a late command moves
   every later one from that sender with it.
3. **Client buffer.** Each client runs committed turns from a small buffer (the existing draw
   lag) and plays slightly faster or slower to keep it at target. This controls how far behind
   the display runs; it is not clock sync.
4. **The client keeps ticking** when the window loses focus or a menu opens
   (`runInBackground`, the options-menu pause) during a match.
5. **A slow machine is not a slow link.** A client that cannot simulate at the relay's pace is
   warned and, if it falls a set number of turns behind, gets the caretaker. Phase 0 sets the
   minimum spec.
6. **Disconnects.** A disconnected keeper gets the **caretaker** (defends, does not sell, build
   or spend) for the whole rejoin window, long enough for a crash, relaunch, load and replay.
   The full computer keeper takes over only when the player leaves on purpose or the window
   ends. A returning player gets control back on an announced tick.
7. **Pause** is granted from a small per-player budget.

**Exit:** with one peer on a 300 ms, 5 % loss link, the others' play is unaffected; that peer's
own clicks still acknowledge at once.

## Phase 5: possession

Built to the verdict of phase 2's prototype.

- **Send movement input, not poses.** A pose sent at tick T lands after knockback, freezes or
  speed changes that the sender had not seen, so honest players would be clamped. The
  simulation moves the creature from input; the client predicts with the same integer code and
  blends to the confirmed position over about 100 ms when they differ.
- **Validation (R3)** of the movement input (range of each axis, rate). Teleports come from the
  simulation's own spell, never from the client. Only then does a possession movement command
  return to the allow-list.
- **Attacks** start their animation and sound locally; the hit resolves in the simulation.
- **Headings** are interpolated.

**Exit:** possession with two ticks of simulated latency feels like zero latency; a speed-hack
test client gains nothing.

## Phase 6: Steam and the match lifecycle

- **Relay hosting:** our relay program on the Steamworks game-server API, in a few regions; the
  same binary runs on a player's machine for private, LAN and community games.
- **Transport and lobby:** SDR transport, Steam lobby, ticket check (R7, R8). Lobby metadata is
  client-writable, so the relay checks every peer's build hash, `HashVersion`, data fingerprint,
  map hash and settings itself.
- **Seed** picked by the relay after settings lock; **game speed** is a locked lobby setting.
- **Start barrier:** the first tick waits until every peer has loaded, up to a timeout; a peer
  that never loads starts with the caretaker.
- **Custom maps** through Steam Workshop rather than transfer between players.
- **Relay failure:** a new relay instance resumes from the longest chained-hash prefix every
  client holds identically, drops the tail, and refuses to let one client's prefix rewind the
  match by more than N turns (N sized by the spike and phase 3).
- **Desync reports** carry the per-subsystem diff and the journal, but no world state, so no
  DK2-derived data reaches our servers.
- **Versions:** the lobby matches only identical builds and fingerprints; old journals replay
  only on the build that recorded them.
- **Per-account desync and leave counts** for matchmaking.

## Multiplayer rules still to design

- **Victory, teams and fog settings**; the result screen; what `ContinueAfterResultCommand` does
  in a match.
- **Trigger commands:** `GuiButtonCommand` fires player event 53 with any target, and any peer's
  `SkipCameraPathCommand` changes trigger state. Decide which a match accepts.
- **Alliance offers:** model DK2's offer array (action 118) and accept a make only against a
  standing offer; until then alliances are refused from peers.
- **Slot lifecycle:** a defeated keeper becomes a spectator; a kicked player cannot rejoin; the
  match ends when no human remains.
- **Spectators, replays and multiplayer save/resume**, all journal-based.

## Rollback

Not planned. At 250 ms ticks with previews the strategy layer does not need it. Only phase 2's
possession verdict could call for it, and it would stay inside the session layer.

## Risks

- **Computer keeper determinism and cost**, now that every machine runs every keeper and its
  state, routing cache included, must round-trip through the snapshot.
- **A handover into a dungeon the AI did not build**, tested in phase 1.
- **IL2CPP versus CoreCLR**, and real data across platforms, which CI cannot hold.
- **Rejoin time** grows with match length if checkpoints are sparse; phase 0 sizes it.
- **The worker thread and the session**: the tick thread waits for frames without blocking the
  main thread.
