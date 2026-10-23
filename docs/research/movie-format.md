# Movies: the .tgq container, TQI video and EA ADPCM audio as DKII.EXE plays them

**Scope:** everything a decoder needs to play `Data/Movies/*.tgq` the way DKII.EXE does:
the chunk container, the audio header and the EA ADPCM decode, the TQI ("pIQT") intra-frame
video codec down to the IDCT and the colour conversion, the display geometry, audio/video
sync, and skipping. Out of scope: when the front end plays which movie
(`front-end-menus.md` section 2).

Every row is a static read of DKII.EXE v1.70 unless it says otherwise; nothing here was
decoded and compared with a capture yet, so tables and arithmetic read from the bytes are
**CONFIRMED** as "what the code does", and claims about what a field *means* are at most
**STRONGLY INFERRED**. Function records: `docs/research/functions/<ADDRESS>.md`.
Proof-of-concept file: `BullfrogIntro.tgq` (1,986,192 bytes).

## 1. The player is EA's own library, linked in

| Fact | Provenance | Confidence |
|---|---|---|
| The player is not Smacker or Bink. It is EA's in-house movie library compiled into DKII.EXE, with its own PE sections: `uva_data` (0x007A8000; colour tables, VLC tables, zigzag, IDCT constants), `idct_dat`, `tqia_dat`, `lbmpeg_d`. The block decoder, IDCT and colour converters are hand-written assembly (register calling convention, `ESP` reused as a pointer in `0x005AF310`). | PE section table; `0x005AE220`, `0x005B018C`, `0x005AF310` | CONFIRMED (sections, asm); STRONGLY INFERRED (EA library) |
| One dispatcher handles EA's whole family: TGV key/delta (`kVGT`/`fVGT`, kinds 1/2), TGQ (`pQGT`, kind 3), **TQI (`pIQT` and `UV2f`, kind 4)**, MAD key/mid/end (`kDAM`/`mDAM`/`eDAM`, kinds 5-7). Only kind 4 is described here. | `0x005A7EA0` | CONFIRMED |
| Playback runs on its own threads: `0x005A8F70` opens the file and starts a decode thread (`0x005A8AE0` -> `0x005A8B20`), which starts an audio feeder thread (`0x005AC210` -> `0x005ABEF0`). The front end only polls `0x005A8FB0` every 50 ms while pumping input (`0x0053BCD0`). | `0x005A8F70`, `0x005A8A60`, `0x005A8B20`, `0x005AC210` | CONFIRMED |

## 2. Container

| Fact | Provenance | Confidence |
|---|---|---|
| A chunk is a 4-byte tag, a little-endian u32 size that includes the 8-byte header, then size-8 payload bytes. Tags are compared as little-endian u32s, and `0x005A7EA0` accepts both byte orders only for `SCHl`, `SCDl`, `SCCl`, `SCLl`, `SNDC` and the MAD and TGV tags (`SCHl` and `lHCS` alike); `SEAD`, `SEND`, `SCEl`, `1SNh`, `1SNd`, `1SNe`, `pIQT` and `pQGT` match one fixed order. | `0x005A7EA0`, `0x005A7CE0` | CONFIRMED |
| At open, leading header chunks (`SCHl`, `SEAD`, `1SNh`) are counted: their number is the number of audio streams, and their presence turns audio on. Stream 1 is played by default; with several streams, of each run of consecutive audio chunks only the n-th is decoded and the rest are skipped. The file is then rewound to its start. | `0x005A7CE0`, `0x005A7B10`, `0x005A7EA0` | CONFIRMED |
| Dispatch: `SCHl`/`1SNh` -> audio header (`0x005A87F0`); `SEAD` -> header with fixed values (2 channels, 22050 Hz, compression 10); `SCDl`/`1SNd`/`SNDC` -> audio block (`0x005A8430`); `pIQT` -> video frame kind 4 (`0x005A82D0`); `SCCl`, `SCLl` -> skipped; `SCEl`, `1SNe`, `SEND` -> end of stream. Any other tag is an error. | `0x005A7EA0` | CONFIRMED |
| `SCCl` (here u32 187, the audio block count) is never read. | `0x005A7EA0` | CONFIRMED |
| Chunks are read into ring buffers (video 1,200,000 bytes, audio 300,000 bytes, 24 queue slots each). Before playback starts the reader pre-reads until 21 audio blocks are queued; during playback it reads ahead while fewer than 22 video and 22 audio chunks are queued. | `0x005A7B10`, `0x005A8E10` | CONFIRMED |
| BullfrogIntro.tgq: `SCHl` (40), then `SCDl`, `pIQT` strictly alternating (187 audio, 189 video), `SCCl`, `SCEl`. | file scan | CONFIRMED |

