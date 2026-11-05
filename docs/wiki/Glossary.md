# Glossary

Terms used across this wiki and the repository. Where a term has a home in the
repository, the entry links to it.

## Confidence and status

**The confidence scale** (ADR-0642) is used everywhere: research notes,
function records and the `[Dk2Behavior]` attribute in code.

| Level | Means |
|---|---|
| **CONFIRMED** | Seen in the running game: an owner observation, a stopwatch, a native capture. Memory of playing the game does not count. |
| **STRONGLY INFERRED** | Read in the binary or in the shipped data. A binary read never rises above this (ADR-0046). |
| **HYPOTHESIS** | Fits the evidence, with a step nobody has read. |
| **UNKNOWN** | DK2's answer is not known. |
| **DISPUTED** | Function records only: two readings conflict. |

The results of our own runs are *measured*, not CONFIRMED.

**Function record status** (ADR-0660): how far our code mirrors one function of `DKII.EXE`.

| Status | Means |
|---|---|
| **READ** | Read in the binary, not mirrored. |
| **PARTIAL** | Our code mirrors part of it; an `implements:` line names that code. |
| **BUILT** | Our code mirrors all of it; an `implements:` line names that code. |
| **SUPERSEDED** | Not a function, or folded into another record. |

**ADR status.** `proposed`, `accepted`, `amended`, `superseded` or
`withdrawn`. An accepted ADR stays `accepted` when its code lands. What is
built shows in the code that cites it, which the
[[Status Matrix|Status-Matrix]] counts.

## Project terms

- **ADR**: an architecture decision record, numbered `ADR-NNNN`, in
  [`DECISIONS.md`](https://github.com/franjom/netherhold/blob/master/docs/DECISIONS.md).
  Every deliberate choice, including every difference from DK2, has one.
- **Q-number**: a row in
  [`open-questions.md`](https://github.com/franjom/netherhold/blob/master/docs/research/open-questions.md),
  e.g. Q41. See [[Open Questions|Open-Questions]].
- **Function record** (KB record): one file per `DKII.EXE` function in
  [`docs/research/functions/`](https://github.com/franjom/netherhold/tree/master/docs/research/functions),
  named by its address, with its role, confidence, status, evidence and history.
  See [[KB Coverage|KB-Coverage]].
- **Research note**: a document in
  [`docs/research/`](https://github.com/franjom/netherhold/tree/master/docs/research)
  on one mechanic, with its sources and confidence.
- **System**: one entry of the closed list in
  [`systems.yaml`](https://github.com/franjom/netherhold/blob/master/docs/systems.yaml)
  (imps and tile work, navigation, rooms and so on). The wiki is indexed by systems.
- **Tick**: one step of the simulation. A tick is 250 ms, four a second, which
  is DK2's own rate (ADR-0089). `GameTick` counts them.
- **`Fp32`**: DK2's 32-bit fixed-point number, a raw integer over 4096. Gameplay
  maths stays in integers; floats are for display only (ADR-0001).
- **`SimRandom`**: the simulation's only source of randomness, explicitly
  seeded. Its generator is pluggable; one option is DK2's own (ADR-0275).
- **`WorldState`**: all simulation state. Everything that affects a run is
  reachable from it, hashed and saved.
- **State hash**: a 64-bit hash of `WorldState` and the random generator's
  position, pinned by a test on Linux and Windows (ADR-0006). See [[Determinism]].
- **`HashVersion`**: a number that goes up whenever a change deliberately alters
  simulation state, together with the pinned hash.
- **`InputFrame` / `PlayerCommand`**: what goes into one tick. A human, a
  scenario, the computer keeper or an outside bot all play by submitting commands.
- **`GameDatabase`**: the game's balance data, read from the shipped files.
  Every number the simulation uses comes from it.
- **Scenario**: a YAML file under
  [`scenarios/`](https://github.com/franjom/netherhold/tree/master/scenarios)
  that the headless harness runs; most assert an expected outcome.
- **Install-gated test**: a test that needs your DK2 installation (`DK2_DIR`).
  Without it the test is reported as skipped, never as passed (ADR-0100).
- **Native capture**: a recording taken from the original game while it runs:
  memory, packets, route or world logs. Captures are kept outside the repository.
- **Packet replay**: a session recorded in DK2, replayed headlessly in our
  simulation so the two outcomes can be compared.

## DK2 files

- **`DKII.EXE`**: the original game's executable, v1.70. It is the primary
  source, read in Ghidra (see [[Legal and Clean Room|Legal-and-Clean-Room]]).
- **KWD / KLD**: the shipped data files. `.kwd` holds global records (creatures,
  terrain, rooms, spells, variables); `.kld` holds one level's map, players,
  things, triggers and variables. Both are chunked records that
  [`DK2.Data`](https://github.com/franjom/netherhold/blob/master/src/DK2.Data/README.md)
  reads and writes back byte for byte.
- **KMF**: a 3D model file. **WAD**: an archive of shipped files, such as
  `Meshes.WAD` (the models) and `Paths.WAD` (the camera paths).
- **Variables**: named balance values (`CLAIM_TILE_HEALTH`, the seconds-to-ticks
  multiplier...) in `GlobalVariables.kwd`, which each level can override.

## DK2 game terms

- **Keeper**: a player, human or computer. Each owns a dungeon.
- **Dungeon Heart**: the keeper's core room. Losing it loses the level.
- **Imp**: the keeper's worker. Imps dig, claim tiles, reinforce walls, mine
  gold and carry things (see [[Imps and Tile Work|Imps-and-Tile-Work]]).
- **Claimed tile**: floor a keeper owns. Rooms are built on claimed tiles, and
  territory spreads from them.
- **Reinforced wall**: a wall an imp has strengthened, which the enemy must
  break through.
- **Portal**: the room creatures arrive through, attracted by the keeper's rooms.
- **Hand of Evil**: the keeper's cursor. It picks up, drops and slaps creatures
  and objects.
- **Possession**: a keeper spell that lets the player control one creature directly.
- **Hero**: the enemy faction of the campaign. Heroes arrive in parties,
  some through a Hero Gate.
- **Mana**: the keeper's spell currency. Spells cost it to cast, and some
  cost it to keep up.
- **State id**: the number DK2 gives what a creature is doing (idle, fighting,
  sleeping...). Our creatures hold DK2's own state ids.
