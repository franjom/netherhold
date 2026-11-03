# World sounds: wiring DK2's gameplay sound effects

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.
> **For Codex:** this plan is written for you. Phase W1 comes first; W2–W8 depend only on W1 and are independent of one another.

**Goal:** the player hears what DK2 plays while a level runs: the hand and its cursor, digging and claiming, rooms being built and sold, imps and creatures at work, objects, doors, traps, spells and fights, footsteps and the looping sounds of things and rooms. Interface, speech and music already work and are out of scope.

**Architecture:** unchanged (ADR-0002, ADR-0408). Every request is read from DKII.EXE before it is built. A request that a simulation function makes is emitted by our mirror of that function as a `SimEvent`, the way `ThingSoundRules` does for creatures; a request that a cursor or presentation function makes is emitted by `DK2.Presentation` into `SoundRequests`. Unity only plays. Sound never touches simulation state: no task in this plan moves the hash, and a task that finds it must stops and reports.

**Tech stack:** as in [the completion plan](2026-10-29-hud-and-front-end-completion.md), header.

---

## 0. Where things stand (2026-10-31)

| What | Where |
|---|---|
| Decoder, maps, resolver, voice ownership, speech, music | [sound-playback.md](../research/sound-playback.md), [sound-effects.md](../research/sound-effects.md), [music.md](../research/music.md); completion plan Phase C |
| The world-sound path (W1.1) | `ThingSoundRules` (simulation, any thing kind) → `SimEvent.ThingSound` → `WorldSoundDirector` (one voice per thing) → `ClassicSoundPlayer.Play` |
| World events that play today | those of the phases built in §5 (W1 to W6); each phase's Result lists what it built, what is silent and what waits |
| Open audio questions | Q678 (voice limits and fades), Q684 (flag and final playback semantics) |

The completion plan's "Later world-sound queue" note (after Task C3) is superseded by Task W7.1 here.

## 1. Rules for the executor

[The completion plan §1](2026-10-29-hud-and-front-end-completion.md#1-rules-for-the-executor) applies unchanged: worktree per phase, `kb_get` before decompiling and `kb_upsert` after, confidence on every claim, tests first, the gate, review and documentation pass per task. In addition:

- **Emit where DK2 emits.** For each call site in the tables below, find our mirror of the containing function through its KB record's `implements` line. If the function is not built, the sound waits for it; write nothing that fakes the trigger.
- **Category comes from the requester.** DK2 chooses the category from the thing's definition, never from the event: creature definition `+0x6E3`, object definition `+0x10E`, room or terrain definition `+0x195`, door definition `+0xA9`, trap definition `+0xB0`, shot definition `+0x125`, keeper spell `+0x72`, or the hand category (manager slot `+0x58`). Use the matching `SoundCategory` field our definitions already load (`CreatureDef.SoundCategory`, the KWD records' `SoundCategory`).
- **Handles are per thing.** Positional requests pass the thing's stored handle (`+0x2E`, a second one at `+0x32`) and store the returned one; `SoundVoiceBindings` is our equivalent. Give each thing a stable `SoundVoiceId`, as `WorldSoundDirector` does (`0x80000000 | kind<<24 | id`; creature voices unchanged).
- **A silent event is not a bug.** Some requested events have no samples in any shipped category (marked *silent?* below). Confirm against the shipped `SFX.map` of the actual category; if it has no group with samples, DK2 is silent there too, and we build nothing.

## 2. Definition of done

The owner plays Level 1 and Level 3 in Unity and hears, in each phase's area, what the original plays. Per phase: a checked row in §5 with the commit, the tests and the owner's hearing check. Automated: each emitted request has a test that the right function, in the right state, emits the right `(category, event)` with the thing's position and voice.

## 3. How the inventory was made (re-run it before each phase)

Every in-game sound goes through the sound manager at global `0x0075B468` (vtable `0x0066F7FC`):

| Slot | Function | Use |
|---|---|---|
| `+0x1C` | `0x00567790` | category lookup by name (definition loaders) |
| `+0x20` | `0x005678F0` | positional request `(handle, category, event, position)` |
| `+0x24` | `0x00567810` | nonpositional request `(handle, category, event)` |
| `+0x2C`/`+0x30` | `0x00567AF0`/`0x00567A70` | replay an existing handle, without / with position (loops) |
| `+0x58` | `0x00567240` | returns the HAND category |

Recipe: `ghidra.py SlotCallCtx.py <out> 0x20 40` (and `0x24`, `0x30`), keep the sites whose window loads `[0x0075b468]`, read the pushes (last push is the handle, then category, event, position), then look the event up in every non-speech `Data/Sound/Sfx/**/*SFX.map`. On 2026-10-31 this gave 196 positional and 40 nonpositional sites. The tables below are that result, grouped by phase. The family column was assigned by the category's load offset; a few may be wrong, so re-read each site before building it.

---

## Phase W1 — Foundation

### Task W1.1: A general world-sound event

**Research:** none beyond §1–3.

**Files:**
- Modify: `src/DK2.Simulation/CreatureSoundRules.cs` → a `ThingSoundRules.Request(context, system, thingId, category, event, position)` used by creatures, objects, doors, traps, shots and rooms; `ThingSoundEvent` holds the event values (`0xD6`, `0xDF` so far).
- Modify: `src/DK2.Simulation/SimulationEvents.cs` (`CreatureSound` → `ThingSound`), `src/DK2.Presentation/CreatureSoundDirector.cs` → `WorldSoundDirector`.
- Tests: `tests/DK2.Presentation.Tests/WorldSoundTests.cs` (was `CreatureSoundTests.cs`); `CombatApproachTests.ListeningForSoundsLeavesTheHashAlone` runs with and without a sound sink and compares hashes.

**Steps:** FAIL → implement → PASS → gate → commit `sound: one world-sound event for every thing kind`.

### Task W1.2: The frequent-event throttle

