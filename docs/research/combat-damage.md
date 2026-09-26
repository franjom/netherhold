# Mechanic: the melee damage formula, end to end

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- The reference install's `GlobalVariables.kwd`, via `DK2.Data`

Original version: DK2 v1.70 (GOG)

Questions: **Q8** (the combat formula). Opens Q337. Completes `combat.md` §5–§6,
which found the pieces; this assembles them.

**Built as a pure rule** (`MeleeDamageRules`), **called by the swing since ADR-0164**
(`FightRules.LandBlow`, combat-fight-state.md §4) with no stance, the bearing from target to
attacker and no player modifier —
the simulation has no attack state, so nothing swings. See §6.

---

## 1. The three fixed-point helpers, identified

`combat.md` §5.1 read `0x0043D750` and `0x00441990` as "the shape of a clamp".
**They are not clamps.** Each is a four-line fixed-point operator over 12-bit
values, taking `this` and an argument by pointer and writing through an
out-pointer it returns:

| routine | what it does |
|---|---|
| `0x0043D750` | `*out = *this + *arg` |
| `0x00441990` | `*out = (*this << 12) / *arg` — a 12.12 **divide** |
| `0x004363F0` | `*out = (*this * *arg) >> 12` — a 12.12 **multiply** |

That single correction turns §5.1's unreadable multiplier into an arithmetic
statement, and it is what unblocked the rest of this page.

## 2. The multiplier, and all three flags are already named in our own notes

`FUN_00494D00`'s three arms are **mutually exclusive** — `if / else if / else if`,
not three factors folded together, which is how `combat.md` §5.1 read them. Each
arm builds `(ceiling + value) / ceiling` and the default is 1.0:

| arm | attacker flag | ceiling | value | multiplier |
|---|---|---|---|---|
| A | `+0x3E6` bit 7 — **`Possessed`** | 100 | `global+0x31B` | `(100 + v) / 100` |
| B | `+0x3EA` bit 3 — **`InfluencedByPossessed`** | 200 | `global+0x31B` | `(200 + v) / 200` |
| C | `+0x3E6` bit 15 — **`CombatPitFighter`** | 100 | `global+0x482` | `(100 + w) / 100` |
| — | none | — | — | `1.0` |

The bit names are **not new work**: all three were already in this project's
`creature-object.md` bit tables. §5.1 called them UNKNOWN because it never looked
there, which is the cheapest lesson on this page.

`global+0x482` is **variable 225 `CombatPitMeleeDamageModifierPercentage`**, and
it ships **100** — so a Combat Pit fighter deals **double** damage, not "100 % of
normal". The name reads like a scale and the arithmetic is an increment.

**Arms A and B share one value and halve it by changing the ceiling.** With one
`v` the pair yields `+v %` and `+v/2 %`. The shipped table has exactly such a
pair: variable 98 `FirstPersonStatIncreasePercentage` = **12** and variable 113
`PossessionFriendlyStatIncreasePercentage` = **6**.

**But neither of those globals is ever read.** `0x006F28E0` and `0x006F28F2` each
appear once in the whole image — in their own setter.

**`global+0x31B` (`0x006F286B`) is filled by variable 224** (Q337, answered 2026-09-26,
ADR-0237; `verification-2026-09-26.md` §1). The handler `0x0051DC70` stores each
`CreatureFirstPersonStats` record's value as a word at `0x006F2863 + 2 × statId`, and stat 4 is
`MeleeDamage`. The VARIABLES dispatcher `0x00521790` reaches it; the chunk table `0x0051E0A0`
registers that dispatcher (`0x0051E1D7`). The shipped value is **60**: a possessed creature hits
for **×1.6**, and one influenced by possession for **×1.3**. An address scan and a `+0x31B`
displacement search both miss it, because the store is base plus index.

## 3. The base, which is ours already

```
level == 1  ->  base = def+0x5C4 (MeleeDamage)
otherwise   ->  base = MeleeDamage * statRow[MeleeDamage][level] / 100
```

`statRow` is the 32 × 9 table of `combat.md` §4 at `global+0x117`, signed
`int16`, signed divide by 100. `CreatureLevelRules.Scale` is already this, so
level 10 is **600 %** — a level-10 King Reginald swings for 5580 before anything
else applies.

