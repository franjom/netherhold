# Sound effects: native playback and the OpenKeeper audio leads

Sources: DKII.EXE v1.70, read through Ghidra on 2026-10-29; shipped
`Data/Sound/Sfx/**/*{BANK,SFX}.map`, SDT banks and `Data/editor/Creatures.kwd`.
OpenKeeper PRs #778 (`cc491c3e`), #593, #792 (`c7f8881f`) and #589
(`7f5cd817`) are leads, read from the local clone; facts only, no code reused.
Music sequencing remains in [music.md](music.md); SDT framing remains in
[front-end-assets.md section 5](front-end-assets.md#5-sound).

## 1. Creature footstep events

**CONFIRMED:** the ordinary per-tile presentation pass asks the animation mark
crossing helper for 0, 1 or 2 and requests event `0x314` or `0x315` for the
two nonzero results. The creature wrapper substitutes `0x337`/`0x338` while
creature `+0x3EA` bit 21 is set. This is the grounded-water flag
([existing water evidence](steering-behaviours.md#75-the-creatures-tick-0x004d5a40-and-its-callers)
and the native `InWater` [debug label](creature-object.md#bitfields)), so the
wading event interpretation is **CONFIRMED**. These are presentation events, not simulation
steps. Sources: [render pass](functions/0043CC30.md),
[crossing helper](functions/004B4630.md), [event wrapper](functions/0048FF10.md).

**CONFIRMED:** the file contains 48 pairs of unsigned words at `+0x1323`,
copied to definition `+0x7A3`. Installing type-6 animation art scales each
selected pair's mark by `4096 / resource frame count`; crossing operates on
that normalized animation phase. See [record reader](functions/00568E30.md),
[definition builder](functions/00492320.md) and
[animation chooser](functions/0048BE50.md). `KwdCreatures` does not parse these
pairs. Slots 1/2/4/9/38 correspond to walk/run/recoil/angry/back-off in the
existing animation mapping; the native chooser indexes the selected slot,
without a five-slot whitelist.

**CONFIRMED:** native marks are frame indices normalized by the
selected resource and played at its current phase/rate. Event A/B distinguish
crossings before/after phase wrap, rather than naming a physical left/right
foot. The crossing helper also substitutes the first mark when only the
second is zero and uses strict interval boundaries. OpenKeeper's fixed
`mark / 30` equivalence is **UNDETERMINED** until resource timing and animation
rates are compared; this read does not establish a universal 30 fps rule.

## 2. BANK.map archive flags

The 11-byte archive record's flag byte is at `+9`. **CONFIRMED** by the
[map reader](functions/0060FBC0.md) and the
[archive selector](functions/0060E7D0.md):

| Bit | Native consequence | Verdict |
|---|---|---|
| `0x01` | mapped SDT: whole-file mapping, separate per-entry cache | CONFIRMED; more precise than "loaded whole into memory" |
| `0x02` | MMIO SDT stream with a separately read offset table | CONFIRMED |
| `0x04` | append `.sf2`, choose SoundFont backend; fall back to mapped SDT if open fails | CONFIRMED |
| `0x20` | selector does not test this bit | CONFIRMED locally; "never read anywhere" and an MPEG authoring meaning remain UNDETERMINED |

Backend dispatch and constructors are recorded at
[006197E0](functions/006197E0.md), [00628980](functions/00628980.md),
[00628BD0](functions/00628BD0.md), [00628E40](functions/00628E40.md);
the opens at [00628A10](functions/00628A10.md) and
[00628C70](functions/00628C70.md).
Shipped examples: front_end banks are `0x21`, the nine music banks `0x22`,
and one_shot_atmos names `Global\SoundFont` with `0x24`.

## 3. Random selection, volume and pitch

**REFUTED as exact native formulas:** OpenKeeper uses inclusive random maxima
for volume, pitch and delay. Native random volume uses unsigned item bytes
`+0x0C/0D`, pitch uses signed bytes `+0x0E/0F`, and delay uses unsigned words
`+0x10/12`. Each random path uses `min + random % (max - min)` when the span
is nonzero: the upper endpoint is excluded. Volume/pitch swap reversed
endpoints first; their parameter-driven branches can reach the upper endpoint.
The native setters pass volume through and preserve pitch as an offset;
clamping volume to 127 or interpreting a final playback ratio as
`100 + offset` is **UNDETERMINED** here. See
[volume](functions/0060CAA0.md), [pitch](functions/0060CB90.md),
[delay](functions/0060CCF0.md), [volume setter](functions/00615260.md),
[pitch setter](functions/006152A0.md).

**CONFIRMED with a numerical caveat:** sample weights are running thresholds.
The [native sample picker](functions/0060CFF0.md) selects the first threshold
greater than or equal to the upper 16 bits of `rotr(next sound LCG, 13)`,
and falls back to the first sample when none matches. A single sample costs
no random draw. Raw `random(totalWeight)` with a strict comparison is not the
same value-for-value algorithm. The LCG is independent of simulation RNG.

## 4. Slow-frame throttle and unverified playback budgets

**CONFIRMED:** DK2 has the 29-event throttle and the 17-value rotating gap
table described by OpenKeeper. It is not an OpenKeeper invention.
[The native update](functions/00566FC0.md) runs once per sound-manager/world
frame. Elapsed time **through 300 ms** is normal; 301–499 ms is slow;
500 ms and above is heavy. Thus OpenKeeper's `<300` normal boundary differs
at exactly 300 ms. [Nonpositional](functions/00567810.md) and
[positional](functions/005678F0.md) requests use the same counters and rotation.

The event and gap tables, reset and counter aging belong to those records.
Native speech's throttle bypass has not been traced in this pass.
**UNDETERMINED:** native equivalents of OpenKeeper's 32 voice slots, 24 audible
voices, 12-tile eligibility and 250 ms fades. Those constants are visible in
OpenKeeper, but the category/instance paths read here do not establish their
native equivalents or prove their absence. OpenKeeper's additional three
footsteps per 200 ms window is likewise not established as native.

## 5. SFX.map group flags and parameter id

The native flag/parameter contract is canonical in
[music.md section 5.1](music.md#51-native-group-flag-consumers-2026-10-29).
