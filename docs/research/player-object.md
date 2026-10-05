# The player object's fields, named by DK2 (Q264)

**Scope:** runtime offsets on DK2's player object. What a *trigger* does with
them is `trigger-semantics.md`; this is the map those readings point into.

`FUN_004BCF10` is a debug dumper that writes the whole player object to a log,
one `printf` per field with the field's name in the format string. It is the
strongest kind of evidence this project gets short of running the game: **the
names are the original's own**, not an inference from behaviour, an editor label
or a community document.

## Confidence

| Claim | Confidence | Basis |
|---|---|---|
| Each offset below carries the name beside it | **Known** | Direct instruction read: the value is loaded from that offset and pushed as the argument to the format string quoted |
| The bit-named entries are bits of the dword at `+0x76` | **Known** | Each is `SHR`/`AND 1` on `+0x76` before the push |
| The dump is complete for the fields it prints | **Strongly inferred** | Straight-line code, no branches skipped; but it prints no member of the score block at `+0x142`–`+0x16E` |

## The fields

| offset | name | note |
|---|---|---|
| `+0x04` | `TagId` | the player's thing-table id; the id the trigger walker pairs with a root. Equal to `Number` on a level load (below, Q271) |
| `+0x06`, `+0x16` | `ThingsOwnedList[]`, `TotalNumberOfOwnedThings[]` | eight-entry arrays, one slot per thing category — words then dwords. **Category 0 is creatures**, and condition type 19 walks both |
| `+0x36` | `RoomList` | head of the owned-room list, walked through the object table at `0x006EE540` |
| `+0x3C` | `OwnedArea` | four values |
| `+0x76` | *(bitfield)* | `CanPopImps`, `BookHeldInPossession`, `CallToArmsOn`, `DungeonHeartIsInvulnerable`, `HeartPlugRemoved`, `GameWonSequenceActive`, `ReaperCreatedForEndSequence`, `ThirdPersonPossess`, `WarnAboutLowMana`, `FirstPayDaySet`, `HasPlayedHornyAvailable`, `HasPlayedToDismissReaper`, `PortalsOpen`, `Active`, `NotUsed`. Bits read 2026-09-23: 2 `CallToArmsOn`, 3 `HeartPlugRemoved`, 4 `DungeonHeartIsInvulnerable` — the dumper prints the last two in the other order (`0x004BD478`, `0x004BD494`) |
| `+0x7A` | `ImpProcessedCounter` | zeroed at the top of every player frame |
| `+0x7E` | **`Money`** | condition type 30 |
| `+0x82` | **`CurrentMana`** | condition type 32 |
| `+0x86` | `ManaIncreaseRate` | |
| `+0x8A`, `+0x8E` | `NumberOfManufacturingCreatures`, `NumberOfResearchingCreatures` | `+0x8A` is the Manufacture hooks' count, which the decay reads (`leave-hook-bodies.md` §4) |
| `+0x92`–`+0xAE` | `LastScanTick`, `PlayerScanTick`, `ManaGainTick`, `LastLowManaWarningTick`, `ImpGenerateTick`, `ImpPopScanTick`, `LastTrapManaWarningTick`, `LastNoManaWarningTick` | |
| `+0x132` | `KilledByPlayerId` | read by condition type 47 |
| `+0x134`, `+0x136` | `CreaturePossessed`, `CreatureHeldInPossession` | `+0x134` gates condition type 73 |
| `+0x138`–`+0x13E` | `CallToArmsTag`, `CallToArmsPosition`, `CallToArmsManaTick` | |
| `+0x173` | **creatures lost** | a dword, incremented by the death handler `FUN_00487C70`; condition type 22's `PlayerId 0` path |
| `+0x19A`, `+0x19E` | `Status`, `ObjectiveTextId` | `Status` is 0/1/2/3 = playing/conquered/won/lost, written once (`win-and-defeat.md` §1); but see the kill tally below |
| `+0x1BE`–`+0x1C1` | `ObjectiveActionPointId`, `NumberOfThingsInHand`, `MaxNumberOfThingsInHand`, `FreeRangeChickenCount` | |
| `+0x20D` | `CasinoBigWinnerGameTickRemaining` | |
| `+0x20F`, `+0x213` | `ResearchCompleted`, `ResearchPoint` | points accumulated and the 1-based entry being researched (Q265): `Research_AddPoints` `0x004C2DC0` adds to `+0x20F`, zeroes it on payment and passes `+0x213` to `0x004C30F0`, `0x005070A0` and `0x00507040(+0x20F, +0x213)`; `0x004C3000` writes `+0x213`. The completed count is `+0x17B`. `WorldState.PlayerState` agrees |
| `+0x217` | `ManufactureCompleted` | |
| `+0xB35`–`+0xB3F` | `GemX`, `GemY`, `GemId`, `DungeonArea` | |
| `+0xB43`, `+0xB47` | `DungeonHeartPosition`, `EntranceLastSackedCreatureTypeId` | |
| `+0xB4F`, `+0xB53` | `LastPayDayTime`, `WorldPayDayTime` | |
| `+0xE93` | `PossessedCreatureInitialGold` | |
| `+0xEE2`, `+0xEE6` | `GameTickDied`, **`NumberOfSlaps`** | `+0xEE6` is condition type 71; credited to the slapped thing's owner (below, Q278) |
| `+0xF4A` | `DiscoInfernoCount` | |
| `+0xFCE` | `CreatureUsed[]` | an array |
| `+0x10D2`–`+0x10F2` | `MaintainImpManaPerSecond`, `MaintainTrapManaPerSecond`, `MaxManaGainPerSecond`, `ManaGainPerSecond`, `BasicManaPerSecond`, `MaxMana`, `OneShotManaGain`, `OneShotManaLoss` | |
| `+0x10DA`, `+0x10DC` | **`TriggerRoot`, `TriggerRootRepeatCount`** | confirms `trigger-walkers.md` by name |
| `+0x10F6` w, `+0x10F8`, `+0x10F9` | `MaxCreatures`, `NumberOfCreatureTypes`, **`NumberOfRooms`** | `+0x10F9` is condition type 25's unfiltered count |
| `+0x10FA`–`+0x10FC` | `ComputerPlayerOn`, `ImpsWantARescan`, the player `Number` | |
| `+0x111D` | `FireworksTime` | |

