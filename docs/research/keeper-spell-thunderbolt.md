# Keeper Thunderbolt on a creature

Sources: DKII.EXE v1.70, Ghidra 12.1.4/PyGhidra, 2026-09-28; shipped
KeeperSpells.kwd and Shots.kwd; the owner's Level 2 playback probe at
`D:/DungeonKeeperRemake/reverse-engineering/dk2runs/level2-win-20260928/creature-map.csv`.
The capture remains outside this repository. Binary and data claims below are
**STRONGLY INFERRED** unless a paragraph says otherwise. Observed health/state
values are **CONFIRMED**; causal attribution has its own confidence.

## 1. Descriptor and selected process — STRONGLY INFERRED

Loader `0x004F7CE0` consumes a record with a `0x78` prefix before the file
payload. Raw values, without floating-point transcription:

| Field | File offset | Runtime offset | Raw value |
|---|---|---|---|
| spell id | `0xEE` | `+0x75` | `0x06` |
| shot | `0xF0` | `+0x42` | `0x15` |
| data 1 / 2 | `0xD0 / 0xD4` | `+0x43 / +0x47` | `0x00000096 / 0x00000000` |
| upgraded shot | `0x113` | `+0x4B` | `0x15` |
| upgraded data 1 / 2 | `0x114 / 0x118` | `+0x4C / +0x50` | `0x000000C8 / 0x00000000` |
| mana cost | `0x11C` | `+0x58` | `0x00001770` |
| cooldown | `0xCC` | `+0x3E` | `0x00000000` |
| location / target selectors | `0xEF / 0xDA` | `+0x76 / +0x77` | `0x01 / 0x02` |

Cooldown loads as unsigned low-32-bit `(raw * ticksPerSecond) >> 12`.
Shot 21 has process 9, process flags `0x40`, flags `0x04`, damage/collision
classes both zero, attack category 6, health `0x08`, speed `0x0800`, radius
`0x0333`. Its default data1 is `0x19`, overwritten by the spell. Death,
hit-thing and hit-solid shot ids are zero. The normal and upgraded spells
therefore share a process; their damage parameters differ.

The initializer `0x004AD1B0` installs process 9's callback `0x004AF1E0` at
`0x0069E8E9`. It runs on the first shot update, not a projectile hit test.
`shots.md` §§4 and 8 own the general event and process machinery.

## 2. Validation and packet boundary — STRONGLY INFERRED

`0x00513340` does no additional permission checks: it extracts spell id from
field 1's low byte, flags from its high word, target tag from field 2's low
word, and the player tag from packet `+0x10`. It calls `0x004BBF90`, ignores
its result, and returns 1. Action 54 is a separate handler.

The creature helper's permission, payment and clock order and its unconditional
zero return are in `keeper-spell-casting.md` §§1–2. Instruction verification:
`0x004BC178..0x004BC259` creates the shot, writes data and target, debits mana,
adds the cost to player `+0x10F2`, and stamps both clocks. Target selectors
1–3 and 6 also set the shot's forced-target word. Thunderbolt's origin is
raised by `0x2000` in height; the current simulation's shots omit height.
The helper's creation call uses the normal shot byte even after selecting
upgraded parameters; the shipped Thunderbolt's two shot ids are identical.
The public adapter selects the requested upgraded descriptor (ADR-0260).

Location selector 1 (`0x00517B80`) requires self/allied land, excludes neutral
ownership, and rejects navigation-solid bit 3. The world slot is
`0x0066E3EC +0x20C -> 0x005090C0`; it reads the navigation grid at `world+0x9F2`.
Water, lava, portal-hole and door bits are distinct. The nonenemy relation is
`0x004BF3D0`, not a generic same-owner test.

