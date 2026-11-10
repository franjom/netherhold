# Seamless lockstep multiplayer

How multiplayer should feel: no click ever waits visibly, no player's bad connection freezes the
others, and a player who leaves or drops neither ends the match nor stalls it. The rules are
ADR-0724; the threats are in `2026-11-09-multiplayer-threat-model.md`. This plan is the order
of work and the exit test for each step. It was reviewed critically on 2026-11-10; the findings
are folded in below.

## Architecture

- **Lockstep.** Every machine runs the whole simulation from the same ordered commands.
- **No server we run holds DK2's data**, which we may not distribute. The official relay is a
  data-free broker: it orders commands, owns the turn clock, enforces deadlines, compares state
  hashes, stores input journals and forwards snapshots. It never simulates.
- **Desync is settled by consensus.** With three or more players, the majority resyncs the rest
  from a majority snapshot. With two, one side is resynced and repeated desync ends the match as
  no contest. There is no server-side truth to appeal to.
- **Computer keepers run on every machine.** Their state joins the snapshot and the hash. No
  single machine may act for another keeper.
- **Self-hosted overseer.** A player who owns DK2 may run a server on their own install that
  simulates as an authoritative peer. It speaks the relay's protocol and adds a trusted AI,
  truth on desync and rejoin from the server. Communities and tournaments get it; the official
  service does not need it.

## Starting point (2026-11-10)