**Corrected 2026-09-09.** Those four are not four keeper spells — they are four
separate **availability tables**, each a `{count, base}` pair at stride 8:
`RoomInfo[]` (`+0x56`/`+0x5A`), `DoorInfo[]` (`+0x5E`/`+0x62`), `TrapInfo[]`
(`+0x66`/`+0x6A`) and the keeper spells (`+0x6E`/`+0x72`). Room, door and trap
entries are nine bytes: `TypeId:u8@0`, `Availability:u32@1`, `Upgraded:u32@5`.
Spell entries are **17 bytes**, independently checked 2026-09-28 in the lookup
`0x004BAFA0`, cooldown reader `0x004BE390`, and cast writer `0x004BBD30`:
id at 0, availability at 1, last-cast tick at 5, last-checked tick at 9,
upgrade at 13 (all but id are dwords). `FUN_004BAB20` is the
`RoomInfo` lookup: a descending scan for `TypeId`, returning that entry's
`Availability` or **1** when the type is absent. Reading the earlier summary as
one spell array was a mis-transcription of four loops that share a format string.

## The score block, and what it is made of

Twelve dwords at `+0x142`, `+0x146` … `+0x16E` are summed into a weighted total
at `0x004BF211`–`0x004BF2EA` in the level-end routine `0x004BE630`. The table at
`0x0069F300` holds **1000, 40, 50, 5, 5, 1, 2, 5, 20, 40, 50, 500**; the first
eleven weight the other eleven fields in address order (`+0x16E` as 20.12 fixed
point). `+0x152` is added **unweighted at the sum** (`ADD` at `0x004BF2EA`), and the
500 at `0x0069F32C` has no reference anywhere (read 2026-10-04).

The player dumper prints none of them, but the **level-statistics** dumper
(`debug-dumpers.md`) prints a struct whose first twelve fields land on those
offsets exactly. `+0x142` is the struct's `+0x00`:

