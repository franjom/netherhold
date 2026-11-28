---
systems: [tooling-process, rooms, creatures, doors-traps]
---
# OpenKeeper PR relevance: merged 1296 to 1363, and the behavioural open PRs above 1161

Reviewed 2026-11-20. This continues the 1162-1295 merged audit.
It covers every **merged** OpenKeeper PR numbered 1296 to 1363 (11 PRs) and 56 **open** PRs picked for
their behavioural claims. PR links are provenance for leads, not proof of DK2 behaviour, and no
OpenKeeper code was copied. No code was changed, no test was run and no native experiment was
played. The docs and function records the corrections name were fixed the same day; the code changes are made and tracked by the task that aligns them, not here.

## How each verdict was reached

Four read-only reviewers split the PRs by theme (A combat and spells, B rules and messages,
C small rules and kill attribution, D animation and effects). Each verdict is tagged:

- **Ghidra**: the reviewer re-read the function in DKII.EXE and the result is in the list below.
- **grep**: the reviewer only checked that our docs or function records have a matching entry.
  Read those as triage, not as a check of the binary, and not as a comparison of content.

Group B's "covered" verdicts are mostly grep, and its batch PRs (#1314 to #1327, #1347) were
sampled, not read line by line. The belgianguy PRs cite GDB and Wine captures that are not in
the PRs, so their claims are leads until the binary is read.

## Corrections to our docs and code

