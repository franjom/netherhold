# HUD rendering feasibility pilot

Isolated Step6 tooling, not the production HUD. It exercises a custom shader
rendering into a `RenderTexture`, displayed through UI Toolkit's `Image` under
the copied production URP configuration. It does not test direct UI Toolkit
custom materials or choose the production font-atlas implementation.

Run from the Step6 worktree, with a licensed Unity 6000.6.0f1 editor:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/hud-probe/run.ps1
```

The script accepts `-Unity`, `-Install`, `-Project` and `-Output`. Scratch and
output paths must be beneath `D:/DungeonKeeperRemake/build`. It refuses an
unmarked existing project or one already open in Unity. The batch editor uses
two worker threads; it neither opens nor saves the owner's active project.
The scratch manifest enables image conversion for PNG decoding/capture.

## What the images show

The map is a **synthetic class grid**, using the game palette and `Map-BG.PNG`
from `EngineTextures.WAD`. The pilot maps source pixels to the class banks read
from the binary loader; it does not animate neutral colours or classify game
tiles. No live world or discovery data is used. Native backdrop/UV placement,
palette cycling and class/marker behaviour still need campaign comparisons.
The same-named FrontEnd palette has different RGB values; it is not substituted.

Font rows from top to bottom all draw `Dungeon Heart 100%`:

1. Size-14 `.bf4` bitmap at its native sample scale.
2. Size-72 bitmap reduced to the pilot's small comparison scale.
3. Size-72 distance field reduced to that same scale.
4. Size-14 bitmap enlarged twice.
5. Size-72 distance field at the larger comparison scale.

The bounded distance-field conversion is a pilot, not a production atlas/cache.
These samples do not validate all glyphs, locales, metrics, layout styles or
matching native-size reductions. Large-font glyph shapes/spacing must be checked
against the selected original style before adopting them in the real interface.
The small original bitmap remains the reference where it is drawn exactly.

## Checks and observed result

2026-10-17, Unity 6000.6.0f1, AMD Radeon(TM) Graphics, production URP asset:
both 1024×768 and 2560×1440 targets rendered. The palette decoded as 64×16;
the size-14 and size-72 files each expose 251 glyph records.

The source compiles without warnings. The Unity smoke checks assert initialized
URP, transparent disc corners/opaque centre, transparency through UI composition,
and visible pixels in every font row at both sizes. `Passed` means those checks,
not original-game fidelity acceptance. Both captures were also inspected manually
and by an independent reviewer: the disc and rows render; the larger field sample
is visibly sharper than the enlarged small bitmap.

Artifacts: `D:/DungeonKeeperRemake/build/hud-toolkit-results/` contains
`result.json`, both PNGs, `compile.log` and `unity.log`. Captures are derived
from the install and stay gitignored. The editor also reports a startup exception
inside `UnityEditor.Search.SearchDatabase`; the rendering checks complete, but
this is not a claim of exception-free editor startup.

Not verified: campaign minimap colours/markers/rotation, dirty-grid uploads, runtime
resizing, input, camera navigation, production text rendering, GPU timings or UI
performance. A target-texture size comparison is not a real window/input test.
Native evidence and remaining research: [minimap-rendering.md](../../docs/research/minimap-rendering.md).