| offset | field | weight |
|---|---|---:|
| `+0x142` | `DungeonHeartsDestroyed` | 1000 |
| `+0x146` | `EnemyCreaturesKilled` | 40 |
| `+0x14A` | `HeroesDestroyed` | 50 |
| `+0x14E` | `RoomsTakenOver` | 5 |
| `+0x152` | `RoomExtraPoints` | unweighted at the sum |
| `+0x156` | `LandOwned` | 5 |
| `+0x15A` | **`GoldMined`** | 1 |
| `+0x15E` | `ManaStored` | 2 |
| `+0x162` | `ItemsManufactured` | 5 |
| `+0x166` | `CreaturesCommanded` | 20 |
| `+0x16A` | `CreaturesConverted` | 40 |
| `+0x16E` | `CreatureTraining` | 50 |

So **condition type 31 tests how much gold this keeper has mined** (Q260). The
credit at `0x004973CF` adds to the owning player from a creature, which is what
mining is.

**There are two scoring systems, split by game mode (Q267, read 2026-10-04).**
`Variables.kwd` holds `MpdScore*` at ids 228–233 (50, 5, 1, 10, 20, 40; globals
`0x006F29D5`–`0x006F29E9`). Each is read only at its event: hero killed
`0x004882FB` and `0x004BEA05`, land owned `0x00455A70`/`0x00455ACB`, gold slab
mined `0x004973A7`, item manufactured `0x004F5439`, creature entered `0x004DCB81`,
id 233 `0x004F28B9`. Each read is gated by the mode byte `[0x0075AD4B] == 4` and the
player's `Number >= 3`, and passes the variable to world slot `+0x344`
(`0x00511650`), which adds it to `LevelScore` (`+0x72A2`, floored at 0;
`trigger-state.md` §4). The table above is read only at `0x004BF211`, which
`0x004BEF14`–`0x004BEF26` enters in modes 1 and 4; mode 4 stores the sum at
`0x00759C4B`, the others hand it to `0x00561BF0`. So in mode 4 both run, and mode 1
uses the table alone. **CONFIRMED** for the readers, gates and arithmetic; that
mode 1 is single-player campaign and mode 4 My Pet Dungeon is STRONGLY INFERRED
(no mode data read). Ours builds neither.

## TagId and Number are equal on a level load (Q271, read 2026-10-04)

`Number` (`+0x10FC`) has one writer, `Players_Create` `0x004C1E20`, which stores
index + 1 in each of the seven records. `TagId` (`+0x04`) is the thing-table id,
given by the base constructor `0x00507850`: the first free slot from the cursor
`0x006A481C`, or the forced id `0x006A4820` while `0x006F2540` is set (save restore).
`GameAction_LoadLevel` resets the cursor to 1 (`0x00507A30` at `0x00513994`) and
calls `Players_Create` at `0x00513A4B` with only memsets and copies between; the
vector constructor `0x00635EC0` builds the records in index order through
`0x004B8450`, which calls `0x00507850`. So player *i* gets `TagId` *i*+1 = `Number`,
given that slots 1–7 are empty then (STRONGLY INFERRED). Activation
(`0x004C2340`, `0x004C2460`) selects by `Number` and returns `TagId`; neither writes
either field. **Unread:** the save-restore path and the second `Players_Create`
caller, `0x00509B3E` in world slot `+4` (`0x005099A0`). Ours keeps one id per
player, which matches on a level load; Q305 follows for level load only.

## NumberOfSlaps counts slaps on the player's own things (Q278, read 2026-10-04)

Every increment of `+0xEE6` goes to the owner of the thing acted on
(`[thing+0x2A]`), never to a slapper argument. A literal search for `0xee6` finds
eight functions, four of which increment:

- `Hand_SlapCreature` `0x0048B8A0`, two sites. Its second argument, a player
  record, supplies only the damage source's `TagId`. States `0xD1`/`0xFF` count and
  return early. No count: `IsHorny` (sent to state `0xF2`), states `0x4C`/`0x65`/`0xC1`,
  a room owned by another keeper, a refused `SetState`, room type 12 (damage only).
