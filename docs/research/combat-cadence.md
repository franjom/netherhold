# Mechanic: combat cadence — the two attack clocks

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-16. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- The reference install's `Data/editor/Creatures.kwd`, swept directly

Original version: DK2 v1.70 (GOG)

Questions: **Q327** (answered here), Q336 (opened here). Splits the cadence out
of `combat.md`, which was at its line budget.

**Built (ADR-0164):** both clocks in ticks — `CombatRules.TicksOf` — Gate B as
`CombatRules.SwingMayBegin` on `TimeLastUsedMelee`, Gate A as `CombatRules.BlowLands`
on the swing's start tick and `MeleeHitDelay`, the `0xCF9` field now parsed (Q336 keeps
its shipped name open). Haste and slow on Gate B are not. combat-fight-state.md §4 has
the swing they gate.

---

## 1. Q327, answered: they are two fields

`combat.md` §5 put `MeleeRecharge` at `def+0x5C6`; §6.2 found the swing's own
gate reading `dword[def+0x5CA]` and could not tell whether that was a second
field or a mis-assignment. **It is a second field**, and §5 was right.

Every access in the image, by width, gives the block's boundaries:

| offset | width | field | who reads it |
|---|---|---|---|
| `def+0x5C0` | dword | `MeleeRange` | `FUN_0045E190` |
| `def+0x5C4` | **word** | `MeleeDamage` | `FUN_00494D00`, twice |
| `def+0x5C6` | dword | **`MeleeRecharge`** | `FUN_00494EF0`, which has **6 callers** |
| `def+0x5CA` | dword | the attack-gate interval | `FUN_00498CA0`, `FUN_00498D20`, `FUN_00498E34` |
| `def+0x5CE` | dword | — | — |

The widths are not inferred. `FUN_0051F3B0`, the definition copier, moves all
five in order — dword, word, dword, dword, dword — with no gap, so the
boundaries are settled by live code even where the meaning is not.

## 2. Where each comes from in the shipped record

`FUN_00492320` copies the loaded record into the compact definition field by
field, each copy guarded by its own presence dword. Its pairs, with the
`+0x3F0` record-to-frame delta (`payday.md` §6) applied:

| frame | file | destination |
|---|---|---|
| `EBP+0x12DC` | `0x0EEC` `MeleeRange` | `def+0x5C0` |
| `EBP+0x1304` | `0x0F14` `MeleeDamage` | `def+0x5C4` |
| `EBP+0x12E8` | `0x0EF8` **`MeleeRecharge`** | `def+0x5C6` |
| `EBP+0x10E9` | `0x0CF9` — **unnamed, and we do not parse it** | `def+0x5CA` |

The delta is confirmed inside this same routine rather than assumed: it opens
by byte-summing `0x1939` bytes of the record into `def+0x5AF`, and
`0x1939 − 0x3F0 = 0x1549 = 5449`, which is exactly `KwdCreatures`' record size.

**File `0x0CF9` is a real field.** Our parser maps `0x0CFF` and `0x0D01` and
leaves `0x0CF9` in a gap. Swept across the shipped table it holds 31 distinct
clean two-decimal `fp32` seconds — 0.75, 1.25, 0.50, 0.95, 0.25, 0.47, 1.17,
0.83 — which a misaligned read does not produce. Its shipped name is **Q336**.

## 3. The load-time conversion: seconds become ticks

Both fields are multiplied on the way in:

the loader reads `MeleeRecharge` (fp32 seconds) from the file record at
`+0x12E8` (base register `EBP`), multiplies it by `EDI`, shifts the product right
by 12 to undo the fixed point, and stores the result, now in ticks, at
`def+0x5C6` (base register `EBX`).

`EDI` is `[0x006E4154 + 0x40]`, **variable 19**, which `training.md` §4 already
established is **4 game ticks a second in all 66 shipped levels**. So both
fields land as integer tick counts, and the consumer confirms it: `FUN_00494EF0`
reads `def+0x5C6` and immediately does `SHL EDX,0xc`, promoting an integer back
into 12-bit fixed point. Nothing else in the image performs that conversion.

This is the same shape `training.md` found for the per-second columns, and it
truncates the same way — `MeleeRecharge` 1.20s becomes 4 ticks, i.e. **1.00s**.

| creature | `MeleeRecharge` | in ticks | gate interval | in ticks |
|---|---|---|---|---|
| Imp | 1.20s | 4 | 0.00s | **0** |
| Goblin | 1.30s | 5 | 0.75s | 3 |
| Troll | 1.30s | 5 | 0.25s | **1** |
| Bile Demon | 3.50s | 14 | 1.25s | 5 |
| Vampire | 2.50s | 10 | 0.50s | 2 |
| Wizard | 2.80s | 11 | 1.75s | 7 |
| Horny | 1.50s | 6 | 0.75s | 3 |

