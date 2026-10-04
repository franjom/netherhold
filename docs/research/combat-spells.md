# Mechanic: the spell arms — how a creature casts in a fight, and what a cast is

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-23. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046). Field names are DK2's own where its dumper or its file
  layout gives one (`creature-object.md`, `kwd-record-fields.md`).
- The reference install's `CreatureSpells.kwd` (41 records) and `Creatures.kwd`
  (46 records), read with `DK2.Data`'s layouts — a **roster**, never copied here.

Original version: DK2 v1.70 (GOG)

Questions: answers Q365 (the flag bits, from the roster); opens Q392–Q394;
corrects `combat-acquisition.md` §6, which read the survey's choice as "the
strongest" — the routine it compares by is the **recharge**. `combat-fight-state.md`
§4 owns the melee arm and the two clocks this mirrors; §2 the entry and reach test.

**What this settles:** in reach and off cooldown a caster starts **creature action
16**, stamps the slot with the turn at once, and the action spawns a **shot thing**
`ShotDelay` ticks later, aimed at the target's centre with a lead of one and a half
times its last movement per tile of range; the action ends the tick the shot
leaves. The survey keeps the attack spell whose next-ready time is latest, so a
fresh Warlock throws Firebomb before Fireball. A friendly spell is cast once and
the target dropped. A spell the creature cannot cast makes it look for another
target, then step back or run. **Built for the attack spells** (ADR-0167, §11); the
friendly arm waits on the buff table (Q394).

---

## 1. The spell definition as the game holds it (`0x004F7860`)

The loader copies from a parsed intermediate record into a 0x46-byte runtime
definition, each field behind a presence flag; a field absent from a file keeps
the constructor's zero (`0x004F7810`). File offsets are `kwd-record-fields.md`'s.

| runtime | file | field | note |
|---|---|---|---|
| `+0x1F` dword | 200 | `ShotData1` | copied to the shot (§7) |
| `+0x23` dword | 204 | `ShotData2` | copied to the shot |
| `+0x27` dword | 212 | `Flags` | **bits as in the file** (§2) |
| `+0x2B` word | 216 | `CombatPoints` | 50 for all but three |
| `+0x2D` dword | 234 | `RechargeTime` | **× variable 19 (ticks a second) >> 12: ticks** |
| `+0x31` dword | 208 | `Range` | position units, 4096 a tile |
| `+0x35`–`+0x3D` words | 220–228 | the five string ids | |
| `+0x3F` dword | 218 | `SoundEvent` | word widened |
| `+0x43` byte | 230 | `CreatureSpellId` | |
| `+0x44` byte | 231 | `ShotTypeId` | the shot fired (§7) |
| `+0x45` byte | 238 | `AlternativeShot` | the buff table's index (§2) |

So `RechargeTime` is fixed-point seconds in the file and **ticks** in the game;
`CombatRules.TicksOf` is the same multiply. Shipped: 2.5 s (the two Heals) to 25 s
(Maiden Web); Fireball 5 s, Firebomb 7.5 s, Lightning 13 s, Freeze 15 s.

## 2. The flag bits, named from the roster (Q365)

The survey (§5) and the fight-starter read bits 0–3 and 7 of `+0x27`. The 41
shipped records sort them:

| bit | who carries it | reading |
|---|---|---|
| 0 (`0x1`) | every shot fired at an enemy: Fireball, Firebomb, Freeze, Lightning, Gas, Guided Bolt, Slow, Grenade, Drain, Hail Storm, the Arrows, Knives, Disruption, Wind, Skeleton Army, Spit, Web… | **attack spell** — the survey's candidate; nibble 0 (creature) or 2 (thing) |
| 1 (`0x2`) | Raise Dead, Imp Teleport, Imp Haste | **thing-targeting** — scan B over the neutral player's things (dead bodies for Raise Dead); the Imp's two are job magic, not fight spells |
| 2 (`0x4`) | Haste, the two Heals, Cast Armour, Invulnerable, the two Invisibles | **friendly** — scan F, nibble 3 (§8) |
| 3 (`0x8`) | Mistress and Fairy Freeze, Gas Cloud, Gas Missile, Slow, Wind | **status, no damage**: refused against a thing target (a door needs damage) — the fight-starter takes melee instead, and the survey drops the slot when `AttackAbnormalTargets` names a living thing |
| 5, 6 | Imp Teleport / Imp Haste alone | UNKNOWN (Q394) |
| 7 (`0x80`) | the two Invisibles alone | kept by the survey as the friendly spell's "kind"; its use is UNKNOWN (Q394) |
| 8 (`0x100`) | every record but the two Invisibles | UNKNOWN (Q394) |

