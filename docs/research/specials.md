# Special objects

## Increase Level (object 117)

**CONFIRMED (binary and original Level 1 replay, 2026-09-27).** Packet action 88 activates a selected object whose definition has the Special type flag. The packet carries its original entity slot, which must be mapped to a simulation object instance. The handler dispatches on subtype and destroys the object after activation. For subtype 117, it walks the activating player's creatures, raises each one level through the usual level increment, then heals each to its new maximum through the health routine. Effect, sound, and notification work remains presentation work.

The original Level 1 packet activates slot 1128 at turn 1708. The level's sole placed Special is subtype 117 at (12,13), so slot 1128 → our object 1 is **STRONGLY INFERRED**. The read-only creature capture shows all seven keeper Goblins at 1875 health on turn 1708; one had 890 on turn 1707. This confirms the full heal as well as the 1500 → 1875 level-2 maximum. The replay map is `build/verification/level1-observed-entity-map.csv` (local, ignored).

With this activation mapped, the headless Level 1 replay kills the Lord at turn 1846 and reaches player status 2 (Won) at tick 1872, the original win tick (Q444). Other combat timing still differs: the first Goblin arrives at turn 672 here versus 753 in DK2 (`navigation-captures.md`).

Other Special subtypes remain **UNKNOWN** here and are left unimplemented.
