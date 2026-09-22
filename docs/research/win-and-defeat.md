# Mechanic: winning, losing, and the fall of a dungeon heart

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-22. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046). Names in `code` that are not ours are DK2's, from its
  debug dumper (`player-object.md`) or its own assertion strings
- the shipped campaign's `*Triggers.kld`, swept with `dk2tools kwd triggers`
- `Creatures.kwd` / `Objects.kwd` / the global VARIABLES table

Original version: DK2 v1.70 (GOG)

Questions: answers Q263, closes the state half of Q40, Q93 and Q207, opens Q358
and Q359. Built as ADR-0162.

---

## 1. `Status`: one field, one writer, four values

DK2's debug dumper names `player+0x19A` **`Status`**. Exactly one routine writes
a non-zero value into it, `0x004BE630` — `thiscall (CPlayer*, int32 code)` —
and it begins:

```
if (player.Status != 0) return          ; a game ends once
player.Status = code
```

so the **first verdict stands**. It then snapshots the end-of-level statistics:
land owned (`+0x156`, from `0x004BFE00`), mana (`+0x82` → `+0x15E`), a count
via `0x004BFEA0`, and **elapsed seconds** into `+0x17F` as game turn divided
by variable 19 (`[0x006F2590]`, turns per second). The other two writers,
`0x004B8640` and `0x004C0120`, store zero — construction and reset.

The codes, from the eleven call sites (`PreCall.py`):

| code | pushed by | meaning |
|---|---|---|
| 1 | player frame, heart destroyed by a **keeper** | conquered |
| 2 | `WIN_GAME`; last keeper standing; allied wins | won |
| 3 | `LOSE_GAME`; heart destroyed by **heroes or neutrals**; allied losses | lost |

Readers that test `Status == 2` sit in the interface (`0x00403B29`,
`0x0040BEB9`, `0x0047A9B6`): the won-game sequence.

## 2. `WIN_GAME` and `LOSE_GAME` are the player dispatcher's first two arms

The player action dispatcher `0x004C8620` takes `type − 10`, ranges to `0x28`,
and indexes a byte table at `0x004C8A98` into a jump table at `0x004C8A60`
(`DispatchMap.py`, which prints `index + 1`; base 10, so a printed 1 is type
10). Its first two arms are the whole story:

```
0x004C8662  (type 10, WIN_GAME)   push 2 ; ecx = player ; call 0x004BE630 ; eax = 1
0x004C8675  (type 11, LOSE_GAME)  push 3 ; ecx = player ; call 0x004BE630 ; eax = 1
```

`this` is the player whose tree reached the record — or, from a creature tree,
its owner, because the creature dispatcher delegates player-owned types with the
owner as `this` (`trigger-semantics.md`). **Neither action carries a
`PlayerId`**, and this is why: the context supplies it. The world action
dispatcher (`0x00518AD0`, base 1, byte table `0x00519930`, jump table
`0x005198C4`) sends both to its failure exit `0x00519893`, so a `WIN_GAME`
authored in a world tree ends nothing and prints `Unhandled Trigger When`.

**`WIN_SUBOBJECTIVE` (12) and `LOSE_SUBOBJECTIVE` (13) are refused by every
dispatcher** — world and player both send them to the failure exit, and the
creature dispatcher does not own them. They are dead in v1.70; six shipped
records use them.

**What gates them in the shipped campaign.** Over all 66 `*Triggers.kld`, the
parent condition of each record:

| action | records | parent condition types |
|---|---:|---|
| `WIN_GAME` | 40 | type 1 flag ×30, type 2 timer ×9, type 72 ×1 |
| `LOSE_GAME` | 47 | type 1 ×15, type 47 killed-by ×11, type 25 room count ×7, type 2 ×5, type 72 ×3, 28 ×2, 19 ×2, 71 ×1, 4 ×1 |

Every `WIN_GAME` hangs off a flag, a timer, or the always-true-headless type 72.
Level 1's is trigger 204: timer 12 `>= 6` seconds, started by action 8 at the
end of the level's task chain. **Six levels script their loss as "type 25: this
keeper owns zero rooms of type 5"** — the Dungeon Heart — which is how a script
sees a fallen heart, and says the fallen heart leaves its keeper's room list
(§4, and Q358 for how).