All fourteen `Unique *` records and the Imp ship **zero** at `0x0CF9`, and their
`IntroductionStringId` at `0x0CFF` is zero too — the whole region is unwritten
for them, so this is a property of those records, not of the read.

## 4. The two gates are separate clocks

**Gate A — `creature+0x3A5` against `def+0x5CA`.** Three routines share it
verbatim, all in the attack cluster:

1. The interval is read from the definition (`creature+0x370`) at `def+0x5CA`.
2. The game turn is fetched from `0x00517490` (`trigger-state.md` §6).
3. The last-swing stamp is read from `creature+0x3A5`.
4. The gate passes only when the turn is strictly greater than
   `stamp + interval - 1` — that is, `turn >= stamp + interval`; otherwise
   "not yet" (an unsigned compare).

`FUN_00498E34` is the third, so **the swing's cadence is `def+0x5CA`, not
`MeleeRecharge`**. Eighteen instructions write `creature+0x3A5`; four are in
`FUN_00496700`–`FUN_00496D80` and the rest sit in an unnamed run at
`0x004997D8`–`0x0049A457`.

**Gate B — `creature+0x427` against a scaled `MeleeRecharge`.** `FUN_0048C9B0`
is a two-line predicate over a different stamp:

it takes the modified `MeleeRecharge` in ticks from `0x00494EF0`, shifts it
left by 12 into fixed point, and divides it by a dword reached through
`creature+0x3AF` and `+0x3B3`; it then adds the stamp at `creature+0x427` and
returns `now >= stamp + interval`.

So `MeleeRecharge` is genuinely a recharge — it simply is not the one the melee
swing waits on. Gate B gates **starting** melee action 17 in the fight state's
melee arm; Gate A then times the blow inside it (combat-fight-state.md §4).

`FUN_00494EF0` itself is `__thiscall` on the definition taking the creature, and
folds the same two instance flags `FUN_00494D00` uses for damage — `+0x3E6`
bit 7 with ceiling 100.0, `+0x3EA` bit 3 with ceiling 200.0, both against global
`+0x351`, plus `+0x460` in the first arm — before combining with `def+0x5C6`.
The three helpers (`0x0043D750`, `0x00441990`, `0x004363F0`) are 12.12
fixed-point add, divide and multiply, and the flag arms are mutually exclusive
(combat-damage.md §1).

## 5. Is `FUN_00492320` live?

It has **0 callers and 0 absolute occurrences** across every initialised block,
and the `0x1939` checksum loop occurs once in the image, so there is no inlined
twin to find. That is the same signature as `FUN_00498E34`, whose reachability
`combat.md` §6.1 already attributes to a table built at runtime — so
"unreferenced" here is not evidence of dead code.

The positive argument is stronger than the parallel: `FUN_00494EF0` reads
`def+0x5C6` as an integer and re-promotes it, so *something* converts seconds to
ticks at load, and this routine is the only code in the image that does. The
same argument already carries `training.md` §4, which reads its pairs as the
loader's.

**Residual doubt.** If instead it is linker-retained dead code from a build
where the definition was assembled differently, the *layout* it encodes still
holds — `FUN_0051F3B0` confirms that independently — but the file-to-runtime
*mapping* for `def+0x5CA` would be inherited from a stale twin. One owner
observation discriminates: a Troll's gate interval is 1 tick and a Bile Demon's
is 5, so **a Troll should swing about five times as often as a Bile Demon**.

## 6. What this project builds

- `MeleeRecharge` and the `0x0CF9` field are **both** per-creature integers in
  ticks, converted once at load with `× 4 >> 12`. Store the ticks, not the
  seconds, or the truncation is lost.
- The swing is due when `turn >= lastSwingTurn + interval`, with the `- 1` the
  binary writes folded in — an interval of 0 means every turn.
- `MeleeRecharge` belongs to Gate B and must not be wired to the swing.

## Confidence

**CONFIRMED (binary):** the five field boundaries and their access widths; that
`def+0x5C6` and `def+0x5CA` are distinct live fields with disjoint readers; both
gates' arithmetic; that `EDI` is `[0x006E4154+0x40]`.

**CONFIRMED (shipped data):** the two columns and their tick conversions.

**STRONGLY INFERRED:** that file `0x0CF9` is the source of `def+0x5CA`, and that
`FUN_00492320` reflects the live loader (§5).

**UNKNOWN:** the shipped name of file `0x0CF9` (Q336); why
the `Unique *` records ship zero.
