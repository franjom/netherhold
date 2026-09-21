# Mechanic: how a creature enters combat — `spFighting` and the AttackType switch

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-21. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- `Data/Text/Default/Text.str` from the reference install, for the state labels

Original version: DK2 v1.70 (GOG)

Questions: advances Q8. `combat.md` owns the swing and the damage sink,
`combat-damage.md` the arithmetic, `combat-cadence.md` the two clocks,
`creature-action-table.md` the action table this one dispatches into.

**What this settles:** the chain from *a creature decides to fight* down to
*health goes down* has one gap left, and it is no longer the middle. Combat is a
**creature state**, not a creature action and not a player command.

---

## 1. The chain

| step | what | where |
|---|---|---|
| 1 | creature enters **state 199 or 200, "engaging in battle"** | state table `0x006DA8B0` |
| 2 | its handler is **`spFighting`**, `0x0045f030` | registered at `0x0047f027` |
| 3 | `spFighting` switches on **AttackType** | `0x0045fb4d` |
| 4 | it starts creature **action 16** (spell) or **17** (melee) | `0x004969d0` |
| 5 | action 17's handler swings | `0x00498E30`, `combat.md` §6 |
| 6 | damage reaches the health routine | `FUN_004B2CE0`, `combat.md` §1 |

**`spFighting` is DK2's own name**, not ours and not Flame's: the routine prints
it. Two of its assertions read *"Creature type %d tag %d is possessed but has
called spFighting"* and *"... is not a fighter but has called spFighting"*. The
same rule as `Follow_Path` applies — an identifier the original ships is the
original's (ADR-0080, and the memory it came from).

## 2. The states, from the registration run

One straight-line run at `0x0047f000` fills consecutive 25-byte records. The
stores land 25 apart, which recovers the index directly:

| state | label (`Text.str`) | handler |
|---|---|---|
| 199 | engaging in battle | `0x0045eb20`, which tail-calls `spFighting` |
| **200** | **engaging in battle** | **`0x0045f030` `spFighting`** |
| 201 | fleeing | `0x00484940` |
| 202 | fleeing | `0x00461850` |
| 203 | fleeing | `0x004602e0` |

Our `CreatureStateTable` already carries all five as `Cat.Combat`, from the
table's own `+0x14` string ids — so this is the handler column arriving for rows
that were already right, not a correction.

## 3. The switch is on AttackType

```
AL  = byte[creature + 0x529]
ECX = AL & 0xF          ; the arm
CMP ECX,4 / JA default  ; five arms, 0..4
JMP [ECX*4 + 0x4602c0]
```

The **high** nibble of the same byte is shifted down and passed on as an argument
to the spell arm, so one byte packs the attack kind and a second small id.

| arm | what it starts | the assertion if it fails |
|---|---|---|
| 0, 2 | creature action **16** | *failed to set spell instance for spell %d at target %d* |
| 3 | creature action **16** | the same |
| 1, 4 | creature action **17** | *failed to set melee instance at target %d* |
| default (5+) | logs only | *Creature type %d, tag %d, Attacking %d, AttackType %d* |

So **action 17 is the melee instance and 16 is the spell instance**, named by the
original's own assertions rather than inferred from the handler.

## 4. Only two places in the executable start action 17

Reading the literal pushed at every call of `0x00496700` and its three wrappers —
109 sites, of which 4 compute the index instead — gives the whole map of who
starts what (`creature-action-table.md` §4). Action **17** has exactly two
starters, and they are the two ways a creature can swing:

- `0x00514da4`, inside game action 26's handler — **the player, possessing**
  (`game-actions.md` §3)
- `0x00460103`, inside `spFighting` — **the creature, by itself**

Action 16 pairs with it at both: twice in the same handler, twice in `spFighting`.
That the possession path and the AI path start the same two actions is what makes
the action table the right place for combat to live rather than either caller.

## 5. What `spFighting` also does

Its remaining assertions name three more jobs, none of them read yet:

- **Target validity.** *"Creature type %d, tag %d, attacking invalid tag %d."*
- **Re-acquisition.** *"failed to re-aquire previous target type %d, tag %d,
  state %d"* — so a target is remembered across something and looked up again.
- **Fear.** *"Stats or level issue, creature type %d has fear threshold of %d on
  this level"*, which is what the three **fleeing** states next door are for.
- **`AttackingSpecificCreature`**, a named quantity printed as two numbers by the
  possessed and not-a-fighter assertions.

## 6. The engagement record is `creature + 0x4FE`

`spFighting` rebases: `LEA EDI,[EAX + 0x4FE]` at `0x0045f1b2`, and **EDI is not
written again on any path that reaches the switch** — the block has exactly one
entry, `JNZ 0x0045fb23` at `0x0045f83a`, and every other write to EDI in the
function is past it. So the small offsets at the switch are creature offsets:

| at the switch | really | what |
|---|---|---|
| `word[EDI + 0x04]` | `word[creature + 0x502]` | the **target's thing tag** |
| `byte[EDI + 0x2B]` | `byte[creature + 0x529]` | **AttackType** low, a second id high |

State 199's handler agrees from the other side: it computes `ESI = creature +
0x4FE` outright and reads `[ESI + 0x2B]` with the same `AND 0xF` test, and it
resolves `word[creature + 0x502]` through the liveness table `0x006ED540` and the
global thing table `0x006EE540` — the standard tag lookup (`room-list.md`).

**This settles a field the swing reads and `combat.md` §6.2 could only name.**
The swing takes its target from `word[attacker + 0x399]`, which sits inside the
**34-byte current-action block** at `+0x38D`–`+0x3AE` that `FUN_00496700` fills
(`creature-action-table.md` §3) — as does `+0x3A5`, the turn stamp the recharge
gate compares. And the value `spFighting` passes to the action-start wrapper is
`word[creature + 0x502]`. So the two target fields are one target moving:

> `creature+0x502` is the **engagement's** target, held by the state; beginning
> action 17 copies it into the action block, where `creature+0x399` is the
> **swing's** target. The state picks the enemy; the action carries it.

## 7. Acquisition drives the state, not the other way round

**`spFighting`'s first act is an acquisition gate**, and it is the four
instructions after the rebase:

```
CMP word[creature + 0x502],0   ; do I have a target?
LEA EDI,[creature + 0x4FE]
JNZ  ...                       ; yes -> straight on
CALL 0x0045cb00                ; no  -> acquire one
TEST EAX,EAX / JZ ...          ; acquisition can fail
```

So the target field is the fight's condition, and **`FUN_0045cb00` is the
acquirer**. It has twelve callers: **six inside `spFighting` itself** — which is
what the *"failed to re-aquire previous target"* assertion is about — one inside
game action 26's handler, the possession path, and four elsewhere
(`0x0045e9f0`, `0x00470e30`, `0x00481400` twice, `0x00488930`).

**`FUN_0045cb00` calls `FUN_0045bdf0`**, and that is where a target is chosen.
It writes `creature+0x502` twelve times and, between two of them, calls
`FUN_004752b0` — one of the four sites that `SetState`s **199**. The twelve are
not twelve rules; §9 reads them.

That reverses the obvious reading. A creature does not enter "engaging in battle"
and then look for someone to hit; **it acquires a target and entering the state
is a consequence**, written on the same branch. Which is why state 200 is set by
no literal anywhere and 199 by four: the states are reached through acquisition
and through combat re-entering itself, never announced from outside.

The field's writers, for whoever reads `FUN_0045bdf0` next: **43 stores to
`+0x502` across twelve functions**, concentrated in `FUN_0045bdf0` (12),
`spFighting` (7), `FUN_0045cb00` (6), `FUN_004602e0` (5) and a cluster at
`0x0045a550`–`0x0045b000` (7).

## 9. The NonFighter selector judges one candidate, and rolls back

`0x0045BDF0` is `thiscall (CCreature*, int32) -> int16` — a **tag** out. §10 shows
it is the **NonFighter** arm, which only the **Imp** and **Jack** ever run, and
everything in it reads as that once you know: it is not a search over every
enemy but a judgement of **one candidate creature**, held in `ESI`, which it
either commits to or puts back.

**It opens by measuring the way home** — which is what a worker would do. Before anything else it takes the
creature's own player (`creature+0x2A`), asks it for the dungeon heart's
position, converts that tile to 1/4096 units with a half-tile centre offset
(`<<12`, `+0x800`), and squares the absolute differences against the creature's
own position at `+0x1A`/`+0x1E`. So **the creature's squared distance from its
own heart is an input to the decision**, computed first. Flame's map calls that
accessor `getDungeonHeartPosition`; the instructions say the same thing
independently, which is the only reason it is repeated here (ADR-0080).

**The acceptance gate is mutual.** A candidate passes only if

```
word[cand + 0x502] == 0        ; it has no target of its own
  or word[cand + 0x502] == our own tag   ; or its target is us
