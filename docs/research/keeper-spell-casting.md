# Keeper spell permission and cast boundary

Independent read of `DKII.EXE` v1.70 with Ghidra 12.1.4/PyGhidra,
2026-09-28, Heal added 2026-09-29. Behaviour is **STRONGLY INFERRED**; no
matching original cast sequence was captured. This covers the helpers called by
the computer keeper, not the eventual effects of every shot process. Keeper
spell selection belongs to `computer-keeper-management.md` §4.

## 1. Availability and clocks

`0x004BAFA0` searches the player's spell table by id, returning availability
1 when absent. Entry layout, independently checked against the player dumper,
is in `player-object.md`. This lookup does not turn accrued research points
into availability.

`0x004BE390` returns zero when last-cast tick is zero. Otherwise cooldown is
`max(0, definition[+0x3E] - (currentTurn - lastCast))`. Equality is ready.
The position/creature permission helpers `0x004BB8A0`/`0x004BBA40` repeat this
test and check mana through `0x004C1740`: zero cost always passes; otherwise
cost must be at most current mana. This differs from the keeper's emergency
CTA selection gate, which requires strictly more mana than cost.

The helpers derive cost from descriptor `+0x58`, with the Create Imp
multiplier described in `imps-and-mana.md`. The position helper also refuses
spell 5 when an owned creature has descriptor flag `0x4000`, and spell 14
when player flag `0x1000` is set. Both then call location and target validators.
The creature helper additionally requires a live creature tag before passing
its position to location validation and its tag to target validation.

Create Imp (spell 4) selects location 1 and target 4, read from its record
(`KeeperSpells.kwd` bytes `0xEF` and `0xDA`; Call To Arms 4/4, Inferno 1/5,
Heal 4/1, Thunderbolt 1/2 read the same way agree with the selectors already
built). DK2's seeded Level 2 replays confirm the location arm for it: a cast on
neutral dirt makes nothing, one on the keeper's claimed floor makes an imp
(`imps-and-mana.md`, ADR-0312). `ImpSupplySystem.Buy` applies both through
`KeeperSpellRules.PlaceAllowed`.

## 2. Cast side effects

The position path is built for Call to Arms and Inferno: `KeeperSpellRules.CastAtPlace`
runs the helper below for spell 3 with selectors 4/4 (`call-to-arms.md`, ADR-0279) and
spell 9 with 1/5 (`keeper-spell-inferno-chicken.md`, ADR-0302).

Both cast helpers take three stack arguments: spell id, position pointer or
creature tag, and flags; `RET 0xC` and callers establish this. Their decompiler
output misplaces arguments after virtual calls, so the following bookkeeping
was checked in instructions as well.

`0x004BBD30` (position) uses flag bit 0 to bypass permission/payment. The
ordinary path checks permission and mana, chooses the normal or upgraded
shot descriptor, creates the shot, stores its two process parameters, and
starts it at the requested position. Creation failure returns zero. Successful
ordinary creation debits mana, adds to player `+0x10F2` (one-shot mana loss),
and stamps both last-cast and last-checked with the current turn
(`0x004BBEEE..0x004BBF4F`). Spell 14 also sets player flag `0x1000`.
A zero shot id returns success without creating a shot or charging mana.

`0x004BBF90` (creature) validates, creates a shot at the creature, stores its
target tag and process parameters, then debits mana and stamps both clocks
(`0x004BC1EB..0x004BC259`). Its final return is **zero even after successful
creation** (`0x004BC272..0x004BC27B`). The keeper battle routine ignores that
return after its separate permission test; interpreting zero as a reason to
retry would change its command stream. Before the shot and the payment, a
selected shot of process 3 with a positive data word refuses a target whose
health is not strictly below its level-scaled maximum (§4). Nonzero bypass flags
and the remaining process-specific effects need separate implementation checks.

## 3. Validation dispatch

`0x005176B0` selects location validation from descriptor byte `+0x76`:
six entries at `0x006FCDD8`, stride 17. `0x00517730` selects target validation
from byte `+0x77`: seven entries at `0x006FCE40`, stride 18. These are runtime
tables, initialized by `0x005174A0` and `0x00517580`, not readable static data.
Index zero is null and fails; out-of-range selectors fail. Callback mapping:

| Index | Location callback | Target callback |
|---|---|---|
| 1 | `0x00517B80` | `0x00517800` |
| 2 | `0x00517CB0` | `0x00517930` |
| 3 | `0x00517DE0` | `0x00517990` |
| 4 | `0x00517F10` | `0x005179D0` |
| 5 | `0x00517F60` | `0x00517F60` |
| 6 | invalid | `0x00517AB0` |

