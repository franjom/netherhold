# Reaper keeper spells: creation and lifetime

Read 2026-10-04 from DKII.EXE v1.70, the shipped data, and independent private
Ghidra clones. Raw output stays in `reverse-engineering/.cache/`.

## 1. Descriptor and creation

**CONFIRMED.** `KeeperSpells.kwd` Summon Horny record1680: id5, location1,
target4, shot46, no bonus, data words0/0, mana100000, upkeep2000, cooldown0.
OneShotReaper record4928 uses the same shot with data1=99, mana0 and upkeep125.
Shot46 record10811 has process33, flags4, process flags128, health1,
speed2048 and radius819. Its callback is [004AFAF0](functions/004AFAF0.md),
installed by [004AD1B0](functions/004AD1B0.md); it runs at shot death.

The ordinary position cast pays once when the shot is made. Creature creation
can fail later without refunding that payment. The callback checks map bounds
and the navigation solid bit, obtains floor height, and creates the caster's
Reaper through [004B78D0](functions/004B78D0.md)/[004B7900](functions/004B7900.md).
The creature manager refuses when its class0 count is already448.

The selected type is the first loaded definition whose resolved id has IsHorny.
The level initializer performs that scan; unique definitions resolve to their
base id. See [0050B5B0](functions/0050B5B0.md), [00494360](functions/00494360.md),
[0050D220](functions/0050D220.md) and [0050D240](functions/0050D240.md).
Shipped Horny is id27, flags0xA00652B4, and is not a unique definition.

Data1=0 creates level10, enters235 and sets HornyTick `+4C9` to
now+50×variable19. It clears the physical combat-control bits after creation.
The nonzero-data branch writes low16(data1×variable19) to ReaperTimer `+4E9`,
sets IsOneShotReaper and chooses a different level; its build is Stage5.

## 2. Arrival and departure

**CONFIRMED.** The state age is a dword at creature `+26A` (state object `+1C`).
Accepted state changes reset it; the dispatcher increments it before the
category hook and handler, after its suspend gate. It is distinct from `+2F5`.

| State | Simulation consequence |
|---|---|
| 235 | Clears ReaperComplete; age1 immediately enters236. Other ages wait until12. |
| 236 | Age1 alpha0; adds32, saturating255. Only an already255 alpha enters237. |
| 237 | Age1 starts action46; its completion enables combat bits and enters238. |
| 242 | Age1 preserves action47, otherwise starts46. From age6×variable19, subtract32 alpha; zero sets health0 and the second entity sweep removes it. |

The four controllers are recorded at [00474740](functions/00474740.md),
[004747E0](functions/004747E0.md), [00474850](functions/00474850.md) and
[00474CC0](functions/00474CC0.md). Their flags5 include the unattackable bit.

Action46 has art35, mode0, no handler and duration0. Its plain start has cycle
count0 and looping playback: the first cycle flag or zero rate ends it at the
next action poll. Ordinary main wraps ignore the end segment; the unusual
start-to-main carry can still enter it. See [00496AA0](functions/00496AA0.md),
[004B3C10](functions/004B3C10.md) and their initializer records.
Art35 missing its Present flag selects fallback8. Shipped art35 has type6,
flags513, 121 frames at30FPS, no segments: duration16520, advance253 and
17 elapsed ticks. Its fallback is type0. A zero-FPS type6 resource is malformed,
with no audited finite-duration fallback. Ordinary arrival reaches238 on
handler turn28; a fresh departure alpha255 reaches zero at age31.

## 3. Resources and limits

**CONFIRMED.** [004BB8A0](functions/004BB8A0.md) refuses another Summon Horny
while any owned class0 creature has IsHorny, including arrival/departure.
[004BFB80](functions/004BFB80.md) finds the first such owned creature.
Normal upkeep and insufficient-mana departure require player status Playing,
IsOneShotReaper clear, raw state outside235/237/242, and HornyTick strictly
less than now. [004C17A0](functions/004C17A0.md) adds spell5's upkeep2000.
After payment, [004C10E0](functions/004C10E0.md) dismisses below2000 remaining;
equality passes. Dismissal [00491B70](functions/00491B70.md) enters242, disables
combat and clears movement. Entity updates precede the player payment, so
that departure's first handler runs next turn.

The category28 health request is not an HP drain: its negative delta is
refused for IsHorny by the health routine. See [0047A9B0](functions/0047A9B0.md)
and [004B2CE0](functions/004B2CE0.md). Normal creation assigns no expiry timer.
OneShotReaper is excluded from these upkeep/dismissal paths despite its125 field.
The campaign talisman count remains **UNKNOWN**, tracked only in Q447.

## 4. Build and verification boundary

**BUILT, STRONGLY INFERRED mapping.** Stage4 exposes spell5 with data1=0:
creation, arrival235/236/237, upkeep and departure242, using the integer clock.
**Implementation:** `ReaperRules`, `ReaperSystem` and `ReaperRemovalSystem`.
The shared arm's nonzero-data casts remain refused until Stage5. Hash/save173
preserves the lifecycle fields and corrected constructor defaults; pin `901D3950F7F8504D`.
Constructor [004872A0](functions/004872A0.md) initializes combat bits on;
the summon callback clears them, leaving authored Horny's fight acquisition enabled.

Full Horny idle/wander/Chicken/forced-target controllers remain explicitly
UnbuiltHandler/ResearchOnly; see `special-role-idle.md`. This build does not
claim a complete playable Horny AI. Height, visual effects and sounds retain
the existing presentation boundary. General animation reuse/bypass cases
outside ordinary fresh Reaper action installation are not newly modeled.
The complete no-install gate passed3375 tests, zero failures and395 skips;
Release and Unity compilation have zero warnings/errors. All42 scenario runs
preserve stage0, Stage3 and initial Stage4 event/world artifacts:94 files identical,
84 changed only in hash series/report hashes from version173.
Initial independent review requested two corrections;
the constructor-default correction awaits verification and the Q448 correction follows separately.
