# Mechanic: the friendly spell arm and its applicability table

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-23.
  Addresses are **facts about the binary in our own words**; no decompiler
  output enters this repository (ADR-0046). Field names are DK2's own where
  its dumper prints them (`creature-object.md`); the rest are named by what
  they do.
- The reference install's `CreatureSpells.kwd`, read through `DK2.Data` only
  to associate the five `AlternativeShot` values with the shipped spells.

Original version: DK2 v1.70 (GOG)

Questions: answers Q394. `combat-spells.md` owns the spell survey, cast action
and attack arm; `creature-status-effects.md` owns what each successful buff
does to its target.

**What this settles:** the friendly survey keeps the last eligible friendly
slot, then either targets the caster alone for Invisible or searches the
caster's own creature list for the nearest fighting creature below a quarter
health. Haste requires no current speed effect; Heal requires less than half
health; Armour, Invulnerable and Invisible require their own status word to be
zero, with the latter three also requiring the Combat state category, 22. The scan may choose
the caster and never searches allied players. Spell flag bits 5 and 6 drive
the Imp's Teleport and Haste special paths; bit 7 makes Invisible self-only;
no reader of bit 8 was found.

---

## 1. Table filler (`0x0049A6C0`)

The filler writes six packed 17-byte records at `0x006E4AC8`. Each is four
dwords followed by its one-byte index. The first dword is an applicability
routine. During dispatch, the second is added to the target pointer before the
call. The third and fourth are copied to local storage but never consumed by
this call path; all live records set all three metadata dwords to zero. Record
0's null representation alone has `-1` in the last dword.

| `AlternativeShot` | spell(s) | record | routine | dwords `+4`, `+8`, `+C` |
|---:|---|---:|---:|---|
| 0 | every non-friendly spell | `0x006E4AC8` | null | `0, 0, -1` |
| 1 | Haste | `0x006E4AD9` | `0x0049A830` | `0, 0, 0` |
| 2 | Heal, Monk Heal | `0x006E4AEA` | `0x0049A840` | `0, 0, 0` |
| 3 | Cast Armour | `0x006E4AFB` | `0x0049A8C0` | `0, 0, 0` |
| 4 | Invulnerable | `0x006E4B0C` | `0x0049A8F0` | `0, 0, 0` |
| 5 | Invisible, Rogue Invisible | `0x006E4B1D` | `0x0049A920` | `0, 0, 0` |

The behavioral meaning of `+8` and `+C` is therefore none on this path. Their
source-level role in the compiler's member-call representation is UNKNOWN and
does not block the spell rule.

## 2. Applicability dispatcher (`0x0049A7B0`)

The friendly scan calls the wrapper at `0x0049A7A0(target, spellId)`, which
calls `0x0049A7B0(target, spellId, 0)`. The dispatcher:

1. resolves the runtime spell definition through the world's spell accessor;
2. reads `AlternativeShot` at definition `+0x45`;
3. refuses an index of 6 or greater;
4. selects `0x006E4AC8 + index * 17` and refuses a null routine;
5. adds the record's `+4` dword to the target pointer, passes the wrapper's
   zero as the routine argument, and returns the routine's result unchanged.

Thus the record routine is the whole spell-specific predicate. Invalid
indices, record 0 and a null routine return false. The five live routines
ignore the zero argument.

## 3. Haste applicability (`0x0049A830`)

True exactly when the target's speed-effect countdown `+0x459` is zero. It
does not itself test health or fight state; scan F supplies both restrictions
for a non-Invisible friendly spell.

## 4. Heal applicability (`0x0049A840`)

True exactly when current health `+0x40` is strictly below half the target's
level-scaled maximum:

```text
maximum = level == 1 ? Hp : Hp * levelPercentage[level] / 100
accept  = health < (maximum >> 1)
```

`Hp` is definition `+0x692`; the experience level is `+0x403`; the signed-word
level percentage is in the globals table at `+0xE1 + level * 2`. Integer
division truncates before the halving. Scan F already requires health below a
quarter of the same maximum, so every candidate reaching this routine passes
its half-health test.