All callbacks were read. Location 1/2 use the nonenemy relation, 3 the enemy
relation; 4 omits ownership. They reject the world `+0x20C` predicate.
Selectors 2/3/4 conditionally require tile flag `0x2000` when cast flag bit 1
is set. Location 1 additionally excludes the neutral player. Location 1/2/3
and target 4 apply room-position exclusions for Portal, Heart, Prison,
Temple and Combat Pit. Selector 5 always returns true. Byte `0x11` of each target
entry (1 for selectors 1, 2, 3 and 6, 0 for 4 and 5) is what `0x005177B0` returns
to the keeper's battle step: a thing cast against a cast at a place. The descriptor
flag word `+0x54` is file `0xDC` (`0x004F7CE0` copies it from its intermediate at
`0x154`); the battle step reads its bits 2, 3 and 4 (`computer-keeper-management.md` §4).

Target 1 normally admits own creatures, with room-owner and Pit-fighter
exceptions, excluding descriptor flag `0x4000` in its ordinary ownership arm.
Cast flag bit 2 instead selects foreign nonneutral creatures. Target 2 reverses
that flag choice; target 3 accepts any live nonneutral creature. Target 6 has
additional state/status restrictions, or accepts an owned type-9 object (read in full:
`functions/00517AB0.md`).
These predicates are not interchangeable with pickup permission.

**Thunderbolt gates resolved:** see `keeper-spell-thunderbolt.md` §2 for the
world `+0x20C` predicate, Heart-position helper `0x004DA150`, and shipped
selectors. Target 6's tests are read in full (`functions/00517AB0.md`).
A cast command must follow the selected callbacks rather than substitute
a generic ownership or range check.

## 4. Heal — the claim audit (2026-09-29)

Shipped record 10 (`KeeperSpells.kwd`, CONFIRMED by reading the file): shot 4,
data 16000/0, upgraded shot 4 with 25000/0, cost 5000, cooldown 0, location 4,
target 1, flag word `0x8` (supportive), no upkeep. Shot 4, "Modify Health"
(`Shots.kwd`): process 3, process flags `0x4` (the thing struck), flags `0x23`,
collide class 3, health 128, speed 3686, no death or hit shots. The creature
Heals reach the same shot as the death shot of their instant shot 53
(`shots.md` §8, §9); the keeper fires it directly.

**Path (STRONGLY INFERRED).** Action 55 → `0x004BBF90` (§2): the mana test
`0x004C1740` (zero passes, else cost ≤ mana, `0x004BC038`), the permission
`0x004BBA40`, the row's `Upgraded` byte (`+0xD`) choosing the data words (the
creation still uses the normal shot byte, `0x004BC160`), then at
`0x004BC0C5..0x004BC13E`: a selected shot of process 3 with data1 > 0 computes
the target's level-scaled maximum (`Hp`, or `Hp × table[+0xE1+level×2] / 100`)
and **returns 0 before creating or paying when health ≥ maximum** (`JGE`). The
shot is made at the creature's position, its words written (`+0x13D` data1,
`+0x141` data2, `+0x147` the target tag), and for target selectors 1, 2, 3 and
6 the forced-collision word `+0x14D` is set (`0x004BC1CF`); the mana is debited,
`+0x10F2` credited and both clocks stamped, as for Thunderbolt.

**Permission (STRONGLY INFERRED).** `0x004BBA40`: cost against mana, then the
cooldown (`0x004BE390`'s arithmetic inline), then a live creature tag, then
location 4 (`0x00517F10`) at the creature's position: the world `+0x20C`
predicate must be zero (the navigation-solid bit), with cast flag bit 1 also
wanting tile byte `+9 & 0x20`; no ownership. Then target 1 (`0x00517800`),
flags zero, in this order: a creature holding a room id (`+0x3B9`) passes iff
that room's owner (`+0x1D`) is the caster; else a `CombatPitFighter` (`+0x3E6`
bit 15) passes iff the tile under it names a room (`0x004559B0`) the caster
owns; else the creature passes iff its kind lacks `IsHorny` (`+0x686` bit 14)
and its owner is the caster. Flag bit 2 instead takes any live creature owned by
neither the caster nor neutral. Neither validator asks range, fog or health.

