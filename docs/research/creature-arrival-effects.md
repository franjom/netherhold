# Creature portal arrival effects

Research only, 2026-10-25. Binary dispatch and shipped-data assignments are
CONFIRMED; descriptions of their combined appearance are STRONGLY INFERRED
from the named artwork and authored colours. No native visual experiment was
run. Independently checked by the `verify_arrivals` subagent.

## Scope and method

Sources: DKII.EXE from `E:/GogGames/Dungeon Keeper 2`, and its
`Data/editor/Creatures.kwd`, `Effects.kwd`, `EffectElements.kwd`.
The repository's `KwdReader`, `KwdCreatures` and `KwdAdditionalRecords`
parsed all 46 creatures, 370 effects and 596 elements. A recursive sweep of
all 408 KWD/KLD files under `Data/editor` found exactly one chunk of each
of those three types, with no parse errors or overriding definitions.
This covers this installation's campaign and skirmish maps, not arbitrary mods.

SHA-256 fingerprints, in that source order:

```text
DKII.EXE          B5E4E2F6B76335E2A2E7F43BB6903BFC6F4170EABF04598955B00F512A47B449
Creatures.kwd     E215C03C7722E89F7736CED7C710406DC10D6CF48E5AD37BED45FAF2055C52E2
Effects.kwd       35D3816E5274D62D0DDBDB7B292F1E7550DBBE9C975FC80EC7FED75B3BF48F10
EffectElements.kwd B8C64FDC1418F1154C3F4BD874BE91B4138013D8BAA1CB2397E53B1BEB7D2AE6
```

Docker MCP profile `local` supplied shared-memory lookup and context-mode
analysis of the extracted records through the read-only `/workspace` mount.
PyGhidra MCP supplied fresh decompilation and disassembly. Scratch extraction
is reproducible from the named parsers and offsets; it contains no native capture.

## Binary selection

CONFIRMED: [Portal_CreateArrival](functions/004DC9A0.md),
`0x004DCB8F..0x004DCBD1`, reads the word at creature-definition `+0x776`.
A nonzero id and unsigned global `0x00759AEC >= 3` request the effect through
world/view slot `+0x38C`. Calling that global the effects-detail setting remains
STRONGLY INFERRED. The effect precedes action 49 and the room-type-25 branch,
so the selection applies to both ordinary Portal and Hero/Mercenary Portal
arrivals. It is outside the first-introduction notification test: later arrivals
also request it. The scripted arrival uses this creator too.

