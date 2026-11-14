---
systems: [audio]
---
# Music segment length (Q703)

Source: retail `DKII.EXE`, PyGhidra reads, and shipped music banks measured
with the remake's unchanged `MpegAudioDecoder`, 2026-11-14.
STRONGLY INFERRED from binary/data; no running-game observation.

## Native boundary

The measured `Track_1_1HD.sdt` entries have type `0x25`: stereo, 16-bit MPEG
audio, with the decoded-length flag set. Their 32-bit header field at `+0x20`, named
`SampleCountField` in our parser, is a **decoded PCM byte length**, not a
count of stereo frames. Divide by four for these entries.

The music BANK records have flag `0x22`. `SoundCategory_OpenArchive`
(`0x0060E7D0`) selects the streamed backend through bit `0x02`; its data
access is not the mapped backend. Voice construction `0x00618680` selects
the MPEG path for header type bit `0x20`. The streamed constructor
`0x0061F0C0` copies the flagged header's `+0x20` into instance `+0x30`.
The reader `0x0061F1B0` limits output to `min(requested, length - position)`
using `+0x30` and `+0x14`, advances position by those bytes, and takes its
end path when position reaches length. It does not play every padded MPEG
frame simply because the compressed payload contains it.

The linked segment-end callback `0x006164D0` picks and queues the next sound;
it does not choose a millisecond cutoff from the SFX item. The lower MPEG
reader supplies the byte boundary. Other playback modes and codecs are not
established by this measurement.

## Decoder measurement

All 46 MPEG entries in `Track_1_1HD.sdt` give the same lengths:

| Quantity | Value |
|---|---:|
| SDT decoded bytes | 1,167,316 |
| Declared stereo frames | 291,829 |
| Full NLayer decode, stereo frames | 292,608 |
| Extra frames | 779 |
| Extra duration at 22,050 Hz | 35.3288 ms |

These are parsed/decoded measurements, not captured playback timings.
The disposable probe uses the existing `MpegAudioDecoder.Decode` and does
not modify its output. Inputs, decoder assemblies, source hashes and results
are retained in `F:/DK2Evidence/studies/q703-segment-length--2026-11-14--001`.
`manifest-verified.json` verifies the archived files; the original
`manifest.json` is retained with its invalid self-entry.

## Remake gap

`SdtEntry.SampleCountField` is retained by the parser but unused by playback.
`ClassicSoundPlayer.Fill` queues the whole `PcmClip`; consequently the tested
segments include the extra 779 frames. Q703 is answered, but trimming the
flagged MPEG output is an implementation task, not built by this research.