```
damage = (multiplier * base) >> 12          ; 12.12, truncating
```

## 4. What the swing then does to it, in order

`FUN_00498E34` from `0x00498EC1`. **The damage is negated immediately**, at
`0x00498ED3`, so every step below operates on a negative number and every
`/ 100` truncates **toward zero** — each bonus is therefore slightly *smaller*
in magnitude than the same arithmetic on a positive value. That is a real
rounding quirk, not a presentation detail.

1. `damage = -FUN_00494D00(def)(attacker)`
2. the target's vtable slot `+0x34` must return non-zero, or nothing happens
3. **the bearing bonus** (`0x004990C4`), on target kind 0:
   `a = atan2(attacker.x - target.x, attacker.y - target.y) & 0x7FF`; if
   `a > 0x3FF` then `a = 0x7FE - a`; `a &= 0x3FF`; `b = a / 20`, giving 0–51;
   and **only if `b > 15`**, `damage += damage * b / 100`. Purpose UNKNOWN
   (Q325) — nothing in it reads either thing's facing.
4. kinds 2, 3 and 4 call `FUN_004998A0`, which **does not touch the damage**
   (`combat.md` §6.1 — hit presentation)
5. **`if (damage == 0) the health routine is never called at all`** — a swing
   rounded to nothing is not a swing
6. two per-player modifiers off the attacker's owner (`attacker+0x2A`), each
   skipped when its signed byte is `<= 0`:
   - `byte[player+0x1090]`: `damage = damage * (v + 10) * 10 / 100`
   - `byte[player+0x1091]`: `damage = damage * (v * 15 + 100) / 100`
7. the **Monk** (`attacker+0x36F == 0x13`) against a creature sets
   `target+0x3EA` bit 2 — **`CantBeResurrected`**, named in `creature-object.md`
   — around the call and clears it after (`combat.md` §6.2)
8. `FUN_004B2CE0(target, damage, word[attacker+0x28], 0)`
9. a Horned Reaper (`def+0x686 & 0x4000`) hitting a **Stone Knight**
   (`target+0x36F == 0x1C`) additionally calls `FUN_00490120` with the same
   three arguments (Q328)

There is **no resistance step**. `combat.md` §6.1.1 ruled the shipped
`Resistances` table out of the live path, and reading the swing end to end finds
no other one: nothing between the accessor and `FUN_004B2CE0` consults the
target's own defensive numbers at all.

## 5. Worked example

A level-1 Goblin (`MeleeDamage` 170), unpossessed, not in a Combat Pit, owner
with both modifiers zero, attacking off-axis so `b <= 15`:

    multiplier = 4096 (1.0)
    base       = 170
    damage     = (4096 * 170) >> 12 = 170, applied as -170

The same Goblin at level 8 (`MeleeDamage` row = 300 %): `170 * 300 / 100 = 510`.
In a Combat Pit: `(100 + 100) / 100 = 2.0`, so `1020`.

## 6. What is built, and what is deliberately not

`MeleeDamageRules` implements §2–§4 as pure functions and is covered by tests
against the shipped table. **`FightRules` calls it** for every blow (ADR-0163 onward).
Until combat existed nothing did, because a formula wired into a simulation that
could not swing would have reported progress it had not made. (Updated 2026-09-26.)

`atan2` (`FUN_0065FB54`) is **not** reimplemented; step 3 takes the raw angle as
an argument, because a bearing this project cannot yet produce is not a bearing
worth approximating.

## Confidence

**CONFIRMED (binary):** the three helpers; the three mutually exclusive arms and
their ceilings; the level scaling; the negation and its position; the bearing
arithmetic and its `> 15` guard; the zero-damage early out; both player
modifiers; the Monk bracket; the argument order into `FUN_004B2CE0`.

**CONFIRMED (repo cross-reference):** `Possessed`, `InfluencedByPossessed`,
`CombatPitFighter` and `CantBeResurrected`, from `creature-object.md`.

**UNKNOWN:** the bearing bonus's purpose (Q325);
what `FUN_00490120` does (Q328).