[The loader](functions/00492320.md) copies staging `+0x1326` to definition
`+0x776`, guarded by presence dword `+0x1A4`; staging begins file `+0x3F0`,
so the source is CREATURES record byte `0xF36`, unsigned little-endian 16-bit.
Action 49 is the Entrance animation (art slot 30), not effect id 49.
Placement, heading and the animation exit are recorded in
[portal-creature-action.md](portal-creature-action.md#the-entrance-effect-read-2026-10-14-built-as-adr-0476).

## Creature catalogue

Effect/element ids and resource names are CONFIRMED shipped data. The appearance
column describes those assets; it does not establish final pixels, brightness,
particle density or duration. Beam/disc codes are expanded below.

| Creature id and name | Entrance effect | Composition / expected appearance | Beam/disc |
|---|---:|---|---|
| 3 Bile Demon | 164 | Three green/yellow-green mist elements 332/333/334 | G |
| 4 Mistress | 163 | Blue-grey mist 329, ice chunks 330, pale yellow sparkles 331; mist occupies two generation slots | B |
| 5 Warlock | 165 | Fire artwork `FB` 338 in two slots, turquoise `Sparklee` 339 | F |
| 6 Dark Elf | 178 | `parrow` 348 in two slots and green mist 349 | G |
| 7 Goblin | 181 | Grey smoke `sm` 354 and muted `Sparklea` 355 | D |
| 8 Vampire | 176 | Bat artwork `lowbat` 341, white/grey mist 342/343, green and purple sparkles 344/345 | D |
| 9 Skeleton | 177 | Flying-bone artwork `Bones` 346 and grey smoke 347 | D |
| 10 Troll | 180 | Orange/yellow and yellow-green `Sparkle-00`/`Sparkle-01` 352/353 | F |
| 11 Salamander | 172 | Fire 338 plus three generated fire-trail branches ending in `fb` 141 | F |
| 12 Firefly | 179 | Two yellow/ochre sphere elements `wtsphere` 350/351 and smoke 347 | D |
| 22 Black Knight | 184 | Lightning branch, hail/ice branch and purple/magenta sparkles 356/357 | None |
| 23 Dark Angel | 160 | Dark green mist 325, black-sphere artwork `bksphere` 327, lightning and hail/ice branches | N |
| 24 Rogue | 185 | Coin artwork `Coin` 358, white `GSparkle` 359 and smoke 347 | N |
| 45 Maiden Of The Nest | 366 | Green mist 585 and purple `gsparkle` 586 | None |

Vampire and Skeleton have configured arrival effects but lack the ordinary
Portal availability bit; a configured effect does not make a type eligible.
Unique variants likewise have configured effects but fail the selector's
unique-type gate. Eligibility is [the existing portal research](portals-mercenary.md#2-what-separates-the-sides-is-the-room-type-inside-the-shared-code).

### The Mistress recollection

The owner's recollection was a blizzard/storm. CONFIRMED data supports that:
effect 163 emits `mist`, `icechunk` and `sparklea`, with WhirlpoolRate 12 and
a blue beam companion. Describing the combination as a swirling ice storm is
STRONGLY INFERRED. It does not call the separately named Hailstorm effect 341;
that branch belongs to Dark Angel and Black Knight arrivals.

### Hero Portal mercenaries

CONFIRMED: all nine types with the Hero Portal availability flag use **369**:
13 Knight, 15 Giant, 16 Wizard, 17 Elven Archer, 18 Thief, 19 Monk,
20 Fairy, 25 Guard, 30 Royal Guard. Effect 369 emits purple-grey smoke
element 596 (`SM`, RGB 120/80/120), with NextEffectId 370; effect 370 emits
element 594 (`gsparkle`, RGB 100/100/100). Neither has a beam/disc link.
WhirlpoolRate is 8 on 369 and 0 on 370. This is one shared mercenary effect,
not nine creature-specific designs. The common creator's effect request occurs
before its room-type branch; keeper ownership does not change this choice.

### Remaining records: complete 46-type coverage

| Creature ids | Configured effect |
|---|---|
| 32 Unique Firefly; 33 Unique Goblin; 34 Unique Warlock; 35 Unique Troll; 36 Unique Dark Elf | 179; 181; 165; 180; 178 respectively |
| 37 Unique Skeleton; 38 Unique Mistress; 39 Unique Salamander; 40 Unique Rogue | 177; 163; 172; 185 respectively |
| 41 Unique Bile Demon; 42 Unique Vampire; 43 Unique Black Knight; 44 Unique Dark Angel | 164; 176; 184; 160 respectively |
| 2 Prince Tristran; 14 Dwarf; 21 King Reginald; 26 Prince Felix; 29 Lord Of The Land; 31 Prince Balder | 369, same configured composition as mercenaries; this does not establish ordinary portal eligibility |
| 1 Imp; 27 Horny; 28 Stone Knight; 46 Jack | 0: no effect through this field; their other creation/summoning paths are outside this catalogue |

## Effect dependency catalogue

CONFIRMED: generator [0x004386F0](functions/004386F0.md) treats generated ids
as effects when parent flag `0x40` is set, otherwise as elements. The two
namespaces overlap numerically. In these tables `E` and `L` distinguish them;
duplicates are intentional generation slots. `Next` is the stored NextEffectId,
not an assertion that one effect waits for the preceding effect to finish.
All 33 reachable effect records and 41 referenced elements are covered here.
Their death/contact links, including element NextEffectId, are all zero.

| Effect | Generated records | Next |
|---:|---|---:|
| 160 | E342, E341, E340 | 159 |
| 163 | L329, L330, L331, L329 | 167 |
| 164 | L332, L333, L334 | 166 |
| 165 | L338, L339, L338 | 168 |
| 172 | E171, E9, E9, E9 | 0 |
| 176 | E175, E174, E173 | 161 |
| 177 | L346, L347 | 161 |
| 178 | L348, L349, L348 | 166 |
| 179 | L350, L351, L347 | 161 |
| 180 | L352, L353 | 168 |
| 181 | L354, L355 | 161 |
| 184 | E340, E183, E341 | 0 |
| 185 | L358, L359, L347 | 159 |
| 366 | L585, L586 | 0 |
| 369 | L596 | 370 |
| 370 | L594 | 0 |
| 342 | L325, L327, L327 | 0 |
| 341 | L537, L21, L21, L21 | 0 |
| 340 | E4 | 0 |
| 4 | No generated ids; own art `ProceduralMesh`, type 7, named Lightning Bolt | 0 |
| 183 | L356, L357 | 0 |
| 171 | L338 | 168 |
| 9 | E169 | 0 |
| 169 | L141 (`fb`, named Fire Trail Element 3) | 0 |
| 175 | L344, L345 | 0 |
| 174 | L342, L343 | 0 |
| 173 | L341 | 0 |
| 159 (N) | L324, `Lightbeams`, RGB 50/50/50 | 161 |
| 161 (D) | L326, `Whitedisc`, RGB 255/255/255 | 0 |
| 166 (G) | L335, `Lightbeams`, RGB 50/255/0 | 216 |
| 167 (B) | L336, `Lightbeams`, RGB 10/120/255 | 161 |
| 168 (F) | L337, `Lightbeams`, RGB 255/80/10 | 161 |
| 216 | L326, `Whitedisc`, RGB 255/255/255 | 0 |

Effect 341 is named Portal Enter Hailstorm effect: L21 is `icechunk`,
RGB 150/150/150, and L537 is `gsparkle`, RGB 90/90/250.
The beam elements are type-6 animated art; the disc is a flat sprite.
Resource names and RGB are authored data, not calibrated displayed colours.

### Authored light descriptors

The reachable effects also carry these nonzero-radius light descriptors
(CONFIRMED data). Raw RGB and radius are given without interpreting light flags
as displayed intensity. All other reachable effects have radius zero.

| Effect | Light RGB | Radius (raw) | Light flags |
|---:|---|---:|---|
| 4 | 128/255/255 | 10240 | 0x3 |
| 9 | 0/0/0 | 8192 | 0x80 |
| 159 | 255/255/255 | 8192 | 0 |
| 160 | 70/255/120 | 10240 | 0 |
| 163 | 120/250/144 | 10240 | 0x2000 |
| 164 | 120/174/44 | 10240 | 0x3000 |
| 165 | 144/94/50 | 10240 | 0x1800 |
| 171 | 144/124/120 | 10240 | 0x1800 |
| 172 | 144/104/120 | 10240 | 0x1800 |
| 176 | 120/255/0 | 10240 | 0 |
| 177 | 44/44/44 | 10240 | 0x3800 |
| 178 | 200/104/200 | 8192 | 0x1000 |
| 179 | 255/240/30 | 10240 | 0 |
| 180 | 44/180/50 | 10240 | 0x800 |
| 181 | 144/144/144 | 10240 | 0x3800 |
| 183 | 144/100/44 | 10240 | 0x2800 |
| 185 | 144/144/144 | 10240 | 0x3800 |
| 342 | 44/144/100 | 10240 | 0x1800 |
| 366 | 34/144/44 | 10240 | 0x3800 |

## Verification and limits

The verifier independently checked the loader, dispatch assembly, action-table
distinction, all 46 creature assignments, all recursive effect/element links,
and the absence of embedded creature definitions in the installed editor/maps
corpus. Its 6,582 raw-field comparisons had zero mismatches; it also checked
all 19 light rows and all four SHA-256 fingerprints. The parent extraction
additionally used the repository's real parsers
for the 408-file sweep and fingerprinted the four primary sources.

UNKNOWN: matched original-game visual timings and particle trajectories,
visibility at every graphics setting, and the exact native procedural lightning
appearance. The named Lightning Bolt data confirms that branch's selection,
not a complete reconstruction of its renderer. No gameplay or rendering code
was changed, and this catalogue does not claim current Unity equivalence.
