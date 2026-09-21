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

All five take `(creature, int32)` and return a **word** — a tag — and the return
is compared against the sentinel **`0xEA60`** (60000). The rosters are the whole
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

## 2. The five share one template

Every arm read so far opens the same way, which is worth stating once rather than
per arm:

- two ranges out of the definition, `def+0x77B` and `def+0x77F`, each **squared
  and shifted left 12** into 1/4096 units;
- a **Turncoat indirection** on the creature: if `+0x453` is non-zero take
  `+0x455` as its side, else its own player. Flame's map calls that pair a
  turncoat timer and player, and `KeeperSpells.kwd` record **7 is Turncoat**, so
  a defected creature is read as belonging to the keeper it defected to;
- a candidate walked from **`dword[player + 0x10FD]`**, a tag at its `+0`
  resolved through the thing table, then `word[thing + 0x6]` to the candidate.

They differ in the rule applied to each candidate.

## 3. The NonFighter selector judges one candidate, and rolls back

`0x0045BDF0` is `thiscall (CCreature*, int32) -> int16` — a **tag** out. §1 shows it is the **NonFighter** arm, which only the **Imp** and **Jack** ever run, and
everything in it reads as that once you know: it is not a search over every
enemy but a judgement of **one candidate creature**, held in `ESI`, which it
either commits to or puts back.

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
resolved through the thing table, then `word[thing + 0x6]` to the candidate. That
answers §2's open "where does the candidate come from" for this arm.

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

**A correction to this document.** Its previous revision said the selector
"writes the target on twelve separate branches", which read as twelve ways of
choosing an enemy. Eleven of the twelve are rollback and teardown; exactly one
takes a target.

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

## 5. What is left

`Blitzer` (`0x0045B800`), `Flanker` (`0x0045BAD0`) and `Support`
(`0x0045B500`), none read. Within NonFighter, what the near-the-heart path
decides. And the two predicates the NonFighter commit is tried against,
`0x00460b50` and `0x004512F0` with its five-tile argument.

## Confidence

**CONFIRMED (binary):** the five-arm dispatch, its jump table read entry by
entry, and all five targets; that the five share a signature returning a tag; the
loader pair `EBP+0x12F3` → `def+0x67E` and so the file offset `0x0F03`; the
shared template of §2; NonFighter's gate, its tentative commit and rollback, its
`0xD000` threshold and the single read of that flag; the Imp's forced one-tile
range and that creature type 1 is the Imp; Blocker's order of tests and the three
terms of its exclusion.

**CONFIRMED (data):** the `FightStyle` of all 46 shipped creatures, read from
`Creatures.kwd` at `0x0F03`; the state labels 6, 202 and 251 through `Text.str`.

**STRONGLY INFERRED:** that `0x0045BDF0` is the non-combatant rule rather than a
general one — it is what `FightStyle` 0 dispatches to and its two creatures are
the Imp and Jack. Blocker now supports that from the other side by lacking its
heart test, but three arms remain unread.

**UNKNOWN:** the three unread selectors, what the near-the-heart path decides,
and `+0x3EA` bit 6.
