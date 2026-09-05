# DK2.Convert

Standalone conversion library for turning assets from a user's own Dungeon
Keeper 2 installation into the open formats consumed by `FileAssetProvider`.
Original and converted assets are proprietary and must never be committed.

## KMF model format

Confidence is **CONFIRMED** against the shipped v1.70 files used by the guarded
tests and corroborated with OpenKeeper's KMF reader.

KMF is little-endian and chunked with four-character identifiers. The outer
`KMSH` header contains a version and a `HEAD` chunk whose kind is static mesh
(1), animation (2), or group (3). Mesh and animation files then contain:

- `MATL`, holding `MAT2` records. Each material has NUL-terminated ASCII names,
  zero or more texture names, flags, brightness, shininess, and an optional
  environment-map texture name.
- a `MESH` or `ANIM` section with a `HEAD`, `CTRL`, and `SPRS` hierarchy;
- sprite headers (`SPHD`) carrying triangle counts for each detail level,
  vertex count, and a detail-selection factor;
- one sprite-data record per header, carrying a material index, triangle vertex
  indices stored as bytes, per-vertex UVs stored as unsigned 16-bit values
  divided by 32768, and float normals.

Static vertices contain a 16-bit index into a `GEOM` array of float XYZ
positions. Animated vertices instead index `ITAB`; a per-index, per-frame byte
in `VGEO` is added to that base to select a `GEOM` entry. Animated positions
pack signed 10-bit X, Y, and Z components into a 32-bit word and multiply the
normalized result by the animation geometry scale. The reader currently emits
frame-zero geometry; animation tracks are not part of the first deliverable.

`CTRL` values, section-size meanings, the static-header unknown field, material
flag meanings, sprite detail-factor semantics, animation interpolation/key-frame
semantics, and group (`GROP`) semantics are **UNKNOWN** here. They are retained,
identified, or skipped only where they cannot affect frame-zero geometry; no
meaning is guessed.

## Texture formats

There are two unrelated stores:

- `Data/EngineTextures.WAD` contains 102 ordinary PNG files, mostly interface
  art. They need only the existing WAD decompressor and can be copied unchanged.
- KMF material textures—including terrain and creatures—are not in a WAD. They
  live in `DK2TextureCache/EngineTextures.dir` and `.dat`. The `TCHC` directory
  contains NUL-terminated names and offsets. Each data entry starts with width,
  height, stored size, two scaled dimensions, and an alpha flag, followed by a
  compressed bitstream.

The model texture codec is **CONFIRMED** against the shipped cache and
corroborated with OpenKeeper. It predicts colour-channel DC values between 8×8
blocks, entropy-decodes quantized coefficients, performs two integer inverse
transforms, converts the resulting luma/chroma planes to RGBA, and optionally
decodes an alpha plane. Names ending in `MM0` are the highest-resolution mipmap
and are selected for an unsuffixed KMF texture name. Two shipped KMF names do
not exist in the cache: `Goblin2` and `Goblinbak`; the explicit, corroborated
corrections are `GoblinFront` and `GoblinBack`.

The two scaled-dimension fields, coefficient codec's historical name, and the
meaning of nonzero alpha-header bits below bit 7 are **UNKNOWN**. TGQ is used by
movies and BF4 by fonts; neither participates in terrain or creature textures.

## GLB output

`GlbWriter` emits a glTF 2.0 binary (`.glb`) for either static KMF geometry or
the frame-zero mesh exposed from an animated KMF. Each KMF sprite becomes one
glTF mesh primitive using its first detail level and material. A sprite whose
first detail level contains no triangles is preserved as a point primitive.

DK2 XYZ is rotated to glTF XYZ as `(x, -z, y)`, matching the established KMF
loader reference. KMF triangle order is reversed for glTF front-face winding.
UVs are retained as decoded. The first named KMF texture becomes the material's
base-colour texture; all named alternatives are also retained in material
`extras`, along with a named environment texture when present. Texture names
map to relative, URI-escaped `.png` names by default,
and callers may supply the relative-path mapping needed by their output layout.

No offline glTF validator is installed in the development environment. Tests
therefore independently reload the GLB container and verify its JSON, chunk
alignment, buffer ranges, accessors, transformed vertex bytes, indices, and
external image URIs.
