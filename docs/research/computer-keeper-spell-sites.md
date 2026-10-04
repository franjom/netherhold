# Computer-keeper spell casts: every site

Read 2026-10-04 from DKII.EXE v1.70 (Ghidra 12.1.4, Claude's PyGhidra MCP clone and the
headless runner). STRONGLY INFERRED unless marked. Spell selection inside the battle step
is `computer-keeper-management.md` §4; the cast helpers are `keeper-spell-casting.md`.

## 1. Census of the cast entry points

`CallScanAll` over `.text` and `cseg` for the four cast entry points `0x004BB8A0`,
`0x004BBD30`, `0x004BBF90` and `0x004BC290`, and the permission helpers `0x004BBA40`
and `0x004BBBC0`. Positive control: the five known battle-step calls
(`0x0050320D`, `0x0050361D`, `0x0050362D`, `0x0050365F`, `0x00503672`) all appear.
Every call reached from the computer-keeper tick `0x00500C20`:

| Call | Function | Spell | Built |
|---|---|---|---|
| `0x00500F9F` | tick step 11 | 4 Create Imp at the heart ± 2 | yes (ADR-0247) |
| `0x004FED48` | attack management `0x004FECE0` | 3 Call to Arms at the front | yes (ADR-0298) |
| `0x0050320D` | battle step `0x00503060` | 3 Call to Arms near a fighter | yes (ADR-0283) |
| `0x0050361D`, `0x0050362D` | battle step | a scanned thing spell | yes (ADR-0283/0287/0302) |
| `0x0050365F`, `0x00503672` | battle step | a scanned place spell | yes (ADR-0302) |
| `0x004FD64E` | gold step `0x004FD590` | 13 Create Gold (§2) | yes (ADR-0352) |
| `0x005004B1`, `0x005004F9` | Sight step `0x005003A0` | 1 Sight of Evil (§3) | no: ADR-0352 stage 2 |

The other callers are not the computer keeper's. `0x004BB55F` in `Player_KeeperTick`
casts spell 4 with bypass flag 1 for every player (the free imp, `imps-and-mana.md`).
`0x00513322`, `0x0051335F` and `0x0051338F` are the game-action handlers `0x005132D0`,
`0x00513340` and `0x00513370`, registered as actions 54, 55 and 56 by `0x00511A80`
(`game-actions.md`). `0x0050E54F`, `0x0050E57F` and `0x0050E5AF` are permission-only
wrappers in world vtable slots `+0x254/+0x258/+0x25C`, which only `0x004075E0` and
`0x0040B7F0` call (slot scan; its control found the battle step's `+0x250` call).

**The battle scan never picks a new spell (CONFIRMED from shipped data).** Read through
`KwdKeeperSpells`, the flag words are: Sight `0x10`, Possession `0x10`, Call to Arms
`0x10`, Create Imp `0x12`, Summon Horny `0x10`, Thunderbolt `0x4`, Turncoat `0x0`,
Tremor `0x14`, Inferno `0x4`, Heal `0x8`, Chicken `0x4`, Create Gold `0x10`,
OneShotReaper `0x10`. Turncoat carries neither arm bit; Tremor and the rest carry the
excluded bit 4. The corpus audit finds 13 KeeperSpells records in 408 KWD/KLD files:
no level file carries keeper-spell records, so no level overrides the word.

**Q537: the computer keeper never reaches `0x004BC290`.** Its one caller is action 56's
handler. The keeper issues actions only through `World_DispatchAction` (`0x0050E5C0`),
eleven calls in its range, each with a literal id: 40 at `0x004FD0D7`, `0x004FD410`,
`0x004FDEF3` and `0x004FD750`'s three (`EBP` set at `0x004FD77C`), 41 at `0x004FA849`
and `0x00500D13`, 62 at `0x00503008`, 63 at `0x004FF22C`, 88 at `0x0050061D`. No caller
of the dispatcher anywhere stores 54, 55 or 56; those come from input packets.

## 2. The gold step — `0x004FD590`

Reached from the tick's task 0 (`0x00501167`) and task 4 (`0x005011DC`) only, when
`Flags` bit 1, player gold (`+0x7E`) below `TendancyMoney` (`+0x4AF`) and the player's
pending gold-dig count (`+0x1105` → `+0x467C`) zero; task 0 also requires that no task
was started. It spends no allowance and its cast's return is ignored.

- **Deposit arm.** If `GoldDeposits[CurrentGoldArea]`'s amount dword (`+0x32` + 8 ×
  index) is nonzero: copy its x/y words into `DigRoomAt` (`+0x72/+0x74`), set
  `CurrentGoldArea` to 0, rescan (`0x00501C10`), zero `TaskTimeCounter` and set Task 5.
- **Create Gold arm.** Otherwise set `CurrentGoldArea` to 0 and rescan, then, whatever
  the rescan found, when spell 13's availability (`0x004BAFA0`) is 3 call `0x004BBD30`
  for the player (`computer+0xE`) with spell 13, flags 0 and the position
  X = (heartX + 1) × 4096 + 0x800, Y = (heartY + 1) × 4096 + 0x800, Z = 0
  (`0x004FD619..0x004FD64E`): the centre of the tile diagonally south-east of
  `DungeonHeartPosition` (`0x004C5F50`), which is the heart room's centre tile.

The rescan clears all four slots first (`0x00501C38..0x00501C4B`) and is the only writer
of their amounts (`0x005021D0`). `CurrentGoldArea` is written only by creation and as
0 by this step and the room planner's cost arm (`0x004FC9CA`), so it is always 0.
Tasks 5 and 6 read `DigRoomAt`, which tasks 5–7 never write. So the stored list is the
previous scan's result: the Create Gold arm runs at the first gold step and whenever the
previous scan found no deposit. The cost arm (`0x004FC9AE`) copies `GoldDeposits[0]`
even when empty, rescans and sets Task 5, and never casts.

**The cast is refused on a shipped Heart.** Location selector 1 rejects the Heart's
inner three by three (`keeper-spell-thunderbolt.md` §2); the centre plus (1, 1) of a
5 × 5 Heart is in it. The refused cast creates nothing and charges nothing.

**Built** (ADR-0352) as `KeeperAI.GoldStep`: the keeper issues the cast whenever DK2
calls it and the simulation's validators refuse it. Our gates keep `WantsGold`'s task
test for the pending gold-dig count, and the cost arm with nothing stored takes the
fresh scan's first deposit (Q539).

## 3. The Sight step — `0x005003A0`

The tick calls the prisoner step `0x005006D0` whenever allowance remains, then this step
only when allowance remains and `+0x4B6 & 0x300000` (`UseSightOfEvil`, bits 20–21) is
nonzero (`0x005010A9..0x005010D6`). Its allowance is a stack byte (`RET 4`).

1. X = `random(width << 12)`, then Y = `random(height << 12)`, both from the world stream
   (`0x005558D0`), in 1/4096 tile; Z = 0. The world bounds slot `+0x200` must pass.
2. Mode 1: `random(100) < 2`. Mode 2: `random(100) < 20`, then the owner of the tile at
   the position (tile `+8`, low 12 bits) must be an enemy (`0x004BF390`). Mode 3 returns.
3. Spell 1's availability must be 3; then `0x004BBD30(1, &pos, 0)`. A nonzero return
   spends one allowance.

The cast's permission (location 5, target 4), shot, payment and clocks are
`keeper-spell-sight.md` §1; its visible effect is that document's §2.