- **The tick is 250 ms** (`SimulationTiming.TicksPerSecond = 4`, DK2's own rate).
- **Click to world change is already slow in single-player.** A click waits for the next tick
  (125 ms on average). The draw clock then runs a tick behind (`DungeonRunner.cs` around line
  1010, ADR-0687, ADR-0694) so it can interpolate. Multiplayer adds the relay's turn close, the
  round trip and a jitter buffer: expect roughly 450 to 600 ms from click to world change.
  **Instant previews (phase 2) are therefore required, not polish.** Phase 0 measures the real
  numbers.
- **Interpolation exists** for creatures, shots, lights and the camera path. Headings snap.
- **Ticks run in batches on a worker thread**, catching up to a cap. Only a batch's first tick
  carries the pending commands. Game speed comes from two local settings.
- **Only tag tiles show at once** (`QueueTagPreview`, acknowledged when the tick has run). Slap
  and spell cursors animate at once. Pick-up, drop, room, door and trap placement show nothing
  until the tick; world sounds come from simulation events.
- **Possession has no human path in Unity.** The simulation installs any pose it is sent
  (`PossessionRules.Move`), so `PeerCommands` refuses `MovePossessedCreatureCommand` for now.
- **Computer keepers do not run in Unity.** Only the harness calls `KeeperAI.Think`. Their state
  (`KeeperAIState`, a private random generator per keeper) is outside the snapshot and the hash.
- **Determinism:** CI already proves Linux and Windows reach equal hashes under CoreCLR. Nothing
  compares the Unity IL2CPP build. The simulation uses no wall clock and no unordered iteration
  into hashed state. Two `[ThreadStatic]` scratch fields exist (`AnimationTimingRules`,
  `StateEntryLog`).

## Phase 0: measure (2026-11-10)

A `DK2.Tools` benchmark on the largest campaign maps with computer keepers playing, sampled at
several points up to a few thousand ticks:

- tick time: median, 99th percentile, worst; allocation per tick (a GC risk under IL2CPP);
- computer keeper `Think` time and allocation per keeper per tick;
- `WorldSnapshot` size (raw and compressed), write and read time;
- simulation-only ticks per second (rejoin catch-up speed);
- click-to-world-change latency in the Unity build today, the baseline phase 2 must beat.

Results go in this document. They size rejoin, snapshot transfer, the minimum machine spec and
the rollback decision.

## Phase 1: session layer, single-player first

1. `IGameSession`: `Submit(command)`, `TryGetFrame(tick, out InputFrame)`, the local player
   id, and session actions (pause, speed, handover, result screen). `LocalGameSession`
   implements it in process.
2. Every submitted command goes through `CommandCodec` and `PeerCommands.Read` with the local
   player as sender, so R1 to R4 run on every single-player command.
3. `DungeonRunner` submits instead of filling `_pendingCommands`, and takes frames from the
   session. The worker thread runs the ticks the session has frames for.
4. `RevealMapCommand` from the result screen becomes a session action.
5. **Computer keepers move into the session** and run on every machine:
   - `KeeperAIState`, each keeper's random generator and `KeeperStaging` join `WorldSnapshot`
     and the state hash (a `HashVersion` bump). An AI created by a handover seeds its
     generator from (match seed, player, tick).
   - **Canonical frame order:** human commands by player, then by sender sequence; then
     computer keeper commands by player. The journal records the merged frame.
   - **Failure isolation:** an exception in `Think` disables that keeper's AI on that tick. It
     fails identically everywhere, so the match goes on. The same applies to a command
     handler: the command is dropped and logged, never rethrown out of the tick. Phase 3's
     fuzzing then removes the causes.
6. **Handover test now, not in phase 3.** Swap a player to the computer keeper at tick T over
   the existing campaign replays. Define the handover's side effects: whatever is in the hand
   is dropped at the Heart, possession ends, a personality is chosen for a slot that had none.
7. Pause and game speed become session actions. In single-player they act at once.
8. The journal records the frames the session agreed on. Replays, relay journals and resyncs
   share one format.
9. **Simulated latency** (delay, jitter, loss) on `LocalGameSession`, the developer tool for
   phases 2 and 5.
10. **IL2CPP hash parity:** a Unity player build replays a journal and must report the CoreCLR
    hashes. Per release, in CI.

**Exit:** single-player plays as before; existing replays reproduce their hashes (after the
`HashVersion` bump); a campaign level runs with its computer keepers in Unity; a handover at any
tick keeps all replays deterministic; the IL2CPP build matches.

## Phase 2: instant acknowledgement

Generalise the tag-preview pattern into **pending-command previews**. Each submitted command
gets a presentation-only preview, keyed by the tick that will apply it. It is removed when that
tick has run, replaced by the real result or by a refusal cue.

- **Pick-up:** the creature or gold appears in the hand at once and is hidden at its world spot,
  with the pick-up sound.
- **Drop:** the thing appears at the drop spot with the drop animation started.
- **Room, door and trap:** a ghost of the room or object, with the build sound.
- **Slap and spells:** these already animate at once; add the sound at click time.
- **Refusals and conflicts:** the simulation emits a refusal event per refused command. A
  preview whose command was refused fades with the refusal sound. This also covers conflicts: two
  players grabbing one creature, or a room sold before a drop on it lands.

Previews never touch `WorldState`, so they cannot desync anything.

**Exit:** measured against phase 0's baseline, with two ticks of simulated latency, a playtest
cannot tell the difference from zero on pick-up, drop, tag, build and slap.

## Phase 3: loopback multiplayer, no Steam

An in-process relay and two to four sessions in one test process, joined by a fake transport
that adds latency, jitter, loss and reordering.

- **Agreement:** equal state hashes over long seeded matches, through every fault injected.
- **Late peer:** its commands land later, in their original order; the others never stall.
- **Departure and handover:** the relay announces the tick, every peer switches on it, and the
  hashes stay equal.
- **Rejoin:** a returning player loads a peer's snapshot, catches up, and must match the
  majority's next hash; control returns on an announced tick.
- **Consensus:** a peer forced to diverge is resynced by the majority; in two-player matches,
  repeated divergence ends the match as no contest.
- **Hostile peer (R3):** every allowed command tag is fuzzed against random world states and
  must be refused or applied without throwing, with equal hashes everywhere.
- **Snapshot fuzzing:** `WorldSnapshot.Read` gets the same treatment as the KWD readers, since
  rejoin makes it network-facing.

**Exit:** these run in CI as deterministic tests.

## Phase 4: the relay owns time

1. **The relay is the clock.** It closes a turn every tick and broadcasts the commit: turn
   number, the ordered frame, the input delay in effect, and any handovers.
2. **Per-sender sequence numbers.** The relay keeps each sender's commands in order. When one
   command misses its turn, every later command from that sender moves with it, so a drop can
   never overtake its pick-up.
3. **Adaptive input delay.** The smallest whole number of ticks covering the slowest peer's
   95th-percentile round trip, its reported execution lag and a jitter margin, with a minimum
   of one tick. It rises at once and falls only after a sustained improvement. Changes are
   announced two turns ahead and apply on the same tick everywhere.
4. **Deadlines.** A command arriving after its turn closed goes into the next open turn. The
   sender is told, and its preview stays until the real tick. The relay never waits.
5. **Client buffer.** Each client keeps a small buffer of committed turns (the existing draw
   lag) and plays slightly faster or slower to keep it at its target. At 4 Hz with
   interpolation this is invisible. It controls how far behind the display runs; it is not
   clock sync.
6. **A slow machine is not a slow link.** A client that cannot simulate at the relay's pace
   reports its lag. It is warned, and handed to the computer keeper if it falls a set number of
   turns behind. Phase 0 sets the minimum machine spec.
7. **Disconnects.** A disconnected keeper first gets a **caretaker** for a grace period
   (proposed 60 seconds): the computer keeper defends but does not sell, build or spend. After
   the grace period, the full computer keeper takes over. A returning player gets control back
   on an announced tick.
8. **Pause** is a request the relay grants from a small per-player budget, not an unlimited
   right.

**Exit:** with one peer on a 300 ms, 5 % loss link, the others' play is unaffected; that peer's
own clicks still acknowledge at once.

## Phase 5: possession

Build the human possession path with prediction designed in from the start.

- The camera and the possessed body move at frame rate from local input. The client sends its
  pose every tick, as DK2's packets do, and others see it interpolated.
- **The simulation validates every pose (R3)** before `MovePossessedCreatureCommand` returns to
  the allow-list: distance per tick within the creature's speed, walkable ground, no passing
  through walls, on the map. The Creature Teleport form is allowed only when the simulation's
  own state says a teleport happened.
- **Reconciliation:** when the simulation clamps a pose, the client blends to the confirmed pose
  over about 100 ms instead of snapping.
- **Attacks** start their animation and sound locally; the hit resolves in the simulation, at a
  pose that trails the local view by the input delay. Decide whether hits use the pose the
  attacker saw (lag compensation) or the simulated one.
- **Headings** are interpolated too.
- **Playtest gate:** first-person combat at 4 Hz with real latency must feel acceptable before
  this phase closes. If it does not, possession needs a finer tick or rollback.

**Exit:** possession with two ticks of simulated latency feels like zero latency, and a
speed-hack test client is clamped.

## Phase 6: Steam and the match lifecycle

- **Transport and lobby:** SDR transport, Steam lobby, ticket check (R7, R8). Lobby metadata is
  client-writable, so the relay checks every peer's build hash, `HashVersion`, map hash and
  settings itself before start.
- **Seed:** the relay picks the match seed after settings lock.
- **Start barrier:** the first tick waits until every peer has loaded, up to a timeout; a peer
  that never loads starts as a computer keeper.
- **Custom maps** through Steam Workshop (hash, size, moderation) rather than transfer between
  players.
- **Relay failure:** clients keep the latest agreed snapshot and journal, so a new relay instance
  can resume the match from them by consensus.
- **Desync diagnosis:** per-subsystem hashes (tiles, creatures, players, random state, navigation,
  computer keepers) at checkpoints. On a mismatch, clients upload a compressed per-subsystem diff
  and the journal, so a field desync can be reproduced.
- **Versions:** each release is a new simulation (`HashVersion` changes with most rules work). The
  lobby matches only identical builds; Steam updates force-restart lobbies; old journals are
  replayed only by the build that recorded them.
- **Rate limits and message caps** on the relay (R1).

## Multiplayer rules still to design

- **Victory, teams and fog settings** for multiplayer maps; what the result screen does, and
  whether `ContinueAfterResultCommand` and `SkipCameraPathCommand` mean anything in a match.
- **Alliances need consent.** Today one player's command makes both allied
  (`AllianceSystem.cs`). Check DK2's own rule first.
- **Slot lifecycle:** a defeated keeper becomes a spectator; a kicked player cannot rejoin; the
  match ends when no human remains.
- **Spectators, replays and multiplayer save/resume**, all possible once computer keepers are in
  the snapshot (phase 1).

## Rollback: decide after phase 0

Rollback predicts remote input and re-simulates when it arrives late. At 250 ms ticks with
previews, the strategy layer does not need it. Possession might: phase 5's playtest gate decides
whether it needs a finer tick or rollback. Rollback is affordable only if one tick plus one
snapshot restore costs a small fraction of a frame, which phase 0 measures. Either way it stays
inside the session layer.

## Risks

- **AI determinism and cost.** Every machine runs every computer keeper, so the AI must be as
  deterministic as the simulation and cheap enough for the minimum machine spec.
- **A handover into a dungeon the AI did not build** is the riskiest new behaviour; phase 1 tests
  it first.
- **IL2CPP versus CoreCLR.** A hash difference there makes consensus side against honest players.
- **The worker thread and the session.** The tick thread must wait for frames without blocking the
  main thread.
- **Local settings that change the simulation** (speed, cheats, debug commands) must not exist in
  a match. The session owns them.