### 2.1 The SCHl audio header

| Fact | Provenance | Confidence |
|---|---|---|
| Payload bytes 0-3 (`PT\0\0` here) are skipped unread. | `0x005A87F0` (parse starts at payload+4) | CONFIRMED |
| From payload+4 the header is a list of elements `tag:u8, len:u8, value: len bytes big-endian` (1-3 byte values are sign-extended). Elements before the byte `0xFD` are skipped (here `00 01 02`, `06 01 65`, `1B 01 19`). After `0xFD`: `0x81` -> sample width (default 16), `0x82` -> channels (default 1), `0x83` -> compression (default 0), `0x84` -> sample rate (default 22050); any other element is skipped; the element byte `0x8A` ends the parse (the bytes after it, here `8A 04 00000000 FF`, are never read). | `0x005A87F0`, `0x005A8910`, jump table `0x005A88F4` | CONFIRMED |
| `0x85` (here `02 81 84` = 164,228 samples) is skipped: DKII.EXE never reads the total sample count. | `0x005A87F0` | CONFIRMED |
| BullfrogIntro.tgq: channels 2, compression 7, no rate element, so **22050 Hz**. | file + defaults | CONFIRMED |
| Compressions handled: 0 = raw 16-bit PCM (mono is duplicated to stereo), 7 = EA ADPCM (section 3), 10 = another codec through `0x005AC703` (unread). | `0x005A8430` | CONFIRMED (dispatch); UNKNOWN (codec 10) |

## 3. Audio: compression 7 (EA ADPCM)

| Fact | Provenance | Confidence |
|---|---|---|
| `SCDl` payload: u32 sample count (**ignored** for compression 7), then four little-endian int16, sign-extended: payload+4 current L, +6 previous L, +8 current R, +10 previous R; then the groups. The predictor state is reloaded from every block's header (no carry-over between blocks). | `0x005A8430` (`MOVSX` at `0x005A8521`-`0x005A8553`) | CONFIRMED |
| Stereo: groups = floor((payload size - 12) / 30); each group is 30 bytes and yields 28 stereo samples. Mono: floor((payload - 12) / 15) groups of 15 bytes, 28 samples each; mono uses current/previous at payload+4/+6. BullfrogIntro: 32 groups -> 896 samples per block. | `0x005A8430` | CONFIRMED |
| Stereo group: byte 0 high nibble = L coefficient index, low nibble = R coefficient index; byte 1 high nibble = L shift, low nibble = R shift; then 28 bytes, each **high nibble = L sample, low nibble = R sample**. Mono group: byte 0 high = coefficient index, low = shift; then 14 bytes, high nibble first, then low nibble. | `0x005ABC60`, `0x005ABAE0` | CONFIRMED |
| Coefficients, int32 at `0x00670AA0`: c1 = {0, 240, 460, 392}, c2 (at `0x00670AB0`) = {0, 0, -208, -220}. Index 4-7 of c1 reads c2[0-3]; higher indices read past the table (not valid in practice). | `read_bytes 0x00670AA0` | CONFIRMED |
| Per sample: d = (nibble << 28) >> (shift + 8) (32-bit arithmetic shift, i.e. sign-extended nibble << (20 - shift)); new = (d + cur*c1 + prev*c2 + 0x80) >> 8 (arithmetic); clamp to [-32768, 32767]; prev = cur; cur = new (the clamped value). | `0x005ABC60` | CONFIRMED |
| Output is always interleaved 16-bit stereo at the header rate (mono duplicated into both channels). It is the same formula as FFmpeg's `adpcm_ea`. | `0x005A8430`, `0x005AC450` (DirectSound format 2 ch, 16 bit, rate from header) | CONFIRMED |

## 4. Video: TQI (`pIQT`)

### 4.1 Frame header

