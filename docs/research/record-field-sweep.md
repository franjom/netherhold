# The creature and object records, swept for fields that carry no information

**2026-09-07, Q213.** The balance table was swept for zero-shipped values on
2026-09-06 and ten of the twelve turned out to be **live mechanisms switched
off** (ADR-0085). **The creature and object records had never been swept at
all**, which is the larger surface: 56 scalar fields per creature across 46
creatures, and 21 per object across 145 objects.

New tool: **`dk2tools kwd fieldsweep creatures|objects`**. It reflects over the
parsed record type and reports, for every scalar field, how many distinct values
it takes across all records — splitting them into *zero everywhere*, *the same
non-zero value everywhere*, and *varies*. Both of the first two categories are
worth knowing: **a field that never varies cannot discriminate between
creatures, so any rule this project keys on one is either dead code or a rule we
invented.**

## Creatures: two fields are zero for all 46

| field | file offset | runtime offset | verdict |
|---|---|---|---|
| `GuardDuration` | `0xED4` | `+0x750` | **CUT** |
| `IdleDuration` | `0xED8` | `+0x754` | **CUT** |

And a third, found beside them, which is the more interesting case:

| field | file offset | runtime offset | verdict |
|---|---|---|---|
| `SlapFearlessDuration` | `0xEDC` | `+0x758` | **CUT — and its values vary** |

**All three are parsed, converted, copied, and never consumed.** Each has
exactly three references in the executable and no fourth:

- `FUN_00491DC0` — the definition initialiser, zeroing the field;
- `FUN_00492320` — the loader, writing it;
- `FUN_0051F3B0` — the definition **copy** routine, which loads it from one def
  and stores it into another.

A load-then-store pair inside a copy routine is not a consumer, and neither is
an initialiser. **Nothing else in either executable block touches them.**

**`SlapFearlessDuration` is the one to remember.** Two of these three are zero
everywhere, which is at least a hint. That one **takes two distinct values** —
somebody set it deliberately for some creatures — and still nothing reads it.
Varying data looks live in a way that zeroed data does not, and it is not
evidence of anything. This project does not use any of the three (they are
parsed into `CreatureDef` and printed by `dk2tools kwd creatures`, nothing more),
so there is nothing to change.

## Creatures: four fields are constant across all 46

| field | value everywhere |
|---|---|
| `DistanceCanSee` | 4 |
| `DistanceCanHear` | 4 |
| `PerceptionRange` | 6 |
| `UnhappyThreshold` | 50 |

`UnhappyThreshold` was already known and documented. The three ranges matter
more than they look: **every creature in the game sees four tiles, hears four
and perceives six.** So any rule that reads a *per-creature* sight range is
reading a constant, and a reimplementation is free to treat these as global
constants — but must not invent per-creature variation to make a behaviour work,
because the data cannot express it.

## Objects: one field is zero for all 145

`ManaValue` (file `0x34E`) is **zero in every one of the 145 object records**.
This project never reads it outside the dump tool, and one behaviour test
already asserts a bed's is zero.

**The binary side of this one is not done**, and the reason is worth recording
rather than glossing. The trick that resolved the creature fields — go from file
offset to runtime offset through the loader — needs the *source* delta for the
record type, and the objects loader does not use the creatures one. A query for
the offset the creature convention predicts returned zero hits, with a positive
control passing in the same session, so that is a real mismatch and not a broken
scan. Finding the objects loader is the next step. `ManaValue` is therefore
**"zero in all shipped data, consumer unknown"** — not cut, not disabled.

## The method this produced, which is worth more than the findings

Locating a creature field's runtime offset used to be guesswork, and guessing
cost a wrong answer during the hero-portal work: reasoning from adjacency put
the second flag field at file `0xF00`, which holds a 3000/2800 value.
**The loader gives the mapping exactly, and the map is a constant.**

`FUN_00492320` is the creature-definition loader. Its shape, repeated once per
field for ~130 fields:

```
MOV  EAX,dword ptr [EBP + 0x25c]    ; this field's "was it set?" flag
TEST EAX,EAX
JZ   skip                            ; leave the initialiser's value alone
MOV  ECX,dword ptr [EBP + 0x12ec]    ; SOURCE: the file record
MOV  dword ptr [EBX + 0x686],ECX     ; DEST:   the runtime definition
```

