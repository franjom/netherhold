# Mechanic: combat — the damage sink, and what scales a blow

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-15. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- The reference install's `Creatures.kwd` and `GlobalVariables.kwd`, via `DK2.Data`

Original version: DK2 v1.70 (GOG)

Questions: **Q8** (the combat formula), Q313 (the attack site), and a
confirmation for `creature-levels.md`.

**Nothing here is built.** This is the first sitting on Q8, which was a one-line
`UNKNOWN` stub with no research page. It establishes where damage *lands* and
what scales it; it does not yet establish who swings, how often, or at whom.

---

## 1. The damage sink

**`FUN_004B2CE0` is the common creature-health routine**, and every health
change in the game goes through it. It was already described in
`creature-death.md` §4 without an address, found here from the one live reader
of `CreatureDyingStateDurationSeconds` at `0x006F25B0` (`0x004B2FA1`, inside it).

It is `__thiscall` on the **target**, with three stack arguments. One call site
reads, in push order:

```
PUSH 0                 ; mode — 0 is the ordinary one
PUSH <word>            ; the source: a thing id out of the caller's context
PUSH <signed delta>    ; health change, negative to hurt
MOV  ECX, <target>
CALL 0x004B2CE0
```

That settles a reading `creature-death.md` had to state in prose: the
**"ordinary mode" is a literal third argument of zero**, and the dying-countdown
mode is the same routine called with a different value. The early return for a
creature already flagged `CreatureDying` is therefore a branch on this argument,
not on a global.

It has **40 callers**, which is the measure of how much of the game is health.
Four are already named in this project's notes: the prison drain `FUN_0047B7C0`
(`prison.md`), `FUN_004DAE40` from `DungeonHeart.cpp` (`structure-blocks.md`),
and the two level-change routines in §4 below.

## 2. Three creature-instance offsets, from the same reading

| offset | field | how it was established |
|---|---|---|
| `creature+0x40` | **current health** | subtracted from a level-scaled maximum, and the remainder capped by `HpFromChicken` (§3) |
| `creature+0x370` | **pointer to the creature definition** | dereferenced, then read at `+0x692` = `Hp` (§3) |
| `creature+0x403` | **experience level** | §4 |

`creature+0x36F` was already the creature type id (ADR-0115), so `+0x370` sits
directly beside it, which is what a loader that stores an id and then resolves it
to a pointer would produce.

## 3. The trap this sitting walked into first

`FUN_00514830` reads `MeleeAttackType` **and** calls the health routine, which is
why it was opened first. **It is not combat.** It scales `def+0x692` by the
level percentage, subtracts `creature+0x40`, and caps the result at
`def+0x694` — and the loader's copy pairs name those two fields **`Hp`** (file
`0xF0C`) and **`HpFromChicken`** (file `0xF0E`). It is the healing handler: heal
by `HpFromChicken`, but never past the level-scaled maximum.

It has **no callers**. It is reached through a dispatch table of 20-byte entries
around `0x006A5894`, the handler sitting at `+0x10` of its entry, which is worth
recording because the combat handlers are likely to be its neighbours (Q313).

Named here so the next reader does not spend the same hour on it.

## 4. What a level does, confirmed from the binary

`creature-levels.md` read the **32 × 9** stat table out of `GlobalVariables.kwd`
and called the table and its indexing CONFIRMED. The runtime side now agrees,
independently.

**The cached global block.** `[0x006E4154]` holds `0x006F2550`, the base of the
cached balance block — the same block `balance-variables-in-the-binary.md` maps,
where `CreatureDyingStateDurationSeconds` sits at `+0x60` and
`PrisonModifyCreatureHealthPerSecond` at `+0xD1`.

**The stat rows follow the scalars**, each a run of **nine `int16`** covering
levels 2–10, addressed in code by a base pre-biased so that `+ level*2` lands on
the right entry:

```
row(stat) = 0x006F2550 + 0xE1 + (stat - 1) * 18
```

| stat | id | row |
|---|---|---|
| Health | 1 | `+0xE1` |
| Fear | 2 | `+0xF3` |
| Threat | 3 | `+0x105` |
| **MeleeDamage** | 4 | `+0x117` |
| Pay | 5 | `+0x129` |
| MaxGoldHeld | 6 | `+0x13B` |

Health and MeleeDamage were read off their consumers. The other four are a
**prediction from the 18-byte stride that was then tested**: each predicted
offset does appear in the binary, in exactly the
`MOVSX word ptr [reg + idx*2 + row]` shape the formula requires. Six consecutive
stat ids on one arithmetic rule.

**The application, and it is ours already.** Every site has the same shape:

```
if level == 1        -> use the record value unchanged
else                 -> value = record * (signed int16) row[level] / 100
```

