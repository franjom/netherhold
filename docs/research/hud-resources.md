# Resource HUD

Sources: DKII.EXE v1.70, read-only Ghidra queries, 2026-10-18; the install's
Text.str and EngineTextures cache. Output stays in `reverse-engineering/resources-*`.
Function records hold the individual verdicts.

## Values and update cadence — CONFIRMED

`0x00422BC0` draws the viewing keeper's mana total from player `+0x82` and
two rates. It is called by the active HUD dispatcher `0x00419DF0` each frame,
not only on a mana payment. A FlashButton descriptor beginning `00 F2` suppresses
the numbers on odd 250-millisecond real-time phases; background drawing stays.
The two rate displays are omitted at screen widths **600 or below**.

The green incoming rate is `0x004C1770`: the lesser of player `+0x10DE` and
`+0x10E2 + upkeep + +0x10E6 + +0x10EE`. The upkeep term is `0x004C17A0`, also
shown independently as the red outgoing rate. These are **gross incoming and
outgoing rates**, not positive/negative versions of a net value. `+0x10E2`
already has ordinary imp/trap costs subtracted; adding upkeep recovers gross.
See `imps-and-mana.md` and the existing mana function records for payment rules.
Our simulation's `ManaRules.GainPerSecondByPlayerIndex` is already gross:
**do not add upkeep to its result**. Use its separate upkeep result for red.
The native upkeep helper evaluates Call to Arms against the **current game
turn**: its prepaid clock must be strictly earlier before that upkeep appears.
The HUD therefore uses the scoped `UpkeepPerSecondByPlayerIndex` overload with
`world.CurrentTick`, rather than the default overload's next-tick evaluation.
This affects the displayed red rate at the prepaid boundary; simulation
payments retain their existing timing. The native gross helper uses the same
current-turn upkeep while reconstructing gross from its net running field.

`0x00414BC0` draws Money, player `+0x7E`, not treasury capacity. During the
payday warning it alternates Money and the negative coming unpaid wage bill
on 500-millisecond clock phases. The cutoff is measured in raw turns; a zero
cutoff uses 100. The warning latch also lasts until ten game seconds after
the last payday. A `00 F3` descriptor suppresses numbers on odd 250-ms phases.
Blank phases bypass the warning-latch update. During bill phases the wage bill
refreshes when current milliseconds exceed its cached stamp, then stores that
clock unchanged. See `payday.md` for its rules.
The already implemented `CasinoJackpotRules.WageBill` is this same producer;
resource capture reuses it. Flashing uses the real clock and continues while
the simulation is paused; the ten-second extension and alarm use game time.

## Digits and colours — CONFIRMED

These resource numbers are **sprites, not BF4 text**. `0x004152B0` loads the
actual raw-string resource names below. Older Ghidra symbol names replaced
punctuation and must not be used as paths.

| Use | Resources | Highest cache image |
|---|---|---|
| Money and mana total at width >= 640 | `Gui/icons/i-0` through `i-9` | 32×32 |
| Same totals below width 640 | `Gui/icons/lo-0` through `lo-9` | 16×16 |
| Mana rates | `Gui/icons/mana/s-0` through `s-9` | 16×16 |
| Incoming icon / sign | `Gui/icons/mana/s-in`, `s-+` | 16×16 |
| Outgoing icon / sign | `Gui/icons/mana/s-out`, `s--` | 16×16 |

`0x00415650` converts a signed decimal integer, draws no thousands separators,
and can pad the left with empty character slots. Gold and mana total pass
right-alignment enabled, seven slots and size mode 0. That mode transforms
virtual pair `(48,40)`: the first component is installed in **both** sprite
size fields, and the second is character advance. Alternate size mode 1 uses
`(96,60)` and is not selected by these two callers. A leading minus uses
catalogue resource 106, `gui/Icons/I--`, even below width 640. Preserve each
source image's transparent padding.

`0x00415880` draws incoming/outgoing icon, sign and decimal digits. All have
transformed virtual 64 in both sprite size fields. The spacing pair is
`(16,24)`; horizontal steps use its first component. Icon starts at the supplied
point, sign one step farther right and first digit two steps farther right.
Remaining digits advance one step each. No right-alignment padding is used.

| Quantity | RGB |
|---|---|
| Money | 238,182,0 |
| Mana total | 0,252,156 |
| Incoming mana | 50,255,50 |
| Outgoing mana | 255,50,50 |
| Payday bill affordable | 0,178,235 |
| Payday bill unaffordable | 255,75,75 |

## Geometry and active artwork

**CONFIRMED:** virtual reference coordinates are **2560×1920**.
`0x0052C5A0` and `0x0052C630` independently scale horizontal components by
screen width/2560 and vertical ones by height/1920, with integer division.
At 1024×768 both factors are 0.4. The adapter's existing height-preserving
768-reference transform corresponds to multiplying virtual coordinates by
0.4, then height/768; it deliberately avoids the native widescreen stretching.

