# External resources, and what we may legally take from each

Written 2026-09-03, after the owner asked for OpenKeeper to be used as much as
possible. It is an invaluable resource and it is also **GPLv3**, so "as much as
possible" has a precise boundary that this file records once so it does not have
to be re-argued.

**Revised 2026-09-04**: this project moved from MIT to GPLv3 (ADR-0041), which
removed the *legal* half of that boundary and left the *methodological* half
standing. The section below separates the two, because they no longer say the
same thing.

---

## The licence boundary — read this before opening any of it

**This project is GNU GPL version 3 or later** (owner decision 2026-09-04,
ADR-0041; it was MIT until then, ADR-0003). Every source file carries
`SPDX-License-Identifier: GPL-3.0-or-later` and a test enforces it.

**Read the next two paragraphs together, because the interesting part of this
file is that they now disagree.**

| source | licence | what the *licence* permits | what this project *does* |
|---|---|---|---|
| **OpenKeeper** | **GPLv3** | code may be combined with a GPLv3 work, with attribution and notices retained | **facts only, with provenance — still no code.** A project choice now, not a legal limit. See below |
| Flame | unlicensed (re-checked 2026-09-06 against both the GitHub API and the clone: no `LICENSE`, `COPYING` or equivalent anywhere in the tree) | **nothing** | facts only, with provenance — **and never one of its identifiers**, see below |
| GLKeeper | MIT | inclusion in a GPLv3 work is fine (MIT is one-way compatible) | adapted once, for the WAD decompressor, attributed in `THIRD-PARTY-NOTICES.md` |
| YamlDotNet | MIT | same | referenced as a package, attributed |
| KeeperKlan / editor manual / format write-ups | third-party documentation | facts freely, cite the source | same |
| the shipped DK2 manual | EA copyright | nothing may be redistributed | quoted in short excerpts for research (`manual-1999.md`); the PDF stays in gitignored `original-reference/` |
| the DK2 install itself | proprietary | **nothing may be redistributed** | no executable, model, texture, audio, video or map file is ever committed |

### The licence bar on OpenKeeper is gone. The project bar is not.

Until 2026-09-04 the rule "no OpenKeeper code" had a legal reason: GPLv3 is
copyleft, so copying its Java into an MIT project was the one outcome the
licence choice ruled out. **That reason no longer exists** — this project is
GPLv3, and GPLv3 code may be combined with it.

Three things still forbid it, and none of them was ever a licence consequence:

1. **The brief.** It asks for *clean-room behavioural reimplementation based on
   observed behaviour and documented research rather than copying proprietary
   implementation wholesale*. Reading somebody else's implementation makes a
   clean-room claim harder to make honestly, whatever the licence says.
2. **The owner's standing instruction**, given when OpenKeeper was first cloned:
   *"if someone has already done the research and it is solid, not refutable, why
   wander around? I need you to just reference it and compare with our own
   findings, not copy it."* Reference and compare — not adopt.
3. **It has been working.** Under the facts-only rule OpenKeeper has confirmed
   three scan-derived offsets, **every** record type's on-disk size (eight of
   them new, each correct to the byte on the first attempt), the MAP flags byte
   from an independent direction, and one place where its own layout is
   incomplete (Q70). Worked examples 2, 3 and 5 below are the record. A rule
   that has produced that much corroboration is not obviously worth relaxing.

**Worked example 5 is the template to keep**, and it is the sharpest case yet of
why "reference, don't copy" is a *good* rule rather than a merely lawful one. On
Q74 their source supplied a mechanism we had not considered
(`ALTER_TERRAIN_TYPE`) and a byte layout that would have cost an afternoon of
scanning; sweeping the shipped maps supplied the **answer**, which no amount of
reading their code could have given, because their code does not know what the
shipped maps contain either. Theirs said *how a tile could be closed*; ours said
*whether any tile is*.

