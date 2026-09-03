# External resources, and what we may legally take from each

Written 2026-09-03, after the owner asked for OpenKeeper to be used as much as
possible. It is an invaluable resource and it is also **GPLv3**, so "as much as
possible" has a precise boundary that this file records once so it does not have
to be re-argued.

---

## The licence boundary — read this before opening any of it

**This project is MIT** (owner decision, ADR-0003). That is not a style
preference; it decides what may enter the repository.

| source | licence | what we may take |
|---|---|---|
| **OpenKeeper** | **GPLv3** | **facts only, with provenance. No code, no structure, no translation.** |
| Flame | unlicensed | facts only, with provenance |
| GLKeeper | MIT | code may be adapted **with attribution** — done once, for the WAD decompressor (`THIRD-PARTY-NOTICES.md`) |
| KeeperKlan / editor manual / format write-ups | third-party documentation | facts freely, cite the source |
| the DK2 install itself | proprietary | **nothing may be redistributed** — no executable, model, texture, audio, video or map file |

**Why "facts only" and not "code with attribution".** GPLv3 is copyleft: a
derivative work must itself be GPLv3. Copying, translating or closely
paraphrasing OpenKeeper's Java into our C# would make this project GPLv3 too,
which is the one outcome the owner's licence choice rules out. Facts —
"the TERRAIN record is 552 bytes", "field 0x190 is a flag word" — are not
copyrightable, and taking those is exactly what ADR-0003 already permits.

**The narrower risk, which matters even where the law is comfortable.** This
project's brief asks for *clean-room behavioural reimplementation based on
observed behaviour and documented research*. Reading somebody else's
implementation makes a clean-room claim harder to make honestly, whatever the
licence says. So the order of preference stays what the research method in
`HANDOFF.md` already says:

1. ask the owner, or observe the original;
2. read the KWD data ourselves;
3. third-party **format documentation** (010 Editor templates, KeeperKlan);
4. **OpenKeeper as documentation** — its wiki, its issue tracker, and its
   file-format *facts*;
5. IDA/Ghidra on the executable.

**Where OpenKeeper is most valuable and least risky:** it has solved the *file
formats* and it has 2,800 commits of accumulated knowledge about which fields
matter. Reading its **field names and record layouts as facts** is squarely
inside the boundary. Reading its **gameplay rule implementations** is where the
clean-room argument starts costing something — and it is also where its own
authors say the least is settled: OpenKeeper's creature AI has been an
acknowledged stub for years, which is the same wall Q7 describes here.

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
| [Simon Schreibt on DK2's walls](http://simonschreibt.de/gat/dungeon-keeper-2-walls/) | live | A rendering write-up. Useful for the Unity view, says nothing about simulation |
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