## 3. The heart is an object, and the room's own frame keeps it alive

No level places a Dungeon Heart *object*: across the whole THINGS corpus there is
no `OBJECT` of type 13. The engine creates it from the heart room at load. Its
health is `object+0x40`; its maximum is variable 174 `DungeonHeartObjectHealth`
(100000, `[0x006F294B]` — the one id in that dispatcher stored raw rather than
divided by variable 19); its regeneration is variable 131
`DungeonHeartHealthRegenerationPerSecond` (25, `[0x006F2902]`).

**The room frame.** `0x004D9A60` is the Dungeon Heart room's update. Its body at
`0x004D9EFB` walks the room's object list (`word[room+0xE]`, next at
`+0x12A`) for an object whose type byte `+0x12C` is 13:

```
found: countdown byte room+0x56 ; reload from variable 19 -> once a second
       if health != 0:
           add min(regen, max - health)            ; never past the maximum
           via 0x004E52F0(room) -> owner, 0x004DA200(owner, amount, ownerTag)
       cache health in room+0x57
none:  0x004E9130(room, 1)                          ; ruin it (§4)
```

A heart at zero health **does not regenerate** — the `health != 0` test skips it
— and a room whose heart object is gone is ruined on its next frame.

## 4. Ruin: Q263 answered

`0x004E9130(room, flag)` switches on the room type byte `+0x18` from 5 (Dungeon
Heart) through 23. The heart arm destroys the room's remaining objects
(`0x004E62D0`, then `0x004B7B10` — the thing deleter — per object) and then

```
room+0x9A bit 0 = flag          ; XOR-masked in, so 1 sets it
for each tile of the room:  game vtable +0x1D8 (x, y), 0x004522A0 (map, x, y),
                            0x004EDC10 (game+0x9BE, x, y)   ; rebuild the tiles
```

**`+0x9A` bit 0 is the ruin.** Q263 asked what it was because the player frame's
gold sum skips a heart whose bit is set (`0x004B9F7C`, `payday.md`,
`trigger-semantics.md`): a ruined heart stores nothing. The same routine is also
what trigger action 32 `COLLAPSE_HERO_GATE` calls (`0x005196F5`), so "ruin" is
the engine's general collapse of a room, and the owner's Q93 answer — the fallen
heart turns to ruin, claimable but neither sellable nor buildable — is this bit.
How the ruined room leaves the keeper's room list, which type 25 needs, is Q358.

## 5. The blow that kills: `KilledByPlayerId`

`0x0049B5E0` is the object death handler (reached from the thing deleter
`0x004B7B10` at `+0x17`). When the dying object's type is 13:

```
0x004C0060(owner = object+0x2A, killer = word[object+0x36])
    -> owner.KilledByPlayerId (+0x132) = killer
       owner.GameTickDied      (+0xEE2) = game turn
destroy every type-94 object (Arch Pillar) in the heart's room
0x004DA560(room, object, killer)          ; DungeonHeart.cpp — the effect
```

`word[object+0x36]` is the player whose blow last landed; what writes it is the
object damage path, unread (combat, Q8). `KilledByPlayerId` is DK2's own name
and this project already carried it for condition type 47; this is its writer.
A world bit gates the whole arm: `[world+0x7239] & 0x40` skips it, UNKNOWN.

## 6. The verdict: the player frame's tail

`0x004BB2A0` is the last call of the player's per-turn frame `0x004B9EC0`
(`+0x1F4`). It is the housekeeping tail of `Player.cpp` — its second half is the
free-imp spawn and the research clocks — but it **opens with the verdict**:

```
if (KilledByPlayerId != 0 && !(flags76 & bit22)):
    game vtable +0x2F4 (this player's slot, 0, 0)        ; eliminated
    if killer == HERO id [0x006F254C] or NEUTRAL id [0x006F2548]:
        0x004BE630(player, 3)                             ; lost
    else:
        player.ObjectiveTextId (+0x19E) = 701 ; killer.ObjectiveTextId = 702
        0x004BE630(player, 1)                             ; conquered
    mode [0x0075AD4B] == 1 && local player ? 0x004DAE40(player) : 0x004DAA10(player)
    if mode != 1:
        count players with Number(+0x10FC) >= 3, Status == 0, !(flags76 & bit19)
        exactly one -> 0x004BE630(that one, 2)            ; last keeper standing
```