`0x00422BC0` supplies these global virtual text anchors:

| Element | x | y |
|---|---:|---:|
| Mana total, before seven-slot right alignment | 172 | 32 |
| Incoming rate icon | 184 | 88 |
| Outgoing rate icon | 360 | 88 |

Static menu **21**, record `0x006939A0`, has draw rectangle `(0,20,1020,88)`
and widgets starting `0x00691068`. Its gold counter widget `0x006911A8` has
draw rectangle `(680,0,400,88)` and no right/bottom anchor bits. The counter
adds 20 virtual units to its positioned top before drawing the number.
Thus its unanchored digit start is `(680,40)` before right-alignment padding.
Its callback still draws the Money background when the number flashes off.

Both the gold callback and mana composite install catalogue **76/77**,
`gui/Windows/Mana-Bg-00` and `Mana-Bg-01` (128×64 MM0). They share a base
rectangle of virtual width 224 and height 112: mana edges `(0,12,224,124)`,
gold edges `(516,12,740,124)`, plus transformed offset rectangle `(12,164,16,0)`
and draw flags 8. Let `L=X(left)`, `W=X(right)-L`, `Y=Y(12)`,
`H=Y(124)-Y(12)`, `D=X(12)`, where X/Y are the native integer transforms.
The halves draw at **`(L+4D,Y)` and `(L+4D+W,Y)`**, each W×H. At 1024×768:
mana starts `(16,4)` / `(105,4)`, each 89×45; gold starts `(222,4)` /
`(312,4)`, each 90×45. Multiplying the virtual origin 48 by 0.4 only at the
end gives a different result; preserve the transform-before-multiply order.
The active sprite renderer's complete flags/cropping semantics remain partly
unread; these supplied rectangles and source padding are confirmed.

Other catalogue resources are **not evidence that they are active**:
74/75 Mana_Frame-00/01, 78/79 Mana-00/01 and 80/81 Mana_drain-00/01 are each
128×64 cache images. No literal runtime-slot use or static widget selection
was found for them in the inspected paths. Gold-bg-00/01 (88/89, type 1) are
also present; the ordinary gold number callback instead uses 76/77 above.

## Hover and clicks — confirmed bindings, partial actions

Widget coordinates below are local to menu21. `0x00528570` adds its `(0,20)`
parent origin, so Heart and Payday start at screen-virtual y48.

| Widget | Local virtual draw rectangle | Tooltip id | Binding |
|---|---|---:|---|
| Heart health | 28,28,100,100 | 2139 | Secondary `0x00416680`: zoom to Heart |
| Mana total | 140,0,360,88 | 2133 | No click callbacks |
| Payday countdown | 540,28,100,100 | 2143 | No click callbacks |
| Gold | 680,0,400,88 | 2141 | Primary `0x00416730`: request gold in hand |

Companion Text.str lines describe the current mana total (2134), heart
health `%19` and right-click zoom (2140), current wealth and 100-gold pickup
(2142), and coming payday bill `%20` (2144). `0x00416730` submits action 105:
100 normally, 1000 when modifier mask equals 1, 10000 when it equals 2;
other masks, including combined modifiers, take the default 100 branch.
`0x005B2A50` constructs that mask from left/right Shift (1), Ctrl (2), Alt (4).
It then selects hand context 9. Gameplay execution is the separate
action handler described below.

`0x0042D820` dispatches ordinary widget status to `0x0042DB60`. That formatter
uses the tooltip id plus one for these four descriptions. Heart description
requires a live Heart object; assembly `0x0042DC7E..0x0042DCBA` supplies `%19`
as signed integer `current health * 100 / maximum health`, truncating toward
zero. Payday `%20` is `0x004BE290`'s current bill. The generic GUI popup uses
the widget's tooltip id with a 250-millisecond delay; see world-hover-text.md.

## Orb, gauges and remaining limits

The composite's call `0x00422F60(0,3)` draws a **Dungeon Heart health gauge**,
not a mana-total orb: it finds room type 5's object type 13, reads its health,
uses heart-bg/heart-hp catalogue 84/85, and alarm 166 after damage. Its
decompiler drops valid floating-point branches; the assembly confirms a
radial health fill and three-game-second, alternating-turn alarm window.
The Heart background draws at physical `(0,3)`. The payday helper `0x00415000`
draws a separate countdown gauge at `(X(516),Y(12))` with payday-BG/payday-Hp,
catalogue 86/87. Both gauges install **X(160) in both size fields**: 64×64 at
1024×768. Preserve the source disc padding. The owner's DK2 screenshot shows the Heart's frame over its green ring (2026-10-20);
the pushed layers (fill 3, frame 2, alarm 1, [0x00422F60](functions/00422F60.md)) fit that if a lower layer
is in front, which is inferred. The adapter draws each ring behind its frame. These are static positions; the
literal Heart y=3 is physical and does not scale with the screen height.