**If the owner wants to change this, it takes its own ADR** — plus an entry in
`THIRD-PARTY-NOTICES.md` and retained upstream copyright notices, because GPLv3
attribution is an obligation and not a courtesy. Not a quiet copy. The one place
worth even asking about is the file-format readers, which are the most complete
in existence — and which are also the part already reproduced here independently
and cross-validated to the byte. Their **gameplay** code is the part where the
clean-room argument would cost something, and it is also where their own authors
say the least is settled: OpenKeeper's creature AI has been an acknowledged stub
for years, which is the same wall Q7 describes here.

### The order of preference, unchanged

1. ask the owner, or observe the original;
2. read the KWD data ourselves;
3. third-party **format documentation** (010 Editor templates, KeeperKlan);
4. **OpenKeeper as documentation** — its wiki, its issue tracker, and its
   file-format *facts*;
5. IDA/Ghidra on the executable.

### What the relicence *did* change here

- **Flame did not move.** Unlicensed is unlicensed; a copyleft licence on our
  side grants nothing over somebody else's work.
- **The MIT material already here is fine.** MIT is one-way compatible with
  GPLv3, so the GLKeeper WAD adaptation and YamlDotNet keep their attribution
  and need no action (`THIRD-PARTY-NOTICES.md` says so).
- **Provenance matters more, not less.** Under GPLv3 a borrowed *implementation*
  carries attribution and notice-retention obligations, so the line between "we
  learned a fact from them" and "we took code from them" has to stay visible in
  the record. Every research note already states its source; keep doing that.

**If a specific case looks borderline, flag it to the owner rather than
deciding.** That instruction is in the brief and this file does not override it.

---

## The Resources list from OpenKeeper's README, checked 2026-09-03

Status is what a fetch from here actually returned.

