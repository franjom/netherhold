# Mechanic: combat — the damage sink, and what scales a blow

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-15. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- The reference install's `Creatures.kwd` and `GlobalVariables.kwd`, via `DK2.Data`

Original version: DK2 v1.70 (GOG)

Questions: **Q8** (the combat formula), Q313 (the attack site), and a
confirmation for `creature-levels.md`.

**Nothing here is built.** Two sittings on Q8, which was a one-line `UNKNOWN` stub
with no research page. They establish where damage **lands** (§1), what **scales**
it (§4, §5) and **who swings** (§6). Still missing: the cadence, how a defence
reduces a blow, and what a kill awards — §7.

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
| — (unnamed, Q336) | `0xCF9` | `def+0x5CA` |

**Their consumers are few, which is the useful part.** Ignoring the definition
copier `FUN_0051F3B0`, which reads all four and writes them straight back:

- `def+0x5C4` **MeleeDamage** — read twice, both in `FUN_00494D00`
- `def+0x5C0` **MeleeRange** — read once, in `FUN_0045E190`, a two-instruction
  accessor
- `def+0x5C6` **MeleeRecharge** — read once, in `FUN_00494EF0` (6 callers).
  It is **not** the swing's interval: `combat-cadence.md` settles Q327
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

## 6. The swing, found 2026-09-15, named 2026-09-16 (Q313 — ANSWERED)

**`FUN_00494D00` has exactly one caller: `FUN_00498E34`.** That is Q313's whole
question, and it is now answered from two directions that agree — `CallScanAll`
found the call site as bytes before the region was analysed, and `AtAndCallers`
attributes it to a named function after `ForceGaps.py` defined one there (§6.0).
So the accessor's caller IS the attack routine, which is what the hypothesis
below predicted.

The rest of this section is how it was found, and §6.2 is what the routine does.

`FUN_00494D00` had **no callers and no address references anywhere in the image**.
That is not the same as having none: a direct `CALL` encodes a *relative*
displacement, so a byte scan for the absolute address cannot see it, and Ghidra's
own xrefs are blind wherever code was never analysed. `CallScanAll` finds the one
site, `0x00498EC8`, in an **unanalysed region** — the same region that reads
`MeleeAttackType` four times over. The attack code was simply not disassembled.

The swing runs from `0x00498E96`, inside a larger handler whose entry is earlier:

1. **The target comes off the attacker**: `word[attacker+0x399]` is a thing id,
   checked against the liveness byte table at `0x006ED540` and resolved through the
   global thing table `0x006EE540`. So **`creature+0x399` is the current target**.
2. Refuse if `target+0x16 == 2`.
3. `damage = FUN_00494D00(attackerDef)(attacker)` — §5.1's accessor, which
   confirms its calling convention from the other side: definition as `this`,
   creature as the argument.
4. **Negate it.** Damage reaches the health routine as a negative delta.
5. A virtual call on the target, vtable slot `+0x34`, must return non-zero — a
   "does this take a hit at all" predicate.
6. **A bonus percentage off the bearing between the two things.** Corrected
   2026-09-16 — this said "a 10-bit field divided by 8", and the divisor is **20**:
   `0x66666667` with `SAR EDX,3` is a signed divide by 20, and reading the shift
   alone gave 8. The arithmetic, instruction by instruction at `0x004990C4`:
   `FUN_0065FB54(attacker.x - target.x, attacker.y - target.y)` — an atan2 over
   the position triple at `+0x1A`/`+0x1E` — then `& 0x7FF` (2048 to a turn),
   reflected about zero as `0x7FE - a` when above `0x3FF`, divided by 20 to give
   0–51, and **if that exceeds 15**, `damage += damage * it / 100`.
   **The arithmetic is CONFIRMED and the purpose is UNKNOWN (Q325).** Nothing in
   it reads either thing's facing, so as written the bonus is a function of the
   compass bearing from target to attacker — up to +51% for attacking along one
   axis and nothing along the other. That is a strange combat rule, which is
   reason to doubt the reading of its *purpose*, not the reading of its
   instructions.
7. **A branch per target kind** on `target+0x12`, the thing's class. Kind 0 is
   handled separately and earlier; kinds 2, 3 and 4 each read one byte out of the
   target's own definition and call `FUN_004998A0` with it (§6.1).
8. **Two per-player multipliers**, both verified instruction by instruction on
   2026-09-16 and both skipped when the byte is zero or less:
   `byte[player+0x1090]` applies `damage * (v + 10) / 10`, and
   `byte[player+0x1091]` applies `damage * (15v + 100) / 100` — **+15% a unit**,
   where before only the first was read. What they are is still UNKNOWN: a keeper
   spell, a handicap or a difficulty setting would all look like this.

