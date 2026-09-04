# Research status

Snapshot of what is known vs unknown. Authoritative long-form:
`DK2_REIMPLEMENTATION_PLAN.md` §3 (full system-by-system table).
Live question list: `docs/research/open-questions.md`.
What is *built* rather than known, against the long-term goals:
`docs/GOAL_ALIGNMENT.md`.

## Solved (usable today)
- Terrain (41 types), rooms (26 types) and the level tile grid: parsed and
  cross-validated; the dig/claim terrain transition graph is data. See
  docs/research/kwd-terrain-rooms.md.
- Creature definitions: all 46 records parsed (stats, flags, job tables,
  attractions, spells, resistances) — cross-validated against Flame. See
  docs/research/kwd-creatures.md and creature-table-dump.md.
- File formats: WAD, KWD/KLD (~90%), KMF, STR, textures, video — via
  OpenKeeper + dk2-tools; record header verified locally (kwd-container.md).
- Game database: DK2 balance data is KWD `VARIABLES`/`CREATURES`/... records,
  shipped standalone in `Data/editor/*.kwd` on the GOG release.
- Structures: Flame's v1.70 map — 868 structs, 10,548 globals, 7,282 fields.
- Level scripting: all 51 trigger action types enumerated.
- Where gold is: DK2 has no player-gold variable, only four per-place limits.
  Gold lives in tiles. See docs/research/rooms-and-gold.md and ADR-0015.
- Where a room may go: the ROOMS record's own placeable-on flags decide, and
  they partition the shipped rooms exactly as the game plays.
- Where mana comes from: owned land plus a flat 30 for a keeper's dungeon heart.
  Three terrain types out of 41 carry a mana gain and they are exactly the
  ownable non-room ones. The ceiling is a flat 200000 for every keeper, and a
  keeper has exactly one heart whose loss is defeat (both observed in the
  original). See docs/research/mana.md.

- Where mined gold goes: seam → the imp's hands → a treasury tile, and what
  happens when there is nowhere to put it. Q31, ADR-0018/0019, from the owner's
  observation. The one loose end is the floor lump DK2 leaves when every store is
  full, which needs the OBJECTS record (M6).
- How a tile is worked: one uniform data-driven rule for all ten work kinds, at
  **five applications a second** (Q47, measured at 273 gold a swing, ADR-0020),
  worked **from beside a wall and standing on a floor tile** (Q44/Q49, ADR-0028).
- **How DK2 draws its walls** — as far as anyone outside EA knows. Solid mass
  with intersections cut into it, wall pieces chosen by the size and shape of the
  room behind them, damage as a texture swap, and runtime vertex deformation
  (the last inferred rather than proven by its source). See
  docs/research/wall-rendering.md. **Rendering only — no rule may depend on it.**
- **How terrain constrains movement.** Creatures move only on dug-out tiles and
  never through solid rock; a dug-out tile can never be closed again (DK2 has no
  fill-in a keeper can trigger); and two terrains can never be dug through, for
  two different reasons — Impenetrable Rock is unmarkable, Gems destroy to
  themselves. All CONFIRMED by the owner 2026-09-04, all now asserted rather than
  merely true. Q73/Q74, docs/research/manual-1999.md.
- **What most rooms are for**, in prose if not in numbers — from the shipped 1999
  manual, paired with the balance variable that quantifies each one. Not
  implemented, but no longer unknown. See docs/research/manual-1999.md.
- **Which balance numbers a level actually plays on.** Every shipped level ships
  its own VARIABLES and 26 ids are overridden by at least one of them — one of
  which was already live (`MinimumImpThreshold` is 0 on four secret levels
  against a global 4). Loaded and merged as of ADR-0040. The by-product is
  larger: **`Availability` is decoded** — kinds are rooms/creatures/traps/doors/
  spells, states are unavailable/researchable/available, and it is per level and
  per player, which is what makes level 1 a tutorial and `Secret1` a puzzle. See
  docs/research/kwd-variables.md §*Update 2026-09-04*.

