# GPU vertex animation implementation plan

**Goal:** Complete modern-rendering audit item 8 only, preserving the drawn pose and exact CPU picking.
**Architecture:** Keep the shared animation clock and existing CPU-upload surface contract. An optional pose surface receives the segment and phase instead. Godot shares one mesh and floating-point position texture per decoded clip; the world shader interpolates before lighting and camera transforms.
**Constraints:** Unity is frozen. No simulation changes, commits, index changes or cleanup. Captures, archived HEAD and performance output live under the owner's C: temporary directory. All runs isolate player files and engine logs.

## Implementation

- [x] Add regression tests for pose-only draws, lazy full sampling, tagged-point sampling, segment replacement and stop/restart. Keep existing upload tests.
- [x] Expose read-only decoded frames and single-vertex interpolation in RenderAnimation; preserve Sample(float, bool, Vector3[]).
- [x] Add the optional pose surface and cached on-demand full sampling to CreatureAnimationPlayer. Control points and lightning sample their vertices directly.
- [x] Pack Godot-frame RGB32F positions by source vertex and frame. Supply source indices through CUSTOM2. Share the template mesh and material variants per clip, including additive next passes.
- [x] Interpolate positions in the world vertex shader using the instance phase. Bound each instance by all clip frames; reset animation parameters and bounds when returning to static art.
- [x] Sphere-reject hover candidates before requesting exact posed bounds and triangles. Preserve existing admission and tie rules.

## Review focus

- Segment changes and deferred replacement must invalidate the sampled pose, including identical phase values across different clips.
- Main, possession, preview and shadow cameras must use the same posed vertices and clip-wide bounds.
- Hover environment and red next passes must use the same clip texture and instance phase.
- Level reload must release clip resources with its WorldScene; no stale texture or animation state may survive.
- Warm draws and CPU sample queries must allocate nothing after first use.

## Verification

- [x] Run shared animation, picking and hover tests, then the full Release suite and both requested builds.
- [x] Archive and build HEAD separately; import and build solutions with the pinned Godot console binary.
- [x] Capture the requested 1080p scenes with scripted pointer and both lighting configurations. Compare decoded RGBA pixels; inspect every difference as HEAD/new/difference crops.
- [x] Interleave HEAD and new perf.ps1 rounds for Level1 and Level7, at least two rounds each. Record draw calls, frame/GPU/render-CPU p50, allocation and eliminated clip-copy/upload metrics.
- [x] Verify the turn-400 hash B5C0CB842094AB59.
- [x] Perform sceptical self-review, then independent reviewer and documenter passes. Update README, audit, remaining replacements and applicable KB records; run docs and kb gates. Leave changes uncommitted.
