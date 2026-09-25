# Mechanic: creature status effects — what a spell leaves on the creature it hits

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-23 (the field sweeps
  were begun by Codex in its worktree; every fact here was read again). Addresses
  are **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046). Field names are DK2's own where its dumper prints them
  (`creature-object.md`); the rest are named by what they do.

Original version: DK2 v1.70 (GOG)

Questions: answers Q396; corrects Q393 — the words the cooldowns divide by are the
**work rate and its enhancement** (Q6, `EfficiencyRules`), not haste; reads the
Reaper Fireball's handler for Q395; corrects `shots.md` §4a, whose body bit 21 is
**dying**, not invisibility; opens Q399 (what the build approximates). `shots.md` §8
is the shot side of every effect here.

**What this settles:** every status a shot leaves is a **tick countdown on the
creature**, run once a tick by one routine, plus a rule somewhere else that reads
the word while it is nonzero. Freeze is a state that stands still; Lightning and
the Reaper Fireball are a stun state of `StunDuration`; Haste and Slow change the
speed the body is set to and put it back; Armour halves damage; Invulnerable
refuses it; Invisible makes the creature untargetable and drops its attackers;
Poison bleeds a fixed amount a tick; **fire does nothing but burn on screen**. The
health routine every blow and shot ends in scales damage by 128ths: stunned
double, frozen five quarters, armoured or possessed half.

---

## 1. The timed-effects update (`0x0048E160`)

Called once a tick from the creature's own update (`0x00488930`), and **skipped
entirely for an `IsHorny` definition** (flag bit 14). Each word below is a tick
count that steps down while nonzero; the constructor (`0x004872A0`) zeroes them all.

| word | set by | each tick | at zero |
|---|---|---|---|
| `+0x443` armour | Cast Armour (§2) | effect 0x75 every 16 ticks | — |
| `+0x445` invulnerable | Invulnerable (§2) | effect 0x95 every 32 ticks | — |
| `+0x447` frozen | Freeze (§2) | effect 0x72 every 16 ticks; busy and tinted | a `Possessed` creature gets message 0x16 |
| `+0x449`, `+0x44D`, `+0x451` | unread | count down | — |
| `+0x44B` `ChickenTimer` | the chicken spell | count down | `SetState(1)` |
| `+0x44F` invisible | Invisible (§2) | rendered at alpha 100; busy | — |
| `+0x453` `TurncoatTimer` | Turncoat | count down | — |
| `+0x459` speed effect | Haste, Slow (§2) | count down | with `+0x45B` also 0, speed := `def+0x5B4` (walk) |
| `+0x45B` second speed timer | unread | effect 0x75 every tick | speed := walk when `+0x459` is 0 too |
| `+0x45D`/`+0x45F`/`+0x461` poison ticks, amount, owner | Poison Spit (§2) | **while not frozen**: health −`+0x45F` through the health routine with owner `+0x461` | — |
| `+0x463`/`+0x465`/`+0x467` fear over time | a spell effect (`combat-fear.md` §2) | `TempFear` += `+0x465` from `+0x467` | — |
| `+0x469`/`+0x46B` burning ticks, owner | fire (§2) | sound 0x340; three effects 0x58 at random within `def+0x63A` of me | `+0x46B` := 0, the sound stopped |
| `+0x4D7` | unread | count down | — |

Two more countdowns live in the animation chooser (`0x0048BE50`): `+0x4DD` (set by
the speed handler to `ShotData2`) and `+0x4E3` (20 after Lightning) only choose
the animation and its flags. The dumper's names for `+0x443`–`+0x4E3` were not
found; `ChickenTimer` and `TurncoatTimer` are `creature-object.md`'s.

## 2. The effects, one by one

**Haste and Slow** (`0x0048CAD0(creature, percent, seconds)`, process type 4).
Refused while a speed effect runs (`+0x459 ≠ 0`). Otherwise the body's speed
(`vtable+0x90`) × `percent` / 100, clamped between a quarter of `def+0x5B4` and
`ticks-a-second << 12`, is set through `vtable+0x8C`; `+0x459` = `seconds` ×
ticks-a-second; `+0x3BF` = 1. Haste Creature and Imp Haste: 150 % for 15 s (60
ticks); Slow: 20 % for 15 s. When the ticks run out the speed is set back to the
definition's walk. The animation flag `+0x4DD` and body bits 16/23 are display.

