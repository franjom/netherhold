---
systems: [audio]
---
# QMixer distance attenuation: how QMIXER.DLL turns {minimum, maximum, scale} into a volume

Source: `QMIXER.DLL` (QSound QMixer, the copy DK2 ships next to `DKII.EXE`), read through Ghidra
on 2026-11-13 in its own project (image base `0x18000000` as Ghidra placed it).
**Every address below is a QMIXER.DLL address, not a DKII.EXE one.** Nothing here was seen in the
running game; it is all read from the DLL's code and its constant tables (STRONGLY INFERRED unless
a line says otherwise). The DKII.EXE side (what DK2 sends, and when QMixer is used at all) is in
[0061AE30](functions/0061AE30.md) and [Q678](open-questions.md).

## 1. The curve

`d` is `|M * (source - listener)|`, with `M` the listener orientation matrix at mixer `+0x90`
(`0x1800BA40`); that is the Euclidean distance only if `M` is orthonormal, and DK2's use of
`QSWaveMixSetListenerOrientation` is unread. `min`, `max`, `scale` are the three floats sent through
`QSWaveMixSetDistanceMapping`. Positions and distances share one unit (the caller's; DK2 uses 2 units
per tile); nothing rescales them (the only transform on a position is a z sign flip, applied alike
to source and listener).

| distance | distance factor |
|---|---|
| `d > max` | 0 (silent; the maximum is a hard cut-off, never a floor) |
| `d <= min` | 1 (full volume) |
| `min < d <= max`, `scale == 0` | 1 (no attenuation at all) |
| `min < d <= max`, `scale == 1` | `min / d` |
| `min < d <= max`, other `scale` | `min / (min + scale * (d - min))` |

So `scale` is a rolloff multiplier on the distance past `min`: 1 is inverse distance (6 dB less per
doubling of `d`), below 1 is shallower, above 1 steeper, 0 is flat. The factor is a linear
amplitude multiplier, continuous at `min` (value 1), and at `max` it drops from its last value to 0.
No clamp or minimum other than the two comparisons; `min`, `max` and `scale` are stored as sent
(the export does not check `min < max`).

