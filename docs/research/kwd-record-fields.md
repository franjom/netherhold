# KWD/KLD record fields completed for M1

**Scope:** Dungeon Keeper 2 v1.70 shipped editor data. This page records file
layout, not gameplay behaviour.

## Summary of confidence

| Claim | Confidence | Basis |
|---|---|---|
| CREATURE_SPELLS is 266 bytes; SHOTS 239; EFFECTS 246; EFFECT_ELEMENTS 182 | **Known** | `contentSize / itemCount` in the shipped files; every parsed record round-trips |
| THINGS tags 194–205 have the recorded payload sizes and layouts | **Known** for the eleven shipped tags; **Hypothesis** for DeadBody (202) | All 6,318 shipped payloads consume and round-trip; tag 202 has no shipped instance |
| Both TRIGGERS payloads are 16 bytes with an 8-byte union followed by ids, type and repeat count | **Known** | All 9,161 shipped payloads consume and round-trip |
| LEVEL has a 25,603-byte base metadata block, an optional 80-byte extension, then typed 72-byte paths and a height-sized `u32` tail | **Known** for the shipped files | All 66 LEVEL files consume and round-trip |
| GLOBALS is a LEVEL path discriminator rather than a shipped standalone chunk | **Known** for this corpus; standalone form **Unknown** | Exhaustive sweep of 408 KWD/KLD files finds no type-0 chunk or path row |
| Semantic field names below | **Strongly inferred** | Names come from OpenKeeper's GPLv3 reader; widths/order are independently checked against the shipped bytes |

## Re-checking

`dk2tools kwd corpus` parses all 408 KWD/KLD files and reports the count for
each of the 18 record-type ids. For every newly decoded record it also compares
the retained source image byte-for-byte. `dk2tools kwd records <file>` prints
decoded ids/types and consumed lengths. The corpus on 2026-09-07 contained:

| type | records |
|---|---:|
| CREATURE_SPELLS | 41 |
| SHOTS | 65 |
| EFFECTS | 370 |
| EFFECT_ELEMENTS | 596 |
| THINGS | 6,318 |
| TRIGGERS | 9,161 |
| LEVEL | 66 |
| GLOBALS | 0 |

## Fixed definition records

All offsets are record-relative. An `ArtResource` is 84 bytes: ASCII name[64],
flags `u32`, a 12-byte type-dependent value block, then four discriminator/data
bytes. A `Light` is 24 bytes: four fixed-point `s32` values, flags `u32`, RGBA.
Fixed-point values are deliberately retained as raw signed integers here; M1
does not decide their gameplay unit.

- **CREATURE_SPELLS (266):** name[32], two ArtResources, two shot-data `u32`s,
  range, flags, seven `u16` combat/sound/text ids, four byte ids, recharge,
  alternative-shot byte, and `Unknown_00EF[27]`.
- **SHOTS (239):** name, ArtResource, Light, air friction, mass, speed, two data
  words, process flags, radius, flags, eight effect ids, health, eleven byte
  ids/categories, sound category[32], threat and burn duration.
- **EFFECTS (246):** name, ArtResource, Light, ten motion/scale values, flags,
  nine lifecycle ids/values, eight generated ids, twelve generation values,
  generation type, elements per turn and `Unknown_00F4`.
- **EFFECT_ELEMENTS (182):** name, ArtResource, ten motion/scale values, flags,
  seven ids/health values, RGB, random/table colour indices, fade percentage and
  next-effect id.

The model exposes each scalar. Repeated homogeneous fields (the eight generated
effect ids) remain a fixed-length list. Every record retains its exact bytes and
`ToBytes()` returns a defensive copy, preserving editor padding and unknowns.

## THINGS

The outer `tag:u32, payloadSize:u32` framing was already known. M1 now decodes
the payload itself:

| tag | typed payload | bytes | unknown fields retained explicitly |
|---:|---|---:|---|
| 194 | Object | 24 | `Unknown_0008[4]` |
| 195 | Trap | 16 | `Unknown_0008`, `Unknown_000F` |
| 196 | Door | 20 | `Unknown_0008`, `Unknown_0011[3]` |
| 197 | ActionPoint | 56 | none |
| 198 | NeutralCreature | 24 | `Unknown_0017` |
| 199 | GoodCreature | 32 | `Unknown_001D[2]` |
| 200 | KeeperCreature | 28 | none |
| 201 | HeroParty | 555 | `Unknown_0023`, `Unknown_0027`; sixteen 32-byte GoodCreature slots |
| 202 | DeadBody | 16 | none; layout unobserved in shipped data |
| 203 | EffectGenerator | 36 | `Unknown_0008`, `_000C`, `_0010`, `_0012`, `_001E[6]` |
| 204 | Room | 20 | `Unknown_0008`, `_000C`, `_000F` |
| 205 | Camera | 84 | none; eighteen fixed-point position/lens values are individually exposed |

The tag/type names and semantic field names are OpenKeeper's readings. They are
used as navigation labels, not evidence about behaviour. No implementation was
copied. Payload widths were independently checked against all shipped values.

## TRIGGERS

Both tags use:

```text
00..07  type-dependent body
08      trigger id       u16
0A      next/sibling id  u16
0C      child id         u16
0E      target/action type byte
0F      repeat count byte
```

`GenericTriggerRecord` and `ActionTriggerRecord` expose the body bytes, useful
word/dword views, and a type-aware parameter dictionary. Known parameter names
are transcribed from the external reader. Unsupported types expose the two body
dwords as `Unknown_0000` and `Unknown_0004`, so no byte disappears because a
semantic name is missing. M1 reads the format only; executing any trigger stays
in M6.

## LEVEL and GLOBALS

The level metadata contains the UTF-16 name, description, author, email and
information fields; trigger id; the `ticksPerSecond` field already used by the
timing research; a 520-byte `Unknown_1184`; 512 UTF-16 message slots; flags,
sound/reward/text fields, introduction overrides and terrain path. The optional
80-byte extension is retained as `Optional_6403` because its internal semantic
names have not been independently corroborated.

The inner section starts with check value 222 and a byte count, followed by
`itemCount` path rows:

```text
recordType:s32  Unknown_0004:s32  ASCII path[64]
```

The final `height` dwords remain `UnknownPathTail`. Type 0 (`GLOBALS`) belongs
to this path discriminator. No shipped v1.70 level uses it, although the reader
accepts and round-trips it in an install-independent synthetic test. Q220 records
the absent standalone sample; Q221 records the unobserved DeadBody payload.

## Provenance

- Primary: the shipped v1.70 KWD/KLD corpus under the configured DK2 install.
- Secondary: OpenKeeper field order, widths and names, consulted under GPLv3 as
  permitted by ADR-0003; implementation was not copied.
- Existing container framing and sizes: `docs/research/kwd-container.md`.