**Freeze** (`0x0048CB60(creature, ticks)`, type 11). Refused for `IsHorny` and
while already frozen. `+0x447` = `ShotData1` × ticks-a-second (Mistress Freeze 10 s
→ 40 ticks, Fairy Freeze 15 s → 60); the current action is ended (`0x00496D00` /
`0x00496D20`); `SetState(218)` "frozen", whose handler (`0x00481950`) **returns 1
while `+0x447` is nonzero and then `SetState(1)`**: the creature stands. While
frozen it takes five quarters damage (§3), its poison pauses, an effect 0x104
follows it (`0x00487C70`), and the fight's target gate `0x0045A6D0` reads the word
(STRONGLY INFERRED: a frozen creature is not a fighter while it stands).

**Cast Armour** (type 5). `+0x443` = `ShotData1` (80 ticks, 20 s) — **a duration,
not an amount**. While set, every negative health change through the health
routine is halved (§3). The melee blow's Stone Knight path (`0x00490120`, §3)
halves too.

**Invulnerable** (type 10). `+0x445` = `ShotData1` × ticks-a-second (5 s → 20
ticks). The melee blow (`combat-fight-state.md` §4) and a shot's contact damage
(`shots.md` §7) both refuse a target with the word set; the health routine itself
does not test it.

**Invisible** (type 20, Thief and Rogue). `+0x44F` = `ShotData1` × ticks-a-second
(15 s → 60 ticks); then `0x0045EE60(creature)`: every creature in my melee and
ranged attacker slots whose `Attacking` is me lets me go (`Attacking` := 0; its
`AttackType` low nibble set to 15 only when my health is already gone;
`AttackingId` cleared for an abnormal attacker), my slots are emptied, and
`0x0045A8B0(me)` recomputes body bit 6 of `+0x3EA`, **the targetable bit**: set
only when I am not dying, not a `CombatPitFighter`, not in state 76, still have
health, `0x00473990` and `0x00485210` allow, body `+0x3E6` bit 14 is clear,
**`+0x44F` is zero**, `+0x46F` bit 3 is set, my state's table flag bit 2 is
clear, and fewer than eight attackers sit in each slot array. So an invisible
creature is dropped by its attackers and acquired by nobody until the ticks end.
Rendered at alpha 100. (The shot predicates' bit 21 is dying, §3 — shots do hit
an invisible creature that stands in their way.)

**Lightning** (type 9). Damage `ShotData1` (300), **doubled on a water tile**,
through the health routine (`shots.md` §8); `+0x4E3` = 20 (the electrified
animation); then, unless held (`+0x3CB`), in state 23, `IsHorny` or `Possessed`,
`0x00491830(creature, 255)`: `SetState(255)` "stunned", action 52, and
`0x0049AA30(15)`. **The Reaper Fireball** (type 29, `0x004AF610`) stuns the same
way, refused only for a dying or `IsHorny` target.

**The stun needs its state.** `0x00491830` tests `SetState(255)` and on a refusal
returns 0 before the action and event 15. The rule refuses 255 from 216–218, 219,
246, 71, 207 and 208, so **a frozen creature cannot be stunned**, nor a dying one
(read 2026-09-25). Freeze's `SetState(218)` is untested and never refused.

**Stunned** (state 255, handler `0x004810E0`). Stands for **`StunDuration`** —
`def+0x74C`, file `0xED0` × ticks-a-second by the loader (`0x00493B57`), parsed as
`CreatureDef.StunDuration` — with effect 101 stuck on it from the first tick;
when the time is up, action 53 (getting up) and `SetState(257)`, whose handler
(`0x00481200`) returns 1 while an action is current (STRONGLY INFERRED: then
idle); `+0x3CB` := 0. States 209, 255, 256 and 257 are the stunned family
(`0x0048F350`): while in one, **damage doubles** (§3).

**Poison Spit** (type 38, `0x004B0270` → `0x0048CC50(creature, ShotData2,
ShotData1)`). `+0x45D` = `ShotData2` (10 ticks), `+0x45F` = `ShotData1` (200),
`+0x461` = the shot's owner: 200 a tick for ten ticks while not frozen, credited
to the Maiden's keeper. Refused for a dying target. The sibling `0x0048CC70` sets
the fear-over-time pair the same way.

**Drain** (type 35): `shots.md` §8 — `min(target health, ShotData1 % of its
level-scaled Hp)` moved from target to caster.

**Fire** (`0x0048EBD0(creature, 10, owner)`, from a fire-category contact hit on a
creature under a fifth of its health, from area damage type 3 and process type
30). `+0x469` = 10 ticks, `+0x46B` = the owner. **No routine that reads `+0x469`
or `+0x46B` changes health**: the readers are the update above (sound and three
fire effects a tick), the animation chooser, the effect follower (`0x00487C70`)
and the serialisers. Burning is a two-and-a-half-second display in v1.70
(CONFIRMED, negative: every reader of both words was walked).

