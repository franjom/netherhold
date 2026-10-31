# World sounds: wiring DK2's gameplay sound effects

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.
> **For Codex:** this plan is written for you. Phase W1 comes first; W2–W8 depend only on W1 and are independent of one another.

**Goal:** the player hears what DK2 plays while a level runs: the hand and its cursor, digging and claiming, rooms being built and sold, imps and creatures at work, objects, doors, traps, spells and fights, footsteps and the looping sounds of things and rooms. Interface, speech and music already work and are out of scope.

**Architecture:** unchanged (ADR-0002, ADR-0408). Every request is read from DKII.EXE before it is built. A request that a simulation function makes is emitted by our mirror of that function as a `SimEvent`, the way `CreatureSoundRules` does today; a request that a cursor or presentation function makes is emitted by `DK2.Presentation` into `SoundRequests`. Unity only plays. Sound never touches simulation state: no task in this plan moves the hash, and a task that finds it must stops and reports.

**Tech stack:** as in [the completion plan](2026-10-29-hud-and-front-end-completion.md), header.

---

## 0. Where things stand (2026-10-31)

| What | Where |
|---|---|
| Decoder, maps, resolver, voice ownership, speech, music | [sound-playback.md](../research/sound-playback.md), [sound-effects.md](../research/sound-effects.md), [music.md](../research/music.md); completion plan Phase C |
| The one world-sound path built so far | `CreatureSoundRules` (simulation) → `SimEvent.CreatureSound` → `CreatureSoundDirector` → `ClassicSoundPlayer.Play` |
| World events that play today | creature `0xD6` battle call (`CombatSystem`) and `0xDF` sacked (`LeavingRules`) |
| Open audio questions | Q678 (voice limits and fades), Q684 (flag and final playback semantics) |

The completion plan's "Later world-sound queue" note (after Task C3) is superseded by Task W7.1 here.

## 1. Rules for the executor

[The completion plan §1](2026-10-29-hud-and-front-end-completion.md#1-rules-for-the-executor) applies unchanged: worktree per phase, `kb_get` before decompiling and `kb_upsert` after, confidence on every claim, tests first, the gate, review and documentation pass per task. In addition:

- **Emit where DK2 emits.** For each call site in the tables below, find our mirror of the containing function through its KB record's `implements` line. If the function is not built, the sound waits for it; write nothing that fakes the trigger.
- **Category comes from the requester.** DK2 chooses the category from the thing's definition, never from the event: creature definition `+0x6E3`, object definition `+0x10E`, room or terrain definition `+0x195`, door or trap definition `+0xF7`, shot definition `+0x125`, keeper spell `+0x72`, or the hand category (manager slot `+0x58`). Use the matching `SoundCategory` field our definitions already load (`CreatureDef.SoundCategory`, the KWD records' `SoundCategory`).
- **Handles are per thing.** Positional requests pass the thing's stored handle (`+0x2E`, a second one at `+0x32`) and store the returned one; `SoundVoiceBindings` is our equivalent. Give each thing a stable `SoundVoiceId`, as `CreatureSoundDirector` does for creatures.
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
- Modify: `src/DK2.Simulation/CreatureSoundRules.cs` → a `ThingSoundRules.Request(context, system, thingId, category, event, position)` used by creatures, objects, doors, traps, shots and rooms; keep `CreatureSoundEvent` values as constants.
- Modify: `src/DK2.Simulation/SimulationEvents.cs` (`CreatureSound` → `ThingSound`), `src/DK2.Presentation/CreatureSoundDirector.cs` → `WorldSoundDirector`.
- Tests: rename and extend `tests/DK2.Presentation.Tests/CreatureSoundTests.cs`; add a test that a simulation run with a sound sink and without one ends with the same hash.

**Steps:** FAIL → implement → PASS → gate → commit `sound: one world-sound event for every thing kind`.

### Task W1.2: The frequent-event throttle

