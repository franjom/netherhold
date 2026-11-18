---
systems: [hud-front-end, creatures, presentation-godot]
---
# The possession HUD

DKII.EXE v1.70, read 2026-11-18. STRONGLY INFERRED unless specified otherwise.
This is the detailed read of `0x00420D10`, previously known only by four windows.
The owner's original Imp screenshot is CONFIRMED visual evidence, at
`F:\DK2Evidence\captures\visual\possession-first-person--2026-11-15--001\payload\imp-pov-pasted-by-owner.png`.

## Draws

The creature section accepts camera mode 1 or 2, a live class-0 thing from
player `+0x134`, owned by the interface's player. It installs definition art
`+0x510` for the top portrait and `+0x57C` for melee. The spell loop examines
three slots, requiring nonzero spell id and available level, using each spell's
GUI art. Selection bit 0 chooses melee; bits 3..7 choose the spell slot. Their
backings are selected-creature and selected-spell. Slot 3 has selected-group
backing and group-mode art.

Health is creature `+0x40` divided by level-scaled definition health `+0x692`;
its fill width is clamped. Carried gold is `+0x3FB`, drawn with resource digits.
The recharge getters `0x0048CA10` and `0x0048C8B0` report 0..100 against the
possession-modified interval, without the combat gate's work-rate divisor.
Partial recharge draws bright and 0.4-grey radial portions.

Primary special kinds 1, 2, 3, 6, 7 choose throw, sniper, bat, sneak and pray
icons. Kind 1 uses throw-mode when player `+0x136` is empty and selection bit2
is clear, throw_book-mode when empty and selected, and throw_imp-mode for any
nonzero carried thing; the latter two have selected-creature backing. State flags select backing; secondary kind 5
draws hypnosis with backing while a target is held. The player-owned creature
list contributes portrait art `def+0x52B` whenever `+0x482` is nonzero.

The executable's source catalogue has 69-byte entries: id, 64-byte name, type;
runtime offsets are 27 bytes per entry. Directly read entries: 69 m-hp_bg,
72 Bar-00, 90 bat-mode, 91 group-mode, 92 pray-mode, 93 sneak-mode,
94 sniper-mode, 95 throw-mode, 96 throw_book-mode, 97 throw_imp-mode,
111 F-Sniper, 112 F-Frozen, 113 m-hp, 114 m-h-tone-bg, 145 m-h-bg,
173 selected-creature, 175 selected-spell, 230 hypnotise-mode, 234 map-2,
237 selected_group. The bar backdrop is repeated along the screen.

## Callees

`0x00410580` selects overlay entries 107..110 for creature types 13/28,
22, 25/30, 12; id 0 retains the previous selection, other ids clear it.
Those catalogue ids resolve to F-Knight, F-Black_Knight, F-Guard and F-FireFly
respectively; records108/109 store swapped ids. Filters compose sniper first,
creature-specific second and frozen third, after the top creature panel and
before the minimap, followers and bottom backdrop.
`0x00410630` draws the catalogue entry at screen origin. Sniper draws entry
111 directly; frozen draws entry 112. `0x00428120` invokes enabled indicator
menus 45..50. `0x0052C5A0` scales coordinates independently by width/2560 and
height/1920. Art-installation and digit helpers retain their existing readings.

## Remake boundary and choices

`PossessionHud` builds a `HudFrame`; `PossessionHudView` captures world facts;
`InstallPossessionHudArt` reads GUI resources behind the install abstraction.
Godot presents the frame. Normal panels and shortcuts are blocked while the
session holds the camera; the existing minimap is retained without input.
The existing mana/Heart corner is retained. Menus45..50 use the shared indicator
facts and stacked layout. The visible surface and bottom-docked elements account
for UI scaling in the engine-free frame builder.

The top strip's spacing is an adapted layout, not original pixel identity:
portrait at reference (520,8), 128 square, a 292-unit health slot, carried-gold
slot and 128-square action icons 136 apart. The original screenshot guides its
proportions. The shared HUD uses height-based virtual units instead of DK2's
independent horizontal stretching. Exact native health-bar layering and follower
geometry remain incomplete. The carry-art resolver accepts the independent
possession carry fact; the simulation does not yet model that slot, so the adapter
currently supplies Empty. It must not substitute ordinary keeper hand contents.