- `0x004A7E70`, a trap's reaction (by shape, a slap): increments at its end
  unconditionally.
- `Object_ReactToSlap` `0x0049C2A0`: increments before anything else.
- `0x004A8E70`, a trap's spreading pass, credits the owner of each other trap it
  triggers. It is not obviously a slap.

`Player_Initialise` `0x004B8640` zeroes it. The HUD `0x00427BA0` shows widget
`0x31` while world `+0x72B8` (slot `+0x36C`; action 50 sets it through `+0x370`)
minus the local player's `+0xEE6` is not negative. Ours slaps (`HandSystem.Slap`)
but never increments `SlapCount`, so condition 71 reads 0.

## The kill tally at `+0x19E`, and why the dumper's name for it is entry 0

Condition type 22's arm (`0x004C7D0F`) reads a **u16 at `+0x19E + 2 x n`** on
another keeper's record, where `n` is the *asking* keeper's `Number`
(`+0x10FC`). It reaches that record through the player array — base
`world[0x84]`, stride **4389**, which is one player record — so a `PlayerId` of
*k* selects index *k−1*.

The death handler `FUN_00487C70` writes the same place:

1. It reads the victim's owner from `victim+0x2A` (the victim is in `ESI`).
2. It reads that owner's `Number`, the byte at `owner+0x10FC`.
3. On the **killer** (in `EDI`) it adds one to the word at
   `+0x19E + Number*2` — one more of theirs.
4. It tests bit `0x40` of the victim's byte at `+0x3E6`; if set, the next step
   is skipped.
5. It adds one to the dword at `+0x173` of the victim's owner — that owner's own
   total.

So `+0x19E` is a **16-entry per-victim tally living on the killer**, and
`+0x173` is the victim's own running total — the two halves of type 22. The
total is skipped when the victim carries bit `0x40` at `+0x3E6`, so a creature
can count for the killer and not against its owner; what that bit marks is
UNKNOWN.

**The array is 16 words and ends at `+0x1BE`**, exactly where the next dumped
field begins, which is what fixes its length. The debug dumper names `+0x19E`
`ObjectiveTextId` — that is **entry 0**, the slot for player `Number` 0, and no
keeper has that number. Whether the game genuinely reuses the slot or the
dumper simply names the offset is not separated here; either way, reading
`ObjectiveTextId` as a plain scalar and the tally as starting later would be
wrong, because both instructions above use `+0x19E` itself.

## Starting gold is paid into the heart (Q75)

Read 2026-09-28 with Ghidra 12.1.4. STRONGLY INFERRED from the binary; the
outcome is CONFIRMED by the owner's Level 2 recording. The balance table has no
starting balance, but the level's PLAYERS record does, at offset 0 (`KwdPlayers`).

- **The loader.** `0x0051F0B0` hands the record's gold to the money routine
  `0x004BA420` with no tile. With no tile, that routine only adds the amount to
  the player's `+0x7E`.
- **The heart.** The THINGS loader (`0x0052156D`) runs the heart's builder
  `0x004DB700` for a room record on terrain 14. At the heart's centre tile the
  builder reads `+0x7E`, zeroes it and deposits it into the heart through
  `0x004F4090`. That routine fills the room's tiles in chain order from the given
  tile, skipping the platform. What the heart cannot take goes into the keeper's
  Treasuries with room. Anything still left is dropped.
- **No stockpile.** The player tick (`0x004B9EC0`) sets `+0x7E` to the sum of the
  keeper's Treasury and Heart gold every tick (while `+0x132` is 0, unread). So
  `+0x7E` is the displayed total, not the separate stockpile the manual describes.
- **Level 2.** Its keeper starts with 16,000, a full heart. The recording shows a
  store pile on all sixteen rim tiles at tick 2. Its imps found no store with room
  (`0x00464400`), put their gold down from turn 155 and first carried gold home
  after the Lair was paid for at 503.

Built as `ThingProjection.Place(..., startingGold)` with `GoldStore.FillAlongChain`
(ADR-0270). Deviates: the Treasuries are taken in our room order, not the player's
room list.