| Bytes | Meaning | Provenance | Confidence |
|---|---|---|---|
| 0-1 | u16 width (320) | `0x005AA2E0` | CONFIRMED |
| 2-3 | u16 height (480) | `0x005AA2E0` | CONFIRMED |
| 4 | u8 quantiser q (100 in every BullfrogIntro frame) | `0x005AA2E0`, `0x005AA878` | CONFIRMED |
| 5, 6 | 0x14, 0x1E = 20, 30: width/16 and height/16 | values only; DKII.EXE never reads them | CONFIRMED (unread); STRONGLY INFERRED (meaning) |
| 7 | flags (3 here): bit 1 selects the colour-conversion routine (set: `0x005AF310`/`0x005AF7E0`, section 4.6; clear: `0x005ACFC4`, unread); bit 0 is stored at decoder+0x44 and its reader was not found | `0x005AA2E0`, `0x005AB250`, `0x005AB2F0` | CONFIRMED (stored); UNKNOWN (bit 0 use, bit-1-clear path) |
| 8.. | bitstream | `0x005AA8D8` (data pointer = payload + 8) | CONFIRMED |

### 4.2 Dequantisation matrix (rebuilt every frame, `0x005AAB10`)

| Fact | Provenance | Confidence |
|---|---|---|
| Two tables in `.rdata`. **S** (int32 at `0x00670860`, row-major) is the AAN prescale in Q13, S[r][c] = 8192 / (a_r * a_c), a_0 = 1, a_k = sqrt(2) cos(k pi / 16). **Q** (int32 at `0x00670960`) is the MPEG-1 default intra matrix (values below). | `read_bytes` | CONFIRMED |
| **q = 100:** M[i] = fixmul(S[i], 0x80000) = 8 * S[i]: a flat matrix, Q is not used. **q != 100:** f = (107 - q) * 0.0625 (x87, stored as a 32-bit float); m_i = trunc(Q[i] * f * 65536.0); M[i] = fixmul(S[i], m_i). Then always M[0] = fixmul(S[0], 0x80000) = 0x10000. | `0x005AAB10` (`CMP EAX,0x64` at `0x005AAB19`; constants `0x00670A90` = 107.0, `0x00670A98` = 0.0625, `0x00670A9C` = 65536.0) | CONFIRMED |
| fixmul(a, b) = (a * b + 0x8000) >> 16 on the 64-bit product (the `ADC` adds bit 15). | `0x005AAB46`-`0x005AAB4E` | CONFIRMED |
| Coefficients are indexed by the *transposed* natural position (section 4.4), and so is M; with q != 100 that applies Q transposed. Irrelevant at q = 100 because 8*S is symmetric. | `0x005AE68D` | CONFIRMED |
| This differs from FFmpeg's `eatqi` (which always applies the MPEG matrix with qscale = (215 - 2q) * 5). If BullfrogIntro decodes wrong with the flat matrix, check the frame bytes before doubting the read: the branch is unambiguous. | `0x005AAB19` | CONFIRMED (DK2 side) |

S (Q13), row-major:

```
 8192  5906  6270  6967  8192 10426 15137  29692
 5906  4258  4520  5023  5906  7517 10913  21407
 6270  4520  4799  5332  6270  7980 11585  22725
 6967  5023  5332  5925  6967  8867 12873  25251
 8192  5906  6270  6967  8192 10426 15137  29692
10426  7517  7980  8867 10426 13270 19266  37791
15137 10913 11585 12873 15137 19266 27969  54864
29692 21407 22725 25251 29692 37791 54864 107619
```

Q, row-major (the MPEG-1 default intra matrix):

```
 8 16 19 22 26 27 29 34
16 16 22 24 27 29 34 37
19 22 26 27 29 34 34 38
22 22 26 27 29 34 37 40
22 26 27 29 32 35 40 48
26 27 29 32 35 40 48 58
26 27 29 34 38 46 56 69
27 29 35 38 46 56 69 83
```

### 4.3 Bitstream

| Fact | Provenance | Confidence |
|---|---|---|
| The stream is read as little-endian 32-bit words, **bits taken most-significant first within each word** (a peek is `SHLD` of word[pos >> 5] and word[(pos >> 5) + 1] by pos & 31). Equivalent: byte-swap every 32-bit word, then read big-endian. | `0x005AE220` | CONFIRMED |
| The bit position starts at 0 at payload+8 each frame and runs on through the whole frame: no alignment, no slice or row markers. | `0x005A95B0` | CONFIRMED |
| The three DC predictors (Y, Cb, Cr) are reset to 0 at the start of each frame only (not per row). | `0x005A95B0` (decoder +0x508/+0x50C/+0x510) | CONFIRMED |

