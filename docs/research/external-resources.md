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
   incomplete (Q70). The worked cases below are the record. A rule
   that has produced that much corroboration is not obviously worth relaxing.

The fourth worked case below is the template to keep, and the sharpest illustration of
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
| §8.2 **Triggers** — attribute, event, player and flag triggers | TRIGGERS parses; execution remains **M6** |
| §7.7 **Computer Players** | the AI keeper. A long-term goal, unscheduled |
| §7.1 Setting Creature Properties, §7.4 Hero Parties | THINGS and PLAYERS parse; world placement remains **M6** |
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
never uses. §8.2 Triggers is where a trigger action would be documented. See Q74 and the worked cases below.

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

## Q303, and what each project knew: a worked case from 2026-09-11

Recorded because the failure was **ours misquoting a source**, not the source
being wrong — the shape worth recognising again.

`CreatureDeathRules.HasDyingState` decided the collapsed, rescuable window from
`GenerateDeadBody` alone, on this repo's note that OpenKeeper's
`processHealthDepleted` tests that flag "and nothing else". It does not. That is
its **first** branch; three more follow it.

| | OpenKeeper | Flame | DKII.EXE, read 2026-09-11 |
|---|---|---|---|
| no `GenerateDeadBody` | yes, first | — | gate 1, `0x004B2F2E` |
| `Possessed` | **no** | — | gate 2, `0x004B2F37` |
| Prison / Torture Chamber | yes, as **entity components** | — | gate 3, as the **room tile under the creature**, `0x004B2F81` |
| `DieInstantly` | **no** | names the getter | gate 4, `0x004B2F8D` |

So OpenKeeper had two of the three we lacked, differently keyed, and the reading
that mattered most — the tile, not the entity — only DK2 has. Flame names
`0x004B2CE0` as damage processing on the physical-thing base class and
`0x0048E110` as the `DieInstantly` getter, which independently matches both
readings; `0x0048B770` and `0x0048F1D0` are unnamed address stubs there, so this
project's readings of those two stand alone. Flame's names are cited here **as
Flame's** and are not adopted.

**The lesson for this file's standing rule.** "Facts only, with provenance"
already required saying where a fact came from. It did not require saying **how
far into the source we read**, and a quotation that stops early is
indistinguishable from a source that stops early. When a note records what
another project does, record the whole routine or say which branch was read.

## The standing rule

Every fact taken from any of these gets recorded the way a data extract already
is: **the claim, the source, and the confidence.** A number from a forum post is
a *hypothesis to confirm against the install or the running game*, never an
anchor — that lesson cost this project a whole session on Q53, and it is lesson 3
in `HANDOFF.md`.

---

## Method, from four worked cases

The full write-ups lived here until 2026-09-09; each one's result has a home
elsewhere, so what is kept is the lesson and the pointer.

**Reference and compare, never copy** (owner: *"if someone has already done the
research and it is solid, not refutable, why wander around? Reference it and
compare with our own findings, not copy it"*). Applied to the three record
offsets this project had found by scanning for a measured value — its shakiest
data findings — OpenKeeper's full parse of both records confirmed all three
independently. That is the shape every use of these sources should take.

**Generalise the check into a sweep.** The same comparison run across every
record type settled all their sizes in an afternoon. Results and tables:
`kwd-container.md` and `kwd-record-fields.md`.

**Filing a resource is not reading it.** One entry sat unread for a day while
its own row already said what it was good for. The row was right; nobody acted
on it. Write-up: `wall-rendering.md`.

**An outside source points at the mechanism; the install gives the answer.**
Asked whether DK2 can fill a dug-out tile back to rock (Q74), OpenKeeper named
`ALTER_TERRAIN_TYPE` as a candidate and the shipped data eliminated two of
three, leaving the level editor. Note the question was later closed the other
way: **there is no fill-in mechanic at all** (Q219), and `FillInHealth` has no
reader. The lesson stands and the conclusion did not — which is why a candidate
is recorded as a candidate.

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

