# The computer keeper — what `DKII.EXE` does when it plays against you

**Branch `claude/keeper-ai`, 2026-09-06.** Research write-up for the AI keeper:
where it lives in the binary, every parameter it carries and where each comes
from, and the decision loop, read to the depth stated in §5. Nothing here is
decompiler output and nothing here is a Flame identifier; every function is
referred to by its address and by a description in our own words (ADR-0046).
Where the running game has not been watched, a binary-derived fact is capped at
**STRONGLY INFERRED**; **CONFIRMED** below means *confirmed from shipped data*,
never from the decompiler.

Working name for the thing in any future code: **KeeperAI**. The binary's own
vocabulary is "Computer Player" (`ComputerPlayer.cpp`, and a debug dump that
prints `Computer Player` as its header); the field names used below are EA's,
read from that dump's format strings, not anyone's reconstruction.

| Tag | Meaning here |
|---|---|
| **CONFIRMED** | Read from shipped data (`Data/editor/**`), reproducible with a hex editor |
| **STRONGLY INFERRED** | Read from the binary — string tables, instruction operands, or control flow; two independent methods where noted |
| **HYPOTHESIS** | One reading, or a decompiler-only inference about intent |
| **UNKNOWN** | Nothing checked shows it |

---

## 1. Where it lives

### 1.1 Address range and shape — STRONGLY INFERRED

