# DK2.Convert

Standalone conversion library for turning assets from a user's own Dungeon Keeper 2
installation into the open formats consumed by `FileAssetProvider` (`src/DK2.Assets`).
Original and converted assets are proprietary and must never be committed.

Types: `KmfReader` → `KmfModel`/`KmfMesh`/`KmfAnimation`/`KmfMaterial`/`KmfPrimitive`;
`GlbWriter`; `EngineTextureArchive` + `Dk2TextureDecoder` + `PngWriter` →
`RgbaImage`; `TextureExtractor`; `SdtArchive`/`SdtEntry`/`SdtCodec`; `Dk2ArtCatalogue`;
`AssetConverter` → `AssetConversionResult`.

CLI surface, all in `dk2tools`: `convert --out <dir>` runs the catalogue conversion
below; `sdt list <file.sdt>` and `sdt extract <file.sdt> [--out <dir>]` drive
`SdtArchive` directly.

## KMF model format

Confidence is **CONFIRMED** against the shipped v1.70 files used by the guarded tests
and corroborated with OpenKeeper's KMF reader.

KMF is little-endian and chunked with four-character identifiers. The outer `KMSH`
header contains a version and a `HEAD` chunk whose kind is static mesh (1), animation
(2), or group (3). Mesh and animation files then contain:

- `MATL`, holding `MAT2` records. Each material has NUL-terminated ASCII names, zero
  or more texture names, flags, brightness, shininess, and an optional
  environment-map texture name.
- a `MESH` or `ANIM` section with a `HEAD`, `CTRL`, and `SPRS` hierarchy;
- sprite headers (`SPHD`) carrying triangle counts for each detail level, vertex
  count, and a detail-selection factor;
- one sprite-data record per header, carrying a material index, triangle vertex
  indices stored as bytes, per-vertex UVs stored as unsigned 16-bit values divided by
  32768, and float normals.

Static vertices contain a 16-bit index into a `GEOM` array of float XYZ positions.
Animated vertices instead index `ITAB`; a per-index, per-frame byte in `VGEO` is
added to that base to select a `GEOM` entry. Animated positions pack signed 10-bit X,
Y, and Z components into a 32-bit word and multiply the normalized result by the
animation geometry scale. The byte after each animated `GEOM` position is that entry's
key frame within its 128-frame block. `ITAB` supplies one base table per block of 128 frames;
frame `n` uses table `n / 128` and its own `VGEO` byte, and a frame between two keys lies on
the line from entry g to g + 1 (ADR-0390). `KmfReader` exposes every
decoded frame through `KmfAnimation`, while `KmfModel.Mesh` remains the frame-zero
mesh for existing callers and as a fallback if a later frame has an invalid geometry
lookup.

`CTRL` values, section-size meanings, the static-header unknown field, material flag
meanings, sprite detail-factor semantics, and group (`GROP`) semantics are **UNKNOWN** here. They are retained,
identified, or skipped only where they cannot affect decoded geometry; no meaning is
guessed.

### How a creature's clips relate

Measured on 2026-09-26 over all 2,015 KMF files in v1.70 `Meshes.WAD`, all of which
decode: 862 static meshes, 1,145 animations, 8 groups. **Each clip is its own file and
there is no skeleton**: no bones, bind pose or weights exist anywhere in the format.

A creature's clips nevertheless share one body. Vertex counts vary per clip (the
Imp has 26 different counts across 32 clips) only because vertices are split by UV
and normal; distinct positions are constant (Skeleton 112, Imp 96, Bile Demon 126),
and in 27 of the Imp's 35 files every triangle matches `Imp-PoseFrame` by index and
UV. Prop clips keep the whole body and append the prop: the Bile Demon's Eat, Drink
and bed clips hold all 212 body triangles plus the chicken, bottle or bed. A few
files use a second UV layout over the same counts (Q443). The model behind the
modern pipeline is `docs/ASSET_PIPELINE.md` §2.

## Texture formats

There are two unrelated stores:

