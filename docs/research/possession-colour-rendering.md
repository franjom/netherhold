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
another freeze. A positive hit scaled to zero still starts a damage ramp. Subscriptions follow the
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

`PossessionColours.Output` defaults to the normal user-gamma ramp. Explicit
`ApplyColourChanges` selects the current transition table; Godot consumes this
output. Tables, queue and session notifications remain active behind the gate.
ADR-0822 governs this conservative application policy; native observations and
the unresolved enable path remain in Q745 sections 1–2.

## Verification boundary

Tests cover table kinds, gamma, bands, interruption, queue capacity, transition
timing, same-frame damage/heal and thaw/refreeze, zero-rounded hits, world replacement,
gamma updates across current/start/target tables, preservation of the queue on an
unchanged gamma setting, default-off normal gamma, explicit enablement without
queue loss, and observer hash neutrality.
CRT/x87 versus managed floating-point last-bit equivalence has not been checked
with the oracle.
The corrected default passed 181 tests; Release solution and Godot builds had
zero warnings/errors. Hash/save remains 532. The parent inspected
`F:\DK2Evidence\captures\possession-colour-enable-correction-20261118-001\vampire-untinted\f00000180.png`
(1920x1080, 200 frames at 30 fps, exit 0, no runtime or shader errors). The world
has yellow/brown torchlight, the HUD retains green mana and pink health, and
there is no overall red Vampire wash. The adjacent
`vampire-untinted-metadata.json` records the command and SHA-256. Wall texture
and lighting seams remain visible. The staged campaign room differs from the
native My Pet Dungeon scene; this does not establish pixel parity.

Earlier tinted remake captures remain preserved as superseded implementation
evidence, not native-fidelity validation. Native evidence has one home in Q745
section 1. Owner acceptance and last-bit floating-point equivalence remain open.