| | |
|---|---|
| Translation unit | `ComputerPlayer.cpp`; its `__FILE__` string is the full path `D:\Dev\DK2\Projects\Source\Game\ComputerPlayer.cpp` at `0x006A3348` |
| Code range | `0x004F8150` – `0x00504D5F`. 58 Ghidra functions; 14 of them carry the assert string (30 references, found identically by Ghidra's reference model and by a raw scan for the address word), spanning `0x004F9B90`–`0x00503690` |
| Neighbours | Below: `Workshop.cpp` (last of the room files, references to `0x004F4F9F`). Above: the task-stack and world-dump code, then `World.cpp` from `0x0050B5B0`. So the **alphabetical-bracketing rule does not hold across the binary**: the room files are one block, the creature states another, and `ComputerPlayer.cpp` sits between the rooms and `World.cpp`. Note the two path spellings — room files are `...\source\game\`, this one is `...\Source\Game\` — which is the build's object-list order, not the alphabet |
| Class shape | **Not polymorphic.** RTTI names `CPlayer` and the interface classes; there is no computer-player class name. The AI object is a plain 1,273-byte struct owned by each `CPlayer` through a pointer at `CPlayer+0x1101`, next to a one-byte `computer player on` switch at `CPlayer+0x10FA` |
| Source size | The RNG helper is called with `__FILE__`/`__LINE__`; the largest line number seen is 7,543 (`0x1D77`), so the unit was at least that long |
| Source lines named by asserts | Fourteen functions; the rest of the unit has no anchor and is attributed by contiguity, which Flame independently agrees with (§9) |

### 1.2 Creation, and the two ways it gets its parameters — STRONGLY INFERRED

Every `CPlayer` constructs an AI object in its own init (`0x004B8640` calls the
constructor `0x004F82E0` and the two-pointer init `0x004F8330`). Whether it ever
*runs* is the `computer player on` byte. Two routines turn it on:

1. **Campaign / level data.** The world's level loader calls `0x0051F0B0` once per
   PLAYERS record. If the record's `AI` int is non-zero it switches the player to
   AI (`0x004BB800`), copies the record's **158-byte parameter block** and hands it
   to the unpacker `0x004F8750` (§3.1). It also sets the player's name from the
   record, the trigger id, and — for the local human — the starting camera.
2. **Skirmish / multiplayer lobby.** A world routine at `0x0050C800`, in game modes
   2 and 3, reads a per-slot lobby setting for every player with id ≥ 4: `0xFF`
   leaves the slot human, `9` initialises the slot as *no player*, and any other
   value switches the slot to AI and calls the **personality setter** `0x004F8C60`
   with that value (§3.2) — one of **nine built-in personalities**.

Before either, `0x004F8360` writes the **defaults** (§3.3).

### 1.3 How it is ticked — STRONGLY INFERRED

```
world player-tick loop (0x004C2310)
  └─ for every player: CPlayer tick (0x004B9EC0)
       ├─ neutral player  → other path, return
       ├─ good player     → sets a field to INT_MAX, return
       ├─ recompute treasury gold
       └─ if computerPlayerOn AND player status ∉ {1, 3}:
            AI tick (0x00500C20), timed with QueryPerformanceCounter-style stamps
            into three globals (total, max, count) — the retail build still
            profiles the AI
```

So the AI is ticked **every player tick, with no timer of its own**. Its
`TaskTimeCounter` is incremented once per call and is used only as a task
*timeout* (§5.1). All pacing inside comes from a per-tick **action budget** and
from probabilistic steps. The status values 1 and 3 that stop it are the ones the
player struct stores when a keeper is out of the game; which is "won" and which
"lost" is UNKNOWN from here.

---

## 2. The parameter surface

### 2.1 Where the names come from

The struct's debug dump at `0x00503F50` prints **121 labelled fields** in EA's own
names, each read from a fixed offset — which is how every offset below was
obtained without trusting a third-party layout. Flame's layout of the same struct
(75 named fields) agrees on every offset it names; the dump additionally names the
**45 bit-fields** packed into two 32-bit words that Flame left as opaque flags,
and the 16 runtime bits of the first flags word.

### 2.2 Tuning parameters — one row per field

*Offset* is into the 1,273-byte AI struct. *Block* is the byte offset into the
158-byte PLAYERS block (§3.1). *Table* is the personality table column (§3.2).
*Read by* lists the functions that read the field at runtime, found by an
instruction-level scan of memory-operand displacements across the whole region
(the loaders, the dump and the checksum excluded); for bit-fields the consumer was
found by matching the mask in the decompiled text, a weaker method. *Range* is
across the **22 real AI keepers** in the 66 shipped levels (§3.1); the
Good/Neutral rows are excluded because the player tick never reaches the AI for
them.

| Offset | Field (EA name) | Width | Block | Table | Read by (addr) | Campaign range | Default |
|---|---|---|---|---|---|---|---|
| `+0x4AF` | `TendancyMoney` | u16 | `0x86` u16 | misc+1 | tick `500C20` | 1000–20000 (mode 5000) | 0 |
| `+0x4B1` | `TendancyType` | u8 | — | — | **nobody** | — | 0 |
| `+0x4BA` | `BlibBlubBlob` | u32 | — | — | `4FAEF0`, `4FDC50` (low nibble = current attack target + 1) | — | 0 |
| `+0x4BE` | `MinimumRandomTimeBeforePlacingReseachedRoom` | u8 | `0x14` | rooms+0 | **nobody** | 0, 1, 15, 30 | 0 |
| `+0x4BF` | `DefaultRoomSize` | u8 | `0x15` | rooms+9 | `4FC690`, `4FCE70` | 9, 12, 16, 25 | 9 |
| `+0x4C0` | `TrapAndDoorUsage` | u8 % | `0x39` | traps+11 | `5000A0` | 1–99 | 0 |
| `+0x4C1` | `MaximumImps` | u8 | `0x79` | creatures+17 | tick | 4–20 | 10 |
| `+0x4C2` | `TilesPerImpRatio` | u8 | `0x90` (**byte**, editor writes u16) | misc+6 | tick | 1–40 (232 = garbage byte) | 50 |
| `+0x4C3` | `ImprisonPercentage` | u8 % | `0x64` | battles+4 | `4F9B90`, `4FA000` | 0–100 | 0 |
| `+0x4C4` | `BARetreatHealth` | u8 % | `0x65` | battles+9 | `4FF260` | 10–30 | 0 |
| `+0x4C5` | `SpecialSearchChance` | u8 | `0x9B` | misc+4 | `4F93B0` (see quirk §6.3) | 0, 1 | 0 |
| `+0x4C6` | `RoomList[15]` | 15×u8 | `0x1B` | computed (§3.2) | `4F93B0` | 8 distinct orders; default `1,2,3,4,6,7,8,…,16` | 0 |
| `+0x4D5` | `SpecialFindChance` | u8 | `0x9C` | misc+5 | **nobody** (but see §6.3) | 0–90 | 0 |
| `+0x4D6` | `Flexibility` | u8 | `0x2A` | rooms+21 | **nobody** | 0, 17, 100 | 0 |
| `+0x4D7` | `IAMinimumCreatures` | u8 | `0x7B` | attack+0 | `4FAB10` | 0–30 | 0 |
| `+0x4D8` | `IAThreatSuperiority` | u16 % | `0x53` u8 | attack+6 | `4FAEF0` | 0–200 | 100 |
| `+0x4DA` | `BAThreatSuperiority` | u16 % | `0x62` u8 | battles+0 | `4F9B90`, `4FA000` | 0–200 | 0 |
| `+0x4DC` | `BACallToArmsValue` | u16 | `0x5A` u16 | battles+2 | `503060` | 150–30000 | 0 |
| `+0x4DE` | `BuildLostRoomAfterMinimumTime` | u8 | `0x04` | rooms+16 | **nobody** | 0, 15, 30 | 0 |
| `+0x4DF` | `SpecialRange` | u8 | `0x8E` (**byte**, editor u16) | misc+3 | **nobody** | 2–15 | 0 |
| `+0x4E0` | `NeutralRoomRangeFromHeart` | u8 | `0x1A` | rooms+19 | `4FC690` | 1–100 | 0 |
| `+0x4E1` | `NeutralRoomRangeFromClaimedArea` | u8 | `0x2B` | rooms+20 | `4FC690` | 0–100 | 0 |
| `+0x4E2` | `IAUseCreatures` | u8 % | `0x57` | attack+22 | **nobody** | 0–100 | 0 |
| `+0x4E3` | `TendancySpeed` | u8 | `0x01` | misc+12 | tick (**the per-tick action budget**) | 1, 2, 4 | 1 |
| `+0x4E4` | `Openness` | u8 % | `0x02` | misc+13 | `5012A0` | 15, 20, 50, 100 | 0 |
| `+0x4E5` | `GoldValue` | u16 | `0x66` | misc+16 | **nobody** | always 100 | 0 |
| `+0x4E7` | `ManaValue` | u16 | `0x58` | misc+14 | **nobody** | always 100 | 0 |
| `+0x4E9` | `RemoveCallToArmsAfter` | u16 | `0x2C` | battles+10 | `4FECE0`, `503060` | 60–14639 | 0 |
| `+0x4EB` | `RemoveCallToArmsEnemyCreatures` | u8 | `0x5C` | battles+12 | `4FEFC0` | 0–6 | 0 |
| `+0x4EC` | `RemoveCallToArmsWithinRange` | u8 | `0x5D` | battles+13 | `4FEFC0` | 0–5 | 0 |
| `+0x4ED` | `RemoveCallToArmsOwnCreatures` | u8 | `0x03` | battles+14 | `4FEFC0` | 0–10 | 0 |
| `+0x4EE` | `WaitBeforeAttackingAgain` | u8 | `0x88` (**byte**, editor u16) | battles+15 | `4FAEF0`, `4FEFC0` (×4 → ticks) | 0–150 (232, 244 = garbage) | 0 |

**Bit-fields in the first parameter word `+0x4B2`** (all loaded from the block
and from the tables):

| Bits | Field | Block | Table | Read by | Campaign values |
|---|---|---|---|---|---|
| 0 | `CreateEmptyRoomShapeAreasWhenIdle` | `0x08` i32 | rooms+1 | none found | 0/1 |
| 1 | `AutomaticallyBuildBiggerLairWhenPortalTakenOver` | `0x0C` i32 | rooms+5 | `4FA400` | 0/1 |
| 2–5 | `TilesLeftBetweenRooms` | `0x16` | rooms+10 | `5012A0` | 0–4 |
| 6 | `PlaceRoomsNextToIdeal` | `0x17` | rooms+15 | `4FC690` | 0/1 |
| 7–8 | `BuildNewRoomWhenMoreSpaceRequired` | `0x19` | rooms+18 | `4FA400` | 0/1 |
| 9–11 | `AggressiveVsDefensiveTrapPreference` | `0x36` | traps+0 | `4FFD80` | 0–4 |
| 12–14 | `TrapVsDoorPreference` | `0x37` | traps+1 | `4FFD80` | 0–4 |
| 15–16 | `DoorPreference` | `0x38` | traps+2 | `4FFD80` | 0/1 |
| 17–18 | `ProbabilityOfMovingCreatureForResearch` | `0x9A` | misc+0 | tick | 0–2 |
| 19–22 | `IACreatureSkillLevel` | `0x52`, clamped to a global max level | attack+1 | `4FAB10`, `4FF260`, `4FF5C0` | 0–10 |
| 23 | `IAAllRoomsPlaced` | `0x42` i32 | attack+11 | `4FAEF0` | 0/1 |
| 24 | `IAAllSpellsResearched` | `0x46` i32 | attack+15 | `4FAEF0` | 0/1/2 (2 reads as 0) |
| 25 | `IAOnlyAttackAttackers` | `0x4A` i32 | attack+23 | `4FAEF0` | 0/1 |
| 26–28 | `AttackingVsDefensiveSpellPreference` | `0x63` | battles+1 | `503060` | 0–4 |
| 29 | `BoulderTrapsOnLongCorridors` | `0x2E` i32 | traps+3 | `4FFD80` | 0/1 |
| 30 | `BARemoveIfCantReinforce` | `0x5E` i32 | battles+5 | `4F9B90`, `4FA000` | 0/1 |

**Bit-fields in the second word `+0x4B6`:**

| Bits | Field | Block | Table | Read by | Campaign values |
|---|---|---|---|---|---|
| 0–2 | `UseKeeperSpells` | `0x81` | spells+1 | `503060` | 0–4 |
| 3–4 | `IAFirstStart` | `0x55` | attack+20 | `4FAEF0` | 0–3 (one 255, masked to 3) |
| 5–6 | `IAFirstEnd` | `0x54` | attack+19 | `4FAEF0` | 0–3 |
| 7 | `SellRecentlyTakenOverRoom` | `0x10` i32 | rooms+11 | none found | 0/1 |
| 8–9 | `CorridorStyle` | `0x18` | rooms+17 | none found | 0–2 |
| 10 | `BoulderTrapOnBreachRoute` | `0x32` i32 | traps+7 | none found | 0/1 |
| 11–14 | `BreachAtXPoints` | `0x56` | attack+21 | copied into all 16 breach slots at load | 1–50 (>15 truncates) |
| 15 | `UnhappyCreaturesHappy` | `0x68` i32 | creatures+0 | `4FA400` | 0/1 |
| 16 | `AngryCreaturesHappy` | `0x6C` i32 | creatures+4 | `4FA400` | 0/1 |
| 17 | `AngryCreaturesDisposeOf` | `0x70` i32 | creatures+8 | `4FA400` | 0/1 |
| 18 | `AngryDisposalMethod` | `0x78` u8 | creatures+12 | `4F9520` | 0/1 |
| 19 | `UnwantedCreatureDisposeOf` | `0x74` i32 | creatures+13 | `4FA400` | 0/1 |
| 20–21 | `UseSightOfEvil` | `0x80` | spells+0 | tick, `5003A0` | 0–2 |
| 22 | `LightningInWater` | `0x7C` i32 | spells+2 | `4FA400` | 0/1 |
| 23–25 | `SpellPreference` | `0x82` | spells+6 | `503060` | 0–4 |
| 26–27 | `CallToArmsUsage` | `0x83` | spells+7 | `4FAEF0`, `4FE9C0`, `4FECE0` | 0–2 |
| 28–29 | `ImprisonedCreatureAction` | `0x9D` | misc+7 | `5006D0` | 0–2 |
| 30 | `NeverAttack` | `0x4E` i32 | attack+27 | `4FAEF0` | 0/1 |

**Nine parameters are loaded but never read** — `TendancyType`,
`MinimumRandomTimeBeforePlacingReseachedRoom`, `SpecialFindChance`, `Flexibility`,
`BuildLostRoomAfterMinimumTime`, `SpecialRange`, `IAUseCreatures`, `GoldValue`,
`ManaValue` — by both the displacement scan and the decompiled-text scan. This is
STRONGLY INFERRED with one caveat: a read through a pointer that already carries
part of the offset would escape both methods. Four bit-fields
(`CreateEmptyRoomShapeAreasWhenIdle`, `SellRecentlyTakenOverRoom`, `CorridorStyle`,
`BoulderTrapOnBreachRoute`) have no consumer by the weaker method only, so they are
HYPOTHESIS-dead.

### 2.3 Runtime state (not tuning) — STRONGLY INFERRED

| Offset | Field | Meaning as used |
|---|---|---|
| `+0x000` bits 0–7 | `Opened`, `WallHug`, `WallHugLeft`, `FightGoingOn`, `NextRoomToBuildSet`, `AllRoomsBuilt`, `HaveBeenAttacked`, `CallToArmsInUse` | one bit each |
| bits 8–13 | `gtDirection`, `DefendDirection`, `OpenDirection` | 2 bits each; the direction the site search walks; rooms with id < 6 use the defend direction, others the open one |
| bits 14–17 / 18–21 | `CurrentEventTask`, `NumberOfEventTasks` | a 3-slot ring of event tasks |
| bits 22–25 / 26–29 | `Task`, `NextTask` | the task machine (§5.2) |
| bit 30 / 31 | `TrapsStartFights`, `TimesOut` | both set **on** by the defaults and never cleared by anything read |
| `+0x004` | `TaskTimeCounter` | ticks since the current task began |
| `+0x008` | `AttackingPlayerId` | last player whose creatures were found near an attack |
| `+0x012` | `MaxBuildArea` (4×i32) | from block `0x92` as four u16 (min x, min y, max x, max y); the personality setter uses the whole map |
| `+0x022` | `EventTask[3]` | 1 = respond to attack |
| `+0x02E` | `GoldDeposits[4]` | x, y, and a size word — the four nearest gold seams (§5.5) |
| `+0x04E` | `CallToArmsPos` | x, y, z in 1/4096 tile |
| `+0x05A` | `RespondToAttack[3]` | threat needed (signed), x, y |
| `+0x072`–`+0x095` | `DigRoomAt`, `DigRoomAtCurrent`, `WallHugStart/End`, `JoinPathStart/End`, `WallHugDistance/Heading`, `DigDX/DY`, `InnerLoopXOrY`, `MaxX/MaxY`, `SquaresTarget/Current` | rectangle-walk state for digging and placing |
| `+0x096` | `Flags` (16 bits) | the editor's feature toggles: bit0 build rooms, bit1 dig gold, bit2 research, bit3 respond to attack, bit4 create imps, bit5 slap. Loaded from block byte `0x7A` and then **overwritten with `0xFFFF`** by both loaders — all six always on |
| `+0x098`/`+0x099` | `CurrentRoomToBuild`, `NextRoomToBuild` | room ids; `0x80` set means "expand an existing one"; `0xE5` (229) means "dig an empty room-shaped area" |
| `+0x09A` | `CurrentGoldArea` | index into `GoldDeposits` |
| `+0x09B`/`+0x09D` | `PlayersAttackedFlags`, `FirstAttackFlag` | bit per player |
| `+0x09F` | `EnemyBreaches[16]` | per enemy: 8 stored tunnels (start, end) and `TunnelsLeft`; 65 bytes each |
| `+0x4EF`/`+0x4F3`/`+0x4F7` | `CallToArmsCounter`, `CannotAttackCounter`, `UntagGoldCounter` | countdowns, decremented once per tick |

---

## 3. Data sources

### 3.1 The PLAYERS record (`KwdRecordType.Players = 180`) — CONFIRMED layout, STRONGLY INFERRED semantics

Lives in `Data/editor/maps/<Level>Players.kld`, one chunk per level. **All 66
files divide exactly: `contentSize / itemCount = 205` in every one** (item counts
3–7). 274 players, 121 with the AI flag set — 99 of those are the `Good Player`
and `Neutral Player` rows every level carries, which the player tick skips before
the AI runs, leaving **22 real AI keepers on 12 levels**.

Record layout (205 bytes), framing as the binary's loader reads it; this agrees
with OpenKeeper's parser (GPLv3, `KwdFile.readPlayers`, cited as a cross-check —
their loader's pointer sits 0x20 bytes before the record in the binary's frame):

| Offset | Width | Field |
|---|---|---|
| `0x00` | i32 | starting gold — passed to the player's money routine at load |
| `0x04` | i32 | AI flag — non-zero creates the computer player |
| `0x08` | 158 | the **AI parameter block**, handed whole to the unpacker |
| `0xA6` | u16 | trigger id |
| `0xA8` | u8 | player id (1 good, 2 neutral, 3+ keepers — matches `PlayerIds`) |
| `0xA9` | 2×u16 | starting camera x, y (used only for the local player) |
| `0xAD` | 32 | name |

The block's byte→field map is §2.2's *Block* column. Everything the unpacker
reads is listed there; what it **does not read** is CONFIRMED-from-data to be
junk in the files: bytes `0x05–0x07`, `0x3A–0x41` (OpenKeeper's "require min
level" / "require threat" ints), `0x84–0x85`, `0x89`, `0x8F`, `0x91` hold ASCII
fragments (`s`, `\l`, `ye`, `ar`, `re`…) in most records — uninitialised editor
memory. **Block byte 0** — OpenKeeper's `aiType` — is not read by the unpacker
either; in the campaign it takes values 0, 2, 4, 5, 6, 8, so it is editor metadata
that the retail game ignores (STRONGLY INFERRED). Block `0x8A` (i32, always 100)
is added to the player's **mana** at load.

The 22 keepers, with the parameters that separate them (full table in the
research working file; all CONFIRMED from data):

| Level | Keeper | Gold | Speed | Room size | Imps / tiles-per-imp | Min army / level / threat % | Never / only-attackers | Money | Prisoners |
|---|---|---|---|---|---|---|---|---|---|
| Level7 | Gormagon | 2000 | 1 | 9 | 4 / 40 | 3 / 1 / 1 | 0 / 0 | 1000 | torture… |
| Level10 | Asmodeus | 10000 | 1 | 9 | 6 / 30 | 30 / 10 / 100 | 0 / 0 | 1 | pit |
| level11A/B/C | Belial, Drako, Morgana, Raksha, Kronos | 10000–15000 | 1 (Raksha 4) | 9/16 | 10 / 1 | 2–20 / 1–3 / 30–150 | Raksha 1 / 0 | 5000–7000 | leave/pit |
| level12 | Malachai | 16000 | 2 | 9 | 8 / 30 | 19 / 1 / 0 | 0 / 0 | 20000 | leave |
| Level13 | Malleus | 16000 | 1 | 16 | 20 / 25 | 1 / 1 / 1 | 0 / 0 | 1000 | leave |
| Level17 | Harkan, Carrion | 10000 | 1 | 25/16 | 4 / (garbage) | 15 / 4–5 / 1 | 0 / 0 | 2000 | pit |
| level18 | Nemesis, Fabian, Faust | 3000–10000 | 1 | 25/9 | 12/4 / 30 | 26,4,0 / 4,1,4 / 125,1,170 | 0,1,1 / 0 | 3000/1000 | torture/leave |
| TrapTime | Keeper 2, Keeper 3 | 3000 | 1 | 12/25 | 4/20 / 15 | 4,15 / 0,10 / 200,20 | 1,0 / 1,0 | 6000/9000 | leave/torture |

Constant across all 121 AI rows: `ManaValue` 100, `GoldValue` 100, starting mana
100, `RemoveCallToArmsWithinRange` mostly 5. `MaxBuildArea` is per level (50
distinct rectangles).

### 3.2 The nine built-in personalities — STRONGLY INFERRED (values CONFIRMED from the executable's data)

The personality setter fills the struct from **seven parallel constant tables of
nine rows** in `.rdata` — one table per editor tab, exactly the grouping the
dump's labels imply:

| Table | Address | Stride | Fields (in order) |
|---|---|---|---|
| rooms | `0x0066DF18` | 22 | MinRandomTime, CreateEmpty(i32), BiggerLair(i32), DefaultRoomSize, TilesLeftBetweenRooms, SellTakenOver(i32), PlaceNextToIdeal, BuildLostRoomAfter, CorridorStyle, BuildNewRoomWhenMoreSpace, NeutralRangeFromHeart, NeutralRangeFromClaimed, Flexibility |
| traps/doors | `0x0066DFE0` | 12 | AggressiveVsDefensive, TrapVsDoor, DoorPreference, BoulderLongCorridors(i32), BoulderBreachRoute(i32), TrapAndDoorUsage |
| initial attack | `0x0066E050` | 31 | MinCreatures, SkillLevel, (i32 unread), ThreatSuperiority, (i32 unread), AllRoomsPlaced(i32), AllSpellsResearched(i32), FirstEnd, FirstStart, BreachAtXPoints, UseCreatures, OnlyAttackAttackers(i32), NeverAttack(i32) |
| battles | `0x0066E168` | 17 | BAThreatSuperiority, SpellAtkVsDef, CallToArmsValue(u16), ImprisonPercentage, RemoveIfCantReinforce(i32), RetreatHealth, RemoveCTAAfter(u16), RemoveCTAEnemy, RemoveCTARange, RemoveCTAOwn, WaitBeforeAttackingAgain |
| creatures | `0x0066E208` | 19 | UnhappyHappy(i32), AngryHappy(i32), AngryDispose(i32), AngryDisposalMethod, UnwantedDispose(i32), MaximumImps |
| spells | `0x0066E2B8` | 8 | UseSightOfEvil, UseKeeperSpells, LightningInWater(i32), SpellPreference, CallToArmsUsage |
| misc | `0x0066E300` | 18 | MoveForResearch, TendancyMoney(u16), SpecialRange, SpecialSearchChance, SpecialFindChance, TilesPerImpRatio, ImprisonedAction, StartingMana(i32), TendancySpeed, Openness, ManaValue(u16), GoldValue(u16) |

The nine rows, named by index using OpenKeeper's `AIType` enum (GPLv3; the names
are theirs, the values are the executable's):

| Parameter | Master Keeper | Conqueror | Psychotic | Stalwart | Greyman | Idiot | Guardian | Thick Skinned | Paranoid |
|---|---|---|---|---|---|---|---|---|---|
| DefaultRoomSize | 25 | 16 | 9 | 25 | 16 | 12 | 25 | 16 | 12 |
| TilesLeftBetweenRooms | 2 | 1 | 0 | 1 | 1 | 1 | 1 | 1 | 1 |
| CorridorStyle | 1 | 0 | 0 | 1 | 0 | 2 | 2 | 2 | 0 |
| NeutralRoomRangeFromHeart | 25 | 20 | 40 | 25 | 20 | 15 | 15 | 14 | 13 |
| TrapAndDoorUsage % | 99 | 70 | 30 | 99 | 50 | 25 | 99 | 50 | 25 |
| DoorPreference | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 2 | 1 |
| IAMinimumCreatures | 15 | 10 | 5 | 15 | 15 | 15 | 4 | 4 | 4 |
| IACreatureSkillLevel | 8 | 5 | 0 | 8 | 5 | 2 | 0 | 0 | 0 |
| IAThreatSuperiority % | 20 | 125 | 0 | 100 | 100 | 100 | 200 | 200 | 200 |
| IAFirstEnd / IAFirstStart | 2/3 | 0/3 | 3/2 | 2/0 | 1/1 | 3/2 | 2/0 | 1/3 | 3/2 |
| BreachAtXPoints | 2 | 1 | 1 | 2 | 1 | 1 | 1 | 1 | 1 |
| IAOnlyAttackAttackers | 0 | 0 | 0 | 0 | 0 | 1 | 1 | 1 | 1 |
| NeverAttack | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 1 |
| BAThreatSuperiority % | 125 | 100 | 50 | 125 | 110 | 90 | 150 | 150 | 150 |
| BACallToArmsValue | 2000 | 1500 | 1000 | 2150 | 2000 | 2500 | 2000 | 2500 | 3500 |
| ImprisonPercentage | 5 | 5 | 0 | 15 | 8 | 25 | 15 | 20 | 25 |
| BARetreatHealth % | 10 | 10 | 10 | 20 | 20 | 20 | 30 | 30 | 30 |
| RemoveCTA enemy / own | 1/10 | 1/5 | 1/1 | 2/10 | 2/10 | 2/10 | 5/4 | 5/4 | 5/4 |
| Unhappy→happy / Angry→happy / Angry→dispose | 1/1/0 | 0/1/0 | 0/0/1 | 1/1/0 | 0/1/0 | 0/0/1 | 1/1/0 | 0/1/0 | 0/0/1 |
| AngryDisposalMethod (1 = sacrifice) | 1 | 1 | 0 | 1 | 1 | 0 | 1 | 1 | 0 |
| MaximumImps | 20 | 15 | 10 | 15 | 10 | 5 | 12 | 8 | 4 |
| UseSightOfEvil | 2 | 1 | 2 | 2 | 1 | 2 | 2 | 1 | 1 |
| LightningInWater | 1 | 0 | 0 | 1 | 0 | 0 | 1 | 0 | 0 |
| CallToArmsUsage | 1 | 1 | 2 | 0 | 0 | 2 | 0 | 1 | 2 |
| MoveForResearch | 0 | 1 | 2 | 0 | 1 | 2 | 0 | 1 | 2 |
| TendancyMoney | 9000 | 6000 | 6000 | 9000 | 6000 | 6000 | 9000 | 6000 | 6000 |
| TilesPerImpRatio | 15 | 25 | 35 | 15 | 20 | 25 | 15 | 15 | 15 |
| ImprisonedCreatureAction | 0 | 2 | 1 | 0 | 2 | 1 | 0 | 3 | 1 |
| Openness % | 50 | 50 | 50 | 20 | 20 | 20 | 15 | 15 | 15 |

Every row: `TendancySpeed` 1, `UseKeeperSpells` 2, `SpellPreference` 2, both trap
preferences 2, `RemoveCallToArmsAfter` 240, `WaitBeforeAttackingAgain` 90,
`RemoveCallToArmsWithinRange` 5, `AllRoomsPlaced`/`AllSpellsResearched` 0,
`SpecialSearchChance` 0, `SpecialFindChance` 90/50/20 repeating, `SpecialRange`
10/8/15/8/5/2/5/4/3, starting mana 100, `ManaValue`/`GoldValue` 100. The full
decode is reproducible from the seven addresses above.

Two more things the setter does: it builds `RoomList` **not from a table** but as
"every room type in the world's definition order, skipping ids 3 (Portal) and 25
(Hero Portal) and any type with a flag byte bit set" — the first 15; and it
consults a **level-name remap table** at `0x006A30A0` (entries of a name pointer, a
replacement personality, and a 10-terminated list of personalities to replace):

| Map | Replaced personalities → replacement |
|---|---|
| Spiral, Pass | 0, 3, 4 → 1 (Conqueror) |
| Caverns | 0, 1, 3, 4, 6, 7 → 2 (Psychotic) |
| Clover | 0, 1, 3, 6, 7 → 2 |
| Olympia | 2 → 0 |
| OnSlaught | 2–8 → 0 (Master Keeper) |
| Siege | 5, 7, 8 → 0 |
| Frosty's Castle | 4, 6, 8 → 0 |
| Frosty's Lair | 4, 5 → 0 |
| Frosty's Realm, Furnace | 6 → 0 |
| No Mans Land | 4, 6 → 0 |

Why those substitutions exist is UNKNOWN (HYPOTHESIS: personalities that could not
win, or could not be beaten, on those maps in testing).

### 3.3 Defaults at creation (`0x004F8360`) — STRONGLY INFERRED

Everything zero except: `TendancySpeed` 1, `DefaultRoomSize` 9, `MaximumImps` 10,
`TilesPerImpRatio` 50, `IAThreatSuperiority` 100, `TilesLeftBetweenRooms` 1,
`BuildNewRoomWhenMoreSpaceRequired` 1, `ProbabilityOfMovingCreatureForResearch`
2, `IACreatureSkillLevel` 1, `IAAllRoomsPlaced` 1, `IAAllSpellsResearched` 1;
`TimesOut` and `TrapsStartFights` **on**; `Flags` 0 (then `0xFFFF` from either
loader).

---

## 4. What the AI can do — its outbound interface — STRONGLY INFERRED

It is a **separate actor issuing the same commands a human issues**. Everything
it does goes through: the player's hand (take thing, drop thing, "is hand
holding"), the generic player-action handler with an action id (40 = tag a tile
for digging, 41 = untag, 62 = slap, 63 = remove Call to Arms, 88 = pick up a
Special), room placement (with the world's "can this room go here" check first),
trap and door placement, keeper-spell casting at a position or on a thing (spell
1 Sight of Evil, 3 Call to Arms, 4 Create Imp, 13 Create Gold — ids CONFIRMED
from `KeeperSpells.kwd`, whose id byte is at record offset `0xEE`), pathfinding
queries, the room list, the dungeon heart position, and a per-player RNG
(`random(n)`, 31 call sites). It reads other players' creature lists directly.
It never writes creature state except one bit: the per-keeper **"imprison me"**
mask on enemy creatures (§5.4).

---

## 5. The decision loop

Depth reached: the tick, the message handler, the threat evaluation, all nine
task states and all maintenance steps have been read; of the 58 functions, 46
have been read in full, 12 small helpers only by role (§7). Everything in this
section is STRONGLY INFERRED unless marked.

### 5.1 One tick (`0x00500C20`)

`budget ← TendancySpeed`; every action costs one; when it hits zero the rest of
the tick is skipped (the counters still run).

1. **Bail** if the owning player's status is 1 or 3.
2. `TaskTimeCounter++`. If `TimesOut` and the counter exceeds **1,680** ticks, the
   counter resets and `Task`/`NextTask` are cleared to 0 — the task is abandoned.
3. **`UntagGoldCounter`**: if non-zero, decrement; on reaching zero, every entry of
   the player's task-list 2 is untagged (action 41); if an untag fails the counter
   is set to 1 to retry next tick.
4. `CannotAttackCounter--`, `CallToArmsCounter--` (each if non-zero).
5. **Message handler** (§5.3).
6. **Event tasks**: while budget and the current slot's `EventTask` is 1 → run
   **respond-to-attack** (§5.4).
7. If budget: if `PlayersAttackedFlags` → **attack management** (`0x4FECE0`:
   place a Call to Arms at the front, or check for its removal); if
   `CallToArmsInUse` → **CTA removal test** (`0x4FEFC0`); then **retreat wounded**
   (`0x4FF260`).
8. **Collect specials** (`0x500520`, no budget): the first Special object on own
   land is activated (action 88). Specials are object ids 117–129 (and 8) —
   CONFIRMED ids from `Objects.kwd`.
9. `random(100) < 50` and budget and army not ready-to-attack → **training
   moves** (`0x4FF5C0`): a creature within a level-up (experience above ¾ of its
   next threshold) or at level ≥ `TrainingRoomMaxExperienceLevel` (balance
   variable 126, global `0x006F28FC`) and below `IACreatureSkillLevel` goes to the
   Combat Pit (16); others go to the Training Room (7). Needs the room and health
   > ¾.
10. If budget: `random(100) < 10` → **traps and doors** (`0x5000A0`, §5.6).
11. If budget and `Flags` bit 4: **imps**. If `FightGoingOn` is clear: when
    `imps < MaximumImps` **and** `imps < ownedTiles / TilesPerImpRatio`, and Create
    Imp is available, cast it at the heart ± a random ±2-tile offset (budget −1).
    If `FightGoingOn`: the **battle step** instead (`0x503060`, §5.7).
12. If `random(3) ≥ ProbabilityOfMovingCreatureForResearch` and budget and `Flags`
    bit 2: if any keeper spell is in the "researchable" state and a Library (6)
    exists → **research move** (`0x4FA8D0`, §7 caveat). Then, unless an event task
    is pending, **job placement** (`0x503690`): drop what the hand holds into a
    room matching one of the creature's jobs; at 10 % pick up a working creature
    for the same.
13. If budget and `Flags` bit 5: **slap** (`0x502F00`): a working creature above
    half health, not recently slapped, 1 % per creature per tick (action 62).
14. If budget: **prisoners** (`0x5006D0`, §5.8); if that returned budget and
    `UseSightOfEvil` ≠ 0: **Sight of Evil** (`0x5003A0`): a random map tile; mode
    1 casts at 2 %/tick anywhere, mode 2 at 20 %/tick only on enemy-owned tiles.
15. **The task machine** (§5.2), looped while budget remains.

### 5.2 The task machine — `Task` values

| Task | Handler | What it does |
|---|---|---|
| **0** | `0x4FAEF0` then `0x4FC690` | **Decide.** First the attack planner (§5.5). If it declines and `Flags` bit 0: the **room planner** (§5.5). Then, if no task was started and `Flags` bit 1 and `gold < TendancyMoney` and no gold-dig task is pending on the task stack → **gold step** (`0x4FD590`) |
| **1** | `0x4FCE70` | **Find a site** for `CurrentRoomToBuild`: `0x5012A0` searches from `DigRoomAtCurrent` in the current direction for a `DefaultRoomSize` square (§5.5). Success → Task 2. Failure → rotate the search direction; after the fourth, 95 % chance to re-seed from a random own room, else give up (Task 0) |
| **2** | `0x4FD030` | **Dig the rectangle**: tag every tile (action 40), `UntagGoldCounter ← 240`, provided the task stack has more than 20 free entries. If the site is not connected, first detour: `Task ← 7` with `NextTask ← 2` to dig a corridor from `JoinPathStart`. Done → Task 3 (or Task 0 for the `0xE5` placeholder) |
| **3** | `0x4FD2B0` | **Place the room** tile by tile over the rectangle (one tile per budget unit); re-tag any tile still solid; done → Task 4 |
| **4** | (inline) | **Done**: budget −1, gold check as in Task 0, reset to Task 0 |
| **5** | `0x4FD660` | **Route to gold**: `DigRoomAtCurrent ←` nearest own room to the deposit (or the heart); `Task ← 7`, `NextTask ← 6` |
| **6** | `0x4FD750` | **Mine the seam**: tag gold-bearing solid tiles in an expanding square spiral around the deposit until radius > 6, then Task 0 |
| **7** | `0x4FDC50` | **Dig a path** from `DigRoomAtCurrent` to `DigRoomAt`: step toward the target (`0x502230`, the wall-hugging line walker); solid tiles are tagged (action 40, `UntagGoldCounter ← 240`, needs > 51 free task entries); water gets a Wooden Bridge (8), lava a Stone Bridge (17) if available; when the path becomes walkable → `Task ← NextTask`; if it reaches **enemy-owned** land during an attack, the remainder is stored in that enemy's breach slots and the task ends; outside `MaxBuildArea` → Task 4 |
| **8** | — | stop for this tick |
| 9–15 | — | invalid: reset to Task 4 |

### 5.3 The message handler (`0x4FA400`)

Walks the player's message queue (56-byte messages; type at `+3`, two data
words at `+0x1F` and `+0x23`, a handled bit at `+0x28`):

| Type | Reaction |
|---|---|
| 0, 13, 22, 26 | ignored (left in the queue) |
| 1, 6, 12, 47 | if `Flags` bit 3: **threat evaluation** at a map position (§5.4); sets `FightGoingOn` |
| 34 | same, at a thing's position (`0x4FA000`) |
| 17 | creature unhappy: if `UnhappyCreaturesHappy` → **make happy** (`0x4F9710`) |
| 18 | creature angry: `AngryCreaturesDisposeOf` → **dispose** (`0x4F9520`); else `AngryCreaturesHappy` → make happy |
| 32 | room taken over: if the room was a Portal (3) or Hero Portal (25) and `AutomaticallyBuildBiggerLairWhenPortalTakenOver` → schedule a **Lair (2)**, expanding an existing one when `BuildNewRoomWhenMoreSpaceRequired` = 0, a new one when 1, nothing when 2 |
| 35 | (portal-related; clears a pending Portal build) then re-run room choice |
| 41 | room became available: schedule it as `NextRoomToBuild` |
| 42 | room needs more space: apply `BuildNewRoomWhenMoreSpaceRequired` — except for the Treasury (1) |
| anything else | marked handled |

**Make happy** reads the creature's grievance code: 0/4/5 → **pay it directly**
(its level-scaled wage is taken from the treasury, minted as a gold object and
dropped on the creature); 1 → drop a **chicken** from the Hatchery on it; 2 →
drop it in the **Lair**; 3 → drop it in a room matching one of its jobs.
**Dispose** drops it in the Temple (13) when `AngryDisposalMethod` = 1, otherwise
at a Portal/Hero Portal (the creature leaves).

### 5.4 Threat, imprisonment, and responding (`0x4F9B90`, `0x4FA000`, `0x4FE0B0`)

- Bail if three event tasks are already queued (the message stays unhandled).
- If the keeper owns a **Prison (11)**: for **every enemy creature**, roll
  `random(100) < ImprisonPercentage`; set or clear this keeper's bit in the
  creature's "imprison me" mask accordingly. So imprisonment is a per-creature
  coin toss re-rolled on every combat message, and the mask is what the creature
  side reads when a fight ends (the creature side is not this brief's).
- `enemyThreat` = sum of level-scaled threat of enemy creatures within 10 tiles
  of the position (radius 5 for the thing variant), plus armed traps when
  `TrapsStartFights`; records `AttackingPlayerId`, sets `HaveBeenAttacked`.
  `allyThreat` = own and allied creatures within 4 tiles.
- Drop tile: nearest own, passable, unblocked tile within ±5.
- `needed = BAThreatSuperiority% × enemyThreat − allyThreat`. With a drop tile:
  `needed < 0` → nothing to do; else queue **respond-to-attack(needed, tile)**.
  Without one: if `BARemoveIfCantReinforce` and `needed ≥ 1` → queue
  **respond-to-attack(−allyThreat, position)** — a withdrawal.
- **Respond-to-attack** (one action per budget unit): positive → pick own creatures
  above half health, not fighting or in certain states, and drop them at the tile
  (or the nearest own tile found by a spiral search), subtracting each one's
  threat until ≤ 0; negative → pick own creatures within 10 tiles of the fight
  and drop them beside the heart, adding their threat until ≥ 0. No candidate →
  the event is dropped. Creature threat = descriptor threat × a per-level
  percentage table.

### 5.5 Planning: attack, rooms, gold

**Attack planner** (`0x4FAEF0`, Task 0 first step):
- Refuse if `NeverAttack`, `CannotAttackCounter > 0`, or `CallToArmsInUse`.
- **Readiness** (`0x4FAB10`): more than `IAMinimumCreatures` non-imps **and** all
  of them at level ≥ `IACreatureSkillLevel`; under-levelled creatures are nudged
  toward training when they are few.
- If `IAAllRoomsPlaced`, also require `AllRoomsBuilt`. If `IAAllSpellsResearched`
  and any spell is still researchable, push a research move instead and refuse.
- Target: with `IAOnlyAttackAttackers`, only the recorded attacker (then the flags
  are cleared); otherwise the **nearest enemy heart** (skipping ids 1 and 2 and
  finished players) whose total army threat × `IAThreatSuperiority`% we exceed.
- If that enemy's `TunnelsLeft` is 0 and a stored tunnel exists → resume it
  (`Task 7`, `NextTask 4`), set `CannotAttackCounter ← WaitBeforeAttackingAgain × 4`.
- First attack on a player (`FirstAttackFlag`): end point by `IAFirstEnd` — 0 the
  enemy **Dungeon Heart (5)**, 1 a random enemy room, 2 the enemy room with the
  least threat within 5 tiles, 3 the most; start point by `IAFirstStart` — 0 own
  room furthest from own heart, 1 random own room, 2 own heart, 3 own room
  nearest the target. Later attacks: random enemy room, random own room.
- Set `Task 7 / NextTask 4`, `TunnelsLeft--`, remember the target in
  `BlibBlubBlob`'s low nibble; when `CallToArmsUsage` = 0 and breaches are used
  up and no CTA is active, place `CallToArmsPos` two tiles off the heart along a
  checked route.

**Room planner** (`0x4FC690`, when the attack planner declines):
- **Room choice** (`0x4F93B0`): first candidate in `RoomList[0..14]` then ids 16–26
  that is available and **not yet owned**, never Wooden/Stone/Hero bridges (8, 17,
  22), Heart (5), Guard Room (9) or Portal (3). None → the same walk for rooms
  that are researchable-but-unavailable, yielding the `0xE5` "empty room shape"
  placeholder. Still none → `NextRoomToBuild ← 3` and **`AllRoomsBuilt`** set.
- Portal/Hero Portal: claim the nearest **neutral** one within `MaxBuildArea` if
  it is within `NeutralRoomRangeFromHeart` of the heart, or within
  `NeutralRoomRangeFromClaimedArea × 2 + 3` of own land → `Task 7 / NextTask 4`.
  Any other room: the same for a **neutral room of that type** first.
- Cost: `roomCostPerTile × DefaultRoomSize > gold` → gold step (Task 5 via the
  nearest known deposit) instead of building.
- Otherwise Task 1 with a direction (rooms id < 6 use `DefendDirection`, others
  `OpenDirection ± random`) and an origin: the **ideal-neighbour table** at
  `0x006A3088` unless `PlaceRoomsNextToIdeal` = 1 (random own room) —
  Treasury→Heart, Lair→Hatchery, Hatchery→Lair, Library→Lair, Training→Heart,
  Workshop→Heart, Prison→Training, Torture→Prison, Temple→Torture,
  Graveyard→Torture, Casino→Treasury, Combat Pit→Prison; rooms with no ideal
  (Portal, Bridges, Guard Room, Heart) use a random own room, avoiding bridges.
- If `NextRoomToBuild` has the `0x80` "expand" bit but the room cannot be
  expanded (`0x4FBE00`), the bit is cleared and a new room is built.

**Site finder** (`0x5012A0`): walk from the origin in the direction (a 4-entry
delta table at `0x006A32A8`) up to 2×side tiles past tagged or room tiles; land on
solid diggable rock; skip `TilesLeftBetweenRooms` more solid tiles; the side×side
square must be all solid, diggable, unowned rock; count the perimeter tiles that
are already open (tagged or clear) and reject if more than `Openness`% of the
perimeter is open; if side² < `DefaultRoomSize`, try one extra strip; validate
against `MaxBuildArea`. Side = ⌊√DefaultRoomSize⌋.

**Gold** (`0x4FD590`, `0x501C10`): the scan flood-fills gold-bearing tiles into
deposits, takes each deposit's centroid, and keeps the **four nearest** to the
keeper's rooms in `GoldDeposits`. The gold step takes `GoldDeposits[CurrentGoldArea]`
→ Task 5; with none known and **Create Gold** available, it casts Create Gold one
tile off the heart.

### 5.6 Traps and doors (`0x5000A0`, `0x4FFD80`)

Needs a Workshop (10) and `random(100) < TrapAndDoorUsage`. The bounding box of
all own rooms is grown by `random(10) − 2` tiles, clamped to the map, and its
four edges are walked; each valid site (`0x4FFA50`, `0x4FFC40` — unread, role from
call graph) gets one placement per budget unit. Placement: `random(4) <
TrapVsDoorPreference` → a **door**: iterate door types available (skipping a
flagged kind); `DoorPreference` 0 = keep scanning to the best, 1 = stop at the
first (worst), 2 = 50 % stop each. Otherwise a **trap**: type 2 is the boulder —
with `BoulderTrapsOnLongCorridors` only where a straight run of 4 valid tiles
exists in some direction, else at `random(4) > AggressiveVsDefensiveTrapPreference`;
other traps split by a per-type "aggressive" byte against
`AggressiveVsDefensiveTrapPreference` (0 → all aggressive, 4 → all defensive).

### 5.7 Battle step (`0x503060`) and Call to Arms

Recomputes `FightGoingOn` from creatures in the fighting state. For a fighter: if
enemy threat within 12 tiles exceeds `BACallToArmsValue`, CTA is affordable, the
tile is not lava, the game tick is a multiple of 40, and a route from the heart
exists → cast **Call to Arms** there; `CallToArmsCounter ← RemoveCallToArmsAfter`,
`CallToArmsInUse`. Otherwise, with `random(4) ≥ UseKeeperSpells`: `random(4) <
AttackingVsDefensiveSpellPreference` chooses a **supportive** spell (flag bit 3 in
the spell descriptor) for an own creature under half health within 10 tiles, else
an **offensive** one (flag bit 2) for an enemy within 10 tiles; among available,
un-cooled spells the pick is weighted by cost against `4 − SpellPreference`
(higher preference → dearer spells); 75 % chance to keep the first target found.
**CTA removal** (`0x4FEFC0`): when the counter reaches 0, or fewer than
`RemoveCallToArmsEnemyCreatures` enemies (hearts count) are within
`RemoveCallToArmsWithinRange`, or fewer than `RemoveCallToArmsOwnCreatures` non-imps
exist anywhere → remove it (action 63) and `CannotAttackCounter ←
WaitBeforeAttackingAgain × 4`. **CTA front** (`0x4FE9C0`): for `CallToArmsUsage`
1/2 the position is near the enemy heart; for 0 it **creeps** from the current CTA
toward the enemy heart, advancing one step once ~80 % of the army is within 10
tiles.

### 5.8 Prisoners (`0x5006D0`) and retreat

With a Prison and prisoners: each prisoner is considered with 5 % probability;
one belonging to a non-enemy goes to a **Lair**; an enemy goes by
`ImprisonedCreatureAction`: 0 Torture Chamber (12), 1 nothing, 2 Combat Pit
(16), 3 Temple (13). **Retreat** (`0x4FF260`): a creature below `BARetreatHealth`%
health (or below 50 % when flagged as in a fight) and at least
`IACreatureSkillLevel` is dropped at the Lair, or the heart if none.

### 5.9 Constants worth knowing

| Constant | Where | Meaning |
|---|---|---|
| 1,680 ticks | tick | task timeout (`TimesOut`) |
| 240 ticks | tasks 2, 3, 6, 7 | `UntagGoldCounter` after tagging |
| 300 | many | a creature is not picked up again within 300 ticks of the last pick-up (skipped in the first 300 ticks of the game) |
| 40 | battle step | CTA cast only on ticks divisible by 40 |
| ×4 | attack planner, CTA removal | `WaitBeforeAttackingAgain` is in units of 4 ticks |
| 10 / 12 / 5 / 4 tiles | threat radii | attack response / battle CTA / thing-variant & weak-room test / ally radius |
| 3 | event ring | at most three pending event tasks |
| 8 × 16 | breaches | stored tunnels per enemy × enemies |
| 4 | gold | deposits remembered |
| 20 / 51 | task stack | minimum free task entries before tagging (room / corridor) |

---

## 6. Quirks the binary has and an implementation would have to choose about

1. **`Flags` forced to `0xFFFF`.** The editor's six behaviour toggles (build, dig
   gold, research, respond, imps, slap) are loaded and then overwritten; the
   campaign files carry garbage there anyway. STRONGLY INFERRED (two loaders).
2. **Nine dead parameters** (§2.2) including `Flexibility`, `GoldValue`,
   `ManaValue`. The editor exposes them; the retail game does not read them.
3. **An off-by-one in room choice's second pass**: it indexes `RoomList[1..15]`
   instead of `[0..14]`, reading one byte past the list — the `SpecialFindChance`
   byte — as a room id. Harmless in shipped data (that byte is 0–90; ids above 26
   fail the availability test) but it means `SpecialFindChance` *is* read, as a
   room id. STRONGLY INFERRED.
4. **The campaign `aiType` byte is not consumed** at load; the 158-byte block's
   values are, so the editor's dropdown only pre-filled them. STRONGLY INFERRED.
5. **Editor u16, game u8** at `TilesPerImpRatio`, `SpecialRange`,
   `WaitBeforeAttackingAgain`; the high byte is ignored. Two campaign keepers
   (Harkan, Carrion) thereby get `TilesPerImpRatio` 232 — one imp per 232 tiles.
6. **`IAFirstStart` = 255 for Malleus** masks to 3 (nearest own room to the target).
7. **`IAAllSpellsResearched` = 2** in 72 records reads as 0 (only bit 0 is kept).
8. Two loaders, one behaviour: the personality setter and the record unpacker set
   the same fields in the same order — a campaign keeper with a given block and a
   skirmish personality with the same values behave identically except for
   `RoomList` (computed vs listed) and `MaxBuildArea` (whole map vs record).

---

## 7. UNKNOWN — explicitly

- **What a "player tick" is in wall-clock or game-tick terms** for these counters
  (1,680; 240; 300; 40). The project's Q1 on the main loop owns this.
- **Player status values 1 and 3** — which is won, which is lost, and whether
  there are others.
- **Message type meanings** beyond what the AI does with them (1, 6, 12, 34, 47
  are combat-shaped; 17/18 unhappy/angry; 32/35/41/42 room events). The senders
  are outside this unit.
- **The research move** (`0x4FA8D0`): it requires fewer than two creatures already
  researching, picks a non-researching creature with the *lowest* value of a
  descriptor byte, requires a Portal or Hero Portal, compares two Entrance-file
  functions, and drops the creature **at the portal room's position**. Whether
  that is how research is started in DK2 (the creature then walks to the library)
  or a misread of which room pointer is used is UNKNOWN.
- **The job→room table** (`JobRooms` in the dump): it lives in zero-initialised
  `.data` at `0x006ED490` and is built at runtime; its contents were not read.
- **12 helper functions read by role only**: `0x4F8150`, `0x4F85F0`, `0x4F86B0`,
  `0x4F8710` (small; the last is called from `Player.cpp`), `0x4FA7C0` (room-full
  test), `0x4FADF0` (own room furthest from heart), `0x4FBD80`/`0x4FBDB0`,
  `0x4FBE00` (can a room expand), `0x4FC3B0` (own tile within range), `0x4FDB90`
  (enemy-land test), `0x4FFA50`/`0x4FFC40` (trap-site validity), `0x502230`/
  `0x5027D0` (the wall-hugging line walker), `0x5039F0` (checksum).
- **Why the skirmish maps remap personalities** (§3.2).
- **Whether the four "no consumer" bit-fields are truly dead** (weaker method).
- **The garbage-byte bug's effect in play**: whether Harkan/Carrion visibly starve
  for imps on Level17.
- **Everything the creature side does with the "imprison me" mask** and with
  dropped creatures — that is the primary session's territory.

---

## 8. Questions for the owner — one sentence each from watching the game

1. Does the AI keeper on Level 10 (Asmodeus) ever attack you, or only when you
   attack it? (`IAOnlyAttackAttackers` = 0 there, `IAMinimumCreatures` 30 at level
   10 — a threshold that may never be met.)
2. Have you ever seen the AI keeper on Level 17 run out of imps? (Predicted from
   the 232 garbage value: at most 4 imps, one per 232 tiles.)
3. When an AI keeper is attacked, do its creatures get *dropped* at the fight
   (they appear from above) rather than walking there? (Predicted: yes.)
4. Does an AI keeper ever pay creatures by dropping gold on them, or feed one a
   chicken by hand? (Predicted: yes — that is how it handles unhappiness.)
5. Does an AI keeper slap its creatures? (Predicted: rarely, about 1 % per working
   creature per tick.)
6. In skirmish, does choosing the same personality on "Spiral" and on another map
   feel different? (The remap table says Spiral silently plays Conqueror for three
   personalities.)
7. Does the AI keeper's Call to Arms visibly walk toward your dungeon heart in
   steps? (Predicted for `CallToArmsUsage` 0.)
8. Have you seen an AI keeper build a bridge across water or lava while tunnelling
   toward you?
9. Does an AI keeper pick up and re-drop its creatures in the library when a spell
   is left to research? (Settles the §7 research-move question.)

---

## 9. What Flame confirmed, and what it could not tell

**Confirmed, and saved time:** the struct size (1,273) and every one of its 75
named fields agree with the binary's own dump; its per-class membership tags
identified 51 of the 58 region functions before a single one was read, and named
seven usefully (the tick, the message handler, the threat evaluation, the
respond-to-attack routine, the checksum, the dump, the personality setter); its
cross-reference map found the two creation paths and the world-level loader in one
grep; it names `CPlayer`'s `computer player on` byte and AI pointer, `money`,
`mana`, `dungeon area`, `status`, `task stack`, and the three profiling globals.

**Could not tell:** the 45 parameter bit-fields (it sees two opaque words), the
16 runtime bits, what any of the 51 functions *does*, the seven personality
tables (it saw one of the seven as a 22-byte struct and missed the other six), the
level-name remap, the ideal-neighbour table, the PLAYERS block layout, and the
names of 34 of the 46 functions the AI calls outside its unit. Measured: **12 of 46
outbound callees named**, none of the task handlers named. So the brief's one-line
summary held — parameters were a lookup, procedure was a Ghidra job — with one
correction: the parameter *names* came from the binary's own strings, which are
better than Flame's (`TendancyMoney` is EA's spelling; Flame has it too) and can be
used in the repository.

---

## 10. Side findings for other sessions (facts only; nothing written in `docs/research/`)

- `RoomTypeId.unk_2` is the **Lair** by behaviour as well as by name: the AI
  schedules room id 2 after taking a portal under a flag literally named "build
  bigger lair". Independent of the `Rooms.kwd` name read.
- **`KeeperSpells.kwd` id byte is at record offset `0xEE`** (ids 1 Sight of Evil, 2
  Possession, 3 Call to Arms, 4 Create Imp, 5 Summon Horny, 6 Thunderbolt, 7
  Turncoat, 8 Tremor, 9 Inferno, 10 Heal, 11 Chicken, 13 Create Gold, 14
  OneShotReaper — no 12). Q55 lists this as unidentified.
- `TrainingRoomMaxExperienceLevel` (balance 126, = 4) is read by the AI: creatures
  at or above it are sent to the Combat Pit instead of the Training Room.
- The retail build **profiles the AI tick** into three globals; the player tick
  wraps it with two timer reads.
- Player-action ids seen: 40 tag, 41 untag, 62 slap, 63 remove CTA, 88 pick up
  Special. Player message types: see §5.3.
- Object ids 117–129 are the thirteen Specials (Increase Level … Locate Hidden
  Land); the AI also matches id 8 (Torture Wheel) in the same switch, which looks
  like a leftover.
- The PLAYERS record parses cleanly at 205 bytes and nothing in the repo reads it
  yet; a `KwdPlayers` reader would be a small, validated addition.

---

## 11. Method, for whoever picks this up

Everything reproducible from this branch's gitignored `reverse-engineering/`
folder while the worktree exists: `ghidra/phase0*.txt` (the assert-string map,
region function list, coverage gaps, displacement scan, call graph),
`ghidra/decomp/*.c` (the 66 exported functions, quarantined), `ghidra/dump-*.txt`
(the offset→label map extracted from the dump routine), `ghidra/personality-*.txt`,
`players-sweep.txt`, and `ghidra/scripts/` (the four Jython scripts and the
Python sweeps). The Ghidra copy is paired to this worktree and is removed by its
`TEARDOWN.ps1`.

Two environment facts that cost time: in this Git Bash, `grep -b` and `dd` report
file offsets **0x100 higher** than the real ones for `DKII.EXE` (text-mode
translation), while Python's `read()` is exact — use Python for raw reads; and the
PE sections map as `.rdata` VA = offset + `0x401000`, `.data` VA = offset +
`0x401A00`, so a single delta is wrong. In Jython, iterate functions with
`getFunctions(True)` and filter by address; the address-seeded iterator returned
nothing here.
