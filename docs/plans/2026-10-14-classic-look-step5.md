# Classic look Step 5 implementation plan

**Goal:** Match DK2's camera and illumination while preserving original design.

**Spec:** [Classic look, Step 5](2026-10-09-classic-look.md#step-5-camera-and-lighting).
Each milestone requires independent review. The owner requested serial builds,
tests and Unity probes; isolated probes must not save or restart the owner's editor.

Rendering rules live in engine-neutral presentation types and the Unity URP
adapter. Discovery needs independent scanned/explored simulation state; its
snapshot and deterministic hash are version260 after master integration. Lighting does not alter simulation.
Original art remains install-backed through the asset provider.

## 5.1 — Reference and baseline

- [x] Inventory binary/data evidence and establish pinned Level1/7/8 views.
- [x] Define viewport, camera, ambient, fog and performance capture controls.
- [x] Independent protocol review.
- [ ] Capture matched original-game reference views with recorded display settings.

Protocol: [Unity probe README](../../tools/classic-unity-probe/README.md).
Owner screenshots establish the requested orientation, not calibrated colour transfer.

## 5.2 — Camera

- [x] Trace authored records, projection, reset yaw, zoom and startup tile-centre focus.
- [x] Implement authored camera and controls; keep whole-map framing diagnostic.
- [x] Test viewport/aspect changes, zoom bounds/locks, yaw wrap, panning and startup focus.
- [x] Independent camera core and startup reviews.

Evidence: [classic-camera.md](../research/classic-camera.md).
The owner accepted the camera view. Native input timing, timed intro/possession
cameras and active-bound transitions remain outside the ordinary camera model.
The owner-requested zoom extension is the explicit deviation in ADR-0478.

## 5.3 — Materials and colour

- [x] Trace texture modulation, RGB packing, boosted byte tints and additive/unlit behavior.
- [x] Apply ordinary terrain and supported room-quarter tint rules; preserve authored materials.
- [x] Verify tag replacement and material/alpha behavior in headless and GPU fixtures.
- [x] Independent material and integration reviews.

Evidence: [classic-lighting.md](../research/classic-lighting.md) and
[KMF materials](../research/kmf-materials.md).
No arbitrary exposure or gamma correction substitutes for a missing native input.
Matched framebuffer colour transfer remains part of the original-game acceptance gate.

## 5.4 — Lighting

- [x] Trace torch/terrain sources, radius response, bounded selection and rolling cache stamps.
- [x] Implement native camera-volume clipping, tile enumeration and cache preparation.
- [x] Publish automatic world sources and authoritative per-tile GPU tables.
- [x] Implement static selection masks and both native animated lighting arms with authored bounds.
- [x] Verify removal, camera movement, borders, all24 slots and resource lifecycle.
- [x] Independently review arithmetic, visibility, cache, shader and integrated publication.
- [x] Render the companion animated torch resource; remaining clock fidelity is Q604.
- [x] Apply confirmed ordinary-top normal inversion and ordinary-object instance-lighting selection.
- [x] Trace and publish authored object lights; retain the scoped limits in static-mesh-lighting.md section4.

Evidence: [classic-lighting.md](../research/classic-lighting.md),
[visibility](../research/classic-light-visibility.md), ADR-0484 and ADR-0487.
Cheap Lighting uses uniform radial light at animated draw translation; ordinary
lighting uses the separately sampled instance arm. Their GPU fixtures preserve
the binary's unusual centre/transform convention rather than normalizing it away.

Preserve the owner-accepted brighter preview with Native Initial Ambient off.
Native startup ambient is confirmed zero; the +/− control is implemented without
registry persistence. Exceptional singular arithmetic, exact framebuffer transfer and
original brightness acceptance remain explicit limits, not invented fill lights.

## 5.5 — Discovery and fog presentation

- [x] Separate permanent scanned/explored state and implement researched producers.
- [x] Render Fog/Claim Scan/reveal-through-fog distinctions without exposing hidden objects.
- [x] Produce Sight of Evil's authored shot/first-update region request.
- [x] Render the separate authored Sight fog overlay and its native opacity.
- [x] Filter torches/effects with current visibility, including ordered worker-tick events.
- [x] Independent simulation, presentation, shader and event-ordering reviews.

Evidence and producer limits: [fog-reveal-sources.md](../research/fog-reveal-sources.md)
and [keeper-spell-sight.md](../research/keeper-spell-sight.md).
The discovery cadence deviation and missing game-mode seed are documented there.
No mode is inferred merely from campaign selection. Sight's spell effects outside
its visibility mask remain outside this camera/lighting step.

## 5.6 — Performance

- [x] Compare quiet and torch-heavy Level8 views with fixed world/camera conditions per view.
- [x] Measure native preparation and URP submission; calibrate allocation counters.
- [x] Reuse prepared cells, source buffers and unchanged-frame visibility snapshots.
- [x] Independent performance-path review.
- [x] Obtain usable GPU frame timings and a windowed player-loop frame-time capture.

Measurements and limits: [verification](../research/classic-lighting-verification.md).
Submission time is CPU time, not GPU execution or a claimed game frame rate.
Scoped allocation counters failed calibration; global player allocations remain
unattributed. Measurements do not justify reducing original visual fidelity.

## 5.7 — Final comparison

- [x] Review all supported implementation milestones with independent subagents.
- [x] Complete final combined regression, adapter, documentation and KB checks.
- [x] Finish final Level1/7/8 campaign captures and inspect their images.
- [ ] Compare pinned original/Unity views and obtain owner visual acceptance.
- [ ] Resolve remaining dark arch faces and Heart smoke colour gaps (Q603, Q605).

This step is not accepted solely because tests pass. The original-view gate
requires the owner to compare the same position, viewport and display settings.
HUD, sound, replacement assets and unrelated simulation parity are outside Step5.
