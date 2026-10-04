# Sight of Evil: a temporary presentation mask

Read 2026-10-04 from DKII.EXE v1.70 and shipped data. Raw output remains in
`reverse-engineering/.cache/`. This stage covers Sight; Reveal Map remains Q194.

## 1. Descriptor and position cast

**CONFIRMED.** `KeeperSpells.kwd` record56: Sight Of Evil id1, location5,
target4, shot33 for normal and upgraded casts, data1=5/7, data2=0, mana5000,
upkeep0 and cooldown0. `Shots.kwd` record7704: shot33, process25, flags4,
process flags0x42, health64, speed2048, radius819 and no damage/collision type.

The shared position permission [004BB8A0](functions/004BB8A0.md) applies mana,
cooldown and the selected validators. Location5 always passes; target4 applies
the existing own-room exclusions. [005177B0](functions/005177B0.md) identifies
target4 as a place. [004BBD30](functions/004BBD30.md) makes the selected shot,
copies normal or upgraded data, pays once and stamps both clocks.
The selector records are [005176B0](functions/005176B0.md),
[00517F60](functions/00517F60.md) and [005179D0](functions/005179D0.md).

[004AD1B0](functions/004AD1B0.md) registers process25's callback
[004AF0E0](functions/004AF0E0.md). [004AD730](functions/004AD730.md) dispatches
events0x40 on the first update and0x2 before movement. Definition health64 is
widened to shot health by [004AAC90](functions/004AAC90.md); the flags4 countdown
in [004AB180](functions/004AB180.md) supplies64 shot updates. This countdown is
distinct from the presentation region's clock.

## 2. Local presentation region

**CONFIRMED.** On its first callback, process25 sets shot `+0x169`. Only when
the owner is the local player and the full dword data1 is nonzero does it post
command13, with position and a packed word: low16 shot health, high16 data1.
The later health20 callback takes a separate effect0xE8 path.

[00517480](functions/00517480.md) and [00527150](functions/00527150.md) deliver
command13 to [00448580](functions/00448580.md). The consumer extracts duration
and radius, caps the radius at7, converts position to tiles and calls
[00441510](functions/00441510.md); it also creates presentation effect0x2D.

There is one active region on this presentation object. A new cast replaces it,
requests redraw of the previous region, and stores start `+0x139F` from
`bridge[1]+4`, end `+0x13A3` as start+duration, radius `+0x13C7`, and clipped
center-minus/plus-radius bounds `+0x13A7/+0x13AB/+0x13AF/+0x13B3`.
These bounds are upper-exclusive at their visibility test; they are not a
permanent square reveal.

[00441B00](functions/00441B00.md), presentation virtual slot `+0xDC`, evaluates
a time-dependent radial alpha inside those bounds. Its result admits an
unexplored tile in both terrain-rendering paths
[0043C310](functions/0043C310.md) and [0043CC30](functions/0043CC30.md).
[0043B220](functions/0043B220.md) retains the region through equality and clears
the start/end stamps only when unsigned bridge-clock now is greater than end.
Active updates request redraw6, including expiry. The shipped interval is64
bridge-clock units; its conversion to simulation ticks/seconds remains Q534.

## 3. Discovery and explored state

**CONFIRMED mechanism and audited storage boundaries.** This is an additional
temporary local rendering predicate, not a write to the scanned `tile+0xB`
bit0x40 or explored `tile+8` word bit0x2000. The process, registration and
render gates do not set those bits. The redraw path goes through
[00447CE0](functions/00447CE0.md), [004484A0](functions/004484A0.md) and a
separate presentation tile cache; it reads the actual fog bits.
[004435E0](functions/004435E0.md) addresses two-byte cells in the grid at
bridge+0x326. The world+0x1E4 callback [00508CA0](functions/00508CA0.md) forwards
to [00451C60](functions/00451C60.md), which updates a minimap colour buffer.
The direct stores in presentation+0xB8 update model/effect fields; its indirect
graphics dependencies were not exhaustively audited.
The absence claim is scoped to these audited Sight bodies and their renderer
predicates, rather than every possible indirect write in the executable.
The field-reader census covered both code blocks, including cseg, and found30
offset matches with the known registration setter as positive control.

The ordinary minimap predicate [00451C90](functions/00451C90.md) and lighting
terrain selector [00452DC0](functions/00452DC0.md) retain their existing fog-bit
rules. The indirect creature-creation revealer [004AD7A0](functions/004AD7A0.md)
is process1, distinct from Sight's process25. See `fog-reveal-sources.md` for
the discovery-writer census.

Supporting presentation reads are retained in the records for
[00439B90](functions/00439B90.md), [00441370](functions/00441370.md),
[00527270](functions/00527270.md) and [005272C0](functions/005272C0.md), with
their scoped or provisional roles. They do not establish a clock-unit mapping.

## 4. Build and observation boundary

**PRESENTATION ONLY: intentionally unbuilt in Stage6.** The task excludes
presentation; permanent simulation discovery would reproduce the wrong effect.
No simulation cast, shot handler, fog state, hash or snapshot field is added.
Hash/save174 and the determinism pin remain unchanged. The modern presentation
view currently has no temporary Sight region and no separate explored layer.

`KeeperView` captures raw terrain/owners/navigation, all rooms and all creatures,
without filtering `DiscoveredBy`. Sight therefore grants it no additional
information in the current adapter. This does not settle every original AI
fog policy; Q190 retains that boundary. The original computer Sight step is
[005003A0](functions/005003A0.md), without newly implementing its M12 policy.

Stage5 and its History correction are independently approved. Stage6's Release
and Unity builds have zero warnings/errors; the no-install suite passes3407,
fails0 and skips396. Ledger,750-record KB,coverage and documentation checks pass.
All42 scenario runs produce178 artifacts identical to Stage5. Against Stage0,
94 remain exact and84 differ only in hash composition; no new behavior changes.
Independent Stage6 review is pending (2026-10-04).
