---
systems: [tooling-process, rooms, creatures, doors-traps]
---
# OpenKeeper PR relevance: 68 merged PRs, 1117 through 1295

Reviewed 2026-11-18. This covers the 68 **merged** OpenKeeper PRs numbered 1117 to 1295, in the
form of [the 1113-1161 audit](openkeeper-pr-audit-1113-1161.md). The open PRs above 1161 are
**not reviewed**; the marker `tools/openkeeper-review/last-reviewed.txt` reads 1161 for that
reason. PR links are provenance for leads, not proof of DK2 behaviour, and no OpenKeeper code
was copied. No code was changed, no test was run and no native experiment was played.

## How each verdict was reached

Items 1 to 4 below were re-read in Ghidra by a reviewer; their function records carry the
evidence and the confidence (STRONGLY INFERRED). Every other verdict, including "no DK2 claim"
and "covered", came from grepping the PR text against our docs and function-record headers, not
from Ghidra. Read those as triage, not as a check of the binary.

## Relevance of the 68

| Verdict | PRs | Meaning |
|---|---|---|
| No DK2 claim | 17 | Port, build, UI, editor or asset changes that assert nothing about DKII.EXE |
| Covered by existing records | about 48 | The claim matches a record or research section we already have |
| Correction to our docs | 2 | #1218, #1200 (below) |
| Gap in our docs only | 2 | #1170 (effect ids), #1164 (the 'width' note) |
| Unconfirmed | 1 | #1201 (state 140) |

The counts are the reviewer's triage and do not sum to 68 exactly: "about 48" is an estimate.

## Re-read in Ghidra

1. **Bridge mana ([#1218](https://github.com/tonihele/OpenKeeper/pull/1218)).** A correction.
   [0x00455B90](functions/00455B90.md) credits the player the tile is tagged to at that moment, and
   a room tile is owned before its terrain is written, so a bridge built as a room tile gives the
   building keeper +1 mana/s; our docs said neutral. Corrected in [mana.md](mana.md),
   the record, Q521 and the ADR-0334 amendment. The sim does not yet mirror the +1 (not yet
   built). The PR's 11/s-versus-4/s level-8 figure could not be derived, and that terrains 17 and
   26 carry the Room flag was not read.
2. **XP loss while not training ([#1200](https://github.com/tonihele/OpenKeeper/pull/1200)).** A
   correction. [0x00485880](functions/00485880.md) runs once per update (call at `0x00489584`), the
   variable is applied raw, and in [0x00457880](functions/00457880.md) a loss larger than the
   balance clamps it to zero, where our docs said no-op and per second. Corrected in
   [training.md](training.md) section 7, [creature-levels.md](creature-levels.md) and
   [balance-variables-in-the-binary.md](balance-variables-in-the-binary.md). The shipped value
   is 0, so only custom levels see it (Q203 stands). `CreatureLevelRules.Grant` still treats an
   oversized loss as a no-op; not yet fixed.
3. **Trap and door death effects ([#1170](https://github.com/tonihele/OpenKeeper/pull/1170)).** A
   gap, filled: the effect ids and file offsets (`0x232` doors, `0x1F8` traps) are in
   [door-blocking.md](door-blocking.md) section 6 and [traps.md](traps.md) section 3. Effects are
   unbuilt.
4. **Creature 'width' ([#1164](https://github.com/tonihele/OpenKeeper/pull/1164)).** A note only.
   Staging `+0x1290` is file `0xEA0` (offset confirmed); whether `def+0x63A` is a radius or a
   full width, and its units, are unconfirmed (HYPOTHESIS). History lines on
   [0x00492320](functions/00492320.md) and [0x0048F690](functions/0048F690.md).
5. **Combat Pit state 140 ([#1201](https://github.com/tonihele/OpenKeeper/pull/1201)).** No
   correction. [0x00457CF0](functions/00457CF0.md) has one data xref, from the state-table
   initialiser at `0x0047E0AA`, and no caller, as [combat-pit.md](combat-pit.md) section 1 says.
   Q431 stays open.

## Leads left open

No Ghidra read was made for these; each is a lead, not a finding.

- #1197: the mentor message `0x41` text for 'risen as a Skeleton' has no record.
- #1181: neutral-claiming versus door-blocked senses has no record; it may be OpenKeeper-only.
- #1268: side details of `0x00463A10` and `0x00476D30` are unrecorded (a non-hungry creature
  still consumes the chicken; eating interrupts only some state groups).
- #1135: the records for `0x00486F80` and `0x00481610` were not checked.
- #1134: the possessed -64 and frozen 160 terms of `0x004B2CE0` were not compared with
  `StatusEffectRules`.
- #1123: the three dig slots per side are not confirmed.

## Limits

Only merged PRs 1117 to 1295 were reviewed. The 65 open PRs above 1161 were not. The PRs' own
measurements were not checked, and no replay outcome, simulation hash or gameplay acceptance
changed.