The room exclusions then reject Portal tile code 4, the Heart's inner three
by three, and tile code `0xFF` in Prison, Temple and Combat Pit. Tile code
`0xFF` is the interior marker (`room-slab-chain.md`, `0x00454110`). For a Heart,
`0x004DA150` subtracts room `+0x2D/+0x2E` (the first slab, not the bounding box)
and rejects when both offsets are strictly between 0 and 4. Its world call
`+0x1D8 -> 0x00508FC0` fetches a tile and does not alter those coordinates.
Tile-code dispatch `0x004EDC10` uses the room construction selector. Shipped
Portal uses 3: `0x004EF730` and table `0x0066DB88` produce code 4 when all four
cardinal neighbors belong to the room. Prison, Temple and Combat Pit use 7:
`0x004EFDE0` sets eight bits for matching owner/terrain neighbors, using the
offsets at `0x006BC370`; `0xFF` requires all eight, including diagonals. The
cast gate derives those predicates directly from room membership.

Target selector 2 (`0x00517930`), flags zero, admits a live creature tag owned
by someone other than the caster and neutral. An ally is eligible. Neither
this target test nor location selector 1 asks range or fog. A dying creature
still has a live creature tag; it passes permission, consumes mana, and its
health routine subsequently refuses damage. With flag bit 2 set the target
callback delegates to selector 1's own-creature arm; that variant is read
but refused by this implementation's ordinary-cast boundary.

Availability is not tested by this packet handler or the helper, and since
ADR-0260's amendment our command does not test it either. The interface gates
it, as for Create Imp (ADR-0247).

## 3. Effect and limits — STRONGLY INFERRED

`0x004AF1E0` resolves the target directly. Creature descriptor second-word bit
0 prevents both damage and stun. Otherwise the negative absolute value of
data1, doubled on water, enters `0x004B2CE0` with mode zero. There is no
level multiplier, resistance-array lookup, aim roll, or damage RNG on this
read path. The shared health modifiers remain in
`creature-status-effects.md` §3: frozen, armour/possession, the owner's damage
byte, stun, and Combat Pit. Integer division rounds toward zero.

The mode-zero instruction is `PUSH 0` at `0x004AF29A`, consumed by the health
call at `0x004AF2D1`. That routine refuses the dying flag at
`0x004B2DE7..0x004B2E05`. Thus **a normal Thunderbolt cannot finish a creature
already in the dying window**; see `creature-death.md` §4. This conflicts with
the task brief, not with any of its 19 casts: none targeted a dying creature.
A lethal blow to a standing eligible creature writes the 240-tick death clock;
health after collapse is no longer hit points.

After damage, the handler writes electrified-display duration `0x14`, then
attempts state 255/action 52/event 15 through `0x00491830`, unless carried,
in category 23, Horny or possessed. The state transition may refuse (including
frozen/dying states). Its damage call does not test invulnerability. No
knock-back operation occurs in the complete process-9 handler.
`0x004B32A0`, called first with radius 2, searches using `radius*(radius>>12)`;
that bound is zero, so its strictly-inside loop cannot reach its fear callback
on this call. The whole helper was read, not assumed cosmetic.

Scan control: `ScanAll.py 006F2590` returned **226** hits across all thirteen
initialized blocks, including `.text` and `cseg`. The negative statements here
are limited to the fully read handler, its initialization/dispatch and the
health path, in that image; they do not claim unrelated processes lack readers.

## 4. Predictions and the 19 casts

Before opening the probe, ignored working notes recorded: ordinary dry-land
normal/upgraded damage 150/200, 300/400 when stunned, twice those on water;
no random spread; no damage while dying. The raw data and equations in §§1–3
were the source, not a fit to the capture.

The CSV is a change log. Use the last preceding row for a slot rather than
requiring a row at turn minus one. Cast-turn rows show the new health; no
blanket one-turn adjustment was applied (ADR-0119). The following measured
values are **CONFIRMED**. Predictions assume the normal spell, no armour,
dry land, and the state last observed before the cast. Collapse predictions
are a state/countdown, not a numeric health subtraction.

