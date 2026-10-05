# Format and engine: how DK2 plays a KMF animation

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra (pyghidra-mcp), 2026-10-05
- `keeper-spell-tremor.md` § Animation clock, for the simulation-side clip clock

Original version: DK2 v1.70 (GOG)

Presentation only: nothing here changes a rule. It decides how the asset
pipeline (`src/DK2.Convert`) turns an animated KMF into a clip.

## 1. The key byte is a key-frame time — CONFIRMED

Each `GEOM` entry is a packed position followed by one byte. The engine's
vertex evaluator `0x0057E5B0` takes a float frame `f` and its integer part `i`:

1. `g = ITAB[i >> 7][index] + VGEO[index][i]` — the geometry entry, as
   `KmfReader` already decodes it.
2. It reads the positions of entries `g` **and `g + 1`**, and their bytes
   `kA` and `kB`.
3. With `base = (i >> 7)·128`, `frameA = base + kA`, it computes
   `t = (f − frameA) · 1/((base + kB) − frameA)`, the reciprocal coming from the
   table at `0x007810A0` (`1/n`, filled by `0x0058F480`).
4. The vertex is `pos[g] + (pos[g+1] − pos[g])·t`.

So the byte is the frame, within the current 128-frame block, at which that
entry is exact, and the engine **interpolates linearly** between consecutive
key entries. A file need store only key poses. The byte does not change the
playback rate.

## 2. The frame is continuous — CONFIRMED

The mesh drawer `0x00577F10` reads the instance's float time at `+0x44`,
clamps it to [0, 1] (or wraps it into [0, 1] when the mesh's loop flag, mesh
`+0x1C` bit 0, is set), sets `f = (frames − 1)·time` and
`i = floor(f)` clamped to [0, frames − 2], and evaluates every vertex at `f`.
One pass of the time covers frame 0 to the last frame.

## 3. The rate

For a creature art clip, the simulation advances a 12.12 phase each turn by a
step derived from the art descriptor's FPS and frame count (`0x004B41E0`,
`0x004B3C10`; `keeper-spell-tremor.md` § Animation clock). One pass lasts
frames/FPS **turns at 4 per second**, not real seconds. That this clock is
what drives the drawn clip is **STRONGLY INFERRED**; for KMF animations other
than creature art it is a **HYPOTHESIS**. The link from the phase (animation
object `+0x94`) to the drawer's time (`+0x44`) is **UNKNOWN**: the
animation-object renderer `0x0059B0C0` picks the segment's mesh through
`0x004B44E0` and calls `0x00579CF0`, but its time argument was not mapped, so
whether the drawn time is interpolated between turns is open.

## 4. What this means for ours

- `KmfReader` (`src/DK2.Convert/KmfReader.cs`, the `GEOM` loop) drops the key
  byte and builds frame `n` from entry `g` alone, without the interpolation to
  `g + 1`. Every frame that is not itself a key is then held at the previous
  key; this is **STRONGLY INFERRED** until one shipped clip is decoded both ways.
- `GlbWriter` writes keys at `frame/30` s with `STEP` interpolation. DK2's pose
  is linear between keys and its clip length comes from the art descriptor;
  30 fps matches some descriptors by coincidence only.
