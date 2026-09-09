# The Dungeon Keeper wiki as a source: how, and the licence question

The owner suggested using the Fandom Dungeon Keeper wiki, and possibly scraping
it wholesale. It is a genuinely useful source — it has already confirmed two
numbers and corrected one of our readings. This note records **how to use it**
and **one thing to settle before anybody mirrors it**.

## ⚠ The licence question — FLAGGED, not decided

**Fandom wiki text is CC BY-SA** (3.0 Unported on most Fandom communities).
This repository is **GPL-3.0-or-later** (ADR-0041). Those two are not
one-way compatible for *distributing text*, and this project's standing rule is
to flag a legal question rather than assume past it.

So, pending the owner's decision:

**Do not commit wiki prose into this repository.** Not as a `docs/` page, not as
a vendored dump, not as a large block quote in a comment.

**Do use it as a source, the way the manual and the install are used.** What
this project takes from any source is *facts about behaviour* — numbers,
sequences, conditions — recorded in our own words with a citation. Facts are not
copyrightable; the wiki's phrasing is. A short attributed quotation of a
sentence, where the exact wording is the evidence, is ordinary quotation and is
what the notes below do.

**This is the same discipline as ADR-0046** for decompiler output: use it, cite
it, never vendor it. Two sources, one rule, and the rule is about *distribution*
rather than about reading.

> If the owner wants a local mirror for offline searching, the right shape is
> **outside the repository** — the `reverse-engineering/` pattern, a gitignored
> directory — so that nothing licensed CC BY-SA is ever redistributed under
> GPL-3.0 by us. That needs one word from the owner and is not assumed here.

## How to read it: the API, not the page

A plain fetch of `https://dungeonkeeper.fandom.com/wiki/<Page>` returns
**HTTP 403** — Fandom blocks non-browser user agents. That block is a signal to
respect, and there is no need to work around it, because Fandom publishes a
MediaWiki API:

```
https://dungeonkeeper.fandom.com/api.php?action=parse&page=Vampire&prop=wikitext&format=json
```

That is a documented public interface, it returns wikitext rather than rendered
HTML (which is easier to filter reliably), and using it is not circumvention.

## ⚠ How to read it *correctly*: DK2 only

**The owner's warning, and it is the important one:** each page covers
**Dungeon Keeper, Dungeon Keeper 2, Dungeon Keeper Online and Dungeon Keeper
Mobile together.** This project targets **DK2 v1.70 only**, and the games differ
on exactly the sort of detail we are looking for.

The wiki marks the distinction in two ways, and both must be honoured:

1. **A top-level section per game** — `== Dungeon Keeper 2 ==`. Anything outside
   it is a different game.
2. **Inline `(DK1)` / `(DK2)` / `(DKO)` tags** inside shared infoboxes and
   lists.

**A worked example of why this matters.** The Vampire page's infobox says a
Vampire appears when corpses rot in a Graveyard — *ten* of them for DK1 and
*five* for DK2, on adjacent lines in the same field. Read carelessly, that page
supports either number. Our shipped data says
`DecomposeValueNeededForVampire / DecomposeValue` = `1000 / 200` = **5**, so DK2
is right and DK1's ten would have been a plausible, wrong, well-sourced answer.

**And a trap in the other direction.** The Vampire page's *lead paragraph* — the
part before any game heading — says a resurrected vampire returns "at full
health". The lead is game-agnostic prose covering all four titles, so it is
**not** a DK2 claim, and the DK2 section says only that the vampire loses a level
before reincarnating. The owner's own account is that it *sleeps to recover*.
Those disagree, and the untagged lead is the weakest of the three.

## Standing of the wiki as evidence

Below `owner observation` and the shipped data, above OpenKeeper for *design
intent*, and it earns that by being checkable. Before trusting the Vampire
page's unmeasurable claim (the resurrection rule), four of its measurable DK2
claims were checked against the install and all four held: health 2500, wage
750, job Thinker, fight style Blitzer — plus the sacrifice recipe and the
five-corpse arithmetic. `CreatureNeedsOnRealDataTests` pins them, so if any drift
the citation loses its credit.

**A source that gets the health and the wage wrong is no source at all.** That
is the whole reason to spend a test on numbers we already had.

## What it has produced so far

| Claim (DK2 section) | Status |
|---|---|
| Five corpses in a Graveyard make a Vampire | **CONFIRMED against the data**, `1000/200` — and it validates our reading of `DecomposeValue` as a per-corpse worth, which nothing else could check |
| Two Black Knights sacrificed make a Vampire | **CONFIRMED against the data**, and it is the first thing ever to read `SacrificeEntry`, so it doubles as a layout check |
| A slain Vampire loses a level and reincarnates | **CONFIRMS the owner** |
| Only Monks can lay one to rest permanently | **CONFIRMS the owner** |
| A level-1 Vampire is lost for good | **CONFIRMS the owner** |
| **Lost for good if you control no Graveyard when it is slain** | **NEW — the owner did not mention this third condition** |
| It reincarnates **in the Graveyard** | **AGREES with the owner** once Q113 closed: the sarcophagus is the Graveyard's, not the vampire's own bed |
| At level 10 it raises corpses into Skeletons, which competes with making more Vampires | New, and not built — levels (`CreatureLevelRules`) and corpses (`CreatureActivity.Dead`) now exist, so it is no longer blocked on either |
| Health 2500, wage 750, Thinker, Blitzer | **CONFIRMED against the data** |

## Worth fetching next, when there is a reason to

Not speculatively — a page is worth reading when a mechanic is being built.
The ones with numbers this project will need: **Graveyard**, **Torture
Chamber**, **Temple** (the sacrifice table above), **Lair**, and **Monk**. Each
read the same way: DK2 section only, facts not prose, cross-checked where the
install can check them. **Prison** has since been done from the binary and the
owner instead (`prison.md`), which also found `MotionlessInPrisonSeconds` to
have no reader at all — a caution about taking a variable name from any
secondary source.
