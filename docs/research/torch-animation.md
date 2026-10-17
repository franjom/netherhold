# Wall-flame presentation cadence

Retail DKII.EXE v1.70, Ghidra instructions and complete scoped function reads,
2026-10-17. Position/resource inputs remain in classic-lighting.md section4.

## Confirmed update chain

Presentation vtable0x0066CC94 has update slot0x2C at0x0043B220.
The session0x00526590 asks its presentation's slot0x38 at0x00526C99 and,
only on success, calls slot0x2C at0x00526CE8 with the turn/interpolation record.
The forwarders0x00440FB0/0x00440FC0 delegate slots0x38/0x3C to renderer+0x9E.
That renderer's vtable0x00670574 resolves them to0x0059D760/0x0059D7F0.
The first begins an accepted frame and sets renderer+0x9A; the second ends it
and clears that state. These bodies do not impose a50ms animation interval.
Their device-reset callbacks0x0059D900/0x0059DAB0 rebuild/release the state at
renderer+0xCD; they do not advance the torch phase.

At0x0043B409..0x0043B42A, each presentation update advances the shared torch
handle+0x3BC once through0x004B3AD0. Non-type6 art with flag4 adds its integer
step to the normalized phase and wraps at the stored limit. KTorch's20-frame
descriptor gives step204 and limit4096. This is before the separately gated
50ms particle scheduler at0x0043BF3D; that scheduler does not pace the flame.
The outer application's frame rate/device waiting is not established here.

## Confirmed phase offsets and implementation boundary

Presentation initialization0x0043AAD0 writes seed1 to bridge+0x72 at0x0043AB90.
Tile appearance0x00439B90 draws random4096 on that seed at0x0043A04A and stores
the torch's phase offset at+0x11B. The generator0x005558D0 is the LCG already
specified in dk2-random.md section1. The draw adds that offset to the shared
phase; creation does not reset the shared animation.

Built: ClassicTorchFlameAnimation samples a rebased, unscaled playback clock;
drawing remains read-only. ADR-0494 owns the adapter's50ms playback choice and
its deliberate distinction from native per-frame advancement. The previous
one-Unity-update/one-native-frame mapping accelerated the flame on the owner's
machine. The native outer run loop0x00525370 calls the session update repeatedly;
its post-game Sleep calls do not establish a live20FPS cap. Engine begin-frame
0x00575700 measures FPS; its helper0x00578E50 only stores two inputs and resets
a flag/counter. Backend/display pacing is still unmeasured.
Torch phase offsets retain native LCG/seed1 in a presentation-only stream.

Exact phase offsets are not claimed: the original bridge stream also serves
other consumers, and original creation order differs from our visible-torch
materialization. Hiding/rematerializing a torch can also redraw its offset.
Matching those offsets requires the original bridge stream/lifetime ordering.
