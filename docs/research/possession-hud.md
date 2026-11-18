---
systems: [hud-front-end, creatures, presentation-godot]
---
# The possession HUD
DKII.EXE v1.70. Binary and shipped-asset readings below are STRONGLY INFERRED unless
specified otherwise; individual function-record headers retain their own confidence.
The owner confirmed that the comparison BMPs show a Warlock in My Pet Dungeon.
Eighteen native BMP copies are SHA-256 verified under
`F:\DK2Evidence\captures\possession-hud-owner-20261118-001`; their source files on
`E:` remain unchanged. These images are CONFIRMED visual observations. The earlier
owner-provided Imp image is at
`F:\DK2Evidence\captures\visual\possession-first-person--2026-11-15--001\payload\imp-pov-pasted-by-owner.png`.

## Draws and ordering
The creature section accepts camera mode 1 or 2, a live class-0 thing from player
`+0x134`, owned by the interface's player. Definition art `+0x510` supplies the top
portrait and `+0x57C` the melee icon. The three spell positions stay fixed when a slot
is empty or locked; a nonzero spell id at an available level supplies its GUI art.
Selection bit 0 chooses melee and bits 3..7 the spell slot. The group button is in
the lower column, not a fourth top spell.

Health is `+0x40` divided by level-scaled definition health `+0x692`, with a clamped
fill. Its stream is background, pink fill, then tone artwork; that last layer gives
the native gradient appearance. Carried gold `+0x3FB` overlays the Treasury room's
first GUI art. The focused HUD lookup at `0x004213BB` uses world slot `+0x98` with
room type 1, not object 1; [the room lookup](functions/0050D0B0.md) retains its broader
HYPOTHESIS header. Recharge getters `0x0048CA10`/`0x0048C8B0` report 0..100 against
the possession-modified interval, without the combat gate's work-rate divisor.
Partial recharge composes a 0.4-grey icon and bright radial portion. Selection
overlays are drawn after the icon and sector. The read Warlock icon is opaque
(RGBA alpha 255), while selected-creature artwork is translucent.

The numeric layer argument is not append order: layer 1 is in front of layers 2/3.
The top bar repeats behind the top panel. The bottom bar lies behind the minimap
and lower group column. Filters compose sniper, creature-specific and frozen in
that order; the shared frame preserves their relative place in the HUD stream.

Primary kinds 1/2/3/6/7 choose throw/sniper/bat/sneak/pray. Kind 1 uses throw-mode
when player `+0x136` is empty and selection bit 2 clear, throw_book-mode when empty
and selected, and throw_imp-mode for any nonzero carried thing. The latter two
have selection overlays. State flags select the other ability overlays; secondary
kind 5 draws hypnosis with selection while a target is held. The player-owned
creature list supplies follower art `def+0x52B` when `+0x482` is nonzero.

## Native geometry
`SX(n) = trunc(n * width / 2560)` and `SY(n) = trunc(n * height / 1920)` are independent
integer pixel scales ([conversion record](functions/0052C5A0.md)). The mixed axes
below are read from the call arguments; no original widescreen comparison was made
and no suspected original defect is asserted.

| Element | Native geometry |
|---|---|
| Portrait and top action icons | Square, side `SX(128)`; top Y `SY(8)` |
| Top bar | Repeated `SX(512)` by `SY(88)`, Y `SY(20)`, across screen width; the 1440-unit rectangle width is not its clip |
| Bottom bar | `SX(2560)` by `SY(88)`, Y `SY(1792)` |
| Primary / secondary / group column | X `SX(620) - SY(140)`; Y `SY(1460)`, then plus `SX(128)` and twice `SX(128)` |
| Primary and secondary icons | `SX(128)` square |
| Group icon | `SX(128)` by `SX(184)` |
| Follower i | X `SX(620) + SX(8) + i * (SX(128) + SX(8))`; Y `SX(1624) + SY(12)`; size `SX(128)` by `SY(264)` |

The group's 480-unit column at uniform scale matches the native pixel template.
The earlier 492-unit reading was corrected during stack review before implementation.
Health insets and fill dimensions also mix horizontal and vertical quantities; the
shared builder implements the reviewed argument arithmetic rather than a generic
fixed health rectangle. ABI cleanup checks are in [the scalar wrapper](functions/0040CFD0.md),
[art installation](functions/004B37D0.md) and the coordinate conversion record.

## Resources and callees
The source catalogue has 69-byte entries (id, 64-byte name, type); runtime entries
are 27 bytes. Read entries include 69 m-hp_bg, 72 Bar-00, 90 bat-mode, 91 group-mode,
92 pray-mode, 93 sneak-mode, 94 sniper-mode, 95 throw-mode, 96 throw_book-mode,
97 throw_imp-mode, 111 F-Sniper, 112 F-Frozen, 113 m-hp, 114 m-h-tone-bg, 145 m-h-bg,
173 selected-creature, 175 selected-spell, 230 hypnotise-mode, 234 map-2 and
237 selected_group.

