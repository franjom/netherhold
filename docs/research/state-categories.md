# Mechanic: the 32 state-category records

Source(s): `DKII.EXE` v1.70 (GOG) read with Ghidra 11.0.1 on 2026-09-10 — the fill
run, the transition routine `FUN_00476D30`, the per-tick dispatcher
`FUN_00476FC0`, and the two accessors of §4. Flame's map was consulted on six
function addresses and named none of them (ADR-0046; ADR-0080).

Original version: DK2 v1.70 (GOG)

**This is the category table's home.** `eating.md` §10 found it and points here;
`creature-state-table.md` owns the 281 *state* records and the transition rule
that consumes these. Confidence: **STRONGLY INFERRED** throughout — every fact
is a binary read, and none has been watched in the running game.

## 1. What it is

**32 records of `0x40` bytes based at `0x00699688`**, indexed by a state's
category byte (`+0x17` of a state record). Like the state table it lives in
uninitialised `.data`, so there is nothing to read on disk: it is filled at run
time by a straight-line run of `MOV`s between **`0x004798F4` and `0x0047A773`**,
which no function contains.

Recovered by walking that run and simulating register values. `Q300`.

### The accounting

| | Check |
|---|---|
| 1 | **Exact identity.** 512 dwords = 32 records x 16 fields, and the walk resolves **511** of them |
| 2 | **The extent is not the scan's.** A whole-program scan for writes into the range finds the same 511 dwords, and the writing instructions span exactly `0x004798F4`-`0x0047A773` — so the hand-chosen window did not decide the answer |
| 3 | **Contiguity.** Every field is written for all 32 records, bar the one below |
| 4 | **Three rows agree with an earlier independent reading** — categories 2, 8 and 9's hooks match `eating.md` §10 exactly |
| 5 | The one unresolved store targets address 0 from `ESP`, outside the table: a function epilogue, not a record |

**The single missing dword is record 0's `+0x00`**, and it is missing because
nothing in the program writes it — see §3.

One register idiom had to be modelled to resolve the rest: `OR ECX,0xFFFFFFFF`
yields all-ones whatever the register held, so those 59 stores resolve without
assuming a prior value.

## 2. Record layout

| Offset | Field | Status |
|---|---|---|
| `+0x00` | the category's own **bit**, `1 << index` | §3 |
| `+0x04` | **enter** hook, run by the transition routine | address not carried |
| `+0x08`, `+0x0c` | always zero in all 32 records | |
| `+0x10` | **`-1` when the enter hook is null, `0` when it is set** | |
| `+0x14` | **update** hook, run every tick before the state's own handler | |
| `+0x18`, `+0x1c` | always zero | |
| `+0x20` | the same null flag for the update hook | |
| `+0x24` | **leave** hook | |
| `+0x28`, `+0x2c` | always zero | |
| `+0x30` | the same null flag for the leave hook | |
| `+0x34` | the **entry mask**, §4 | |
| `+0x38` | the category's own index | §3 |
| `+0x3c` | a small id, §5 | role traced, meaning open |

**A hook is a four-dword slot, not "a pointer with three unused words".** The
transition routine loads all four (`[EDX]`, `[EDX+4]`, `[EDX+8]`, `[EDX+0xc]`)
before calling, and the fourth is `-1` for every null hook and `0` for every set
one, across all 96 slots without exception. The middle two are always zero. The
shape and the null convention are what MSVC emits for a **pointer to member
function**, which fits a binary that also ships RTTI — that reading is
HYPOTHESIS; the `-1`-iff-null rule is measured.

This corrects Q186's "each hook is a 16-byte slot with only the first word
used".

## 3. Two fields that are not what they look like

**`+0x38` is the identity.** It holds the record's own index for all 32, with no
exception. The transition routine indirects through it twice — `index[index[t]]`
— and because it is the identity that collapses to `t`. An earlier reading of
this project's took the double indirection to mean `+0x38` named a
*representative* category; it does not.

**Record 0's bit is zero, and nothing writes it.** The fill run stores 511 of
the 512 dwords and never `0x00699688`. A whole-program reference scan confirms
it: fourteen instructions **read** that address, across three functions, and not
one writes it. So the refused category's bit is permanently `0`.