`CreatureLevelRules.Scale` is `value * percent / 100` and
`GameDatabase.StatPercent` returns 100 for `level <= 1`. **Both halves match the
original instruction for instruction**, including the sign: the binary reads the
percentage with `MOVSX`, and this project's `_wideStatPercent` already carries
values that do not fit a byte.

So the level mechanism moves from *read off the shipped table* to **CONFIRMED
against the executable**, and the divide by 100 is a real signed divide there
(the `0x51EB851F` multiply-and-shift), not a rounding convention we chose.

**Who writes the level.** Only four instructions store to `creature+0x403`:
one sets the literal **1** (`FUN_004872A0`, a creature being made), two compute
it in `FUN_0048AFA0` and `FUN_0048B120` — both of which are also health-routine
callers — and one is an unrelated struct. `FUN_0048AFA0` **decrements** it, so
DK2 has a level-*loss* path; what triggers it is UNKNOWN and this project has no
such path (`CreatureLevelRules` only grants).

## 5. The melee block in the creature definition

Recovered from the loader's copy pairs, with the known pair
`src=0x12F8 → dst=0x6BB` (`library-and-research.md`) passing as the control:

| field | file | runtime |
|---|---|---|
| `MeleeRange` | `0xEEC` | `def+0x5C0` |
| `MeleeDamage` | `0xF14` | `def+0x5C4` |
| `MeleeRecharge` | `0xEF8` | `def+0x5C6` |
| `MeleeAttackType` | `0xF50` | `def+0x77A` |

**Their consumers are few, which is the useful part.** Ignoring the definition
copier `FUN_0051F3B0`, which reads all four and writes them straight back:

- `def+0x5C4` **MeleeDamage** — read twice, both in `FUN_00494D00`
- `def+0x5C0` **MeleeRange** — read once, in `FUN_0045E190`, a two-instruction
  accessor
- `def+0x5C6` **MeleeRecharge** — read once, in `FUN_00494EF0`
- `def+0x77A` **MeleeAttackType** — read in an unnamed block at
  `0x00499068`–`0x004991B6`, and in the healing handler of §3

### 5.1 `FUN_00494D00` is the melee-damage accessor

`__thiscall` on the **definition**, taking the **creature** as its argument —
the opposite way round from most of this codebase's assumptions, and worth
stating because it is how the level gets in.

It builds a multiplier in **12-bit fixed point** starting at `1.0` (`0x1000`),
and three instance flags each fold a factor into it before the base damage is
read:

| flag | ceiling pushed | balance value used |
|---|---|---|
| `creature+0x3E6` bit 7 | `100.0` | global `+0x31B`, `int16` |
| `creature+0x3EA` bit 3 | `200.0` | global `+0x31B`, `int16` |
| `creature+0x3E6` bit 15 | `100.0` | global `+0x482`, **byte** |

Each pair goes through the same two helpers (`0x0043D750`, `0x00441990`) before
a fixed-point multiply (`IMUL` then `SHRD …, 0xC`), which is the shape of a
clamp between two values rather than a plain product. Only then is `def+0x5C4`
read and scaled by the level row of §4.

**What those three flags are is UNKNOWN.** Two share a balance value and differ
only in their ceiling, which is what a *weakened* and a *strengthened* state
would look like — but naming them from the shape would be a guess, and this
project does not name mechanisms before reading their consumers.

## 6. What is still unknown, in the order it should be taken

1. **Who calls `FUN_00494D00`** — the swing itself. That caller holds the
   attacker, the target, and the moment, and it is the whole of Q313.
2. **`MeleeRecharge`'s consumer** `FUN_00494EF0` — the cadence, and the first
   thing that can be checked against a stopwatch.
3. **How `MeleeAttackType` meets a defence.** The unnamed block at
   `0x00499068`–`0x004991B6` reads it four times over. `CreatureDef` carries a
   per-creature `Resistances` list; nothing here has yet shown the two meeting.
4. **Experience from a kill.** `creature-levels.md` records that nothing earns
   by fighting here because combat does not exist; the adder `FUN_00457880` is
   already read, so this is a question about its callers.
5. **What decrements a level** (§4).

## Confidence

**CONFIRMED (binary):** the health routine and its argument shape; the three
creature offsets; the melee block's runtime offsets; the stat matrix's runtime
layout and the `× percent / 100` rule with level 1 exempt; that `FUN_00514830`
is healing and not combat.

**STRONGLY INFERRED:** that `FUN_00494D00` is *the* melee-damage accessor rather
than one of several — it is the only reader of `def+0x5C4` that is not the
definition copier, which is strong but is an absence.

**UNKNOWN:** everything in §6. No damage formula end to end, and no attack.
