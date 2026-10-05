# Mechanic: winning, losing, and the fall of a dungeon heart

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-22 and 2026-09-23. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046). Names in `code` that are not ours are DK2's, from its
  debug dumper (`player-object.md`) or its own assertion strings
- the shipped campaign's `*Triggers.kld`, swept with `dk2tools kwd triggers`
- `Creatures.kwd` / `Objects.kwd` / the global VARIABLES table

Original version: DK2 v1.70 (GOG)

Questions: answers Q129, Q263, Q358 and Q363, closes the state half of Q40, Q93 and Q207,
opens Q359. Built as ADR-0162, amended 2026-09-23 for the defeat's consequences.

---

## 1. `Status`: one field, one writer, four values

DK2's debug dumper names `player+0x19A` **`Status`**. Exactly one routine writes
a non-zero value into it, `0x004BE630` — `thiscall (CPlayer*, int32 code)` —
and it begins by returning at once if `player.Status` is already non-zero — a
game ends once — and otherwise storing `code` in it, so the **first verdict
stands**. It then snapshots the end-of-level statistics:
land owned (`+0x156`, from `0x004BFE00`), mana (`+0x82` → `+0x15E`), a count
via `0x004BFEA0`, and **elapsed seconds** into `+0x17F` as game turn divided
by variable 19 (`[0x006F2590]`, turns per second). The other two writers,
`0x004B8640` and `0x004C0120`, store zero. The first is construction. The second is
game action 134, which lets a player carry on after the result: it flags the
player, then treats its creatures (`game-actions.md` §3.1).

The codes, from the eleven call sites (`PreCall.py`):

| code | pushed by | meaning |
|---|---|---|
| 1 | player frame, heart destroyed by a **keeper** | conquered |
| 2 | `WIN_GAME`; last keeper standing; allied wins | won |
| 3 | `LOSE_GAME`; heart destroyed by **heroes or neutrals**; allied losses | lost |

Readers that test `Status == 2` sit in the interface (`0x00403B29`,
`0x0040BEB9`, `0x0047A9B6`): the won-game sequence.

**Players 1 and 2 stay at 0 in every shipped level (Q519, 2026-10-05).** Seven of the eleven
call sites skip them: the frame tail `0x004BB2A0` is reached only after `0x004B9EC0` has returned
for the neutral and hero players (`0x004B9F22`, `0x004B9F3A`), and the alliance loops and
`0x0050A6A2` test `Number >= 3`. Four filter nothing: the `WIN_GAME`/`LOSE_GAME` arms
(`0x004C8666`, `0x004C8679`), whose `this` can be player 1 or 2 because their own roots are walked
before that return (`0x004B9EEC`), and game actions `0x54`/`0x55` (`0x005060E0`, `0x00506100`), which
take the player from the packet (`+0x10`); the one `0x54` sender read, `0x0040A937`, uses the local
player. No shipped `WIN_GAME`/`LOSE_GAME` is reachable from a hero, neutral, party or world root: all
87 hang under keepers (80 under player 3's root, 1 under a player-3 creature, 6 under players 4 and 5
in King Of The Hill). So the message gate `0x004C4600` (`+0x19A` non-zero refuses) admits players 1
and 2, as ours does.

## 2. `WIN_GAME` and `LOSE_GAME` are the player dispatcher's first two arms

The player action dispatcher `0x004C8620` takes `type − 10`, ranges to `0x28`,
and indexes a byte table at `0x004C8A98` into a jump table at `0x004C8A60`
(`DispatchMap.py`, which prints `index + 1`; base 10, so a printed 1 is type
10). Its first two arms are the whole story:

- `0x004C8662`, type 10 (`WIN_GAME`): calls `0x004BE630` on the player with
  code 2, then returns 1.
- `0x004C8675`, type 11 (`LOSE_GAME`): calls `0x004BE630` on the player with
  code 3, then returns 1.

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