1. **Poison Spit swaps its two values ([#1271](https://github.com/tonihele/OpenKeeper/pull/1271)).**
   Ghidra, STRONGLY INFERRED. [0x004B0270] hands shot `+0x13D` (ShotData1) and `+0x141` (ShotData2)
   to [0x0048CC50], which stores them at creature `+0x45D` and `+0x45F`; [0x0048E160] counts
   `+0x45D` down by 1 a tick and applies `+0x45F` as damage a tick. So Data1 is the **timer** (200
   ticks) and Data2 the **damage per tick** (10). `creature-status-effects.md` section 2 says
   10 ticks of 200, and `ShotRules.cs:578` passes `shot.Data2, shot.Data1` into
   `StatusEffectRules.Poison(ticks, amount)`, so the Maiden's poison is built the wrong way
   round. A code fix is a simulation change (hash version and Q-number rules apply). Not made.
2. **Electric Chair kills what it cannot electrocute ([#1304](https://github.com/tonihele/OpenKeeper/pull/1304)).**
   Ghidra, CONFIRMED by the decompile (the floor-slot predicate disassembly was not read). In
   states 131 ([0x00484590]) and 136 ([0x00484CE0]) a creature on a type 0x3D device whose
   definition `+0x686` bit `0x80000` is clear is killed through creature vtable slot `+0x20`
   (0x004B2AD0, which sets health to 0; slot read from bytes at `0x66D544`). Our `TortureRules.Holds`
   docs and `state-category-hooks.md` section 3.3 say the chair "turns away" the creature. The
   Imp and Firefly lack the flag, so the death looks intended. Check the callers of `Holds` first.
3. **Wall deterioration runs per tick ([#1315](https://github.com/tonihele/OpenKeeper/pull/1315)).**
   Ghidra, STRONGLY INFERRED. [0x004BB6D0] loops variable 145 times per call with no time gate,
   and its only caller [0x004BB2A0] calls it every tick it runs. Built as `WallDeteriorationRules`
   (HashVersion 534); the damage is a signed byte through Map_ApplyTileWork, per the
   [0x004BB6D0](functions/004BB6D0.md) record. [0x004B9EC0] is Player_Tick, not a damage routine.
4. **Mana cap order ([#1316](https://github.com/tonihele/OpenKeeper/pull/1316)).** Ghidra, STRONGLY
   INFERRED. In [0x004C10E0] the [0x004C17A0] result is added to the gain, the sum is capped at
   `+0x10DE`, then [0x004C17A0] is subtracted again, so those upkeep terms are charged only for the
   part past the cap. `ManaRules.ApplyOneSecond` caps the gain alone and subtracts the whole upkeep
   afterwards, which differs for the possession, Call to Arms and Reaper terms. [0x004C17A0] itself
   was not re-read; the Call to Arms and Reaper terms come from its record.
5. **Vampire return ([#1347](https://github.com/tonihele/OpenKeeper/pull/1347)).** Ghidra, STRONGLY
   INFERRED, and it matches our record, not the PR. [0x004C6460] picks a **random** matching
   Graveyard, not the first, and [0x0048FFC0] sets health to the old level's maximum before the
   level drop, so health ends above the new maximum.
6. **Stale prose, code right ([#1326](https://github.com/tonihele/OpenKeeper/pull/1326)).** Ghidra,
   STRONGLY INFERRED. The Prison hook [0x0047B7C0] and Casino hook [0x0047AD70] reload a byte counter
   when it reaches zero, giving periods of **17** and **5** ticks. `HookFires` already does this;
   `prison.md` section 4b still says "-12 every 16 ticks".
7. **Reaper heart wording ([#1313](https://github.com/tonihele/OpenKeeper/pull/1313), [#1342](https://github.com/tonihele/OpenKeeper/pull/1342)).**
   Ghidra, STRONGLY INFERRED. [0x0045E1B0] tests object id `0x5E` in type-5 rooms without ruin bit
   `0x9A&1`; the Dungeon Heart object is 13 (`boulder.md`). The PR's "heart object in each room" and its
   health-above-0 check are not what the binary does. Our records already match the binary.

## Gaps in our records (the binary agrees with the PR)

All Ghidra, STRONGLY INFERRED unless stated.

- **Wind ([#1242](https://github.com/tonihele/OpenKeeper/pull/1242)).** [0x004AE990]: tiles from
  tile-R to tile+R-1; creatures closer than R, skipping the shot's own tag; heading += R * `0x600000`
  / (dist + R * `0x1000`); a creature of another owner takes hp * (dist + R * 4096) / (R * `0x2000`) *
  data2 / 100 and, unless in state `0xD2`, `0x107` or `0x108`, is kicked to `0xD2`. Our record is
  HYPOTHESIS and has none of the formulas.
- **Raise Dead ([#1243](https://github.com/tonihele/OpenKeeper/pull/1243)).** No record of
  [0x004AE880]: it makes a creature of type (shot `+0x13D`) for the shot's owner from every class 6
  thing on the tile, plays effect `0x144` and removes the body, with no owner test; the caster's own
  target scan takes only neutral-owned bodies ([combat-acquisition.md](combat-acquisition.md)).
- **Speed change ([#1223](https://github.com/tonihele/OpenKeeper/pull/1223)).** No records of
  [0x004AE000] and [0x0048CAD0]. Percent is shot `+0x13D`, seconds `+0x141`; refused while `+0x459`
  is non-zero; clamped to [def `+0x5B4` >> 2, variable 19 << 12]. The cap is not "one tile a
  tick".
- **Slap ([#1188](https://github.com/tonihele/OpenKeeper/pull/1188)).** Covered: [0x0048B8A0] sets
  150 % speed for 400 ticks (variable 19 * 100) when `+0x459` is 0.
- **Hover wobble ([#1329](https://github.com/tonihele/OpenKeeper/pull/1329)).** Covered by
  [0x004B4430]: a hovered special (def `+0xF4` bit 2) gets each angle set to base +/- 25. Nothing in
  `src/` mirrors it. What clears the stored base was not found.
- **Idle star ring ([#1333](https://github.com/tonihele/OpenKeeper/pull/1333)).** [0x004A2C00]
  emits effect 204 every 16 ticks and is suppressed over Lava on one arm. Our record is HYPOTHESIS, sits
  under `prison-rats.md`, and does not say process 36 is the special box.
- **Trap idle animation ([#1360](https://github.com/tonihele/OpenKeeper/pull/1360)).** No record.
  Trap_Update step 8 (0x004A8C6B) sets no playback rate itself (HYPOTHESIS that it comes from the
  shared timing).
- **Not in any doc or record, not read:** worker drag descriptor `0x13` (#1336), the Fear Trap eye
  streams (#1362), `START_AS_DYING` and `WILL_BE_ATTACKED` (#1351, #1353), `SHOW_HERO_KILLS`
  (#1355), the Workshop-warning flash (#1363), the Library stored draw offset and scale (#1337,
  Q813), Heal refused at full health ([0x004075E0], #1327), dungeon breach events (#1320) and the
  ONE_SHOT_HORNY level list (#1321).

## Disputed

- **Kill tallies ([#1215](https://github.com/tonihele/OpenKeeper/pull/1215), [#1256](https://github.com/tonihele/OpenKeeper/pull/1256)).**
  Ghidra, STRONGLY INFERRED. [0x00487C70] credits the killer from tag `+0x36` or the first
  attacker's owner; IsGood victims add to `+0x14A`; otherwise a kill of another keeper's creature
  adds to `+0x146`; `+0x19E[owner]` tallies every credited, counted death and `+0x173` every counted
  death. Condition 23 ([0x004C7DF0]) reads `+0x146` for selector 0, else `+0x19E + 2n`. #1215 says
  the type-23 total "excludes workers"; settled, the binary has no worker test: the only definition
  test is IsGood (bit 8, `0x004882D0`) and the sole caller `0x004B7A36` is ungated. What the total
  excludes is IsGood victims and own-creature kills. Conditions 22 and 23 are built (Q276,
  HashVersion 534).

## Covered, or no DK2 claim

Covered by an existing record (grep unless noted): #1177, #1180, #1199, #1219, #1224, #1260,
#1263, #1265, #1276, #1298, #1299, #1301, #1302, #1303, #1309, #1318, #1321, #1322, #1323,
#1344, #1345, #1361, #1332. Ghidra also agreed on #1223, #1188, #1300 (category mask `0x801A3F7B`
has no bit 28, and states 235 to 242 are category 28), #1319 (the weight table at `0x69F300`:
1000, 40, 50, 5, 5, 1, 2, 5, 20, 40, 50, 500) and #1303.
No DK2 claim: #1209, #1348, #1338, #1184, #1185, #1281, #1212 and #1255 (animation cadence unmeasured).

## Unconfirmed

Records exist but their content was not compared with the PR: #1203, #1314, #1317, #1324. Not read
at all: #1289 (Skeleton Army timing), #1343 (48-turn trail), #1340 and #1341 (movement edge rule
[0x004C8BD0], [0x004B2030]).

## Owner questions

- Does a Maiden-poisoned creature lose 10 health a tick for 200 ticks in the original? The binary
  says yes; our simulation does the opposite.
- Possession seems to cost no mana while income is far under the cap. `keeper-spell-possession.md`
  records an unexplained no-drain observation; the mana-cap order in correction 4 may explain it.

## Limits

Not reviewed: the rendering and engine PRs of adrian-andrzjewski (#1182, #1198, #1278, #1279,
#1305, #1310, #1311, #1312, #1325, #1335), #1308 (computer player), #1346 (project split) and #1349.
The PRs' own measurements were not checked, and no replay outcome, simulation hash or gameplay
acceptance changed. The marker `tools/openkeeper-review/last-reviewed.txt` stays at 1161 because
the open PRs below 1363 above are not all reviewed and the hook compares numbers only.
