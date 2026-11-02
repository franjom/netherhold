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

## 7. Terrain, digging, claiming and rooms

Read 2026-11-02 for plan Phase W3. Same conventions as section 6: every request is one slot
`+0x20` call (handle, category, event, position); a row is silent when no shipped `SFX.map` has the
group with samples.

**The terrain category (CONFIRMED).** Terrain definition `+0x195` is the manager's category handle
for the name in TERRAIN file bytes `0x1DF`..`0x1FE` (the loader [0x004F66E0](functions/004F66E0.md)
copies them from its intermediate record, shifted `+0xB8`, and resolves them through slot `+0x1C`;
shipped values read from the file: `TERRAIN_ROCK`, `TERRAIN_WALL`, ..., and `ROOM_*` for every room
terrain, so a room tile's category is its room terrain's. Terrains 27, 28, 29, 30, 38 and 39 have none).
Ours: `TerrainDef.SoundCategory`. Two more handles follow, `+0x199` (Temple only: `ROOM_TEMPLE_WATERLESS`
by name) and `+0x19D` (the second name, file `0x204` by the same shift, unread); the ambience grid
([0x0044E2A0](functions/0044E2A0.md)) reads all three (W7.4).

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| terrain `0xF4`, handle 0, tile centre, height 0 | [0x00452F60](functions/00452F60.md) at `0x0045327C`: health reached 0 and the destroyed type (`+0x1C6`) differs from the tile's terrain, after the tile is set or sold, before the destroy effect; category of the terrain the tile had | CONFIRMED | `TileWorkSystem.Execute`, `TremorRules.DamageWall` |
| terrain `0x10C`, same shape | same function at `0x00453359`: health reached the maximum and the max-health type (`+0x1C5`) differs, before the conversion and its effect | CONFIRMED; the plan's "dig hit" is wrong, it is the upward conversion (reinforce, claim, corner fill) | `TileWorkSystem.RequestCompletionEffect` |
| imp `0x10A` / `0x10B`, the imp's handle, at the imp | [0x004974C0](functions/004974C0.md) at `0x00497635`: after the strike that left the tile not claimable (only then); `0x10A` when the tile's room word (`+2` low 12 bits) is 0, else `0x10B`; the handle is stored back | CONFIRMED | `TileWorkSystem.RequestClaimSound` |

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
| room `0xF1`, tile centre, height 0 | [0x004C5C30](functions/004C5C30.md) at `0x004C5D87`: paid tile, game turn differs from the turn argument (the drag wave passes its queue turn, so its first diagonal is silent; the keeper wrapper passes 0; loader and trigger builds are unpaid) | CONFIRMED | `RoomBuildSystem.PlacePaidTile` |
| room `0xF5`, tile centre, height `0x1000` | [0x004C5DB0](functions/004C5DB0.md) at `0x004C5EF8`: a sale with refund, after the refund is paid in | CONFIRMED | `RoomSellSystem` |
| room `0xF4`, same place | same function, `0x004C5F29`: the refundless sale (a worked-down room tile, trigger actions) | CONFIRMED site; no group outside `room_dungeon_heart` | nothing |
| room `0x2F8`, lowest corner of the rectangle, height `0x1000` | [0x00442960](functions/00442960.md) at `0x00442D57`: command-table handler for the human rectangle build, after its pre-check passes | CONFIRMED site; issue side STRONGLY INFERRED | `RoomBuildSystem.RequestAcceptedSound` |
| room `0xF7`, nonpositional | [0x004E5650](functions/004E5650.md) at `0x004E570C`: a room changes owner, the room's handle `+0x14`, definition `+0x449` | CONFIRMED site | silent: no `SFX.map` has `0xF7` |
| room `0xF8` | [0x004E7FE0](functions/004E7FE0.md) at `0x004E81BE` | CONFIRMED site | silent: no `SFX.map` has `0xF8` |

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
| `0xF1`, handle 0, heart centre, height `0x1000` | [0x004D9A60](functions/004D9A60.md) at `0x004D9E00`: opening turn 0, only when the heart's owner is the local player (`+0x264`) | CONFIRMED | `HeartIntroRules.RoomFrame` via `HeartSoundRules.RequestOpened`; `ThingSoundRequest.ViewerOnlyFor` and `WorldSoundDirector`'s viewer play it for that keeper only |
| `0x329`, room handle `+0x14`, at the heart object | [0x004DA200](functions/004DA200.md) at `0x004DA2DB`: a negative delta while the heart has an object, before the invulnerability test | CONFIRMED | `HeartSoundRules.Damage` (the fight, shot and boulder blows) |
| `0xDA`, room handle, at the heart object | [0x004DA560](functions/004DA560.md) at `0x004DA5B2`: the heart's death handler | CONFIRMED | `HeartSoundRules.Damage`, on the blow that kills |
| `0xF4`, the player's handle `+0x10A2`, heart tile, height 0 | [0x004DAA10](functions/004DAA10.md) at `0x004DAC38`: the death turn, after the rooms and tiles are handed over | STRONGLY INFERRED (the tile is the saved heart position) | `DefeatRules.OnDeathTurn` |
| `0xF4`, same handle, tile centre | [0x004DAE40](functions/004DAE40.md) at `0x004DB102`: the local keeper's collapse | CONFIRMED site | waits: that function is research only |
| `0x150`, handle 0, at the heart object, whatever the game turn | [0x0043CC30](functions/0043CC30.md) at `0x0043CE85`: a pending beat the one-second wall-clock gate lets through | CONFIRMED | `HeartBeatSound`, asked for by `DungeonScene` where it consumes the beat |
| `0x2F9`, the object's handle, terrain 14's category | unnamed code at `0x004A32F9`: an object's update that works wall tiles down through `Tile_ApplyWork` with their whole health | HYPOTHESIS: the defeat collapse's destroyer object | waits: not built |
| casino `0x316`, then `0x33E` | [0x004D9A60](functions/004D9A60.md) at `0x004DA00F`, `0x004DA114`: while world `+0x723A` bit 0 is set (the host-clock Saturday dance, [0x0050A850](functions/0050A850.md)); category terrain 0x18 (`ROOM_CASINO`) | CONFIRMED site | waits: the host clock is not an input of ours (Q571) |

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
| `0x344`, handle 0, the global `EFFECTS` category (world `+0xA3BB`, set at `0x0050A284` in the world initialiser [0x005099A0](functions/005099A0.md)), at the effect's position (the drawn tile's corner, height `0x1000`) | [0x004D6270](functions/004D6270.md) at `0x004D6610`, only when the party's effect `0x140` (Fireworks, flags `0x4440`) was created (`TEST EAX,EAX` at `0x004D65E9`); creation is [0x00440420](functions/00440420.md), which fails off the map, on a solid tile, out of the camera (no flag `0x8000`) and on a tile neither explored nor in Sight | CONFIRMED | the room update asks for the effect (`CasinoJackpotRules.RoomUpdate`, on the tile of the room's chain the existing draw picks); the view plays the pop when its effect model reports the request created (`RoomConstructionParticles.EffectCreated`, `PartyPopSound`) |
| casino `0x316` | same function at `0x004D63D0`: for each creature of the owner in 153, the party music, the room's handle, volume by distance when the tile is explored; a null handle kicks the dancers | CONFIRMED site | waits: a looped music handle and the explored-tile volume (W7.3) |
| casino `0x33E` | same function at `0x004D6727`: the party-off path while room `+0x3D` is set, the room's handle, only for an explored tile | CONFIRMED site | waits with `0x316` |
| workshop `0x2E9`, `0x2E8` | [0x004F4B20](functions/004F4B20.md) at `0x004F4C27`, `0x004F4DC7`, `0x004F4F03`: the three random timers, each with a world effect, the room's handle, at the effect's position | CONFIRMED sites | waits: the timers draw the simulation stream (`Random_Next` on the world seed), which ours does not; building them moves the state hash, so it needs a hash version |
| portal `0x34B`, nonpositional, handle 0, room definition `+0x449` | [0x004DCF50](functions/004DCF50.md) at `0x004DD024`: a creature offered by the multiplayer special arrival | CONFIRMED site | waits: that branch returns at once in single player and is not built |
| prison `0xE7`, `0xE8` | [0x004E35C0](functions/004E35C0.md) at `0x004E3669`, and the door objects' state arms at `0x004A3819`, `0x004A38E9`: the room's handle, at the door object | CONFIRMED sites | waits: the door objects (109, 116) and their toggles are not built |
| prison `0xE9`, `0xEA` | [0x004E3680](functions/004E3680.md) at `0x004E3729`, `0x004E377E`: lock and unlock, after message `0x3E`/`0x3F` | CONFIRMED sites | waits with them |
| prison `0x3F` | [0x004E2C70](functions/004E2C70.md) at `0x004E2DBD`, in the terrain category of the room's terrain | CONFIRMED site | silent: no `SFX.map` has `0x3F` |

The plan's "casino `0x316`, `0x33E`, `0x344`" are three different things: the first two are the party's
music and its end, the third a one-shot pop in `effects` (seven samples). `0x316` has one sample and `0x33E`
three, only in `room_casino`; the heart room's frame plays the same two groups in the casino category during
the Saturday dance (section 8). `0x34B` has one sample in `room_portal`; `0x2E8` one and `0x2E9` three in
`room_workshop`; `0xE7`-`0xEA` one each in `room_prison` and every door category.

The workshop's requests do not test an effect's creation result, unlike the party's. Known differences from DK2: a keeper's captured
builds replayed through `RoomLog` arrive as direct builds, so each tile sounds `0xF1` and there is no `0x2F8`; ours builds no thing-sound
event while the event sink is the null sink.
