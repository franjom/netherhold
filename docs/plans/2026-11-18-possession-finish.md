# Finish possession

**Goal:** complete the owner's possession follow-up on the retained
`codex/possession-finish` worktree and branch, leaving fixes for review without push.
**Architecture:** existing engine-free possession session and rules own behaviour;
Godot partials upload draw lists, geometry and ramps and forward input (ADR-0805).
**Spec:** the owner's task of 2026-11-18; existing design is
[possession view](2026-11-14-possession-view.md). ADR-0813 governs rendering.

## Work and verification

Each numbered item has its own commit after tests, binary review, fixes and the
documenter pass. Unread branches remain unbuilt with a bounded-search account.
All edits and Git staging are confined to this worktree. Native captures are
add-only under `F:\DK2Evidence`; no cleanup of worktrees or evidence is authorized.

- [x] 1. Colour tables, queue and session driver are built. Application is developer-only
  and off by default (ADR-0822), with a documented developer launch path; corrected native observations: [Q745](../research/screen-colour-changes.md).
- [ ] 2. Godot fade implemented; final corridor capture inspected, owner acceptance remains.
  See [possession-warp section 4](../research/possession-warp.md#4-the-possession-distance-fade-strongly-inferred-implementable)
  for verification and the owner-requested settled-only scope deviation.
- [ ] 3. Godot possession visibility implemented; final wall/corridor captures inspected,
  tile-edge checks and owner acceptance remain.
  Scope and verification: [possession-warp section 8.3](../research/possession-warp.md#83-the-enumeration-strongly-inferred-whole-disassembly-of-0x005737e0-and-0x00572f60).
- [ ] 4. Shared HUD corrections and height-fit policy built (ADR-0821); final wide-fit
  captures inspected, owner acceptance remains.
  Scope and limits: [possession HUD](../research/possession-hud.md).
- [ ] 5. Supported ability data, commands, keys, sniper and save/hash fields built (ADR-0819).
  Carry/throw physics and native action136 room mapping remain open; [scope and bounded reads](../research/possession-special-abilities.md).
  Authored foreground attacks and creature filters are built ([scope](../research/possession-foreground.md));
  action and transition-pointer highlighting, including ordinary badges, are built
  ([scope](../research/possession-target-highlight.md)). Owner acceptance remains.
- [ ] 6. Room-pillar roof and explicit object-death view built; bounded lighting, listener
  and mouse reads recorded in [client completion](../research/possession-client-completion.md).
  Shot creation-effect upload and qualitative entry-star visibility are checked
  ([Q749](../research/possession-entry-effect.md)); sound playback and the object-possession
  bridge remain unbuilt. Native visual parity and owner acceptance remain.

Before each review-fix commit run the Release solution and Godot builds, full suites
of every test project without filters, and KB, docs and status gates.
Visual items require 1920x1080 Movie Maker captures from the post-opening save,
including distance fade, occlusion, HUD, a current-action target and an authored swing.
Tints and hit flashes stay off; the corrected native observations supersede that capture target.
Compare the Black Knight visor at 1600x1200 and label widescreen fitting as an adaptation.
Entry staging focuses the ordinary camera before possession and projects the authored
creation request. Roof entry timing follows the [endpoint gate](../research/kwd-terrain-rooms.md#ceiling-the-possession-view-roof);
ordinary HUD display remains during entry, then the [possession panel](../research/possession-hud.md#entry-and-landing)
and visor appear at landing. Capture scope is in Q749.
Preserve `PossessionFlight`: the owner has [confirmed the move](../research/possession-client-completion.md#entry-and-return-observations).
The session's entry/return HUD marker changes display bookkeeping only.
Use temporary settings/profile/save/hiscore paths and remove only `override.cfg`
created for a capture, as the owner explicitly requests.

## Review focus

- Interrupted and repeated colour events blend from the current ramp; queue limit 8.
- Fly-in/out never inherits settled-only fade, occlusion or first-person input.
- Hidden tiles hide all content and skip off-map entries without indexing errors.
- Ordinary HUD input stays blocked across entering, possessing and leaving; Options/GUI shortcuts wait for settled first person.
- Level/spell gates, replay serialization and deterministic hashes follow binary reads.
