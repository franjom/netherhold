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

**UNKNOWN in deliverable 1.** Texture containers and compression will be
documented when texture extraction is implemented.
