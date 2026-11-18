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
with the oracle.
The parent inspected `f00000180.png` in the final format-532 `vampire-tint-fit`
and `vampire-hit-fit` directories under
`F:\DK2Evidence\captures\possession-finish-20261118-final-532-001`.
The settled Vampire tint and stronger red runtime poison-hit flash cover the world
and possession HUD. Foreign Imps were relocated to valid neutral-player positions;
the earlier fixture clipping is absent. Shared capture parameters and the matching
build are recorded in [HUD verification](possession-hud.md#verification-boundary).
Earlier format-531 images remain valid observations of the pre-correction HUD.
The older `colour-001` and `visibility-001` recordings are excluded as validation
because their missing-player-4 fixture was invalid. No independent native colour
comparison or owner acceptance is claimed.
