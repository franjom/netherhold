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

The chosen level is low8(global `0x006F295C`), loaded from the LEVEL body byte
`+0x6403` through configuration `+0x6537`. See [0056C100](functions/0056C100.md)
and [0051E6D0](functions/0051E6D0.md). The creation branch calls ordinary
level-up target-minus-one times; selected levels0/1 perform no grants.

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

**CONFIRMED.** A successful spell14 position cast sets player `+0x76` bit0x1000
after the shot, payment and clocks; its permission refuses that bit. Only spell5
tests the owned-Reaper restriction, so a timed Reaper can coexist with a normal
one. The first owned Reaper controls upkeep; a newer timed Reaper can therefore
suppress normal upkeep until removal. Initialization clears the used bit.
See [004BBD30](functions/004BBD30.md) and [004B8640](functions/004B8640.md).
The scoped direct-offset/bit scan, including cseg, found the cast setter,
permission/interface readers and initialization clear. Examined expiry/removal
bodies do not reset it; indirect writes and save serialization were not exhaustively
audited. Interface readers are [00424840](functions/00424840.md),
[00424EC0](functions/00424EC0.md) and [00424FD0](functions/00424FD0.md).

The creature-update prefix [00488930](functions/00488930.md) tests IsHorny
and the nonzero timer word, decrements it, and requests242 at zero. It ignores
the state-change result and clears combat/movement either way. This precedes
held/down/suspension guards and owner result does not pause it. Countdown does
not require IsOneShotReaper; an initially zero timer stays zero. Accepted expiry
runs departure age1 that same turn, without an immediate health change. The
ordinary99-second variant spends396 creature updates, then removes30 updates
after expiry through the shared fade.
SetState's InHand guard is mirrored locally by the Reaper state request; the
general omitted-guard audit remains Q503. The hand adapter's retained state1
differs from original state76 (ADR-0209), so this does not claim that broader
state integration has been completed.

## 4. Build and verification boundary

**BUILT, STRONGLY INFERRED mapping.** Stages4/5 expose spell5 and spell14:
creation, arrival235/236/237, upkeep and departure242, using the integer clock.
**Implementation:** `ReaperRules`, `ReaperSystem` and `ReaperRemovalSystem`.
The shared nonzero-data arm uses LEVEL metadata, the timer and variant flag;
successful spell14 casts retain a player used flag across actor removal.
Missing extended LEVEL metadata is an explicit adapter refusal, rather than a
guessed level. The original legacy-layout fallback has not been established.
The existing shot allocator has no allocation-failure boundary; creature-pool
failure after a successful cast still spends the use. Hash/save174 preserves
the player flag and prior lifecycle state; pin `8213FCEC0D1B3EA0`.
Constructor [004872A0](functions/004872A0.md) initializes combat bits on;
the summon callback clears them, leaving authored Horny's fight acquisition enabled.

Full Horny idle/wander/Chicken/forced-target controllers remain explicitly
UnbuiltHandler/ResearchOnly; see `special-role-idle.md`. This build does not
claim a complete playable Horny AI. Height, visual effects and sounds retain
the existing presentation boundary. General animation reuse/bypass cases
outside ordinary fresh Reaper action installation are not newly modeled.
The complete Stage5 no-install gate passed3407 tests, zero failures and396 skips;
Release and Unity compilation have zero warnings/errors. All42 Stage5 scenarios
preserve stage0 and corrected Stage4 event/world artifacts:94 files identical,
84 changed only in hash series/report hashes from version174.
Stage4's two corrections are independently approved. Stage5 review is pending.