- **Where creatures come from — the portal, implemented** (ADR-0042,
  docs/research/portals.md). Four gates, all four in the data: `Availability`
  kind 2 unlocks a creature, `CreaturePool` caps how many of it, its
  `CreatureAttraction` slots say what must be in the dungeon to draw it, and
  `CreaturesSupportedByFirstPortal`+5-each-after caps the lot. **Level 1
  produces nothing until its keeper builds a lair**, because the Goblin's only
  attraction is a Lair — the tutorial's order is a mechanic, not a script. Three
  of the composition rules came from OpenKeeper rather than the files, the
  licence now permitting it (ADR-0003 second amendment). **A fifth gate was
  found later the same day, in the binary:** a creature's own flags decide
  whether a portal may produce it at all — `AvailableViaPortal` required,
  `IsUnique` disqualifying — and without it a keeper's portal produced *Knights
  and Fairies* on the three Frosty levels, which mark the whole hero roster
  AVAILABLE and pooled for keepers 3 to 6 (ADR-0047).
- **The owner CONFIRMED that arrivals slow as a dungeon fills** — the one thing
  in the portal that nothing in the shipped data could express. What is left is
  no longer *whether* but *why*: the implemented interval scaling and a
  weighting the binary suggests would both produce it, and they differ in
  whether lair size matters (Q97).