So **the heart's death is not the defeat; the stamped killer is**, read one turn
later, and the side that struck decides between *lost* and *conquered*.
`Number >= 3` is exactly this project's `PlayerIds.IsKeeper`. Bits 19 and 22 of
`+0x76` are not among the fifteen the dumper names; UNKNOWN. Texts 701 and 702
are not in `Text.str` (that table's 0–704 are placeholders), so `ObjectiveTextId`
indexes another table: UNKNOWN which.

## 7. Two things read, not built

**Alliances end games together** (Q359). After the snapshot, `0x004BE630`
walks every player from `game+0x7A`, reading the alliance byte
`player+0x1AE+otherNumber` both ways: at `0x004BEF88` it counts players whose
`Status` is 0 or 2 and, if exactly one remains playing, ends it with 2; at
`0x004BF0AE` it ends the caller with 2 when every other playing keeper is a
mutual ally, and at `0x004BF172` those allies with 2 as well; at `0x004BEFFB` a
loop ends allies with 3. Which code each loop is reached under is unread, so the
structure is CONFIRMED and the conditions are not.

**What a defeat does** (Q358, the owner's Q40: *creatures leave through the
nearest portal, rivals claim rooms and gold*). `0x004DAA10` (1069 bytes) and
`0x004DAE40` (2159 bytes, `DungeonHeart.cpp`) are the two consequence routines.
The first puts creatures into state **194 "leaving Dungeon"** by id
(`0x0048AD30(0xC2)`, twice), clears actions (`0x00496D00`/`0x00496D20`), and
ends with a call to the health routine `0x004B2CE0`. Neither is read further.

## 8. What this project builds (ADR-0162)

- `PlayerState.Status` with DK2's codes (`PlayerStatus`), `StatusTick`; written
  once by `WorldState.EndGame`.
- Actions 10 and 11 performed for the player of a player or creature tree,
  refused with `Unhandled` in a world tree; 12 and 13 stay `Unhandled`.
- `HeartHealth`, `HeartLastAttackerPlayerId`, `HeartDiedTick`, `HeartRuined`;
  `WorldState.DamageHeart` is the death handler's stamp. Nothing calls it from
  the tick order yet — combat is unbuilt.
- `DungeonHeartSystem`, last in the tick: regeneration once a second, ruin one
  turn after death, verdict from the killer's side, last keeper standing.
- A ruined heart has no gold capacity (`GoldStore`) and is claimable
  (`TileWorkRules.Applies(world, …)`).

Not built: §7 entirely; the object damage path that writes `+0x36`; the
multiplayer-mode branch; the room leaving the room list.

## Confidence

**CONFIRMED (binary):** `Status` at `+0x19A`, its single non-zero writer, the
one-shot guard, codes 1/2/3 and every site that pushes them; the player action
dispatcher's tables and its arms for 10 and 11; that 12 and 13 reach the failure
exit of both dispatchers; the heart object's type, health offset and both
variables; the room frame's regeneration, its once-a-second gate, the zero-health
skip and the no-object → ruin call; `+0x9A` bit 0 as the ruin and its use by
`COLLAPSE_HERO_GATE`; the death handler's stamp of `KilledByPlayerId` and
`GameTickDied`; the verdict's conditions, its side test against the hero and
neutral ids, the objective text ids, and the last-keeper-standing count.

**CONFIRMED (data):** the parent types of all 87 shipped `WIN_GAME` and
`LOSE_GAME` records; no type-13 object in any THINGS file; 100000 and 25.

**STRONGLY INFERRED:** that the alliance loops of §7 propagate a win to allies
and a loss to allies — structure read, entry conditions not.

**UNKNOWN:** what writes `object+0x36`; bits 19 and 22 of `+0x76`; the world bit
`+0x7239 & 0x40`; which table `ObjectiveTextId` indexes; everything in §7's
consequence routines beyond their calls; how a ruined room leaves the room list.