**The start is 100000, not the OBJECTS record's 34464 (Q129, read 2026-10-05).**
`Object_Init` (`0x0049B1C0`) seeds every object's `+0x40` from the definition's
16-bit `Hp` (`def+0x104`, 0x0049B27F), so for an instant the heart holds 34464.
`DungeonHeart_Build` (`0x004DB700`) then adds `var174 − health` through `0x004B2CE0`
(0x004DB920–0x004DB936), whose object arm adds flat (§3.1), so the heart holds
100000. The builder next takes `trunc(health × (100 − pct) / 100)` off through
`0x004DA200` (signed `IMUL` by `0xAE147AE1`, then `SAR 5` plus the sign bit, at
0x004DB952); `pct` is the heart room thing's percentage word, passed by the THINGS
loader at 0x0052156D. With `pct` 0 the builder ruins the room (`0x004E9130(1)` sets
`room+0x9A` bit 0) and 0x004DB77D skips the heart block: a ruined heart room with no
heart object. Level 7's heart (1) should start at 1000 — STRONGLY INFERRED, as it
assumes bit 4 of `player+0x76` is clear at load and the heart is `Damageable`. Ours
ignores the percentage: Level 7 and Level 8 start at 100000.

**The room frame.** `0x004D9A60` is the Dungeon Heart room's update. Its body at
`0x004D9EFB` walks the room's object list (`word[room+0xE]`, next at
`+0x12A`) for an object whose type byte `+0x12C` is 13:

- **Found:** the countdown byte at `room+0x56` runs down and is reloaded from
  variable 19, so the rest happens once a second. If the heart's health is not
  zero, it gains `min(regen, max - health)` — never past the maximum — by
  finding the owner through `0x004E52F0` (called with the room) and calling
  `0x004DA200` with the owner, the amount and the owner's tag. The health is
  then cached in `room+0x57`.
- **None:** `0x004E9130` is called with the room and 1 — the room is ruined
  (§4).

A heart at zero health **does not regenerate** — the `health != 0` test skips it
— and a room whose heart object is gone is ruined on its next frame.

### 3.1 The health change routine, `0x004DA200(owner, delta, source)` — Q363

Read 2026-09-23. Both the frame's regeneration and every blow arrive here. The
health routine `0x004B2CE0` opens with `word[thing+0x36] = source` for every thing,
and its object arm (class `+0x12` == 2) tests the type: an **Arch Pillar (94) hands
its delta straight to `0x004DA200` on its owner** (`thing+0x2A`); any other object
takes a negative delta only if its definition's flags (`def+0xF4`) carry `Damageable`
(bit 21); and an object's delta then lands **flat** — no multiplier, no clamp — at
`0x004B324A`. So a pillar is a proxy: it has no health of its own that matters.

`0x004DA200` finds the owner's heart tile (`0x004C5F50`), its room (`0x004559B0`)
and the room's object of type 13; none → return 0. Then, in order:

1. a negative delta plays sound `0x329` at the heart (display);
2. **`player+0x76` bit 4, `DungeonHeartIsInvulnerable`, skips everything below** (its
   writer is unread; bit 3 is `HeartPlugRemoved`, bit 2 `CallToArmsOn` — the dumper
   prints the last two in the other order);
3. `0x004B2CE0(heart, delta, source, 0)` — the flat add above, stamping the source;
4. a negative delta raises **notification 1 at the heart tile** (`0x004C4600(world+0x2E54,
   1, owner, x, y, 1)`): speech 345 *"Your Dungeon Heart is under attack!"*, 120 s;
5. the heart's visual stage from `health × 100 / max` clamped to 0..100, in tens
   (tables `0x006A1930`/`0x006A1948`; display);
6. returns the heart's health.

