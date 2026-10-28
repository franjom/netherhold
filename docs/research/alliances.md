# Alliances

What DK2's alliance array is, who writes it, and what reads it. The verdict half (allies ending
games together) is [win-and-defeat.md](win-and-defeat.md) §7. Read 2026-10-26 (Q558, Q576).

## 1. The array — CONFIRMED

Each player record holds eight bytes at `player+0x1AE`, indexed by the other player's `Number`
(`+0x10FC`, 1..7 by record index, `0x004C1E20`), so the index is the player id. A byte is 1 for
an ally. The predicates:

- `0x004BF3D0` Player_IsAllied: yes for the player's own tag, else the byte.
- `0x004BF390` Player_IsEnemy: its negation, no for the player's own tag.

Ours: `PlayerState.AlliedWith`, one bit per id, read by `WorldState.AreAllied` (self allied) and
`CombatRules.IsHostile`.

## 2. Writers

- **The level's VARIABLES id-66 records**, `0x0051DC90` (dispatched by `Variables_InitDispatch`
  `0x0051AA90`): record words `+0x14` and `+0x18` are two player numbers; each player's byte at the
  other's number is set to 1. It never clears. CONFIRMED from the handler.
- **Trigger action 18** (`0x00519376`) and the make/break game actions `0x00513FF0`/`0x00513F60`
  write both sides. Already built (`TriggerExecutor`, `AllianceSystem`).
- `Player_Initialise` zeroes the array when `Players_Activate` (`0x004C2460`) activates a record.

The records survive the load: the Level 11A capture holds neutral (2) allied with heroes and with
keepers 3–7, exactly that level's records (Q558). The shipped records (`dk2tools kwd varsweep`):

| levels | pairs |
|---:|---|
| 63 | (2,1), then (k,2) for each keeper k from 3 to the level's highest |
| `level18` | the above to 6, then (5,4), (6,4), (6,5): its three computer keepers are allies |
| `Level17` | (3,2), (2,4), (2,5), (2,1) |

So in every shipped level neutral is everybody's ally, and the heroes are no keeper's ally.
No shipped map has an ownable tile with owner 0 (all 66 scanned 2026-10-26), so every unowned
tile names the neutral player and is allied land (Q514).

Ours applies the records in `WorldState.FromMap` (ADR-0546).

## 3. Readers — audited

All call sites were read: 33 of `0x004BF3D0`, 40 of `0x004BF390`. Each compares two owners
through the array. In the simulation sites, neutral (2, the tag `[0x006F2548]` that
`0x00513C09` copies from player record 2) and heroes (1) have **no special case** beside the
call except these, every one of which ours already mirrors:

| Site | Explicit test | Ours |
|---|---|---|
| Claim handlers `0x0046967F`, `0x0046ABCF` | a neutral tile skips the ally test | `ImpTaskSystem.IsAlliedFloor` (§4) |
| Door update `0x004A5EBA`, `0x004A5F6E`, `0x004A6001` | neutral creatures excluded | `DoorSystem` |
| Door contact `0x0048A391` | neutral doors excluded | `CreatureMovementSystem.DoorContact` |
| Possessed dig `0x00514E62` | neutral tiles exempt | `PossessionRules.Dig` |
| Sneak `0x0048EED9` | neutral tile rejected | `PossessionRules.Inputs` |
| Spell location 1 `0x00517B80` | neutral land excluded | `KeeperSpellRules` |
| Hand drops `0x00450910`, `0x00450BE0` | neutral excluded, own arm for workers | `HandRules` |
| Prison rescue `0x004E870D`, `0x004E8791` | neutral prison passes, neutral prisoner skipped | `JailBreakRules` |
| Keeper attack plan `0x004FB022`, `0x004FB02A` | heroes and neutral skipped before the test | `KeeperAttackPlanner` |
| NonFighter `0x0045BF71` | hero owner path | `CombatTargetRules` |

The interface's alliance panel and its message recipients (`0x004256B0`, `0x00425B50`,
`0x004260F0`, `0x004051B5`, `0x004063FC`) also skip heroes and neutral explicitly; they decide
nothing in the simulation. Two of `Combat_SelectAVictim`'s scans never ask the array at all (scan
A, `AttackAbnormalTargets`; scan B, a thing-targeting spell over the neutral player's `+0x12`
list, `0x0045D0A4`; combat-acquisition.md §6), so alliances do not change them.

Everywhere else, once neutral is an ally: keeper and hero creatures stop picking neutral
creatures as targets (combat selectors `0x0045B140`–`0x0045BAD0`, `0x0045D7C8`), traps and
hearts stop firing at them, shots pass them, fear counts them as friends, the computer keeper sums
their strength as friendly (`0x004FA000`, `0x004F9B90`), its attack tunnel and Sight of Evil stop
treating neutral land as enemy land (`0x004FDB90`, `0x005003A0`), and torture-chamber rescue
treats a neutral chamber as allied (`0x004E87FC`). Neutral creatures never start fights
themselves (combat-acquisition.md §3).

**Not built here, and what their builders need:**

- State 265's door forcing (`0x00476AA8`) and the secret-door reveal (`0x004A64FC`): the ally test
  only, no neutral exemption.
- Creature_AttackAdjacentDoor (`0x004814B7`): a neutral door is attacked even when allied
  (`0x004814A7`).
- The Reaper's forced target (`0x0045E208`, `0x0045E32A`) and the keeper's prisoner step
  (`0x005006D0`): no neutral exemption, so neutral creatures are skipped and neutral prisoners go
  to the release branch.
- Spell location validators 2 (`0x00517CB0`) and 3 (`0x00517DE0`): no neutral exemption; no
  shipped spell uses either.
- Jobs/states 25 and 170 (`0x00481EF0`, `0x00482080`): the treasury they pass to `0x004C6930` is
  an *enemy* player's, which steal-gold.md §7 has as the owner's own. UNKNOWN until reread.

## 4. The one difference ours had

The claim handlers test, in order: not the imp's own tile; the tile owner is neutral, or not an
ally of the imp's owner (`0x0046ABCF`: `CMP` with `[0x006F2548]`, then `0x004BF3D0`); only then
claim, attack-floor or convert-room. Ours tested only `owner != keeper`, so after the records
our imps would have claimed and converted an allied keeper's ground (Level 18). Fixed by
`IsAlliedFloor` (ADR-0546). It takes owner 0 as neutral too, where the binary compares only the
neutral tag; inert, since no tile is owned by 0 (§2). In the just-dug arm (`0x0046967F`) the test
cannot refuse either: a just-dug tile is Dirt Path, not ownable, so neutral's (Q514). Only the
ordinary claim arm meets an ally's ground.
