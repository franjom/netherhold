# Mechanic: the creature state table

Source(s): `DKII.EXE` v1.70 (GOG) read with Ghidra 11.0.1 on 2026-09-06; the
reference install's own `Data/Text/Default/Text.str`, decoded with `DK2.Data`'s
`Dk2StringTable`; Flame's v1.70 symbol map for the cross-check in §5.4 only —
consulted, never quoted (ADR-0046; plan §2.2.1).

Original version: DK2 v1.70 (GOG)

This is the table's home; `eating.md` §2.1 points here. Confidence:
**CONFIRMED** for §1–§5, **UNKNOWN** for the items in §6.

**A second, unrelated handler table** holds the melee attack: the 81-record
creature **action** table at `0x006E4198` (`creature-action-table.md`).

## 1. What it is — CONFIRMED

**281 records of 25 (`0x19`) bytes based at `0x006DA8B0`**, running to
`0x006DC420` inclusive; the next global begins at `0x006DC424`, after three
bytes of alignment padding. **State id equals record index** for all 281, so ids
run `0`–`280` (`0x000`–`0x118`).

The table lives in the **uninitialised** `.data` block
(`0x006CCC00`–`0x007A6DCF`), so there is nothing to read in the file on disk. It
is filled at run time by a single straight-line run of `MOV` instructions
between **`0x0047BBE3` and `0x004805E4`**. No function contains that run, which
is why a caller search finds nothing, and why recovering it means walking the
disassembly tracking register values rather than reading bytes out of a section.

## 2. Record layout — CONFIRMED

| Offset | Size | Field | Status |
|---|---|---|---|
| `+0x00` | 4 | **update handler** — the per-tick function | CONFIRMED |
| `+0x04` | 4 | written and never read | CONFIRMED dead (Q184) |
| `+0x08` | 4 | written and never read | CONFIRMED dead (Q184) |
| `+0x0c` | 4 | written and never read | CONFIRMED dead (Q184) |
| `+0x10` | 4 | state id, equal to the record index — **also never read** | CONFIRMED dead (Q184) |
| `+0x14` | 2 | **`Text.str` string id** — the state's display label | CONFIRMED |
| `+0x16` | 1 | **group tag**, compared for equality by `SetState` | CONFIRMED |
| `+0x17` | 1 | **category**, index into the 32-entry table at `0x00699688` | CONFIRMED |
| `+0x18` | 1 | flag byte | partly decoded (Q185) |

**Four of those nine fields are dead** (Q184). A raw address scan of every slot
of record 0 settles it, because the stride of 25 compiles to
`[EAX + EAX*4 + base]` with `EAX` pre-multiplied by 5, so every indexed read in
the executable carries the base constant and is visible to a scan. `+0x04`,
`+0x08`, `+0x0c` and `+0x10` have **one store each and zero reads**, all four
inside the fill run: the three "unread function pointers" are not enter, leave
or message hooks and not part of one wide pointer, and the dispatcher takes the
state id from the **creature**, not from the record.

The frame is pinned from the code that *indexes* the table, never from the data
that fills it: the dispatcher `FUN_00476FC0` dereferences
`(**(code **)(&DAT_006da8b0 + *(int *)(param_1 + 0x10) * 0x19))()`, so record
`N` begins at `0x006DA8B0 + 25N` and the handler is at `+0`.

### 2.1 The two bytes are not the same kind of thing

Read line by line, `FUN_00476D30` (`SetState`) gives them different jobs.
**`+0x16` is a group tag**: `SetState` only ever compares it for *equality*
between the current state and the requested one, and nothing indexes anything
with it — which is why it can hold `50` and `100` alongside `0`–`13` without
being out of range. **`+0x17` is the category**, and it is the one used as an
index: `&DAT_00699688 + cat * 0x40`, with two sibling tables at `0x006996BC` and
`0x006996C0`. Its range across all 281 records is **`0`–`31`**, exactly the 32
entries of a `0x40`-byte-per-entry table, and **`+0x17 == 2` means "utility
state"** — the case that saves the caller's state and resumes through
`UtilityNewState` (probe **Q174**; the protocol is `travel-states.md`).

### 2.2 The transition rule, in the original's own order — CONFIRMED

Read from `FUN_00476D30` on 2026-09-10. The routine takes the requested state id
and reads the current one from the state object's `+0x10`. **Every test but the
last reads the *target*'s two bytes**, which is what the earlier summary here
did not say, and the order is load-bearing.

**When the group tags are equal**, three tests in this order:

| | Test | Result |
|---|---|---|
| 1 | target group is `3` **and** target category is `0x17` | **allowed**, whatever the current category |
| 2 | target category is `0` | **refused** |
| 3 | otherwise | allowed iff target category **equals** current category |

Test 1 before test 2 is why a group-3 creature can always die: group `3` with category `0x17` is
states **216, 217 and 218** — the two *dying* states and *frozen* — so a group-3
creature enters them from any category, including the category `0` that test 2
would otherwise refuse. Test 2 before test 3 is why two category-`0` states
never reach each other even though their categories are equal.