## 3. The health routine's multipliers (`0x004B2CE0(thing, delta, source, flag)`)

The routine opens by stamping the source into `thing+0x36` for every thing, then takes
its **object arm** (class 2): an Arch Pillar hands the delta to its owner's heart routine,
any other object needs `Damageable`, and the delta lands flat (`win-and-defeat.md` §3.1).
What follows is the creature arm.

For a creature and a negative delta, after the refusals — dying (body bit 21)
unless `flag`; `OnlyAttackableByHorny` (def bit 22); **`IsHorny` (def bit 14) takes
no negative delta at all**; body `+0x3E6` bit 0 (`InHand`) leaves the health
untouched; a `Possessed` target first gets `0x0048F2E0` — the delta is scaled by
a multiplier in 128ths, floored at 0:

| condition | multiplier |
|---|---|
| base | 128 |
| frozen (`+0x447`) | 160 |
| armoured (`+0x443`) **or `Possessed`** | −64 |
| the target's owner's byte `+0x1095` | −8 each |
| stunned (states 209/255/256/257) | +128; with a non-Imp among the attackers (`0x0045EC10`) `0x00481360` runs (unread: likely the early end of the stun) |
| `CombatPitFighter` | −128 + `PitPercentageDamageTakenOfNormalCombat` (variable 239, 20) |

`health += delta × m / 128` (rounded toward zero). Then, for a creature with
`GenerateDeadBody` whose health is now ≤ 0, not `Possessed` and not already dying:
lair and hatchery ties are cut, and unless a room it is in refuses, **health :=
`CreatureDyingStateDurationSeconds` × ticks-a-second** (60 × 4 = 240) and
`0x0048B770` sets body bit 21 — the dying countdown `creature-death.md` follows.
The Stone Knight's blow path (`0x00490120`, taken by the melee handler only for a
type-28 target): the delta twice when stunned, half of it given back when
armoured or possessed, and at ≤ 0 health := a sixth of level-scaled `Hp` and the
same `0x0048B770`.

## 4. Built and not built

**Built (ADR-0168, `HashVersion` 73).** `StatusEffectRules`, `CreatureStatusSystem` and
`ShotRules.Process`: the words of §1 that have a setter here — armour, invulnerable,
frozen, invisible, the speed effect (stored as ticks and percent), poison's three, and
the stun's clock — as hashed creature fields stepped once a tick before the fight, skipped
whole for `IsHorny`; §3's multipliers and refusals in `Hurt`, which every melee blow,
contact and area hit now passes, the invulnerable word refused by all three; §2's
setters from the shot's process events 0x40 and 0x4, on the thing struck (a hit
writes it into the target word, `0x004ABE7D`); `Frozen` and `Stunned` as standing
activities the fight gate and the job systems refuse (`CombatRules.Preoccupied`);
`IsAttackable` refusing an invisible creature and `DropAttackers` letting its
attackers' targets go; Haste and Slow as a percent on the movement cap with the
quarter-walk floor; Lightning on the first tick, doubled on water, immune for
`CreatureFlags2` bit 0; Drain through the health routine both ways. Gate B's division
by the work rate was corrected first (`combat-fight-state.md` §4). On state ids
(ADR-0208): freeze enters 218 and thaws to 1; the stun enters 255, is not taken when
the rule refuses it, and gets up through 257 to 1.

**Not built, named (Q399):** the per-player byte `+0x1095`, `CombatPitFighter`, the Stone
Knight's blow path, the stun's early end `0x00481360`, state 257's length, the ignite
and every other display, the fear-over-time pair, the unread words. Ours lets the
engagement go on freeze and stun where DK2 only changes the state, and scales the
cap every tick where DK2 pins the body speed once.

## Confidence

**CONFIRMED (binary):** §1's countdowns and their side effects; §2's setters,
values and refusals; the frozen handler; the stun handler and `StunDuration`'s
loader multiply; the Invisible drop and the targetable bit's conditions; §3's
multipliers and refusals; the Stone Knight path.

**CONFIRMED (negative):** fire changes no health in v1.70.

**CONFIRMED (owner, 2026-09-23):** the Reaper Fireball stuns — the target is thrown to the
ground with the stunned effect.

**STRONGLY INFERRED:** state 257 ends idle when action 53 ends; the fight gate's
use of `+0x447`; `0x00481360` as the stun's early end.

**UNKNOWN:** `+0x449`, `+0x44D`, `+0x451`, `+0x4D7`, `+0x45B` (their setters);
the per-player byte `+0x1095`; Q393's index-31 accessor.