The frame's regeneration passes `word[room+0x1D]`, the owner's tag, as the source, so
between blows the heart's last source is its own keeper. The frame head also reads the
percentage every tick and raises notification 39 (speech 346, *"Your Dungeon Heart is
dying!"*, 50 s) when it is **exactly 50** and 40 (speech 344, *"... in a critical
state!"*, 50 s) when it is **exactly 10** — equality, not a threshold crossed.

**The generic thing sweep deletes it (STRONGLY INFERRED, 2026-09-23).** The
two-pass sweep `0x004B7830` updates things first, then calls each thing's virtual
slot `+0x28`. The class-2 object's implementation, `0x004B5C30`, returns true
exactly when signed `object+0x40 <= 0`. A true result calls virtual slot `+0x30`;
the object's slot is `0x0049BEC0`, which reads its thing id and calls the generic
deleter `0x004B7B10`. That invokes the object death handler `0x0049B5E0` before
destruction and unlinking, so the Heart-specific killer stamp, pillar removal and
death effect in §5 run as part of the same deletion. On a later Heart-room frame,
the missing object causes the ruin transition in §4. No Heart-specific health
poller is involved: the class-wide `<= 0` predicate is the Q363 missing link.

**Built (hash-neutral):** `WorldState.DamageHeart` is step 3 flat with the source stamped
first; `RegenerateHeart` stamps the owner; the `heart_hit`/`shot_heart_hit` events stand
for notification 1. Not built: the invulnerable flag (no writer read), the two
percentage notices, pillar objects (the ring tile stands in, Q363).

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

1. `0x004C0060` is called with the owner (`object+0x2A`) and the killer (the
   word at `object+0x36`); it stores the killer in the owner's
   `KilledByPlayerId` (`+0x132`) and the game turn in the owner's `GameTickDied`
   (`+0xEE2`).
2. Every type-94 object (Arch Pillar) in the heart's room is destroyed.
3. `0x004DA560` is called with the room, the object and the killer — the effect,
   from `DungeonHeart.cpp`.

`word[object+0x36]` is the player whose blow last landed; what writes it is the
object damage path, unread (combat, Q8). `KilledByPlayerId` is DK2's own name
and this project already carried it for condition type 47; this is its writer.
A world bit gates the whole arm: `[world+0x7239] & 0x40` skips it, UNKNOWN.

## 6. The verdict: the player frame's tail

`0x004BB2A0` is the last call of the player's per-turn frame `0x004B9EC0`
(`+0x1F4`). It is the housekeeping tail of `Player.cpp` — its second half is the
free-imp spawn and the research clocks — but it **opens with the verdict**:

Only when `KilledByPlayerId` is non-zero and bit 22 of the flags at `+0x76` is
clear:

1. The game's vtable slot `+0x2F4` is called with this player's slot, 0 and 0 —
   the player is eliminated.
2. If the killer is the hero id (`[0x006F254C]`) or the neutral id
   (`[0x006F2548]`), `0x004BE630` is called on the player with code 3 — lost.
3. Otherwise the player's `ObjectiveTextId` (`+0x19E`) is set to 701 and the
   killer's to 702, and `0x004BE630` is called on the player with code 1 —
   conquered.
4. If the mode at `[0x0075AD4B]` is 1 and this is the local player,
   `0x004DAE40` is called on the player; otherwise `0x004DAA10`.
5. If the mode is not 1, the players with `Number` (`+0x10FC`) at least 3,
   `Status` 0 and bit 19 of `+0x76` clear are counted; if exactly one remains,
   `0x004BE630` is called on that one with code 2 — last keeper standing.

So **the heart's death is not the defeat; the stamped killer is**, read one turn
later, and the side that struck decides between *lost* and *conquered*.
`Number >= 3` is exactly this project's `PlayerIds.IsKeeper`. Bits 19 and 22 of
`+0x76` are not among the fifteen the dumper names; UNKNOWN. Texts 701 and 702
are not in `Text.str` (that table's 0–704 are placeholders), so `ObjectiveTextId`
indexes another table: UNKNOWN which.

## 7. Alliances, read and not built; what a defeat does, read and built

**Alliances end games together** (Q359). After the snapshot, `0x004BE630`
walks every player from `game+0x7A`, reading the alliance byte
`player+0x1AE+otherNumber` both ways: at `0x004BEF88` it counts players whose
`Status` is 0 or 2 and, if exactly one remains playing, ends it with 2; at
`0x004BF0AE` it ends the caller with 2 when every other playing keeper is a
mutual ally, and at `0x004BF172` those allies with 2 as well; at `0x004BEFFB` a
loop ends allies with 3. Which code each loop is reached under is unread, so the
structure is CONFIRMED and the conditions are not.

**What a defeat does** (Q358; the owner's Q40: *creatures leave through the nearest
portal, rivals claim rooms and gold*). Read 2026-09-23. The verdict tail (§6) calls one
of two `DungeonHeart.cpp` routines **every turn a killer is stamped**: `0x004DAE40` for
the local player in mode 1 (`[0x0075AD4B]`, single player), `0x004DAA10` for everyone
else — every computer keeper, and every player in the other modes.

**`0x004DAA10`, the general arm.** On the death turn (`now == GameTickDied`), and only
while the heart tile still carries a heart room (`0x00453A20(tile, 5)`):

1. `0x004F38C0(heart room)`: each gold pile (object 3) in the room becomes a *Gold*
   object (1) of the **neutral** player at the same tile with the same amount, the
   keeper's total debited (`0x004BA420`); the pile is deleted.
2. every room in the keeper's list (`player+0x36`, next `+0x8`) **except the heart (5)
   and room ids 18–23** (hero gates, hero stone bridge) is given to the neutral player
   (`0x004E5650`: relinked into that player's list, `room+0x1D` retagged, counts moved) —
   portals included, which is why the creatures below can still leave;
3. every tile in the keeper's bounding box (`+0x3C..+0x48`) owned by the keeper
   (`tile+8 & 0xFFF`) with no room (`tile+2 & 0xFFF == 0`) is rewritten to its terrain's
   `DestroyedTypeTerrainId` (`def+0x1C6`) owned by neutral (`0x0044FC40(x, y, type,
   NEUTRAL, 1, 1)`): claimed path to dirt, a reinforced wall to rock;
4. sound `0xF4` at the heart; then for each room left in the list a *Dungeon Heart
   Obstacle* (object 100) is created at the heart's centre, owned by neutral, and attached
   to the room (`0x004E6220`, `obj+0x133 = 1`). Its role is UNKNOWN.

From the fifth turn (`GameTickDied + 4 < now`), every turn, for each creature in the
keeper's list (`player+0x6`, next `+0x10`): one flagged `CreatureDying` gets virtual
slot `+0x20` (unread); one whose prison-owner word `+0x3CB` is set is skipped unless
the jailer is the fallen keeper, when it gets slot `+0x20` too; then, if the thing's own
finished predicate (slot `+0x28`, §3.1) is false: a **non-worker owned by the keeper**
not yet `Leaving` (`+0x3E6` bit 14) has any action cleared (`0x00496D00`/`0x00496D20`)
and is set to state 1 then **194 "leaving Dungeon"** (states 194–196, 6, 7, 87 and 193
are left as they are); **everyone else — every Imp, and any creature in the list not
owned by the keeper — is killed outright**, `0x004B2CE0(c, −health, KilledByPlayerId,
0)`, the killer credited.

**`0x004DAE40`, the local player's collapse** (research only). At death only the
Portal (3), Wooden Bridge (8), Stone Bridge (17) and Hero Stone Bridge (22) go neutral;
each other room has the thing on every slab removed (`0x00453760`, `0x004E62D0`, tile
bit 14 cleared, `0x00452340`, deleted) and gets the same obstacle object. From
15 s + 1 turn after death every non-heart room's slabs are rewritten to their destroyed
terrain (`0x0044FC40(…, roomOwnerTag, 0, 1)`) and the room freed (`0x004ECD00`), and a
*Destructor* (object 98) is created at the heart with effect `0x26`; afterwards, while
no Destructor lives (`player+0xB4C`, decremented by its death arm) and `+0x76` bit 14
is clear, one is created on a random owned tile holding no gold pile (3) and no alarm
crate (5) — the bounding box exhausted, bit 14 is set. Its creatures follow the same
arm from the death turn, not the fifth. What a Destructor does is UNKNOWN.

**Built (hash-neutral, 2026-09-23): `DefeatRules`, run by `DungeonHeartSystem` every
turn a killer is stamped.** The general arm: gold to neutral piles per heart tile
(ours holds gold in tiles, not pile objects), rooms to `PlayerIds.Neutral` by retagging
their tiles, roomless owned tiles to their destroyed terrain (an unownable result
carries no owner, ADR-0045), workers killed, non-workers set `Leaving` and walked by
`LeavingRules` to the nearest Portal or Hero Portal of **any** player by straight-line
distance, as `creature-leaving.md` §1 reads it (2026-09-24). Not built: the obstacle objects, the prisoner and
dying slots, the collapse arm. The heart room stays in the list as a ruin, so how
condition type 25 counts it out is still UNKNOWN.

## 8. What this project builds (ADR-0162)

- `PlayerState.Status` with DK2's codes (`PlayerStatus`), `StatusTick`; written
  once by `WorldState.EndGame`.
- Actions 10 and 11 performed for the player of a player or creature tree,
  refused with `Unhandled` in a world tree; 12 and 13 stay `Unhandled`.
- `HeartHealth`, `HeartLastAttackerPlayerId`, `HeartDiedTick`, `HeartRuined`;
  `WorldState.DamageHeart` is the death handler's stamp, called by the melee blow
  (`FightRules`), a shot's contact (`ShotRules`) and the Boulder (ADR-0164, ADR-0333).
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