**When the group tags differ**, the current state is first replaced by the id at
the state object's `+0xDF` (`UtilityNewState`) **if the current category is `2`**
— so a creature standing in a movement state is judged by where the movement
is heading (the dumper's own name for `+0xDF` is `UtilityNewState`), not by the
movement. The substitution is inside the transition rule, which is
why a resume slot is a prerequisite for the rule and not only for resuming.

The move is then allowed if **any** of three conditions holds, over the category
records at `0x00699688` — allowed when the two categories are equal, when the
target's entry mask is zero, or when the mask names the category being left.
`+0x38` turned out to be the identity for all 32 records, so the routine's
double indirection through it collapses; **the records, the mask's direction and
that identity have one home, `state-categories.md`** (Q300).

**Coverage.** Both halves now resolve: **all 78,680** ordered pairs of distinct
states are decided, 37,791 allowed and 40,889 refused. The two state bytes alone
settled 20,628 of them; the category records decide the rest.

## 3. The labels — CONFIRMED

The `+0x14` word is a string id in the install's own `Text.str`, and it resolves
to the text the game shows for what a creature is doing — **the "current
activity" line on the flower above its head**. All 281 resolve and none is out
of range; **seventy distinct labels** cover the 281 states, the commonest being
`idle` at 49. States 21 and 22 are labelled `reinforcing walls`, which is what
the owner read off an imp's flower directly (Q171). Six records carry id `0`,
which is not a label — `Text.str` id 0 is an unrelated string — so those states
have no display text.

That is a fifth independent check on the frame and the count: a frame off by one
record, or a walk run past the table's end, would put arbitrary bytes in this
field, and they would not all land inside a 3,087-entry string table.

**A legal question, flagged rather than assumed.** The labels in §4 are short UI
strings from a shipped asset, present as *research documentation* — they are how
a state is identified — and a reimplementation must load them at run time from
the user's own install like every other asset, so nothing is redistributed by
the code. If reproducing them even in this form is judged too far, the
`Text.str` id column alone keeps the table useful.

## 4. The table

Two states per row, left then right, so that state `N` sits in row
`N / 2` — the left half when `N` is even, the right when it is odd.

| Id | Label | Handler | `Text.str` | Grp · Cat · Flag | Id | Label | Handler | `Text.str` | Grp · Cat · Flag |
|---|---|---|---|---|---|---|---|---|---|
| 0 `0x00` | idle | `0x00000000` | 2599 | 100 · 0 · 0 | 1 `0x01` | idle | `0x004773d0` | 2599 | 0 · 1 · 0 |
| 2 `0x02` | doing nothing | `0x00477660` | 2675 | 2 · 0 · 1 | 3 `0x03` | doing nothing | `0x00477680` | 2675 | 2 · 0 · 0 |
| 4 `0x04` | doing nothing | `0x00477680` | 2675 | 2 · 0 · 0 | 5 `0x05` | doing nothing | `0x004776b0` | 2675 | 2 · 0 · 1 |
| 6 `0x06` | en route | `0x00470c80` | 2670 | 9 · 2 · 1 | 7 `0x07` | en route | `0x00471460` | 2670 | 9 · 2 · 1 |
| 8 `0x08` | en route | `0x00470e30` | 2670 | 9 · 2 · 1 | 9 `0x09` | en route | `0x00471430` | 2670 | 9 · 2 · 1 |
| 10 `0x0a` | idle | `0x00463df0` | 2599 | 8 · 3 · 1 | 11 `0x0b` | idle | `0x00463db0` | 2599 | 8 · 3 · 1 |
| 12 `0x0c` | idle | `0x00464240` | 2599 | 8 · 3 · 1 | 13 `0x0d` | digging | `0x004654f0` | 2600 | 8 · 3 · 0 |
| 14 `0x0e` | digging | `0x004655d0` | 2600 | 8 · 3 · 0 | 15 `0x0f` | claiming territory | `0x00465bc0` | 2601 | 8 · 3 · 0 |
| 16 `0x10` | claiming territory | `0x00465cd0` | 2601 | 8 · 3 · 0 | 17 `0x11` | destroying rooms | `0x00466080` | 2602 | 8 · 3 · 0 |
| 18 `0x12` | destroying rooms | `0x00466180` | 2602 | 8 · 3 · 0 | 19 `0x13` | claiming territory | `0x00465e60` | 2601 | 8 · 3 · 0 |
| 20 `0x14` | claiming territory | `0x00465f20` | 2601 | 8 · 3 · 0 | 21 `0x15` | reinforcing walls | `0x004662e0` | 2603 | 8 · 3 · 0 |
| 22 `0x16` | reinforcing walls | `0x00466390` | 2603 | 8 · 3 · 0 | 23 `0x17` | repairing walls | `0x00466660` | 2604 | 8 · 3 · 0 |
| 24 `0x18` | repairing walls | `0x00466710` | 2604 | 8 · 3 · 0 | 25 `0x19` | mining | `0x00465790` | 2605 | 8 · 3 · 0 |
| 26 `0x1a` | mining | `0x00465870` | 2605 | 8 · 3 · 0 | 27 `0x1b` | collecting Gold | `0x00466850` | 2606 | 8 · 3 · 1 |
| 28 `0x1c` | taking Gold to the Treasury. | `0x00466a40` | 2786 | 8 · 3 · 1 | 29 `0x1d` | taking Gold to the Treasury. | `0x00466ac0` | 2786 | 8 · 3 · 1 |
| 30 `0x1e` | collecting an item | `0x00466bc0` | 2608 | 8 · 3 · 1 | 31 `0x1f` | archiving a Spell Book | `0x00466cb0` | 2609 | 8 · 3 · 1 |
| 32 `0x20` | collecting an item | `0x00466df0` | 2608 | 8 · 3 · 1 | 33 `0x21` | taking a Special to Library | `0x00466f30` | 2610 | 8 · 3 · 1 |
| 34 `0x22` | installing a Trap | `0x00467040` | 2611 | 8 · 3 · 1 | 35 `0x23` | installing a Trap | `0x004671b0` | 2611 | 8 · 3 · 1 |
| 36 `0x24` | installing a Door | `0x004672f0` | 2614 | 8 · 3 · 1 | 37 `0x25` | installing a Door | `0x00467460` | 2614 | 8 · 3 · 1 |
| 38 `0x26` | digging | `0x00467650` | 2600 | 8 · 3 · 1 | 39 `0x27` | digging | `0x00467790` | 2600 | 8 · 3 · 1 |
| 40 `0x28` | collecting corpses | `0x004678a0` | 2618 | 8 · 3 · 1 | 41 `0x29` | collecting corpses | `0x00467aa0` | 2618 | 8 · 3 · 1 |
| 42 `0x2a` | rescuing wounded creatures | `0x00467d00` | 2617 | 8 · 3 · 1 | 43 `0x2b` | taking prisoners | `0x00467dd0` | 2620 | 8 · 3 · 1 |
| 44 `0x2c` | taking prisoners | `0x00468010` | 2620 | 8 · 3 · 1 | 45 `0x2d` | dragging a casualty to the Lair | `0x00468420` | 2619 | 8 · 3 · 1 |
| 46 `0x2e` | taking prisoners | `0x004683f0` | 2620 | 8 · 3 · 1 | 47 `0x2f` | taking prisoners | `0x00468270` | 2620 | 8 · 3 · 1 |
| 48 `0x30` | taking prisoners | `0x00468330` | 2620 | 8 · 3 · 1 | 49 `0x31` | idle | `0x00468510` | 2599 | 8 · 3 · 1 |
| 50 `0x32` | idle | `0x004b4c20` | 2599 | 8 · 3 · 0 | 51 `0x33` | teleporting | `0x004653f0` | 2623 | 8 · 3 · 0 |
| 52 `0x34` | teleporting | `0x004654a0` | 2623 | 8 · 3 · 0 | 53 `0x35` | researching | `0x00475a20` | 2625 | 8 · 4 · 0 |
| 54 `0x36` | researching | `0x00475d20` | 2625 | 8 · 4 · 0 | 55 `0x37` | researching | `0x00484940` | 2625 | 8 · 4 · 1 |
| 56 `0x38` | researching | `0x00476060` | 2625 | 8 · 4 · 0 | 57 `0x39` | researching | `0x00475e70` | 2625 | 8 · 4 · 1 |
| 58 `0x3a` | researching | `0x00484940` | 2625 | 8 · 4 · 1 | 59 `0x3b` | researching | `0x004762d0` | 2625 | 8 · 4 · 1 |
| 60 `0x3c` | researching | `0x004761f0` | 2625 | 2 · 0 · 1 | 61 `0x3d` | researching | `0x00476290` | 2625 | 2 · 0 · 1 |
| 62 `0x3e` | researching | `0x00476320` | 2625 | 2 · 0 · 1 | 63 `0x3f` | manufacturing | `0x00470110` | 2624 | 8 · 5 · 0 |
| 64 `0x40` | manufacturing | `0x00470470` | 2624 | 8 · 5 · 0 | 65 `0x41` | manufacturing | `0x00470860` | 2624 | 8 · 5 · 0 |
| 66 `0x42` | manufacturing | `0x00470560` | 2624 | 8 · 5 · 1 | 67 `0x43` | manufacturing | `0x00470c00` | 2624 | 8 · 5 · 1 |
| 68 `0x44` | manufacturing | `0x00470b20` | 2624 | 2 · 0 · 1 | 69 `0x45` | manufacturing | `0x00470bc0` | 2624 | 2 · 0 · 1 |
| 70 `0x46` | manufacturing | `0x00470c10` | 2624 | 2 · 0 · 1 | 71 `0x47` | idle | `0x00459180` | 2599 | 3 · 0 · 1 |
| 72 `0x48` | idle | `0x00459320` | 2599 | 1 · 0 · 3 | 73 `0x49` | idle | `0x004593e0` | 2599 | 1 · 0 · 3 |
| 74 `0x4a` | idle | `0x00459460` | 2599 | 1 · 0 · 3 | 75 `0x4b` | idle | `0x0046ea60` | 2599 | 3 · 7 · 4 |
| 76 `0x4c` | idle | `0x0046eab0` | 2599 | 3 · 7 · 5 | 77 `0x4d` | idle | `0x0046f1f0` | 2599 | 3 · 7 · 0 |
| 78 `0x4e` | idle | `0x0046f360` | 2599 | 3 · 7 · 0 | 79 `0x4f` | looking for the Lair | `0x0046f4f0` | 2626 | 7 · 6 · 5 |
| 80 `0x50` | making a home | `0x0046f6b0` | 2627 | 7 · 6 · 5 | 81 `0x51` | going to bed | `0x0046f810` | 2671 | 7 · 6 · 5 |
| 82 `0x52` | sleeping | `0x0046f910` | 2672 | 7 · 9 · 5 | 83 `0x53` | sleeping | `0x0046fa60` | 2672 | 7 · 9 · 4 |
| 84 `0x54` | sleeping | `0x0046fe90` | 2672 | 7 · 9 · 5 | 85 `0x55` | sulking | `0x00482f80` | 2673 | 6 · 10 · 1 |
| 86 `0x56` | wandering | `0x00480750` | 2628 | 50 · 0 · 1 | 87 `0x57` | wandering | `0x004808e0` | 2628 | 50 · 0 · 1 |
| 88 `0x58` | waiting | `0x00480f50` | 2629 | 9 · 2 · 1 | 89 `0x59` | idle | `0x004778e0` | 2599 | 0 · 1 · 1 |
| 90 `0x5a` | idle | `0x00477850` | 2599 | 0 · 1 · 1 | 91 `0x5b` | eating | `0x004634b0` | 2630 | 7 · 8 · 1 |
| 92 `0x5c` | eating | `0x00463500` | 2630 | 7 · 8 · 1 | 93 `0x5d` | eating | `0x00463760` | 2630 | 7 · 8 · 0 |
| 94 `0x5e` | eating | `0x00463720` | 2630 | 7 · 8 · 0 | 95 `0x5f` | eating | `0x00458680` | 2630 | 7 · 8 · 0 |
| 96 `0x60` | eating | `0x00463be0` | 2630 | 3 · 7 · 1 | 97 `0x61` | guarding | `0x00462930` | 2661 | 8 · 11 · 1 |
| 98 `0x62` | guarding | `0x00462d00` | 2661 | 8 · 11 · 1 | 99 `0x63` | guarding | `0x00463140` | 2661 | 2 · 0 · 1 |
| 100 `0x64` | on patrol to a Guard Post | `0x004632a0` | 2662 | 8 · 11 · 1 | 101 `0x65` | praying | `0x00472930` | 2632 | 8 · 12 · 1 |
| 102 `0x66` | praying | `0x00472ab0` | 2632 | 8 · 12 · 1 | 103 `0x67` | praying | `0x00472ac0` | 2632 | 8 · 12 · 1 |
| 104 `0x68` | praying | `0x00472d30` | 2632 | 8 · 12 · 1 | 105 `0x69` | praying | `0x00472e90` | 2632 | 8 · 12 · 1 |
| 106 `0x6a` | praying | `0x00473010` | 2632 | 1 · 12 · 1 | 107 `0x6b` | praying | `0x004730e0` | 2632 | 2 · 0 · 1 |
| 108 `0x6c` | training | `0x00485310` | 2633 | 8 · 13 · 1 | 109 `0x6d` | training | `0x004859e0` | 2633 | 8 · 13 · 1 |
| 110 `0x6e` | training | `0x00485580` | 2633 | 8 · 13 · 1 | 111 `0x6f` | training | `0x004855b0` | 2633 | 8 · 13 · 1 |
| 112 `0x70` | training | `0x004857f0` | 2633 | 8 · 13 · 1 | 113 `0x71` | training | `0x00485a60` | 2633 | 2 · 0 · 1 |
| 114 `0x72` | being held captive | `0x00473130` | 2674 | 1 · 14 · 3 | 115 `0x73` | being held captive | `0x004731f0` | 2674 | 1 · 14 · 3 |
| 116 `0x74` | being held captive | `0x00473350` | 2674 | 1 · 14 · 3 | 117 `0x75` | being held captive | `0x00473550` | 2674 | 1 · 14 · 3 |
| 118 `0x76` | being held captive | `0x00473750` | 2674 | 1 · 14 · 3 | 119 `0x77` | being held captive | `0x00473730` | 2674 | 1 · 14 · 3 |
| 120 `0x78` | being held captive | `0x00470c00` | 2674 | 1 · 14 · 2 | 121 `0x79` | freeing comrades | `0x004739e0` | 2636 | 1 · 14 · 2 |
| 122 `0x7a` | freeing comrades | `0x00473c60` | 2636 | 1 · 0 · 3 | 123 `0x7b` | being held captive | `0x00473df0` | 2674 | 1 · 14 · 3 |
| 124 `0x7c` | being held captive | `0x00473e80` | 2674 | 1 · 14 · 3 | 125 `0x7d` | being held captive | `0x00473f20` | 2674 | 1 · 14 · 3 |
| 126 `0x7e` | being held captive | `0x00473920` | 2674 | 2 · 0 · 3 | 127 `0x7f` | torturing a captive | `0x00483b90` | 2634 | 8 · 0 · 0 |
| 128 `0x80` | torturing a captive | `0x00483e90` | 2634 | 8 · 0 · 0 | 129 `0x81` | being tortured | `0x00483ff0` | 2635 | 8 · 0 · 0 |
| 130 `0x82` | torturing a captive | `0x00484110` | 2634 | 1 · 15 · 2 | 131 `0x83` | being tortured | `0x00484590` | 2635 | 1 · 15 · 2 |
| 132 `0x84` | being tortured | `0x00484940` | 2635 | 1 · 15 · 2 | 133 `0x85` | torturing a captive | `0x00484c80` | 2634 | 1 · 15 · 2 |
| 134 `0x86` | torturing a captive | `0x00484950` | 2634 | 1 · 0 · 1 | 135 `0x87` | torturing a captive | `0x00484cc0` | 2634 | 100 · 0 · 0 |
| 136 `0x88` | torturing a captive | `0x00484ce0` | 2634 | 2 · 0 · 2 | 137 `0x89` | pit-fighting | `0x00457a00` | 2637 | 1 · 16 · 1 |
| 138 `0x8a` | pit-fighting | `0x00457a70` | 2637 | 1 · 16 · 1 | 139 `0x8b` | pit-fighting | `0x00457b40` | 2637 | 1 · 16 · 1 |
| 140 `0x8c` | pit-fighting | `0x00457cf0` | 2637 | 1 · 16 · 1 | 141 `0x8d` | pit-fighting | `0x00458270` | 2637 | 2 · 0 · 1 |
| 142 `0x8e` | cheering on a fight | `0x00457d60` | 2638 | 8 · 31 · 1 | 143 `0x8f` | cheering on a fight | `0x00457f40` | 2638 | 8 · 31 · 1 |
| 144 `0x90` | cheering on a fight | `0x004580d0` | 2638 | 8 · 31 · 1 | 145 `0x91` | drinking | `0x00456160` | 2639 | 8 · 17 · 25 |
| 146 `0x92` | drinking | `0x00456b70` | 2639 | 8 · 17 · 25 | 147 `0x93` | drinking | `0x00456d60` | 2639 | 8 · 17 · 25 |
| 148 `0x94` | gambling | `0x004567d0` | 2640 | 8 · 17 · 25 | 149 `0x95` | gambling | `0x00456980` | 2640 | 8 · 17 · 25 |
| 150 `0x96` | gambling | `0x00456450` | 2640 | 8 · 17 · 25 | 151 `0x97` | gambling | `0x00456490` | 2640 | 8 · 17 · 25 |
| 152 `0x98` | gambling | `0x00456ec0` | 2640 | 8 · 0 · 25 | 153 `0x99` | dancing | `0x00456e20` | 2641 | 8 · 17 · 92 |
| 154 `0x9a` | drinking | `0x00456fb0` | 2639 | 2 · 0 · 25 | 155 `0x9b` | gambling | `0x00457010` | 2640 | 1 · 0 · 29 |
| 156 `0x9c` | gambling | `0x004574a0` | 2640 | 1 · 0 · 29 | 157 `0x9d` | gambling | `0x00457560` | 2640 | 1 · 0 · 29 |
| 158 `0x9e` | gambling | `0x004576e0` | 2640 | 1 · 0 · 93 | 159 `0x9f` | gambling | `0x00463320` | 2640 | 8 · 18 · 28 |
| 160 `0xa0` | stealing booty | `0x00482700` | 2642 | 8 · 18 · 1 | 161 `0xa1` | stealing booty | `0x00482760` | 2642 | 8 · 18 · 1 |
| 162 `0xa2` | stealing booty | `0x00482880` | 2642 | 8 · 18 · 1 | 163 `0xa3` | stealing booty | `0x00482010` | 2642 | 8 · 18 · 1 |
| 164 `0xa4` | stealing booty | `0x00482190` | 2642 | 8 · 18 · 1 | 165 `0xa5` | stealing booty | `0x004824e0` | 2642 | 8 · 18 · 1 |
| 166 `0xa6` | stealing booty | `0x00482ad0` | 2642 | 8 · 18 · 1 | 167 `0xa7` | stealing booty | `0x00482b30` | 2642 | 8 · 18 · 1 |
| 168 `0xa8` | stealing booty | `0x00482c50` | 2642 | 8 · 18 · 1 | 169 `0xa9` | escaping with stolen booty | `0x00482db0` | 2643 | 8 · 18 · 1 |
| 170 `0xaa` | stealing booty | `0x00482080` | 2642 | 8 · 0 · 1 | 171 `0xab` | stealing booty | `0x004822f0` | 2642 | 8 · 0 · 1 |
| 172 `0xac` | escaping with stolen booty | `0x00482eb0` | 2643 | 8 · 0 · 1 | 173 `0xad` | idle | `0x00471eb0` | 2599 | 8 · 18 · 1 |
| 174 `0xae` | idle | `0x00472150` | 2599 | 8 · 18 · 1 | 175 `0xaf` | idle | `0x004721c0` | 2599 | 8 · 18 · 1 |
| 176 `0xb0` | idle | `0x00486cf0` | 2599 | 8 · 18 · 1 | 177 `0xb1` | attacking Dungeon Heart | `0x00462560` | 2645 | 8 · 18 · 1 |
| 178 `0xb2` | attacking Dungeon Heart | `0x00484940` | 2645 | 8 · 18 · 1 | 179 `0xb3` | attacking rooms | `0x00458370` | 2644 | 8 · 18 · 1 |
| 180 `0xb4` | attacking rooms | `0x00458430` | 2644 | 8 · 18 · 1 | 181 `0xb5` | attacking rooms | `0x00458510` | 2644 | 8 · 18 · 1 |
| 182 `0xb6` | attacking walls | `0x004589c0` | 2646 | 8 · 18 · 1 | 183 `0xb7` | attacking walls | `0x004586b0` | 2646 | 8 · 18 · 1 |
| 184 `0xb8` | attacking walls | `0x00458c70` | 2646 | 8 · 18 · 1 | 185 `0xb9` | attacking walls | `0x00458d10` | 2646 | 8 · 18 · 1 |
| 186 `0xba` | tunnelling | `0x00485c10` | 2647 | 8 · 18 · 1 | 187 `0xbb` | collecting wages | `0x00472390` | 2669 | 7 · 20 · 1 |
| 188 `0xbc` | collecting wages | `0x00472690` | 2669 | 7 · 20 · 1 | 189 `0xbd` | collecting wages | `0x00472740` | 2669 | 7 · 20 · 1 |
| 190 `0xbe` | heeding Call To Arms | `0x00455da0` | 2648 | 10 · 25 · 1 | 191 `0xbf` | heeding Call To Arms | `0x00455ea0` | 2648 | 10 · 25 · 1 |
| 192 `0xc0` | heeding Call To Arms | `0x00455f30` | 2648 | 10 · 25 · 1 | 193 `0xc1` | joining your Dungeon. | `0x00459c20` | 2788 | 1 · 7 · 70 |
| 194 `0xc2` | leaving Dungeon | `0x00459de0` | 2649 | 1 · 7 · 1 | 195 `0xc3` | leaving Dungeon | `0x0045a000` | 2649 | 1 · 7 · 1 |
| 196 `0xc4` | leaving Dungeon | `0x0045a0d0` | 2649 | 1 · 7 · 1 | 197 `0xc5` | leaving Dungeon | `0x00459d50` | 2649 | 1 · 7 · 0 |
| 198 `0xc6` | joining your Dungeon. | `0x0045a200` | 2788 | 1 · 7 · 0 | 199 `0xc7` | engaging in battle. | `0x0045eb20` | 2787 | 4 · 22 · 3 |
| 200 `0xc8` | engaging in battle. | `0x0045f030` | 2787 | 4 · 22 · 3 | 201 `0xc9` | fleeing | `0x00484940` | 2658 | 4 · 22 · 3 |
| 202 `0xca` | fleeing | `0x00461850` | 2658 | 4 · 22 · 3 | 203 `0xcb` | fleeing | `0x004602e0` | 2658 | 4 · 22 · 3 |
| 204 `0xcc` | disarming a Trap | `0x00458df0` | 2653 | 12 · 24 · 1 | 205 `0xcd` | disarming a Trap | `0x00458eb0` | 2653 | 12 · 24 · 1 |
| 206 `0xce` | disarming a Trap | `0x00458f60` | 2653 | 12 · 24 · 1 | 207 `0xcf` | stunned | `0x00480fb0` | 2597 | 3 · 0 · 0 |
| 208 `0xd0` | stunned | `0x00480ff0` | 2597 | 3 · 0 · 0 | 209 `0xd1` | stunned | `0x00481020` | 2597 | 3 · 7 · 64 |
| 210 `0xd2` | stunned | `0x00481520` | 2597 | 3 · 7 · 66 | 211 `0xd3` | celebrating | `0x00458680` | 2654 | 2 · 0 · 0 |
| 212 `0xd4` | idle | `0x004816c0` | 2599 | 2 · 0 · 1 | 213 `0xd5` | idle | `0x004816f0` | 2599 | 2 · 0 · 1 |
| 214 `0xd6` | idle | `0x00481750` | 2599 | 1 · 7 · 1 | 215 `0xd7` | idle | `0x004818e0` | 2599 | 2 · 0 · 1 |
| 216 `0xd8` | dying | `0x00459850` | 2598 | 3 · 23 · 0 | 217 `0xd9` | dying | `0x004599d0` | 2598 | 3 · 23 · 0 |
| 218 `0xda` | frozen | `0x00481950` | 2656 | 3 · 23 · 0 | 219 `0xdb` | idle | `0x00481970` | 2599 | 3 · 21 · 1 |
| 220 `0xdc` | fleeing | `0x00476390` | 2658 | 13 · 27 · 1 | 221 `0xdd` | fleeing | `0x00484940` | 2658 | 13 · 27 · 1 |
| 222 `0xde` | advancing | `0x00461f00` | 2651 | 8 · 18 · 1 | 223 `0xdf` | advancing | `0x00462130` | 2651 | 8 · 18 · 1 |
| 224 `0xe0` | waiting | `0x004620f0` | 2629 | 8 · 18 · 1 | 225 `0xe1` | exploring | `0x0045a3e0` | 2659 | 8 · 18 · 1 |
| 226 `0xe2` | idle | `0x004787f0` | 2599 | 8 · 18 · 1 | 227 `0xe3` | idle | `0x004b4c20` | 2599 | 8 · 18 · 1 |
| 228 `0xe4` | idle | `0x00473f80` | 2599 | 1 · 28 · 0 | 229 `0xe5` | idle | `0x00474100` | 2599 | 1 · 28 · 0 |
| 230 `0xe6` | idle | `0x004741d0` | 2599 | 1 · 28 · 0 | 231 `0xe7` | idle | `0x004743c0` | 2599 | 1 · 28 · 1 |
| 232 `0xe8` | idle | `0x00474430` | 2599 | 1 · 28 · 1 | 233 `0xe9` | idle | `0x004744f0` | 2599 | 1 · 28 · 0 |
| 234 `0xea` | idle | `0x00474f80` | 2599 | 1 · 28 · 1 | 235 `0xeb` | idle | `0x00474740` | 2599 | 1 · 28 · 5 |
| 236 `0xec` | idle | `0x004747e0` | 2599 | 1 · 28 · 5 | 237 `0xed` | idle | `0x00474850` | 2599 | 1 · 28 · 5 |
| 238 `0xee` | idle | `0x004748e0` | 2599 | 1 · 28 · 1 | 239 `0xef` | idle | `0x00474960` | 2599 | 1 · 28 · 1 |
| 240 `0xf0` | idle | `0x00474af0` | 2599 | 1 · 28 · 1 | 241 `0xf1` | idle | `0x004b4c20` | 2599 | 1 · 28 · 1 |
| 242 `0xf2` | idle | `0x00474cc0` | 2599 | 1 · 28 · 5 | 243 `0xf3` | burning in Lava | `0x004819e0` | 2660 | 1 · 7 · 1 |
| 244 `0xf4` | burning in Lava | `0x00481c90` | 2660 | 1 · 7 · 1 | 245 `0xf5` | engaging in battle. | `0x00481850` | 2787 | 2 · 0 · 1 |
| 246 `0xf6` | idle | `0x00461a10` | 2599 | 3 · 29 · 1 | 247 `0xf7` | incapacitated | `0x004831d0` | 2655 | 1 · 30 · 0 |
| 248 `0xf8` | incapacitated | `0x004833c0` | 2655 | 1 · 30 · 0 | 249 `0xf9` | incapacitated | `0x00483760` | 2655 | 1 · 30 · 0 |
| 250 `0xfa` | guarding | `0x00481cf0` | 2661 | 8 · 0 · 0 | 251 `0xfb` | responding to a threat | `0x00475660` | 2664 | 4 · 22 · 3 |
| 252 `0xfc` | responding to a threat | `0x00458680` | 2664 | 4 · 22 · 3 | 253 `0xfd` | reporting danger | `0x00484940` | 2665 | 11 · 26 · 3 |
| 254 `0xfe` | reporting danger | `0x004759a0` | 2665 | 11 · 26 · 3 | 255 `0xff` | stunned | `0x004810e0` | 2597 | 3 · 7 · 66 |
| 256 `0x100` | stunned | `0x004810d0` | 2597 | 3 · 7 · 66 | 257 `0x101` | stunned | `0x00481200` | 2597 | 3 · 7 · 2 |
| 258 `0x102` | idle | `0x00474de0` | 2599 | 2 · 0 · 1 | 259 `0x103` | celebrating | `0x00461130` | 2654 | 4 · 22 · 3 |
| 260 `0x104` | celebrating | `0x00458680` | 2654 | 4 · 22 · 2 | 261 `0x105` | hypnotised | `0x00461860` | 2666 | 8 · 29 · 1 |
| 262 `0x106` | idle | `0x00474ee0` | 2599 | 2 · 0 · 1 | 263 `0x107` | stunned | `0x004815a0` | 2597 | 3 · 7 · 66 |
| 264 `0x108` | stunned | `0x00458680` | 2597 | 3 · 7 · 66 | 265 `0x109` | fleeing | `0x004769d0` | 2658 | 13 · 27 · 3 |
| 266 `0x10a` | recuperating | `0x00461b50` | 2667 | 8 · 18 · 1 | 267 `0x10b` | destroying rooms | `0x00458620` | 2602 | 8 · 18 · 0 |
| 268 `0x10c` | destroying rooms | `0x00458680` | 2602 | 8 · 18 · 0 | 269 `0x10d` | idle | `0x004626a0` | 2599 | 3 · 7 · 68 |
| 270 `0x10e` | idle | `0x004b4c20` | 2599 | 3 · 7 · 0 | 271 `0x10f` | idle | `0x004750b0` | 2599 | 3 · 7 · 0 |
| 272 `0x110` | *(no label)* | `0x00481d50` | &mdash; | 3 · 7 · 68 | 273 `0x111` | *(no label)* | `0x0046e820` | &mdash; | 3 · 7 · 68 |
| 274 `0x112` | *(no label)* | `0x00483790` | &mdash; | 8 · 11 · 0 | 275 `0x113` | *(no label)* | `0x00483870` | &mdash; | 8 · 11 · 0 |
| 276 `0x114` | *(no label)* | `0x004839e0` | &mdash; | 8 · 11 · 0 | 277 `0x115` | *(no label)* | `0x00483ac0` | &mdash; | 8 · 11 · 0 |
| 278 `0x116` | joining your Dungeon. | `0x00462730` | 2788 | 1 · 7 · 70 | 279 `0x117` | joining your Dungeon. | `0x004627e0` | 2788 | 1 · 7 · 70 |
| 280 `0x118` | joining your Dungeon. | `0x00462840` | 2788 | 1 · 7 · 70 | | | | | |

State `0`'s fourth pointer is the one field of 2,529 the register walk does not
resolve. State `0` is the null state and its handler is `0x00000000`.

## 5. What settles the count and the frame

Two unrelated errors were stacked in the earlier reading, and both are traps.
**The count** came from a scan window `[0x006DA000, 0x006DC000)` that ended
`0x420` bytes before the table did, so the walk stopped at a boundary that had
nothing to do with the data and reported 239 records, "none unknown" — true and
worthless, because every record it *saw* resolved. **A count is not checked by
the thing it was counted with.** The same truncation was in the published code
range, recorded as `0x0047BBE9`–`0x0047FAEB`. **The frame** started at
`0x006DA8C0`, which is record 0's *id* field, so every scalar read correctly and
only the four function pointers came from the following record: a fixed-stride
table shifted by a whole field group is invisible. Flame's base address was
right and this project's was not, which is where the rule *a disagreement with
Flame is a reason to re-derive* comes from.

Six independent checks settle it now:

| | Check |
|---|---|
| 1 | **Offset histogram.** Every reference into the table's address range falls on exactly nine offsets modulo 25 — `+0`, `+4`, `+8`, `+0xc`, `+0x10`, `+0x14`, `+0x16`, `+0x17`, `+0x18` — with no strays, which recovers the stride and the frame without assuming either |
| 2 | **Exact accounting.** 2,529 writes into the range = **281 × 9**; every field of every record is written exactly once |
| 3 | **Contiguity.** Each of the nine fields spans records `0`–`280` with no gaps |
| 4 | **Self-consistency.** 277 records write their state id as a literal and every one equals its record index; the four that write it from a register are records 0, 1, 3 and 8, and the smallest literal anywhere in the table is 2 — exactly what is left when those four are removed |
| 5 | **The labels resolve.** All 281 `+0x14` values are valid `Text.str` ids (§3) |
| 6 | **Flame numbers 58 of the handlers and agrees on every one** (§5.4) |

### 5.4 The sixth check: Flame's own numbering, and it does not disagree once

The five checks above are internal to this reading; the sixth is not. Flame's
map does not merely list the handler addresses — for some of them its symbol
**encodes the state's display label together with its decimal state id**, the
same two facts recovered here separately from the `MOV` walk and from
`Text.str`. That makes the map an *independent statement of the numbering*, and
it can be checked row by row.

| | |
|---|---:|
| Distinct non-null handler addresses in §4 | 264 |
| …present as symbols in Flame's map | **264** |
| …whose symbol carries a decimal state id | 58 |
| …where that id **equals** our record index | **58** |
| …where it disagrees | **0** |

Fifty-eight of 281 is a fifth of the table, spread across imp work, the
Call-To-Arms states, the good-creature states, the keeper states and the leaving
states rather than clustered in one region, so it exercises the frame at many
points. §5 records that the previous frame was wrong and Flame's base address
right; this is the converse, run at the level of individual records.

**What it does not settle.** Flame is a reading of the binary like ours, not the
binary, so this raises confidence rather than adding evidence of a new kind; two
readings can share a mistake. It says nothing about `+0x04`–`+0x0c`, the flag
byte, or the categories, which stay open in §6. **Coverage:** of the 264
handlers, **160 carry an auto-generated placeholder name** in Flame and 38 carry
only the translation unit they were compiled from, so the map confirms the
numbering for a fifth of the table and names the *behaviour* of none of it.
Reading what a handler does is still a Ghidra job.

**Boundary.** Per ADR-0046 and the plan's §2.2.1, Flame is unlicensed: the
scheme is described here in our own words and **not one of its symbols is
reproduced**. Nothing was needed from it beyond the numbers above, because the
labels in §3 were resolved independently from the install's own `Text.str`.

## 6. What is still open

- **`+0x04`, `+0x08`, `+0x0c`** — answered as **dead** (Q184, §2); most are null
  and nothing reads any of them. What they were *for* is unknown.
- **`+0x18`, the flag byte.** Fifteen distinct values (`0`–`5`, `25`, `28`,
  `29`, `64`, `66`, `68`, `70`, `92`, `93`). **Six of the eight bits are used:
  bits 5 and 7 are set by no shipped state**, so their meaning cannot be
  recovered from this table at all. Populations, per bit: 0 → **195**, 1 → 43,
  2 → 25, 3 → 15, 4 → 15, 6 → 15.

  Bit 3 suppresses the hunger clock and bit 4 the tiredness clock; they never
  appear apart, and together they mark the **contiguous run 145–159** — the
  Casino's states. That run **spans three categories** (eight in 17, six in 0,
  one in 18), so category 17 does not delimit the room and a mechanic keyed on
  it alone would miss seven states. Bit 1 suppresses the need-anger routine and
  all eleven captive states carry it. Bits 0, 2 and 6 are UNKNOWN (Q185,
  `eating.md` §3, `payday.md` §6).

  **The 147 this section used to give for bit 0 was the count of states whose
  flag byte is exactly `1`**; six values carry the bit and 195 states have it.
  Bit 1's 43 was counted as a bit and is right.
