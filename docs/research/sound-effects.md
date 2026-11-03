---
systems: [audio]
---
# Sound effects: native playback and the OpenKeeper audio leads

Sources: DKII.EXE v1.70, read through Ghidra on 2026-10-29; shipped
`Data/Sound/Sfx/**/*{BANK,SFX}.map`, SDT banks and `Data/editor/Creatures.kwd`.
OpenKeeper PRs #778 (`cc491c3e`), #593, #792 (`c7f8881f`) and #589
(`7f5cd817`) are leads, read from the local clone; facts only, no code reused.
Music sequencing remains in [music.md](music.md); SDT framing remains in
[front-end-assets.md section 5](front-end-assets.md#5-sound). The interface and global requests (phase W8) are in [sound-effects-interface.md](sound-effects-interface.md).

## 1. Creature footstep events

**STRONGLY INFERRED:** the ordinary per-tile presentation pass asks the animation mark
crossing helper for 0, 1 or 2 and requests event `0x314` or `0x315` for the
two nonzero results. The creature wrapper substitutes `0x337`/`0x338` while
creature `+0x3EA` bit 21 is set. This is the grounded-water flag
([existing water evidence](steering-behaviours.md#75-the-creatures-tick-0x004d5a40-and-its-callers)
and the native `InWater` [debug label](creature-object.md#bitfields)), so the
wading event interpretation is **STRONGLY INFERRED**. These are presentation events, not simulation
steps. Sources: [render pass](functions/0043CC30.md),
[crossing helper](functions/004B4630.md), [event wrapper](functions/0048FF10.md).

**STRONGLY INFERRED:** the file contains 48 pairs of unsigned words at `+0x1323`,
copied to definition `+0x7A3`. Installing type-6 animation art scales each
selected pair's mark by `4096 / resource frame count`; crossing operates on
that normalized animation phase. See [record reader](functions/00568E30.md),
[definition builder](functions/00492320.md) and
[animation chooser](functions/0048BE50.md). Ours: `CreatureDef.FootstepMarks`, built in
[sound-effects-continuous.md section 1](sound-effects-continuous.md#1-footsteps). Slots 1/2/4/9/38 correspond to walk/run/recoil/angry/back-off in the
existing animation mapping; the native chooser indexes the selected slot,
without a five-slot whitelist.

**STRONGLY INFERRED:** native marks are frame indices normalized by the
selected resource and played at its current phase/rate. Event A/B distinguish
crossings before/after phase wrap, rather than naming a physical left/right
foot. The crossing helper also substitutes the first mark when only the
second is zero and uses strict interval boundaries. OpenKeeper's fixed
`mark / 30` equivalence is **UNDETERMINED** until resource timing and animation
rates are compared; this read does not establish a universal 30 fps rule.

## 2. BANK.map archive flags

The 11-byte archive record's flag byte is at `+9`. **STRONGLY INFERRED** by the
[map reader](functions/0060FBC0.md) and the
[archive selector](functions/0060E7D0.md):

| Bit | Native consequence | Verdict |
|---|---|---|
| `0x01` | mapped SDT: whole-file mapping, separate per-entry cache | STRONGLY INFERRED; more precise than "loaded whole into memory" |
| `0x02` | MMIO SDT stream with a separately read offset table | STRONGLY INFERRED |
| `0x04` | append `.sf2`, choose SoundFont backend; fall back to mapped SDT if open fails | STRONGLY INFERRED |
| `0x20` | selector does not test this bit | STRONGLY INFERRED locally; "never read anywhere" and an MPEG authoring meaning remain UNDETERMINED |

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

**STRONGLY INFERRED with a numerical caveat:** sample weights are running thresholds.
The [native sample picker](functions/0060CFF0.md) selects the first threshold
greater than or equal to the upper 16 bits of `rotr(next sound LCG, 13)`,
and falls back to the first sample when none matches. A single sample costs
no random draw. Raw `random(totalWeight)` with a strict comparison is not the
same value-for-value algorithm. The LCG is independent of simulation RNG.

## 4. Slow-frame throttle and unverified playback budgets

**STRONGLY INFERRED:** DK2 has the 29-event throttle and the 17-value rotating gap
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

Verdict "STRONGLY INFERRED" is for the sound site; the records of `0x004BFBD0` and `0x004C0CB0` stay HYPOTHESIS for the rest of those functions.
`HandCursorFeedback.Reset` (focus loss, disable) also clears the slap latch, which DK2 clears only at completion.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| HAND `0xEC`, positional | state 5 callback [0x0040E300](functions/0040E300.md), entry event | STRONGLY INFERRED | `HandCursorFeedback.Play(Tag)` |
| `0xEC`, nonpositional, terrain definition (`+0x195`) | same callback | silent: only `hand` has the group, and no terrain category has an SFX.map | nothing |
| HAND `0xC5`, positional | state 11 callback [0x0040E520](functions/0040E520.md), frame 3, latch | STRONGLY INFERRED | `HandCursorFeedback.Advance` |
| creature `0xC3` | [0x004BFBD0](functions/004BFBD0.md), handle 0 | STRONGLY INFERRED | `HandSystem.PickUp` |
| object `0xC3` | [0x004C0CB0](functions/004C0CB0.md), the object's handle | STRONGLY INFERRED | `HandSystem.PickUpObject`, `PickUpStoreGold` |
| creature, object `0xC4` | [0x004BC710](functions/004BC710.md) | STRONGLY INFERRED | `HandSystem.Drop`, `DropObject` |
| object `0xC5` | [0x0049C2A0](functions/0049C2A0.md) | silent | nothing |
| creature `0xCA`/`0xCB`/`0xCC`/variable | [0x0040C290](functions/0040C290.md), kind 2 | STRONGLY INFERRED | `CreatureHoverSound` |
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
(`+0x3B9` zero), `0xCA`, `0xCB` or `0xCC` by mood class `+0x28A` (Happy, Unhappy, Angry; Q269). Built as `CreatureHoverSound` (presentation), the action told by `CreatureCurrentAction`'s stand-ins
([sound-effects-continuous.md section 2](sound-effects-continuous.md#2-the-action-table)). A special under the hand requests `0xE4` in
its object category on three of every four turns; `OBJECT_SPECIAL` has no such group.

## 7. Terrain, digging, claiming and rooms

Read 2026-11-02 for plan Phase W3. Same conventions as section 6: every request is one slot
`+0x20` call (handle, category, event, position); a row is silent when no shipped `SFX.map` has the
group with samples.

**The terrain category (STRONGLY INFERRED).** Terrain definition `+0x195` is the manager's category handle
for the name in TERRAIN file bytes `0x1DF`..`0x1FE` (the loader [0x004F66E0](functions/004F66E0.md)
copies them from its intermediate record, shifted `+0xB8`, and resolves them through slot `+0x1C`;
shipped values read from the file: `TERRAIN_ROCK`, `TERRAIN_WALL`, ..., and `ROOM_*` for every room
terrain, so a room tile's category is its room terrain's. Terrains 27, 28, 29, 30, 38 and 39 have none).
Ours: `TerrainDef.SoundCategory`. Two more handles follow, `+0x199` (Temple only: `ROOM_TEMPLE_WATERLESS`
by name) and `+0x19D` (the second name, file `0x204` by the same shift, unread); the ambience grid
([0x0044E2A0](functions/0044E2A0.md)) reads all three (W7.4).

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| terrain `0xF4`, handle 0, tile centre, height 0 | [0x00452F60](functions/00452F60.md) at `0x0045327C`: health reached 0 and the destroyed type (`+0x1C6`) differs from the tile's terrain, after the tile is set or sold, before the destroy effect; category of the terrain the tile had | STRONGLY INFERRED | `TileWorkSystem.Execute`, `TremorRules.DamageWall` |
| terrain `0x10C`, same shape | same function at `0x00453359`: health reached the maximum and the max-health type (`+0x1C5`) differs, before the conversion and its effect | STRONGLY INFERRED; the plan's "dig hit" is wrong, it is the upward conversion (reinforce, claim, corner fill) | `TileWorkSystem.RequestCompletionEffect` |
| imp `0x10A` / `0x10B`, the imp's handle, at the imp | [0x004974C0](functions/004974C0.md) at `0x00497635`: after the strike that left the tile not claimable (only then); `0x10A` when the tile's room word (`+2` low 12 bits) is 0, else `0x10B`; the handle is stored back | STRONGLY INFERRED | `TileWorkSystem.RequestClaimSound` |

`0xF4` has samples in `terrain_rock`, `terrain_gold`, `terrain_gems`, `terrain_wall` (3 each), so
digging rock, mining gold or gems and breaking a reinforced wall sound; dirt path, claimed path and
every room terrain but the heart's have no `0xF4`, so a ruined room tile is silent (the sale's own sounds
are separate, below). `0x10C` has one sample in `terrain_rock` only, so reinforcing sounds and
claiming dirt path does not; `creature_imp` also has `0x10C`, which is not this site. `0x10A` and
`0x10B` have one sample each in `creature_imp`. `0x10B` needs a claimable tile that still carries a
room link, which no shipped terrain produces (a claimable path is neither a room nor solid); ours
tests the room index, and no test reaches it.

**Building and selling.** A room's tiles use the category of the terrain of its room definition's terrain byte
(`+0x44E`, then terrain `+0x195`): `ROOM_LAIR` for a lair tile. All three requests are handle 0.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| room `0xF1`, tile centre, height 0 | [0x004C5C30](functions/004C5C30.md) at `0x004C5D87`: paid tile, game turn differs from the turn argument (the drag wave passes its queue turn, so its first diagonal is silent; the keeper wrapper passes 0; loader and trigger builds are unpaid) | STRONGLY INFERRED | `RoomBuildSystem.PlacePaidTile` |
| room `0xF5`, tile centre, height `0x1000` | [0x004C5DB0](functions/004C5DB0.md) at `0x004C5EF8`: a sale with refund, after the refund is paid in | STRONGLY INFERRED | `RoomSellSystem` |
| room `0xF4`, same place | same function, `0x004C5F29`: the refundless sale (a worked-down room tile, trigger actions) | STRONGLY INFERRED site; no group outside `room_dungeon_heart` | nothing |
| room `0x2F8`, lowest corner of the rectangle, height `0x1000` | [0x00442960](functions/00442960.md) at `0x00442D57`: command-table handler for the human rectangle build, after its pre-check passes | STRONGLY INFERRED site; issue side HYPOTHESIS | `RoomBuildSystem.RequestAcceptedSound` |
| room `0xF7`, nonpositional | [0x004E5650](functions/004E5650.md) at `0x004E570C`: a room changes owner, the room's handle `+0x14`, definition `+0x449` | STRONGLY INFERRED site | silent: no `SFX.map` has `0xF7` |
| room `0xF8` | [0x004E7FE0](functions/004E7FE0.md) at `0x004E81BE` | STRONGLY INFERRED site | silent: no `SFX.map` has `0xF8` |

`0xF1` has one sample in every buildable room category and the heart, `0xF5` two in each buildable one
(not the heart or portal), so building and selling sound for every room a keeper can lay. `0x2F8`, the
plan's "most rooms have it", is the accept click of a dragged build: one sample in each buildable room's
category, none in the heart or portal. A destroyed room tile asks for `0xF4` twice in DK2 (the refundless sale
and `Tile_ApplyWork`, both in the room terrain's category) and nothing plays outside the heart; ours emits the
applier's only.

## 8. The Dungeon Heart

Read 2026-11-02 for plan Phase W3. The heart room's category is the room definition's terrain's, as for any
room (section 7): `ROOM_DUNGEON_HEART`. The room definition keeps it twice, `+0x44E` (terrain id) and `+0x449`
(the category handle copied from that terrain at load when world `+0xA3C7` is 0,
[0x004EBD9A](functions/004EACC0.md)). The blow (`0x004DA2DB`) and the beat (`0x0043CE85`, through the room's `+0x44E`)
read these; the destruction (`0x004DA5B2`) and the collapse (`0x004DAC38`) read the category of the terrain at the
heart's position (slot `+0x68`, then `+0x195`).

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0xF1`, handle 0, heart centre, height `0x1000` | [0x004D9A60](functions/004D9A60.md) at `0x004D9E00`: opening turn 0, only when the heart's owner is the local player (`+0x264`) | STRONGLY INFERRED | `HeartIntroRules.RoomFrame` via `HeartSoundRules.RequestOpened`; `ThingSoundRequest.ViewerOnlyFor` and `WorldSoundDirector`'s viewer play it for that keeper only |
| `0x329`, room handle `+0x14`, at the heart object | [0x004DA200](functions/004DA200.md) at `0x004DA2DB`: a negative delta while the heart has an object, before the invulnerability test | STRONGLY INFERRED | `HeartSoundRules.Damage` (the fight, shot and boulder blows) |
| `0xDA`, room handle, at the heart object | [0x004DA560](functions/004DA560.md) at `0x004DA5B2`: the heart's death handler | STRONGLY INFERRED | `HeartSoundRules.Damage`, on the blow that kills |
| `0xF4`, the player's handle `+0x10A2`, heart tile, height 0 | [0x004DAA10](functions/004DAA10.md) at `0x004DAC38`: the death turn, after the rooms and tiles are handed over | STRONGLY INFERRED (the tile is the saved heart position) | `DefeatRules.OnDeathTurn` |
| `0xF4`, same handle, tile centre | [0x004DAE40](functions/004DAE40.md) at `0x004DB102`: the local keeper's collapse | STRONGLY INFERRED site | waits: that function is research only |
| `0x150`, handle 0, at the heart object, whatever the game turn | [0x0043CC30](functions/0043CC30.md) at `0x0043CE85`: a pending beat the one-second wall-clock gate lets through | STRONGLY INFERRED | `HeartBeatSound`, asked for by `DungeonScene` where it consumes the beat |
| `0x2F9`, the object's handle, terrain 14's category | unnamed code at `0x004A32F9`: an object's update that works wall tiles down through `Tile_ApplyWork` with their whole health | HYPOTHESIS: the defeat collapse's destroyer object | waits: not built |
| casino `0x316`, then `0x33E` | [0x004D9A60](functions/004D9A60.md) at `0x004DA00F`, `0x004DA114`: while world `+0x723A` bit 0 is set (the host-clock Saturday dance, [0x0050A850](functions/0050A850.md)); category terrain 0x18 (`ROOM_CASINO`) | STRONGLY INFERRED site | waits: the host clock is not an input of ours (Q571) |

`0x329` has three samples, `0x150` one and `0x2F9` four, all only in `room_dungeon_heart`; `0xDA` has one to three in
that category and in 27 creature categories (the creature death sound, Phase W6); `0xF4` and `0xF1` one each in it.
The blow and the destruction share the room's handle (`+0x14`), the collapse passes the player's (`+0x10A2`), so ours
voices it as a `Player` source. `0x316` and `0x33E` exist only in `room_casino`, so the plan's "heart" label was the casino's
category, played by the heart's room frame during the dance, never otherwise. Deviations: the heart object's
position is taken as the centre tile at the floor height of the room (`0x1000`), as ours keeps no heart object; ours has no
invulnerability flag, so the blow sound before that test has no extra case.

## 9. Workshop, casino, prison and portal

Read 2026-11-02 for plan Phase W3. Only the Casino's party pop is built (in the view); the rest waits for functions ours
does not have.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0x344`, handle 0, the global `EFFECTS` category (world `+0xA3BB`, set at `0x0050A284` in the world initialiser [0x005099A0](functions/005099A0.md)), at the effect's position (the drawn tile's corner, height `0x1000`) | [0x004D6270](functions/004D6270.md) at `0x004D6610`, only when the party's effect `0x140` (Fireworks, flags `0x4440`) was created (`TEST EAX,EAX` at `0x004D65E9`); creation is [0x00440420](functions/00440420.md), which fails off the map, on a solid tile, out of the camera (no flag `0x8000`) and on a tile neither explored nor in Sight | STRONGLY INFERRED | the room update asks for the effect (`CasinoJackpotRules.RoomUpdate`, on the tile of the room's chain the existing draw picks); the view plays the pop when its effect model reports the request created (`RoomConstructionParticles.EffectCreated`, `PartyPopSound`) |
| casino `0x316` | same function at `0x004D63D0`: for each creature of the owner in 153, the party music, the room's handle, volume by distance when the tile is explored; a null handle kicks the dancers | STRONGLY INFERRED site | `CasinoJackpotRules.RoomUpdate` ([sound-effects-continuous.md section 3](sound-effects-continuous.md#3-looping-thing-and-room-sounds)); the music is at the camera with a linear distance volume |
| casino `0x33E` | same function at `0x004D6727`: the party-off path while room `+0x3D` is set, the room's handle, only for an explored tile | STRONGLY INFERRED | the same, after a stop of the room's handle |
| workshop `0x2E9`, `0x2E8` | [0x004F4B20](functions/004F4B20.md) at `0x004F4C27`, `0x004F4DC7`, `0x004F4F03`: the three random timers, each with a world effect, the room's handle, at the effect's position | STRONGLY INFERRED sites | `WorkshopTimerRules.RequestSound`, straight after each effect (`0x2E9` after the steam and the lava spill, `0x2E8` after the smoke; the lava bubbling asks none), the room's handle as a `Room` voice by its first tile, in the category of the terrain at the effect's position, at that position; the spill's sits at the object's floor position where DK2 uses its mesh point (marked); the timers landed with ADR-0634 (hash 385) and the sounds are requests only |
| portal `0x34B`, nonpositional, handle 0, room definition `+0x449` | [0x004DCF50](functions/004DCF50.md) at `0x004DD024`: a creature offered by the multiplayer special arrival | STRONGLY INFERRED site | waits: that branch returns at once in single player and is not built |
| prison `0xE7`, `0xE8` | [0x004E35C0](functions/004E35C0.md) at `0x004E3669`, and the door objects' state arms at `0x004A3819`, `0x004A38E9`: the room's handle, at the door object | STRONGLY INFERRED sites | waits: the door objects (109, 116) and their toggles are not built |
| prison `0xE9`, `0xEA` | [0x004E3680](functions/004E3680.md) at `0x004E3729`, `0x004E377E`: lock and unlock, after message `0x3E`/`0x3F` | STRONGLY INFERRED sites | waits with them |
| prison `0x3F` | [0x004E2C70](functions/004E2C70.md) at `0x004E2DBD`, in the terrain category of the room's terrain | STRONGLY INFERRED site | silent: no `SFX.map` has `0x3F` |

The plan's "casino `0x316`, `0x33E`, `0x344`" are three different things: the first two are the party's
music and its end, the third a one-shot pop in `effects` (seven samples). `0x316` has one sample and `0x33E`
three, only in `room_casino`; the heart room's frame plays the same two groups in the casino category during
the Saturday dance (section 8). `0x34B` has one sample in `room_portal`; `0x2E8` one and `0x2E9` three in
`room_workshop`; `0xE7`-`0xEA` one each in `room_prison` and every door category.

The workshop's requests do not test an effect's creation result, unlike the party's. Known differences from DK2: a keeper's captured
builds replayed through `RoomLog` arrive as direct builds, so each tile sounds `0xF1` and there is no `0x2F8`; ours builds no thing-sound
event while the event sink is the null sink.

## 10. Imp jobs

Read 2026-11-02 for plan Phase W4. Every request is a slot `+0x20` call with the imp's own handle (`+0x2E`, stored back),
its definition's category (`+0x370`, `+0x6E3`) and its position (`+0x1A`), except the gold deposit. Counts are samples in the
actual category's `SFX.map`.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0xC4`, handle 0, the gold object's category (id 1, `OBJECT_GOLD`, one sample) | [0x00466AC0](functions/00466AC0.md) at `0x00466B95`: after the deposit and the purse spend, when the room had any space, even for an empty purse; the object is `0x0050DB20(1)` ([record](functions/0050DB20.md)), category `+0x10E` | STRONGLY INFERRED | `ImpTaskSystem.TryPutDown` |
| `0x10D`, the imp's handle | [0x0048B5F0](functions/0048B5F0.md) at `0x0048B687`: every update of a creature whose dragging word `+0x3C5` is nonzero and names a live thing (the imp jobs' body, book, crate or Special; `TakeHold` `0x0048B4A0`), after the steering step; the Portal Gem is held through `PickUp` (`+0x3C3`) and never asks | STRONGLY INFERRED site; the order is HYPOTHESIS | `CarriedThingSoundSystem`, last in the creature update |
| `0x10D` | [0x00467AA0](functions/00467AA0.md) at `0x00467B37`: state 41, the carried corpse, first thing in the handler, before the room tests | STRONGLY INFERRED | `ImpTaskSystem.UpdateCarryingBody`, corpse, once the walk has ended |
| `0x326` | [0x00466CB0](functions/00466CB0.md) at `0x00466DC7` (state 31, a spell book taken by the Library) and [0x00466DF0](functions/00466DF0.md) at `0x00466F0B` (state 32, a crate taken by the Workshop), then state 10; state 33 (a Special) has none | STRONGLY INFERRED | the book arm of `UpdateCarryingObject`, `StoreCrate` |
| `0x327`, `0x328` | [0x00467460](functions/00467460.md) at `0x00467575` (state 37, a door) and [0x004671B0](functions/004671B0.md) at `0x0046729F` (state 35, a trap): on the match, after the crate is consumed | STRONGLY INFERRED | `UpdateCarryingCrate` |
| `0x33D` | [0x00497960](functions/00497960.md) at `0x00497A9A`: the reinforce action's handler, once the wall is done and the clip has completed or wrapped (`+0xF1 & 6`), on each call | STRONGLY INFERRED site | waits: ours has no clip for the reinforce action, its gate has no mirror |
| `0x343`, `0x342` | [0x004651A0](functions/004651A0.md) at `0x004653B0` and [0x004653F0](functions/004653F0.md) at `0x0046547A`: the teleport's two halves | STRONGLY INFERRED sites | waits: the imp teleport is unbuilt (Q418) |

`creature_imp` alone has `0x10D` (three samples), so only an imp's carrying sounds; every other carrier is silent, and a Portal Gem
carrier makes no request (`CarriedThingSoundSystem` skips it by type: ours keeps `+0x3C3` and `+0x3C5` in one field). `0x326` has two, `0x327` one, `0x328` two, `0x33D`, `0x342` and `0x343` one each, all in `creature_imp` only.
A live handle retains an equal-priority request ([sound-playback.md](sound-playback.md)), so asking every update lets one
sample play to its end; ours asks every update too and the player keeps it.

The plan's `0x10C` question (`creature_imp` has three samples): no W4 site asks it. The one `0x10C` request site is
`Tile_ApplyWork`'s terrain request (section 7). The creature action table's sound word (`+25`, [creature-action-table.md
section 5](creature-action-table.md#5-the-25-word-is-a-sound-id-not-a-label)) is `0x10C` in record 8, the wall repair
(state 24, which begins it at `0x004667C0`), which `CreatureAction_Begin` asks: the imp's `0x10C` is the repair action's sound (record 8
read from the table filler `0x004950C0`, store `0x00495378`: STRONGLY INFERRED), not reinforcing; built once per work state, see
[sound-effects-continuous.md section 2](sound-effects-continuous.md#2-the-action-table). Record 7, reinforce, has no word.

## 11. Arrival, rest and reactions

Read 2026-11-02 for plan Phase W4. Every request passes the creature's own handle (`+0x2E`, stored back), its category
(`+0x370`, `+0x6E3`) and its position (`+0x1A`) unless a row says otherwise.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0xE0` | [0x00459C20](functions/00459C20.md) at `0x00459C73`: state 193 once its action has ended, before the step off the hole, **only when the creature's owner is the local player** (game slot `+0x264`) | STRONGLY INFERRED | `CreatureJobSystem`, marked for its owner (`ViewerOnlyFor`) |
| `0xE0`, or `0xFB` | [0x004872A0](functions/004872A0.md) at `0x00487BC4`, the constructor: the `0xE0` arm is only for the hero player's creatures (`[0x006F254C]`), turn 2 or later, world `+0x7239` bit 6 clear, on an explored tile; handle 0. A Horned Reaper (definition `+0x686` bit 14) jumps past those tests to `0xFB` at `0x00487BC4`, so it asks for every creation, and is silent (no creature category has `0xFB`) | STRONGLY INFERRED | waits: `WorldState.SpawnCreature` has no event path, and "explored" is the viewer's bit |
| `0xF6` | [0x0046EAB0](functions/0046EAB0.md) at `0x0046EEA9`: state 76 after the floor-contact test, not on a Temple floor slot | STRONGLY INFERRED site | waits: ours has no fall (ADR-0209) and no contact moment |
| `0xCE` | [0x0046FA60](functions/0046FA60.md) at `0x0046FC7A`: state 83 when the art-12 gate holds and no action runs, with action 24; gated by an unread bed call (`0x0046FAF9`) | STRONGLY INFERRED site | waits: no action 24 clock, gate unread |
| `0x111`, `0xCD` | [0x00463760](functions/00463760.md) at `0x00463859` (the chicken's own handle and category, at the bird) and `0x0046398D` (the eater's): state 93 | STRONGLY INFERRED | `CreatureEatSystem.Swallow` |
| `0xC7` | [0x00481520](functions/00481520.md) at `0x00481549`: state 210's first act; the record had called it a display request | STRONGLY INFERRED | `TremorRules.RunRecovery` |
| `0xC7` | [0x00481020](functions/00481020.md) at `0x0048104C`: state 209's first act | STRONGLY INFERRED site | waits: ours has no state 209 |
| `0xCB` | [0x004816F0](functions/004816F0.md) at `0x00481719`: state 213 every call | STRONGLY INFERRED site | nothing: state 213 is unreachable (room-return-states.md section 6) |
| `0xDC` | [0x004611D0](functions/004611D0.md) at `0x004617E6` (withdraw) and [0x004752B0](functions/004752B0.md) at `0x004754AC` (the Scout's alarm), after the route and run speed | STRONGLY INFERRED | `FearRules.Withdraw`, `FearRules.RaiseAlarm` |
| `0x2FA` | [0x00475660](functions/00475660.md) at `0x00475934`: state 251 after its responders are sent | STRONGLY INFERRED | `FearRules.RespondToThreat` |
| `0xDD` | [0x00462D00](functions/00462D00.md) at `0x004630FF`: state 98's leg that cannot be routed, then state 99 | STRONGLY INFERRED | `GuardStep.Patrol` |
| `0xDE` | [0x00482F80](functions/00482F80.md) at `0x00483021`: state 85 (sulking) with action 25, on its first call and whenever its countdown byte `+0xA7` is a multiple of four game seconds | STRONGLY INFERRED site | waits: ours has no state 85 handler |

Samples: `0xE0` one in each of 26 creature categories, the imp's not among them; `0xF6` in 26, the imp's (three) included (only the firefly and the Jack in the Box lack it);
`0xCE` in 22 and `0xCB` and `0xDE` in 24, none of them the imp's; `0xC7`, `0xDC` and `0x2FA` in the imp's; `0xDD` only in
`creature_knight` and `creature_stone_knight`; `0x111` has eleven in `object_chicken`. Deviation: the chicken's request takes the
floor height, as any object's does (`ThingSoundRules`).

## 12. Rooms' creature states

Read 2026-11-02 for plan Phase W4. Handle, category and position are the creature's own, as in section 11, unless a row says otherwise.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0xD3` | [0x004855B0](functions/004855B0.md) at `0x004857CD`: state 111's request, the melee arm only (the dummy, art 5, action 30), after SetState(112); both actions' begins (30, 31) also ask `0xD3` ([sound-effects-continuous.md section 2](sound-effects-continuous.md#2-the-action-table)) | STRONGLY INFERRED | `TrainingStep.AtMachine` |
| `0xE2` | [0x00472930](functions/00472930.md) at `0x004729FB`: state 101 on a Temple pool cell, after the offering is registered, before the creature goes | STRONGLY INFERRED | `TempleStep.Sacrifice` (the Hand's drop into the pool calls it too) |
| `0xD4` | [0x00472D30](functions/00472D30.md) at `0x00472E70`: state 104 once action 27 begins (art 23 present) | STRONGLY INFERRED | `TempleStep.Face` |
| `0xE1` | [0x00456D60](functions/00456D60.md) at `0x00456DF0`: state 147 while its action runs, at ages that are multiples of eight game seconds | STRONGLY INFERRED | `CasinoStep.Wait` |
| `0xD5` | [0x00456980](functions/00456980.md) at `0x00456B37`: state 149 while its action runs, at multiples of eight seconds; at `0x00456AE9` every fourth second when its table is a Roulette Table with a Roulette Wheel (object 30) on its tile | STRONGLY INFERRED; casino.md had 149 silent | `CasinoStep.Wait` for the eight-second request; the wheel's waits (no wheel objects) |
| `0xCA` | [0x00457560](functions/00457560.md) at `0x00457677`: state 157 once the winnings are in the purse | STRONGLY INFERRED | `CasinoJackpotRules.Collect` |
| `0xC3`, `0xCA` | [0x00472740](functions/00472740.md) at `0x00472803` (the gold object's category, the creature's own handle) and `0x0047287A` (the creature's category, the handle the first request returned): state 189 once the wage is off the floor | STRONGLY INFERRED | `PaydaySystem.Take` |

Samples: `0xD3` in 25 creature categories, `0xE2` in 26 (the imp's included), `0xD4` in 22, `0xE1`, `0xD5` and `0xCA` in 24
(none of these but `0xE2` in the imp's), `0xC3` one in `object_gold`. Deviations: the Hand's drop of a creature into a Temple's pool is
offered at the drop, where DK2 offers it in state 101 after the stun, so its `0xE2` is early; the roulette's four-second `0xD5` is not
built. A live handle retains an equal-priority request (section 10), and the wage's two requests share the creature's handle as
DK2's do, so whether `0xCA` is heard depends on the priority of `0xC3`'s group (untested).

## 13. Level-up, the Reaper and the silent rows

Read 2026-11-02 for plan Phase W4. Handle, category and position are the creature's own unless a row says otherwise.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| `0x349` | [0x0048B120](functions/0048B120.md) at `0x0048B2DB`: every successful level increment, after effect `0x153` | STRONGLY INFERRED | `CreatureLevelRules.LevelUp`, through `StateHookContext.RequestSound` (new, as the effect is requested), so scripted and party level-ups sound too |
| `0x32D` | [0x00474740](functions/00474740.md) at `0x004747BA`: the summoned Reaper's state 235, in the update that enters 236 | STRONGLY INFERRED | `ReaperRules.RunLifecycle` |
| `0x32D`, `0x32E` | [0x004741D0](functions/004741D0.md) at `0x00474295` (finale state 230 age 1, handle 0, after effect 126) and [0x004744F0](functions/004744F0.md) at `0x00474644` (state 233 age 1, after the gem is destroyed) | STRONGLY INFERRED | `FinaleRules.Appear`, `FinaleRules.PickUpGem` |
| `0x323`, `0x111` | [0x00474AF0](functions/00474AF0.md) at `0x00474B74` (at the chicken's position, the Reaper's handle) and `0x00474BA3` (the chicken's handle and category): state 240 with a chicken on its cell | STRONGLY INFERRED sites | waits: state 240 is unbuilt |
| `0x340` | [0x0048E160](functions/0048E160.md) at `0x0048E4F1`: each tick the burning counter is nonzero; DK2 stops the sound (slot `+0x38`) as the fire ends | STRONGLY INFERRED | `StatusEffectRules.UpdateBurning`: the request each tick and, as the fire ends, `ThingSoundRules.Stop` |
| `0x340` | [0x00487C70](functions/00487C70.md) at `0x00488052`: a burning creature that dies and leaves a body | STRONGLY INFERRED site | waits: ours keeps no burning state on a body |
| `0x34C` | [0x004B2CE0](functions/004B2CE0.md) at `0x004B315E`: a dead creature of type `0x2E` (the Jack in the Box) once its blast shot is made | STRONGLY INFERRED | `JackInTheBoxRules.Explode` |
| `0x317` | [0x00488930](functions/00488930.md) at `0x00489F1C`: a possessed creature past three earlier gates (definition `+0x686` bit 18 clear, game turn at or past `+0x3D7`, `+0x526` strictly above the level-scaled definition `+0x696` share) whose `ThreatThing` (`+0x533`) is a live trap with definition byte `+0xBB` equal to `0x19`, then game action `0x15` | STRONGLY INFERRED site | waits: `ThreatThing` is unbuilt |
| `0x312`, `0xA7` | [0x0048FA70](functions/0048FA70.md) at `0x0048FBA2`, `0x0048FC42`: the Reaper-proximity scan | STRONGLY INFERRED sites | nothing: the scan never runs (ADR-0402), so no request is made; `0x312` has eight samples in `creature_imp`, `0xA7` none |

Silent (no `SFX.map` has the group, so DK2 plays nothing and ours builds nothing): `0xE3` in [0x0048B770](functions/0048B770.md)
(`Creature_Collapse`, its first act), `0x12C` (event 300) in the Reaper's states 231, 237 and 242 ([0x004743C0](functions/004743C0.md),
[0x00474850](functions/00474850.md), [0x00474CC0](functions/00474CC0.md)), `0x137` in [0x00488930](functions/00488930.md) (nonpositional), `0x51` in
[0x004D6AA0](functions/004D6AA0.md) (`Casino_Jackpot`).

Samples: `0x349` one in each of 27 creature categories (the imp's included); `0x32D`, `0x32E` (one each) and `0x323` (three) only in
`creature_horned_reaper`; `0x34C` one only in `creature_jackinbox`; `0x317` one only in `creature_goblin`; `0x340` three in each of 25
categories, the imp's not among them. The finale's `0x32D` passes handle 0 (it neither uses nor replaces the Reaper's voice).

## 14. Objects, doors, traps and the boulder

Phase W5 is in [sound-effects-objects.md](sound-effects-objects.md), which keeps this document under its length budget:
[objects and specials](sound-effects-objects.md#1-objects-and-specials), [doors and traps](sound-effects-objects.md#2-doors-and-traps)
and [the boulder](sound-effects-objects.md#3-the-boulder).

## 15. Spells, shots and combat

Phase W6 is in [sound-effects-combat.md](sound-effects-combat.md): [keeper spells](sound-effects-combat.md#1-keeper-spells-and-the-categories),
[the Tremor](sound-effects-combat.md#2-the-tremor), [shots](sound-effects-combat.md#3-shots), [a creature's spell](sound-effects-combat.md#4-a-creatures-spell),
[the swing, death and corpses](sound-effects-combat.md#5-the-swing-death-and-corpses) and [the torture devices](sound-effects-combat.md#6-the-torture-devices).

## 16. Footsteps, the action table, loops and first person

Phase W7 is in [sound-effects-continuous.md](sound-effects-continuous.md): [footsteps](sound-effects-continuous.md#1-footsteps), [the action table](sound-effects-continuous.md#2-the-action-table) and [loops and stops](sound-effects-continuous.md#3-looping-thing-and-room-sounds).
