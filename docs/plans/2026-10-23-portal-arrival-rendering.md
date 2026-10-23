# Portal arrival rendering implementation plan

> Execute inline with the executing-plans skill; preserve the owner's Step6 scene edits.

**Goal:** Restore the original portal arrival's continuous white light and investigate its final colour.

**Architecture:** Keep simulation unchanged. Submit flat effect quads and additive effect meshes to the existing transparent triangle stream so they interleave with the Portal chasm and crystals.

**Tech stack:** Unity 6000.6, URP 17.6, C# presentation adapter.

**Spec:** Owner's remake screenshot and DK2 sequence; native draw evidence in `research/kmf-materials.md` section7 and `research/portal-creature-action.md`.

## Constraints and review focus

- Preserve additive blending, opaque depth rejection, tag-overlay priority and other terrain ordering.
- Retain UVs, vertex colours, transformed positions and normals; clear transient geometry when effects expire.
- Keep persistent terrain storage independent of per-frame effect geometry.
- Check camera changes, empty effects, rotated roots and unchanged terrain mesh reuse.
- Keeper-colour timing must come from binary/data evidence, never an invented transition.

## Task: portal transparency and animation

- [x] Add `tools/classic-unity-probe/PortalEntranceProbe.cs`; reproduce black wedges and missing sixty-frame beams with shipped art.
- [x] Extend `TransparentTerrain` with retained frame-scoped flat/additive mesh submissions and colour streams.
- [x] Route flat quads and additive effect meshes from `ClassicConstructionEffects`; dispatch art types5 and6 as meshes.
- [x] Verify13 GPU cases,96 terrain/reuse cases and the original-data Level8 sequence; before/after captures reproduce disc occlusion and missing sixty-frame beams.
- [x] Audit colour initialization, generation and action49; record the remaining central-cloud producer as Q619 without inventing a transition.
- [x] Review, perform the documenter pass, run adapter/docs/KB gates, then commit by named paths.