- **DKII.EXE may be decompiled, and its output may not enter the repository**
  (ADR-0046, answering Q96 — an owner decision). The rule is fact / expression:
  numbers, formulas and control flow may cross into `docs/research/` in our own
  words; decompiler output stays in gitignored `reverse-engineering/`. Binary
  facts rank **below** owner observation and may never reach CONFIRMED on the
  binary alone. First pass: `docs/research/binary-entrance.md` — five findings,
  one defect fixed, one contradiction recorded unresolved, and the question it
  was opened for (Q86's slope) **not** answered, because the spawn clock is not
  in `Entrance.cpp`.
- **Not every player is a keeper** (ADR-0043, `PlayerIds`): 0 is unowned, 1 the
  hero side, **2 neutral — which is what an unclaimed room belongs to** — and
  3–7 keepers. CONFIRMED three ways, and it had been assumed away everywhere.
  The by-product of asking whose portal an unclaimed portal is, and it closed
  Q17 — which had been open long enough for a decision to rot on it (ADR-0045).
- **How a keeper claims a room somebody else holds** (ADR-0044): **whole, never
  tile by tile.** CONFIRMED by the owner. One tile is worked at
  `ConvertRoomHealth`'s −4000 — thirteen swings, 2.6 seconds — and the entire
  room instance changes hands, gold included (which is what Q40 saw on defeat).
  Answering it exposed that `Claim` had been taking rooms a tile at a time since
  rooms existed, and building it exposed a second, unrelated defect that had
  **walled a level-1 keeper in permanently** (ADR-0045). One owner sentence, two
  four-month defects, neither about portals.

- **A creature has its own bed and sleeps in it** (ADR-0049, **CONFIRMED by the
  owner**): a lair is one bed per tile, a creature takes a free tile on arrival,
  and **it heals only while asleep in its own bed** — 100 a second. The proxy
  ADR-0048 shipped was wrong in both halves, healing anything standing awake on
  any of its keeper's lair tiles. It also settled a third thing nobody asked:
  **a lair's capacity is its tile count**, which is *not* the general rule for
  rooms (a temple's is its rim, Q60). And it produced **the first driver a
  non-worker creature has ever had**, reusing the imps' pathfinder rather than
  growing a second way to walk.
- **A creature has health, and a hunger clock** (ADR-0048,
  docs/research/creature-needs.md). Maximum from the record's `Hp`,
  regeneration on your own land from the record, healing in a lair at 100 a
  second, and death at zero — once a game second, because every number involved
  is written per second. Three things the data said that no list had: **32767
  means "never" per need rather than per creature** (eight creatures never eat,
  six never sleep, and neither set contains the other — the Vampire sleeps and
  never eats, the **Dwarf** eats and never sleeps, which closes a question open
  since creatures were parsed); **own-land regeneration applies to two creatures
  out of 46** (Imp 67, King Reginald 400, the same shape as mana's three terrain
  types out of 41); and **a chicken can over-heal** (Jack has 240 health and a
  chicken worth 250), which makes the clamp load-bearing. Hunger arrives and
  cannot be satisfied — chickens are objects (M6) — and **costs no health on
  purpose**: there is no starvation anywhere in the balance table, because what
  going unfed costs is anger.

## Unsolved (research required before implementing)
- **What a room does.** ← being worked on, **the portal done and the library
  next**, and **much less of a
  blank sheet than it was**. The ROOMS record carries art, size and cost and says
  nothing about behaviour, but the 1999 manual says what each room is *for* and
  the balance table has a per-second rate for nearly all of it — a lair heals 100
  and calms 550 a second, a prison drains 3, a casino's payout lever is the
  `Smiles`/`Money` variable pairs, training caps at level 4 and costs the keeper
  5 gold a second. See docs/research/manual-1999.md, "What each room does".
  What is still missing is the *shape*: capacity, per-creature eligibility, and
  what a room does with the state it changes. Do not assume "capacity = tile
  count": Q60 established that a temple's capacity is its **rim**, `2(w+h) − 4`,
  and the manual says the dungeon heart stores its gold in its "outer ring" too.
  Several rooms are blocked on objects (M6) or on creature needs, not on research.
- **What creatures then do.** ← **health, hunger, beds and sleep now exist**
  (ADR-0048, ADR-0049), so a creature is no longer inert: it can be hurt, healed
  and killed, it gets hungry, and it takes a bed in a lair, walks to it and
  sleeps in it. What it still has no notion of is **mood, anger, experience or a
  job**, and it still cannot be picked up or dropped. **Anger is the next one
  that matters** — the owner has CONFIRMED the ladder twice (Q9 for pay, Q105 for
  lairs: *unhappy, then angry, then it leaves the dungeon*) — but it is
  **blocked on Q106, its unit.** Every rate is in the data and there is no
  ceiling anywhere, including in OpenKeeper, whose mood system is an empty stub.
  A wrong ceiling would propagate into job selection and desertion, so it waits
  for one stopwatch (ADR-0050, docs/research/creature-anger.md). Fixing the
  signedness of the six anger fields on the way in found that **slapping angers
  and being picked up calms** — read unsigned, picking a creature up would have
  added 65286 to its anger.
- **How a keeper *destroys* a room somebody else holds.** `AttackRoomHealth` =
  −1500 names it and there is no `TileWorkKind` for it. **Claiming** one is done
  (ADR-0044) — a room changes hands whole, never tile by tile, CONFIRMED by the
  owner — but beating one down is a different job at a different rate and is not
  built. Two rules inside claiming are still ours: whether a dungeon heart is
  exempt (Q93) and whether a long bridge really goes whole (Q94).
- **A level's own starting creatures.** THINGS is unparsed, so no level's roster
  loads and every creature in a run either came from a portal, from
  `ImpSupplySystem`, or from a scenario file saying so.
- **Which rooms a keeper may build on a given level.** `Availability` kind 1 says
  so, per level and per player, and `RoomRules` does not model it at all — a
  keeper here can build any buildable room on any level.
- Pathfinding (biggest risk), creature moods/anger, combat resolution,
  job-selection priority order, computer-player AI, audio event semantics.
- **Creatures have no needs yet** — no health, hunger, mood or experience, by
  design (`CreatureState` carries only what a built mechanic reads). Several room
  behaviours will be blocked on this rather than on research.
- What happens on defeat when a dungeon heart falls (Q40) - the rule is known,
  the consequences are not, and nothing implements it.
- What picks a wall piece (Q71) and whether wall deformation is deterministic per
  tile (Q72). Both presentation-only; Q72 is a look-and-see for the owner.

## Per-mechanic notes
One file per mechanic in `docs/research/`, tagged
CONFIRMED / STRONGLY INFERRED / HYPOTHESIS / UNKNOWN per AGENTS.md.

## Answerable by playing the game
`docs/research/observation-checklist.md` — the open questions a person in front
of DK2 can settle by looking, as concrete experiments, ranked by what they
unblock, plus the ones playing cannot settle. **Every question that has ever
headed this list is now answered** — Q47's tile-work rates (273 gold a swing),
Q45's room minimum sizes, three of Q86's four portal questions (answered from
OpenKeeper rather than by playing), and Q86's fifth — *do arrivals slow as the
dungeon fills* — CONFIRMED by the owner on 2026-09-04. What heads it now is
**Q97, and it is one experiment rather than a stopwatch**: build a *large* lair
and see whether arrivals still slow at the same rate. If lair size changes the
pace, the slowdown is draining eligibility and the implemented interval scaling
is the wrong cause for the right symptom; if it does not, the interval really
does stretch. Followed by the slope itself — two consecutive gaps *with the
goblins counted*, which the formula says differ by exactly `base ÷ 2`.
