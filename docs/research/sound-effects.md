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

The event and gap tables, reset ([`0x00567080`](functions/00567080.md)) and
counter aging belong to those records. Ours is `SoundThrottle`, applied to every
effect request in `ClassicSoundPlayer.Play` and updated after each world frame.
Speech is not exempt: [`0x00568490`](functions/00568490.md) sets the mode to 0
around its request, which lifts only the id >= 0x400 exemption, so a speech id
equal to a listed event is throttled too. Ours queues speech without the
throttle; whether any shipped speech id collides is unchecked. Untraced: whether
the front end updates the throttle (ours does not, so it keeps the last game
frame's load, normal before any) and when native first stamps it in a level
(ours stamps it at construction, so the first game frame is heavy).
**UNDETERMINED:** native equivalents of OpenKeeper's 32 voice slots, 24 audible
voices, 12-tile eligibility and 250 ms fades. Those constants are visible in
OpenKeeper, but the category/instance paths read here do not establish their
native equivalents or prove their absence. OpenKeeper's additional three
footsteps per 200 ms window is likewise not established as native.

## 5. SFX.map group flags and parameter id

The native flag/parameter contract is canonical in
[music.md section 5.1](music.md#51-native-group-flag-consumers-2026-10-29).

## 6. The hand and its cursor

Read 2026-11-02 for plan Phase W2 ([world-sounds plan](../plans/2026-10-31-world-sounds.md)).
Every request is one slot `+0x20` call (positional) at the site named; the category is the
requester's definition, so a row is silent when no shipped `SFX.map` of that category has the
group with samples (checked across every non-speech category).

**The pointer position (STRONGLY INFERRED; the tests that pin it say so).** Interface `+0x1068` is the pick record
[0x005992B0](functions/005992B0.md) fills: `+0x0/+0x4` the pointer, `+0x8/+0xC` the tile,
`+0x10/+0x14` the world X/Y (12.12, clamped to the map), `+0x1C` the hit kind, `+0x20` its id.
So `+0x1078/+0x107C` is the pointer's world position, and the hand's requests use it with height
`0x2000` (2.0 tiles). The hand category is the name `HAND` ([0x00567240](functions/00567240.md));
ours is `hand`.

Verdict "CONFIRMED" is for the sound site; the records of `0x004BFBD0` and `0x004C0CB0` stay LIKELY for the rest of those functions.
`HandCursorFeedback.Reset` (focus loss, disable) also clears the slap latch, which DK2 clears only at completion.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| HAND `0xEC`, positional | state 5 callback [0x0040E300](functions/0040E300.md), entry event | CONFIRMED | `HandCursorFeedback.Play(Tag)` |
| `0xEC`, nonpositional, terrain definition (`+0x195`) | same callback | silent: only `hand` has the group, and no terrain category has an SFX.map | nothing |
| HAND `0xC5`, positional | state 11 callback [0x0040E520](functions/0040E520.md), frame 3, latch | CONFIRMED | `HandCursorFeedback.Advance` |
| creature `0xC3` | [0x004BFBD0](functions/004BFBD0.md), handle 0 | CONFIRMED | `HandSystem.PickUp` |
| object `0xC3` | [0x004C0CB0](functions/004C0CB0.md), the object's handle | CONFIRMED | `HandSystem.PickUpObject`, `PickUpStoreGold` |
| creature, object `0xC4` | [0x004BC710](functions/004BC710.md) | CONFIRMED | `HandSystem.Drop`, `DropObject` |
| object `0xC5` | [0x0049C2A0](functions/0049C2A0.md) | silent | nothing |
| creature `0xCA`/`0xCB`/`0xCC`/variable | [0x0040C290](functions/0040C290.md), kind 2 | CONFIRMED, not built | waits for the action table |
| object `0xE4` | same, kind 4, a special | silent | nothing |

**Tag (`0xEC`).** State 5 is requested through [0x0040ABC0](functions/0040ABC0.md) by two arms of
[Hand_Click](functions/004075E0.md): tool modes 0 and 3, on a press whose target is a tile with the
Taggable flag (and interface `+0x13CF` zero, or the tile's terrain word `+0x185` zero). The same arms
start the drag (`+0x13DB` = 1, `+0x13F0` = 1, or 2 when the click record's flag bit 1 is set) and the
tag or untag command (`0x28`/`0x29`) is posted at release, so the sound is on the **press**, once per
drag, tag and untag alike, not on completion. The state's callback receives event 0 (adopt), then
event 1 (the sound); event 2 (completion, one frame at 30 a second) moves it to state 6, which has no
sound. Ours fires the tag sound only on a primary press with a Hand-tool drag, so DK2's tag and untag from other tool modes have no UI path. No other call of `0x0040ABC0` pushes the literal 5.

**Slap (`0xC5`).** State 11 (Slap, 15 frames at 30 a second) is requested by
[0x00408EE0](functions/00408EE0.md) on an empty-handed right press over a creature the hand may slap
(own, or another keeper's when both `0x0048E6A0` and `0x004918B0` accept it: the first failing posts action `0x6A` with no swing, the second does nothing), a slappable object (definition flag
`0x200`) or a trap whose definition byte `+0xBC` is `0x24` (and whose `+0x10C` bit 5 is clear). Callback event 3, run on every channel update before the
completion test, sounds once when the frame is at least 3 and byte `0x006CE95C` is zero, then sets
it; event 2 (completion) clears it. A request on that channel that arrives while the swing is at frame 3 or less is
ignored whatever state it asks for (a tag included); one after frame 3 restarts the swing and leaves the latch set, so it plays no second sound
until a swing completes. The drop states use the base channel and are not guarded. Ours builds the creature case (`SlapCreatureCommand`); the object and trap
slaps have no command yet.

**Lift and drop.** The creature lift sound ends the pick-up preparation, so it plays for a creature
that is or becomes state 75 even when the hand is full, and not when state 75 is refused. Creature
`0xC3` and `0xC4` pass handle 0, so they neither use nor replace the creature's own voice; the object
requests pass the object's handle. A drop sounds at the position the drop passes to `Hand_Drop` for any thing, AI drops included. For the human drop
(`0x004C0EE0`) its height is never set (0), and X and Y are the live creature under the hand's position (height
included) or else the pointer's; ours mirrors that, and deviates for the AI keepers, which drop with their own position. Of the 145 shipped objects only object 1 (`OBJECT_GOLD`) has samples for either (one each);
the treasury pile (object 3), gold bags and most others have an empty category, the chicken's has no
group. A store's pile is object 3, so lifting store gold is silent.

**Hover.** [0x0040C290](functions/0040C290.md) runs from the interface's per-update pointer refresh
(`0x00403D78`) and from every pointer event ([0x00406530](functions/00406530.md)). For a hover kind 2
(the viewer's own hoverable creature; kind 3, another's, is silent) it requests, with the creature's
handle, only on game turns that are a multiple of 3 times the tick rate (container `+8`, 4): the
event is the current action record's sound word ([creature-action-table.md
section 5](creature-action-table.md#5-the-25-word-is-a-sound-id-not-a-label)) when an action is running
(`+0x3A9` neither 0 nor 21) and the word is nonzero, otherwise, while the creature is in no room
(`+0x3B9` zero), `0xCA`, `0xCB` or `0xCC` by mood class `+0x28A` (Happy, Unhappy, Angry; Q269). The mood arm itself needs only `+0x3B9`, `+0x28A` and the turn gate, but
only the action word decides which arm runs, and ours has no action table or single current-action field to read
it from; the creature hover waits for that (W7.2). A special under the hand requests `0xE4` in
its object category on three of every four turns; `OBJECT_SPECIAL` has no such group.
