# Runtime sound requests

## Decoder boundary

The runtime media reader and decoder are in DK2.Data.Media (ADR-0583). NLayer3.0.0 decodes
FrontEndHD entry0 to26112 mono samples (68 LayerI frames) and Mentor entry0 to58752 mono
samples (51 LayerII frames), both22050Hz. No derived assets are written to disk.

## Category and sample selection

CONFIRMED from DKII.EXE v1.70, read in Ghidra2026-10-29:

- `0x00532670`: label hover requests group783 (0x30F) in the front-end category.
  Its sound latch resets after the eight-frame glow fade, rather than immediately on leaving.
- The widget table at `0x006AC8C4`, stride48: slots32..36 name OBJECT_FE_PARCHMENT
  and press groups778..782. Their release ids are zero. Slot31 names FRONT_END.
- `0x00567810`: sound manager slot36 forwards the category and group id into the category
  service, rejecting disabled service/effects and zero category/id.
- `0x00608C20` resolves the category then `0x00614C90` scans its20-byte group records
  for the exact id. Empty groups yield no instance. A live handle retains equal/lower-priority requests even when the event changes.
- `0x0060DA70` chooses the player class from group flags; linked players are separate
  from ordinary effects. Ordinary shipped interface groups have one item.
- `0x0060CFF0`: one sound returns without RNG. Otherwise advance the sound LCG
  (1664525*x+1013904223), rotate right13, take the high16 bits, choose the first sound
  whose cumulative32-bit weight is >= that draw, or fall back to the first sound.
- Archive and sample indices are1-based (music.md section5); runtime entry ids are0-based.

STRONGLY INFERRED from OpenKeeper's BankMapFile/SfxMapFile, then parsed against shipped
maps: the map readers preserve unknown byte fields rather than assign them invented names.
The front-end hover resolves to Global\FrontEnd entry0; press778 to Global\Gui entry7.

## Traced wrappers

`0x00567730` is the manager's category-service request wrapper with a zero category field;
`0x00567210` returns its initialized flag. `0x00608A00` forwards to `0x0060E520`;
`0x00608A10` forwards to the underlying service's slot8. `0x00608B70` is a category-load
wrapper with a service lock; its full loading contract is not established here.
`0x006157F0` constructs an ordinary player and invokes its initialization; `0x00615920`
installs vtable0x00674070 after base construction; `0x00615940` returns3.
`0x00568490` plays a queued speech event through manager slot36 and applies temporary
music/effect volume ducking; exact mixer behavior is not implemented by this read.

## Playback ownership

`SoundVoiceBindings` revokes a source's previous voice whenever the pool reassigns it,
including unvoiced reuse. It preserves the native equal-or-higher priority retention rule
above. Regression tests cover both ownership transfers and the inclusive battle deadline.
`MusicWorldObserver.Battles` exposes eight read-only snapshots with typed slots, tile centres,
owner masks and member counts for presentation consumers; it never writes world state.

Creature sound requests share this playback path; the battle-call trigger and
its acceptance limits are in [combat-fight-state.md section 9](combat-fight-state.md#9-battle-call-on-approach-2026-10-31).

## Category choice presses

CONFIRMED bytes: room widget0x0068FCB8, spell widget0x0068FDF8 and Workshop widget0x0068FF38
all carry sound slot5 at+0x4C. The generic GUI sound table0x006AC8C4+5*0x30 names
GUI_BUTTON_ICON_LARGE with press0x104/release0x136. The model emits once per captured
primary press/release and leaves unknown/unselectable choices silent. This does not change
the existing choice-selection timing.

## Spoken subtitles

With speech enabled, the path overlay follows the actively playing level line's subtitle
flag and index; no line means no subtitle. With speech disabled it keeps the parallel timed
queue (camera-paths.md section7). Common notifications do not acquire a level subtitle flag.
The runtime poll changes only presentation state. Regression advances the wall clock beyond
the text timer while retaining an active voice, then verifies completion and speech-off fallback.