The consequence is load-bearing. The mask test is `bit[from] & mask[target]`, so
a creature whose current category is 0 can never be admitted by a mask — and
several masks *do* name category 0 (bit 0 set), which means **those bits can
never fire**. Whether that is deliberate or a shipped defect is UNKNOWN; the
behaviour is the same either way, and this project reproduces it.

## 4. The entry mask — `+0x34`

It is the **target**'s mask, tested against the bit of the category being left,
so it says *which categories may enter this one*. `eating.md` §10 described it
as "a mask of categories this one may be left for", which is backwards.

**Zero means unrestricted, not closed.** The routine admits the move outright
when the mask is zero, before any bit test. Five categories ship a zero mask —
the refused one, movement, prison, torture and the combat pit — and ten ship
all-ones.

The distinct restrictive masks are few, which is why the categories fall into
groups that admit the same predecessors:

| Mask | Categories carrying it | Admits (bit set) |
|---|---|---|
| `0x8002383B` | 4, 5, 10, 11, 12, 13, 17, 19, 31 | 0, 1, 3, 4, 5, 11, 12, 13, 17, 31 |
| `0x800A3C3B` | 6, 8, 9, 20 | adds 10 and 19 |
| `0x80123B7B` | 21 | adds 6, 8, 9, 20; drops 10, 19 |
| `0x801A3F7B` | 25 | the widest restrictive one |
| `0x00000003` | 3, 18 | 0 and 1 only |

## 5. `+0x3c` — the role, not the meaning

Two accessors read it, at `0x00478ACC` and `0x00478B73`, and both have the same
shape: take the creature's current category, substitute the state object's
`UtilityNewState` if that category is movement, and return
`record[category].+0x3c`. Both let a field of the state object override the
result, and both special-case the combat category, returning one value for it
and another for everything else.

So the field is **a per-category default for a small id, overridable per
creature**. Its callers sit outside the creature units, in the `0x0041xxxx` and
`0x005xxxxx` ranges; one of them compares the result against `0x4B` and then
calls into what looks like the audio path. That last reading is **HYPOTHESIS**
and is the reason the field is not named: Flame names none of the six functions
involved, and the creature-state debug dumper `FUN_00479200` does not print the
state object's own `+0x3c` either.

Values in the table run 2, 4, 5, 6, 12, 16-22 and 29; the accessors can also
return 25 and 26, which no record holds.

## 6. What is still open

- **`+0x3c`'s meaning** (§5). The next step is the caller at `0x004BFBD0`, which
  is the shortest of the three and the one that compares against `0x4B`.
- Whether record 0's zero bit is deliberate (§3).
- The **hook addresses** are recovered but deliberately not carried into `src/`:
  nothing in this project calls them. They are the reading list for naming the
  seven categories `CreatureStateCategory` still leaves as `UnnamedNN`, and ten
  categories have no hook at all, so for those the states themselves are the
  only evidence there will be.


## 7. The 32 records, as recovered

The values themselves, so this note is the table's source and the generated code
is downstream of it. `bit` is `+0x00`, `mask` is `+0x34`, and `E`/`U`/`L` say whether
the enter, update and leave hooks are set. Hook *addresses* are deliberately absent:
nothing in this project calls them (§6).