**Research:** [sound-effects.md §4](../research/sound-effects.md#4-slow-frame-throttle-and-unverified-playback-budgets) is CONFIRMED (`0x00566FC0`, `0x00567810`, `0x005678F0`): 29-event counters, the 17-value gap table, 300/500 ms frame thresholds.

**Files:** Create `src/DK2.Presentation/SoundThrottle.cs`, applied in `WorldSoundDirector` before a request reaches the player. Tests: `SoundThrottleTests.cs` from the record's table values and boundaries (exactly 300 ms is normal).

**Steps:** FAIL → implement → PASS → gate → commit `sound: the native frequent-event throttle`.

### Task W1.3: Positional playback in Unity

`ClassicSoundPlayer` plays world requests at their position. Q678 stays open: do not import OpenKeeper's voice counts, distances or fades. Set a 3D source per request with Unity's default rolloff, record the choice as a remake policy in the code comment, and leave Q678 as is. Owner check: a battle call is heard from its side of the screen.

---

## Phase W2 — The hand and its cursor (presentation)

These run in cursor and hand code, so they are emitted in `DK2.Presentation` (`HandCursor`, `PlayerInput`) as `SoundRequest`s, not as `SimEvent`s, unless the KB record shows the function is simulation.

| Event | Category | Site | What |
|---|---|---|---|
| `0xEC` | hand | `0x0040E300` (`Cursor_CompleteTagState`) | tag tiles, at the cursor (`+0x1078/+0x107C`, height `0x2000`); also a nonpositional `0xEC` in a definition category, likely silent |
| `0xC5` | hand | `0x0040E58D`, latch byte `0x006CE95C` | slap |
| `0xC5` | object | `0x0049C2E2` (`Object_ReactToSlap_LIKELY`) | slapped object |
| `0xC3`/`0xC4` | creature | `0x004BFCE2` (`Hand_PrepareCreature_LIKELY`) / `0x004BC81D` (`Hand_Drop`) | creature picked up / dropped |
| `0xC3`/`0xC4` | object | `0x004C0E01` (`HandState2_LiftObject_LIKELY`) / `0x004BCA02` (`Hand_Drop`) | gold and objects picked up / dropped (`object_gold`) |
| `0xCA`/`0xCB`/`0xCC`, variable | creature | `0x0040C446`/`0x0040C414`/`0x0040C3E2`/`0x0040C371` (`Hand_UpdateHoverTarget_LIKELY`) | hovering over a creature |
| `0xE4` | object | `0x0040C50F` | hovering over an object (*silent?*) |

**Research first:** whether untagging goes through `0x0040E300` too (read the tag state table, hand-cursor-rendering.md); what selects the variable event at `0x0040C371`; the slap latch's reset.

**Files:** `HandCursor.cs`/`PlayerInput.cs` emit; tests in `DK2.Presentation.Tests` (`HandSoundTests.cs`): one `0xEC` per completed tag, at the cursor; slap once per latch.

**Commit:** `sound: the hand's tag, slap, pick-up, drop and hover sounds`.

---

## Phase W3 — Terrain, digging, claiming and rooms

| Event | Category | Site | What |
|---|---|---|---|
| `0x10C` | terrain | `0x00453359` (`Tile_ApplyWork`) | dig hit (only `terrain_rock` has it) |
| `0xF4` | terrain | `0x0045327C` (`Tile_ApplyWork`) | tile broken (wall, gold, gems, rock) |
| `0x10A`, `0x10B` | creature | `0x00497635` (`Imp_ClaimAction`) | imp claiming |
| `0xF1` | room | `0x004C5D87` (`Player_BuildRoomTile_LIKELY`) | room tile built |
| `0xF5`, `0xF4` | room | `0x004C5EF8`, `0x004C5F29` (`Player_SellRoomTile_LIKELY`) | room tile sold |
| `0xF7` | room | `0x004E570C` (`Room_TransferOwnership_LIKELY`, nonpositional) | room captured (*silent?*) |
| `0xF8` | room | `0x004E81BE` (`Room_TypeEffectResponse`) | (*silent?*) |
| `0x2F8` | room | `0x00442D57` | most rooms have it; read the site |
| `0x34B` | room | `0x004DD024` (nonpositional) | portal |
| `0xF1`, `0x316`, `0x33E` | room | `0x004D9E00`, `0x004DA00F`, `0x004DA114` (`DungeonHeart_Update`) | heart |
| `0x329` | room | `0x004DA2DB` (`Player_ModifyHeartHealth`) | heart damaged |
| `0xDA` | room | `0x004DA5B2` (`Heart_OnDestroyed_LIKELY`) | heart destroyed |
| `0xF4` | room | `0x004DAC38`, `0x004DB102` | heart collapse / local defeat |
| `0x150`, `0x2F9` | room | `0x0043CE85`, `0x004A32F9` | heart presentation |
| `0x2E8`, `0x2E9` | room | `0x004F4F03`, `0x004F4C27`, `0x004F4DC7` (`Workshop_Tick_LIKELY`) | workshop |
| `0x316`, `0x33E`, `0x344` | room/effects | `0x004D63D0`, `0x004D6727`, `0x004D6610` (`Casino_RoomUpdate`) | casino |
| `0xE7`–`0xEA` | room | `0x004E3669`, `0x004E3729`, `0x004E377E`, `0x004A3819`, `0x004A38E9` | prison doors |
| `0x3F` | room | `0x004E2DBD` | (*silent?*) |

**Commit per group:** digging and claiming; build and sell; heart; workshop, casino, prison and portal.

---

## Phase W4 — Imp jobs and creature states

All in the creature's category (`creature_<type>`), positional at the creature, with its handle.

| Event | Site (function) | What |
|---|---|---|
| `0xC4` | `0x00466B95` (`ImpState29_DepositGold`, object category) | gold deposited |
| `0x10D` | `0x00467B37`, `0x0048B687` | carrying a body |
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
| `0xDD`, `0xDE` | `0x004630FF`, `0x00483021` | knights; read the site |
| `0x349` | `0x0048B2DB` (`Creature_LevelUp`) | level up |
| `0x340` | `0x00488052`, `0x0048E4F1` | read the site |
| `0x317` | `0x00489F1C` | goblin only |
| `0x32D`/`0x32E`/`0x323` | `0x004747BA`, `0x00474295`, `0x00474644`, `0x00474B74` | Horned Reaper |
| `0x34C` | `0x004B315E` | Jack-in-the-box |
| `0xE3`, `0x12C`, `0x137`, `0xA7`, `0x51` | `0x0048B795`, `0x004743FE`/`0x0047488F`/`0x00474D32`, `0x00488D4F`, `0x0048FC42`, `0x004D6C23` | (*silent?*) |

**Commit per state family** (imp jobs; arrival and rest; rooms' creature states; level-up and Reaper).

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

---

## Phase W8 — Remaining interface and global events

Not world sounds, but found unwired by the same scan: cheat entry `0x133`; icon pages `0x33A` (`0x004125A7`, `0x00413AA3`); tab scroll `0x32B`/`0x32C`; front-end map `0x32A`; options `0x33C`, `0x341`, speech and music tests `0x345`/`0x346`, `0x31D`; mana `0xF` (`0x004C1226`); `0x1` (`0x004B9BD6`); effects `0x344` (`0x004BA0A8`); one-shot atmosphere `0x2EA` (`0x005372B3`); speech-path site `0x00568531`. Check each against what the completion plan's C3 already built before adding it.

---

## 5. Progress

| Phase | Commit | Tests | Owner heard it |
|---|---|---|---|
| W1 Foundation | | | [ ] |
| W2 Hand and cursor | | | [ ] |
| W3 Terrain and rooms | | | [ ] |
| W4 Imps and creature states | | | [ ] |
| W5 Objects, doors, traps | | | [ ] |
| W6 Spells, shots, combat | | | [ ] |
| W7 Data-driven and loops | | | [ ] |
| W8 Interface leftovers | | | [ ] |