| resource | status | worth what, and when |
|---|---|---|
| [Reversal of DKII Binary File Formats](http://keeperklan.com/threads/4623-Reversal-of-DKII-Binary-File-Formats) | **403 direct; readable via web.archive.org** | **Already paid for itself** — see below. Format facts from somebody who spent 18 months in the executable |
| [DK2 texture formats](http://keeperklan.com/threads/220-DK2-texture-format) | 403 direct; try archive | Art pipeline. Matters when the Unity view needs to read original textures — and note nothing original may be redistributed |
| [Jadex AgentKeeper](https://code.google.com/p/jadex-agentkeeper/) | **dead** (Google Code is shut down) | — |
| [werkt/kwd](https://github.com/werkt/kwd) | live | A KWD/KLD loading library, and **code** — so facts only, and check its licence before reading closely. Written by the author of the format thread above |
| [EA sound & video formats](http://wiki.multimedia.cx/index.php?title=Electronic_Arts_Formats) | live | Audio/video containers. M7+, and presentation rather than simulation |
| [Simon Schreibt on DK2's walls](http://simonschreibt.de/gat/dungeon-keeper-2-walls/) | **live, read 2026-09-04** | **Read, and it paid off twice** — see below. Rendering only, and it says nothing about simulation, which was the right reason to defer it and the wrong reason to have kept deferring it. Write-up: `wall-rendering.md` |
| [DK2 official editor manual](http://keeper.lubiki.pl/dk2_docs/dk2_editor_manual.htm) | **live, ~145KB** | **The one to read properly, and not yet.** See below |

Also on the README, outside Resources: the
[OpenKeeper wiki](https://github.com/tonihele/OpenKeeper/wiki/How-to-set-up-OpenKeeper),
a [KeeperKlan OpenKeeper forum](https://keeperklan.com/forums/101-OpenKeeper)
(403 from here) and a [Discord](https://discord.gg/e2Dnqkn).

---

## What the format thread already gave us

**It corroborated ADR-0031 from a completely independent direction**, which is
the best kind of confirmation and worth recording as the template for how these
sources should be used.

The thread documents the on-disk MAP element as:

```
struct Element {
  BYTE idTerrain;
  BYTE idPlayer;
  BYTE flags;   /* only a '2' bit here is interpreted to do anything special at
                   load, 1 may indicate 'valid', but it is not interpreted as such */
  BYTE unused;  /* as you suspected */
};
```

and adds that the 2 bit *"assigns an as yet unknown flag in the runtime map
element"*.

Three of our findings fall out of that, from a source with no stake in them:

- the third byte **is** a flags field — we had it as `TileState.Flags` on a guess;
- **2 is the only value that means anything** — our sweep found exactly the values
  1 and 2 across all 66 maps, and nothing else;
- the fourth byte is **unused** — we had found it uniform per level and called it
  uninteresting (Q16).

**And what the 2 is *for* was open there.** Somebody eighteen months inside the
executable knew the flag existed and not what it did. Our answer — a bridge over
lava — came from sweeping 66 shipped maps for what each bridge tile *borders*
(Q64). So the two approaches are complementary rather than redundant: static
reversal finds the fields, behavioural sweeps find the meanings.

That is the pattern to expect from these resources, and the reason to read them
as facts rather than as answers.

---

## The editor manual: valuable, and for later

Its own README entry says it "contains a lot of hints on how the game should
work", and that is fair — but its subject is the **editor**, so its sections map
onto milestones we have not reached:

| section | bears on |
|---|---|
| §8.2 **Triggers** — attribute, event, player and flag triggers | the TRIGGERS record, unparsed. **M6** |
| §7.7 **Computer Players** | the AI keeper. A long-term goal, unscheduled |
| §7.1 Setting Creature Properties, §7.4 Hero Parties | THINGS and PLAYERS records. **M6** |
| §2.5 Creature Pool, Availability/Researchability | already parsed (`GlobalVariables.CreaturePool`, `Availabilities`), read by nothing |
| §2.6, §3.4 Imps | M3, and the owner has already answered more precisely than a manual would |

Nothing in it touches what is being built now — tile work, workers, rooms, mana —
because the owner's direct observation of the running game has been both more
precise and more current than editor documentation. **Read it at M6**, when
triggers and objectives arrive, and read it in full then rather than grepping it
now.

**One thing to look for when that happens** (added 2026-09-04): **is there a
fill-in tool in the editor, or a fill-in trigger action?** That is the only
surviving candidate explanation for `FillInHealth` and `TerrainFlags.FillInable`,
which the shipped game gives no keeper access to and which OpenKeeper declares and
never uses. §8.2 Triggers is where a trigger action would be documented. See Q74
and *Worked example 5* below.

---

## The game manual: read 2026-09-04, and it earned its keep

Not an external resource in the sense of the rest of this file — it shipped in
the box, and the owner supplied it (`original-reference/`, gitignored, EA's
copyright). Full write-up in `manual-1999.md`. Recorded here because it changes
where this project should look for a certain kind of answer.

**What it is good at, and nothing else here is:** saying *what a mechanic is
for*. The balance table gives `ModifyAngerOfCreatureInLairPerSecond = −550` with
no statement of what a lair does; the manual says creatures in lairs recover
health and happiness, with no number. Read together they are a rule. That pairing
is the whole of the "what rooms do" research, and it took an afternoon rather than
the several sittings of owner observation it would otherwise have needed.

**What it is bad at:** numbers (it has almost none), timing (it says "each turn"
and never defines a turn), and currency — it shipped with **1.0** and we target
**v1.70**. It disagrees with the shipped data five times, all in creature
attraction, and the data wins every time.

**Its reliability was established before it was used**, which is the part worth
copying: its room pages print a cost and a minimum size for all seventeen rooms,
both of which we already parse. **17 of 17 costs matched and 16 of 17 sizes**, and
the one exception contradicts the manual's own creature page. Only then was the
rest of it trusted — and it immediately corroborated Q45, Q34, Q31, Q9, Q67,
`MinimumImpThreshold`, and both experience caps.

Filing it under the standing rule below: every fact taken from it is tagged with
the manual as its source and, where the shipped data also speaks, the data's
reading is the one implemented.

---

## The standing rule

Every fact taken from any of these gets recorded the way a data extract already
is: **the claim, the source, and the confidence.** A number from a forum post is
a *hypothesis to confirm against the install or the running game*, never an
anchor — that lesson cost this project a whole session on Q53, and it is lesson 3
in `HANDOFF.md`.

---

## Worked example 2: the three offsets we found by scanning

**2026-09-03, with the repo cloned locally.** The owner's framing — *"if someone
has already done the research and it is solid, not refutable, why wander around?
Reference it and compare with our own findings, not copy it"* — points at exactly
the right first target: **the three offsets this project found by scanning for a
measured value.** Those were its shakiest data findings. OpenKeeper parses both
records in full.

**Method.** Read the field *order and widths* out of OpenKeeper's reader, compute
cumulative offsets, and compare. Nothing was copied; what was taken is arithmetic
over a field list, which is fact.

**The arithmetic is self-validating.** `readArtResource` consumes 64 + 4 + 12 + 4 =
**84** bytes, and OpenKeeper's own scattered offset comments then act as
checkpoints on the running total:

| their comment | our computed offset | agrees |
|---|---|---|
| `xc8` | 0x00C8 | ✓ |
| `// e0` | 0x00E0 | ✓ |
| `// ee` | 0x00EE | ✓ |
| `// 120` | 0x0120 | ✓ |
| `// 174` | 0x0174 | ✓ |

Five checkpoints land exactly, so the layout is certainly right — and therefore so
is everything derived from it:

| ours | theirs | verdict |
|---|---|---|
| `KwdKeeperSpells.OffsetManaUpkeepPerSecond` **0xE2**, u16 | `manaDrain`, `readUnsignedShort` at **0xE2** | **confirmed, and named** |
| `KwdKeeperSpells.OffsetManaCost` **0x11C**, s32 | `manaCost`, `readInteger` (signed) at **0x11C** | **confirmed, including the signedness** |
| KEEPERSPELLS record size **406** (derived from contentSize / itemCount) | field list totals **406** | **confirmed** |
| `TrapsOnRealDataTests` trap upkeep **0x0202**, u16 | `manaUsage`, `readUnsignedShort` at **0x0202** | **confirmed, and named** |
| TRAPS record size — we never derived one | field list totals **579** | new, and confirmed against the install |

> **Correction, 2026-09-04.** This row first read "totals 516", which was a
> partial sum: 516 is where `manaUsage` *ends*, not where the record does. The
> full field list is **579**, and `Traps.kwd`'s own `contentSize / itemCount` is
> 579 exactly. The mistake is instructive — a sum abandoned at the interesting
> field looks like a record size and is not one, and nothing catches it except
> the install. Worked example 3 makes that check the method rather than an
> afterthought.

**The signedness agreement is the pleasing part.** We read `manaCost` as signed
32-bit for one reason: Summon Horny's 100000 overflows a u16. They declare it
`int` and read it signed. Two routes, same width, same reason.

**And it drew a distinction we had missed.** A trap carries **two** mana fields:
the standing `manaUsage` at 0x0202, and a separate 4-byte **`manaCostToFire`** at
0x01D4 — what one shot costs. The owner's Q59 measurement was of a standing
upkeep that rose as traps were built, so it is the first; the per-shot cost is a
field nothing here reads yet, and it is not something the measurement could have
told us apart.

**What this cost and what it bought.** Ten minutes, and it upgraded three
scan-derived offsets from "believable, with a long note explaining why" to
"confirmed from an independent direction, with the original field names". That is
the best value-per-minute of any research this project has done, and it is a
direct argument for doing the same sweep across the other record types before
parsing them at M6.

**What it did not do**, and this is the honest half: it confirmed *where* the
fields are, not *what the numbers mean*. `manaDrain` being 7 for Create Imp still
needed the owner watching an orb go from −28 to −35, and `manaUsage` being a
standing charge still needed five trap measurements across three values. Static
reversal locates fields; only the running game says what they do.

---

## Worked example 3: every record size, in an afternoon

**2026-09-04.** Worked example 2 ended with an argument for doing the same sweep
across the other record types before parsing them at M6. This is that sweep. Full
results and the tables live in `docs/research/kwd-container.md` §*Update
2026-09-04*; what belongs here is the **method**, because it generalises and
because it is the shape every future use of these sources should take.

**The method, in one line:** read the field *order and widths* out of their
reader, sum them, and check the total against `contentSize / itemCount` from the
shipped files.

**Why that is inside the boundary.** A list of field widths is a fact about a
file format. Adding it up is arithmetic. Nothing is copied, nothing is
translated, and the output is a single integer per record type — which is then
*verified against the install*, so the sources are contributing a hypothesis and
the original is still the oracle. The C# that came out of it
(`KwdTaggedRecords`) implements a rule the install proved, not a structure their
code has.

**What it bought.** **Eight new record sizes, every one confirmed to the byte on
the first attempt**, and the shape of the two types that have no fixed size at
all. A field list is a hundred-odd widths in sequence; a sum that lands exactly
on an independently measured stride cannot contain a wrong field cancelled out by
another wrong field — not eight times running. So the *layouts* are confirmed,
not merely the totals, and that is the whole of what M6 needs before it can start
parsing.

**What it cost.** An afternoon, most of it spent on two things the arithmetic
could not do by itself: OpenKeeper's `readArtResource` peeks 12 bytes behind a
`mark`/`reset` and switches on a type where every branch happens to consume the
same 12 bytes, so it is 84 bytes rather than the 174 a naive sum gives; and
tagged unions (VARIABLES, THINGS, TRIGGERS) sum to the total of all their
branches, which is meaningless. Both were caught by the install disagreeing.

**The three results worth carrying, beyond the numbers:**

1. **The cross-check is the method, not a formality.** It caught a partial sum I
   had already written down as a record size (TRAPS 516; see the correction
   above), it caught `readArtResource` at 174, and it caught three tagged unions.
   Every single failure of the arithmetic was found by the install and none by
   inspection.
2. **A source can be wrong in a direction that costs you nothing, and you should
   still write it down.** Their CREATURES field list totals **5377** against the
   shipped **5449**; their own offset check skips the last 72 bytes with a
   warning. It is harmless to us — `KwdCreatures` stops at `0x1081`, and every
   offset it reads was *confirmed* by this same sweep — but anyone extending that
   parser past `0x1501` would be extending it into territory nobody has
   described. Recorded as **Q70** rather than left as a shrug.
3. **They locate fields; only the running game says what they mean.** Unchanged
   from worked example 2 and worth repeating with evidence: this sweep produced
   twelve record sizes and not one gameplay rule. `manaUsage` being a standing
   charge still took five owner measurements; the MAP flags byte still took a
   66-map behavioural sweep. **The division of labour is stable — static sources
   for structure, the owner and the original for behaviour.**

---

## Worked example 4: the resource that was filed correctly and read too late

**2026-09-04, at the owner's prompting** — *"did you read this resource:
simonschreibt.de/gat/dungeon-keeper-2-walls/ ? maybe it will be useful"*. It was.
Full write-up in `docs/research/wall-rendering.md`; what belongs here is why it
sat unread for a day when the table above already said what it was for.

**The filing was right.** The row said "useful for the Unity view, says nothing
about simulation", and both halves are true. Deferring it while the work was tile
rules and mana was correct.

**The trigger was missed.** The Unity view became the current task, and nothing
went back to the row that said "for the Unity view". A resource table is only as
good as the moment somebody re-reads it, and this one had a "when" column that
nobody consulted when the *when* arrived. The editor manual has the same shape —
its row says **M6** — so the same failure is queued up for triggers and
objectives.

**What reading it bought, in an afternoon:**

- **It corroborated a guess.** `TileView.HealthPermille` was added on the
  reasoning that a renderer should not need the terrain table to draw damage.
  DK2 does show wall damage and does it by swapping in damage-state *textures* —
  so both the fraction and the eventual asset kind are confirmed.
- **It refuted a simplification.** `AssetCatalogue` asks for one mesh per solid
  terrain. DK2 draws a wall from a set of pieces chosen by the size and shape of
  the room behind it. One key per terrain is too coarse, and the catalogue now
  says so rather than reading as complete.
- **It gave the converter its route** — DK2WADTOOL, Hoenir's 010 Editor KMF
  template, trass3r's KMF→OBJ scripts. Same category of third-party format
  documentation this project already relies on for KWD, so squarely inside the
  boundary; and no converted mesh may be committed, which is what
  `FileAssetProvider` assumes by only ever reading `build/converted`.

**And it produced two open questions rather than pretending to answer them**
(Q71, what picks a wall piece; Q72, whether the deformation is deterministic per
tile). The article's own author separates what he verified from what he inferred,
and marks the runtime "noise modifier" as his inference with the alternative
stated. **Copying that discipline across was most of the work of writing the
note** — a source that labels its own confidence is easy to record faithfully and
easy to over-read.

**The rule this adds to the standing one:** a resource row's "when" is a
commitment. When a milestone starts, re-read the rows that name it — the table
already knows what to read and it cannot tell you on its own.

---

## Worked example 5: OpenKeeper pointed at the mechanism, the install gave the answer

The cleanest illustration yet of the owner's standing instruction — *"if someone
has already done the research and it is solid, not refutable, why wander around? I
need you to just reference it and compare with our own findings, not copy it."*

**The question** (Q74, 2026-09-04): the owner said DK2 has no way to fill a
dug-out tile back to rock. Our terrain data says otherwise — `FillInHealth` = 5,
a `FillInable` flag on the five floor terrains, and Claimed Path's route back to
Rock. Three coordinated fields are not an accident. So: is there *any* path by
which a tile becomes solid again, and did anybody else find one?

**Step 1 — what OpenKeeper has.** Two greps.

| where | what |
|---|---|
| `tools/convert/map/Terrain.java:55` | `FILL_INABLE(0x00008000)` — the same bit we read |
| `tools/convert/map/Variable.java:333` | `FILL_IN_HEALTH(11), // value=5` — the same id and value |

Both **declared once and referenced nowhere else** in `src/`. Their worker task
list — `DigTileTask`, `ClaimTileTask`, `ClaimWallTileTask`, `RepairWallTileTask`,
`ClaimRoomTask`, plus the carry/capture tasks — has no fill-in task.

That is a **negative result worth having**. The most complete reimplementation in
existence read the same fields and never found a use, which rules out "somebody
already solved this and we are re-deriving it badly". It is corroboration, not
proof: two projects can be stuck in the same place.

**Step 2 — the thing they had that we did not.** Their trigger-action enum
(`TriggerAction.java:55`) has `ALTER_TERRAIN_TYPE(22)`, and
`MapController.alterTerrain` implements it as a direct terrain write plus an
optional owner change — **no health, no rate**. That is a mechanism we had not
considered, and it is a genuine candidate answer to "how does a tile become solid
again".

Their reader also gave us a **layout hypothesis** for the record: the action type
is the second-to-last byte of the payload, the repeat count the last, and an
`ALTER_TERRAIN_TYPE` body is `terrainId(u8), playerId(u8), 0, 0, posX(u16),
posY(u16)`, with 1-based coordinates.

**Step 3 — the install decides, not them.** A hypothesis about a record layout is
exactly the kind of claim this file says must be confirmed against the shipped
data. New command `dk2tools kwd alterterrain [-v]` decodes every action record in
every shipped trigger file and prints, per `ALTER_TERRAIN_TYPE`, what the target
tile *starts* as.

**The layout self-validated**: 5408 action records across 66 files, and every
single decoded action type lands inside the enumerated 1..60 range. A wrong offset
would have produced garbage in the hundreds.

**And the answer was the reverse of what was being looked for.** Of 160
`ALTER_TERRAIN_TYPE` actions, **94 are `Impenetrable Rock → Dirt Path`** — scripts
*opening* passages sealed against digging. Level 1's three Rock writes are also
opening: (15,18), (16,18) and (17,18) all start as Impenetrable Rock and are
softened into diggable Rock. **Exactly two writes in the entire campaign turn an
open tile solid**, both in `Secret3`, both Claimed Path → Reinforced Wall.

**What the division of labour bought.** OpenKeeper supplied a mechanism we had not
thought of and a byte layout that would have taken an afternoon to find by
scanning. The install supplied the answer, which no amount of reading their source
could have given — because their source does not know what the shipped maps
contain either. Neither source alone would have done: theirs said *how a tile
could be closed*, ours said *whether any tile is*.

**And it closed the question the honest way, which was partly.** The fill-in
*health* mechanic still has no user anywhere. The temptation was to call
`ALTER_TERRAIN_TYPE` the explanation and file Q74 as answered; it is not the
explanation, because it needs no rate. Two of three candidates eliminated, one
left, and the remaining one named (the level editor).

---

## Where the reference checkouts live (2026-09-06)

Both are **outside this repository**, on purpose. Risk R15 in
`DK2_REIMPLEMENTATION_PLAN.md` originally proposed vendoring Flame's `mapping/`
into `original-reference/research/`; a clone on a separate path is strictly
better, because it cannot be staged by accident at all.

| What | Where | Pinned at | Licence |
|---|---|---|---|
| **Flame** | `D:\Flame` | `f6e2a3d385bf2e6e95dc70cab88aa387bb7659d9` (2026-03-01) | **none** |
| **OpenKeeper** | `D:\OpenKeeper` | tracks `origin/master` | GPLv3 |
| DK2 v1.70 | `E:\GogGames\Dungeon Keeper 2` | GOG 1207658959 | EA proprietary |

### The identifier line, which is Flame-specific

The boundary this file already draws — facts yes, code no — needs one extra
clause for Flame, because Flame is the only source here that grants *nothing*.

**A field offset is a fact about DK2. A name is somebody's authorship.** That
`+0x3B9` on the creature holds a room id is a fact, freely usable. That DiaLight
chose to call the state-category array `g_stateEntries` is an editorial act by an
author who has published no licence permitting its reuse.

**The OpenKeeper precedent does not transfer.** This project does use
OpenKeeper's names — `CreatureStatId`'s members, for one — under ADR-0003's
second amendment, with attribution in `THIRD-PARTY-NOTICES.md`. That rests on
OpenKeeper being GPLv3 and this project being GPLv3. Flame has no licence for
that arrangement to hang on.

**So, in practice:** consult it freely, navigate a Ghidra database with it (that
database lives outside the repository and is never distributed), confirm
readings against it — and when writing anything down here, describe what was
learned **in this project's own words**, with provenance, and never paste one of
its identifiers into a source file — not as a member, not as a type, not in a
comment. Where a document has to name one to explain a finding, it is named *as*
Flame's and attributed, which is a different act from adopting it, and
**ADR-0080 (2026-09-06) makes that the rule**: cite freely in documents, never
in code. `FlameIdentifierTests` sweeps `src/`, `tests/` and the Unity adapter
for the shape of Flame's field names.

**What Flame is actually good for**, measured rather than assumed
(`DK2_REIMPLEMENTATION_PLAN.md` §2.2.1): structure — struct layouts, vtable
membership, cross-references — and confirming an identification reached
independently. It names only **34%** of the symbols in the address range where
creature state, jobs and needs live, so it will not answer behavioural questions.
For those the order in `AGENTS.md` still holds: the binary, then the shipped
data, then the owner watching the game.

