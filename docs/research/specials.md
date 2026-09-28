# Special objects

## Increase Level (object 117)

**STRONGLY INFERRED (binary, rechecked 2026-09-28).** Action 88 at
`0x00513760` checks a live entity, object category 2, and definition Special
flag `+0xF4 & 4`; it calls `0x004A3FC0` for the command's player, then destroys
the object. Neither this handler nor the subtype-117 arm (`0x004A42C0`) checks
land ownership, distance or reach. Do not add those checks to the command.
Human selection eligibility remains **UNKNOWN** here; the AI's separate
selector is in `computer-keeper-management.md` section 3.

Subtype 117 walks the activating player's creature list, increments level,
and applies the new maximum health through the health routine. Effect, sound,
notification and the player's activation counter remain unimplemented.
The positive-health Increase Level result below is **CONFIRMED** by capture;
that does not confirm every admission rule or creature-state edge case.

The original Level 1 packet activates slot 1128 at turn 1708. The level's sole placed Special is subtype 117 at (12,13), so slot 1128 → our object 1 is **STRONGLY INFERRED**. The read-only creature capture shows all seven keeper Goblins at 1875 health on turn 1708; one had 890 on turn 1707. This confirms the full heal as well as the 1500 → 1875 level-2 maximum. The replay map is `build/verification/level1-observed-entity-map.csv` (local, ignored).

Replay timing and remaining differences: `navigation-captures.md`,
"Level 1 winning packet" (Q444).

Other Special subtypes remain **UNKNOWN** here and are left unimplemented.