`0x00410580` selects overlays 107..110 for creature types 13/28, 22, 25/30 and 12;
id 0 retains the previous selection, other ids clear it. They resolve to F-Knight,
F-Black_Knight, F-Guard and F-FireFly; entries 108/109 store swapped ids.
`0x00410630` draws at screen origin; sniper uses 111 and frozen 112 directly.
`0x00428120` invokes enabled indicator menus 45..50.

## Viewport and north-marker boundaries
The [interface initializer](functions/00402D00.md) supplies interface `+0x10C6`
to [the menu-tree loader](functions/0052B8F0.md). The loader copies globals
`0x00756EEC/0x00756EF0` to receiver `+4/+8` without a 4:3 clamp in those writes.
Its separate 640x480 rectangle is not those dimension fields. The remaining
upstream viewport contract is [Q876](open-questions.md).

The bounded search also checked [interface construction](functions/00402670.md),
[reset](functions/004033F0.md), [frame input](functions/004036E0.md),
[disposal](functions/00402C20.md), [tree construction](functions/0052B870.md) and
[tree clearing](functions/0052BAE0.md). Their records retain the scoped verdicts;
this does not establish a physical-screen or constrained-content interpretation
for the upstream globals. No original widescreen capture was compared.

The [ordinary north callback](functions/0041EDC0.md) uses white RGB. Shipped north
art is grey/white and map-2 has no baked N; probes are under
`F:\DK2Evidence\captures\possession-hud-art-north-20261118-001`.
The owner's native possession BMPs show a red marker. The remake keeps white
pending the possession tint-path read tracked by [Q875](open-questions.md).

## Built scope and retained adaptations
`PossessionHud` builds a `HudFrame`, `PossessionHudView` captures world facts and
`InstallPossessionHudArt` loads behind the install abstraction. Godot presents the
reviewed geometry, health layering, Treasury bag, fixed spell positions, selection
overlays, lower group column and follower geometry. The old four-argument icon
constructor remains available; role is an added init property.

The shared possession minimap uses map-2's empty corners and native per-axis disc
placement. The ordinary minimap builder is preserved. Normal panels and shortcuts
remain blocked while possession holds the camera; the map has no possession input.
ADR-0821 implements a height-fit content policy consistent with ADR-0497. In shared
virtual units, the content X factor is `min(1, width * 1920 / (height * 2560))`.
This caps the raw native X factor on widescreens while retaining it for narrower
viewports. HUD content, the mana/Heart corner, indicators and the minimap share
that factor; backdrop bars and filters still cover the full viewport. This is a
remake presentation choice, not an observed original widescreen rule. The native
per-axis formula above is retained; the content policy does not redefine its
upstream dimensions. Hash/save remain 532.
User UI scale, bottom docking and the stacked indicator layout remain host
adaptations. The float renderer representation has not been checked for equality
with native integer pixel rounding.

The carry-art resolver accepts a distinct possession carry fact. Its native slot is
not modeled by the simulation, so the adapter supplies Empty; ordinary keeper hand
contents must not replace it. Carry/throw implementation limits are in
[special abilities](possession-special-abilities.md).

## Verification boundary
The parent inspected all six format-532 fit-set frames under
`F:\DK2Evidence\captures\possession-finish-20261118-final-532-001`:
`warlock-wide-fit`, `corridor-prayer-fit`, `wall-prayer-fit`, `vampire-tint-fit` and
`vampire-hit-fit` and `virtual-room-pillar-fit`, each `f00000180.png`. Every recording is 1920x1080, 200 frames,
exit 0, with zero runtime/shader errors. All use the same DLL, whose SHA-256 matches
the checkout after recording; no product source changed after its build. Exact
fixtures, commands, hashes and logs are in `capture-manifest-fit-v3.md` and `SHA256SUMS-fit-v3.txt` there.

The Warlock frame shows gradient health contained in its frame, Treasury bag and
6,900 gold, a bright selected melee icon, empty map corners and the complete lower
group icon. Its level-2 fixture keeps the third, unavailable spell position empty.
The parent also viewed supplementary 1600x1200
`warlock-native-aspect-final/f00000180.png` and `hud-comparison-final-1600.png`.
That 4:3 run predates the fit-policy DLL and retains its own metadata; it is not
part of the six-recording common-build set. The native My Pet Dungeon Warlock
and remake Level1 fixture differ in state and mode-specific counters.

Earlier 1920-wide `*-final` recordings are INTERMEDIATE FAILED LAYOUT evidence,
retained with `wide-layout-INTERMEDIATE.txt`; their names do not establish final
validation. The six fit recordings supersede them. Owner acceptance remains open,
as do native integer-rounding parity and Q875/Q876. The 18 native BMPs establish
original appearance, not acceptance of the remake.