- `Data/EngineTextures.WAD` contains 102 ordinary PNG files, mostly interface art.
  They need only the existing WAD decompressor (`DK2.Data`'s `WadArchive`) and can be
  copied unchanged.
- KMF material textures — including terrain and creatures — are not in a WAD. They
  live in `DK2TextureCache/EngineTextures.dir` and `.dat`. The `TCHC` directory
  contains NUL-terminated names and offsets. Each data entry starts with width,
  height, stored size, two scaled dimensions, and an alpha flag, followed by a
  compressed bitstream.

The model texture codec is **CONFIRMED** against the shipped cache and corroborated
with OpenKeeper. It predicts colour-channel DC values between 8×8 blocks,
entropy-decodes quantized coefficients, performs two integer inverse transforms,
converts the resulting luma/chroma planes to RGBA, and optionally decodes an alpha
plane. Names ending in `MM0` are the highest-resolution mipmap and are selected for
an unsuffixed KMF texture name. Two shipped KMF names do not exist in the cache:
`Goblin2` and `Goblinbak`; the explicit, corroborated corrections are `GoblinFront`
and `GoblinBack`.

The two scaled-dimension fields, the coefficient codec's historical name, and the
meaning of nonzero alpha-header bits below bit 7 are **UNKNOWN**. TGQ is used by
movies and BF4 by fonts; neither participates in terrain or creature textures.

## SDT sound banks

Confidence is **CONFIRMED** against all 142 SDT files in the shipped v1.70
installation (8,959 entries) and corroborated, for format facts only, with
OpenKeeper's SDT reader. They agree on the container layout and the channel
interpretation of every type that occurs; the payload codec label is narrower below
because the shipped frame headers are the authority.

An SDT starts with a little-endian 32-bit entry count followed by that many 32-bit
absolute entry offsets. Every shipped entry has a 40-byte header: header size,
stored-data length, a fixed 16-byte ASCII name, 16-bit sample rate, 8-bit sample
depth, 8-bit sound type, a zero field, a 32-bit field OpenKeeper names sample count,
and another zero field. The payload follows the header. The offset and length ranges
account for every byte in every shipped bank. The exact semantics of the proposed
sample-count field are **UNKNOWN**; all shipped PCM entries store zero there, so
`SdtArchive` exposes it without using it to construct the WAV.

The shipped sound types are:

- type 0: an empty placeholder (999 entries);
- type 2: mono PCM, wrapped in a standard RIFF/WAVE PCM container on extraction (211
  entries);
- type 36: mono MPEG audio (7,461 entries);
- type 37: stereo MPEG audio (288 entries).

OpenKeeper calls types 36 and 37 MP2, but the shipped payloads disagree with that
narrower label: their MPEG frame headers identify 4,004 Layer I streams and 3,745
Layer II streams. The converter follows the payload, reports the actual layer, and
preserves each compressed elementary stream unchanged as `.mp1` or `.mp2` rather than
claiming to produce PCM. An unrecognized payload or type is preserved as `.bin` and
reported as undecoded; it is never wrapped in a misleading WAV. OpenKeeper names type
3 as stereo PCM but marks that reading doubtful, and no shipped bank contains type 3,
so its meaning is **UNKNOWN** here.

There are 8,938 entries declared at 22,050 Hz and 21 at 44,100 Hz. Seventeen shipped
banks contain both rates. `SdtArchive` exposes the rate on every entry, and extraction
writes every nonempty entry separately; it never selects one rate as preferred. Names
occupy only 16 bytes and are often truncated or duplicated, so output names begin
with the stable entry index to prevent one variant overwriting another.

Six Layer I entries in the two `MiscCreatures` banks declare 22,050 Hz in the SDT
header while their MPEG frame headers say 44,100 Hz. Both values are exposed
(`SampleRate` and `PayloadSampleRate`) rather than silently choosing one; which value
original DK2 uses is **UNKNOWN**.

**Every compressed payload carries one trailing zero byte.** Walking the MPEG frames
of all 7,749 compressed entries lands, in every single case, exactly one byte short
of the entry's `StoredSize`, and that byte is always zero. So an extracted `.mp1` or
`.mp2` ends with a byte no decoder will read as a frame — which is what the game
stores and is therefore what is written out, since the contract here is to preserve
the elementary stream rather than to tidy it. Ordinary decoders ignore it; a strict
one may report a trailing-garbage warning. Whether it is a terminator, an alignment
pad or an off-by-one in EA's own packer is **UNKNOWN**, and 7,749 out of 7,749 is too
uniform to be an accident.

## GLB output

`GlbWriter` emits a glTF 2.0 binary (`.glb`) for static and animated KMF geometry.
Each KMF sprite becomes one glTF mesh primitive using its first detail level and
material. A sprite whose first detail level contains no triangles is preserved as a
point primitive.

Animated positions are emitted as glTF morph targets with a `WEIGHTS` animation
channel. This is the closer reading of KMF because its sprite topology, material, UVs,
and normals remain fixed while the per-frame lookup changes only each vertex position.
Frame zero is the base mesh and every later frame is a position-delta target. Each
integer frame is evaluated as DK2's key interpolation does, and the weights are keyed
`LINEAR`, so a viewer's blend between sampled frames is DK2's pose (ADR-0390).

One pass runs from the first frame to the last over the creature art descriptor's frame
count divided by its FPS, in seconds; that descriptor count differs from the KMF's in 300 of
906 creature clips, and the pass follows the descriptor. A KMF with no descriptor
(objects, rooms) is keyed at an assumed **30 frames per second**. That the descriptor's
clock is what times the drawn frame is STRONGLY INFERRED (Q442,
`docs/research/kmf-animation-playback.md` §3). If any later
frame cannot be decoded, or an externally constructed animation does not match the
frame-zero topology, `GlbWriter` deliberately emits the valid frame-zero mesh without
an animation channel.

DK2 XYZ is rotated to glTF XYZ as `(x, -z, y)`, matching the established KMF loader
reference. KMF triangle order is reversed for glTF front-face winding. UVs are
retained as decoded. The first named KMF texture becomes the material's base-colour
texture; all named alternatives are also retained in material `extras`, along with a
named environment texture when present. Texture names map to relative, URI-escaped
`.png` names by default, and callers may supply the relative-path mapping needed by
their output layout.

No offline glTF validator is installed in the development environment. Tests
therefore independently reload the GLB container and verify its JSON, chunk
alignment, buffer ranges, accessors, transformed vertex bytes, indices, and external
image URIs.

## Catalogue conversion

`dk2tools convert --out converted-assets/` loads the same game database used by
`AssetCatalogue.ForDatabase` and reports one result for every key in that catalogue.
The key's dot-separated segments become directories exactly as `FileAssetProvider`
expects. Material images shared by GLBs are stored below `_textures`; they are
dependencies of catalogue assets, not additional logical keys.

The KWD art-resource slots used here are fixed-format fields: solid terrain uses its
side mesh (falling back to complete when absent), walkable terrain uses the first
material texture of its complete mesh, rooms use their complete resource, and objects
use their primary mesh. Creature mappings are `IDLE_1` for idle and the display mesh,
`WALK` for movement-like activities, `SLEEPING` for sleeping, `DIE` for dying, and
`MANUFACTURING` for working. The Imp is the measured exception: its melee slot is
named `Imp_Dig` and supplies working. A blank slot is reported missing rather than
replaced with an unrelated animation.

Where a complete floor resource names a numbered mesh collection rather than a single
KMF, texture conversion reads the first numbered member's declared material. This
does not infer how the members are placed. In particular, the Imp's nominal sleeping
slot contains `Imp_Claim1(Land)` and is reported missing rather than mislabeled as
sleep.

KMF group placement and group material inheritance remain **UNKNOWN**. A catalogue key
whose selected resource is a group is therefore reported missing instead of
flattening the group by guesswork. Effect conversion is also **UNKNOWN**.