### 4.4 Macroblocks and blocks (`0x005AE220`)

| Fact | Provenance | Confidence |
|---|---|---|
| 16x16 macroblocks in raster order (left to right, top to bottom; 20 x 30 here). Each has six 8x8 blocks in the order **Y0 (top-left), Y1 (top-right), Y2 (bottom-left), Y3 (bottom-right), Cb, Cr**: 4:2:0. | `0x005AE220` (destinations +0, +0x20, +0x200, +0x220 in a 16-int-wide luma buffer; Cb at +0x400, Cr at +0x500) | CONFIRMED |
| DC: a size is decoded with the MPEG-1 luma DC table (blocks Y0-Y3) or chroma DC table (Cb, Cr), sizes 0-8 only. Luma: `100`=0, `00`=1, `01`=2, `101`=3, `110`=4, `1110`=5, `11110`=6, `111110`=7, `1111110`=8. Chroma: `00`=0, `01`=1, `10`=2, `110`=3, `1110`=4, ... `11111110`=8. Then size bits v; if the top bit of v is 0, diff = v - (2^size - 1), else diff = v. pred += diff (Y0-Y3 share one predictor; Cb and Cr each have their own). | tables `0x007AC92C` (7-bit peek), `0x007AC9AC` (8-bit peek); `0x005AE220` | CONFIRMED |
| Coefficient 0 = pred * M[0] = pred * 0x10000. | `0x005AE513` | CONFIRMED |
| AC: MPEG-1 Table B.14 (DCT coefficients, table zero), intra rules: `10` = end of block (also valid as the first AC code), `11s` = run 0 level 1, no special first-coefficient code. 111 run/level codes up to 16 bits plus sign, EOB and escape, decoded by a 17-bit peek through eight lookup tables (`0x007AC69C`..`0x007AC8AC`; entry = length << 16, run+1 << 8, level). Sign bit right after the code: 1 = negative. | `0x005AE639`-`0x005AE749`; table enumerated from the binary | CONFIRMED |
| Escape `000001`, then 6-bit run, then 8-bit level: 0x00 -> read 8 more bits, level = that (128-255); 0x80 -> 8 more bits, level = that - 256; 0x81-0xFF -> level - 256; else level as is. No sign bit after an escape. | `0x005AE74E`-`0x005AE7C2` | CONFIRMED |
| Position: start at k = 0; each code advances k by run + 1. The coefficient goes to natural index Z[k] where Z is the standard MPEG zigzag **transposed** (row and column swapped): Z[1] = 8, Z[2] = 1, Z[3] = 2, Z[4] = 9, Z[5] = 16 ... (table `0x007AC5B0` stores it reversed, indexed by 63 - k). Value = level * M[Z[k]]. No oddification, no mismatch control, no coefficient clamp; nothing stops k passing 63 in a corrupt stream. | `0x007AC5B0`, `0x005AE67B`-`0x005AE69A` | CONFIRMED |
| Because the coefficients are stored transposed and the IDCT (4.5) writes its output transposed back, the picture comes out in normal orientation: an implementer may use the plain zigzag and a plain row/column IDCT. | `0x005AE7D0`-`0x005AE90C` | CONFIRMED |
| If the first AC code is EOB the IDCT is skipped and all 64 samples = coefficient 0 >> 17 (arithmetic), which equals what the IDCT would give. | `0x005AE91A` | CONFIRMED |

### 4.5 IDCT (`0x005B018C` first pass, `0x005B03B5` second pass)

An AAN (Arai-Agui-Nakajima) inverse built from the AAN forward flowgraph: the scale factors
are folded into S (4.2); the even-part sqrt(1/2) is a Q31 integer multiply, the odd part is x87
floating point rounded to nearest. Pass 1 runs on the 8 rows of the coefficient block and
writes each result as a column of a 9-int-stride scratch block; pass 2 runs on the scratch rows
and writes 8 consecutive outputs per call, each shifted `>> 17` (arithmetic). Pass 1 skips a row
whose s1..s7 are all 0 (all 8 outputs = s0); the result is identical.

For inputs s0..s7 (all 32-bit ints), with mulsq(x) = 2 * floor(x * 0x5A82799A / 2^32)
(signed; 0x5A82799A = sqrt(1/2) * 2^31) and rnd(x) = round-half-even to int via the 1.5 * 2^52
magic constant (`0x007AC5A8`):