- **The group tag's meaning.** Its role in `SetState` is known exactly (§2.1);
  what the groups *are* is not. Group **7** is the fourteen states that *are* a
  need, which the needs routine reads as "do not ask" (`eating.md` §3).
- **The 32 categories — DECODED, all of them** (Q186, ADR-0083,
  `training.md` §2): `+0x00` the category's own bit, `+0x04` enter hook,
  **`+0x14` update hook, called every tick by `FUN_00476FC0` immediately before
  the state's own handler**, `+0x24` leave hook, `+0x34` an allowed-category
  mask, `+0x38` its own index. Five are named by what their hooks do: **4
  research, 5 manufacture, 13 training, 14 prison, 22 combat**; nineteen have no
  hooks at all. That per-tick hook is where several mechanics this project had
  been looking for in *state* handlers actually live — the prison drain and the
  training award both. `+0x3c` is still unread.

## Implementation and tests

`CreatureStateTable` carries all 281 rows — the label's string id, the group tag,
the category and the flag byte. **§4 above is their source**: it carries all 281
rows, and `tools/gen-creature-state-tables.py` regenerates the code from it, so a
row is corrected here and never there. What may *not* cross is the Ghidra dump
those rows were read from (ADR-0046), which is why the note holds the values
rather than pointing at a file. `CreatureStateTableTests` re-asserts §5's checks,
so a drifted row fails. Handler addresses stay in §4; label *strings* stay out
per §3, only their ids.

`CreatureStateTransitions` implements §2.2's equal-tags half and reports the
other half as `NeedsCategoryTable`. It hashes nothing and holds no state.

This project still has **30 activities** (`CreatureActivity`) where DK2 has 281
states, each of ours composing several of the original's. ADR-0135 makes the
state id authoritative and retires the enum state by state rather than at once.
The table is also the map a mechanic is read against: `eating.md`, `prison.md`,
`training.md`, `payday.md`, `lair-acquisition.md` and `creature-anger.md` all
index it, and their state ids are tested there.
