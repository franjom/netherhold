---
systems: [audio]
---
# Sound effects: footsteps, the action table, loops and first person

Sources: DKII.EXE v1.70, read through Ghidra on 2026-11-03; shipped `Data/Sound/Sfx/Global/*SFX.map`, `Data/editor/Creatures.kwd`.
Plan Phase W7 of [world-sounds](../plans/2026-10-31-world-sounds.md). Conventions as in
[sound-effects.md section 6](sound-effects.md#6-the-hand-and-its-cursor): a request is one slot `+0x20` call of the sound manager
(handle, category, event, position); a row is silent when no `SFX.map` holds the group with samples. Section 1 is the footsteps,
2 the action table, 3 the looping sounds, 4 the terrain ambience grid and first person.

## 1. Footsteps

Presentation only: no simulation state is read or written. Native evidence is in [sound-effects.md section 1](sound-effects.md#1-creature-footstep-events)
([render pass](functions/0043CC30.md), [crossing helper](functions/004B4630.md), [event wrapper](functions/0048FF10.md)).

**The marks (STRONGLY INFERRED).** The 48 pairs of unsigned words at CREATURES file `+0x1323` are definition `+0x7A3 + 4 x slot` (first mark) and `+0x7A5 + 4 x slot`
(second). The install of a type-6 clip ([chooser](functions/0048BE50.md), at `0x0048C5D0`) indexes the pair by the slot **asked for**, not the
slot the fallback resolver ([0x00494860](functions/00494860.md)) returns, and divides each word, shifted left 12, by the **resolved** clip's frame count, into
animation object `+0x7F` / `+0x83`; a clip that is not type 6 clears both. Ours: `CreatureDef.FootstepMarks` (`KwdCreatures` reads and writes all 48 pairs;
`FootstepMarkPair.Normalize` is the division). Slots are the file's art blocks (block `0x20 + 84 x slot`); slot 1 is the walk, 2 the run, 4 the recoil,
9 the angry clip, 38 the back-off (`ArtSourceMap`'s resource index is the slot minus one).

Shipped data: authored pairs for slot 1 (walk) and 2 (run) exist for the Imp (2, 8 for both), Horny (slot 1 only), the Giant, the Knight, the Black Knight, the Stone Knight, King Reginald, the three Princes, the Lord of the Land and the Unique Black Knight (the humanoids' 16, 27 and 13, 25 or near); no other definition has authored ones (the uniques hold `0xCDCD`), so a Goblin or a Warlock never steps on land. Slots 4 (the Imp's 7, a clip of 11 frames), 9 (the Giant's 15) and 38 (the knights', a type-1 resource, which clears marks) carry pairs for clips this view does not play. Slots 46 and 47 of the 48 hold the creature's pose name as ASCII (`GOB-Pose` for the Goblin, a shorter string for the Imp) (the copy of 48 pairs runs over a string, so no art slot reaches them), and the unique creatures' unauthored slots hold `0xCDCD`, whose normalized mark lies far past one clip and never crosses. Slot 2 of the Imp has pair (2, 8) but no run clip
(resource type 0): the run is requested, the walk resolved, and the marks are slot 2's over the walk's 12 frames.

**The crossing (STRONGLY INFERRED).** [0x004B4630](functions/004B4630.md) reads the animation object's previous-turn phase `+0x90` and current phase `+0x94` (12.12, wrapped at 4096),
takes the step between them (4096 added when negative) and scales it by two frame fractions in 1/256ths (each product shifted right 8 after adding 255 when negative, a
division toward zero) to an interval `[start, end]` from the previous phase. Either mark strictly inside, and the result is 1; otherwise, while `end` exceeds 4096, both ends drop
4096 and a mark inside the lowered interval gives 2; no mark gives 0. A zero first mark with a zero second is none; a nonzero first mark with a zero second takes the first for the second.
Ours: `AnimationMarkCrossing.Test`, with `RenderAnimationClock.PreviousPhase` and `Phase`.

**The request (the arm STRONGLY INFERRED, the fractions HYPOTHESIS).** [0x0043CC30](functions/0043CC30.md)'s footstep arm (`0x0043CD52`) runs for a thing whose `[thing+0x12]` is 0 (a creature), takes the animation object at creature `+0x44` (marks at creature `+0xC3`/`+0xC7`) and calls the helper at `0x0043CD84` with two fractions: this frame's (`[view+0x262D]`) and an earlier one (`[view+0x2625] + ([view+0x2621] - [view+0x2629]) x 250`), whose writers are unfound; the likely reading is milliseconds into a 250 ms turn, still divided by 256, so ours moves the last frame's fraction back by 256 per turn. It requests `0x314` for 1 and `0x315` for 2 through [0x0048FF10](functions/0048FF10.md), which substitutes `0x337` / `0x338` while creature `+0x3EA` bit 21
is set. The request is the creature's handle `+0x2E`, category `+0x6E3` and position `+0x1A`. Ours: `CreatureFootsteps.For`, asked by `DungeonScene` after each creature clip draws.

**The wading flag (STRONGLY INFERRED).** [Creature_Update](functions/00488930.md) clears bit 21 each update, then sets it at `0x00488CB2` when the creature's `+0x22` is at most the signed word at `+0x26` and the terrain under it
has flag `0x80` (water, the bit the water speed tests). `+0x22` and `+0x26` are read as the creature's height and the floor's; the comparison is unread beyond that. Ours: not flying and the tile's terrain is water
(`CreatureView.Wading`), a bridge being its own non-water terrain.

**Samples.** `0x314`/`0x315` have one or two samples in `creature_black_knight`, `_giant`, `_horned_reaper`, `_imp`, `_king`, `_knight` and `_skeleton` only; `0x337`/`0x338` one or two in every creature category but the Firefly's and the Jack in the Box's, so a wading creature of any other kind sounds, and a walking Stone Knight or Horny, which have marks but no `0x314`, does not.

**Built:** `CreatureFootsteps`, the marks, `AnimationMarkCrossing`, `AnimationMarkWatcher` (the last drawn frame's fraction) and the Unity wiring
(`ClassicCreatureAnimation.FootstepCrossing`, `DungeonScene.CreatureFootstepRequested`, `DungeonRunner`); the request goes through `ClassicSoundPlayer.Play`, so the throttle and the
voice retention judge it. Marked deviations: the interval's earlier fraction is moved back by 256 per turn (benign if the native is milliseconds into a 250 ms turn); the clip's marks are tested for the walk and run clips only (the view plays no
recoil, angry or back-off clip; slot 4's, 9's and 38's wait for a clip that plays them); only creatures that are drawn and inside the camera's frustum sound, where DK2's test failures of vtable `+0x18`/`+0x34` and the not-in-sight case (`0x0043CD10`, `0x0043CD1B`, `0x0043CD33`) all jump to the footstep arm, so it sounds creatures it does not draw (whether they reach `0x0043CC30` at all, gate `0x0043CC71`, is unread); DK2 tests
each drawn frame, ours each `Draw` of the clip. Not imported: OpenKeeper's three footsteps per 200 ms and fixed `mark / 30` ([sound-effects.md section 4](sound-effects.md#4-slow-frame-throttle-and-unverified-playback-budgets)).

## 2. The action table

**The words (STRONGLY INFERRED).** Record `+25` of the 81 records at `0x006E4198` is a sound word in the creature's category ([creature-action-table.md section 5](creature-action-table.md#5-the-25-word-is-a-sound-id-not-a-label)). The filler [0x004950C0](functions/004950C0.md) stores
one word in each record; reading its stores with the registers tracked gives 44 nonzero words, and `CreatureActionTable.SoundWord` is that table. Action: word. 1: `0x334`, 2: `0x109`, 5 and 6: `0xE5`,
8: `0x10C`, 9: `0x109`, 11, 17, 40 and 80: `0xD7`, 14, 15 and 16: `0xD8`, 19: `0xC5`, 20 and 21: `0xDB`, 23 and 45 to 48: `0xE6`, 24: `0xCE`, 25 and 67: `0xDE`, 26: `0xCD`, 27 and 50: `0xD4`,
28: `0xE0`, 29, 51 and 65: `0xD1`, 30 and 31: `0xD3`, 32: `0xD2`, 33, 35, 66 and 69: `0xCA`, 34: `0xCC`, 39: `0xE1`, 41 and 42: `0xD0`, 58: `0x12D`, 63: `0xCF`. The earlier list in the action-table document
(`0xC5` to `0xE1`, `0x109`, `0x10C`, `0x12D`, `0x334`) missed `0xE5` and `0xE6`. Three readers use the word: `CreatureAction_Begin` ([0x00496700](functions/00496700.md) at `0x004968BC`), the hover
([0x0040C290](functions/0040C290.md)), and `0x0045A200`, whose use is unread.

**The begin's gate (STRONGLY INFERRED).** `0x004968BC` asks the word after the clip is kept or installed, not when "the animation flag is set" as the first reading had it: it tests the animation's previous-turn and current phase
and asks unless the previous phase is below the current one and the current is nonzero. So a freshly installed clip (phases 0) and one that has just wrapped sound, and one kept part way is silent. The request is the creature's
handle `+0x2E` (stored back), category `+0x6E3`, position `+0x1A`.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| dig `0x334` (action 1), mine `0x109` (action 2), `creature_imp` (3 samples each) | the imp's work states, which begin action 1 at `0x00465679` and action 2 at `0x00465A5D`: when the action begins and as each cycle of its clip wraps | STRONGLY INFERRED word; the cycle HYPOTHESIS | `ImpTaskSystem.StepWorkAnimation`: at the turn after the work state is entered, then each time the clip wrapped on the last turn and the strike is done (the first cycle takes four turns, the rest three) |
| wall repair `0x10C` (action 8), `creature_imp` (3) | state 24 [0x00466710](functions/00466710.md) at `0x004667C0`: its first call with no action running, after the task and the tile test | STRONGLY INFERRED word and site | `ImpTaskSystem.UpdateWorking`, once per work state (the clip's length, so DK2's one per cycle, is unread) |
| melee `0xD7` (17), cast `0xD8` (16, and 15 free-aimed) | the swing's and cast's begins (`spFighting` `0x00460103`, `0x0045FB96`; possession `0x00514DA4`, `0x0051529F`) | STRONGLY INFERRED | `FightRules.StartSwing`, `StartCast`, `BeginPossessedAction` |
| training `0xD3` (30, 31) | state 111's begins (`0x00485692`, `0x00485794`, `0x0048571E`), before the state's own `0x004857CD` | STRONGLY INFERRED | `TrainingStep.Begin`: the spell arm now sounds too |
| forge `0x112` | [0x00498D20](functions/00498D20.md), action 32's handler, run at the head of each update while the action runs: past the start stamp less one plus `def+0x5CA`, for a creature in Workshop category 5, the Anvil's own handle and category, at its position | STRONGLY INFERRED site | `WorkshopStep` (`AskAnvilSound`), every update of state 65 past the delay |
| creature hover `0xCA`/`0xCB`/`0xCC` or the action's word | [0x0040C290](functions/0040C290.md) as in [sound-effects.md section 6](sound-effects.md#6-the-hand-and-its-cursor) | STRONGLY INFERRED | `CreatureHoverSound`, presentation; the action from `CreatureCurrentAction`'s stand-ins |
| sleeping `0xCE` (24), reinforce `0x33D` | [0x0046FA60](functions/0046FA60.md), [0x00497960](functions/00497960.md) | STRONGLY INFERRED sites | waiting: the sleep handler's bed call `0x0046FAF9` is unread, and ours has no clip for the reinforce action's `+0xF1 & 6` gate |

Samples: `0x334`, `0x109`, `0x10C` three each in `creature_imp` only; `0xD7` five in `creature_salamander` alone; `0xD8` in 14 creature categories; `0xD3` in 25; `0xD2` and `0x12D` in none; `0x112` three in `OBJECT_FORGE`
(the Anvil's category in Objects.kwd). So the imp's dig and mine, a caster's cry and a trainee's grunt are audible; the begin words of actions 32 (`0xD2`) and 58 are silent.

The remaining records with a word (actions 5, 6, 9, 11, 14, 19 to 21, 23, 25 to 29, 33 to 35, 39 to 42, 45 to 51, 58, 63, 65 to 67, 69 and 80) wait for the mirrors of the states that begin them: each is a separate question (which
state asks, whether ours has a place that is its begin), not read here beyond the table. Several are already asked by a state site of W4 (`0xD4`, `0xE1`, `0xCD`, `0xCA`): a begin and a state request share the creature's handle, so
the live voice retains the second.

Marked deviations: the begin's phase test is stood in for by "freshly installed or wrapped last turn"; ours tells the running action only for the ones above, so the hover sounds the mood where DK2 would sound the word of an
action ours does not name; the dig and mine sound at the turn after the work state is entered where DK2's first call is, by the same reading as the dig's own cadence; the hover's tool and refresh gates are not mirrored.
`CreatureAction_Begin` returns 0 before the sound when the running action's record byte `+0x1C` is zero (`0x00496952`); that byte is 1 in all 81 records, so it never refuses and the check is not mirrored.
Imp action 1's possessed (`0x00514FC9`), training (`0x00485E7D`) and other begins are not asked.

## 3. Looping thing and room sounds

**The manager's slots (STRONGLY INFERRED, read 2026-11-03).** Vtable `0x0066F7FC`: `+0x20` positional request, `+0x24` nonpositional, `+0x30` replay with a position (`0x00567A70`), `+0x34` set a property of a handle (`0x00567A10`),
`+0x38` run a command object on the service (`0x00567A40`), `+0x54` a global volume set (the party restores it). The handle is an index into the service's instance table.

- **Replay `+0x30` is a position update, not a restart.** `0x00567A70` converts the position relative to the listener and calls `0x0060D950`: it finds the live instance by handle (`0x0060DB80`) and calls the instance's
  vtable `+0x20` (`0x0060C810`), which stores the position at `+0x10..+0x18` and moves the backend voice (an instance with flag `0x40` only tests its time-out: now past start plus `+0x1C` sets "finished", flag `0x10`). It returns
  the handle, or 0 when no live instance has it, and every caller stores the return into the thing's handle: `thing+0x2E = replay(thing+0x2E, position)`, so a handle whose sound has ended is dropped.
- **Stop `+0x38`.** The sites build a command object (`{vtable 0x0066C77C, handle}`), and its method `0x00608440` calls `0x0060D8C0`: the instance found by handle gets its vtable `+0x18` (`0x0060D0F0`): flag `0x800` set, the
  backend voice stopped when flag 1 is set, flag `0x10` (finished) set. The plan's "stop in the `*_Cleanup` functions" is right for the sites below; the other `+0x38` sites in the save and load code use other command objects (`0x0066C6BC`).
- **A group that loops (STRONGLY INFERRED).** Group flag bit `0x08` makes the instance flags 6 ([0x0060C960](functions/0060C960.md)); instance flag 4 is the backend repeat count 9999 ([0x0060CDB0](functions/0060CDB0.md)). Bit `0x04` adds instance flag 2 only. Of every shipped group
  (1354 across the `SFX.map` files), two have bit `0x08`: `0xF2` in `spell_fireball` and `spell_firebomb`, the shot's second handle (the trail). The others that run long (flags `0x04`/`0x02`: burning `0x340`, the flies `0x152`, the boulder's
  roll `0x14F`, the Tremor's `0xFB`, the party's `0x316`) end with their sample and are asked for again by their thing, or replayed at its position while the instance lives. `IsLooping` is ours.

| Site | What it does | Verdict | Built as |
|---|---|---|---|
| replay: [Creature_Update](functions/00488930.md) `0x0048A86E`, `Object_Update` `0x0049BE87`/`0x0049BEA1`, `Shot_Update` `0x004AB7C3`/`0x004AB7DD`, `Door_Update` `0x004A59FB`, `0x004A5A19`, `0x004A6483`, `0x004A649D`, `Trap_Update` `0x004A8DBD`, `0x004A8DD7`, `Room_Tick` `0x004E4BA0`, `World_UpdateTremors` `0x00510502`, `ViewEffect_Update` `0x00435149` | each update of the thing: the live handle(s) moved to its position | STRONGLY INFERRED | `VoiceLocator` and `ClassicSoundPlayer.FollowVoices`, each frame, for creatures, objects and shots; a door, trap, room and Tremor never moves |
| burning stop: [0x0048E160](functions/0048E160.md) at `0x0048E62E` | the update in which the burning counter reaches zero stops the creature's handle `+0x2E`, after the three effects | STRONGLY INFERRED | `StatusEffectRules.UpdateBurning` asks `ThingSoundRules.Stop` |
| `Creature_DeathBookkeeping` `0x00488076` | a burning creature that dies asks `0x340` with its handle and stops the returned handle at once: silence | STRONGLY INFERRED site | waits: ours keeps no burning state on a body |
| Casino party: [0x004D6270](functions/004D6270.md) at `0x004D63D0` | `0x316` in the category of the terrain at the room's anchor tile, with the room's handle `+0x14`, asked for each room creature in state 153 each update **at the camera's point** (`0x00572CD0` from the view object's `+0xD68..+0xD7C`; the tile's centre is used only for the terrain, the explored test and the distance); volume through slot `+0x34` property `0xE`: V x (1 - d / 45 tiles), d the larger axis distance capped at `0x2D000`, when the tile is explored, else 1; while the party runs on an explored tile a global volume is ducked every update (`+0x54`, `0x004D649F`) | STRONGLY INFERRED | `CasinoJackpotRules.RoomUpdate` asks `0x316` once per update with a dancer, listener-relative (`ThingSoundRequest.ListenerRelative`, `SoundRequest.VolumeAnchor`), the anchor the first tile (the room's `+0x27` is read as it, STRONGLY INFERRED); `ClassicSoundPlayer` plays it nonspatially at `AnchoredVolume` of the camera focus (V read as 1; no explored gate); the duck is not built (what it scales is unread) |
| the party off: `0x004D66BA`, `0x004D6727` | the handle's volume is set to 0 (`0x004D6690`), stopped, the global volume restored (`0x004D667A`), and `0x33E` asked with it at the tile's centre when the tile is explored | STRONGLY INFERRED | `CasinoJackpotRules.RoomUpdate`: `Stop`, then `0x33E`, as room voice |
| Casino freed, `0x004D7153`; Heart's room frame, `0x004DA0B2` (the Saturday dance) | stops the room's handle | STRONGLY INFERRED sites | waits: ours has no room-freed path that ends a party's music, and the dance needs the host clock |
| `Object_OnDelete` `0x0049B6BE`, `Door_Cleanup` `0x004A52FD`, `Trap_Cleanup` `0x004A7B6F` | stop the thing's second handle `+0x32` after the deletion request | STRONGLY INFERRED sites | nothing: ours gives an object, door or trap one voice, and the deletion's `0xF4` replaces it |
| the Tremor's replay `0x00510502` | an active slot's handle moved to its place every update | STRONGLY INFERRED | nothing: the place never changes |
| the flies' loop `0x152`, `0x004B1373` | handle 0 the first time, then `+0x30` replays it; asked only while the viewer possesses a creature | STRONGLY INFERRED site | waits for first person (section 4) |
| the boulder's roll `0x14F` | asked again at the start of each update (`Boulder_Roll`), and `Object_Update` replays the handle at the boulder's position | STRONGLY INFERRED | a re-request each update (W5) and `FollowVoices` for the object's voice |

**The loop model chosen (remake policy, Q678 stays open).** A request for a live voice of equal or higher priority is retained (`SoundVoiceBindings.CanRetain`), so a thing that asks again every update, as DK2's burning, boulder and tremor do, loops by being asked.
`ClassicSoundPlayer` keeps four rules of its own: a group with flag `0x08` plays looping; a `SoundRequest` with `Stop` ends the named voice at once and is exempt from the throttle; once a frame `FollowVoices` moves each playing voice to its thing's position
(creature, object, shot) without restarting it; a looping voice whose thing has left the world is stopped, so no loop outlives its thing. Unity's default 3D rolloff stays, and no voice count, distance or fade is imported.

Marked deviations: a replay is applied once per rendered frame, where DK2 applies it once per update; the party's explored-tile gates (the music at volume 1, the end sound not asked) are not applied, so a party in
unexplored ground is as audible as the camera's distance allows, and the base volume V (`0x0075AD17`) is read as 1; `0x316` is one request per update where DK2 asks once per dancer; the global volume `+0x54` sets at the party's end is not built (what it scales is unread).

## 4. Terrain ambience grid and first person

**The grid drives the ambient beds `0x2EA` and `0x2EB` (STRONGLY INFERRED, read 2026-11-03).** [0x0044E2A0](functions/0044E2A0.md) rebuilds, when the listener's point or three orientation words changed (or in map mode 7, or while the local player's `+0x134` is set), an
11-by-11 grid of words around the listener's tile, a cell zero unless the tile's flag bit `0x2000` (the explored bit the Casino also tests) is set, else the terrain's category handle `+0x195` (`+0x199` on a Temple tile whose room `+0x61` is zero, `+0x19D` on an arm the caller selects
in `EBX`, inferred zero). It hands the grid to service slot `+0x3C`, which is the ambient pass `0x00609480`: it marks each category whose map has group `0x2EA` (`0x00614C50`), keeps per category a tile count and a weight sum from the row and column offsets (table `0x006C6F98`, so a
point biased to the listener), adds an emitter for each category new to the list, then `0x00609CE0` pops the best two records from a heap ordered by the tile-count and weight records, so only those two categories keep emitters (the loop bound at `+0x28` is unread but at most two), and the handles of the others are stopped with a command carrying `0x46`. The per-update pass `0x00609A30` then asks group `0x2EA` of each emitter with its handle (through the category
player, `+0x10`), replays it at the emitter's point (`0x00608D50` is the replay `0x0060D950`), and asks `0x2EB` the same way only when `0x00608EA0` is at least 2; a handle that has died (the sample ended) is dropped, and an emitter with neither handle is removed
until the next grid builds it again. So the beds are the categories of terrain and rooms in view, not a per-thing sound. The plan's `0x319` is in no code as a literal (the push scan finds none) though every terrain category has it with four samples: its requester is UNKNOWN.

Samples: `0x2EA` is a single item in `terrain_water`, `terrain_lava` and their `_1st_person` variants (four samples), `room_graveyard` (three), `room_torture` and `room_workshop` (one), and a multi-item group (18 items in `room_library`, 14 in `room_portal`, 10 in `room_temple`, 3 in `room_prison`, 26 and 27 in the Hero Gate's; flags `0x04` or `0x0400`) elsewhere, which the
resolver does not play (it plays single-item groups; the linked picker is music's); `0x2EB` is in 9 categories with items.

| Request | Where it runs | Verdict | Built as |
|---|---|---|---|
| terrain and room ambience `0x2EA` | the grid, as above | STRONGLY INFERRED | `AmbienceGrid` (the cells and an emitter for the two heaviest categories with a playable group; the heap's comparator and tie-break are unread, so ours is cell count, first seen on a tie, INFERRED), `ClassicSoundPlayer.UpdateAmbience` (a voice per category, moved to the emitter's point, asked again when its sample ends, stopped when the category leaves); `DungeonRunner` builds it for the screen's centre tile |
| `0x2EB` | the same, when `0x00608EA0` is 2 or more | STRONGLY INFERRED | not built: the quality gate's meaning is unread |
| possessed Imp at rock it cannot dig or mine `0x335` | `GameAction_PossessedCreatureAct` at `0x00515021`: the solid-terrain arm, the creature's handle and category, at the act's pose | STRONGLY INFERRED site; the position HYPOTHESIS | `PossessionRules.DigOrClaim` (`creature_imp` only, two samples) |
| lava `0x313` | `Creature_Update` at `0x0048980C`: a worker (definition `+0x686` bit 0), every eighth turn staggered by its id, over the terrain it stands on: the terrain's own handle (`+0x101`) at the creature, in the category `+0x19D` when the local player possesses and the creature is the local player's, else `+0x195` | STRONGLY INFERRED site | waits: only `terrain_lava_1st_person` has the group (one sample), so it needs first person |
| flying creatures `0x152`, `0x147` | [0x0048FE30](functions/0048FE30.md): a flying creature while the local player possesses: types 12 and 20 (Firefly, Fairy) ask `0x152` and set property 3 to 100 when mood band 2, else 0; any other airborne one asks `0x147` | STRONGLY INFERRED | waits: first person; `0x147` has samples only in `creature_fairy`, whose type takes the first branch, so it is silent |
| the flies' loop on a body `0x152` | `0x004B1373`, sound-effects-combat.md section 5 | STRONGLY INFERRED site | waits: first person |
| possessed creature `0xC9`/`0xCC` (action 18) | `0x00499A40`, sound-effects-combat.md section 5 | STRONGLY INFERRED site | waits: first person |
| possessed footsteps `0x332`, `0x333`; wading `0x337`, `0x338` | the Hand's possessed movement at `0x0040F3FF` and `0x0040F504`: handle 0, the creature's category, alternating each 0x800 units of an accumulator, plus 5 while wading | STRONGLY INFERRED sites | waits: the first-person movement input is unbuilt; `0x332`/`0x333` have samples in 25 creature categories |

First person is unbuilt in the Unity adapter (HANDOFF, possession camera and input), so what needs the possessed view waits: lava, the flyers' beds, the flies, `0xC9`/`0xCC` and the possessed footsteps.

Marked deviations: the two-category limit's ordering is inferred; the emitter's point is the mean of its cells' centres, DK2's a weighted point; the listener is the screen's centre tile, DK2's a camera field; the Temple's waterless and the first-person categories are not asked;
a bed ends with its sample and the next frame asks again, DK2's next grid build; a category that leaves the grid is stopped at once, not with the `0x46` command; the beds in linked groups (Library, Portal, Temple, Prison, Hero Gate) are silent until a linked picker plays them.