`AlternativeShot` is 1 Haste, 2 Heal, 3 Armour, 4 Invulnerable, 5 Invisible and 0
otherwise: an index into a six-record, 17-byte table at `0x006E4AC8` (a routine
and three dwords) that `0x0049A7B0(target, 0)` calls for the friendly scan's
"castable on this ally" test — the buff's own applicability (Q394).

Ranges: one tile (Gas Cloud, Wind, Skeleton Army, the Invisibles), two (Knives),
three (most), four (Fireball, Firebomb, Gas Missile, Grenade, Hail Storm, Web),
five (the Guided Bolts, the Arrows, Wizard and Reaper Fireball).

## 3. The creature's three slots

File `0xDC4 + 24i`: a vector (12 bytes), `PlayAnimation` at `+13`, `ShotDelay`
fixed-point seconds at `+16`, the spell id at `+22`, the level at `+23`
(`KwdCreatures`, `CreatureSpellSlot`). The loader (`0x004935D3`–`0x0049367F`)
packs only the **nonzero** slots, counting them at `def+0x789`, into 19-byte
records at `def+0x5E1 + 19i`: `[0]` id, `[1]` level, `[2]` dword **the delay in
ticks — fixed seconds × variable 19 >> 12, the multiply `MeleeHitDelay` gets**,
`[6]`/`[0xA]`/`[0xE]` the vector, `[0x12]` `PlayAnimation`. `0x004943F0(def, i)`
returns the record, or a static all-zero one past the count. The creature holds
a last-cast turn per slot at `+0x41B + 4i`.

Shipped wind-ups, in ticks: Warlock, Wizard and Maiden **11**; Dark Elf 5;
Salamander, Fairy and the Dark Angel's first slot 4; Monk 3; Bile Demon,
Mistress, Vampire, Elven Archer and the Horned Reaper 2; Imp, Firefly, Thief,
Rogue and the Dark Angel's other two slots 0. The vector is the shot's origin in
the creature's frame — the Warlock's `(0, −1761, 1433)` is 0.43 tile ahead and
0.35 up — and is the same for all of one creature's slots.

## 4. Can it cast? (`0x0048C800(creature, def, slot)`)

No spell id → no. Never cast (stamp 0) → yes. Otherwise the recharge `R` from
`0x004F7AB0` (ticks) is scaled `(R << 12) / (haste + slow)`, the two words at
`+0x3AF`/`+0x3B3` summed — **the work rate and its enhancement** (Q6,
`EfficiencyRules.Modifier`: `0xCCC` = 0.8 fresh, so a fresh creature's cooldown runs a
quarter long; the divisor Gate B uses too, `combat-fight-state.md` §4) — and the
answer is **`stamp + scaled < now`**, strict.

`0x004F7AB0(spellDef, creature)` supplies the possession-adjusted interval;
its table and truncations are confirmed and built (Q393), with evidence in
`keeper-spell-possession.md` §Modifiers. The sibling `0x0048C8B0` gives the same recharge as a percentage
elapsed, 100 when ready, for the possession display.

## 5. Which spell the survey picks (corrects `combat-acquisition.md` §6)