## 5. Cast Armour applicability (`0x0049A8C0`)

True only when the target's state category is 22, Combat, and its armour countdown
`+0x443` is zero. The category comes from `0x00478050`: the state table's category
byte for the current state, and for a walk (category 2) the category of the state it
resumes in — so a creature walking to a fight counts, and 22 is not one state id.

## 6. Invulnerable applicability (`0x0049A8F0`)

True only when the target's state category is 22 (§5) and its invulnerability
countdown `+0x445` is zero.

## 7. Invisible applicability (`0x0049A920`)

True only when the target's state category is 22 (§5) and its invisibility countdown
`+0x44F` is zero. Bit 7 makes the survey offer only the caster as this
routine's target (§8); the possessed-creature action applies the same override
again (§9).

## 8. Friendly scan F in `SelectAVictim` (`0x0045CB00`)

### 8.1 Which friendly spell is surveyed

The ordinary three-slot spell survey is skipped for a worker. It walks slots
0 through 2, requiring the slot's level, a nonzero spell id, and the ordinary
can-cast predicate (`0x0048C800`), which a `Support` with an escape tile (`0x00460C50`
returns 0) is excused on the attack arm only: the friendly arm re-tests `0x0048C800`
at `0x0045CCEB` for every creature (ADR-0325, `combat-spells.md` §5). Every
spell whose definition has `Flags & 4` overwrites the saved friendly slot,
range and kind, so the **last eligible friendly slot wins**. Its squared combat
range is `(Range >> 12) * Range`; its kind is `(Flags & 0xFF) >> 7`, deliberately
discarding bit 8.

### 8.2 Invisible: the self-only arm

Kind 1 means bit 7 was set. The only candidate is the caster itself. It must
already be in the Combat category (22, §5) and pass §2; there is no health gate and no creature-list
walk. On success its own tag is selected. This is the survey-side use of bit 7.

### 8.3 Every other friendly spell

The scan is skipped while the caster has a nonzero `TurncoatTimer` `+0x453`.
Otherwise it walks the creature list belonging directly to the caster's owner,
not allied players. There is no self-exclusion, so the caster can qualify when
encountered in that list. A candidate must, in order:

- have a strictly smaller squared distance than the current best;
- not be a `CombatPitFighter`, captive or tortured;
- pass the spell-specific predicate in §3–§6;
- be in the Combat category, 22 (`0x00478050` at `0x0045DC3F`, §5);
- have health strictly below one quarter of its level-scaled `Hp`;
- be within the caster's sight and visible, or within hearing and reachable by
  `0x0045AFF0`. An Imp's sight limit here is one tile and its hearing limit is
  zero.

The distance is the ordinary fixed-point planar term:

```text
d2 = (abs(dx) * abs(dx) >> 12) + (abs(dy) * abs(dy) >> 12)
```

It chooses the nearest qualifying creature; a tie keeps the earlier list
entry. Spell range does not score or filter candidates here. It becomes the
engagement's range after selection.

### 8.4 Result and failure

A selected friendly target gets one final route request (`0x80` for a
non-Imp, zero for an Imp). Success stores its tag, the spell's range squared,
and attack nibble 3. A failed final route clears the attack target and returns
false immediately.

If no friendly target qualifies, selection falls through to the best target
already found by the earlier hostile scans. If those scans also found nothing,
`SelectAVictim` returns false. The friendly spell is not converted into a cast
on an arbitrary ally or on the caster.

## 9. Spell `Flags` bits 5–8

The survey covered every analyzed instruction in the initialized executable
blocks, including `.text` (`0x00401000`–`0x00652BFF`) and `cseg`
(`0x00653000`–`0x0066BFFF`). `ReadsAt.py` found every source operand at `+0x27`;
the sites that receive a spell definition from the world's `+0xE8` accessor
were then read in full. The spell readers are in `0x0040BBE0`, `0x0045A550`,
`0x0045AA20`, `0x0045CB00`, `0x004651A0`, `0x004F2F50` and `0x00514830`.

