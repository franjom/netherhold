# Front-end assets: what the menus, campaign map, movies and menu sounds ship as

DK2 v1.70 (GOG install), read 2026-10-23 from the shipped files with one-off scripts and the repo's own readers (`DK2.Tools.exe`: `kwd map|things|triggers|objects|terrain|rooms`, `sdt list`, `str list`). No asset bytes are in the repo; this document holds names, sizes and counts only.

Scope is the **files**. Which state of the menu machine or the campaign map uses which file is a DKII.EXE question and is not answered here; rows that say "used by X" give the evidence for the pairing (a file name, a designer's index, a count match).

Confidence: **CONFIRMED** = read from the bytes or by a repo reader over every file in the class; **STRONGLY INFERRED** = consistent evidence, not proved; **HYPOTHESIS** = plausible, unchecked; **UNKNOWN**. OpenKeeper (GPLv3) is cited for facts only, and no code was taken from it.

## 0. Summary: readers present and missing

| Asset class | Files | Repo reader | Missing piece | Size |
|---|---|---|---|---|
| `FrontEnd.WAD` container | 1 archive, 445 entries | `DK2.Data.Wad.WadArchive` | indexed extraction built in F1; the by-name map still keeps the last (§1.3) | small |
| PNG (371) | `FrontEnd.WAD` | `WadImageSource` injects the runtime PNG decoder; Unity `ClassicFrontEndAssets` supplies it | built in F1 | small |
| BMP, 24-bit (42) | `FrontEnd.WAD` | `DK2.Art.Textures.BmpImageReader` | built in F1; all 42 decode | small |
| `.444` compressed images (29) | `FrontEnd.WAD` | `LoadingScreenImageReader` and the texture decoder's direct-RGB mode | built in F1; all 29 unmasked images decode; masked variant refused | small |
| GIF (1), PSD (1), `HiScores.dat` (1) | `FrontEnd.WAD` | none | GIF: the age-rating icon. PSD: the title screen's source, probably unused. HiScores: see §8 | small |
| Front-end level `FrontEnd3DLevel*` | 6 files | `KwdReader`, `MapGrid`, `KwdThings`, `KwdTriggers`, players and variables | none | — |
| Front-end meshes (KMF) | 93 FE-named plus shared ones in `Meshes.WAD` | `DK2.Art.KmfReader` (not run on these files here) | none expected (STRONGLY INFERRED) | — |
| Front-end textures | 560 `3dFe*`/`3dMap*`/`MPD*` entries in `EngineTextures.dir` | `DK2.Art.EngineTextureArchive` | none | — |
| Camera paths 250-274 | 25 `.KCS` in `Paths.WAD` | `DK2.Data.Camera.KcsCameraPath` | none (format CONFIRMED over all 80 files, camera-paths.md) | — |
| Fonts `FONT_FRONTEND*.bf4` | 6 | `DK2.Data.Text.Bf4FontReader` (an install test sweeps all 22 `.bf4`) | none | — |
| String tables | 92 `.str` | `DK2.Data.Text.Dk2StringTable`, `LevelTextTable` | none | — |
| SDT sound banks | `FrontEndHD`, `GuiHD`, `Patch_1HD`, 10 `Track_*`, 33 speech banks | `DK2.Convert.Audio.SdtArchive` lists the entries and extracts the raw MPEG payload | an MPEG-1/2 **Layer I and Layer II** decoder in memory at run time | medium |
| `*BANK.map` / `*SFX.map` event maps | 2 per sound object | none | a reader: bank → archive names, event → groups → variants → (sample, archive) | medium |
| Music sequencing | `musicSFX.map` (27,042 B), 10 track banks | none | the segment sequencer: its rules are in DKII.EXE | medium-large |
| Movies `.tgq` | 24 files, 213 MB | `DK2.Data.Media` and Unity `ClassicMoviePlayer` | playback proof of concept built; front-end sequencing remains F7 | large |
| `Settings/HiScores.dat` | 1 | `DK2.Data.Campaign.HiScoresFile` | reader/writer built in F1 (§8); qualification and entry UI remain F5 | small |
| Save games (load menu) | `Data/Save/*.SAV`, `*.QSAV` | `DK2.Data.Save.Dk2SaveReader` | whether it reads the fields the load menu lists was not checked | UNKNOWN |

## 1. `Data/FrontEnd.WAD`

### 1.1 Container (CONFIRMED, bytes)

- 22,599,650 bytes on disk, DWFB version 2, **445 entries, none compressed**, 22,573,843
  bytes of payload. It is the same container as the other four WADs (wad-format.md).
- Types by magic: 371 PNG, 42 BMP (`BM`, 24-bit), 29 `.444`, 1 GIF, 1 PSD
  (`8BPS`), 1 `.dat`. These counts match minimap-rendering.md's inventory.

### 1.2 Entries grouped by purpose

The grouping is by name, so it is STRONGLY INFERRED; the counts, sizes and pixel
dimensions are CONFIRMED (read from the PNG IHDR, BMP and `.444` headers).

| Group | Names (pattern) | Count | Payload bytes | Dimensions |
|---|---|---|---|---|
| Title screen | `Titlescreen.png` and 10 language variants (Danish, Dutch, French, German, Italian, Japanese, Korean, Polish, Spanish, Swedish), `Titlescreen-Japanese.444`, `Titlescreen.psd`, `TitleScreen\icon-Mature.gif` | 14 | 6,425,433 | 640×480 |
| Loading screens | `LoadingScreen{W}x{H}.444` and `M-LoadingScreen{W}x{H}.444` at 400×300, 512×384, 640×480, 800×600, 1024×768, 1280×1024 and 1600×1200, in two directory runs (`LoadingScreen\` and `LoadingScreen-Japanese\`) | 28 | 7,784,260 | per name |
| Backdrops | `BackdropMain`, `BackdropArch`, `BackdropMap`, `BackdropMPD1..6`, `BackdropMPDEmpty`, `map_template` | 11 | 2,306,367 | 640×480 |
| Logo | `Logo.png`, `Logo-Japanese.png` | 2 | 104,336 | 327×123 |
| Level shots (`Obj Shots` run) | `Level{n}-0/-1.png`: levels 1-20 with 6a/6b, 11a/b/c and 15a/b (24 levels × 2) | 48 | 1,890,385 | 144×112 |
| 2D campaign map pieces | `2dLevel{n}-{1..4}.png` (incl. a/b/c) and `2dLevelS{n}-{1..4}.png` | 133 | 1,059,178 | 32×30 to 230×157 |
| My Pet Dungeon level shots | `MPD1-0..MPD7-0.png` | 7 | 276,353 | 144×112 |
| Secret level shots | `Obj_Shots\Secret5-0.png`, `Secret1..4-0.png` | 5 | 204,083 | 144×112 |
| Movie-menu thumbnails | `M-000-0` .. `M-018-0` (19), `M-Intro-0`, `M-Outro-0`, `M-Hilight`, `Mov_Shots\M-Unavailable-0` | 23 | 237,824 | 72×56 |
| Multiplayer and skirmish map thumbnails | `Multi{players}-{AJT,AMT}-{map}{96,144}.bmp` and `Thumbnails\Multi4-AMT-Platform96.bmp`: 21 maps, 144 and 96 px | 42 | 1,889,244 | 144×144, 96×96 |
| Panel backgrounds | `BG-Controls`, `BG-Game_Settings`, `BG-Load`, `BG-Map`, `BG-Multiplayer`, `BG-Objective`, `BG-Service`, `BG-Sessions`, `BG_Tiles\BG-Skirmish` | 24 | 48,243 | 18-283 px tall strips |
| 3D front-end widgets | `3DFE\3DTick`, `3DCancel`, `3DsmlButton{On,Off,Sel}`, `3DSliderButton`, `3D{Sound,Graphics,Control}HSlider`, `3DControlVSlider`, `3DaudioSlider` | 11 | 28,768 | 34-208 px |
| Scroll bars and arrows | `{0..5}-{H,V}-Scroll_Bar[_BG]`, `*-Arrow` | 32 | 57,500 | small |
| Buttons and ticks | `Bigbutton`, `BigBtonSel`, `BigbuttonDark` (180×64), `Confirm`/`Cancel`/`Save`/`Load_Skirmish` with `-Over`/`-Click`, `Tick*`, `Hilight_Frame`, `Buttons\Tick` | 29 | 128,607 | 18-180 px |
| Cursor frames | `idle1..8`, `cursor\press9`, `press1..8` | 18 | 113,425 | 75×75 |
| Other | `MessageBox` (326×149), `Box-00/01`, `Ally*` (16×16, 9), `2d-BG\pd_template` (366×310), `MapColours` (64×16), `Maps\Map-000` (148×148), `C-Pointer`, `C-Pointer-2`, `HiScores\HiScores.dat` | 18 | 19,837 | — |
| **Total** | | **445** | 22,573,843 | |

- The level-launch screen loads the two images `Obj Shots\<level> 0` and `1`
  (`0x0053AC60`, campaign-map-and-progression.md). The WAD's `Obj_Shots\` run (entries
  271-330) holds exactly those: `Secret1..5-0`, `MPD1..7-0`, and `Level{n}-0`/`-1` for
  the 24 campaign levels. Secret and MPD levels have only the `-0` image (CONFIRMED,
  names). That the `\<level> <n>` string resolves to `Level<n>-<n>.png` is STRONGLY
  INFERRED; the formatting is in the sibling's read.
- The 19 `M-0xx` thumbnails match the 19 `CutSceneLevel{n}.tgq` movies, and
  `M-Intro`/`M-Outro` match `INTRO.TGQ`/`Outro.tgq` (STRONGLY INFERRED, count and name).
- The 21 multiplayer thumbnails name the multiplayer maps; `LEVELM1..20_BRIEFING.str`
  hold 20 multiplayer level names (§7). Whether a thumbnail is missing a briefing, or the
  reverse, was not checked.

### 1.3 Directory runs and duplicate names (CONFIRMED, bytes)

13 entries carry a directory prefix (`TitleScreen\`, `Thumbnails\`, `Obj_Shots\`,
`Mov_Shots\`, `Maps\`, `LoadingScreen-Japanese\`, `LoadingScreen\`, `HiScores\`,
`cursor\`, `Buttons\`, `BG_Tiles\`, `3DFE\`, `2d-BG\`). In every case the entries after
the prefixed one belong to the same group but have no prefix. So the prefix probably
marks the first file of a directory run (STRONGLY INFERRED: the `Obj_Shots\` run is
exactly the 60 images DK2 loads as `Obj Shots\…`, §1.2; the WAD tool's name
compression is not read).

As a result **31 names occur twice (62 entries)**: the 14 loading screens (the Japanese and the default runs), 16 `2dLevelS{1..4}-{1..4}.png`, `Cancel.png` (48×48 and 60×60) and `Confirm.png`. `WadArchive.TryGetEntry` keeps the later entry for each, so a lookup
by bare name cannot reach the first. `WadArchive.Extract(int)` and
`WadImageSource.Read(int)` reach either copy by index; general directory-qualified
lookup remains unimplemented. The F3 menu provider selects the initial root run
by index: root Confirm/Cancel are both 48x48, while the later Buttons run contains
an opaque green Confirm and a 60x60 outline Cancel. Mixing those later copies
produced the empty-looking cancel button. Native `0x00534210` requests plain
Confirm/Cancel; directory-run reconstruction remains STRONGLY INFERRED from the
shipped names, not proved by the storage-query wrapper.

### 1.4 `.444` images (bytes CONFIRMED, codec STRONGLY INFERRED)

The 8-byte header is `u16 width, u16 height`, a flag byte at +4 and three
replacement-colour bytes at +5..+7. All 29 shipped images have flags `0x65`,
no alpha and no mask (CONFIRMED from their bytes). Dimensions match their names.

Native header setup `0x005D77A0` takes alpha from flag bit 7 and enables a block
mask when bit 0 is clear. The 32-bit block routine `0x00668810` reads absolute
8-bit DC values for RGB (and optional alpha), using the shared AC decoder
`0x00665EF0` and transforms; it consumes mask data after the colour channels.
This differs from engine textures' predicted DC and luma/chroma conversion.
`0x00666210`'s constant-column shortcut checks all seven AC coefficients.
These are STRONGLY INFERRED from the binary, including the corrected escape
advance for the extended positive/negative AC forms.

F1 implements the unmasked path, pads to 8-pixel blocks and crops to the header
size. Synthetic tests cover absolute DC, channel order, cropping, alpha, extended
escapes and the fifth AC coefficient; all shipped BMP/.444 entries decode.
This is not a matched native pixel oracle. Masked images are explicitly refused.
OpenKeeper's `LoadingScreenFile` and `LoadingScreenTextureDecoder` corroborated
the header and RGB/DC distinction (facts only; THIRD-PARTY-NOTICES.md).

## 2. The front-end level: `Data/editor/maps/FrontEnd3DLevel*`

| File | Bytes | Content (CONFIRMED, repo readers) |
|---|---|---|
| `FrontEnd3DLevel.kwd` | 26,831 | level info. Its strings are only the paths of the global `.kwd` files and of the five `.kld` files below: no title, music or briefing |
| `FrontEnd3DLevelMap.kld` | 428 | **7 × 14 tiles**. Terrain: 30 Edge of Map ×38, 14 Dungeon Heart ×25, 1 Impenetrable Rock ×19, 34 Hero Gate [FrontEnd] ×16. Owners: p1 16, p2 57, p3 25 |
| `FrontEnd3DLevelPlayers.kld` | 671 | three players: "Good Player", "Neutral Player", "Keeper 1" |
| `FrontEnd3DLevelThings.kld` | 316 | **8 things**: 1 action point (id 1, tiles (1,6)-(5,12), flags 0x10), 6 ROOM records for terrain 34 (Hero Gate [FrontEnd], room 21) at (3,8), (4,8) and (3,9)-(3,12), all player 1, and 1 ROOM record for terrain 14 (Dungeon Heart) at (3,3), player 3. **No objects, creatures, traps, doors or camera things.** |
| `FrontEnd3DLevelTriggers.kld` | 108 | **one trigger**: condition type 34 (level time), comparison 4 (`>`), value 0, repeat 1, which runs action 45 with Available = 0. Action 45 sets notifications on or off (`TriggerExecutor`), so the level turns notifications off on its first turn |
| `FrontEnd3DLevelVariables.kld` | 152 | `MinimumImpThreshold` = 0 (kwd-variables.md) |

- Room 21 "Hero Gate [FrontEnd]" is a 1×1 `HeroGateFrontEndTile` construction with HP
  600 and terrain 34. Its pieces are `3DFrontend1..16` (wall-pieces.md); it builds no
  walls (wall-pieces.md §3); its floor table is the 3×6 table `0x0066DD28`
  (room-floors-and-liquids.md). CONFIRMED in those documents.
- None of the front-end objects (§2.1) is placed by the level, so the executable must
  create them (STRONGLY INFERRED: they appear in no `.kld`; the code that spawns them
  belongs to the menu-machine read).

### 2.1 Front-end objects in `Objects.kwd` (CONFIRMED, `kwd objects`)

| Id | Name | Flags of note |
|---|---|---|
| 89 | 3D Front End Banner 1 | BoulderCanRollThrough |
| 102 | Hero Gate Table | CanBePickedUp, Placeable, HP 1000 |
| 110 | 3D Front End Candle Stick | — |
| 115 | Frontend 3D Arrow | CanBePickedUp |
| 130 | MPD Level Indicator | CanBePickedUp, CanBeSlapped, Obstacle, Highlightable, Placeable |
| 131 | 3D Front End Gem Holder | CastShadows |
| 132 | 3D Front End Chain | — |
| 133 | HeroGate MPD Table | CanBePickedUp, HP 1000 |
| 134, 135, 136 | 3D Front End Banner 2, 3, 4 | — |

### 2.2 Front-end meshes in `Meshes.WAD` (CONFIRMED names, 2,015 entries in all)

| Pattern | Count | Bytes | Likely use (by name, STRONGLY INFERRED) |
|---|---|---|---|
| `3DFrontend1..16.kmf` | 16 | 237,011 | the Hero Gate [FrontEnd] room pieces (wall-pieces.md) |
| `3dmap_level{1..21,6a,6b,11a,11b,11c,15a,15b}.kmf` | 28 | 232,614 | campaign-map land pieces, one per level (21 is the extra) |
| `3dmaplevel{n}_arrows.kmf` | 24 | 65,847 | campaign-map arrows per level |
| `MPD_map1..7.kmf`, `MPDSelector_activate.kmf` | 8 | 113,524 | My Pet Dungeon table |
| `Secret_Level1..5.kmf` | 5 | 13,586 | secret levels on the map |
| `banner1..4_swing.kmf`, `chain_swing.kmf` | 5 | 43,314 | objects 89/134-136 and 132 |
| `3dfe_candle.kmf`, `3dfe_beams.kmf`, `3DFE_GemHolder.kmf`, `3dgembit.kmf`, `gembeams.kmf` | 5 | 147,198 | objects 110 and 131, and the gem effects |
| `door wood fe.kmf`, `door wood fe01.kmf` | 2 | 2,536 | a front-end door |
| `REA3_GemIntro`, `REA3_PickUpGem2/4/5` | 4 | 461,660 | the Horned Reaper's gem animations (shared `REA3_*` set; front-end use HYPOTHESIS) |
| `Hero_table.kmf`, `Gem.kmf`, `Tablet.kmf`, `Torch1.kmf` | 4 | — | shared names; their front-end use is HYPOTHESIS |

The 93 meshes in the first eight rows carry front-end names (1,970,333 bytes counting
the 17 `REA3_*` files, which are excluded from the 93).

### 2.3 Front-end textures in `DK2TextureCache/EngineTextures.dir` (CONFIRMED names)

5,767 entries in all; each texture is stored at up to 4 mip levels (`MM0..MM3`).
- `3dFe_*`: 70 entries, 20 base textures (arches, chains, floor, walls, pillar base,
  `MapRock`).
- `3dMap_*`: 392 entries, 98 bases: one texture per level and level group, each with
  `_D` and `_E` variants, plus border tiles in `_PLAYER`, `_RIVAL`, `_SELECTED`,
  `_UNEXPLORED`, `_SECRET` and `_SECRET_DEFEATED` states.
- `MPD_*`: 98 entries, 28 bases (`MPD_map_*` borders, `MPD_selector1..3`).

### 2.4 The 2D campaign-map fallback

The 2D map is a backdrop image, and a byte index bitmap gives the slot under the
pointer (`0x00538D00`, campaign-map-and-progression.md, STRONGLY INFERRED there). The
WAD candidates, by name and pixel format (format CONFIRMED, pairing HYPOTHESIS):
- **Backdrop:** `BackdropMap.png`, 640×480 RGB. For My Pet Dungeon, `BackdropMPD1..6`
  and `BackdropMPDEmpty`; the MPD backdrop varies with the number of levels open.
- **Index bitmap:** `map_template.png`, 640×480 **8-bit paletted** (one byte a pixel).
  `BackdropMap.png`, by contrast, is RGB. For MPD, `2d-BG\pd_template.png` (366×310,
  4-bit paletted).
- **Per-level overlays:** the 133 `2dLevel*`/`2dLevelS*` pieces (4 variants each) and
  `MapColours.png` (64×16).

## 3. Camera paths in `Paths.WAD` (CONFIRMED)

The designers' index in `Engine Path Info.txt.txt` heads paths 250-274 "3D Front End
Camera's". At the CONFIRMED 30 frames a second (camera-paths.md §1):

| Path | Index text | Frames | Seconds |
|---|---|---|---|
| 250 | Intro to 3D Front End | 1 | static |
| 251 / 252 | Entry to Table / Table to Entry | 60 / 60 | 2.0 |
| 253 / 254 | Table to 1st right / 1st right to Table | 60 / 60 | 2.0 |
| 255 | Spare | 1 | static |
| 256 / 257 / 258 | Entry to 2nd right / 2nd right to Entry / Static 2nd right | 60 / 60 / 1 | 2.0 |
| 259 / 260 / 261 | Entry to 1st left / 1st left to Entry / Static 1st left | 60 / 60 / 1 | 2.0 |
| 262 / 263 / 264 | Entry to 2nd left / 2nd left to Entry / Static 2nd left | 60 / 60 / 1 | 2.0 |
| 265 / 266 | 1st left to credit view / Credit view to 1st left | 200 / 100 | 6.7 / 3.3 |
| 267 / 268 | Entry to MPD Table / MPD Table to Entry | 82 / 82 | 2.7 |
| 269 / 270 | MPD Table to 1st right / 1st right to MPD Table | 82 / 82 | 2.7 |
| 271 / 272 / 273 | Entry to 1st right / 1st right to Entry / Static 1st right | 60 / 60 / 1 | 2.0 |
| 274 | Static Entry | 1 | static |

That is 25 files: 8 single-frame static views and 17 moves. A static path is 72 bytes
(16-byte header and one 56-byte frame). Other paths without an index entry (233-236,
244: 450-1,200 frames) are not named as front end.

## 4. Fonts (CONFIRMED)

`Data/Text/Default` holds 22 `.bf4` fonts: 6 `FONT_FRONTEND` and 16 `FONT_INGAME`.
Menu style selection and measurement: [menu implementation boundary](front-end-menus.md#f3-implementation-boundary). Replacement font policy: ADR-0539.

| File | Bytes | Line height | Glyphs | RLE glyphs | Code-point range |
|---|---|---|---|---|---|
| `FONT_FRONTEND10.bf4` | 13,540 | 18 | 251 | 12 | U+0020-U+2019 |
| `FONT_FRONTEND12.bf4` | 15,900 | 22 | 251 | 23 | same |
| `FONT_FRONTEND14.bf4` | 17,172 | 25 | 251 | 58 | same |
| `FONT_FRONTEND20.bf4` | 23,804 | 36 | 251 | 140 | same |
| `FONT_FRONTEND28.bf4` | 31,668 | 49 | 251 | 175 | same |
| `FONT_FRONTEND36.bf4` | 41,764 | 63 | 251 | 184 | same |

`Bf4FontReader` reads them: the install test
`Bf4FontReaderTests.EveryShippedFontUnpacksEveryGlyphToItsSize` opens all 22 files.

## 5. Sound

### 5.1 SDT container (CONFIRMED, bytes, and `SdtArchive`)

`u32 count`, then `count` absolute `u32` offsets. Each entry has a 40-byte header:
`u32 headerSize (40)`, `u32 storedSize`, `char[16] name`, `u16 sampleRate`,
`u8 bitsPerSample`, `u8 type`, then three `u32`s. The payload follows the header.

| Type | Meaning | Where |
|---|---|---|
| 0, size 0 | empty slot, usually named "Blank" | 32 of 43 in FrontEndHD |
| 2 | 16-bit PCM | none in the front-end banks |
| 36 | mono MPEG audio | sound effects and speech |
| 37 | stereo MPEG audio | music tracks |

The MPEG layer differs by bank. Each was checked by walking frame headers to the end of
the entry:
- **Front-end and GUI effects are MPEG Layer I.** `FrontEndHD` entry 0 is MPEG-2 Layer I,
  22,050 Hz, 64 kbps: 68 frames of 384 samples end one byte short of the payload.
  Entry 11 is MPEG-1 Layer I at 44,100 Hz. Their names still end in `.mp2`.
- **Speech is MPEG-2 Layer II mono at 48 kbps, 22,050 Hz.** All 1,505 mentor entries
  walk cleanly; together they last about 5,537 s.
- **Music is MPEG-2 Layer II joint/stereo at 112 kbps, 22,050 Hz.**

OpenKeeper's `SdtFile` reads the same layout, and THIRD-PARTY-NOTICES.md already
records it for `SdtArchive`.

### 5.2 Front-end banks (CONFIRMED, bytes)

| File | Bytes | Entries | Non-empty | Content |
|---|---|---|---|---|
| `Global/FrontEndHD.sdt` | 93,715 | 43 | 11 | GLOW 3 FX, PARCHMENT LOOP ×3 (44.1 kHz), SPIKES IN A/H, SPIKES OUT F, gemfe1, FE BUTTON BIG 5, FE BUTTON SMALL ×2. No `HW` twin exists, unlike most banks |
| `Global/GuiHD.sdt` | 411,299 | 66 | 47 | GUI sounds, including the `FE3D TAB END`, `FE3D TAB SLIDE` and `FE3D CLICK ON T…` front-end entries |
| `Global/Patch_1HD.sdt` | 11,613 | 1 | 1 | "QUIT DUNGEON FX", the `multi_player` bank |

### 5.3 Event maps (`*BANK.map`, `*SFX.map`); no reader in the repo

A `BANK.map` holds a 16-byte GUID-like id, two `u32`s and `u32 count` at 0x18, then
`count` 11-byte records, then `count` length-prefixed archive names. CONFIRMED by
parsing; the field meanings are from OpenKeeper's `BankMapFile` (facts only).

| Bank | Archives it names |
|---|---|
| `front_end` | `Global\Gui`, `Global\FrontEnd` |
| `object_fe_map` | `Global\Gui` |
| `object_fe_parchment` | `Global\FrontEnd`, `Global\Gui` |
| `object_fe_spike`, `object_fe_window` | `Global\FrontEnd` |
| `object_fe_torch` | `Global\Ambience` |
| `options`, `options_speech` | `Global\Gui` |
| `options_music` | `Global\Track_1_4` |
| `multi_player` | `Global\Patch_1` |
| `music` | `Global\Track_1_1..1_4`, `Track_1SG`, `Track_3_1..3_4` (9 archives; `Track_Zero` is named by no bank) |
| `speech_mentor` | `speech_mentor\speech_mentor` |

`SFX.map` sizes (bytes): front_end 248, music 27,042, options 1,224, options_music 1,128, options_speech 732, object_fe_parchment 982, _spike 224, _torch 178, _window 138, _map 130, multi_player 130. Per OpenKeeper's `SfxMapFile`: a 28-byte
header, then 24-byte event records (group count, distances, scale), then 20-byte group
records, then variant and sound records (`index`, `archiveId`). That layout was only
spot-checked here: the group counts it gives are 4 (front_end), 18 (options) and 13
(parchment). So it is STRONGLY INFERRED.

The `object_fe_*` banks name five front-end props: map, parchment, spike, torch and
window. Only some of them have an object in `Objects.kwd` (§2.1).

### 5.4 Music (CONFIRMED sizes; durations computed from MPEG frames)

| Bank | Bytes | Entries (non-empty) | Duration |
|---|---|---|---|
| `Track_1_1HD` | 8,548,092 | 46 | 610 s |
| `Track_1_2HD` | 6,504,028 | 36 (35) | 464 s |
| `Track_1_3HD` | 4,088,220 | 22 | 292 s |
| `Track_1_4HD` | 2,415,768 | 13 | 173 s |
| `Track_1SGHD` | 1,858,284 | 10 | 133 s |
| `Track_3_1HD` | 11,849,056 | 79 | 846 s |
| `Track_3_2HD` | 2,999,764 | 20 | 214 s |
| `Track_3_3HD` | 2,699,788 | 18 | 193 s |
| `Track_3_4HD` | 3,599,716 | 24 | 257 s |
| `Track_ZeroHD` | 11,319,142 | 39 (21) | 808 s |

The music is made of segments (`1pt1-001.mp2` …) that `musicSFX.map` sequences.
`options_music` plays from `Track_1_4`. Which segments the front end plays is a
DKII.EXE question (UNKNOWN). `Global/SoundFont.sf2` (404,764 B, RIFF `sfbk`) is a
SoundFont bank; what uses it is UNKNOWN. binary-census.md places a SoundFont library
in the engine range.

### 5.5 Speech (CONFIRMED)

33 `speech_*` folders hold 3,386 entries in 82,334,879 bytes.
- `speech_mentor`: 1,505 entries, 33,288,635 B. It pairs with `Speech.str`; see
  mentor-speech-and-notifications.md.
- Each campaign and secret level has 60 entries, which pair with that level's
  `LEVELn.str` (60 entries).
- `speech_mypetdungeon` has 80 entries (`MYPETDUNGEON.str` has 80).
- `speech_multiplayer` has 60 entries. `speech_horny` is the Horned Reaper's.

Which bank speaks the campaign-map briefings is UNKNOWN (binary question).

## 6. Movies: `Data/Movies/*.tgq`

### 6.1 Format

Container, codec and display evidence now live in [movie-format.md](movie-format.md);
the shipped-file inventory below is the asset catalogue.

### 6.2 Files (CONFIRMED sizes and chunk counts; durations at 25 fps)

| File | Bytes | Video frames | Duration |
|---|---|---|---|
| `BullfrogIntro.tgq` | 1,986,192 | 189 | 7.6 s (audio 7.4 s) |
| `BullfrogOutro.tgq` | 5,937,644 | 421 | 16.8 s |
| `INTRO.TGQ` | 40,589,016 | 3,288 | 131.5 s |
| `Outro.tgq` | 28,535,944 | 2,033 | 81.3 s |
| `trailer.tgq` | 6,785,068 | 750 | 30.0 s |
| `CutSceneLevel1..19.tgq` | 2,867,780-12,862,104 each, 129,611,564 in all | 263-810 | 10.5-32.4 s |

Per cut scene (frames/seconds): 1 385/15.4, 2 293/11.7, 3 553/22.1, 4 351/14.0, 5 267/10.7, 6 488/19.5, 7 350/14.0, 8 802/32.1, 9 541/21.6, 10 303/12.1, 11 263/10.5, 12 524/21.0, 13 574/23.0, 14 801/32.0, 15 654/26.2, 16 809/32.4, 17 297/11.9, 18 362/14.5, 19 810/32.4. All 24 files total 213,445,428 bytes and
about 10.7 minutes.

When they play (from campaign-map-and-progression.md, binary reads by the sibling,
STRONGLY INFERRED there):
- `CutSceneLevel<N>.tgq` plays on the return to the front end after a **won** campaign
  map. 6a/6b share 6, 11a-c share 11 and 15a/b share 15, which is why there are 19 files
  for 24 levels.
- Level 20 plays `Outro.tgq` and then `BullfrogOutro.tgq`.
- Level 13's movie is skipped for two language settings.

`INTRO.TGQ`, `BullfrogIntro.tgq` and `trailer.tgq` are not tied to a level; when they
play is the menu machine's question.

### 6.3 Runtime playback

The in-memory C# decoder and Unity proof of concept are built; format evidence,
timing measurements and limits live in [movie-format.md](movie-format.md).
The owner requires runtime reads of the install, with no ffmpeg or converted
asset cache; sequencing remains F7 of the front-end plan.

## 7. Text (CONFIRMED, `Dk2StringTable` header counts; `str list` spot checks)

| Files | Count | Entries each | Use |
|---|---|---|---|
| `Text.str` | 1 | 3,086 | all UI text, menu labels included. The id ranges are for the menu-machine read |
| `Speech.str` | 1 | 1,542 | mentor subtitles |
| `LEVEL{n}_BRIEFING.str` (1-20, 6a/b, 11a/b/c, 15a/b) | 24 | 6 | 0 level name, 1 land name, 2 main objective, 3-5 sub-objectives |
| `LEVELM{1..20}_BRIEFING.str` | 20 | 6 | multiplayer map name, objective |
| `LEVELMPD{1..7}_BRIEFING.str` | 7 | 6 | My Pet Dungeon name, land, objective, bonus objectives |
| `LEVELS{1..5}_BRIEFING.str` | 5 | 6 | secret level, with entry 0 empty in S1 |
| `LEVEL{n}.str`, `SECRET{1..5}.str`, `DEMO{1..3}.str`, `MULTIPLAYER.str` | 24+5+3+1 | 60 | in-level speech subtitles, paired with `speech_*` |
| `MYPETDUNGEON.str` | 1 | 80 | My Pet Dungeon subtitles |

That is 92 `.str` files. The support files are `MBToUni.dat` (510 B), `UniToMB.dat`
(538 B) and `residx.dat` (3,479 B) in `Default`, and `Data/Text/JCN.dat` (4,698 B).
The briefing-entry meanings are STRONGLY INFERRED from the content of four files.

## 8. Other front-end-only data

| Item | Facts | Confidence |
|---|---|---|
| `Data/Settings/HiScores.dat` | 1,320 bytes = **ten 0x84-byte (132-byte) rows** (`0x0053E610`, campaign-map-and-progression.md). In each row, +0 is a `u32` score (rows descend from 1,000,000), +4 the name in UTF-16 (32 characters, 64 bytes) and +68 the level label in UTF-16 (32 characters, e.g. "20"). The text is not cleared past its terminator: stale bytes, including a pointer-like `0x006BD44C`, survive in three rows. Written by `0x0053E320`/`0x0053E610` when a slot's Total Evil Rating qualifies | row size CONFIRMED (bytes and the binary); fields STRONGLY INFERRED from 10 rows |
| `HiScores\HiScores.dat` in `FrontEnd.WAD` | 450 bytes = ten **45-byte** rows: a `u32` score (2000, 1800 … 200) and then a UTF-16 name. That is not the 132-byte layout, so this is not the file the game reads at run time; whether DK2 reads it at all is UNKNOWN | rows CONFIRMED |
| `Data/Settings/settings.txt`, `Data/Save/save.txt` | 1 byte each: placeholders | CONFIRMED |
| `Data/Save/*.SAV`, `*.QSAV` | the load-game list's source. `QuickSave`, `MPDQuickSave` and `SkirmishQuickSave` are the named quick-save slots | names CONFIRMED |
| `Data/editor/maps/FrontEnd3DLevel*` | §2 | CONFIRMED |
| `DK2TextureCache/EngineTextures.*` | front-end textures, §2.3 | CONFIRMED |
| `Sprite.WAD` | 24 entries, none with a front-end name (minimap-rendering.md) | CONFIRMED |