Over the castable slots (§4, level allowing) with bit 0: the first is taken; each
later one **replaces the best when its `stamp + R` is greater** (`0x0045CE28`,
signed). Fresh, every stamp is 0 and the longest recharge wins — Firebomb over
Fireball, Freeze over Lightning; once cast, a spell's stamp keeps it ahead for as
long as it is castable at all. The earlier reading called this "the strongest by
`0x004F7AB0`"; that routine is the recharge, and the order is readiness. The
chosen spell's `Range` becomes `(range >> 12) × range`, the range² the
fight-starter stores. A friendly slot (bit 2) is the **last** castable one — each
overwrites the saved slot (`spell-friendly-arm.md` §8.1; an earlier reading said
first) — with its range² and bit 7; a bit-1 slot is the thing-targeting spell.

**A Support waives the recharge.** The slot test is `(FightStyle == 4 && !0x00460C50())
|| 0x0048C800()`: a Support creature with an escape tile surveys its attack spell
whether or not it has recharged, so between casts it keeps nibble 0, the spell's
range², and §9–§10's step-back instead of closing to melee; only a cornered Support,
and every other style, needs the spell ready. This is why a Warlock stands off while
its Fireball cools. The waiver sits before the slot's flags are read, so it covers a
thing-targeting (bit 1) slot too; the friendly arm re-tests `0x0048C800` at `0x0045CCEB`
and is not waived. Built for the attack survey as `SpellRules.SurveyAttack(whileRecharging)`
(ADR-0325); the thing-targeting survey is not built.

## 6. The spell arm of `spFighting` (nibbles 0 and 2), `0x0045FB65`

Reached in reach — `d < range²` with line of sight (`combat-fight-state.md` §2
step 9) — with the slot in the high nibble of `AttackType`:

1. §4 refuses → §9.
2. **Start action 16** (`0x004969D0(creature, 0x10, 1, slot, target, 0)`);
   failure asserts *"Creature type %d failed to set spell instance for spell %d at
   target %d."* and returns 1.
3. `AttackCreatureHits` (`+0x4D9`) is incremented; **the slot's stamp is set to
   the turn now, at the start of the cast**; a creature target is told with the
   melee flag 0 (`0x0045AA20`, §5 of the fight doc). Return 1.

The fight's busy test (step 7 of the fight doc) holds the state while the action
runs, so a caster in reach casts at `stamp + scaled recharge < now`, waits
`ShotDelay` ticks, fires, and is free the same tick: a Warlock's Fireball every
20 ticks with an 11-tick wind-up, its Firebomb every 30.

## 7. Action 16, "the spell instance" (`0x00498870`; also record 31's handler)

The handler runs every tick the action is current, with the slot in the action's
first parameter (`+0x399`) and the target's tag in its second (`+0x39D`):

1. For every slot carrying this spell id, body bit 12 of `+0x3EA`
   (`PlayInstanceAnimation`) := the slot's `PlayAnimation` (1 in every shipped slot).
2. **Wind-up.** Unpossessed: `now ≤ start (+0x3A5) + delay` → return 1. A
   `Possessed` creature does not wait.
3. The definition by id (`world vtable+0xE4` present, `+0xE8` fetch); no
   `ShotTypeId` → assert *"Creature Spell %d has no Shot Type associated"* and
   return 0 — the action ends with nothing fired.