### 6.0 The attack routine is `FUN_00498E34`, and nothing references it

The call site above sat in code Ghidra had never turned into a function, so every
caller query over it answered "in -". Measured on 2026-09-15: **25.2% of `.text`
lay outside any defined function**, 1,953 runs of it, and **1,652 of those runs
were already disassembled** — known to be code, simply unattributed. The heads
are `83 EC xx`, an ESP frame, which is why `ForceFunc.py`'s walk back to
`push ebp; mov ebp,esp` never reached them.

`ForceGaps.py` (new) defines a function at the head of every uncovered run. It
took the block from **8,338 functions to 10,186** and the gap from 25.2% to
**16.2%**, and the four `MeleeAttackType` reads now attribute to one function:
**`FUN_00498E34`**, which is the attack routine's entry and contains §6's whole
sequence. The positive control still returns 226.

**Nothing in the image refers to that function.** No `E8`/`E9` rel32 targets it,
and its address appears nowhere as bytes — not in `.text`, not in `.rdata`, not
in `.data`. The function before it ends in a clean `RET` and does not jump in.

**The likely reason, and it is testable.** `creature-state-table.md` puts the 281
state records at `0x006DA8B0` with each handler at `+0`, inside
`0x006CCC00`–`0x007A6DCF` — a region **not present in the file on disk**. A
handler reached only through a table built at runtime would look exactly like
this. So the HYPOTHESIS is that the attack is a **creature state handler**, and
the probe is to find which state's record holds `0x00498E34`. What writes that
table is not a literal store of the address, because no such store exists.

### 6.2 The rest of the routine, read 2026-09-16

**The recharge gate is the first thing it does.** `now = FUN_00517490()` against
`dword[attacker+0x3A5] - 1 + dword[attackerDef+0x5CA]`; not yet, and it returns 1
without swinging. So `+0x3A5` is the **last-swing stamp** and `def+0x5CA` the
interval. That is **not** `MeleeRecharge` at `def+0x5C6` (§5), four bytes earlier
— **Q327, answered 2026-09-16: a second field**, from file `0xCF9`, in ticks
(`combat-cadence.md`). §5 was right and the swing does not wait on it.
The gate is skipped entirely when `attacker+0x3E6 & 0x80` is set.

**A definition flag is copied into the creature on every swing.**
`creature+0x3EA` bit 12 := `def+0x686` bit **29**. Bit 29 is unnamed in our
`CreatureFlags` — the enum jumps 28 to 30 — and this is a reader for it (Q326).

**The Monk carries the permanent kill, and it is a parameter rather than a
state.** At `0x00499224`, if `byte[attacker+0x36F] == 0x13` — creature type 19,
the **Monk** — and the target is a creature, the routine does exactly this:

```
MOV ECX,[ESI + 0x3ea] / OR ECX,0x4 / MOV [ESI + 0x3ea],ECX   ; set bit 2 on the TARGET
CALL 0x004b2ce0                                              ; deal the damage
MOV EAX,[ESI + 0x3ea] / AND AL,0xfb / MOV [ESI + 0x3ea],EAX  ; clear it again
```

Set, damage, clear. A bit held only across one call is an **argument smuggled
through the damage sink**, not a status — which is how DK2 tells the death path
who struck the blow without widening `FUN_004B2CE0`'s signature. What consumes it
is the resurrection routine; see `creature-death.md` §2.3, and it closes the
owner's *"a monk's kill is permanent at any level"*.

**Two Horned Reaper branches**, both keyed on `def+0x686 & 0x4000`, which is
`IsHorny` in our own enum. One fires when attacker and target share an owner
(`word+0x28`) and ends the swing early through virtual slot `+0x20`; the other,
at `0x00499289`, fires when the target's type is `0x1C` — **Stone Knight** — and
calls `FUN_00490120`. Neither is read further (Q328).

### 6.3 What the Monk's bit reaches: reincarnation (Q111)

`FUN_0048FFC0` is the vampire's reincarnation. Its one caller is the death block
of `FUN_004B2CE0`, on the arm where `def+0x686 & 0x800000` — bit 23,
`CanBeResurrected`, set on exactly the two Vampires — is set. It proceeds only
when **both**:

    1 < byte[creature+0x403]          the level, so level 1 is permanent
    (byte[creature+0x3EA] & 4) == 0   the bit §6.2's Monk branch sets

and then restores health to the level-scaled maximum, calls **`FUN_0048AFA0`**
— the level decrement, `level - 1` guarded by `level != 1`, carrying the health
*fraction* across the change — and clears `+0x3E6 & 0x8000`.