**Effect (CONFIRMED binary, 2026-09-23 and re-read).** The collision entry
`0x004ABD60` with `+0x14D` set hits the target tag if live, clears the word
either way, and runs no finder that tick; the hit raises process event 4, so
`0x004ADB30` (shot process 3, `shots.md` §8) runs: a live, non-Horny target;
the Disruption arm for shot 43; otherwise `amount = min(data1, max − health)`,
then, when amount > 0 and the target is not frozen (`+0x447`), held (`+0x3CB`),
a pit fighter or attacking abnormal targets (`+0x3E6` bit 2), `SetState(1)`
(`rebellion.md` §7.2), which ends its walk with its state; then the health
routine `0x004B2CE0(amount, owner, 0)`, which for a positive delta clamps at the
maximum again, refuses the dying flag (`0x004B2D9F`), a virtual predicate at
vtable `+0x74` (unread) and a kind flagged `OnlyAttackableByHorny` (`+0x686`
bit 22), and applies no multiplier and no stun or fear. The shot then dies on the thing (flags bit 1).
So a normal cast heals the whole deficit of any shipped creature (16000 exceeds
every level-scaled `Hp`), interrupts what the target was doing, and a dying
target costs the mana and gains nothing.

**Battle step (STRONGLY INFERRED, `computer-keeper-management.md` §4,
re-read 2026-09-29):** the supportive arm keeps Heal as the last eligible
supportive spell whatever its roll, scans own creatures strictly within five
tiles of the fighter for one strictly below half health without the dying flag,
skipping no held creature, and casts through `0x004BBA40` then `0x004BBF90`,
spending one allowance on a passed permission.

**Built (ADR-0287):** `KeeperSpellRules` spell 10 with `LocationAllowed` (1, 4),
`TargetAllowed` (1, 2) and the `TargetAtMaxHealth` refusal; the forced word as
`ShotState.ForcedCollision` (hash 140), spent by `ShotRules.FindHit`; the idle in
`ShotRules.ModifyHealth`; `KeeperBattleStep` issues the cast from the captured
`HealTargetAllowed` (schema 12). **Approximation:** a creature's room owner is
the owner of the floor under it, as no room id is kept on a creature.
**UNKNOWN:** the vtable `+0x74` predicate; DK2's timing between the cast and the
shot's first update (ours lands the same tick); the original run (Q491).

## 5. The thirteen spells, built and upgraded (2026-10-04)

The shipped KeeperSpells records are ids 1–11, 13 and 14. Research upgrades a spell
when its record has a bonus shot (`0x004C3000`, research-tree.md §2); the grant
`0x004E0F00` is the only caller of the Upgraded setter `0x004BB070` (call xrefs;
direct stores to a row's +0x0D were not scanned). Every cast path
(`0x004BBD30`, `0x004BBF90`, and the unbuilt third path `0x004BC290`, Q537) then
takes the bonus shot and data. Besides the casts, which read it inline, the flag's reader `0x004BB0D0` is
called only by the Possession and Call to Arms upkeep, research and the interface.

| Id | Spell | Built | Upgraded form (normal → upgraded) |
|---|---|---|---|
| 1 | Sight Of Evil | no: presentation (`keeper-spell-sight.md`, Q194/Q534) | radius data1 5 → 7 |
| 2 | Possession | simulation side, ADR-0350 | same shot; upkeep × variable 226 (70 %) |
| 3 | Call To Arms | ADR-0279 | same shot; upkeep × variable 227 (70 %) |
| 4 | Create Imp | ADR-0247 | shot 2 level 1 → shot 60 level 4 |
| 5 | Summon Horny | ADR-0350 | no bonus shot: never upgraded |
| 6 | Thunderbolt | ADR-0283 | data1 0x96 → 0xC8 |
| 7 | Turncoat | ADR-0350 | data1 25 → 40 |
| 8 | Tremor | ADR-0350 | data1 5 → 7 (20 → 28 turns) |
| 9 | Inferno | ADR-0302 | bonus data (`keeper-spell-inferno-chicken.md`) |
| 10 | Heal | ADR-0287 | bonus data (§4) |
| 11 | Chicken | ADR-0302 | bonus data (`keeper-spell-inferno-chicken.md`) |
| 13 | Create Gold | ADR-0350 | gold 1,000 → 2,000 |
| 14 | OneShotReaper | ADR-0350 | no bonus shot: never upgraded |

`KeeperSpellUpgradeTests` reaches each upgrade by research; the spells' own tests
cover the upgraded effects, and `ResearchTreeOnRealDataTests` pins the shipped shots.
