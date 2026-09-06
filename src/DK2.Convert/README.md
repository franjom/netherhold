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
normalized result by the animation geometry scale. `ITAB` supplies one base
table per block of 128 frames; frame `n` uses table `n / 128` and its own
`VGEO` byte. `KmfReader` exposes every decoded frame through `KmfAnimation`,
while `KmfModel.Mesh` remains the frame-zero mesh for existing callers and as a
fallback if a later frame has an invalid geometry lookup.

`CTRL` values, section-size meanings, the static-header unknown field, material
flag meanings, sprite detail-factor semantics, animation interpolation/key-frame
semantics, and group (`GROP`) semantics are **UNKNOWN** here. They are retained,
identified, or skipped only where they cannot affect decoded geometry; no
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

## SDT sound banks

Confidence is **CONFIRMED** against all 142 SDT files in the shipped v1.70
installation (8,959 entries) and corroborated, for format facts only, with
OpenKeeper's SDT reader. They agree on the container layout and the channel
interpretation of every type that occurs; the payload codec label is narrower
below because the shipped frame headers are the authority.

An SDT starts with a little-endian 32-bit entry count followed by that many
32-bit absolute entry offsets. Every shipped entry has a 40-byte header:
header size, stored-data length, a fixed 16-byte ASCII name, 16-bit sample
rate, 8-bit sample depth, 8-bit sound type, a zero field, a 32-bit field
OpenKeeper names sample count, and another zero field. The payload follows the
header. The offset and length ranges account for every byte in every shipped
bank. The exact semantics of the proposed sample-count field are **UNKNOWN**;
all shipped PCM entries store zero there, so `SdtArchive` exposes it without
using it to construct the WAV.

The shipped sound types are:

- type 0: an empty placeholder (999 entries);
- type 2: mono PCM, wrapped in a standard RIFF/WAVE PCM container on extraction
  (211 entries);
- type 36: mono MPEG audio (7,461 entries);
- type 37: stereo MPEG audio (288 entries).

OpenKeeper calls types 36 and 37 MP2, but the shipped payloads disagree with
that narrower label: their MPEG frame headers identify 4,004 Layer I streams
and 3,745 Layer II streams. The converter follows the payload, reports the
actual layer, and preserves each compressed elementary stream unchanged as
`.mp1` or `.mp2` rather than claiming to produce PCM. An unrecognized payload
or type is preserved as `.bin` and reported as undecoded; it is never wrapped
in a misleading WAV. OpenKeeper names type 3 as stereo PCM but marks that
reading doubtful, and no shipped bank contains type 3, so its meaning is
**UNKNOWN** here.

There are 8,938 entries declared at 22,050 Hz and 21 at 44,100 Hz. Seventeen
shipped banks contain both rates. `SdtArchive` exposes the rate on every entry,
and extraction writes every nonempty entry separately; it never selects one
rate as preferred. Names occupy only 16 bytes and are often truncated or
duplicated, so output names begin with the stable entry index to prevent one
variant overwriting another.

Six Layer I entries in the two `MiscCreatures` banks declare 22,050 Hz in the
SDT header while their MPEG frame headers say 44,100 Hz. Both values are
exposed (`SampleRate` and `PayloadSampleRate`) rather than silently choosing
one; which value original DK2 uses is **UNKNOWN**.

## GLB output

`GlbWriter` emits a glTF 2.0 binary (`.glb`) for static and animated KMF
geometry. Each KMF sprite becomes one glTF mesh primitive using its first
detail level and material. A sprite whose first detail level contains no
triangles is preserved as a point primitive.

Animated positions are emitted as glTF morph targets with a `WEIGHTS`
animation channel. This is the closer reading of KMF because its sprite
topology, material, UVs, and normals remain fixed while the per-frame lookup
changes only each vertex position. Frame zero is the base mesh and every later
frame is a position-delta target. Keys select those poses with `STEP`
interpolation because KMF's interpolation and key-frame metadata remain
**UNKNOWN**; interpolating between them would invent behaviour.

The output rate is **30 frames per second**. It is not read from KMF and is an
assumed value, therefore **UNKNOWN** with respect to original DK2 behaviour.
If any later frame cannot be decoded, or an externally constructed animation
does not match the frame-zero topology, `GlbWriter` deliberately emits the
valid frame-zero mesh without an animation channel.

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

## Catalogue conversion

`DK2.Tools convert --out converted-assets/` loads the same game database used
by `AssetCatalogue.ForDatabase` and reports one result for every key in that
catalogue. The key's dot-separated segments become directories exactly as
`FileAssetProvider` expects. Material images shared by GLBs are stored below
`_textures`; they are dependencies of catalogue assets, not additional logical
keys.

The KWD art-resource slots used here are fixed-format fields: solid terrain uses
its side mesh (falling back to complete when absent), walkable terrain uses the
first material texture of its complete mesh, rooms use their complete resource,
and objects use their primary mesh. Creature mappings are `IDLE_1` for idle and
the display mesh, `WALK` for movement-like activities, `SLEEPING` for sleeping,
`DIE` for dying, and `MANUFACTURING` for working. The Imp is the measured
exception: its melee slot is named `Imp_Dig` and supplies working. A blank slot
is reported missing rather than replaced with an unrelated animation.

Where a complete floor resource names a numbered mesh collection rather than a
single KMF, texture conversion reads the first numbered member's declared
material. This does not infer how the members are placed. In particular, the
Imp's nominal sleeping slot contains `Imp_Claim1(Land)` and is reported missing
rather than mislabeled as sleep.

KMF group placement and group material inheritance remain **UNKNOWN**. A
catalogue key whose selected resource is a group is therefore reported missing
instead of flattening the group by guesswork. Effect conversion is also
**UNKNOWN**.