Two things fall out.

**1. `source offset = file offset + 0x3F0`.** Confirmed on two fields at
opposite ends of the record: the flags dword is file `0xEFC` → source `0x12EC`,
and the second flag field is file `0x1545` → source `0x1935`. Both deltas are
`0x3F0` exactly. So **to find any creature field's runtime offset: add `0x3F0`
to its file offset, search for reads of that offset, and read the store that
follows.** `ReadsAt.py` does the search.

**2. Every field is behind a presence flag.** The dwords at `EBP + 0x1F8`,
`0x1FC`, `0x200`, `0x254`, `0x258`, `0x25C`, `0x260` … are a per-field *"this
field was set"* array, tested before each copy. That is an editor mechanism —
an unset field keeps whatever the initialiser gave it — and it means **a field's
presence flag being clear is a third way for shipped data to say nothing**,
alongside a zero value and a constant value.

### Two things read on the way past

- **The loader converts seconds to ticks itself**, with
  `IMUL reg,EAX ; SHR reg,0xc` — value × ticks-per-second ÷ 4096 — which is the
  same load-time conversion the balance table's health variables get as a `/ 4`.
  So a duration field's runtime value is in **ticks**, not in the file's
  fixed-point seconds.
- **Unique creatures take a different construction path.** The dispatcher above
  the loader tests `[source + 0x12EC] & 0x8000000` — the flags dword, bit 27,
  `IsUnique` — and calls one of two different virtual methods before invoking
  the loader.

### And a trap in the tool, which is the opposite of the day's other traps

`ReadsAt.py` filters instruction text for `+ 0x<off>]`, so it matches **stack
offsets as well as struct fields**. The scan for `+0x758` returned a fourth hit,
`MOV ECX,dword ptr [ESP + 0x758]` — a local variable in an unrelated routine,
not a definition field. Every other false result today was a false *negative*;
this one inflates a count, which would have turned "cut" into "has a reader".
**Check the base register before counting a hit.**

---

## Q213 answered: `ObjectDef.ManaValue` is CUT

**2026-09-07.** The objects loader is **`FUN_0049DD60`**, and its source delta is
**`file + 0x80`** — found by aligning our thirteen known object file offsets
against every loader's source offsets in the binary at once (`CopyPairs.py`
harvests every `[EBP + imm]` load followed by a store to a struct field, which is
the shape every KWD record loader uses). The match is **13 of 13 on one delta**,
and the destination offsets are a genuine rearrangement rather than a constant
shift, which is what distinguishes a loader from a block copy:

| file | source | runtime |
|---|---|---|
| `0x32C` `Width` | `0x3AC` | `+0x0F8` |
| `0x330` `Height` | `0x3B0` | `+0x0FA` |
| `0x344` `Flags` | `0x3C4` | **`+0x010`** |
| `0x348` `Hp` | `0x3C8` | `+0x104` |
| **`0x34E` `ManaValue`** | `0x3CE` | **`+0x108`** |
| `0x350` `TooltipStringId` | `0x3D0` | `+0x10A` |
| `0x35A` `ObjectId` | `0x3DA` | `+0x113` |

**`ManaValue` is never read.** Every access to `+0x108` at **word** width in the
whole executable is a **store**: the object-definition initialiser
(`FUN_0049DC80`), this loader, and one unrelated struct in the video region.
**Zero word-sized reads.** Together with the data — zero in all 145 shipped
records — that makes it **CUT**, not disabled.

**The one caveat, stated and bounded.** There are dword reads of `+0x108` in the
binary, and a dword there would cover `ManaValue` in its low half and
`TooltipStringId` (`+0x10A`) in its high half — so a folded reader at that offset
would be after the *tooltip id*, not the mana value. And since all 145 records
ship zero, the mechanism is inert whichever way that falls. The bases of those
dword reads have not been typed, which is why this is a caveat rather than a
proof.

**Method note.** `CopyPairs.py` is the reusable half: it dumps every loader's
source→destination pairs in one Ghidra run, so identifying *any* record type's
loader is now an offset-alignment problem rather than a hunt. The creature delta
is `0x3F0` and the object delta is `0x80`; each record type has its own.