**Research:** [sound-effects.md §4](../research/sound-effects.md#4-slow-frame-throttle-and-unverified-playback-budgets) is CONFIRMED (`0x00566FC0`, `0x00567810`, `0x005678F0`): 29-event counters, the 17-value gap table, 300/500 ms frame thresholds.

**Files:** Create `src/DK2.Presentation/SoundThrottle.cs`, applied to every effect request in Unity's `ClassicSoundPlayer.Play`, not in `WorldSoundDirector`: the native gate sits in the manager's request slots (`0x00567810`, `0x005678F0`), so it also covers interface sounds such as `0x30F`. `DungeonRunner.Sound.cs` updates it after each world frame (`0x005674F0` calls `0x00566FC0` at `0x00567500`); the front end never does. Tests: `SoundThrottleTests.cs` from the record's table values and boundaries (exactly 300 ms is normal).

**Steps:** FAIL → implement → PASS → gate → commit `sound: the native frequent-event throttle`.

### Task W1.3: Positional playback in Unity

`ClassicSoundPlayer` plays world requests at their position. Q678 stays open: do not import OpenKeeper's voice counts, distances or fades. Set a 3D source per request with Unity's default rolloff, record the choice as a remake policy in the code comment, and leave Q678 as is. The director places a sound at the view's `(x, height, -y)`, as `DungeonScene.Ground` draws. Owner check: a battle call is heard from its side of the screen.

---

## Phase W2 — The hand and its cursor (presentation)

These run in cursor and hand code, so they are emitted in `DK2.Presentation` (`HandCursor`, `PlayerInput`) as `SoundRequest`s, not as `SimEvent`s, unless the KB record shows the function is simulation.

| Event | Category | Site | What |
|---|---|---|---|
| `0xEC` | hand | `0x0040E300` (`Cursor_CompleteTagState`) | the press that enters state 5 (tag or untag), at the cursor (`+0x1078/+0x107C`, height `0x2000`); also a nonpositional `0xEC` in a terrain category, silent |
| `0xC5` | hand | `0x0040E58D`, latch byte `0x006CE95C` | slap |
| `0xC5` | object | `0x0049C2E2` (`Object_ReactToSlap_LIKELY`) | slapped object |
| `0xC3`/`0xC4` | creature | `0x004BFCE2` (`Hand_PrepareCreature_LIKELY`) / `0x004BC81D` (`Hand_Drop`) | creature picked up / dropped |
| `0xC3`/`0xC4` | object | `0x004C0E01` (`HandState2_LiftObject_LIKELY`) / `0x004BCA02` (`Hand_Drop`) | gold and objects picked up / dropped (`object_gold`) |
| `0xCA`/`0xCB`/`0xCC`, variable | creature | `0x0040C446`/`0x0040C414`/`0x0040C3E2`/`0x0040C371` (`Hand_UpdateHoverTarget_LIKELY`) | hovering over a creature |
| `0xE4` | object | `0x0040C50F` | hovering over an object (*silent?*) |

**Research first (answered 2026-11-02, [sound-effects.md section 6](../research/sound-effects.md#6-the-hand-and-its-cursor)):** untagging enters the same state 5, and `0xEC` sounds on the *press* that starts a tag or untag drag, not on completion; the variable event at `0x0040C371` is the current action record's `+0x19` word, with `0xCA`/`0xCB`/`0xCC` by mood class only for a creature in no room when no sounding action runs; the slap latch is cleared only when the swing completes.

**Result:** tag `0xEC` and slap `0xC5` (hand) built in `HandCursorFeedback`; creature and object `0xC3`/`0xC4` built in `HandSystem` as `ThingSound` events (simulation, not presentation: `Hand_Drop` and the lift functions are the mirrored functions); object slap `0xC5` and object hover `0xE4` are silent (no object category has the group); the nonpositional `0xEC` is silent; creature hover waits for the creature action table (W7.2): only the action word decides which arm runs, though the mood arm itself needs just `+0x3B9`, `+0x28A` and the turn gate. Tests: `HandSoundTests` in `DK2.Presentation.Tests` and `DK2.Simulation.Tests`.

**Owner check (Unity, Level 1 or 3):** a tag or untag gives one click at the press and none at release, a click without drag included; a slap sounds about 0.1 s into the swing, and a right-click within about 4 frames of the last is swallowed (DK2's guard); lifting with a full hand still sounds; an Imp has pick-up and drop sounds while a firefly and a Jack in the Box stay silent; lifting loose gold sounds and lifting a treasury pile is silent.

**Commit:** `sound: the hand's tag, slap, pick-up and drop sounds` (`dee9c954`, review fixes `59f121af`); the hover sounds move to W7.2.

---

## Phase W3 — Terrain, digging, claiming and rooms

| Event | Category | Site | What |
|---|---|---|---|
| `0x10C` | terrain | `0x00453359` (`Tile_ApplyWork`) | the upward conversion: reinforce, claim, corner fill (only `terrain_rock` has it); not a dig hit |
| `0xF4` | terrain | `0x0045327C` (`Tile_ApplyWork`) | tile broken (wall, gold, gems, rock) |
| `0x10A`, `0x10B` | creature | `0x00497635` (`Imp_ClaimAction`) | imp claiming |
| `0xF1` | room | `0x004C5D87` (`Player_BuildRoomTile_LIKELY`) | room tile built |
| `0xF5`, `0xF4` | room | `0x004C5EF8`, `0x004C5F29` (`Player_SellRoomTile_LIKELY`) | room tile sold |
| `0xF7` | room | `0x004E570C` (`Room_TransferOwnership_LIKELY`, nonpositional) | room captured (*silent?*) |
| `0xF8` | room | `0x004E81BE` (`Room_TypeEffectResponse`) | (*silent?*) |
| `0x2F8` | room | `0x00442D57` | the build drag's accept click, from the command handler `0x00442960`; not "most rooms" |
| `0x34B` | room | `0x004DD024` (nonpositional) | portal |
| `0xF1`, `0x316`, `0x33E` | room | `0x004D9E00`, `0x004DA00F`, `0x004DA114` (`DungeonHeart_Update`) | heart opening (local player); `0x316`/`0x33E` are the casino's category, the Saturday dance |
| `0x329` | room | `0x004DA2DB` (`Player_ModifyHeartHealth`) | heart damaged |
| `0xDA` | room | `0x004DA5B2` (`Heart_OnDestroyed_LIKELY`) | heart destroyed |
| `0xF4` | room | `0x004DAC38`, `0x004DB102` | heart collapse / local defeat |
| `0x150`, `0x2F9` | room | `0x0043CE85`, `0x004A32F9` | heart presentation |
| `0x2E8`, `0x2E9` | room | `0x004F4F03`, `0x004F4C27`, `0x004F4DC7` (`Workshop_Tick_LIKELY`) | workshop; the timers landed (ADR-0634, hash 385) and the sounds are built in W6 |
| `0x316`, `0x33E`, `0x344` | room/effects | `0x004D63D0`, `0x004D6727`, `0x004D6610` (`Casino_RoomUpdate`) | casino |
| `0xE7`–`0xEA` | room | `0x004E3669`, `0x004E3729`, `0x004E377E`, `0x004A3819`, `0x004A38E9` | prison doors |
| `0x3F` | room | `0x004E2DBD` | (*silent?*) |

**Result (2026-11-02, [sound-effects.md sections 7 to 9](../research/sound-effects.md#7-terrain-digging-claiming-and-rooms)):**
- Built: tile broken `0xF4` and converted `0x10C` (`Tile_ApplyWork`; `0x10C` is the upward conversion, not a "dig hit", so reinforcing rock sounds and claiming dirt path does not), the imp's claim `0x10A`/`0x10B`, room tile built `0xF1` and sold `0xF5`, the build command's accept click `0x2F8` (the command-table handler `0x00442960`, not "most rooms"), heart opening `0xF1` (local player only), blow `0x329`, destroyed `0xDA`, death-turn collapse `0xF4`, heart beat `0x150` (presentation), and the Casino party's pop `0x344` (global `effects` category, played by the view only when it creates the party's effect `0x140`).
- Silent (no `SFX.map` has the group): `0xF7`, `0xF8`, `0x3F`; the refundless sale's `0xF4` has samples only in the heart's category and no worked-down tile is a heart's.
- Waiting: Saturday-dance `0x316`/`0x33E` in the heart's room frame (the plan's "heart" label is the casino's category; `World_HostClockEvents` is unbuilt), local-collapse `0xF4` at `0x004DB102` and `0x2F9` (collapse unbuilt; `0x2F9`'s site is a HYPOTHESIS), portal `0x34B` (multiplayer arrival), prison doors `0xE7`-`0xEA` (door objects unbuilt), workshop `0x2E8`/`0x2E9` were waiting for the `Workshop_Tick` timers, which landed with ADR-0634 (hash 385); they are built in W6 (`WorkshopTimerRules.RequestSound`; the Torture Chamber, Temple and Graveyard have the same timer gap)

**Owner check (Unity, Level 1 or 3):** digging rock and mining gold sound at the tile as it breaks, reinforcing a wall sounds at the end of the work, an imp's claim sounds at the imp; a dragged room gives one click at its lowest corner, then a sound for each tile after the first diagonal, and selling a tile sounds; the heart gives its opening sound once at the start, a beat about once a second, and a blow on a heart, its destruction and its collapse at death sound, and the collapse does not cut the destruction's rumble; a Casino party's fireworks pop every twelve seconds only while they are on screen on a tile you have seen; a computer keeper's first-tick room build clicks. `creature_imp`'s `0x10C` is most likely the wall-repair action's sound word (STRONGLY INFERRED; W7.2), not a reinforcing sound.

**Commits:** `sound: tile breaking, reinforcing and the imp's claim`, `sound: room tiles built and sold, and the build command's accept click`, `sound: the heart's opening, blows, destruction, beat and collapse`, `sound: the casino party's tile effect`, and the review fixes `48bd6fb0`.

---

## Phase W4 — Imp jobs and creature states

All in the creature's category (`creature_<type>`), positional at the creature, with its handle.

| Event | Site (function) | What |
|---|---|---|
| `0xC4` | `0x00466B95` (`ImpState29_DepositGold`, object category) | gold deposited |
| `0x10D` | `0x00467B37`, `0x0048B687` | carrying: the corpse's state, and every update of a creature with `+0x3C5` set |
| `0x326`/`0x327`/`0x328` | `0x00466DC7`, `0x00466F0B`, `0x00467575` (`State37_InstallDoor`), `0x0046729F` | imp building and installing |
| `0x33D` | `0x00497A9A` (`CreatureAction_Reinforce`) | reinforcing walls |
| `0x342`/`0x343` | `0x0046547A`, `0x004653B0` (`Imp_WalkOrTeleport`) | imp teleport |
| `0x2FA` | `0x00475934` | responding to a threat |
| `0x312` | `0x0048FBA2` | Reaper proximity |
| `0xE0` | `0x00459C73` (`CreatureState_JoiningDungeon`), `0x00487BC4` | creature arrives |
| `0xF6` | `0x0046EEA9` (`State76_DropLanding_LIKELY`) | dropped creature lands |
| `0xCE` | `0x0046FC7A` (`State83_Sleeping_LIKELY`) | sleeping |
| `0xCD`, `0x111` | `0x0046398D`, `0x00463859` (`Eat_EatChicken`) | eating (`0x111` in `object_chicken`) |
| `0xD3` | `0x004857CD` (`Training_State111`) | training |
| `0xE2`, `0xD4` | `0x004729FB`, `0x00472E70` | temple |
| `0xD5`, `0xE1`, `0xCA` | `0x00456AE9`/`0x00456B37`, `0x00456DF0`, `0x00457677` | casino |
| `0xC3` (object), `0xCA` | `0x00472803`, `0x0047287A` (`Payday_TakeWage`) | payday |
| `0xCB` | `0x00481719` (`State213_PauseWithSound`) | pause |
| `0xC7` | `0x0048104C`, `0x00481549` | tremor knock-down |
| `0xDC` | `0x004617E6`, `0x004754AC` (`Combat_RaiseAlarm`) | withdraw, alarm |
| `0xDD`, `0xDE` | `0x004630FF`, `0x00483021` | a guard's lost leg, a sulker (not knights) |
| `0x349` | `0x0048B2DB` (`Creature_LevelUp`) | level up |
| `0x340` | `0x00488052`, `0x0048E4F1` | burning |
| `0x317` | `0x00489F1C` | goblin only |
| `0x32D`/`0x32E`/`0x323` | `0x004747BA`, `0x00474295`, `0x00474644`, `0x00474B74` | Horned Reaper |
| `0x34C` | `0x004B315E` | Jack-in-the-box |
| `0xE3`, `0x12C`, `0x137`, `0xA7`, `0x51` | `0x0048B795`, `0x004743FE`/`0x0047488F`/`0x00474D32`, `0x00488D4F`, `0x0048FC42`, `0x004D6C23` | (*silent?*) |

**Commit per state family** (imp jobs; arrival and rest; rooms' creature states; level-up and Reaper).

**Result (2026-11-02, [sound-effects.md sections 10 to 13](../research/sound-effects.md#10-imp-jobs)):**
- Built: the imp's gold deposit `0xC4` (gold object's category, handle 0), carrying `0x10D` (every update of a creature whose `+0x3C5` holds a thing, so a Portal Gem carrier is silent; only `creature_imp` has samples), shelving or storing `0x326`, installing `0x327`/`0x328`; a local keeper's creature leaving its portal `0xE0` (marked for the owner), the tremor knockdown `0xC7`, withdraw and alarm `0xDC`, the Scout's dispatch `0x2FA`, a guard's lost leg `0xDD` (audible only for the knight and stone knight), eating `0x111` then `0xCD`; training `0xD3` (the melee arm only), temple `0xE2`/`0xD4`, casino `0xE1`/`0xD5` (every eight seconds) and the jackpot's `0xCA`, payday `0xC3`/`0xCA`; level-up `0x349` (through a new `StateHookContext.RequestSound`), the Reaper's `0x32D`/`0x32E`, burning `0x340` (the request; its stop is W7.3), the Jack in the Box `0x34C`.
- Silent (no `SFX.map` has the group): `0xE3`, `0x12C`, `0x137`, `0xA7`, `0x51`, the Reaper constructor arm's `0xFB`; `0x312` and `0xA7` are also unreachable (ADR-0402), and state 213's `0xCB` has no entry.
- Waiting: reinforce `0x33D` (no clip for the action's gate) and the imp teleport `0x342`/`0x343` (Q418); the hero constructor's `0xE0` (no event path in `WorldState.SpawnCreature`, one gate unread), the landing `0xF6` (no fall), sleeping `0xCE` (action 24), state 209's `0xC7`, the sulk `0xDE` (state 85), the roulette's four-second `0xD5` (wheel objects), state 240's `0x323`/`0x111`, a burning body's `0x340`, `0x317` (`ThreatThing`).
- Disputed: `0xDD`/`0xDE` are a guard's and a sulker's, not knights'; `0x340` is the burning sound; `training-actions.md` had both training arms sounding and `casino.md` had state 149 silent; the tremor starter's `0xC7` had been read as a display request. No W4 site uses `creature_imp`'s `0x10C`: it is the wall-repair action's sound word (W7.2).
- Marked deviations: the Temple pool drop sounds `0xE2` at the drop, DK2 plays it after the stun; the chicken's `0x111` plays at floor height; only the casino's eight-second `0xD5` is built.

**Owner check (Unity, Level 1 or 3):** an imp dragging a body, book or crate has a repeating drag sound and a click as it shelves or installs; an imp taking gold to the Treasury clinks; a creature of yours stepping out of its portal calls out and a hero arriving is silent for now; a withdrawing or alarmed creature calls out; a creature eating a chicken sounds the bird then itself; a trainee grunts as each swing begins, a worshipper as it starts to pray and a sacrifice sounds; gamblers and drinkers sound every eight seconds of an action, a jackpot winner as it takes its winnings, and a paid creature as it takes its wage; a level-up sounds; a Reaper's arrival sounds; a burning creature crackles; a dying Jack in the Box sounds. Open for the owner: whether an imp's carrying sound loops to its end or re-triggers audibly every update; whether a paid creature's `0xCA` is heard after `0xC3` (same handle); whether the Temple pool's early `0xE2` at the drop is acceptable; whether a guard that cannot reach its next leg is silent, except a knight or stone knight; whether a Casino roulette gambler sounds every four seconds and the other tables every eight (ours is eight everywhere).

**Commits:** `b703c90d` (imp jobs), `03e387b4` (arrival, withdraw, alarm, guard, tremor and meal), `e360fbb8` (training, temple, casino and payday), `655308ca` (level-up, Reaper, burning and Jack in the Box), and the review fixes `d5c78bbf`.

---

## Phase W5 — Objects, doors, traps, specials, boulder

| Event | Category | Site | What |
|---|---|---|---|
| `0xF1`, `0xF2` | object | `0x0049B4C8`, `0x0049B4ED` (`Object_Init_LIKELY`), `0x0048AE27` | object created (bed, chicken, bat, rat) |
| `0xF4` | object | `0x0049B691` (`Object_OnDelete_LIKELY`) | object removed |
| `0xF6` | object | `0x0049F03E` | object lands (*silent?*) |
| `0x110`, `0x111` | object | `0x0049FA95`, `0x00474BA3`, `0x00514BEA` | chicken |
| `0x112` | object | `0x00498E08` (`CreatureAction_Manufacture_LIKELY`) | forge |
| `0x311` | object | `0x004A14F2` (`ObjectState18_Struck`) | training dummy |
| `0xFB`, `0x31B` | object | `0x00499404`, `0x00499593`, `0x00499634`, `0x004994D7` | torch, wheel, electric chair |
| `0x10E` | object | `0x004E0B36`, `0x004E0CC6`, `0x004E0E56`, `0x004E1008` (`Library_GrantSpell_LIKELY`) | library (*silent?*; check `library_book`) |
| `0x80`–`0x8F`, `0x310` | object | `0x004A40A7` … `0x004A503A` (`Special_*`) | each special when activated |
| `0x149`–`0x15A`, `0xF4` | object | `0x004A0A96` … `0x004A13DC` (`Boulder_*`) | boulder (`object_boulder` groups are empty; check `trap_boulder`) |
| `0xE7`/`0xE8`, `0xE9`/`0xEA`, `0xEB` | door | `0x004A60B9`, `0x004A6844` (`Door_SetLockMode`), `0x004A642B` | door open/close, lock/unlock |
| `0xF2`, `0xF4`, `0xF5` | door/trap | `0x004A5275`, `0x004A7AF1`; `0x004A52D0`, `0x004A7B48`; `0x004C3705`, `0x004C3825`, `0x004C3A99`, `0x004C3B2A` | built, destroyed, sold |
| `0xEB` | trap | `0x004A82C2`, `0x004A830E` (`Trap_CreateShot_LIKELY`) | trap fires |
| `0xED`, `0xF3` | trap | `0x004A8C1F`, `0x004C360D`, `0x004C39C9` | (*silent?*) |
| `0x2EA`, `0x33F` | object/global | `0x004DF7F0`, `0x004DF860` (`HeroGate_Tick_GUESS`) | hero gate |

**Result (2026-11-02, [sound-effects-objects.md](../research/sound-effects-objects.md), a new document because sound-effects.md would pass its length budget):**
- Built: every object's creation `0xF1`/`0xF2` and deletion `0xF4` (the world calls the sound rules from `SpawnObject` and `RemoveObject`; asked for every object, playback is silent where the category has no group), the chicken's step `0x110` (an `Alternating` request on each call with step count 1; the listener keeps DK2's global counter, no simulation state), the Training Dummy's `0x311`, the specials' `0x80`, `0x8B` and `0x8F` (117, 124, 129), a possessed creature's chicken meal `0xCD`/`0x111`; door `0xE7`/`0xE8` (open state), `0xE9`/`0xEA` (lock command, finale Reaper), trap `0xEB` (the Alarm's two as requests for one keeper's screen) and `0xF4` at the trap's cleanup; the boulder's `0x14F`, `0x149`, `0x14D`/`0x14E`, `0x14A`/`0x15A`, `0x14B`, `0x14C` and `0xF4`. `DoorDef` and `TrapDef` now load the sound category (`Doors.kwd` `0x248`, `Traps.kwd` `0x20F`; the plan's "`+0xF7`" is the thing's definition pointer) and the door's impact byte (`0x226`).
- Silent (no `SFX.map` has the group in the category): object `0xF6`, library `0x10E`, special `0x310`, `0x33F`, trap and door `0xED`, `0xF3`, `0xF2`, door `0xF4`/`0xF5`, trap `0xF5`, object slap `0xC5`, the launch of the boulder, and the torch (no shipped object names `OBJECT_TORCH`).
- Waiting: the torture devices' `0xFB`/`0x31B` (the handler of actions 36 to 38 at `0x00499300`, every sound behind global `0x00759ADC`, open Q635), the Magic Door's `0xEB`, the sale of a door or trap (`0xF4` in the trap's category), the hero gate room's `0x2EA` (unbuilt tick handler), the second `0xF1` of a bed (not asked: a live handle retains it).
- Disputed: the plan's `0x110` sites `0x00474BA3` and `0x00514BEA` ask `0x111`; "`0x10E` library (check `library_book`)" has no group anywhere; the boulder's launch asks no sound; the torch row's categories are not used by any object.
- Marked deviations: a created object's position is the tile's centre when the creator sets it after `SpawnObject` returns; level-load, hatchery-initialiser and snapshot-restore spawns are silent, and so is the level loader's door lock `0xE9`; a door killed this tick may still sound its open state this tick (our health pass runs after the door pass); doors and traps request at the tile's centre and the floor's height; the possessed meal's `0xCD` (listed under W7.4) is built with its `0x111`.

**Owner check (Unity, Level 1 or 3):** a hatched or newly made chicken, a bed placed and a rat or bat made sound; a chicken eaten or killed sounds as it is deleted, and a walking chicken sounds on every second step it takes; a Training Dummy struck by a trainee sounds at the start of its spin; using a special (Increase Level, Receive Imps, Locate Hidden Land) sounds at the box; a door clicks open as a creature reaches it and shut again after, and the lock command clicks; a trap sounds as it fires, only your own Alarm Trap and an Alarm Trap your creatures set off sound; a spent Boulder Trap sounds as its boulder is made; a rolling boulder rumbles, crashes at a turn, sounds on creatures, doors, water and lava, and at its break. Open for the owner: whether the boulder's roll sample (asked every update) drowns the turn crash and the break because DK2's live handle retains them; whether doors click open and shut on every flip as often as in the original; whether a created hatchery chicken's position (its egg's, set after creation) is audible; whether a chicken's deletion is audible under the `0x111` bite; whether a door locked at level load is heard locked in the original (ours is silent); whether the Torture Wheel and the Electric Chair make a sound in vanilla, which settles Q635.

**Commits:** `a3a7183d` (objects, dummy, specials, possessed meal), `ef7d7510` (doors and traps), `4c5d0ced` (the boulder), and the review fixes `09bb0f67`.

---

## Phase W6 — Spells, shots and combat

| Event | Category | Site | What |
|---|---|---|---|
| `0xEF` | keeper spell | `0x004BBEBD`, `0x004BC26F`, `0x004BC4DB` | keeper spell cast |
| `0xFB`, `0x2EC`, `0x31F` | keeper spell | `0x00510355`, `0x005105C3`, `0x005104C6` | tremor |
| `0xEF`, `0xF2`, `0xF4` | shot | `0x004AAF1E`, `0x004AAF40`, `0x004AAF9F`, `0x004AB509` | shot launched / ended |
| `0x1BC`, `0x31A`, `0x12A`, `0x154` | shot | `0x004AB49B`, `0x004AC00C`, `0x004ADA49`/`0x004AFF6B`, `0x004B52FC` | raise dead, skeleton army, grenade |
| `0xE8` | shot | `0x004AF1BF` | sight of evil (*silent?*) |
| `0x336` | creature | `0x004AFA7D` | gas |
| variable | shot | `0x00498459`, `0x0049868C`, `0x00498A8D` | creature spell: event from the spell record `+0x3F` |
| `0xDB` | creature | `0x004992EB` (`Combat_MeleeSwing`), `0x004AC099` | melee swing, shot hit |
| variable | weapon | `0x00499A15` (`Attack_LookupImpactPair_LIKELY`), table `0x0069DA39` | impact by weapon material (`weapon_*`) |
| `0xC9`, `0xCF`, `0x156` | creature | `0x00499B5C`, `0x00499767`, `0x0049965D` | read the sites |
| `0xDA` | creature | `0x00487E4A` (`Creature_DeathBookkeeping_LIKELY`) | death |
| `0xF4`, `0x152` | global `0x006E5048` | `0x004B10A0`, `0x004B1373` | corpse removed, flies |

**Result (2026-11-02, [sound-effects-combat.md](../research/sound-effects-combat.md), a new document because sound-effects.md is near its length budget):**
- Built: a keeper spell's cast `0xEF` (`Spell_CastAtPosition` only when the shot's own category is empty, `Spell_CastOnCreature` always, in the spell's category at the cast point), the Tremor's `0xFB`, `0x2EC` and `0x31F`, a shot's constructor `0xEF`/`0xF2` (two handles), the world stop's `0x1BC`, the lifetime death's `0xF4` and the teardown's `0xF4` (second handle), the Sight of Evil's `0xE8` (its owner's screen), a struck creature's `0xDB` (shots with flags bit 8), a creature's spell word (`+0x3F`) in the shot's category at the caster, Create Imp's cast and constructor sounds (`ImpSupplySystem.Cast`, bought and free), the swing's closing `0xDB`, the weapon impact (`0x004998A0`: `0x114`, `0x1BC`, `0x113`, `0x1BB` by the struck thing's surface byte, in `WEAPON_METAL`, `_WOOD`, `_FLESH` or `_SCYTHE` by the attacker's attack type, at the attacker, on the creature, door, trap and object arms and the possessed act's Anvil, Bucket and Work Bench), the Workshop's `0x2E9` (steam, lava spill) and `0x2E8` (smoke), a creature's `0xDA` and a rotted or buried body's `0xF4` in `OBJECT_FLIES`. `KeeperSpellDef` now loads its category (file `0xF1`) and `CreatureDef`, `TrapDef` and `ObjectDef` their surface byte `ImpactKind` (files `0xF76`, `0x22F`, `0x340`), each with a round trip and a shipped-names or shipped-values test; `ShotDef` already loaded its (`0xC9`) and the test now pins its shipped names. The plan's "keeper spell `+0x72`" is the shot's `+0x72`; the keeper spell's is `+0x6D`, and a shot thing's `+0x125` is its definition pointer.
- Silent (no `SFX.map` has the group): the Tremor's end `0x31F` and the cast's `0xEF` in `KEEPER_SPELL_TREMOR`; every creature-spell word but `0x115` (fireballs, spit) and `0x12A` (Skeleton Army); the weapon impact for surfaces 5 to 7 (`0x112` only in `OBJECT_FORGE`, `0x1BD` nowhere) and `0x1BC` in `WEAPON_SCYTHE`; `0x336` is also unreachable (every shipped gas shot has damage type 2).
- Waiting: the third cast path `0x004BC4DB` (unmirrored, never reached by the computer keeper), `0x31A` (our shots never find a body), `0x12A` (the non-worker arms of shot processes 1 and 36), the grenade's `0x154` (no shot height, Q398), `0xC9`/`0xCC` (action 18's first-person handler, W7.4), the flies' `0x152` (a first-person loop, waits for the possession camera), and the death cry for the creature removals that do not die (leaving, the Temple, the hand).
- Torture handler (`0x00499300`): `0x31C`, `0xCF`, `0x156` **wait**, with the device sounds. Each is behind `[0x00759ADC] == 1`, which has no store and sits in uninitialised data (probably 0 in vanilla, so silent; Q635), and the handler has no mirror.
- Disputed: the plan's `0x1BC` row is the generic world stop, not Raise Dead; its `0xEF`/`0xF2`/`0xF4` shot rows are the constructor, the lifetime death and the teardown; `0x004998A0` was recorded as a pure lookup for presentation and also asks two effects and the weapon sound, with the attacker as `this` (so its handle and position are the attacker's), whose first reading here was mostly silent by missing `WEAPON_SCYTHE` and surface 1, 3 and 4; `0x00499A15` is its sound request, not a separate site; `Shot_Create` passes 0 only in the saved-game loader.
- Marked deviations: a shot's sound position is its view position (height approximate); a place cast's `0xEF` uses the shot's view height as the command carries none; a standing mass-0 shot (every shipped shot but the Grenade) asks `0x1BC` every update, as its hit-solid effect is requested; the weapon impact's two effects are not asked, its surfaces 6 and 7 are `0x112` and `0x1BD` (the jump table sends 6 to record `+0x21`, 7 to `+0x1D`; the first reading had them swapped) and its creature, trap and object surface bytes are read only when the loaders' presence dwords are set, which ours does unconditionally (harmless for shipped data); Create Imp's free cast sounds at z 0 and its bought cast at the floor's height, the bought lookup (world vtable `+0x204`) being unread; the heart arm uses the Arch Pillar's surface byte; `DeathUncounted` is never set, so its guard is inert; the Tremor slot's handle is a voice per slot; the death cry is asked in `CreatureHealthSystem.Die` only; a corpse's removal voice is the body's, where DK2's is the flies' loop.

**Owner check (Unity, Level 1 or 3):** casting Create Imp (the spell's sound at the point, for a bought and a free imp), Thunderbolt, Chicken, Create Gold, Heal or Call to Arms gives the spell's cast sound at the target or the place, Inferno and Sight of Evil theirs from the shot; a Tremor rumbles at the cast and thumps as it shakes the floor; Sight of Evil gives a cue five seconds before it ends, only for its owner; a warlock's or salamander's fireball sounds as the shot leaves; a shot sounds at its death, and its second handle's sound shortly after; a creature's swing sounds a weapon impact (a metal, wood, flesh or scythe thud by what it hits: in melee a blow on flesh and on metal, that is flesh creatures, knights, doors, traps and objects, differ) and then its own cry when its blow lands, and a possessed creature striking a Work Bench thumps with flesh-weapon samples, an Anvil and a Steel Bucket theirs; a creature sounds as it dies; a body sounds as it rots away; a Workshop sounds with its steam and lava-spill effects and with its smoke, each every 8 to 24 seconds. Open for the owner: whether the death and teardown `0xF4` of one shot are heard as one or as two; whether `0x2EC` every update while a Tremor shakes is too frequent for the throttle; whether a creature that leaves the dungeon or is sacrificed sounds its death cry in the original (ours is silent); whether Torture Chamber victims scream and whether the Electric Chair shows the `G_` or the full Electrocute animation (settles Q635).

**Commits:** `9ae756d5` (spells, Tremor, shots, a creature's spell), `eca19945` (swing, death, corpse), `59f04ff4` (this section), the merge of master `bf9bb3de` (hash 385), `81553e10` (weapon impact, Create Imp, the Workshop, the review fixes) and `f3a1b3ab` (the impact's surface order, Create Imp's z, the Workshop's sounds).

---

## Phase W7 — Data-driven and continuous sounds

### Task W7.1: Footsteps

As [sound-effects.md §1](../research/sound-effects.md#1-creature-footstep-events) (CONFIRMED): parse the 48 animation-mark pairs at file `+0x1323` (`KwdCreatures` does not yet), cross them on the normalized animation phase, request `0x314`/`0x315`, wading `0x337`/`0x338` (`0x0048FF10`, called from `0x0043CC30`). Presentation only. Preserve phase crossing, cumulative thresholds and the throttle. Do not import OpenKeeper's three-per-200 ms rule.

### Task W7.2: Action-table sounds

`CreatureAction_Begin` (`0x004968EB`) plays the action record's `+25` word ([creature-action-table.md §5](../research/creature-action-table.md#5-the-25-word-is-a-sound-id-not-a-label)). Load that word and emit it where our action starts.

### Task W7.3: Looping thing and room sounds

Slot `+0x30` replays a stored handle at the thing's position every update: `Door_Update` (`0x004A59FB`…), `Trap_Update` (`0x004A8DBD`), `Shot_Update` (`0x004AB7C3`), `Object_Update` (`0x0049BE87`), `Creature_Update` (`0x0048A86E`), `Room_Tick` (`0x004E4BA0`), tremors (`0x00510502`). Read how the handle is first created and stopped (`+0x38` sites in the `*_Cleanup` functions) before building a loop owner in `WorldSoundDirector`.

### Task W7.4: Terrain ambience grid and first person

`EffectGrid_UpdateAroundListener` (`0x0044E2A0`) hands an 11-by-11 grid of per-terrain words to the sound service (slots `+0x3C`/`+0x48`/`+0x54`). It probably drives the terrain loops (`0x2EA`, `0x2EB`, `0x319` in every terrain category); confirm that before building. Possession: `0x313` lava (`0x0048980C`), `0x147`/`0x152` (`0x0048FEA4`, `0x0048FECE`), `0x335`, `0xCD` (`GameAction_PossessedCreatureAct`), the movement sites `0x0040F3FF`/`0x0040F504`.

**Result (2026-11-03, [sound-effects-continuous.md](../research/sound-effects-continuous.md), a new document because sound-effects.md is at its length budget):**
- **W7.1 built.** `KwdCreatures` reads and writes the 48 mark pairs (`CreatureDef.FootstepMarks`; pairs 46 and 47 of the shipped files hold an ASCII pose name, not marks); `AnimationMarkCrossing` mirrors `0x004B4630`, `AnimationMarkWatcher` keeps the last drawn frame's fraction, `CreatureView` carries the walk's or run's pair, its category and the grounded-water flag, and `DungeonScene` asks `CreatureFootsteps` (`0x314`/`0x315`, `0x337`/`0x338` wading) after each creature clip draws, through the player's throttle. Silent for every creature without authored marks (only the Imp, Horny, the Giant, the Knight, the Black Knight, the Stone Knight, King Reginald, the Princes, the Lord of the Land and the Unique Black Knight have them) and for those without `0x314` samples on land.
- **W7.2 built** (`CreatureActionTable`, the 44 words read from the filler `0x004950C0`): the imp's dig `0x334` and mine `0x109` (each clip wrap), wall repair `0x10C` (once per work state), the swing `0xD7`, casts `0xD8`, the trainee's `0xD3` (the spell arm now sounds), the Anvil's `0x112`, and the creature hover (`CreatureHoverSound`, presentation; the action is told by `CreatureCurrentAction`'s stand-ins). Sleeping `0xCE` and reinforce `0x33D` wait (an unread bed gate; no clip for the reinforce gate). The imp's `0x10C` is the repair action's word (CONFIRMED from the table), not reinforcing. The other 30 words wait for the mirrors of the states that begin them.
- **W7.3 built.** The manager's `+0x30` is a position update of a live handle and `+0x38` a stop command (not "replay a loop"), so the model is: `SoundRequest.Stop` and `ThingSoundRules.Stop`, `VoiceLocator` with `ClassicSoundPlayer.FollowVoices` (a playing voice follows its creature, object or shot; a looping voice whose thing is gone ends), and a loop for groups whose flag word has bit `0x08` (only the fireball's and firebomb's trail). On it: the burning stop and the Casino party's `0x316` and `0x33E`. The Tremor's replay (a place that never moves) needs nothing; the boulder's roll is a re-request each update plus the follow; the flies wait for first person.
- **W7.4:** the grid drives the ambient beds `0x2EA` and `0x2EB` per category (CONFIRMED; `0x319` has no requester found), built as `AmbienceGrid` and `ClassicSoundPlayer.UpdateAmbience` for single-item groups (water, lava, graveyard, torture, workshop; the Library, Portal, Temple, Prison and Hero Gate beds are linked groups and wait for a linked picker); the possessed Imp's `0x335` at rock it cannot dig is built. Lava `0x313`, the flyers' `0x152`/`0x147`, the flies, `0xC9`/`0xCC` and the possessed footsteps `0x332`/`0x333` wait for the first-person camera.
- Disputed or corrected: `training-actions.md` and W4's "melee arm only" (both actions' begins ask `0xD3`); the action-table document's `0xC5`-`0xE1` list (`0xE5`, `0xE6` too) and its "when the animation flag is set" (a phase test); the plan's "stop in the `*_Cleanup` functions" (right for the sites, but the slot is a command object's stop); `0x00488076` stops a burning body's voice at once.
- Marked deviations: the footstep interval's earlier fraction moves back 256 per turn (native 250; the fractions are STRONGLY INFERRED and the difference benign), only walk and run clips are tested and only creatures drawn in the frustum sound (DK2 also sounds undrawn ones); the begin's phase test is "fresh or wrapped last turn"; repair is asked once per work state; the hover names only the actions ours models; replay is per frame; the party's music is at the camera with a linear distance volume (V read as 1), its explored-tile gates and its global duck are not applied and `0x316` is one request per update; the ambience keeps the two heaviest categories (the tie-break is inferred), its emitter point is the mean of its cells, the listener the screen's centre tile, and `0x2EB`, the Temple's and the first-person categories are not asked. No voice budget, rolloff or fade of our own is imported beyond the party's linear 45-tile volume, which is DK2's (Q678 stays open).

**Owner check (Unity, Level 1 or 3):** an Imp, Knight or Giant walking sounds its steps on land and any creature wading sounds splashes; imps digging and mining make a repeating grunt every three or four ticks, a repairing imp one sound, a caster's cry, a trainee's grunt, and a manufacturer at an Anvil hammers; hovering over your own creature every three seconds sounds its mood (or its action's word); a burning creature crackles and stops when the fire ends; a Casino party plays its music while dancers dance and a closing sound when it ends; a boulder's rumble follows it; water and lava near the screen's centre hum, and stop as they leave the view. Open for the owner: whether the first-person-only sounds (lava, flyers' wings, steps) are missed before the possession camera exists; whether an imp's per-wrap dig and mine sound is as busy as the original's; whether the repair sound should repeat; whether the ambience bed volume and its restart after each sample is right (DK2's mix and the weighted point are unread); whether the footsteps of the Imp (the only common creature with authored marks) are as frequent as in the original; whether the hover of an own imp at work sounds its dig word; whether fogged or undrawn creatures should sound their footsteps (DK2 sounds them; ours only those drawn in the frustum); whether the party's music is centred, fades linearly to silence at 45 tiles from its Casino, and whether the dip of a global volume DK2 makes while it plays (not built; what it scales is unread) is missed; whether at most two ambience beds at once are enough.

**Commits:** `f587916b` (footsteps), `ff88c15d` (the action table, the forge and the hover), `395b993e` (stops, following voices and the party's music), `a69ea175` (the ambience beds, the possessed Imp's rock sound and this section) and `ba6fdc57` (the review fixes: the party's music at the camera, the two-category ambience limit, the labels).

---

## Phase W8 — Remaining interface and global events

Not world sounds, but found unwired by the same scan: cheat entry `0x133`; icon pages `0x33A` (`0x004125A7`, `0x00413AA3`); tab scroll `0x32B`/`0x32C`; front-end map `0x32A`; options `0x33C`, `0x341`, speech and music tests `0x345`/`0x346`, `0x31D`; mana `0xF` (`0x004C1226`); `0x1` (`0x004B9BD6`); effects `0x344` (`0x004BA0A8`); one-shot atmosphere `0x2EA` (`0x005372B3`); speech-path site `0x00568531`. Check each against what the completion plan's C3 already built before adding it.

---

## 5. Progress

| Phase | Commit | Tests | Owner heard it |
|---|---|---|---|
| W1 Foundation | `5950dc09`, `14fe94e6`, `d8b71e07`, `07318061` | `WorldSoundTests`, `SoundThrottleTests` (`DK2.Presentation.Tests`), `CombatApproachTests.ListeningForSoundsLeavesTheHashAlone`; full gate green | [ ] |
| W2 Hand and cursor | `dee9c954`, `59f121af` | `HandSoundTests` (`DK2.Presentation.Tests`, `DK2.Simulation.Tests`), `WorldSoundTests`, `ObjectsOnRealDataTests.ObjectSoundCategoriesAreTheShippedNames`; creature hover waits (W7.2) | [ ] |
| W3 Terrain and rooms | `1f042522`, `add84b79`, `264833a8`, `45730dbb`, `48bd6fb0` | `TileSoundTests`, `RoomSoundTests`, `HeartSoundTests`, `CasinoStateTests` (`DK2.Simulation.Tests`), `HeartBeatSoundTests`, `PartyPopSoundTests`, `WorldSoundTests` (`DK2.Presentation.Tests`), `KwdTerrainRoomsTests.TerrainSoundCategoriesAreTheShippedNames`, `CreatureSoundCategoryTests` (`DK2.Data.Tests`); workshop (built in W6), prison doors, portal, casino music, Saturday dance and local collapse wait | [ ] |
| W4 Imps and creature states | `b703c90d`, `03e387b4`, `e360fbb8`, `655308ca`, `d5c78bbf` | `ImpJobSoundTests`, `CreatureStateSoundTests`, `CreatureEventSoundTests` and the sound tests in `CombatFearTests`, `CombatNonFighterTests`, `CreatureJobTests`, `TempleStateTests`, `CasinoStateTests`, `PaydayTests`, `PortalCreatureActionTests`, `FinaleTests`, `JackInTheBoxTests` (`DK2.Simulation.Tests`); waiting rows in the Result above | [ ] |
| W5 Objects, doors, traps | `a3a7183d`, `ef7d7510`, `4c5d0ced`, `09bb0f67` | `ObjectSoundTests`, `PossessionSoundTests`, `DoorTrapSoundTests`, `BoulderSoundTests`, the door unlock in `FinaleTests`, `CreatureStateSoundTests` (`DK2.Simulation.Tests`), `WorldSoundTests` (`DK2.Presentation.Tests`), `KwdDoorsTrapsTests`, `KwdDoorWriterTests`, `KwdTrapWriterTests` (`DK2.Data.Tests`); torture devices, Magic Door, sales and hero gate wait | [ ] |
| W6 Spells, shots, combat | `9ae756d5`, `eca19945`, merge `bf9bb3de`, `81553e10`, `f3a1b3ab` | `SpellSoundTests`, `ShotSoundTests`, `CreatureSpellSoundTests`, `CombatSoundTests`, `ImpSupplySoundTests`, `WorkshopSoundTests`, `PossessionSoundTests` (`DK2.Simulation.Tests`), `WorldSoundTests` (`DK2.Presentation.Tests`), `KwdKeeperSpellWriterTests`, `ImpactKindTests` and the creature, object and trap writer tests (`DK2.Data.Tests`); the third cast path, `0x31A`, `0x12A`, `0x154`, `0xC9`, the flies and the torture handler's sounds wait | [ ] |
| W7 Data-driven and loops | `f587916b`, `ff88c15d`, `395b993e`, `a69ea175`, `ba6fdc57` | `AnimationMarkCrossingTests` (`DK2.Convert.Tests`), `CreatureFootstepMarksTests` (`DK2.Data.Tests`), `CreatureFootstepViewTests`, `CreatureFootstepSoundTests`, `CreatureHoverSoundTests`, `SoundLoopTests`, `AmbienceGridTests` (`DK2.Presentation.Tests`), `CreatureActionTableTests`, `ActionBeginSoundTests`, `CreatureCurrentActionTests`, `ForgeSoundTests`, `CasinoPartyMusicTests`, `PossessionSoundTests` and the burning, training and casino tests (`DK2.Simulation.Tests`); sleeping, reinforce, the linked ambience beds and every first-person sound wait | [ ] |
| W8 Interface leftovers | | | [ ] |
