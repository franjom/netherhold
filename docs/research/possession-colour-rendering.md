---
systems: [camera-lighting, creatures, presentation-godot]
---
# Possession colour rendering

The binary rules and per-creature byte mapping have one home:
[Q745](screen-colour-changes.md). This note records implementation and boundaries.

## Engine-free model

`ScreenColourTables` builds the sixteen three-channel 256-entry ramps.
`ScreenColourChange` holds the current/start/target tables and eight-entry queue;
new changes interrupt from the current table. `PossessionColours` takes session
events, starts the creature ramp during the last 256 ms of entry, and selects
damage, thaw, death and leave transitions from Q745.

Damage and thaw are transient `CreatureState` notifications from the health and
status rules. `PossessionSession` retains their order until the next rendered
frame, including damage immediately healed and thaw immediately followed by
another freeze. A positive hit scaled to zero still flashes. Subscriptions follow the
creature object across world replacement, even when its id is unchanged. These
notifications have no saved or hashed state. Their clock
is the receiving presentation frame, matching a client bridge drained per frame
(STRONGLY INFERRED for the event durations; the remake queue is an implementation choice).

## Full-screen pass

Godot uploads the ramps as a nearest-filtered 256-pixel RGB float texture and
applies them in one canvas pass at layer 110 after the scene, HUD and cursor. The input is quantized
to the nearest byte. The previous scene-only brightness adjustment is held at
one; the final ramp applies the options gamma once.

The pass includes the HUD following Q745 section 1's STRONGLY INFERRED hardware
gamma scope. Shader-layer placement, byte quantization, and a float texture are
remake representation choices.

Tints are enabled unconditionally, following the owner's CONFIRMED observations.
A bounded re-search on 2026-11-18 found only two absolute references to
`0x00759AC0`, the read and disable clear. The settings reset/load reread found
no `+0x48` store. The enable writer remains UNREAD; see Q745 section 2.

## Verification boundary

Tests cover table kinds, gamma, bands, interruption, queue capacity, transition
timing, same-frame damage/heal and thaw/refreeze, zero-rounded hits, world replacement,
gamma updates across current/start/target tables, preservation of the queue on an
unchanged gamma setting, and observer hash neutrality.
CRT/x87 versus managed floating-point last-bit equivalence has not been checked
with the oracle. No native visual comparison or owner acceptance is claimed.
The isolated item-1 snapshot (hash/save 530, pin `0x976F11DAF71E06FC`) built the
Release solution and Godot with zero warnings/errors. Presentation 32, Data 25,
Simulation 33 and frame-order 3 focused tests passed, with no skips. The refreshed
Level 1 replay retained 2,926 ticks and 164 command frames; all 2,927 hashes matched
a strict replay, with header/commands unchanged. Knowledge generation covered 15 rooms.

The parent inspected 1920x1080 MovieMaker frame `f00000180.png` in each of
`F:\DK2Evidence\captures\possession-finish-20261118-colour-001\vampire-tint-mana`
and `vampire-hit-mana` (200 frames each). They show the settled Vampire tint and
an actual runtime poison-hit flash over the world and ordinary HUD. Mana 200,000
was staged after post-opening turn 130. Earlier `vampire-tint`/`vampire-hit` captures
lost possession before entry completed and do not establish the tint. The possession
HUD replacement is pending in this snapshot. Logs contained no shader errors and
three existing missing Godot AI autoload errors; the temporary override was removed.
