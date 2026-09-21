# Mechanic: which enemy a creature picks — `FightStyle` and the five selectors

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-21. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046)
- `Creatures.kwd`, `KeeperSpells.kwd` and `Text.str` from the reference install

Original version: DK2 v1.70 (GOG)

Questions: advances Q8. `combat-entry.md` owns the chain this feeds — the state,
`spFighting`, the AttackType switch and the engagement record — and is where the
acquisition gate that calls these routines is read.

---

## 1. `FightStyle` picks the selector — the field's consumer, found

Inside the acquirer, immediately before the call, sits a five-arm jump table:

```
EDX = creature[0x370]          ; the compact definition
AL  = byte[EDX + 0x67E]        ; 0..4, else the default arm
JMP [EAX*4 + 0x45E174]
```

`def+0x67E` is loaded from **file offset `0x0F03`**, by a guarded store at
`0x0049347A` taking `EBP+0x12F3` — the `0x3F0` frame delta again. That offset is
**`FightStyle`**, which this project already parses and *nothing reads*
(`KwdCreatures`, `manual-1999.md` § the editor's enum). **So `FightStyle` selects
which routine chooses a creature's enemy.** It also fills the slot `training.md`
§4 observed the experience run skipping.

**The table is not in index order**, which is worth stating because assuming it
was inverted the reading once:

| `FightStyle` | routine | size | the shipped creatures |
|---|---|---|---|
| 0 `NonFighter` | **`0x0045BDF0`** | 0x7DC | Imp, Jack |
| 1 `Blitzer` | `0x0045B800` | 0x2C5 | Mistress, Vampire, Dark Angel, Horny, King Reginald, + 5 uniques |
| 2 `Blocker` | `0x0045B140` | 0x2D7 | Bile Demon, Troll, Giant, Knight, Black Knight, Guard, Royal Guard, Stone Knight, Lord Of The Land, three princes, Maiden Of The Nest, + 2 uniques |
| 3 `Flanker` | `0x0045BAD0` | 0x315 | Goblin, Skeleton, Salamander, Firefly, Dwarf, Thief, Monk, Fairy, Rogue, + 4 uniques |
| 4 `Support` | `0x0045B500` | 0x300 | Warlock, Dark Elf, Wizard, Elven Archer, + 2 uniques |

**DK2's own name for the field is "fight type".** The default arm asserts
*"Creature type %d, tag %d, fight type %d unrecognised."*, so `def+0x67E` is
named by the binary itself and `FightStyle` is only the editor's spelling.

All five take `(creature, int32)` and return a **word** — a tag. The rosters are the whole
shipped file, 46 records read at `0x0F03`, and they corroborate DK2's own names
without any of the four unread arms being opened: `Support` is exactly the four
ranged archetypes and their uniques, `Blocker` exactly the heavy melee, and
`NonFighter` exactly the two creatures that do not fight.

**That the biggest of the five is `NonFighter` is the finding**, not a curiosity.
Deciding *not* to fight is the complicated case: §3's distance to one's own
dungeon heart, its two calls into a withdraw-from-fight state, its
engage-only-what-engages-me gate and its heavy rollback are all a worker's
reluctance, and none of it generalises to how a Knight picks a fight.

**Before the dispatch, a re-acquisition path.** `word[creature + 0x482]` is
resolved through the liveness and thing tables; if it is live, `word[thing +
0x4D0]` is read and, if *that* is live, it becomes the answer and **no strategy
runs at all**. This is what `spFighting`'s *"failed to re-aquire previous
target"* assertion is about.

**The strategy byte is on the definition, not the creature**, which is settled
rather than assumed: `CCreature` is `0x53F` bytes, so `+0x67E` cannot be on it.
Its only three writers are that definition's **constructor**, a
definition-to-definition **copy**, and `FUN_00492320` (`combat-cadence.md` §5
asks separately whether that one is live). **No file-load site is among them**,
so whether the strategy id is shipped per creature or computed is UNKNOWN — and
it cannot be answered by subtracting `0x3F0` from `0x67E`, because that constant
maps a file offset to the loader's **staging** struct, not to the runtime one
(`library-and-research.md` has the worked pair).

## 2. The five share one shape: a scan with a score

Every arm is the same function differing only in the rule, and this is worth
stating once rather than per arm:

- **`thiscall (CCreature* me, int32 bestSoFar) -> int16 tag`.** The argument is
  an incoming **best score** and the return the winning candidate's tag, 0 for
  none. The acquirer runs a scan of its own first and hands the running best
  down, so an arm **continues one competition** rather than starting one. What
  that best is initialised to, before either scan, is not yet read.
- **two nested walks.** An outer list from `dword[player + 0x10FD]` linked by
  `word[+0x3A]`; for each node an inner list from its `word[+0x6]` linked by
  `word[+0x10]`. Every candidate is visited — nothing exits early on a hit.
- **two ranges** out of the definition, `def+0x77B` and `def+0x77F`, each squared
  and shifted left 12; and the distance as `dx² + dy²` with each product shifted
  **right** 12. Both land in 1/4096 units, so the comparison is exact.
- a **Turncoat indirection** on the creature: if `+0x453` is non-zero take
  `+0x455` as its side, else its own player. Flame's map calls that pair a
  turncoat timer and player, and `KeeperSpells.kwd` record **7 is Turncoat**, so
  a defected creature is read as belonging to the keeper it defected to.
- **lower score wins**: `if (score >= best) skip`.

Two things are *not* shared, and an earlier revision of this document asserted
both as if they were. **Only `NonFighter` writes `creature+0x502`** — the other
four are pure and leave the target field to the acquirer. And **only
`NonFighter`'s return is tested against `0xEA60`**, at `0x0045D665`, where that
value makes the acquirer return success immediately.

## 3. NonFighter, the arm the Imp and Jack run

§1 shows `0x0045BDF0` is the **NonFighter** arm, which only the **Imp** and
**Jack** ever run, and everything in it reads as that once you know. It is nearly
three times the size of any other arm, and alone among the five it writes the
target field.

**It opens by measuring the way home.** Before anything else it takes the
creature's own player (`creature+0x2A`), asks it for the dungeon heart's
position, converts that tile to 1/4096 units with a half-tile centre offset
(`<<12`, `+0x800`), and squares the absolute differences against the creature's
own position at `+0x1A`/`+0x1E`. Flame's map calls that accessor
`getDungeonHeartPosition`; the instructions say the same independently, which is
the only reason it is repeated here (ADR-0080).

**That distance is a threshold, and §3.1 is what it is for.**

### 3.1 Near its own heart, a worker stops checking whose side you are on

**Owner, 2026-09-21, unprompted:** *imps do fight when the dungeon heart is under
attack — they attack the creatures attacking it.* That observation is what sent
this read back into the routine, and it lands on the one computation the section above had
flagged as unexplained.

```
EDX = dx*dx + dy*dy            ; to the creature's OWN heart, 12.12 fixed
CMP EDX,0xD000                 ; 53248 / 4096 = 13 tiles squared
JNC  skip                      ; farther -> leave the flag clear
MOV dword[EBP-0x18],1          ; nearer -> set it
```

so the radius is **sqrt(13), about 3.6 tiles**. The flag starts clear and is read
**exactly once**:

```
CMP AL,1 / JZ  0x0045C1AB      ; one condition takes a different path
MOV EAX,[EBP-0x18]
TEST EAX,EAX / JNZ 0x0045C1AB  ; being near my own heart takes the SAME path
MOV AX,word[ESI + 0x28]        ; the path it skips begins with the candidate's OWNER
```

`word[thing+0x28]` is the owner word (`navigation-captures.md`). **So a worker
within ~3.6 tiles of its own dungeon heart is routed past the test that starts by
reading whose side the candidate is on.** What the alternative path at
`0x0045C1AB` then decides is unread, so the *mechanism's location and trigger*
are established here and its outcome is not — but it is where the owner's
behaviour lives, and nothing else in the routine could produce it.

**And the Imp is special-cased by id.** The engagement range comes from
`def+0x77B`, squared and shifted into 1/4096 — except when `byte[creature+0x36F]`,
the creature type, **is 1**, which `Creatures.kwd` says is the **Imp**. Then the
range is forced to `0x1000`, exactly **one tile squared**. So the two creatures
sharing `NonFighter` do not share a reach.

**Where the candidate comes from.** `ESI` is walked out of a list: the creature's
player at `+0x2A`, then `dword[player + 0x10FD]` to a head, a tag at its `+0`
resolved through the thing table, then `word[thing + 0x6]` to the candidate —
the outer walk of §2.

**The acceptance gate is mutual.** A candidate passes only if

```
word[cand + 0x502] == 0        ; it has no target of its own
  or word[cand + 0x502] == our own tag   ; or its target is us
and byte[cand + 0x3EA] & 0x40  ; and it carries this flag
```

so on **this** path it engages only somebody already coming for it. The
near-the-heart path above does not pass through here, which is the difference
between a worker minding itself and a worker defending the heart.

**The commit is tentative, and this is what the twelve stores are.** On
acceptance the candidate's tag goes into `creature+0x502` and **the previous
target is stashed** in a local. A predicate then runs (`0x00460b50`), and a map
query with `0x5000` — five tiles in 1/4096 units — follows. If a check fails the
stashed tag is **written back**. So:

| stores | what they are |
|---|---|
| 1 | take the new candidate |
| 5 | **restore** the previous target after a failed check |
| 6 | **clear** the target, some also zeroing two word arrays at fight `+0x6` and `+0x16` |

**Twice corrected, and still under review.** One revision of this document said
the selector "writes the target on twelve separate branches", reading as twelve
ways of choosing an enemy; the next said one store takes and eleven undo. The
counts above were made while the routine was believed to judge a single
candidate. It loops (§2), and three of the twelve stores are inside the loop
while nine are in a tail past its exit, so the split needs re-reading.

## 4. Blocker: hostility, one exclusion, then distance

`0x0045B140` is the arm for `FightStyle` 2, the fifteen heavy melee creatures. It
is a third the size of NonFighter and makes **three calls** where NonFighter
makes eleven. **It computes no distance to any dungeon heart and contains no
`0xD000`**, which settles §3.1's threshold as the worker's own rule rather than a
general one.

Per candidate, in order:

1. **Whose side.** The candidate's side is its Turncoat player if its turncoat
   timer is running, otherwise `word[its player + 0x4]`.
2. **The hostility predicate.** `0x004BF390(mySide, theirSide)` — `thiscall
   (CPlayer*, uint16)`. Zero rejects the candidate.
3. **One exclusion**, and every term of it resolves in this project's own data:

   ```
   if  cstate.currentStateId == 6                    ; "en route"
   and cstate.utilityNewState in { 251, 202 }        ; "responding to a threat", "fleeing"
   and word[me + 0x53B] != 0                         ; my fight block already holds a retreating creature
       -> skip this candidate
   ```

   `+0x53B` is `0x3D` into the `+0x4FE` fight block, which corroborates that
   block's base a third time. So a Blocker will not start on something that is
   already running — toward a threat or away from one — while it is still
   tracking someone retreating.
4. **Distance** between the two positions at `+0x1A`/`+0x1E`, against the two
   ranges of §2.

## 5. Support: shoot what the front line is already fighting

`0x0045B500` is `FightStyle` 4, the four ranged archetypes. It is the arm where
the two definition ranges finally do different work. Per candidate, in order:

1. **Whose side**, then the hostility predicate `0x004BF390`, then **the same
   three-term exclusion as Blocker** — same fields, same constants. Two arms
   agreeing exactly makes that a shared rule, not a quirk of one.
2. **Distance** `d = dx² + dy²`, in the units of §2.
3. **A weighting by the candidate's level**, applied only when
   `dword[cand+0x3E6]` bit 12 is set: `d = d * pct / 100`, where `pct` is the
   candidate's `def+0x760` for a level-1 creature and otherwise that value scaled
   by a word from a table indexed by its **experience level**
   (`byte[cand+0x403]`, `combat.md` §4). So level can make an enemy read as
   nearer or farther than it is. Bit 12 and `def+0x760` are both UNKNOWN.
4. **An alignment discount.** `def+0x686` is the creature flag dword
   (`job-selection.md` §6.2), bit 7 `IsEvil` and bit 8 `IsGood`:

   ```
   if (my flags & IsGood) read the candidate's IsEvil
   else                   read the candidate's IsGood
   if set: d = d * 7 / 8
   ```

   so **a target of the opposite alignment is preferred by 12.5%** — a tie-break,
   not a gate. The acquirer's own pre-dispatch scan applies the identical
   discount, which is what makes the two scans one competition.
5. **The score, and this is the arm's own rule.** `0x0045B420` counts, over the
   **candidate's** two 8-word arrays at `+0x504` and `+0x514` — the `+0x4FE`
   fight block's `+0x6` and `+0x16`, its base corroborated a fourth time — how
   many entries are creatures whose own fight type is 1 `Blitzer` or 2 `Blocker`.
   There is no side test, so it counts **the melee creatures already engaged with
   this candidate**. Then

   ```
   n > 0 : score = n * range1² + d
   n = 0 : score = 9 * range1² + d
   ```

   Lower wins, so the ordering is lexicographic: **among enemies the melee are
   already on, prefer the least crowded; an enemy nobody is fighting sorts behind
   every enemy that is**, as though nine were on it. A ranged creature neither
   piles onto a swarm nor pulls a fresh enemy.
6. **The gates, reached only by a candidate that beat the best.**
   `byte[cand+0x3EA] & 0x40` must be set — the bit NonFighter also gates on. Then
   `word[cand+0x4A3]` must not equal my player id: `+0x4A3` is **`SneakingAs`**
   (`hand-of-evil.md`, `prison.md`), so **a creature disguised as my keeper is
   not shot at**, the disguise working on target selection and not only on the
   hand. Then **either** within `def+0x77B` and `0x004512F0([0x6E5050]+0x9DA,
   myPos, itsPos, 0, 1)` non-zero, **or** within `def+0x77F` and `0x0045AFF0` —
   the routine all five arms call — non-zero. For creature type 1, the **Imp**,
   the first is forced to one tile and the second to zero.
7. Accepting stores the candidate's tag (`word[cand+0x4]`) and its score.

**The two ranges are `DistanceCanSee` and `DistanceCanHear`**, file `0xEC8` and
`0xECC`, which this project already parses and `navigation-and-steering.md` §2
already names for `+0x77B`. The loader rounds each to whole tiles
(`(value + 0x800) >> 12`), which is why the selector squares and shifts back. So
they are **not a min/max pair**: sight has one test and hearing another, and
passing *either* accepts — a creature engages what it can see, or what it can
hear.

**On shipped data the distinction is invisible.** All 46 creatures ship both
ranges at `16384`, exactly **4 tiles** (`dk2tools kwd creatures -s`). Only the
Imp ever differs, and only at runtime, where the code forces sight to 1 and
hearing to 0. A reimplementation that models one range will match the original on
every shipped creature and diverge only on modified data — so the two paths must
still be kept apart, because their *tests* differ even where their radii do not.

**That uniformity makes the §5.5 ordering exact.** With sight at 4 tiles,
`range1²` is `16 << 12`, and acceptance requires `d < range1²`; so a tier-`n`
score can never reach tier `n+1`, and the count of melee attackers strictly
outranks distance. The level weighting of step 3 is the one thing that could
break that, by inflating `d` past the radius before the comparison.

`0x0045B420` has a twin at `0x0045B490` counting fight type **4** in the same two
arrays, called only by `Flanker`, which calls both.

**A cross-doc UNKNOWN closes here.** `navigation-and-steering.md` §2 records a
steering repulsion from a "remembered entity" at `entity+0x537`, with a countdown
at `+0x539`, written by `0x0045CB00`, and asked which entity that is
(hypothesis: what it flees or fights). `0x0045CB00` is the target acquirer
(`combat-entry.md` §7), and `+0x537`/`+0x539` are `+0x39`/`+0x3B` into the
`+0x4FE` fight block, two words from Blocker's `+0x53B`. So **the entity the
steering keeps its distance from is the one acquisition picked to fight** —
which is why the reach it uses is that entity's `DistanceCanSee`.

## 6. What is left

`Blitzer` (`0x0045B800`) and `Flanker` (`0x0045BAD0`), neither read. Within
NonFighter, what the near-the-heart path decides and the split of its twelve
stores. The score the acquirer starts the competition from. `0x0045AFF0`, the hearing
test common to all five, and `0x004512F0`, the sight test — line of sight is the
obvious reading of the latter and is not yet evidence. And in Support, bit 12 of `+0x3E6` and
`def+0x760`.

## Confidence

**CONFIRMED (binary):** the five-arm dispatch, its jump table read entry by
entry, and all five targets; the assertion naming the field *fight type*; that
the five share a signature returning a tag; the loader pair `EBP+0x12F3` →
`def+0x67E` and so the file offset `0x0F03`; the shared shape of §2, including
both nested walks and that lower scores win, read in Support and Blocker and
corroborated by NonFighter's loop and epilogue; that only NonFighter writes
`+0x502` and only its return meets `0xEA60`; NonFighter's gate, its `0xD000`
threshold and the single read of that flag; the Imp's forced one-tile range and
that creature type 1 is the Imp; Blocker's order of tests and the three terms of
its exclusion; every step of Support §5, including what `0x0045B420` counts and
that `0x0045B490` is its fight-type-4 twin; the loader pair
`EBP+0x12B8`/`EBP+0x12BC` → `def+0x77B`/`def+0x77F` and so the file offsets
`0xEC8`/`0xECC`, with the round-to-tiles conversion.

**CONFIRMED (data):** `DistanceCanSee` and `DistanceCanHear` are `16384` on all
46 shipped creatures.

**CONFIRMED (data):** the `FightStyle` of all 46 shipped creatures, read from
`Creatures.kwd` at `0x0F03`; the state labels 6, 202 and 251 through `Text.str`.

**STRONGLY INFERRED:** that `0x0045BDF0` is the non-combatant rule rather than a
general one — it is what `FightStyle` 0 dispatches to and its two creatures are
the Imp and Jack. Blocker and Support now support that from the other side by
lacking its heart test, but two arms remain unread.

**UNKNOWN:** the two unread selectors, what the near-the-heart path decides,
`+0x3EA` bit 6, `+0x3E6` bit 12, `def+0x760`, and the score the competition
starts from.