**CONFIRMED fill:** `0x0041EF70` clips a textured square to an angular sector
using a triangle fan from its centre and the crossed square corners. It is
not an annular mesh: the source texture's alpha defines the ring. For fraction
f, white texture draws from `3π−2πf` to `3π`; black-multiplied texture draws
the complementary sector from `3π` to `3π−2πf`. Angles normalize above 2π;
an end below the start gains 2π. Coordinates use x=sin(angle), y=cos(angle),
so the filled area runs clockwise from twelve o'clock. Full Heart health
special-cases the whole white texture; nonpositive health draws it black.
Heart f is current object health / max health. Payday f is turns remaining /
`(now + turns remaining − last payday turn)`; nonpositive remaining draws
the full black texture. The helper's supplied depth is 0.3. Current Heart
health supplies its fraction. `0x004BE330` writes both the first deadline and
the current turn to player `+0xB4F`; `0x004BE1A0` rewrites that stamp each
payday. `PlayerState.LastPaydayTick` preserves this stamp through hashing and
snapshots, so first-arm, subsequent and loaded countdowns have the native
denominator without a guessed interval.

The alarm producer is **the previous displayed health**, not a combat message.
Assembly `0x004230E4..0x0042311A` compares the cached value at interface
`+0x4B27` with current Heart health. A decrease sets `+0x4B2B` to now plus
three game seconds; alarm catalogue 166, `gui/windows/heart-alarm`, draws
on odd original turns strictly before that deadline. `0x00423321..0x00423330`
stores current health for the next draw. `0x004033F0` initializes that cache
to maximum Heart health. Decompiler output incorrectly hides both comparisons
and the final current-health store; assembly is decisive. Settled resource
capture observes health only while the normal HUD is displayed. Hidden captures
leave the comparison value unchanged; restoring the HUD can start a fresh alarm.
Intervening damage healed before the next display does not itself trigger one.

**UNKNOWN:** a separately animated mana-orb producer, animation clock or fill
formula. The names of unused mana textures do not justify inventing one.
The confirmed static slice is the live total, gross incoming/outgoing rates,
original numeric sprite families, colours, source assets and supplied geometry.

The hover status text occupies the adjoining menu rather than the resource
area; see [world-hover-text.md](world-hover-text.md#original-fonts-artwork-and-geometry).
## Counter withdrawal — CONFIRMED

`0x00511A80` registers action 105 at table record `0x006A5A30`, with gameplay
handler `0x00513CE0`. The complete handler and assembly were read. It first
tests player hand count `+0x1BF` against capacity `+0x1C0`; a full hand does
not spend gold. It requests the negative amount through `0x004BA420` with
flag and location zero, retaining the amount actually taken. This is the
same storage transaction already represented by `GoldStore.Spend`.

A nonzero take creates neutral **object type 3** at the zero position through
`0x004B7A90`, adds the taken gold, sets object `+0x146` bit `0x10`, then calls
`0x004C09F0` with hand state 2 and that object's id. The factory failure arm
refunds the requested amount. Our factory does not have an allocation-failure
result; missing definitions are rejected before spending. The absent raw bit's
consumer remains unread; the existing hand/object model carries the pile and
its amount. `WithdrawCounterGoldCommand` runs in `HandSystem` in input order,
so a same-frame drop uses ordinary Hand delivery. It never calls the AI-only
`MintWageCommand`, requires no Treasury when the Heart has stored gold, and
creates no gold when storage is empty.

## Scripted resource flashing — CONFIRMED

These are **FlashButton action 9 descriptors**, not retained player messages.
The primary world vtable `0x0066E3EC` binds slot `+0x2F8` to `0x005096F0`,
which tests world `+0x7291` against `0xFF`, and `+0x2FC` to `0x00509700`,
which returns that descriptor. Category and button are its first two bytes;
`00 F2` selects mana and `00 F3` selects gold. Duration seconds occupy the
next dword and the starting real-clock millisecond stamp occupies the last.

`0x00518AD0` case 9 tests action body byte 2. Zero clears the descriptor;
nonzero calls `0x00510260` with body bytes 0/1 and unsigned dword 4. That
setter stores the target and duration, then stamps `0x005B5E90`'s real clock.
It has no player gate: this is a global view request. `0x00509710` clears
category to `0xFF` and the other fields to zero.

`0x0050A450` expires the descriptor before its pause guard when duration is
nonzero and unsigned `start + duration * 1000 < real milliseconds`. Equality
still flashes; duration zero persists until replacement or explicit clear.
Expiry therefore continues while Options pauses simulation. The existing
scripted-view event queue carries this request to presentation, where wall
time belongs; no real clock is introduced into simulation rules or hashing.