| Step | Formula | Confidence |
|---|---|---|
| odd sums | d53 = s5 - s3; d17 = s1 - s7; osum = s1 + s3 + s5 + s7; odiff = (s1 + s7) - (s3 + s5) | CONFIRMED |
| odd part | z5 = 0.38268343 * (d17 + d53); A = rnd(0.54119611 * d53 + z5); B = rnd(1.30656302 * d17 - z5); t = mulsq(odiff); q1 = A + t; q2 = B + t; q3 = B + osum | CONFIRMED |
| even part | e = mulsq(s2 - s6); E = s2 + s6 + e; c0 = (s0 + s4) + E; c3 = (s0 + s4) - E; c1 = (s0 - s4) + e; c2 = (s0 - s4) - e | CONFIRMED |
| outputs 0-7 | c0 + q3, c1 + q2, c2 + q1, c3 + A, c3 - A, c2 - q1, c1 - q2, c0 - q3 | CONFIRMED |
| constants | `0x007AC598` int 0x5A82799A; `0x007AC59C` 0.54119611f; `0x007AC5A0` 1.30656302f; `0x007AC5A4` 0.38268343f (all single-precision floats; products in x87 extended precision) | CONFIRMED |
| FPU | rnd() needs the x87 in round-to-nearest with at least double precision; that DK2's front end leaves it so is assumed, not read. | HYPOTHESIS |

Checked by hand: a lone s1 or s2 gives outputs proportional to cos((2n+1)k pi/16) with one
ratio for all n, so the flow is a true IDCT. The result is an 8x8 block of "half-scale" samples
v, nominally 0..127 (pixel ~ 2v; a DC-only block gives v = floor(pred / 2)).

### 4.6 Colour conversion (bit 1 of byte 7 set; `0x005AF310` and `0x005AF7E0`)

Tables built once at open by `0x005A9DA0`. For a sample v (luma or chroma), x = clamp(v - 64, -64, 63);
for chroma f = x + 0.5 (x87):

| Table (indexed by v) | Value | Confidence |
|---|---|---|
| Y | x * 0x800801 + 0x4883D8A9 (adds x into three fields; biases B 169, G 123, R 145) | CONFIRMED |
| Cb | ((a & 0x1FF) << 11) + (b & 0x1FF), a = trunc(0.5 - 0.3441 * f), b = trunc(0.5 + 1.772 * f) | CONFIRMED |
| Cr | ((c & 0x1FF) + ((d & 0x1FF) << 12)) << 11, c = trunc(0.5 - 0.7141 * f), d = trunc(0.5 + 1.402 * f) | CONFIRMED |

Constants are doubles at `0x00670A68` (0.3441), `0x00670A70` (0.5), `0x00670A78` (-1.772),
`0x00670A80` (0.7141), `0x00670A88` (-1.402); `0x00670A60` is the float -0.5 subtracted to
form f. Note 0.3441 and 0.7141, not JPEG's 0.344136 and 0.714136. trunc = toward zero
(`__ftol`).

| Step | Fact | Provenance | Confidence |
|---|---|---|---|
| combine | W = Y[vY] + Cb[vCb] + Cr[vCr] as a wrapping uint32; iR = W >> 23; iG = (W >> 11) & 0x1FF; iB = W & 0x1FF. A field that goes negative borrows from the one above it, exactly as the 32-bit add does. | `0x005AF319`-`0x005AF36C` | CONFIRMED |
| to 8 bit | R = clamp(2 * iR - 177, 0, 255); G = clamp(2 * iG - 133, 0, 255); B = clamp(2 * iB - 225, 0, 255) (brightness 100, the default, scales by 100/100). The three tables are contiguous (R 306 entries at `0x007AB730`, G 262 at `0x007ABBF8`, B 354 at `0x007AC010`); an index past a table's end reads the next table. | `0x005A9DA0` | CONFIRMED |
| net effect | for mid chroma (v = 64): R = B = 2vY - 13, G = 2vY - 15 (before clamping). | derived | CONFIRMED (arithmetic) |
| to surface | each 8-bit value is cut to the surface's channel width with rounding, min(((1 << (7 - n)) + value) >> (8 - n), 2^n - 1), and shifted into place from the DirectDraw masks. | `0x005A9DA0`, `0x005A9C60` | CONFIRMED |
| chroma upsampling | nearest neighbour: one chroma sample serves a 2x2 luma square (the same chroma row is passed for luma rows 2r and 2r+1; one chroma column for two luma columns). No interpolation. | `0x005AEBED` call pattern, `0x005AF310` | CONFIRMED |