**Bit 5 (`0x20`, Imp Teleport):**

- `0x00465229`, in the common Imp job dispatcher `0x004651A0`, keeps the first
  castable bit-5 slot. When the job destination is more than two tiles away on
  either axis and the transition gates pass, the Imp stamps that slot and
  enters the teleport action sequence instead of the ordinary job move.
- `0x00515165`, in game action 26's possessed-creature spell path
  `0x00514830`, selects the Teleport-specific destination/effect/relocation arm
  instead of applying a shot or ordinary creature action.

**Bit 6 (`0x40`, Imp Haste):**

- `0x0046522D`, in `0x004651A0`, stamps the castable slot and applies
  `ShotData1` percent for `ShotData2` seconds directly to the Imp through the
  speed-effect routine; a value above 100 also sets its speed-blur countdown.
- `0x00515254`, in `0x00514830`, selects the same direct speed-effect arm for
  a possessed creature's cast.

**Bit 7 (`0x80`, the two Invisibles):**

- `0x0045CD0C`–`0x0045CD1A`, in `SelectAVictim`, extracts bit 7 as the friendly
  kind; kind 1 takes §8.2's self-only path.
- `0x00515125`, in `0x00514830`, replaces the target returned by the general
  spell-target query with the caster's own tag.

**Bit 8 (`0x100`, every shipped spell except the two Invisibles):** no test was
found in those seven spell-definition readers. The survey explicitly included
register tests after dword loads, not only immediate tests on `[record+0x27]`.
The loader at `0x004F7860` copies the bit, but no analyzed behavior reads it.

## 10. Built and not built

**Built (ADR-0170).** `SpellRules.SurveyFriendly` (the last castable friendly slot, §8.1),
`SpellRules.BuffApplies` (the table of §1—§7 by `AlternativeShot`, the Combat category as
`CombatRules.InCombat`), scan F in `CombatTargetRules` (§8.2—§8.4: the self-only arm, the walk
over the caster's own creatures, the quarter-health gate, sight or hearing, one reach test,
nibble 3 with no attacker slot taken) run after the hostile scans and win when an ally
qualifies; `spFighting` step 8, the Support's re-acquisition every tick, so a healer in melee
reach still finds its ally; the ally arm (`FightRules.CastOnAlly`: cast once, target and nibble
cleared, the ally not told). The Heal chain is built with it: the instant handler (type 34)
moves the shot onto its target, the death shot 4 chains, and the Modify Health handler (type 3,
`shots.md` §8) heals the lesser of the data word and the missing health.

**Not built, named (Q401):** the
`TurncoatTimer`, pit-fighter and torture refusals; the Imp Teleport and Imp Haste paths of
bits 5 and 6; the possessed-creature overrides. The buff shots fly as ordinary shots, so a
Haste at 3686 units a tick can pass a standing ally where the 2048-a-tick buffs cannot.

## Confidence

**CONFIRMED (binary):** the table contents and dispatch; every predicate and
strict comparison; the friendly slot overwrite, self-only Invisible arm,
candidate list and gates, distance, route and fallthrough; both readers each
of bits 5, 6 and 7; the bounded negative for bit 8.

**CONFIRMED (data):** the `AlternativeShot` and flag-bit membership of the
shipped spell records.

**CONFIRMED (review at merge, 2026-09-23):** the dispatcher and all five predicates
(`0x0049A7B0`—`0x0049A949`), the category helper `0x00478050`, and the walk's quarter-health
gate (`0x0045DCA3`, `SHR 2` after the level scale) were re-read from the binary; the
test Codex wrote as "state 22" is the state category, corrected above.

**STRONGLY INFERRED:** the table's four dwords are a compiler member-call
representation; only their observed invocation semantics are claimed above.

**UNKNOWN:** the source-level names of the unused `+8` and `+C` metadata
dwords. They have no effect in this call path and no nonzero value in a live
record.
