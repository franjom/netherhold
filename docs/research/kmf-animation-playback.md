# Format and engine: how DK2 plays a KMF animation

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra (pyghidra-mcp), 2026-10-05 and 2026-10-10
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
Wrapping subtracts 1 only while time is greater than 1: exact loop endpoints
retain the last frame (rechecked 2026-10-10).

## 3. The rate

For a creature art clip, the simulation advances a 12.12 phase each turn by a
step derived from the art descriptor's FPS and frame count (`0x004B41E0`,
`0x004B3C10`; `keeper-spell-tremor.md` § Animation clock). One pass takes
4096/step = duration/1024 = 4·frames/FPS turns, which at 4 turns a second is
nominally **frames/FPS seconds**; the integer phase step quantizes this rate.

CONFIRMED on 2026-10-10: ordinary type-6 drawing is `0x0059B880` to
`0x0059C880`. It reads previous/current phase through `0x00496960` and
`0x0043D730`, divides by 4096, unwraps across animation segments and blends
with global `0x00760B7C`. `0x00575A00` writes that global from its caller's
frame interpolation argument. `0x00583120` interpolates position, turns and
phase into the 0x99 draw object, storing phase at `+0x38`.

UNKNOWN (Q442): the transfer from that draw object to the sprite instance's
`+0x44`, and the interpolation factor's derivation from the game clock.
The secondary dispatcher `0x0059B0C0` uses the simple-instance path
`0x00579CF0`/`0x00578530`; it is not the ordinary phase interpolation path.

## 4. What this means for ours

Built (ADR-0390):

- `KmfReader` keeps the key byte and evaluates each integer frame as §1 does,
  the last through frame − 1's lookup as §2's clamp has it. Over every shipped
  KMF, 12.05 M of 28.4 M vertex lookups fall between keys and so move; equal
  keys, a key that runs backwards and a last entry with no `g + 1` occur only
  on the key's own frame, where the numerator of `t` is 0.
- `GlbWriter` samples the frames `LINEAR` and spaces them so one pass lasts the
  art descriptor's frame count over its FPS (`+0x44`, `+0x48`, type 6) from
  frame 0 to the last (§2, §3). That count differs from the KMF's in 300 of 906
  creature clips. A KMF with no descriptor still plays at an assumed 30 fps.
  The phase-to-time link in §3 stays open.

Initial classic playback (ADR-0431): `RenderAnimation` interpolates adjacent
decoded frames into each instance's reusable buffer. Each clip retains its
own topology, materials and UVs; this does not require fitting a second UV
family onto a modern rig (Q443). Unity uploads positions to an instance mesh.
Descriptor count/FPS sets nominal duration; elapsed simulation render time
currently starts on clip changes. The original quantized phase, action restarts,
segment transitions and complete chooser (`0x0048BE50`) are not mirrored yet.
Run, claim/reinforce and eating mappings remain pending. Shared arrays have no
per-sample allocation; Unity upload and whole-scene cost require editor profiling.

The Release probe on this machine (200 instances, 300 renders, 2026-10-10) measured
sampler-only cost: Imp 0.121 ms, Giant 0.132 ms, Salamander 0.125 ms, Mistress
0.196 ms per render, with zero measured sampling allocations. Added shared position
arrays were 50,544/165,816/186,984/289,296 bytes respectively. These figures exclude
the provider's retained decoded KMF frames and Unity conversion/upload/bounds work.