| Turn | Slot | Prior state | Predicted loss | Observed health | Assessment |
|---|---|---|---|---|---|
| 2788 | 442 | 90 | 150 | 1000 → 850 | matches |
| 2801 | 442 | 6 | 150 | 850 → 700 | matches |
| 2847 | 440 | 199 | 150 | 1000 → 850 | matches |
| 2859 | 452 | 203 | 150 | 1000 → 850 | matches |
| 2865 | 455 | 203 | 150 | 1000 → 850 | matches; further 600 damage at 2867 |
| 2880 | 454 | 8 | 150 | 1000 → 850 | matches |
| 2905 | 453 | 199 | 150 | 1000 → 850 | matches |
| 3001 | 428 | 203 | 150 | 1000 → 850 | matches |
| 3027 | 431 | 8 | 150 | 477 → 327 | matches |
| 3032 | 431 | 257 | 300 if still 257 | 327 → 177 | timing unresolved, Q470 |
| 3038 | 431 | 199 | 150 | 177 → 27 | matches |
| 3047 | 430 | 8 | 150 | 631 → 481 | matches |
| 3064 | 427 | 8 | lethal 150 | 100 → 240, state 216 | collapse matches |
| 3089 | 430 | 6 | 150 | 481 → 331 | matches |
| 3094 | 430 | 257 | 300 if still 257 | 331 → 181 | timing unresolved, Q470 |
| 3099 | 430 | 257 | 300 if still 257 | 181 → 31 | timing unresolved, Q470 |
| 3107 | 431 | 6 | lethal 150 | 27 → 240, state 216 | collapse matches |
| 3116 | 429 | 8 | 150 | 1000 → 850 | matches |
| 3241 | 444 | 199 | 150 | 1418 → 952 (466) | contaminated/unresolved, Q471 |

**STRONGLY INFERRED:** the thirteen ordinary nonlethal Dwarf matches isolate
150 net damage. The Dwarves have no own-land regeneration; none of these
rows is an eating/sleeping state. Additional positive damage without an
unobserved offsetting heal would make the loss exceed 150. Fighting states
and targets were inspected around every cast, and the 600 loss at 2867 is
on a different turn. The two collapse rows cannot bound overkill or exclude
another hit in the same turn. These are limits of the probe, not clean
measurements of lethal damage.

**UNKNOWN (Q470):** the three getting-up rows have no intervening logged
state transition. A transition earlier within the cast turn could explain
150, but the probe lacks action timing. `0x0048F350` was re-read and includes
209, 255, 256 **and 257**. The binary-derived multiplier is retained.

**UNKNOWN (Q471):** the leader loses 466, exceeding the simple prediction by
316, and its slot becomes an object on 3242. Slots 426, 434 and 445 (type 7)
are on its tile, with 447 adjacent; 426 and 447 were targeting 444 before the
cast. A same-turn melee/spell contribution is plausible, not measured by
this CSV. It cannot rule out competing damage or attribute the death to the
Thunderbolt alone. Do not tune the spell to 466.

## 5. Implementation boundary

`KeeperSpellRules` and packet action 55 use the existing process-9 shot path.
Other spell ids, nonzero flags and unimplemented selector/process variants
are explicitly refused. Clocks and mana accounting are hashed and saved.
Action 54, computer-keeper battle wiring and other keeper effects remain
outside this change. The normal/upgraded shot id distinction for modded data
follows the requested public adapter contract (ADR-0260).

Read but not represented here: the height/display effects; the owner's
`+0x1095` damage reduction and the stun-extension call `0x00481360` already
listed in Q399. Existing simulation approximations remain: health collapse
is processed by `CreatureHealthSystem`, its countdown is separate from
health, and shots update after creature movement (Q398). The new tests
isolate shot arithmetic and gate/state serialization; they do not claim
full Level 2 playback equivalence or resolution of Q470/Q471.
