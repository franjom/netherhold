# Possession: the unbuilt arms and the command inputs

Primary source: DKII.EXE v1.70, pyghidra-mcp reads on 2026-10-12 (Q535, Q536).
This completes [keeper-spell-possession.md](keeper-spell-possession.md) "Possessed
actions (Stage7b)", which holds the packet layout, the melee pick, the spell scan and
what Stage7b2 built. Each function's record in `functions/` holds the read's scope.
Variable 19 (`0x006F2590`) is the level's TicksPerSecond.

Object types named below: 9 Chicken, 14 Training Dummy, 15 Anvil, 16 Steel Bucket,
46 Work Bench, 64 Book Case, 94 Arch Pillar. A dead body is class 6, not an object
type, and a shot is class 1.

## 1. Melee on objects — CONFIRMED

Action 26 (`0x00514830`) switches an object target (class +0x12 = 2) on type byte
+0x12C through index table `0x005153E8` (type − 1, 46 entries) and jump table
`0x005153D0`: 1–3 gold (built), 9 chicken, 14 dummy, 15/16/46 a shared arm. Every
other type, types above 46 included (64, 94), takes the attack `Begin(17)`. Every
other class (shot 1, trap 3, door 4, body 6) and every creature not in state 0x83
also takes the attack. A handled arm stamps +0x427 = now a second time
(`0x00514FD6`); a refused `Begin(17)` returns without it.

**Chicken (9).** When HungerNeed (+0x29A, state object +0x24E + 0x4C) is 0 and the
creature is not a Bile Demon (type +0x36F = 3), the chicken is killed, not eaten:
its +0x146 bit 0 (EnableDeathEffect) is set and its health set to 0 (vtable +0x20,
`0x004B2AD0`). Otherwise, in order:
1. Unless a Bile Demon: fill = HungerFill def+0x66D, level-scaled (level 1 raw,
   else level row +0x171 × fill / 100, signed). If fill ≠ 0,
   `Anger_AddToBucket(1, Anger_GetBucket(1) / fill, 9)` (signed divide, an add; the
   Hatchery eat `0x00463760` forgets bucket 1 instead), then `Hunger_PayOff(fill)`.
2. Heal: max = def+0x692 level-scaled by row +0xE1; amount = max − health (+0x40)
   when that is ≤ HpFromChicken def+0x694 (unsigned compare), else HpFromChicken;
   `Thing_ModifyHealth(creature, amount, chicken +0x28, 0)`.
3. Sound 0xCD (bank def+0x6E3) at the creature, 0x111 (chicken def+0x10E) at the
   chicken; chicken +0x146 bit 0 cleared; chicken health set to 0.
4. When art 13's byte +0xB bit 0 is set, `BeginAt(26, creature position, 0, 0, 0)`.

No state change and no LastAteTime stamp, unlike the Hatchery's state 93.

**Training Dummy (14).** `Object_SetProcessState(18)` (`0x0049EF60`) writes +0x13A =
18, +0x13E = 0, with no guard, so a second hit restarts it. State 18 (`0x004A13F0`):
at age 1, +0x14A = now, direction −1 when random(2) = 1 else +1, speed 0x200, the
effect def+0x117, art 5, sound 0x311. Each tick u = 0x200·dir·9 / ((now + 3 −
stamp)·3) and heading = (heading + u − dir·64) & 0x7FF while |u| > 0x40, about 20
ticks; then +0xD4 and +0xD8 are zeroed and it returns 0. No damage.