| Cat | Our name | bit | mask | E | U | L | `+0x3c` |
|---:|---|---|---|:-:|:-:|:-:|---:|
| 0 | Refused | `0` | `0x00000000` | . | . | Y | 2 |
| 1 | Idle | `0x00000002` | `0xFFFFFFFF` | . | . | . | 5 |
| 2 | Movement | `0x00000004` | `0x00000000` | . | . | . | 5 |
| 3 | WorkerJob | `0x00000008` | `0x00000003` | . | . | Y | 29 |
| 4 | Research | `0x00000010` | `0x8002383B` | Y | Y | Y | 20 |
| 5 | Manufacture | `0x00000020` | `0x8002383B` | Y | Y | Y | 20 |
| 6 | Lair | `0x00000040` | `0x800A3C3B` | Y | Y | . | 6 |
| 7 | Unnamed7 | `0x00000080` | `0xFFFFFFFF` | . | . | . | 6 |
| 8 | Eating | `0x00000100` | `0x800A3C3B` | . | Y | . | 6 |
| 9 | Sleeping | `0x00000200` | `0x800A3C3B` | Y | Y | Y | 12 |
| 10 | Sulking | `0x00000400` | `0x8002383B` | Y | Y | . | 5 |
| 11 | GuardPost | `0x00000800` | `0x8002383B` | Y | . | Y | 22 |
| 12 | Temple | `0x00001000` | `0x8002383B` | Y | Y | Y | 18 |
| 13 | Training | `0x00002000` | `0x8002383B` | Y | Y | Y | 21 |
| 14 | Prison | `0x00004000` | `0x00000000` | Y | Y | Y | 16 |
| 15 | Torture | `0x00008000` | `0x00000000` | Y | Y | Y | 17 |
| 16 | CombatPit | `0x00010000` | `0x00000000` | Y | Y | Y | 21 |
| 17 | Casino | `0x00020000` | `0x8002383B` | Y | Y | Y | 19 |
| 18 | Unnamed18 | `0x00040000` | `0x00000003` | . | . | . | 6 |
| 19 | Unused | `0x00080000` | `0x8002383B` | . | . | . | 5 |
| 20 | Payday | `0x00100000` | `0x800A3C3B` | . | . | . | 6 |
| 21 | Unnamed21 | `0x00200000` | `0x80123B7B` | . | . | . | 6 |
| 22 | Combat | `0x00400000` | `0xFFFFFFFF` | Y | Y | Y | 4 |
| 23 | Dying | `0x00800000` | `0xFFFFFFFF` | . | Y | . | 5 |
| 24 | TrapDisarm | `0x01000000` | `0xFFFFFFFF` | . | . | Y | 6 |
| 25 | CallToArms | `0x02000000` | `0x801A3F7B` | Y | . | Y | 6 |
| 26 | ReportingDanger | `0x04000000` | `0xFFFFFFFF` | . | . | Y | 5 |
| 27 | Unnamed27 | `0x08000000` | `0xFFFFFFFF` | . | . | . | 5 |
| 28 | Unnamed28 | `0x10000000` | `0xFFFFFFFF` | . | Y | . | 5 |
| 29 | Unnamed29 | `0x20000000` | `0xFFFFFFFF` | . | . | . | 5 |
| 30 | Incapacitated | `0x40000000` | `0xFFFFFFFF` | . | . | . | 5 |
| 31 | Spectating | `0x80000000` | `0x8002383B` | Y | Y | Y | 21 |

Record 0's `bit` is **0** because nothing writes it (§3), not because it is zero-valued.
## 8. What the labels say about the seven unnamed ones

Every state carries a `Text.str` id (`creature-state-table.md` §3), so a category
can be described by the labels of the states in it. That named one of the seven
and ruled out the rest, which is worth recording so the next reader does not
repeat the sweep.

| Category | States | What their labels say |
|---|---|---|
| **26 → `ReportingDanger`** | 2 | both `reporting danger`, and **no other category holds one** |
| 27 | 3 | all `fleeing` — but `Combat` holds three more, so the label does not pick this category out |
| 18 | 34 | 9 `stealing booty`, 6 `idle`, 4 `attacking walls`, 3 `attacking rooms`, 2 each `attacking Dungeon Heart`, `advancing`, `destroying rooms`, 1 `gambling` |
| 7 | 29 | 8 `idle`, 7 `stunned`, 5 `joining your Dungeon.`, 4 `leaving Dungeon`, 2 `burning in Lava`, 1 `eating`, 2 unlabelled |
| 28 | 15 | every one `idle`, over the contiguous run 228–242 |
| 29 | 2 | `idle` and `hypnotised` |
| 21 | 1 | `idle` |

**Only 26 clears the bar** the enum sets — name it from its states only where they
are unambiguous. 27 is the instructive near-miss: a label shared with another
category names neither, and what separates the two clusters is unread. The rest
are dominated by `idle`, which is the commonest label in the table at 49 states
and says nothing about a mechanic.

The route that would settle the other six is the handlers, not the labels: each
category's enter/update/leave hooks are in §7 and none has been read.

## Implementation and tests

`CreatureStateCategoryTable` carries all 32 records — bit, entry mask, whether
each hook exists, and `+0x3c` — generated from this note's dump, and
`CreatureStateCategoryTableTests` re-asserts §1's checks and §3's two anomalies.
`CreatureStateTransitions` is now complete: with the mask in hand, **every one of
the 78,680 ordered pairs of distinct states resolves**, 37,791 allowed and
40,889 refused, and `NeedsCategoryTable` is gone.