4. **Origin** = my position plus the slot's vector rotated by my heading `+0xF4`
   (`Possessed`: the vector's height only). **Owner** = my owner, or the Good
   player when `CombatPitFighter` (`+0x3E6` bit 15) — a pit fighter's shots are
   the heroes'.
5. The target must be alive and not a class-9 thing (a room) → else return 0.
6. `0x004B7CD0(world+8, shotType, owner, &origin, &heading, &shot, 1)` makes the
   shot thing; failure → 0. The `SoundEvent` plays on my `+0x2E` handle. The
   shot's `+0x13D` = `ShotData1`, `+0x141` = `ShotData2`, `+0x145` = my tag,
   `+0x147` = the target's, `+0x16D` = my tag again.
7. **Aim** = the target's position, height raised by half its `vtable+0x40`, plus
   a lead of `(position − previous position) × tiles × 3 / 2`, where `tiles` is
   `isqrt(16·d²) >> 8` — the whole tiles between us — and `vtable+0x2C` the
   previous frame's position (STRONGLY INFERRED, `combat-acquisition.md`). Then
   `0x004AB830(shot, &aim)` and `0x004AB8A0(shot, &aim)`, which turns the shot to
   face the aim (`0x0065FB54`, atan2) and launches it. **Return 0**: the action
   ends the tick the shot leaves.

Everything after — the flight, the hit, `ShotData1` as the flat contact damage,
`ShotData2` as an area radius or a duration — is the shot thing's own update:
[`shots.md`](shots.md).

## 8. The ally-spell arm (nibble 3), `0x00460011`

§4 refuses → return 1, standing. Start action 16 with the slot and the ally;
failure asserts as §6. Then `AttackCreatureHits++`, the stamp, and **the target
and the low nibble are cleared** (`AttackType &= 0xF0`, the slot kept); the ally
is not told. Return 1. A healer therefore casts once per acquisition and looks
again.

## 9. When the spell cannot be cast (`0x0045FC29`)

**Without `AttackAbnormalTargets`** (`+0x3E6` bit 2): the target, nibble and
range² are remembered; I leave the target's attacker slots (`0x0045EF50`'s body,
inline) and clear `Attacking`; `SelectAVictim`. A **different** target → return 1
(the fight continues with it). The **same** target → a spell nibble (0 or 2) →
§10, else return 1. **None** → the old target, nibble and range² are restored;
a creature target with a nibble other than 3 is re-entered through the
fight-starter `0x0045A550` (§8 of the acquisition doc); then §10.

**With it**: a `Support` (`FightStyle` 4) goes to §10 at once; anyone else leaves
the slots, clears `Attacking`, re-arms `AttackingId` to the old target
(`0x0048F390`), runs `SelectAVictim` and returns its result.

## 10. Step back or run (`0x0045FEA0`)

`d²` to the target. Busy (`0x00496D00`) → return 1. Within a **quarter** of both
range² and sight² → an impulse against my heading of `def+0x5BC × 16` (the
shuffle speed; the flee state's back-off uses × 24, `combat-fear.md` §3), checked
by `0x00460DA0` (a route to the spot and the terrain flag) and applied by
`0x004B1F60` at the shuffle speed mode; else the run speed mode. Return 1.

## 11. Built and not built

**Built (ADR-0167, `HashVersion` 72):** the survey's attack choice (§5) in
`SpellRules.SurveyAttack`, run by `CombatTargetRules.Choose` for every non-worker; the
fight-starter's nibble in `Engage` and `EngageHeart`; the cooldown gate §4 with
the work-rate divisor; the spell arm §6 as `FightRules.StartCast`, stamping the
slot at the start; action 16 §7 as `CastFires`/`FireSpell`, firing when the turn is
past start + delay, with the lead on the target's last move, from the caster's own
position — the slot's origin vector is not carried (Q398); the cannot-cast
fallback §9 and the step-back §10 in `CombatSystem.CannotCast`, except that
nothing found leaves the creature idle where DK2 restores the old target. The shot
itself is `shots.md` §10.

**Built since (ADR-0170):** the ally arm §8 and the friendly survey
(`spell-friendly-arm.md`). **Not built:** the thing-targeting
survey; the approach's part-way point for a
spell nibble (`combat-fight-state.md` §2 step 10).
Possession modifiers and its wind-up bypass are built under ADR-0350;
see `keeper-spell-possession.md`.

## Confidence

**CONFIRMED (binary):** §1's mapping and the recharge multiply; §3's loop, layout
and delay multiply; §4's gate, formula and strictness; §5's comparison and its
direction; §6, §8, §9 and §10 in order; §7's every step and both asserts.

**CONFIRMED (data):** the roster behind each bit in §2 and the values in §1–§3.

**STRONGLY INFERRED:** the names of bits 0–3 (from the roster and the two places
each is tested); `vtable+0x2C` as the previous position; class 9 as a room.

**UNKNOWN:** Q392 the shot thing; Q394 bits5–8, bit7's use and the buff
table at `0x006E4AC8`.