**Anvil, Steel Bucket, Work Bench (15, 16, 46).** A hit point from world+4→+0x10
vtable +0x10C, else the object's position; `Attack_LookupImpactPair(def+0x77A, object
def+0x11C, point)`; +0x427 = now. Presentation only.

## 2. The swing on things that are not creatures — CONFIRMED

Action 17's swing `0x00498E30` gates on a live slot, +0x16 ≠ 2 and vtable +0x34;
object, shot and body all return 1 there (`0x00628BC0`). A possessed attacker
(+0x3E6 bit 7) skips the first gate, as its record says. An object gets an impact
lookup from def+0x11C; a shot or body gets none. The blow then passes the owner
modifiers (+0x1090, +0x1091) into `Thing_ModifyHealth` (`0x004B2CE0`):
- an Arch Pillar (94) forwards the change to its owner's Dungeon Heart (`0x004DA200`);
- any other object refuses a negative change unless def+0xF4 bit 21 (Damageable).
  Shipped Objects.kwd (flags at record 0x344, copied unchanged to def+0xF4 by
  ObjectDef_Load `0x0049DD60` when its source +0x7C is nonzero) sets it only on
  Chicken, Rat, Bat, Dungeon Heart and Boulder: a Book Case takes nothing;
- every non-creature then takes +0x40 += delta, flat: a shot's Health (lifetime or
  hit count) and a body's rot timer (DeadBodyDiesAfterSeconds × 4) both shrink.

Nothing picks a body up. When the attacker's def+0x686 bit 14 is set and the target's
death query `0x004B5C30` (health ≤ 0) holds, the attacker enters state 0xEF. Sound
0xDB then plays whether or not it did, and the swing returns 0.

A spell at an Arch Pillar is the ordinary `Begin(16, slot, target)`: action 16
(`0x00498870`) checks only a live, non-class-9 target; an object's vtable +0x40 is 0,
so the shot aims at the pillar's position plus the lead, and its damage reaches the
heart through the forward above.

UNREAD: what removes a shot whose Health a blow takes to ≤ 0; what the object
updater does when state 18 returns 0.

## 3. Dig and claim — CONFIRMED by listing

A melee press with no target, on a creature whose def+0x686 bit 0 is set
(`0x00514E1D`–`0x00515037`); bit 0 clear returns 1 and does nothing.

**Pose tile T** (world vtable +0x1D4). Only when `Tile_TouchesOwnFloor(T, owner)`
(`0x00453B30`) and T's owner (+8 & 0xFFF) differs from the creature's:
1. T owned by a non-neutral player allied to the creature (`0x004BF3D0`): return 0;
   the facing tile is not tried.
2. `Tile_IsClaimablePath(T)` (`0x00453FC0`: neutral, terrain +0x1CB bit 0, not
   solid): `BeginAt(4, pose, 0, 0, 0)`; on success +0x389 = 1 (a camera bob read and
   cleared by `0x0044DCB0`, presentation) and +0x427 = now.
3. Else `0x00454000(T, owner)` (owner differs; terrain Ownable bit 2 and Attackable
   bit 5; not Solid bit 0, not Room bit 4): `BeginAt(6, pose)`; +0x427 on success.

A successful start on T returns 1.

**Facing tile**, when nothing started on T. The index is the packet yaw, not the
heading: i = (u16)(yaw + 0x100) >> 9, 0..4, into table `0x006BC350` = (0,−1), (1,0),
(0,1), (−1,0), (0,−1). tx = (pose X >> 12) + dx, ty likewise; out of the map
(unsigned against world vtable +0x1CC/+0x1D0) returns 1. The begin point is the
tile's corner (tx << 12, ty << 12, 0), not its centre. In order:
`Tile_IsMinableGold` (neutral, solid, terrain +0x185 ≠ 0) → action 2;
`Tile_IsDiggableWall` (solid, not impenetrable, no owner test) → action 1; any other
solid tile → effect 0x335 (presentation). +0x427 on a successful start.

No reinforce arm, no wall claim, no task marked.

**The actions — CONFIRMED** (handlers read 2026-10-12). None reads a task or an imp
job field: each takes its tile from the begin point (+0x38D/+0x391 >> 12) and works it
through `Map_ApplyTileWork` (`0x004508D0`) with the creature's owner. Work rate is
+0x3AF + +0x3B3 (12.12); ×MRT multiplies the floored delta (SAR 12) by
`Combat_MeleeRechargeTicks` (`0x00494EF0`) when possessed (+0x3E6 bit 7); XP is 20
(`0x006F295E`) a strike. Runner mode 3 (`0x00496AA0`) calls the handler each poll
until it returns 0, then holds for the animation; mode 0 ends on that poll.

| action | handler | mode | strike |
|---|---|---|---|
| 1 dig | `0x00496E00` | 3 | Imp: own wall var 9, rock var 8, enemy wall var 10, × rate, ×MRT; XP to any digger. Non-imp: 6 × var 9 × rate, owner ignored, no ×MRT, only while global `0x0075AE93` = 0 (UNREAD; `0x0050C90D` reads it beside game modes 2/3) |
| 2 mine | `0x004971D0` | 3 | var 7 × rate, ×MRT, XP. Gold old·G/H − new·G/H (G terrain +0x185, H +0x1AD) to player GoldMined +0x15A, then the purse up to MaxGoldHeld, the rest on the floor (`0x004510C0`). A gem face (terrain +0x17B bit 1) takes no damage and pays GoldMinedFromGems (25) every strike; CONFIRMED (owner, 2026-10-12: gold every swing, the face never cracks) |
| 4 claim | `0x004974C0` | 3 | ClaimTileHealth × rate, ×MRT, XP; returns 1 while the tile is claimable, so it strikes every poll until it converts; sound 0x10A or 0x10B |
| 6 convert | `0x004977F0` | 0 | AttackTileHealth × rate, no XP, no ×MRT: one strike a press. When the floor falls, every trap and door on the creature's own tile (+0x1A, not the begin point) has its health zeroed (removal LIKELY) and `0x00455030` is notified |

The owner (2026-10-12) has seen hero Dwarves dig walls; whether a possessed non-Imp
digs is not known. Action 5 (`0x00497670`) is claim state 20's convert (imps of any player); its ×MRT branch is reached only by a possessed imp. UNREAD:
`0x0075AE93`, `0x004ECDB0` (run when a dug tile reaches 0), `0x00455030`, `0x004510C0`.
Ours: `TileWorkSystem` already does the rate, XP, GoldMined and the purse-then-floor
hand-off; ×MRT, the non-imp dig, claim's repeat strike, action 6's kind and its
trap/door health zeroing are missing, and the gem rule differs for every miner (Q587).

## 4. Followers — CONFIRMED unless marked

**The press** (slot 3, `0x0051503C`–`0x005150A4`). The owner (2026-10-12): key 7, then a
click on a highlighted creature adds it as a follower, one per click; selector 3 of
`Possession_SelectAbility` (`0x0040F9E0`) is that ability (the key binding is LIKELY). `0x00515D50` picks one creature;
nothing happens when it returns 0 or when the possessed creature's +0x488 ≥ (variable
223 & 0xFF), a signed compare (MaximumGroupNumberOfFollowers, 8). Otherwise
`0x00461980(possessed id)` runs on the pick, and +0x488 is incremented only on its
success. One follower per press; the arm never stamps +0x41B.

**The pick** `0x00515D50`. Players in list order (§6); a player qualifies when
`0x004BF390` against the creature's player returns 0, the own player included. Each
player's creatures (+6, next +0x10), tested in order: the predicate `0x00491AE0` (not
already following, not Horny, state group ≠ 1, +0x3CB = 0, not rebelling, mood
Happy); horizontal (dx²>>12)+(dy²>>12) < 0x31000 from the **creature's** position,
not the pose (7 tiles); the cone `0x00516950` from the pose; sight `0x004512F0` from
the creature. Score = 4096 − dot(normalised(candidate − pose), aim), replaced only
when strictly lower (signed); the best starts at (variable 187 × 0x800) / 360 = 256.

**The start** `0x00461980(leader)`: clears +0x482 when it already names the leader;
runs the predicate; SetState(246, 0); on success +0x482 = leader (word), +0x47E = now,
StopMoving, return 1; on failure +0x482 = 0, return 0. It never touches +0x488, the
job or claims. Its callers count differently: action 26 only on success, action 37
(`0x00516E1C`) always, the hypnosis command and the idle re-follow (`0x00477421`)
never.

**State 246** (row `0x006DC0B6`: update `0x00461A10`, group 3, category 29, no hooks).
Each tick, first: the follow ends (+0x482 = 0, return 0, to idle) when the leader's
slot is dead, its class ≠ 0, its owner differs, or its +0x3E6 bit 7 (possessed) is
clear. Otherwise, when +0x47E ≤ now: `Travel_RouteToPoint(leader position, resume
246, 0x80)`; on failure +0x47E = now + variable 19; on success walk (SetSpeedMode 0)
when (dx²>>12)+(dy²>>12) < 0x5000, else run. +0x47E is not advanced on success, so
each return to 246 re-routes to where the leader is then.

**How following ends.** Possess and unpossess (`0x0048DEA0`) zero the leader's +0x488;
each follower drops on its next 246 tick. Action 38 (`0x00516E40`) clears +0x482 on
every creature of the player that has it, decrementing +0x488 once each (a hypnotised
follower was never counted, so it can go negative). A follower's death decrements the
leader's +0x488 (`0x00487E6A`) and keeps its own +0x482. Fear in spFighting
(`0x0045F425`) clears +0x482 and decrements; fear in ApproachTarget (`0x00470F1A`)
keeps following. A failed idle re-follow clears +0x482 without a decrement. There is
no distance limit.

**Following elsewhere.** +0x482 survives combat. Combat_SelectAVictim
(`0x0045D5BF`–`0x0045D5F5`), after its first scan, replaces the best target with the
leader's +0x4D0 when both the leader and that target are live slots; +0x4D2, owner and
class are not tested, and the forced path skips it. Creature_Update
(`0x0048A78E`–`0x0048A7F1`) zeroes +0x4D0 only while possessed, when now > +0x4D2 + 3 ×
variable 19 (unsigned), so a leader that leaves possession keeps a stale +0x4D0; the
follower side never tests +0x4D2. A hypnotised follower's death also decrements the
leader's +0x488, which the hypnosis take never incremented.

STRONGLY INFERRED from windows (`0x00460586`, `0x00477163`, `0x00478D1D`): while +0x482
is set the flee state does not return to fight state 199, Creature_UpdateState skips
its mood block, Needs_IsBusy counts the creature busy and it gains no need-anger.
spFighting's fear clears +0x482 before fleeing, so a follower that drops the follow
can flee.

**Action 37** (`0x00516A20`, CONFIRMED) is a close recruit, not a recall. From the
possessed creature P's own position and heading +0xF4: P's tile first (things other
than P in the cone, class 0, P's owner, +0x482 = 0), else the 8 neighbours in the
melee order from table `0x006BC370`, the cone tested once at each tile's centre; the
nearest by (dx²>>12)+(dy²>>12) wins, strictly less. Below the cap it calls
`0x00461980(packet word 0)` and increments +0x488 without testing the result. No
sight test, no predicate first. No sender was found (immediate 0x25 scan; a computed
id not excluded): `0x0040FDF0` sends only 0xE and 0x27, and 0x26 comes from `0x0040FFB0`.

**State 261** (`0x00461860`, row `0x006DC22D`: group 8, category 29, text
"hypnotised") follows +0x482 like 246 without the possessed test, walk/run switch or
route option, re-routing every variable-19 ticks through `0x004D0F60`. No entry was
found (scans for 0x105 and the handler's address; a computed id or a save not
excluded); hypnosis uses 246.

+0x488 is read by trigger condition type 73 (`0x004C7B20` arm 23; a dead slot compares
0), so action 37's overcount would reach a trigger. `0x00420D10` reads +0x482 only as a
draw filter (UI). UNREAD: 246's resume while the leader moves; action 37's word 0.

The owner (2026-10-12): a follower that has reached a standing leader keeps shuffling, as the
re-route on every return to 246 predicts; a recruited Imp fights when the possessed creature
strikes a hero, as the leader's-target override predicts. Whether a full eight can be
recruited after a follower leaves or is sacrificed, and the dismiss key, are not known.

## 5. Command inputs — CONFIRMED unless marked

**Sneak, command 0x1D** (handler `0x00517270` → ToggleSneak `0x0048EE90`). Packet
dwords 0 and 1 are a raw tile x, y, unchecked. +0x4A3 = 0 when +0x4A3 is already set
(a second press), or the tile is the creature's own or neutral. Otherwise every
creature of every active player whose effective owner (+0x455 when +0x453 is set,
else +0x28) is allied to the tile's owner is a watcher; d = (dx²>>12)+(dy²>>12),
times the level-scaled def+0x760 percent / 100 when the sneaker's Creeping bit
(+0x3E6 bit 12) is set. A watcher with d < sight² × 0x1000 (sight = its def+0x77B,
strict) and a clear sight line unmasks it (+0x4A3 = 0); unseen, +0x4A3 = the tile
owner. No definition gate.

Readers: the four FightStyle scans (`0x0045B140`, `0x0045B500`, `0x0045B800`,
`0x0045BAD0`), Combat_SelectTargetNonFighter, Combat_NearestAttacker and
Combat_SelectAVictim each skip a candidate whose +0x4A3 equals the searcher's player
id: the disguised-as player's creatures ignore the sneaker, everyone else still sees
it. The permission predicate `0x0048E6A0` accepts the disguised-as keeper
(`0x0048E7C5`), but Hand_PickUp (`0x004BC5EF`–`0x004BC62B`) unmasks the creature and
refuses whenever +0x4A3 is set and the hand is not its owner's, the disguised-as
keeper included: the two disagree. Sight and doors do not read it.
It ends on a second press, on leaving possession (`0x0048EE70` masks +0x3E6 with
0x7FFFC7FF, clearing bits 11–13 and 31), and on a melee or
spell at a target whose owner is allied to the disguise (`0x0051496E`, `0x00515156`;
the spell tests the caster itself when there is no target). No timer.

**Hypnosis, command 0x1B** (`0x005163C0`). The pose and angles come from dwords 0–2
as in action 26. Candidates: the whole world creature list (world +8, next +8), each
an enemy of the sender (Player_IsEnemy), def+0x686 bit 16 (CanBeHypnotized), inside
the cone and in sight from the pose; no range, dying, possessed or
already-hypnotised test. The nearest by (dx²>>12)+(dy²>>12) from the pose wins,
replaced only when strictly less. Bit 9 (IsImmuneToTurncoat) on the pick: nothing
happens. Take: +0x4A9 = its owner; owner set (vtable +0x24) to the possessor's;
`0x00461980` makes it follow (refusable: its predicate wants Happy; the owner change
stands); +0x3EA bit 0 set; +0x4D9 = 0; the possessor's +0x4A7 = its id. A second
press releases: unlink the engagement (`0x0045EF50`), +0x4D9 = 0, owner from +0x4A9,
+0x4A9 = 0, bit 0 cleared, +0x4A7 = 0.

Each successful possessed melee start on a creature force-targets the hypnotised
creature at it and adds 1 to NHypnotisedAttacks +0x4DB. AttackCreatureHits +0x4D9 is
incremented at each attack launch in spFighting (`0x0048F3E0`). Creature_Update
(`0x00488C37`) drops the forced target (+0x3E6 bit 2, +0x3F9) and the engagement and
zeroes both counters when +0x3EA bit 0, +0x4DB ≠ 0 and +0x4DB ≤ +0x4D9 (unsigned): a
command lasts as many attacks as were ordered. That is not the end of hypnosis.
Hypnosis ends on the second press; when possession is left through action 21
(`0x00514590` releases it first at `0x005145D9`, the same restore), which the leave
command, upkeep failure (`0x004C13BA`) and the possessor's death (`0x0048810D`) post;
and on the hypnotised creature's death (`0x00487E07` zeroes the possessor's +0x4A7
without checking it names that creature). The direct exit `0x004BE090` → `0x0048EE70`
does not release it. No timer.

**Sniper, command 39** (`0x005171D0`) sets +0x3E6 bit 31 to packet bit 0 (AND
0x7FFFFFFF, OR dword0 << 31): an assignment, not a toggle, with no definition or
spell gate. Simulation readers: the free-aim range (`0x00515364`: 0x3000 when spell
Range +0x31 plus the bit is nonzero, else 0, which is unlimited) and shot +0x165
(`0x004984D0`), which doubles contact damage (`0x004AC420`). So for a spell with Range
0 sniper mode adds a 3-tile cap. The HUD `0x00420D10` reads it for presentation; the
input routine `0x0040FDF0` sends action 39 with the inverted bit (CONFIRMED).

**Shot range.** `Shot_Update` (`0x004AB2C3`–`0x004AB308`) destroys a shot when MaxRange
+0x155 ≠ 0 and dx² + dy² from StartPosition +0x159/+0x15D exceeds MaxRange², both
32-bit and compared unsigned; Z is ignored. Possessed range 0x3000 is 3 tiles.

## 6. Smaller answers

- Action 26 never reads dword2's 11-bit angle: its callees read words 0 and 1 only
  (the hypnosis command decodes the same field from its own packet). `0x00516050`'s third argument is dead (`RET 0xC`, one caller,
  always 1).
- The caster is a candidate of its own spell scan (and LIKELY of the follower scan,
  by the same argument; UNREAD there) but cannot win when the pose's X/Y
  equal its position: the vertical-only direction scores ≥ about 1200 against 256.
  Whether the client's pose always equals the position is UNREAD.
- A door's cell byte +0xB bit 0 in the spell scan (`0x00515A05`) is the Secret Door
  mark `Tile_SetSecretDoorFlag` (`0x00450FF0`) writes: a Secret Door is never a target while that mark is set. State 39
  (`0x0046781D`) clears it, STRONGLY INFERRED the imp reveal
  (`imp-task-objects-and-walks.md` §2), after which the door is a candidate.
- `World_TileCell` (`0x00508FC0`) has 399 indirect calls (vtable 0x1D8), not audited.
  The melee pick and ToggleSneak pass packet coordinates unchecked; a pose a creature
  can stand at is in bounds because every shipped border tile is solid.
- Player order: `Players_Activate` (`0x004C2460`) pushes each player on the head of
  the active list (world +0x7A, next +0x3A), so scans visit players in reverse
  activation order. Activation follows the level's player records (`0x0051EF80` →
  `0x004C2340`): LIKELY the last record in the file comes first.