**Three CONFIRMED owner statements land on one routine** (Q111, 2026-09-04):
*"they lose a level once they reincarnate"*, *"if they were killed at level 1
they are permanently dead"*, and *"a monk's kill is permanent at any level"*.
`creature-death.md` §2.3 carries the rule; this is the mechanism.

**`FUN_0048AFA0`'s other caller is `FUN_0049C2A0`, and it is unread** — a second
way to lose a level that nothing here has looked at (Q328).

### 6.1 `FUN_004998A0` is not the resistance step

It was opened expecting resistance, and it is worth recording that it is not, for
the same reason §3 is recorded.

It searches a **hardcoded table at `0x0069DA38`** — in `.data`, with values
already in the executable rather than loaded from a KWD — of **37-byte records**
keyed by a byte at offset 0, linear, terminated by `0xFF`. The key it searches for
is the **attack type**; the second argument, a byte from the target's definition,
then picks one of **seven** arms through a jump table at `0x00499A24`, and each arm
reads a **pair of 16-bit values** from that record (37 = 1 + 8 + 7 x 4).

**It never touches the damage.** It takes an out-pointer, returns `RET 0xC`, and
the damage register is untouched across all three calls. The values are in a
170–450 band rather than looking like percentages. So this is most likely **hit
presentation** — which effect a given attack type makes on a given surface —
and calling it armour would have been a guess dressed as a finding. What the pairs
are is UNKNOWN; cross-referencing them against the shipped effect table's id range
is the cheap next check.

### 6.1.1 `Resistances` is loaded, printable, and consulted by nothing

Traced 2026-09-16, and the answer is a negative. The runtime array is at
`def+0x11FC` — the loader `FUN_00568E30` copies four 2-byte pairs there from file
`+0xE0C` (`0x0056983E`), which also confirms the +0x3F0 file-to-runtime mapping
against an instruction. Two bounds-checked accessors read it: `FUN_00569D90`
returns slot *i*'s attack type, `FUN_00569DB0` its value, both refusing `i >= 4`.

**Each accessor has exactly one caller: the debug dumper `FUN_00492320`.**
`0x11FC` occurs **once in the whole executable**, and neither accessor has a
single absolute occurrence in any initialised block, so neither sits in a vtable.
Positive control on the same sitting: 226. So in v1.70 nothing computes damage
from this table, which is why §6.1's hunt kept failing — the step it looked for
is not there. Residual doubt is one shape only, a reader forming the base from a
*different* displacement, which the accessors' existence argues against. Q335.

`0x004E0460` was named here as the next place to look and **that was wrong**: it
is `[this+0x25E] == 0x53`, a creature-state test called from target-resolution at
`FUN_00498E34+0x101`, before the damage step. Twenty bytes on, the routine sets
state `0x54` through `[ESI+0x24E]`, so `+0x24E` is a state machine and `+0x25E`
its current id. That is evidence for item 1 below, not for resistance.

## 7. What is still unknown, in the order it should be taken

1. **Which state record holds `FUN_00498E34`** (§6.0). It decides whether combat
   is a creature state, and with it how an attack is entered at all.
2. ~~**`MeleeRecharge`'s consumer**~~ — **ANSWERED 2026-09-16** (Q327): it
   gates a second clock, not the swing. `combat-cadence.md`.
3. ~~**Where the `Resistances` list is read**~~ — **ANSWERED 2026-09-16, in the
   negative**: by the debug dumper and by nothing else (§6.1.1). What applies
   resistance, if anything does, is Q335.
4. **Experience from a kill.** `creature-levels.md` records that nothing earns
   by fighting here because combat does not exist; the adder `FUN_00457880` is
   already read, so this is a question about its callers.
5. ~~**What decrements a level**~~ — **ANSWERED 2026-09-16**: `FUN_0048AFA0`,
   guarded by `level != 1`, writing `byte[creature+0x403] - 1` and carrying the
   health *fraction* across the change. Two callers: the resurrection routine
   (`creature-death.md` §2.3) and `FUN_0049C2A0`, which is unread.

## Confidence

**CONFIRMED (binary):** the health routine and its argument shape; the three
creature offsets; the melee block's runtime offsets; the stat matrix's runtime
layout and the `× percent / 100` rule with level 1 exempt; that `FUN_00514830`
is healing and not combat.

**STRONGLY INFERRED:** that `FUN_00494D00` is *the* melee-damage accessor — it
is `def+0x5C4`'s only reader outside the copier, which is an absence.

**CONFIRMED (binary, 2026-09-16):** that `FUN_00498E34` is the attack routine —
it is the sole caller of the melee-damage accessor; the recharge gate's shape;
the bearing bonus's *arithmetic*; both player multipliers; the Monk's set-damage-
clear around `FUN_004B2CE0`.

**UNKNOWN:** the bearing bonus's purpose (Q325), and the rest of §7.