### 4.7 Display

| Fact | Provenance | Confidence |
|---|---|---|
| The front end opens the player with flags 0x0B05064C, plus 0x00800000 (audio) or 0x00A00000 (with a sound object). Flag 0x40 doubles the width, flags 0x30 (line doubling) are clear, flags 0x04/0x08 centre the picture. So **320x480 is shown as 640x480 by doubling every pixel horizontally; lines are not doubled**, centred on the surface. | `0x0053BCD0`, `0x005AABF0`, `0x005AA670` | CONFIRMED |
| On a 16-bit surface the doubling is done by the colour tables: each entry holds the pixel in both halves of a dword, so one dword write is two equal pixels (`0x005AB2F0` steps 0x40 bytes per macroblock). | `0x005A9DA0`, `0x005AB2F0`, `0x005AF310` | CONFIRMED |
| On a surface wider than 16 bits per pixel the same path writes one 32-bit pixel per sample, which would not double; DK2's front-end surface depth was not checked. | `0x005A9DA0` | HYPOTHESIS |

## 5. Frame rate, sync and skipping

| Fact | Provenance | Confidence |
|---|---|---|
| **There is no frame-rate field.** The header parser reads only 0x81-0x84; video is paced by the audio. | `0x005A87F0`, `0x005A8B20` | CONFIRMED |
| Each queued audio block remembers how many video frames had been read before it. The audio thread wakes every 10 ms and, when DirectSound's play cursor leaves room, copies the next block into a 32 KB looping buffer and publishes that block's video count. The decode thread shows video frame n only when n <= that count minus L, where L = 32768 / (average audio-block bytes) = 9 for BullfrogIntro (3,584-byte blocks); it waits in 2 ms sleeps, at most ~152 times, then shows the frame anyway. | `0x005ABEF0`, `0x005ABF70`, `0x005AC180`, `0x005A8B20`, `0x005A7B10` | CONFIRMED |
| Net effect: a frame appears when the audio that follows it in the file starts playing (less the buffer lead). BullfrogIntro starts with two video frames before any audio, has 189 frames over 187 audio blocks, and its blocks are not all the same size (896 samples, 868 for some, 176 for the last), so the rate follows the file; where the blocks are 896 samples at 22050 Hz it is **22050 / 896 = 24.61 frames per second**. Our player follows the file (`EaMovieTimeline`); without audio it uses that rate. | derived from the rows above and the file scan | STRONGLY INFERRED |
| Frame dropping: when the decoder is 3 or more frames behind (`+0x98`), TQI frames are read and discarded without decoding (TGV and MAD frames still decode). | `0x005A8B20` | CONFIRMED |
| Without a sound device the audio thread fakes the play cursor at 882 bytes per 10 ms (88,200 B/s = 22050 Hz stereo 16-bit), whatever the header's rate. | `0x005AC180`, `0x005AC450` | CONFIRMED |
| Skipping: the key handler `0x0053C270` (Esc, Enter, Space) and the mouse handler `0x0053C2A0` (any event with a non-zero second argument, i.e. a button) set `0x0073F598`. The player's per-frame callback `0x0053C2C0` (flag 0x40000) returns 1 and clears the flag, and the decode thread then stops. | `0x0053C270`, `0x0053C2A0`, `0x0053C2C0`, `0x005A8B20` | CONFIRMED (code); STRONGLY INFERRED (mouse arg = button) |
| The front end keeps the movie's end at `0x005A8FB0`: playing while the state is not 7 (closed). | `0x005A8FB0`, `0x005A8980` | CONFIRMED |

## 6. Open points

| Point | How to close |
|---|---|
| Whether the flat q = 100 matrix decodes BullfrogIntro cleanly | the C# proof of concept: decode frame 1 both ways and look |
| Byte-7 bit 0, and the bit-1-clear colour path `0x005ACFC4` | read when a movie with those flags turns up (all BullfrogIntro frames have 3) |
| FPU precision/rounding mode during playback | debugger: read the x87 control word inside `0x005B018C` |
| Front-end surface depth (16 vs 32 bit) | debugger or DirectDraw mode set in the front end |
| Compression 10 (`0x005AC703`) | not used by DK2's movies seen so far |
