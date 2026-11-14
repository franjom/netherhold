---
systems: [data-formats, art-assets]
---
# Texture codec: engine textures and .444 images

How DKII.EXE decodes `DK2TextureCache/EngineTextures.dat` entries and `.444` loading screens,
read from the binary on 2026-11-14 for the clean-room `src/DK2.Art/Dk2TextureDecoder.cs`.
The .444 header is in [front-end-assets.md](front-end-assets.md) §1.4.

## 1. Shared block coding (STRONGLY INFERRED, binary and emulated native output)

Both codecs are 8x8 block transforms over a bitstream read most significant bit first from
little-endian dwords. Per channel a block holds 64 coefficients:

- AC values use one variable-length code. The engine copy (`0x00659608`) and the loading-screen
  copy (`0x00665EF0`) read identical tables: 160 code entries (`0x007AF1E0`, `0x007AF780`), a
  64-entry zigzag (`0x007AF0E0`, `0x007AF680`). An entry packs code length, run+1 (or 0x41
  end-of-block, 0x42 escape) and magnitude; a sign bit follows short codes. An escape carries a
  6-bit run and an 8-bit level; levels 0x00 and 0x80 take one more byte (positive, or minus 256).
- Every coefficient is stored times its step: the 64-entry tables `0x006C10C0` (engine) and
  `0x00672608` (loading), identical, times 8 (`0x005B2450`, `0x005D7360`). The DC step is 65536,
  so samples come out in 16.16 fixed point.
- The transform is separable: a pass over each coefficient row writes a column of an
  intermediate block (`0x00666210`, `0x00659930`), a pass over each intermediate row writes an
  output row (`0x00666370`, `0x00659A90`). Each pass is an AAN-style butterfly: products by
  the floats 0.5411961, 1.306563 and 0.3826834 combined on the x87 into two sums, each rounded
  to a nearest-even integer, and two by sqrt(1/2) in Q31 (`0x5A82799A`). The loading copy keeps product bits 31..62; the
  engine copy doubles the high dword, dropping bit 31.

## 2. Engine textures (STRONGLY INFERRED, binary and emulated native output)

`0x005B26F0` sets x87 precision to 64 bits and round-to-nearest, then walks 8x8 blocks left to
right, top to bottom. Every shipped entry has flag byte 0x75 or 0xF5 (bit 4 set), which selects
the 32-bit block routine `0x0065CA50`. Per block
`0x0065C290` decodes Y, Cb, Cr and, with flag bit 7, alpha. Each channel's DC is a difference
coded by a size category (table `0x007AF4E0`, indexed by the next byte) added to a per-channel
predictor reset per image (`0x00659580`). A block whose next two bits are `10` is DC only:
`0x0065C120` fills it with DC shifted left 16. Colour (`0x0065CA50`): Y less 16, times 1.169;
Cb and Cr less 128; R adds 1.602 Cr, G adds -0.391 Cb - 0.813 Cr, B adds 2.025 Cb. The sum is
formed in extended precision with the bias 1.5*2^52 + 0x8008 (`0x007AF048`), stored as a double,
and its low dword shifted right 16 indexes a 0..255 clamp table. Alpha is its sample shifted
right 16 and clamped.

## 3. Loading screens (STRONGLY INFERRED, binary)

`0x00668810` reads R, G, B (and alpha) per block, each with an absolute unsigned 8-bit DC, then
the shared AC code and transform. Colour samples round by adding 0x8000 before the shift;
alpha is shifted without rounding. Masked images are not decoded by our code.

The precision these products run at is a HYPOTHESIS: `0x005D77A0` sets no control word, so the
game's current one applies. Emulated at 64 or 53 bits, all 29 shipped .444 images match our
decoder; at 24 bits 18 of them differ (Q812).

## 4. Native oracle (2026-11-14)

`tools/oracle/texture_blocks.py` runs `0x0065CA50` or `0x00668810` on Unicorn over a whole
image. All 5,767 engine textures (control word 0x37F) and all 29 .444 images (0x37F and 0x27F)
decode to the same bytes as `Dk2TextureDecoder`. The decoder it replaced differed from native
in 554 images by isolated values of one. Hash lists and logs:
`F:\DK2Evidence\studies\2026-11-14-texture-decoder-native\`.
