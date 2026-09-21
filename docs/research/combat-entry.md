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

## 7. What is left

**How a creature reaches the fight at all.** State **200 is never set by a
literal** anywhere in the executable; state **199** — same label, handler
`0x0045eb20` — is, from four sites, and that handler does its once-only work and
then **calls `spFighting` directly** (`0x0045ec01`). Two of the four sites are
inside the fleeing-adjacent sibling `FUN_004602e0` and one is at `0x0046172a`, so
combat largely re-enters itself; the outside entry is **`FUN_004752b0`**, at
`0x0047540e`, and it is unread.

That, and whatever writes `creature+0x502`, is the whole of what stands between a
creature standing still and its enemy losing health.

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

**UNKNOWN:** what writes `creature+0x502`, what `FUN_004752b0` is, the rest of
the record's layout, and what `AttackingSpecificCreature` is.
