# The computer keeper — what `DKII.EXE` does when it plays against you

Research reference for the AI keeper: where it lives in the binary, every parameter
it carries and where each comes from, and the decision loop, read to the depth stated
in §5. §12 is the design for a `KeeperAI` in this codebase and the state of what is
built.

Nothing here is decompiler output and nothing here is a Flame identifier; every
function is referred to by its address and by a description in our own words
(ADR-0046). Where the running game has not been watched, a binary-derived fact is
capped at **STRONGLY INFERRED**; **CONFIRMED** below means *confirmed from shipped
data*, never from the decompiler.

The binary's own vocabulary is "Computer Player" (`ComputerPlayer.cpp`, and a debug
dump that prints `Computer Player` as its header); the field names used below are
EA's, read from that dump's format strings, not anyone's reconstruction. The name in
this codebase is `KeeperAI`.

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
| Code range | `0x004F8150` – `0x00504D5F`. 58 Ghidra functions; 14 of them carry the assert string (30 references, found identically by Ghidra's reference model and by a raw scan for the address word), spanning `0x004F9B90`–`0x00503690`. Plus **three code blocks Ghidra never made functions of**: a 338-byte static initialiser at `0x004F8180` that fills the job→room table (§2.3), and two tiny predicates at `0x00502E90`/`0x00502EF0` reached only through that table. Flame lists all three as functions, so its member count for the unit is 53 |
| Neighbours | Below: `Workshop.cpp` (last of the room files, references to `0x004F4F9F`). Above: the task-stack and world-dump code, then `World.cpp` from `0x0050B5B0`. The **alphabetical-bracketing rule does not hold across the binary**: the room files are one block, the creature states another, and `ComputerPlayer.cpp` sits between the rooms and `World.cpp`. Note the two path spellings — room files are `...\source\game\`, this one is `...\Source\Game\` — which is the build's object-list order, not the alphabet |
| Class shape | **Not polymorphic.** RTTI names `CPlayer` and the interface classes; there is no computer-player class name. The AI object is a plain 1,273-byte struct owned by each `CPlayer` through a pointer at `CPlayer+0x1101`, next to a one-byte `computer player on` switch at `CPlayer+0x10FA` |
| Source size | The RNG helper is called with `__FILE__`/`__LINE__`; the largest line number seen is 7,543 (`0x1D77`), so the unit was at least that long |
| Source lines named by asserts | Fourteen functions; the rest of the unit has no anchor and is attributed by contiguity, which Flame independently agrees with (§9) |

### 1.2 Creation, and the two ways it gets its parameters — STRONGLY INFERRED

Every `CPlayer` constructs an AI object in its own init (`0x004B8640` calls the
constructor `0x004F82E0` and the two-pointer init `0x004F8330`). Whether it ever
*runs* is the `computer player on` byte. Two routines turn it on:

1. **Campaign / level data.** The world's level loader calls `0x0051F0B0` once per
   PLAYERS record. If the record's `AI` int is non-zero it switches the player to AI
   (`0x004BB800`), copies the record's **158-byte parameter block** and hands it to
   the unpacker `0x004F8750` (§3.1). It also sets the player's name from the record,
   the trigger id, and — for the local human — the starting camera.
2. **Skirmish / multiplayer lobby.** A world routine at `0x0050C800`, in game modes 2
   and 3, reads a per-slot lobby setting for every player with id ≥ 4: `0xFF` leaves
   the slot human, `9` initialises the slot as *no player*, and any other value
   switches the slot to AI and calls the **personality setter** `0x004F8C60` with that
   value (§3.2) — one of **nine built-in personalities**.

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

The AI is ticked **every player tick, with no timer of its own**. Its
`TaskTimeCounter` is incremented once per call and is used only as a task *timeout*
(§5.1). All pacing inside comes from a per-tick **action budget** and from
probabilistic steps. The status values 1 and 3 that stop it are the ones the player
struct stores when a keeper is out of the game (§10 decodes them).

---

## 2. The parameter surface

### 2.1 Where the names come from

The struct's debug dump at `0x00503F50` prints **121 labelled fields** in EA's own
names, each read from a fixed offset — which is how every offset below was obtained
without trusting a third-party layout. Flame's layout of the same struct (75 named
fields) agrees on every offset it names; the dump additionally names the **45
bit-fields** packed into two 32-bit words that Flame left as opaque flags, and the 16
runtime bits of the first flags word.

### 2.2 Tuning parameters — one row per field

*Offset* is into the 1,273-byte AI struct. *Block* is the byte offset into the
158-byte PLAYERS block (§3.1). *Table* is the personality table column (§3.2). *Read
by* lists the functions that read the field at runtime, found by an instruction-level
scan of memory-operand displacements across the whole region (the loaders, the dump
and the checksum excluded); for bit-fields the consumer was found by matching the mask
in the decompiled text, a weaker method. *Range* is across the **23 real AI keepers**
in the 66 shipped levels (§3.1); the Good/Neutral rows are excluded because the player
tick never reaches the AI for them.

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
| `+0x4C5` | `SpecialSearchChance` | u8 | `0x9B` | misc+4 | **nobody** | 0, 1 | 0 |
| `+0x4C6` | `RoomList[15]` | 15×u8 | `0x1B` | computed (§3.2) | `4F93B0` | 8 distinct orders; default `1,2,3,4,6,7,8,…,16` | 0 |
| `+0x4D5` | `SpecialFindChance` | u8 | `0x9C` | misc+5 | `4F93B0` (accidental read on the second pass; §6.3) | 0–90 | 0 |
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

**Bit-fields in the first parameter word `+0x4B2`** (all loaded from the block and
from the tables):

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
| 19 | `UnwantedCreatureDisposeOf` | `0x74` i32 | creatures+13 | none found | 0/1 |
| 20–21 | `UseSightOfEvil` | `0x80` | spells+0 | tick, `5003A0` | 0–2 |
| 22 | `LightningInWater` | `0x7C` i32 | spells+2 | none found | 0/1 |
| 23–25 | `SpellPreference` | `0x82` | spells+6 | `503060` | 0–4 |
| 26–27 | `CallToArmsUsage` | `0x83` | spells+7 | `4FAEF0`, `4FE9C0`, `4FECE0` | 0–2 |
| 28–29 | `ImprisonedCreatureAction` | `0x9D` | misc+7 | `5006D0` | 0–2 |
| 30 | `NeverAttack` | `0x4E` i32 | attack+27 | `4FAEF0` | 0/1 |

**Nine parameters are loaded but never read** — `TendancyType`,
`MinimumRandomTimeBeforePlacingReseachedRoom`, `SpecialSearchChance`, `Flexibility`,
`BuildLostRoomAfterMinimumTime`, `SpecialRange`, `IAUseCreatures`, `GoldValue`,
`ManaValue` — by both the displacement scan and the decompiled-text scan. STRONGLY
INFERRED with one caveat: a read through a pointer that already carries part of the
offset would escape both methods. Four bit-fields
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

Two things the dump prints that are **not** in the struct:

- **`JobRooms[10]`** is a **global** table at `0x006ED490`, ten 17-byte entries indexed
  by creature job type, filled once by a function-less initialiser (`0x004F8180`):
  `{ u8 roomId; fn* mayPlace; u32; u32; i32 unused = −1 }`. Only four jobs map to a
  room — **3 Research → Library (6)**, **4 Train → Training Room (7)**, **5
  Manufacture → Workshop (10)**, **7 Torture → Torture Chamber (12)** (job ids
  CONFIRMED against this repository's `JobType`). The predicates: Research runs the
  make-room-for-a-researcher step (§5.1 step 12) and returns true, Train returns "army
  not ready to attack", Manufacture always true, **Torture always false** — so the AI
  never drops a creature into the Torture Chamber for work. Sleep, Eat, Guard, Pray
  and Drink have no room. Consumers: job placement (`0x503690`) and the "make happy"
  grievance 3 path (`0x4F9710`).
- The whole 1,273-byte struct is **saved and loaded as one block** (`0x4F8710` writes
  it, `0x4F86B0` reads it and then restores the two pointers at `+0x0A` and `+0x0E`)
  — see `docs/research/sav-format.md`.

---

## 3. Data sources

### 3.1 The PLAYERS record (`KwdRecordType.Players = 180`) — CONFIRMED layout, STRONGLY INFERRED semantics

Lives in `Data/editor/maps/<Level>Players.kld`, one chunk per level. **All 66 files
divide exactly: `contentSize / itemCount = 205` in every one** (item counts 3–7). 274
players, 121 with the AI flag set; 98 of those are the `Good Player` and `Neutral
Player` rows every level carries, which the player tick skips before the AI runs,
leaving **23 real AI keepers on 10 levels**. Parsed by `KwdPlayers`; the three counts
are pinned by `DK2.Data.Tests.KwdPlayersTests`.

Record layout (205 bytes), framing as the binary's loader reads it; this agrees with
OpenKeeper's parser (GPLv3, `KwdFile.readPlayers`, cited as a cross-check — their
loader's pointer sits 0x20 bytes before the record in the binary's frame):

| Offset | Width | Field |
|---|---|---|
| `0x00` | i32 | starting gold — passed to the player's money routine at load |
| `0x04` | i32 | AI flag — non-zero creates the computer player |
| `0x08` | 158 | the **AI parameter block**, handed whole to the unpacker |
| `0xA6` | u16 | trigger id |
| `0xA8` | u8 | player id (1 good, 2 neutral, 3+ keepers — matches `PlayerIds`) |
| `0xA9` | 2×u16 | starting camera x, y (used only for the local player) |
| `0xAD` | 32 | name |

The block's byte→field map is §2.2's *Block* column. Everything the unpacker reads is
listed there; what it **does not read** is CONFIRMED-from-data to be junk in the
files: bytes `0x05–0x07`, `0x3A–0x41` (OpenKeeper's "require min level" / "require
threat" ints), `0x84–0x85`, `0x89`, `0x8F`, `0x91` hold ASCII fragments (`s`, `\l`,
`ye`, `ar`, `re`…) in most records — uninitialised editor memory. **Block byte 0** —
OpenKeeper's `aiType` — is not read by the unpacker either; in the campaign it takes
values 0, 2, 4, 5, 6, 8, so it is editor metadata that the retail game ignores
(STRONGLY INFERRED). Block `0x8A` (i32, always 100) is added to the player's **mana**
at load.

The 23 keepers, with the parameters that separate them (all CONFIRMED from data):

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

**Gormagon never plays.** The owner (2026-09-17): Level 7's keeper dies within the
first seconds and the player takes over its rooms. Its own root (trigger 3, condition
47) only reacts to the death; what deals it is UNKNOWN — the hero at (55,36) rooted on
trigger 29 (creature condition 3 → creature actions 20 and 42) is the candidate. So the
first campaign keeper that plays is **Asmodeus on Level 10**; Level 13, Level 17 and
TrapTime also author neither action anywhere.

Constant across all 121 AI rows: `ManaValue` 100, `GoldValue` 100, starting mana 100,
`RemoveCallToArmsWithinRange` mostly 5. `MaxBuildArea` is per level (50 distinct
rectangles).

### 3.2 The nine built-in personalities — STRONGLY INFERRED (values CONFIRMED from the executable's data)

The personality setter fills the struct from **seven parallel constant tables of nine
rows** in `.rdata` — one table per editor tab, exactly the grouping the dump's labels
imply:

| Table | Address | Stride | Fields (in order) |
|---|---|---|---|
| rooms | `0x0066DF18` | 22 | MinRandomTime, CreateEmpty(i32), BiggerLair(i32), DefaultRoomSize, TilesLeftBetweenRooms, SellTakenOver(i32), PlaceNextToIdeal, BuildLostRoomAfter, CorridorStyle, BuildNewRoomWhenMoreSpace, NeutralRangeFromHeart, NeutralRangeFromClaimed, Flexibility |
| traps/doors | `0x0066DFE0` | 12 | AggressiveVsDefensive, TrapVsDoor, DoorPreference, BoulderLongCorridors(i32), BoulderBreachRoute(i32), TrapAndDoorUsage |
| initial attack | `0x0066E050` | 31 | MinCreatures, SkillLevel, (i32 unread), ThreatSuperiority, (i32 unread), AllRoomsPlaced(i32), AllSpellsResearched(i32), FirstEnd, FirstStart, BreachAtXPoints, UseCreatures, OnlyAttackAttackers(i32), NeverAttack(i32) |
| battles | `0x0066E168` | 17 | BAThreatSuperiority, SpellAtkVsDef, CallToArmsValue(u16), ImprisonPercentage, RemoveIfCantReinforce(i32), RetreatHealth, RemoveCTAAfter(u16), RemoveCTAEnemy, RemoveCTARange, RemoveCTAOwn, WaitBeforeAttackingAgain |
| creatures | `0x0066E208` | 19 | UnhappyHappy(i32), AngryHappy(i32), AngryDispose(i32), AngryDisposalMethod, UnwantedDispose(i32), MaximumImps |
| spells | `0x0066E2B8` | 8 | UseSightOfEvil, UseKeeperSpells, LightningInWater(i32), SpellPreference, CallToArmsUsage |
| misc | `0x0066E300` | 18 | MoveForResearch, TendancyMoney(u16), SpecialRange, SpecialSearchChance, SpecialFindChance, TilesPerImpRatio, ImprisonedAction, StartingMana(i32), TendancySpeed, Openness, ManaValue(u16), GoldValue(u16) |

The nine rows, named by index using OpenKeeper's `AIType` enum (GPLv3; the names are
theirs, the values are the executable's):

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
10/8/15/8/5/2/5/4/3, starting mana 100, `ManaValue`/`GoldValue` 100. The full decode
is reproducible from the seven addresses above.

The setter builds `RoomList` from the world's definition enumeration: the first
15 entries with room-descriptor `+0x43D` bit 7 **set**, **or** id 3 (Portal), **or**
id 25 (Hero Portal). All other entries are skipped. The inclusion branches and
15-entry bound were independently checked at `0x004F8E8B..0x004F8EDE`
(2026-09-28); interpreting those jumps as exclusions reverses the predicate.
The setter also caps attack skill at the byte at `0x006F2976`, initializes each
enemy's breach count, sets build bounds to `(1,1,width-1,height-1)` and forces
`Flags = 0xFFFF`, as the campaign loader does.

Before selecting table rows, it consults the **level-name remap table** at
`0x006A30A0`: 40-byte entries containing a name pointer, replacement personality,
and up to eight dword personality ids terminated by 10. The map-name comparison
is case-sensitive and stops at `End Of List`. The following bytes and the
selection loop in `0x004F8C60` were independently rechecked on 2026-09-28:

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
`BuildNewRoomWhenMoreSpaceRequired` 1, `ProbabilityOfMovingCreatureForResearch` 2,
`IACreatureSkillLevel` 1, `IAAllRoomsPlaced` 1, `IAAllSpellsResearched` 1; `TimesOut`
and `TrapsStartFights` **on**; `Flags` 0 (then `0xFFFF` from either loader).

---

## 4. What the AI can do — its outbound interface — STRONGLY INFERRED

It is a **separate actor issuing the same commands a human issues**. Everything it
does goes through: the player's hand (take thing, drop thing, "is hand holding"), the
generic player-action handler with an action id (40 = tag a tile for digging, 41 =
untag, 62 = slap, 63 = remove Call to Arms, 88 = pick up a Special), room placement
(with the world's "can this room go here" check first), trap and door placement,
keeper-spell casting at a position or on a thing (spell 1 Sight of Evil, 3 Call to
Arms, 4 Create Imp, 13 Create Gold — ids CONFIRMED from `KeeperSpells.kwd`, whose id
byte is at record offset `0xEE`), pathfinding queries, the room list, the dungeon
heart position, and a per-player RNG (`random(n)`, 31 call sites). It reads other
players' creature lists directly. It never writes creature state except one bit: the
per-keeper **"imprison me"** mask on enemy creatures (§5.4).

---

## 5. The decision loop

Depth reached: the tick, the message handler, the threat evaluation, all nine task
states and all maintenance steps have been read; **all 58 functions and the three
function-less blocks have been read in full**, plus 26 of the helpers the unit calls
outside itself (pick up, drop, hand test, room finder, enemy/ally tests, RNG,
task-list lookups, portal capacity, the tile predicates). Everything in this section
is STRONGLY INFERRED unless marked.

### 5.1 One tick (`0x00500C20`)

`budget ← TendancySpeed`; every action costs one; when it hits zero the rest of the
tick is skipped (the counters still run).

1. **Bail** if the owning player's status is 1 or 3.
2. `TaskTimeCounter++`. If `TimesOut` and the counter exceeds **1,680** ticks, the
   counter resets and `Task`/`NextTask` are cleared to 0 — the task is abandoned.
3. **`UntagGoldCounter`**: if non-zero, decrement; on reaching zero, every entry of
   the player's task-list 2 is untagged (action 41); if an untag fails the counter is
   set to 1 to retry next tick.
4. `CannotAttackCounter--`, `CallToArmsCounter--` (each if non-zero).
5. **Message handler** (§5.3).
6. **Event tasks**: while budget and the current slot's `EventTask` is 1 → run
   **respond-to-attack** (§5.4).
7. If budget: if `PlayersAttackedFlags` → **attack management** (`0x4FECE0`: place a
   Call to Arms at the front, or check for its removal); if `CallToArmsInUse` → **CTA
   removal test** (`0x4FEFC0`); then **retreat wounded** (`0x4FF260`).
8. **Collect specials** (`0x500520`, no budget): the last eligible Special encountered
   in the owned-object list is activated (action 88). The loop overwrites its saved
   tag on each match (`0x00500520`); list order in play remains to be checked.
   Specials are object ids 117–129 (and 8) — CONFIRMED ids
   from `Objects.kwd`.
9. `random(100) < 50` and budget and army not ready-to-attack → **training moves**
   (`0x4FF5C0`): a creature within a level-up (experience above ¾ of its next
   threshold) or at level ≥ `TrainingRoomMaxExperienceLevel` (balance variable 126,
   global `0x006F28FC`) and below `IACreatureSkillLevel` goes to the Combat Pit (16);
   others go to the Training Room (7). Each needs its destination room; the
   health > ¾ gate is on the Combat Pit branch only (`0x004FF5C0`).
10. If budget: `random(100) < 10` → **traps and doors** (`0x5000A0`, §5.6).
11. If budget and `Flags` bit 4: **imps**. If `FightGoingOn` is clear: when
    `imps < MaximumImps` **and** `imps < ownedTiles / TilesPerImpRatio`, and Create Imp
    is available, cast it at the heart ± a random ±2-tile offset (budget −1). If
    `FightGoingOn`: the **battle step** instead (`0x503060`, §5.7).
12. If `random(3) ≥ ProbabilityOfMovingCreatureForResearch` and budget and `Flags`
    bit 2: if any keeper spell is in state 2 (read as "not yet researched") and a
    Library (6) exists → **make room for a researcher** (`0x4FA8D0`). This is *not* a
    move into the library. It counts own creatures whose descriptor job list contains
    Research (job 3); if **fewer than two** can research, it takes the non-researcher
    with the lowest value of an unnamed descriptor byte (`+0x787`, §7) and — only when
    the dungeon is **at its portal creature cap** (`first-portal count + (portals − 1)
    × per-extra-portal count` ≤ creatures flagged as counting) and the creature is not
    in hand, not in a handful of states, and not picked up within the last 300 ticks —
    picks it up and **drops it at a Portal or Hero Portal**, so it leaves and frees a
    slot for a creature that can research. Then, unless an event task is pending,
    **job placement** (`0x503690`): drop what the hand holds into a room matching one
    of the creature's jobs through the `JobRooms` table (§2.3); at 10 % pick up a
    working creature for the same.
13. If budget and `Flags` bit 5: **slap** (`0x502F00`): a working creature above half
    health, not recently slapped, 1 % per creature per tick (action 62). The
    "recently slapped" guard is the AI's own pacing of its hand; a *human* keeper has
    no slap cooldown in DK2 (§12.5).
14. If budget: **prisoners** (`0x5006D0`, §5.8); if that returned budget and
    `UseSightOfEvil` ≠ 0: **Sight of Evil** (`0x5003A0`): a random map tile; mode 1
    casts at 2 %/tick anywhere, mode 2 at 20 %/tick only on enemy-owned tiles.
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
| **7** | `0x4FDC50` | **Dig a path** from `DigRoomAtCurrent` to `DigRoomAt`: step toward the target with the line walker below; tiles it returns are tagged (action 40, `UntagGoldCounter ← 240`, needs > 51 free task entries); water gets a Wooden Bridge (8), lava a Stone Bridge (17) if available; when the path becomes walkable → `Task ← NextTask`; if it reaches **enemy-owned** land (any of the 8 neighbours or the tile itself owned by an enemy, `0x4FDB90`) during an attack, the remainder is stored in that enemy's breach slots and the task ends; outside `MaxBuildArea` → Task 4 |
| **8** | — | stop for this tick |
| 9–15 | — | invalid: reset to Task 4 |

**The line walker** (`0x502230`, `0x5027D0`): from `DigRoomAtCurrent` step one tile at
a time along whichever axis is further from `DigRoomAt`. The first tile met that is
solid, water or lava is returned to the caller when it is **diggable** (terrain Solid
and not Impenetrable, `0x453D10`), water or lava. An **undiggable** obstacle is walked
around: the straight line is continued through it to find the exit tile
(`WallHugEnd`), both hugging directions are trial-walked (up to 24 turns each,
`0x5027D0`) and the one that gets closer to the target wins (`WallHugLeft`); then, in
`WallHug` mode, the walker follows the obstacle's edge using a clockwise N/E/S/W table
(`0x0069A070`), returning any diggable rock it meets (so it digs through where it can)
until it reaches the exit tile and resumes the straight walk; arriving back at
`WallHugStart` fails the task.

### 5.3 The message handler (`0x4FA400`)

Walks the global retained message queue filtered by player, skipping handled
records. Layout, producers, duplicate suppression and expiration are audited in
[`computer-keeper-messages.md`](research/computer-keeper-messages.md).

| Type | Reaction |
|---|---|
| 0, 13, 22, 26 | ignored (left in the queue) |
| 1, 6, 12, 47 | if `Flags` bit 3: **threat evaluation** at a map position (§5.4); sets `FightGoingOn` |
| 34 | same, at the attacked room's position (`0x4FA000`) |
| 17 | creature unhappy: if `UnhappyCreaturesHappy` → **make happy** (`0x4F9710`) |
| 18 | creature angry: `AngryCreaturesDisposeOf` → **dispose** (`0x4F9520`); else `AngryCreaturesHappy` → make happy |
| 32 | room captured from a non-neutral owner: if Portal (3) or Hero Portal (25) and `AutomaticallyBuildBiggerLairWhenPortalTakenOver` → schedule a **Lair (2)**, expanding an existing one when `BuildNewRoomWhenMoreSpaceRequired` = 0, a new one when 1, nothing when 2. Neutral acquisition sends 33, handled by the default branch |
| 35 | room lost: clear a pending Portal build, then re-run room choice |
| 41 | room needed: schedule it as `NextRoomToBuild` if available |
| 42 | room needs more space: first attempt to untag all gold-dig tags, for **every room type**, then apply `BuildNewRoomWhenMoreSpaceRequired`. A failed untag on still-gold terrain suppresses Treasury expansion and marks the message handled; other room types continue (message audit §3) |
| anything else | marked handled |

Only three parameter bit-fields are read here (`UnhappyCreaturesHappy`,
`AngryCreaturesHappy`, `AngryCreaturesDisposeOf`). The instruction-level scan had also
credited `UnwantedCreatureDisposeOf` and `LightningInWater` to this function; the
decompiled data flow does not read them, so both are **probably dead** (§7).

**Make happy** reads the creature's grievance code: 0/4/5 → **pay it directly** (its
level-scaled wage is taken from the treasury, minted as a gold object and dropped on
the creature); 1 → drop a **chicken** from the Hatchery on it; 2 → drop it in the
**Lair**; 3 → drop it in a room matching one of its jobs. **Dispose** drops it in the
Temple (13) when `AngryDisposalMethod` = 1, otherwise at a Portal/Hero Portal (the
creature leaves).

### 5.4 Threat, imprisonment, and responding (`0x4F9B90`, `0x4FA000`, `0x4FE0B0`)

- Bail if three event tasks are already queued (the message stays unhandled).
- If the keeper owns a **Prison (11)**: for **every enemy creature**, roll
  `random(100) < ImprisonPercentage`; set or clear this keeper's bit in the creature's
  "imprison me" mask accordingly. Imprisonment is a per-creature coin toss re-rolled
  on every combat message, and the mask is what the creature side reads when a fight
  ends.
- `enemyThreat` = sum of level-scaled threat of enemy creatures within 10 tiles of the
  position (radius 5 for the thing variant), plus armed traps when `TrapsStartFights`;
  records `AttackingPlayerId`, sets `HaveBeenAttacked`. `allyThreat` = own and allied
  creatures within 4 tiles for position messages, 5 for the thing variant.
- Drop tile: nearest own, passable, unblocked tile within ±5.
- `needed = BAThreatSuperiority% × enemyThreat − allyThreat`. With a drop tile:
  `needed < 0` → nothing to do; else queue **respond-to-attack(needed, tile)**. Without
  one: if `BARemoveIfCantReinforce` and `needed ≥ 1` → queue
  **respond-to-attack(−allyThreat, position)** — a withdrawal.
- **Respond-to-attack** (one action per budget unit): positive → pick own creatures
  above half health, not fighting or in certain states, and drop them at the tile (or
  the nearest own tile found by a spiral search), subtracting each one's threat until
  ≤ 0; negative → pick own creatures within five tiles of the fight and drop them beside
  the heart, adding their threat until ≥ 0. No candidate → the event is dropped.
  Creature threat (`0x4FBDB0`) = the descriptor's threat value at level 1, otherwise
  threat × a global per-level percentage table (`i16[level]` at `+0x105` of the table
  pointed to by `0x006E4154`) / 100.
- **Enemy** (`0x4BF390`) means not this player and the alliance byte for that player is
  0; **ally** (`0x4BF3D0`) is the complement. Players 1 and 2 are skipped by the attack
  planner, not by these tests.

### 5.5 Planning: attack, rooms, gold

**Attack planner** (`0x4FAEF0`, Task 0 first step):

- Refuse if `NeverAttack`, `CannotAttackCounter > 0`, or `CallToArmsInUse`.
- **Readiness** (`0x4FAB10`): more than `IAMinimumCreatures` non-imps **and** all of
  them at level ≥ `IACreatureSkillLevel`. If the under-levelled count is no greater
  than that level threshold and is less than the army size, the helper calls the
  creature's ordinary level-up routine `0x0048B120` once for **each** under-levelled
  creature before the final readiness test (`0x004FABC4`). This grants levels
  directly; it does not send them to training. STRONGLY INFERRED from the binary.
- If `IAAllRoomsPlaced`, also require `AllRoomsBuilt`. If `IAAllSpellsResearched` and
  any spell is still researchable, push a research move instead and refuse.
- Target: with `IAOnlyAttackAttackers`, only the recorded attacker (then the flags are
  cleared); otherwise the **nearest enemy heart** (skipping ids 1 and 2 and finished
  players) whose total army threat × `IAThreatSuperiority`% we exceed.
- If that enemy's `TunnelsLeft` is 0 and a stored tunnel exists → resume it (`Task 7`,
  `NextTask 4`), set `CannotAttackCounter ← WaitBeforeAttackingAgain × 4`.
- First attack on a player (`FirstAttackFlag`): end point by `IAFirstEnd` — 0 the enemy
  **Dungeon Heart (5)**, 1 a random enemy room, 2 the enemy room with the least threat
  within 5 tiles, 3 the most; start point by `IAFirstStart` — 0 own room furthest from
  own heart, 1 random own room, 2 own heart, 3 own room nearest the target. Later
  attacks: random enemy room, random own room.
- Set `Task 7 / NextTask 4`, `TunnelsLeft--`, remember the target in `BlibBlubBlob`'s
  low nibble; when `CallToArmsUsage` = 0 and breaches are used up and no CTA is active,
  place `CallToArmsPos` two tiles off the heart along a checked route.

**Room planner** (`0x4FC690`, when the attack planner declines):

- **Room choice** (`0x4F93B0`): first candidate in `RoomList[0..14]` then ids 16–26
  that is available and **not yet owned**, never Wooden/Stone/Hero bridges (8, 17, 22),
  Heart (5), Guard Room (9) or Portal (3). None → the same walk for rooms that are
  researchable-but-unavailable, yielding the `0xE5` "empty room shape" placeholder.
  Still none → `NextRoomToBuild ← 3` and **`AllRoomsBuilt`** set.
- Portal/Hero Portal: claim the nearest **neutral** one within `MaxBuildArea` if it is
  within `NeutralRoomRangeFromHeart` of the heart, or if an owned tile lies on the
  boundary of a square of side `NeutralRoomRangeFromClaimedArea * 2 + 3` centred
  on it. The accepted room starts Task 7 / NextTask 4. Any other room: the same
  for a **neutral room of that type** first (handler research section 3).
- Cost: `roomCostPerTile × DefaultRoomSize > gold` → gold step (Task 5 via the nearest
  known deposit) instead of building.
- Otherwise Task 1 with a direction (rooms id < 6 use `DefendDirection`, others
  `OpenDirection ± random`) and an origin: the **ideal-neighbour table** at
  `0x006A3088` unless `PlaceRoomsNextToIdeal` = 1 (random own room) — Treasury→Heart,
  Lair→Hatchery, Hatchery→Lair, Library→Lair, Training→Heart, Workshop→Heart,
  Prison→Training, Torture→Prison, Temple→Torture, Graveyard→Torture, Casino→Treasury,
  Combat Pit→Prison; rooms with no ideal (Portal, Bridges, Guard Room, Heart) use a
  random own room, avoiding bridges.
- If `NextRoomToBuild` has the `0x80` "expand" bit but the room cannot be expanded
  (`0x4FBE00`), the bit is cleared and a new room is built.
- **Expanding** (`0x4FBE00`): the room's tiles are walked to find its bounding box
  (using the tiles with the smallest and largest x+y as corners); the four one-tile
  strips just outside it are tested — every tile diggable rock or own empty floor, at
  most one gap, the tile beyond it in bounds — and a qualifying strip becomes the dig
  rectangle (`Task 2`, then `Task 3` places the room), so a room grows by one row or
  column at a time. Which strip wins when several qualify was not resolved.
- "The Lair", "a Portal", "the Temple" etc. throughout this section mean **a random own
  room of that type** (`0x4C6460` picks uniformly among unflagged rooms of the type
  with the world RNG), not the nearest or the first.
- **Directions** are fixed at creation (`0x4F85F0`): take the heart's offset from the
  map centre along its dominant axis; `DefendDirection` points from the centre toward
  the heart (0 north, 1 west, 2 south, 3 east — the site finder's table order),
  `OpenDirection` is the opposite. So core rooms (id < 6) are sited **behind** the
  heart, toward the map edge, everything else toward the centre.

**Site finder** (`0x5012A0`): walk from the origin in the direction (the 4-entry table
at `0x006A32A8`: N (0,−1), W (−1,0), S (0,+1), E (+1,0), each with the two step signs
the square uses) up to 2×side tiles past tagged or room tiles; land on diggable solid
rock; skip `TilesLeftBetweenRooms` more solid tiles; the side×side square is **centred
on the approach line and extends forward** from the landing tile; each of its tiles
must be diggable rock, own floor without a room, or a terrain whose descriptor byte
`+0x1CB` has bit 0 (§7); **only when `TilesLeftBetweenRooms` is non-zero**, count the
perimeter tiles that are already open (tagged or not solid) and reject if more than
`Openness`% of the perimeter is open — a keeper that builds rooms touching skips the
test entirely; if side² < `DefaultRoomSize`, try one extra strip; validate against
`MaxBuildArea`. Side = ⌊√DefaultRoomSize⌋. The square may overlap the keeper's own
empty floor, which is how a room ends up flush against the corridor that led to it.

**Gold** (`0x4FD590`, `0x501C10`): the scan flood-fills gold-bearing tiles into
deposits, takes each deposit's centroid, and keeps the **four nearest** to the keeper's
rooms in `GoldDeposits`. The gold step takes `GoldDeposits[CurrentGoldArea]` → Task 5;
with none known and **Create Gold** available, it casts Create Gold one tile off the
heart.

### 5.6 Traps and doors (`0x5000A0`, `0x4FFD80`)

Needs a Workshop (10) and `random(100) < TrapAndDoorUsage`. The bounding box of all
own rooms is grown by `random(10) − 2` tiles, clamped to the map, and its four edges
are walked; each valid site gets one placement per budget unit. A **valid site**
(`0x4FFA50`) is interior to the map and checks the centre and opposite
neighbours through world vtable slot `+0x208` (the navigation solid bit;
`tunnelling.md`); adjacent dig tags are excluded. The centre must be open
and opposite sides solid, a corridor tile. The spacing helper (`0x4FFC40`) rejects a site with an own trap or
door at squared distance strictly below nine. Placement: `random(4) <
TrapVsDoorPreference` → a **door**: iterate door types available (skipping a flagged
kind); `DoorPreference` 0 = continue scanning unless `random(100) == 0`, 1 = stop
at the first, 2 = stop when `random(100) ≤ 50` (the inclusive comparison matters).
Otherwise a **trap**: type 2 is the boulder — with
`BoulderTrapsOnLongCorridors` only where a straight run of 4 valid tiles exists in
some direction, else at `random(4) > AggressiveVsDefensiveTrapPreference`; other traps
split by a per-type "aggressive" byte against `AggressiveVsDefensiveTrapPreference`
(0 → all aggressive, 4 → all defensive).

### 5.7 Battle step (`0x503060`) and Call to Arms

Recomputes `FightGoingOn` from creatures in the fighting state. For a fighter: if
enemy threat within 12 tiles exceeds `BACallToArmsValue`, CTA is affordable, the tile
is not lava, the game tick is a multiple of 40, and a route from the heart exists →
cast **Call to Arms** there; `CallToArmsCounter ← RemoveCallToArmsAfter`,
`CallToArmsInUse`. Otherwise, with `random(4) ≥ UseKeeperSpells`: `random(4) <
AttackingVsDefensiveSpellPreference` chooses a **supportive** spell (flag bit 3 in the
spell descriptor) for an own creature under half health within five tiles, else an
**offensive** one (flag bit 2) for an enemy within five tiles; among available, un-cooled
spells the pick is weighted by cost against `4 − SpellPreference` (higher preference →
dearer spells). For each eligible target, `random(100) > 75` stops the scan
(24 %); otherwise the selected target is overwritten and the scan continues.

**CTA removal** (`0x4FEFC0`): when the counter reaches 0, or fewer than
`RemoveCallToArmsEnemyCreatures` enemies (hearts count) are within
`RemoveCallToArmsWithinRange`, or fewer than `RemoveCallToArmsOwnCreatures` non-imps
exist anywhere → remove it (action 63) and `CannotAttackCounter ←
WaitBeforeAttackingAgain × 4`. **CTA front** (`0x4FE9C0`): for `CallToArmsUsage` 1/2
the position is near the enemy heart; for 0 it **creeps** from the current CTA toward
the enemy heart, advancing one step once ~80 % of the army is within 10 tiles.

### 5.8 Prisoners (`0x5006D0`) and retreat

With a Prison and prisoners: each prisoner is considered with 5 % probability; one
belonging to a non-enemy goes to a **Lair**; an enemy goes by
`ImprisonedCreatureAction`: 0 Torture Chamber (12), 1 nothing, 2 Combat Pit (16), 3
Temple (13). **Retreat** (`0x4FF260`): a creature below `BARetreatHealth`% health (or
below 50 % when marked as a Combat Pit fighter) and at least `IACreatureSkillLevel` is dropped
at the Lair, or the heart if none.

### 5.9 Constants worth knowing

| Constant | Where | Meaning |
|---|---|---|
| 1,680 ticks | tick | task timeout (`TimesOut`) |
| 240 ticks | tasks 2, 3, 6, 7 | `UntagGoldCounter` after tagging |
| 300 ticks | make-room step, job placement | a creature is not **picked up** again within 300 ticks of the last pick-up (skipped in the first 300 ticks of the game). This is the pick-up guard, not a slap guard |
| 40 | battle step | CTA cast only on ticks divisible by 40 |
| ×4 | attack planner, CTA removal | `WaitBeforeAttackingAgain` is in units of 4 ticks |
| 10 / 12 / 5 / 4 tiles | threat radii | attack response / battle CTA / thing-variant, weak-room test, battle spell and withdrawal / ally radius |
| 3 | event ring | at most three pending event tasks |
| 8 × 16 | breaches | stored tunnels per enemy × enemies |
| 4 | gold | deposits remembered |
| 20 / 51 | task stack | minimum free task entries before tagging (room / corridor) |
| 3 tiles | traps and doors | minimum spacing from own traps and doors |
| 24 | line walker | turns tried per side when choosing which way round an obstacle |
| 15 + 5×(portals − 1) | make-room step | portal creature cap: balance variable 73 (`0x006F28B4`, 15 in shipped data) plus the adjacent byte per extra portal, whose id is HYPOTHESIS (OpenKeeper lists variable 177 = 5 by that name) |
| 4 ticks = 1 game second = 1 s | everything | the LEVEL record's ticks-per-second field is 4 in all 66 shipped levels, and a tick is **250 ms**: binary-read and stopwatched by the owner (Q1, ADR-0089; `research/balance-variables-in-the-binary.md`). So 1,680 ticks = 7 minutes, 240 = 1 minute, 300 = 75 s, 40 = 10 s. Measured live at 3.93–3.95 ticks/s in steady state |
| `seed = seed × 0x19660D + 0x3C6EF35F`, `rotl(seed, 19) mod n` | RNG | every `random(n)` here draws from the **world's** stream (seed at world `+0x15A0`), so the AI is deterministic given the world state |

---

## 6. Quirks the binary has and an implementation would have to choose about

1. **`Flags` forced to `0xFFFF`.** The editor's six behaviour toggles (build, dig
   gold, research, respond, imps, slap) are loaded and then overwritten; the campaign
   files carry garbage there anyway. STRONGLY INFERRED (two loaders).
2. **Nine dead parameters** (§2.2) including `Flexibility`, `GoldValue`, `ManaValue`.
   The editor exposes them; the retail game does not read them.
3. **An off-by-one in room choice's second pass**: it indexes `RoomList[1..15]`
   instead of `[0..14]`, reading one byte past the list — the `SpecialFindChance` byte
   — as a room id. Harmless in shipped data (that byte is 0–90; ids above 26 fail the
   availability test) but it means `SpecialFindChance` *is* read, as a room id.
   STRONGLY INFERRED.
4. **The campaign `aiType` byte is not consumed** at load; the 158-byte block's values
   are, so the editor's dropdown only pre-filled them. STRONGLY INFERRED.
5. **Editor u16, game u8** at `TilesPerImpRatio`, `SpecialRange`,
   `WaitBeforeAttackingAgain`; the high byte is ignored. Two campaign keepers (Harkan,
   Carrion) thereby get `TilesPerImpRatio` 232 — one imp per 232 tiles.
6. **`IAFirstStart` = 255 for Malleus** masks to 3 (nearest own room to the target).
7. **`IAAllSpellsResearched` = 2** in 72 records reads as 0 (only bit 0 is kept).
8. Two loaders, one behaviour: the personality setter and the record unpacker set the
   same fields in the same order — a campaign keeper with a given block and a skirmish
   personality with the same values behave identically except for `RoomList` (computed
   vs listed) and `MaxBuildArea` (whole map vs record).
9. **Torture is a job with a room but a predicate that always refuses** (§2.3), so the
   job-placement step never uses the Torture Chamber; only the prisoner step does.
   Sleep, Eat, Pray, Guard and Drink have no room mapping at all, so an unhappy
   creature's "drop it in a room for one of its jobs" only ever finds a Library,
   Training Room or Workshop.
10. **The "research" step throws a creature away** rather than moving one to the
    library (§5.1 step 12). An implementation that wants the editor's promise
    ("probability of moving a creature for research") would be inventing behaviour;
    the shipped one frees a portal slot.
11. Room expansion measures a room's bounding box by its smallest and largest x+y
    tiles, which is exact for rectangles and wrong for L-shapes.
12. **`Openness` only bites when there is a gap.** The perimeter test is skipped when
    `TilesLeftBetweenRooms` is 0 (§5.5). Seven campaign keepers combine `Openness` 20
    with a gap of 1–3 (Gormagon is not one; he has 20 and 0), and for them a 3×3 site
    is accepted only when at most 3 of its 16 perimeter tiles are open — a pocket in
    solid rock reached by a one-tile corridor. Where such a keeper's heart stands in
    open floor, every direction is rejected and the site search re-seeds from a random
    own room until something is enclosed enough. Predicted, not observed: those
    keepers build slowly at first. A candidate for §8.

---

## 7. UNKNOWN — explicitly

- **Player status is resolved** in `research/win-and-defeat.md` §§1–2, including
  trigger actions 10/11. The keeper still ticks after status 2; statuses 1/3 stop it.
- **Remaining message producer branches and suppression fields** are open; the
  independently checked senders and retention rules are in
  `research/computer-keeper-messages.md`. In particular 41 means room needed,
  and 47 is emitted by the Turncoat shot process.
- **Which creature stat the make-room step minimises** — the descriptor byte at
  `+0x787` of the in-memory creature descriptor (Flame names neither it nor the job
  count next to it at `+0x78B`). The candidate ordering is therefore known in form,
  not in meaning.
- **Which creatures "count" toward the portal cap** — those with bit 1 of the
  creature's state-flags word (`+0x3E6`) set; the meaning of that bit is the creature
  side's.
- **Spell state 2** is read as "not yet researched" from how both callers use it; the
  keeper-spell state enumeration itself was not read.
- **Terrain descriptor byte `+0x1CB` bit 0**, which the site finder and the expansion
  test accept as buildable alongside diggable rock and own floor.
- **Room-expansion side priority** when more than one strip qualifies.
- **What `0x4F8150` does** — a 41-byte routine in the unit, referenced by nothing the
  AI calls, that hands a word at `+0x66` to a pair of stream helpers.
- **Why the skirmish maps remap personalities** (§3.2).
- **Whether the six "no consumer" bit-fields are truly dead**: the four found by the
  instruction scan (`SellRecentlyTakenOverRoom`, `CorridorStyle`,
  `BoulderTrapOnBreachRoute`, `BreachAtXPoints`) plus `UnwantedCreatureDisposeOf` and
  `LightningInWater`, which the scan credited to the message handler and the decompiled
  data flow does not read (§5.3).
- **The garbage-byte bug's effect in play**: whether Harkan/Carrion visibly starve for
  imps on Level17.
- **Everything the creature side does with the "imprison me" mask** and with dropped
  creatures.

---

## 8. Questions for the owner — one sentence each from watching the game

1. Does the AI keeper on Level 10 (Asmodeus) ever attack you, or only when you attack
   it? (`IAOnlyAttackAttackers` = 0 there, `IAMinimumCreatures` 30 at level 10 — a
   threshold that may never be met.)
2. Have you ever seen the AI keeper on Level 17 run out of imps? (Predicted from the
   232 garbage value: at most 4 imps, one per 232 tiles.)
3. When an AI keeper is attacked, do its creatures get *dropped* at the fight (they
   appear from above) rather than walking there? (Predicted: yes.)
4. Does an AI keeper ever pay creatures by dropping gold on them, or feed one a chicken
   by hand? (Predicted: yes — that is how it handles unhappiness.)
5. Does an AI keeper slap its creatures? (Predicted: rarely, about 1 % per working
   creature per tick.)
6. In skirmish, does choosing the same personality on "Spiral" and on another map feel
   different? (The remap table says Spiral silently plays Conqueror for three
   personalities.)
7. Does the AI keeper's Call to Arms visibly walk toward your dungeon heart in steps?
   (Predicted for `CallToArmsUsage` 0.)
8. Have you seen an AI keeper build a bridge across water or lava while tunnelling
   toward you?
9. When an AI keeper's dungeon is full (its portal cap reached) and it has a Library
   but no researchers, have you seen it pick up a creature and drop it at its own
   portal so the creature leaves? (Predicted: yes — that is what the "research" step
   does. It never carries a creature *into* the library.)
10. Have you ever seen an AI keeper drop a creature into its Torture Chamber to work
    there, as opposed to dropping a prisoner in? (Predicted: never.)

---

## 9. What Flame confirmed, and what it could not tell

**Confirmed:** the struct size (1,273) and every one of its 75 named fields agree with
the binary's own dump; its per-class membership tags identified 51 of the 58 region
functions and named seven usefully (the tick, the message handler, the threat
evaluation, the respond-to-attack routine, the checksum, the dump, the personality
setter); its cross-reference map found the two creation paths and the world-level
loader; it names `CPlayer`'s `computer player on` byte and AI pointer, `money`, `mana`,
`dungeon area`, `status`, `task stack`, and the three profiling globals. It also knew
the three code blocks Ghidra had not made functions of (§1.1). Asked about the 26
outbound helpers by address, it named 13 usefully (pick up / drop / hand test, the room
finder, the ally test, the heart position, the RNG, the action-handler dispatcher, the
task-list head lookup, the two stream read/write helpers, the level and last-dropped
fields on the creature, the status field on the player).

**Could not tell:** the 45 parameter bit-fields (it sees two opaque words), the 16
runtime bits, what any of the 51 functions *does*, the seven personality tables (it saw
one of the seven as a 22-byte struct and missed the other six), the level-name remap,
the ideal-neighbour table, the PLAYERS block layout, and the names of 34 of the 46
functions the AI calls outside its unit — **12 of 46 outbound callees named**, none of
the task handlers. Nor the portal-capacity pair, the enemy test, the "diggable rock"
predicate, the job-list accessor, or the creature-descriptor bytes the make-room step
reads.

So: parameters were a lookup, procedure was a Ghidra job. The parameter *names* came
from the binary's own strings, not from Flame, which is why they can be used in this
repository (`TendancyMoney` is EA's spelling; Flame has it too).

---

## 10. Side findings for other sessions

- `RoomTypeId.unk_2` is the **Lair** by behaviour as well as by name: the AI schedules
  room id 2 after taking a portal under a flag literally named "build bigger lair".
  Independent of the `Rooms.kwd` name read.
- **`KeeperSpells.kwd` id byte is at record offset `0xEE`** (ids 1 Sight of Evil, 2
  Possession, 3 Call to Arms, 4 Create Imp, 5 Summon Horny, 6 Thunderbolt, 7 Turncoat,
  8 Tremor, 9 Inferno, 10 Heal, 11 Chicken, 13 Create Gold, 14 OneShotReaper — no 12).
  Q55 has not taken this: it still records the record's id field as unidentified, and
  `KwdKeeperSpells` keys spells by data-file name.
- `TrainingRoomMaxExperienceLevel` (balance 126, = 4) is read by the AI: creatures at or
  above it are sent to the Combat Pit instead of the Training Room.
- The retail build **profiles the AI tick** into three globals; the player tick wraps it
  with two timer reads.
- Player-action ids seen: 40 tag, 41 untag, 62 slap, 63 remove CTA, 88 pick up Special.
  Player message types: see §5.3.
- Object ids 117–129 are the thirteen Specials (Increase Level … Locate Hidden Land);
  the AI also matches id 8 (Torture Wheel) in the same switch, which looks like a
  leftover.
- The PLAYERS record is read by `KwdPlayers` (`src/DK2.Data/Kwd/KwdPlayers.cs`), loaded
  per level by `GameDatabaseLoader.LoadCampaignPlayers`, and its AI block is decoded
  into `KeeperAIParameters`.
- **The game RNG** (`0x5558D0`) is a 32-bit LCG, `seed × 0x19660D + 0x3C6EF35F`, output
  `rotl(seed, 19) mod n`, with the seed pointer passed by the caller; the world's stream
  lives at world `+0x15A0` and the AI, room finder and heart code all draw from it.
  Called with `__FILE__`/`__LINE__` and optionally logs each draw between two tick
  bounds — a built-in replay-debugging aid.
- **Player status** (`CPlayer+0x19A`, i32): 0 playing, 1 lost to a keeper, 2 won, 3 lost
  otherwise; set only once per player (`0x4BE630`), which also snapshots the
  end-of-level statistics. The heart-destroyed handler is `0x4BB2A0`, called from the
  player tick.
- **Time-remaining announcements** (`0x4B9740`) fire at 1,800 / 1,200 / 600 / 300 / 240
  / 180 / 120 / 60 / 10 … game seconds left, each multiplied by the ticks-per-second
  global — an independent confirmation that that global is the seconds-to-ticks factor.
- **Portal creature cap** (`0x4DC760`) = variable 73 for the first Portal or Hero Portal
  plus the byte after it per additional one; creatures counted by state flag bit 1
  (`0x4DCCE0`).
- **Terrain flag bits used by the AI** match this repository's `TerrainFlags` (Solid 1,
  Impenetrable 2, Taggable 8, Water 0x80, Lava 0x100) read from the in-memory terrain
  descriptor at `+0x17B`.
- **Owner observation, creature side (2026-09-06), not verified here:** the Mistress is
  the one creature that *enjoys* torture. She voluntarily goes to the torture
  contraptions (racks, electric chairs and the like), her health does not drop while she
  is on them, and she joins in the torturing of other creatures being tortured; whether
  her joining in affects the torture process itself is not known to the owner. This is
  creature AI, out of this document's scope. It is consistent with the keeper AI's
  Torture job predicate always refusing (§2.3): the keeper never *places* a creature
  there for work, so the Mistress's torture-room visits come from her own job selection.
- **Task lists**: the player's task stack (`0x508290`) has 15 lists of 36-byte entries;
  a tile "is tagged" when it appears in list 1 or 2, and the AI untags list 2 as a whole
  when `UntagGoldCounter` expires, so list 2 is the gold-tag list.

---

## 11. Method, for whoever picks this up

Everything is reproducible from the gitignored `reverse-engineering/` folder while its
Ghidra project exists: `ghidra/phase0*.txt` (the assert-string map, region function
list, coverage gaps, displacement scan, call graph), `ghidra/decomp/*.c` (the 66
exported functions, quarantined), `ghidra/dump-*.txt` (the offset→label map extracted
from the dump routine), `ghidra/personality-*.txt`, `players-sweep.txt`,
`ghidra/phase2*.txt` (the whole-program scan for the status field and the job-table
references, the orphan-code listing, the helper decompiles), and `ghidra/scripts/` (the
Jython scripts `phase0*.py` and `phase2*.py`, and the Python sweeps).

Three method notes:

- **Ghidra's function list is not the code.** The job→room initialiser (338 bytes) and
  two predicates in this unit had no function record, so a per-function scan misses
  them; the reference manager reported their stores as coming from "no function", which
  is the tell. When a global's writers all come back as `?`, list the instructions in
  that range, create a temporary function on the first orphan instruction and decompile
  it (`phase2b.py`).
- **Prefer the decompiler's data flow to an instruction-pattern scan** for "is this
  bit-field read": the scan produced two false consumers here (§5.3).
- **Raw reads:** in Git Bash, `grep -b` and `dd` report file offsets **0x100 higher**
  than the real ones for `DKII.EXE` (text-mode translation), while Python's `read()` is
  exact. The PE sections map as `.rdata` VA = offset + `0x401000`, `.data` VA = offset +
  `0x401A00`, so a single delta is wrong. In Jython, iterate functions with
  `getFunctions(True)` and filter by address; the address-seeded iterator returned
  nothing here.

---

## 12. `KeeperAI` in this codebase

§12.1–§12.7 are the design. §12.8 is what exists. Names use `KeeperAI` (the owner's
choice), never the reference project's.

### 12.1 Where it lives, and the one decision that matters

**The keeper is an input source, not a simulation system.** It reads `WorldState` after
tick *N* and emits `PlayerCommand`s for its player id into the `InputFrame` of tick
*N+1*, exactly as the human and the harness's scripted scenarios do. Home:
`src/DK2.AI`, which references Core, Data.Model and Simulation.

Why this and not an `ISimulationSystem`:

- **No state-hash change.** `InputFrame.HashInto` already hashes every command, so a
  replay with the same emitted commands reproduces the same game while the AI's own state stays
  outside `GameSimulation.StateHash()`.
- **It is what the original does.** §4: the AI acts only through the player hand and
  the action handler. Every one of its effects is a command a human could issue.
- **It is testable without the simulation.** A harness `Scenario` carries a keeper
  the same way it carries scripted input.

Cost accepted: one tick of latency (the original decides and acts inside the same
player tick). Cost noted, not accepted blindly: the original draws its random numbers
from the **world's** stream (§5.9). Here the keeper gets its own `SimRandom` seeded
from (level seed, player id) so that adding or removing an AI player does not perturb
the simulation's stream. The delay and separate stream are deliberate deviations
(ADR-0244).

### 12.2 Shape

| Type | Role | State |
|---|---|---|
| `KeeperAIParameters` | the 158-byte block, decoded | **exists** (`DK2.Data.Model`, read by `KwdPlayers`) |
| `KeeperAIState` | the runtime half of §2.3 | **exists**: `Task`/`NextTask`, the counters, the dig rectangle and walker state, the gold deposits, the room-to-build fields, the directions. Not carried: the 3-slot event ring, the 16×8 breaches |
| `KeeperAI` | `IReadOnlyList<PlayerCommand> Think(GameDatabase database, WorldState world)` — one player tick of §5.1 with `budget = TendancySpeed` | **exists** |
| `KeeperView` | the read-only look at the world — rooms via `RoomIndex`, heart, owned tiles, gold, imps, terrain predicates, reachability | **exists**; it is the seam a future `IWorldView` would replace |
| `KeeperDirections` | both direction tables and the creation-time defend/open rule (§5.5) | **exists** |
| `SiteFinder` (+ `BuildArea`) | §5.5's site search, including the gap-gated `Openness` test | **exists** |
| `LineWalker` (+ `WalkOutcome`) | §5.2's corridor digger, with wall-hugging and the two-way trial | **exists** |
| `GoldScanner` | §5.5's seam scan and the four nearest deposits | **exists** |
| `RoomPlanner` | §5.5's room choice, ideal neighbours, directions, claimable neutral rooms | **exists** |
| `KeeperAIPersonality` | the nine skirmish rows of §3.2 as typed static data, producing a `KeeperAIParameters` plus the computed `RoomList` | **does not exist**; the values are decoded in `reverse-engineering/` |
| `ThreatEvaluator`, `AttackPlanner`, `TrapPlacer`, `BattleStep` | §5.4, §5.5, §5.6, §5.7 as pure functions over the view | **do not exist** |
| `IWorldView` | a read-only facade the simulation exposes | **does not exist**. Most of what it would carry is in `WorldState`/`RoomIndex`; the rest is §12.4 |

The housekeeping steps of §5.1 (imps, slap, gold untag, the task machine) are private
methods on `KeeperAI` rather than a separate type.

### 12.3 Commands: what exists, what the keeper still needs

| Original action (§4) | Command today | Gap |
|---|---|---|
| 40 tag / 41 untag | `TagTilesCommand` | none |
| pick up / drop creature | `PickUpCreatureCommand`, `DropCommand` | none. There is one drop command, not one per kind, because DK2 has a single drop handler that takes only a position and the hand chooses what comes out (ADR-0075) |
| pick up an object: a chicken, minted gold, a Special | `PickUpObjectCommand`, `ActivateSpecialCommand` | action 88 activates Increase Level (117); other Special effects remain unimplemented (`specials.md`) |
| 62 slap | `SlapCreatureCommand` | none |
| place room tile by tile | one `BuildRoomCommand` per tile | The command lacks DK2's payment-bypass mode when gold falls below one tile's price (handler research section 6). |
| spell 4 Create Imp | `CreateImpCommand` | none |
| spells 1 Sight of Evil, 3 Call to Arms (+63 remove), 13 Create Gold, battle spells | — | **`CastKeeperSpellCommand(spellId, tile, target)`** |
| place trap / door | `PlaceTrapCommand`, `PlaceDoorCommand` | placement exists (ADR-0231); AI site choice remains |
| pay a creature by minting gold on it | — | needs a treasury-withdrawal rule as well as a command |
| claim a neutral room | digging a path to it, then `ConvertRoom` work | none (ADR-0044) |

Every row marked **bold** adds simulation state or a new effect, so each bumps
`GameSimulation.HashVersion` and `WorldSnapshot.FormatVersion` together and wants its
own ADR — the standing rule in `docs/HANDOFF.md`, *Conventions that are easy to violate
by accident*.

### 12.4 Messages: retained simulation state

The keeper's reactive half (§5.3) reads the shared retained queue filtered by player
(`research/computer-keeper-messages.md`).
The simulation's `SimulationEvents` sink is **observation only** and by ADR-0006 must
never drive behaviour. The proposal is typed, hashed and snapshotted message state,
filled by the producing simulation systems, with the verified shared capacity,
duplicate suppression and expiration rules. Reading an observation must not drain
messages. Keeper acknowledgments must pass through commands so `DK2.AI` remains
an input source. The original keeper runs before queue expiry; the observation
must expose zero-timeout combat messages before they disappear (message audit §3).
**No such type exists (2026-09-28).** Until it does, the keeper runs its proactive
half only (dig, build, gold, imps, slap) and does not respond to attacks.

Alliances, player status, and trap and door instances now exist (ADR-0159,
ADR-0162, ADR-0231). Per-player spell availability is stored and can change through
trigger action 6; the research progression that changes it from accrued points is
absent (`WorldState.SetAvailability`, `library-and-research.md`).

### 12.5 Fidelity choices, decided here so they are not decided by accident

Default: **do what the shipped game does**, including its oddities, and log each as a
named quirk the harness can assert on.

- `Flags` forced to `0xFFFF` (§6.1): ignore the file's value. Yes.
- Byte reads of the three editor u16s (§6.5): `KwdPlayers` already does this; the
  keeper uses the byte. Yes — Harkan and Carrion get one imp per 232 tiles.
- The "research" step evicts a creature at a portal (§6.10): reproduce. It is the
  shipped behaviour and owner question 9 tests it.
- Torture never a placement target (§6.9): reproduce.
- The off-by-one in room choice's second pass (§6.3): **do not** reproduce. It has no
  effect in shipped data and reproducing it means reading a parameter as a room id on
  purpose. This is the one deliberate departure.
- Nine dead parameters: load, expose, never read. `KeeperAIParameters` says so on each.
- Tick unit: the counters are game ticks, four to a game second (Q1, ADR-0089), and the
  simulation already runs on game ticks, so §5.9's constants (1,680; 240; 300; 40)
  transfer unchanged.
- **`KeeperAI.SlapCooldownTicks` is AI pacing, not a DK2 rule.** It is 300 ticks,
  borrowed from §5.9's *pick-up* guard to stand in for the unmeasured "not recently
  slapped" test of §5.1 step 13, and it belongs to this keeper's hand. **DK2 imposes no
  cooldown on slapping**: every slap counts however fast they come, the work bonus is
  rewritten rather than compounded (Q114), and the original's `LastSlapTick` window
  guards only the knock-back impulse (`HandRules.KnockBackWindowSeconds`,
  `docs/research/hand-of-evil.md`). No simulation system may read this constant, and
  a comment that cites it as a slapping rule is wrong.

### 12.6 Tests the sketch implies

1. **Unit, synthetic grids** — built, in `tests/DK2.AI.Tests` (§12.8).
2. **Harness scenario.** Built: `Scenario.ComputerKeepers` loads campaign PLAYERS
   parameters and emits commands for the next input tick. Stock Level 17 Harkan
   digs and builds an available Prison unaided; its portal recruits through the
   ambient portal rule. Level 10 Asmodeus has a same-map 2,408-tick command capture
   beside the owner's save (`computer-keeper-observations.md`).
3. **Determinism.** A repeated Level 17 scenario produces identical emitted
   command hashes and every per-tick world hash. The different-seed claim remains
   to be checked in the harness.
4. **Owner predictions.** §8's ten questions become acceptance checks the first time the
   game is watched with a keeper running.

### 12.7 Order of work

The proactive foundation already exists (§12.8). Remaining plan, agreed 2026-09-28:

1. **Finish the binary audit.** Check message senders and payloads, helper branches,
   personality initialization and existing approximations. Retain explicit confidence
   levels; record deliberate deviations in ADRs before implementing them.
2. **Observation, messages and defence.** Add the serialisable, tick-paced observation
   facade (goal 10), typed player messages, threat evaluation, reinforcement and retreat.
   Acceptance: an attack on its dungeon causes an unaided defensive response.
3. **Dungeon management.** Add need-driven room expansion/building, training moves,
   mood responses, prisoners, specials and research decisions. Acceptance: a verified
   need changes its orders. Reuse §12.3–12.4's existing commands and state.
4. **Deliberate attacks.** Add readiness, enemy selection, attack tunnels, Call to Arms,
   battle spells and trap/door decisions as their simulation effects become usable.
   Acceptance: the keeper prepares and launches an attack without scripted orders.
5. **Personalities and comparison.** Load all nine profiles; test deterministic command
   streams and compare an active original keeper on the same stock map with availability
   and scripting accounted for. M12 requires unaided digging, expansion, building to a
   need, recruitment, defence and attack, with original-game differences reported.

### 12.8 What exists

`src/DK2.AI` (Unity-bound, `netstandard2.1`), no simulation change, world hash
untouched. `KeeperAI.Think` runs the budget, the 1,680-tick task timeout, the 240-tick
gold untag, imp creation, slapping, and the task machine for tasks 0–7. The other files
are §12.2's table.

`tests/DK2.AI.Tests` runs `KeeperAITests` against a synthetic 30×30 rock world
(`KeeperWorld`): the creation-time directions for all four heart positions, the site
finder's placement and its gap gate, the walker in a straight line and round an
impenetrable wall, the gold scanner's nearest four, and the keeper end to end — with
imps stood in for by a helper that digs whatever is tagged, it builds a 9-tile
Treasury, Lair and Hatchery in `RoomList` order, turns to gold when it cannot afford a
room, creates imps up to its ratio, and produces an identical command stream for
identical seeds.

Room and Create Imp availability use the player's current state on every tick.
Stock Level 10's state-4 Prison and Torture Chamber are no longer built
(`computer-keeper-observations.md`). A caller may override the room predicate.

Deviations and approximations, each a candidate ADR:

- The keeper's own RNG stream and one-tick input delay (§12.1, ADR-0244).
- "Connected" is a flood fill from the heart over passable tiles and the keeper's own
  tags; the original's connectivity test was not read in that detail.
- Water and lava stop a corridor (the task ends) because there is no bridge command;
  the original bridges.
- The room planner's second pass (researchable-but-unavailable rooms, the `0xE5`
  empty-shape dig) is not reproduced: there is no research state.
- `SlapCooldownTicks` (§12.5).

The observation facade and typed player messages, reactive and battle decisions,
personality loader, and §5.1 step 9 training moves remain unbuilt (2026-09-27).
Several step 3 prerequisites were implemented by later milestones; see §12.3–12.4.