and byte[cand + 0x3EA] & 0x40  ; and it carries this flag
```

so it engages **only somebody already coming for it**. For a non-combatant that
is the whole rule: fight back, never pick a fight.

**The commit is tentative, and this is what the twelve stores are.** On
acceptance the candidate's tag goes into `creature+0x502` and **the previous
target is stashed** in a local. A predicate then runs (`0x00460b50`), and a map
query with `0x5000` — five tiles in 1/4096 units — follows. If a check fails the
stashed tag is **written back**. So:

| stores | what they are |
|---|---|
| 1 | take the new candidate |
| 5 | **restore** the previous target after a failed check |
| 6 | **clear** the target, some also zeroing two word arrays at fight `+0x6` and `+0x16` |

**A correction to this document.** Its previous revision said the selector
"writes the target on twelve separate branches", which read as twelve ways of
choosing an enemy. Eleven of the twelve are rollback and teardown; exactly one
takes a target.

## 10. `FightStyle` picks the selector — the field's consumer, found

Inside the acquirer, immediately before the call, sits a five-arm jump table:

```
EDX = creature[0x370]          ; the compact definition
AL  = byte[EDX + 0x67E]        ; 0..4, else the default arm
JMP [EAX*4 + 0x45E174]
```

`def+0x67E` is loaded from **file offset `0x0F03`**, by a guarded store at
`0x0049347A` taking `EBP+0x12F3` — the `0x3F0` frame delta again. That offset is
**`FightStyle`**, which this project already parses and *nothing reads*
(`KwdCreatures`, `manual-1999.md` § the editor's enum). **So `FightStyle` selects
which routine chooses a creature's enemy.** It also fills the slot `training.md`
§4 observed the experience run skipping.

**The table is not in index order**, which is worth stating because assuming it
was inverted the reading once:

| `FightStyle` | routine | size | the shipped creatures |
|---|---|---|---|
| 0 `NonFighter` | **`0x0045BDF0`** | 0x7DC | Imp, Jack |
| 1 `Blitzer` | `0x0045B800` | 0x2C5 | Mistress, Vampire, Dark Angel, Horny, King Reginald, + 5 uniques |
| 2 `Blocker` | `0x0045B140` | 0x2D7 | Bile Demon, Troll, Giant, Knight, Black Knight, Guard, Royal Guard, Stone Knight, Lord Of The Land, three princes, Maiden Of The Nest, + 2 uniques |
| 3 `Flanker` | `0x0045BAD0` | 0x315 | Goblin, Skeleton, Salamander, Firefly, Dwarf, Thief, Monk, Fairy, Rogue, + 4 uniques |
| 4 `Support` | `0x0045B500` | 0x300 | Warlock, Dark Elf, Wizard, Elven Archer, + 2 uniques |

All five take `(creature, int32)` and return a **word** — a tag — and the return
is compared against the sentinel **`0xEA60`** (60000). The rosters are the whole
shipped file, 46 records read at `0x0F03`, and they corroborate DK2's own names
without any of the four unread arms being opened: `Support` is exactly the four
ranged archetypes and their uniques, `Blocker` exactly the heavy melee, and
`NonFighter` exactly the two creatures that do not fight.

**That the biggest of the five is `NonFighter` is the finding**, not a curiosity.
Deciding *not* to fight is the complicated case: §9's distance to one's own
dungeon heart, its two calls into a withdraw-from-fight state, its
engage-only-what-engages-me gate and its heavy rollback are all a worker's
reluctance, and none of it generalises to how a Knight picks a fight.

**Before the dispatch, a re-acquisition path.** `word[creature + 0x482]` is
resolved through the liveness and thing tables; if it is live, `word[thing +
0x4D0]` is read and, if *that* is live, it becomes the answer and **no strategy
runs at all**. This is what `spFighting`'s *"failed to re-aquire previous
target"* assertion is about.

**The strategy byte is on the definition, not the creature**, which is settled
rather than assumed: `CCreature` is `0x53F` bytes, so `+0x67E` cannot be on it.
Its only three writers are that definition's **constructor**, a
definition-to-definition **copy**, and `FUN_00492320` (`combat-cadence.md` §5
asks separately whether that one is live). **No file-load site is among them**,
so whether the strategy id is shipped per creature or computed is UNKNOWN — and
it cannot be answered by subtracting `0x3F0` from `0x67E`, because that constant
maps a file offset to the loader's **staging** struct, not to the runtime one
(`library-and-research.md` has the worked pair).

## 11. What is left

**The four selectors that matter for a fight** — `Blitzer`, `Blocker`, `Flanker`
and `Support` — none of which is read. Also: the two predicates the NonFighter
commit is tried against (`0x00460b50`, and `0x004512F0` with its five-tile
argument), what `+0x3EA` bit 6 means, what `AttackingSpecificCreature` is, and
the rest of the `+0x4FE` record.

## Confidence

**CONFIRMED (binary):** the registration run and its 25-byte stride; that state
200's handler is `0x0045f030`; the five state labels through `Text.str`; the
switch's selector, its five arms and which action each starts; that action 17 has
exactly two literal starters; every quoted assertion, which is the binary's own
text.

**STRONGLY INFERRED:** that arms 1 and 4 are melee and 0, 2, 3 spell — the
assertion sits on the failure path of each, which is one instruction removed from
the start it guards.

**CONFIRMED (binary):** that the switch's base is `creature + 0x4FE`, by the
single entry into the block and the absence of any EDI write on that path; that
state 199's handler ends in a call to `spFighting`; that state 200 is set by no
literal and 199 by four.

**CONFIRMED (binary):** the acquisition gate's four instructions; that
`FUN_0045cb00` has exactly twelve call sites and six of them are in `spFighting`;
that `FUN_0045bdf0` writes `+0x502` twelve times and calls `FUN_004752b0`, which
sets state 199; the 43-store census of `+0x502`.

**CONFIRMED (binary):** that the selector measures the creature's squared
distance to its own keeper's heart before anything else; the mutual acceptance
gate; that the commit stashes the previous target and restores it on failure, so
one of the twelve stores takes a target and eleven undo or clear.

**CONFIRMED (binary):** the five-arm dispatch, its jump table read entry by
entry, and all five targets; that the five share a signature returning a tag;
the re-acquisition path ahead of it; that `+0x67E` is a definition field, from
`CCreature`'s size; the loader pair `EBP+0x12F3` → `def+0x67E` and so the file
offset `0x0F03`.

**CONFIRMED (data):** the `FightStyle` of all 46 shipped creatures, read from
`Creatures.kwd` at `0x0F03`.

**STRONGLY INFERRED:** that `0x0045BDF0` is the non-combatant rule rather than a
general one — it is what `FightStyle` 0 dispatches to and its two creatures are
the Imp and Jack, but the other four arms are unread, so nothing here compares
them.

**UNKNOWN:** the four unread selectors, the two predicates the NonFighter commit
tries, `+0x3EA` bit 6, the rest of the `+0x4FE` record, and what
`AttackingSpecificCreature` is.