The channel's final gain is `volume * distanceFactor * coneFactor`, all linear, where `volume`
is the channel volume (`SetVolume` value / 32767; default 10000/32767 = 0.305) and the cone
factor is 1 unless a source cone is set. On the DirectSound path it is sent as
`round(100 * 20 * log10(gain))` hundredths of a decibel, or -10000 (DirectSound's silence) when
`gain <= 0`.

DK2's values: `min` 2.0, `scale` 0.5, so for `2 < d <= max` the factor is `4 / (d + 2)`: 1.0 at
d=2 (one tile), 0.667 at 4 (-3.5 dB), 0.5 at 6 (-6.0 dB), 0.333 at 10 (-9.5 dB), 0.25 at 14
(-12.0 dB), 0.167 at 22 (-15.6 dB), 0.1 at 38 (-20 dB), then 0 beyond the per-item `max`.
In tiles `t = d / 2`: `2 / (t + 1)`.

## 2. Where it is stored and computed (QMIXER.DLL addresses)

- **Export** `QSWaveMixSetDistanceMapping` `0x18003710`: reads `scale` from the struct only when
  `cbSize >= 0x10` (else 1.0), builds a command record {type 0xC, min, max, scale} (`0x1800b000`),
  hands it to `0x180014f0`, which with flags 0 applies it to one channel through `0x180085e0` (index
  times `0x1E4`) and otherwise (not traced here) to every channel, through `0x18006bb0` (which either applies it at once or queues it behind the channel's pending commands, depending on the call flags and the channel's state; not traced further).
- **Consumer of command 0xC**: the channel's command switch `0x18006ce0`. It stores `min` at channel
  `+0x44`, `max` at `+0x48` and `scale` at `+0x4c` (plus copies at `+0x12c/+0x130/+0x134`), only
  when one of the three differs, and marks the channel dirty (`+0x140`, `+0x154`, mixer `+0x58`)
  so the gain is recomputed on the next update. **It stores `scale` only when the mixer's rolloff
  flag (mixer `+0xd8`) is clear**; see section 3.
- **Getter** `QSWaveMixGetDistanceMapping` `0x18001bf0`: writes min, max, scale to struct offsets
  `+4`, `+8`, `+0xc` (it does not write `cbSize`; it requires 0x10 writable bytes).
- **Defaults** (channel init copies the template at `0x18027118`; the three floats are at `0x18027158`):
  min 1.0, max 100.0, scale 1.0. A reused channel keeps its last voice's mapping.
- **The gain**: `0x18009150` (called from `0x180074f0` on each channel update, and by the export
  `QSWaveMixCalculateChannelParams` `0x18001110`; `0x180074f0` passes channel `+4`, which is why the
  reads appear at `+0x40..+0x48` for the channel's `+0x44..+0x4C`) computes the distance, then `d <= max` selects
  the factor from `0x18009460` (the table in section 1; the constants it compares with are 0.0
  and 1.0 at `0x180244e8` and `0x180244ec`), else 0. Result per channel: `+0xbc` factor,
  `+0xc0` volume * distance * cone.
- **Volume out**: `0x18004ba0` takes the gain and `0x18004d30` converts it to hundredths of a
  decibel (100 * 6.0206 * log2(gain), constants at `0x180243f0` and `0x180245f4`) for the
  DirectSound buffer's `SetVolume`; pan comes from the azimuth, the Doppler factor goes to
  `SetFrequency` separately.

## 3. The listener rolloff and other global factors

- `QSWaveMixSetListenerRolloff` `0x18003900` stores a float at mixer `+0xdc` (default 1.0, set in
  the mixer constructor `0x18007c30`) **only when the mixer's rolloff flag is set**, and then
  overwrites the `scale` of every channel with it (`0x18007690`).
- That flag is bit `0x10` of the flags word of `QSWaveMixInitEx` (mixer `+0xd8`). DK2's init
  struct (built at `0x0061A500` in DKII.EXE) has the flags value `0x2` (struct zeroed at
  `0x0061A52D`, `[+4] |= 2` at `0x0061A551`), never `0x10`, and DKII.EXE does not import `QSWaveMixSetListenerRolloff`. So for DK2 the flag is
  clear: each channel's own `scale` is used and no global rolloff factor enters the factor in
  section 1.
- When the flag is set, the DirectSound path stops folding the distance factor into `SetVolume`
  (only the channel volume is sent) and leaves the distance to the 3D buffer with the rolloff as
  its factor. That mode is not DK2's.

## 4. What was not settled

- **Which backend DK2 gets (HYPOTHESIS).** The DLL builds two voice kinds per mixer, plain
  DirectSound buffers and DirectSound 3D buffers (`0x180084e0`, type words `0x1xxxxxx` and
  `0x2xxxxxx`; the 3D buffer is obtained with `IID_IDirectSound3DBuffer`, `0x18004760`), the 3D kind
  only when the device reports 3D support (`0x18007f90`). For a 3D voice the same command 0xC also
  forwards `min` and `max` to the buffer's `SetMinDistance` and `SetMaxDistance`
  (`0x18004840`), so DirectSound's own distance model would apply as well as, or instead of, the
  curve above; with the flag clear the DLL still sends `volume * distanceFactor` to `SetVolume`
  on such a voice (cone excluded there, left to the 3D buffer). Nothing read decides which kind
  DK2's voices are; the curve in section 1 is certain to be computed either way, whether it is
  the audible one on a 3D voice is not.
- **DK2 asks for every buffer kind (lead).** The init sets all six buffer-count fields to the channel
  count (`0x0061A576..0x0061A59D`, `[+0x28]..[+0x3C]`), so 3D buffers are requested too. On a voice
  made as a DirectSound 3D buffer with DirectSound's default rolloff the audible gain would be roughly
  the factor above times `min / d`; which kind a voice gets was not read.
- **The listener basis.** `Sound_RequestPositional` (`0x005678F0`, `0x00567980..0x005679E4`,
  [record](functions/005678F0.md)) hands the mixer `(-dx, 0, -dy)` relative to the listener's
  `+0x4C`/`+0x50`, the vertical slot a literal 0, so the distance is flat, measured from the camera
  focus (QMixer's listener stays at the origin).
- Whether DK2 sets a source cone (the cone factor); `QSWaveMixSetSourceCone` is imported but its
  use was not read.
- No capture of the running game: the dB figures are arithmetic on the formula, not measurements.
