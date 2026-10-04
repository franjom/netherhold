# Turncoat: temporary combat allegiance

Binary read in Ghidra 12.1.4/PyGhidra, 2026-10-04. Instruction and shipped-data
claims below are **CONFIRMED**; gameplay equivalence remains **STRONGLY INFERRED**
without an original-game cast trace. Implementation: `TurncoatRules`, ADR-0350.

## 1. Descriptor and cast

Shipped `KeeperSpells.kwd` record 7 selects location 1, target 2 and shot 31.
Normal/upgraded data1 is 25/40, data2 zero; cost 20000, cooldown raw 245760.
The existing creature permission and cast path applies, including forced first
collision and payment even when the eventual target is immune. `Shots.kwd`
shot 31 has speed 2048, radius 819, health 1, flags 6, collision 2, damage 0,
process 13 and process-event mask 4. The process mapping is
[004AD1B0](functions/004AD1B0.md); the complete effect is
[004AE550](functions/004AE550.md).

The process writes a narrowed unsigned-word countdown and the caster's tag.
It requests idle only from resolved Combat category, through the ordinary
state hooks. Other jobs, room claims, party membership and ownership rosters
remain attached. It does not call the permanent ownership setter.

## 2. Timer and combat consumers

[0048E160](functions/0048E160.md) decrements the countdown before action and
fight processing ([00488930](functions/00488930.md)). At zero it preserves
the caster tag, requests no state and performs no cleanup. The timer gates
the override, so ordinary combat selection returns to the actual owner.
An existing fight continues without an allegiance revalidation
([0045F030](functions/0045F030.md)). Successful death-state entry clears
both words ([00459560](functions/00459560.md), [004596D0](functions/004596D0.md)).

The five hostile selectors use effective combat sides for self and candidate,
but keep actual-owner roster traversal. The NonFighter's Heart defence,
hero-specific branch and strength counts retain actual owners
([0045BDF0](functions/0045BDF0.md)). An active caster skips trap, Heart and
friendly-spell scans ([0045CB00](functions/0045CB00.md)); the friendly scan's
candidate roster otherwise stays tied to actual ownership. Fear and room/job
ownership are not globally reassigned.

Enemy shot collision tests actual-owner hostility **or** an active Turncoat
timer, including the latter branch after the normal dying refusal
([004B5800](functions/004B5800.md)). It must not use effective combat hostility.
The actual owner's ordinary hand permission refuses an active timer; the
owned-pit exception runs first ([0048E6A0](functions/0048E6A0.md)).

## 3. Implementation and verification boundary

Both unsigned words are hashed and saved with HashVersion/FormatVersion 171.
Tests cover normal/upgraded casts, overwrite and word narrowing, immunity,
combat leave hooks, preserved job/room/party/owner roster, death cleanup,
same-turn selection at expiration, pickup permission, a stopped combat walk
and snapshot continuation. Independent
verification is pending.

Disguise remains unbuilt: its watcher and selector checks use effective sides
([0048EE90](functions/0048EE90.md), [0045C690](functions/0045C690.md)). This
stage changes existing hostile selection and scan gates; it does not introduce
a disguise controller. Existing global creature iteration and tick ordering
limits remain documented in `creature-ownership.md` and `creature-status-effects.md`.
Listing censuses establish explicit instruction
accesses, not the absence of indirect/bulk accesses.

## 4. Regression comparison

Stage0 baseline: `../plans/2026-10-03-keeper-spells-baseline.md`.
All 42 scenarios retain byte-identical event logs and final world output;
all ten keeper command traces are also identical. All 42 hash series and
their reports change only through version/composition, matching the prediction.
Artifacts: `build/keeper-spells-turncoat-a` (178 files: 94 identical, 84 changed).
The final repeat in `build/keeper-spells-turncoat-final` is byte-identical to it.
Level 1/2 seed packets remain unavailable, as recorded in the baseline.
