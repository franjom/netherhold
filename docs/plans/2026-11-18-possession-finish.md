# Finish possession

**Goal:** complete the owner's possession task on `codex/possession-finish`, from
master `1fb1c15b`, leaving the worktree and branch for review without merge or push.
**Architecture:** existing engine-free possession session and rules own behaviour;
Godot partials upload draw lists, geometry and ramps and forward input (ADR-0805).
**Spec:** the owner's task of 2026-11-18; existing design is
[possession view](2026-11-14-possession-view.md). ADR-0813 governs rendering.

## Work and verification

Each numbered item has its own commit after tests, binary review, fixes and the
documenter pass. Unread branches remain unbuilt with a bounded-search account.
All edits and Git staging are confined to this worktree. Native captures are
add-only under `F:\DK2Evidence`; no cleanup of worktrees or evidence is authorized.

- [ ] 1. Colour ramps: `PossessionScreenColours` and tests, creature-kind parsing,
  event driver and Godot final-screen pass including HUD. Search gamma enable writer.
- [ ] 2. Godot fade implemented; distance-to-black capture proof and owner acceptance remain.
  See [possession-warp section 4](../research/possession-warp.md#4-the-possession-distance-fade-strongly-inferred-implementable)
  for verification and the owner-requested settled-only scope deviation.
- [ ] 3. Godot possession visibility implemented; GPU capture and owner acceptance remain.
  Scope and verification: [possession-warp section 8.3](../research/possession-warp.md#83-the-enumeration-strongly-inferred-whole-disassembly-of-0x005737e0-and-0x00572f60).
- [ ] 4. Shared HUD subset and Godot canvas built; GPU capture and owner acceptance remain.
  Scope and limits: [possession HUD](../research/possession-hud.md).
- [ ] 5. Supported ability data, commands, keys, sniper and save/hash fields built (ADR-0819).
  Carry/throw physics, foreground transform/renderer and native action136 room mapping
  remain open; [scope and bounded reads](../research/possession-special-abilities.md).
  Ability capture and owner acceptance remain.
- [ ] 6. Room-pillar roof and explicit object-death view built; bounded lighting, listener
  and mouse reads recorded in [client completion](../research/possession-client-completion.md).
  Shot creation-effect sprite upload is built ([effects](../research/shot-effects.md#7-ours));
  sound playback and the object-possession bridge remain unbuilt. GPU acceptance remains.

For every item run the requested Release solution build, Godot build, touched
Presentation tests (plus simulation/determinism tests for item 5), KB and docs gates.
Visual items require 1920x1080 Movie Maker captures from the post-opening save,
including tint, damage, distance fade, occlusion, HUD and an ability. Inspect frames.
Use temporary settings/profile/save/hiscore paths and remove only `override.cfg`
created for a capture, as the owner explicitly requests.

## Review focus

- Interrupted and repeated colour events blend from the current ramp; queue limit 8.
- Fly-in/out never inherits settled-only fade, occlusion or first-person input.
- Hidden tiles hide all content and skip off-map entries without indexing errors.
- Ordinary HUD input stays blocked across entering, possessing and leaving.
- Level/spell gates, replay serialization and deterministic hashes follow binary reads.
