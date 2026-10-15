# Classic look Step 5 implementation plan

> For agentic workers: execute milestones in order using executing-plans. The owner requires an independent subagent review of every milestone.

**Goal:** Match DK2's camera and illumination while preserving simulation behavior.

**Architecture:** Camera and authored rendering inputs belong in engine-neutral
data/presentation types. Unity applies those inputs through the existing URP
adapter. Original assets remain install-backed; rendering changes do not alter
gameplay state, RNG or hashes.

**Tech stack:** C#, netstandard2.1, Unity 6000.6, URP, existing KWD readers.

**Spec:** [Classic look plan, Step 5](2026-10-09-classic-look.md#step-5-camera-and-lighting).

## Constraints and review focus

- Read the binary before implementing an original rendering rule; keep raw
  output under ignored reverse-engineering scratch and findings in the KB.
- Preserve local editor settings and unrelated master changes.
- The owner requested low CPU usage: one build/test process at a time,
  targeted tests, and no repeated dk2tools status/coverage sweeps.
- The owner authorized launching Unity and accepted the camera view on 2026-10-14.
  Runtime comparisons and profiling remain pending, never inferred from tests.
- Review aspect changes, camera rotations, zoom bounds, material blending,
  hidden information and dense torch scenes explicitly.

## 5.1 — Reference and baseline

**Files:** this plan; tools/classic-unity-probe/README.md; canonical camera,
torch, material and fog research notes.

- [x] Inventory existing evidence and identify renderer choices still unproved.
- [x] Define repeatable Level1/7/8 comparisons and capture settings, recording
  active camera, pipeline, ambient mode, lights and performance measurements.
- [x] Request independent review; retain absent captures as pending.

**Deliverable:** A repeatable comparison protocol and evidence map. Documentation
readiness can pass headlessly; the runtime baseline requires owner availability.
Protocol review passes (2026-10-14); runtime baseline remains pending.

Evidence map:
- [Camera](../research/classic-camera.md): shipped profiles and native projection;
  exact startup sub-tile focus and input timing remain separate comparison limits.
- [Torches](../research/wall-torches.md): placement and authored light inputs are
  researched; native falloff/accumulation and Unity response mapping are not equivalent yet.
- [Materials](../research/kmf-materials.md): texture modulation and brightness;
  colour-space/exposure parity remains to be verified.
- [Room floors](../research/room-floors-and-liquids.md) and
  [walls](../research/wall-pieces.md): native terrain tint inputs; full lighting is unbuilt.
- [Discovery](../research/fog-reveal-sources.md): two native bits and scanned terrain;
  current presentation does not express their complete distinction.
- [Controls](../research/controls.md): original ambient/gamma are user-adjustable.

## 5.2 — Camera

**Files:** camera loading in DK2.Data; a ClassicCamera presentation model;
DungeonRunner camera input/application; presentation camera tests.

- [ ] Trace record loading, projection, orientation, zoom and starting focus.
- [ ] Pin shipped camera records and projection at multiple viewport sizes in
  tests; exercise zoom saturation, yaw wrap and rotated panning.
- [x] Replace arbitrary camera framing with authored ordinary-play settings;
  retain explicit whole-map framing as a diagnostic mode.
- [x] Run targeted tests and adapter compilation; request independent review.

**Deliverable:** Authored camera settings and controls. Native input timing and
intro/possession cameras remain separate unless their rules are researched here.
The camera core passes fourteen headless cases, including the Level1 northern-gold
landmark and the native matrix constructor's yaw/roll signs.
Its scoped independent review and startup follow-up review pass (2026-10-14).
Starting sub-tile focus and rendered framing remain pending original comparison.

## 5.3 — Materials and colour

**Files:** ClassicMeshes, existing classic shaders and material tests.

- [ ] Compare native texture modulation, vertex light and brightness with the
  current colour-space, emission and transparency paths.
- [ ] Test confirmed conversions and blending before changing the adapter.
- [ ] Correct evidenced discrepancies; request independent review.

**Deliverable:** Preserved texture/owner colours with documented colour-space
and brightness rules. No arbitrary exposure tuning to hide missing lights.
Authored terrain colour loading, boosted channels and scoped whole-room/wall
tint application are built; 25 terrain data tests, 322 presentation tests and
adapter compilation pass. Independent scoped review passes (2026-10-14);
runtime comparison remains pending. Tags now replace terrain instance tint while
preserving material self-illumination; the four-case Unity material probe and
independent review pass (2026-10-15). Final colour transfer, quarter tint and
unlit tint remain unproved.

## 5.4 — Lighting

**Files:** terrain/room light definitions and readers as needed; presentation
lighting outputs; DungeonScene light application; focused lighting tests.

- [ ] Trace ambient, torch, terrain/lava and Heart light inputs and falloff.
- [ ] Implement confirmed inputs through the existing URP path; replace global
  scene defaults only when their replacement has evidence.
- [ ] Check active renderer/light-selection behavior before attributing torch
  disappearance to a per-object light cap.
- [ ] Verify light placement/colour/visibility headlessly, then review independently.

**Deliverable:** Evidenced world illumination. Torch disappearance and rendered
brightness require a fixed-world camera-motion comparison when Unity is allowed.
Torch input normalization, radius coefficients and the discrete lookup response
are traced; a headless reference and shared adapter input conversion are built
and independently reviewed (2026-10-15). The 348-case presentation suite passes.
Vertex RGB packing before texture modulation is traced and independently
verified. An opt-in URP vertex shader and GPU torch buffer are built; source
review corrected offscreen cluster access. Arithmetic GPU checks and seven actual
URP render cases pass with SRP Batcher enabled; independent integration review
passes (2026-10-15). The preview remains opt-in pending scene comparisons. See classic-lighting.md sections5?7 for the
singular-bin contract and preview limits. Framebuffer transfer, global/terrain
illumination and matched runtime comparisons remain pending.
Production scene check (2026-10-15): torch on/off captures confirm illumination
at startup and after camera movement; the owner's reported absence is weak
contrast, not a demonstrated missing light. A native-initial-ambient diagnostic
passes the placeholder-sun isolation test; it stays off until missing fill
inputs are traced, because unlit tops/arches otherwise turn black. Creature
silhouettes are built under ADR-0482 and pass independent review and GPU checks;
source qualifications, native bounds and original quality integration remain
explicit preview limits. The owner accepted the brighter preview with native
initial ambient disabled and confirmed visible Imp shadows (2026-10-15).
Matched-original acceptance and busy-view profiling remain pending. Terrain
point-light height loading/writing and the bounded light-selection core are built
and independently reviewed; the presentation suite passes355 cases. Native
source gathering is resolved in Q597. Explicit selected-table Unity publication,
terrain/transparent/liquid tile anchors and authoritative empty-table handling
are built and independently reviewed (ADR-0484). GPU and production-scene
verification are recorded in classic-lighting.md section10. Prepared-cell rolling
candidate order is built (ADR-0487); automatic cache visibility, dirty-column
invalidation and lighting-mode publication remain unbuilt. The default
Cheap Lighting option selects the separate animated instance-sampled torch arm
(section9). Its headless reference is built and independently reviewed (section3);
the animated caller's centre/transform and mode integration remain unbuilt.

## 5.5 — Discovery and fog presentation

**Files:** WorldView visibility/terrain selection; reveal-state tests;
fog-reveal-sources.md as the canonical evidence source.

- [ ] Trace scanned versus explored rendering and existing reveal exemptions.
- [ ] Test ordinary, scanned, explored and reveal-through-fog tiles, including
  room/object visibility; implement supported presentation distinctions.
- [ ] Keep any missing simulation discovery state explicit rather than treating
  it as cosmetic; request independent review.

**Deliverable:** Correct visible terrain and hidden information for supported
discovery states. No invented distance fog or dimming approximation.
The existing scanned producer's prisoner/closed-door guards and per-term rounding
are corrected and independently reviewed; eight focused tests pass. Independent
explored state, its sight producer and rendering distinction remain unbuilt.

## 5.6 — Performance

**Files:** existing profiler/capture tooling and the measured bottleneck's owner.

- [ ] Compare equivalent quiet and busy Level8 views using the baseline protocol.
- [ ] Optimize only measured costs; check frame spikes, allocations, light
  submission, transparency and changed-chunk work.
- [ ] Verify unchanged simulation results and request independent review.

**Deliverable:** Before/after CPU/GPU evidence, not a promised frame rate.
Runtime profiling remains pending; the Unity editor is available.

## 5.7 — Final comparison

- [ ] Run the affected tests and one combined build/check pass.
- [ ] Compare camera/material/light/fog results at the pinned reference views.
- [ ] Request final milestone review and owner visual acceptance; list unresolved
  differences in their canonical questions rather than closing them by assumption.

**Deliverable:** Owner-accepted Step 5 with reproducible evidence. HUD, sound,
modern replacement assets and unrelated simulation parity are outside this step.
