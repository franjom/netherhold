# System page template

Every system page on this wiki has the same seven parts, so a reader who knows
one knows them all. A system is one entry of
[`docs/systems.yaml`](https://github.com/franjom/netherhold/blob/master/docs/systems.yaml),
the closed list the generated pages are indexed by (ADR-0643).

A system page is a map, not a copy. It says what the system does in a few
paragraphs and links down into the repository for everything else. A number, an
address or a rule that lives in a research note is linked, not restated: the
note is versioned and checked with the code, and this page is not.

Copy the block below into `docs/wiki/<Page-Name>.md`, fill it in, and add the
page to `_Sidebar.md` under **Systems**.

````markdown
# <System title>

`Research: <chip>` · `Simulation: <chip>` · `Verified vs original: <chip>` · `Unity: <chip>`

<One sentence: what the system is, in the scope words of docs/systems.yaml.>

## Behaviour, in our words

<Two to five short paragraphs: what DK2 does, as a player would notice it and as
the binary explains it. Each claim carries its confidence on the one scale
(see [[Glossary]]). No decompiler output, no Flame names (see
[[Legal and Clean Room|Legal-and-Clean-Room]]).>

## Numbers from the game database

<Which GameDatabase records and fields drive the system (terrain MaxHealth,
creature speeds, room costs...). Name the field and where it is read; do not
copy values that the shipped data already holds. A balance constant written
into simulation code is a bug.>

## Evidence

| What | Where |
|---|---|
| <the decisive function or table> | [`<ADDRESS>`](https://github.com/franjom/netherhold/blob/master/docs/research/functions/<ADDRESS>.md) |
| <the research note> | [`<note>.md`](https://github.com/franjom/netherhold/blob/master/docs/research/<note>.md) |
| <a capture or owner observation> | <experiment link or Q number> |

## Our implementation

- **Code:** <the systems and rule types, linked>
- **Tests:** <the test classes>
- **Scenarios:** <the scenario files, if any>
- **Decisions:** <the ADRs that shaped it; the full list is
  [[Decisions by System|Decisions-by-System]]>

## Divergences and open questions

<Where we knowingly differ from DK2 and why (each one has an ADR), and the open
questions that touch the system (see [[Open Questions|Open-Questions]]).>

---

*Last verified against `<short sha>` on <YYYY-MM-DD>.*
````

## The status chips

Each chip is one word from a fixed list, so they can be compared across pages.

| Chip | Values | Meaning |
|---|---|---|
| Research | `read` · `partial` · `unread` | How much of the system's DK2 behaviour has been read in the binary and the shipped data |
| Simulation | `built` · `partial` · `not built` | How much of it `DK2.Simulation` runs |
| Verified vs original | `measured` · `spot-checked` · `none` | Whether runs of our simulation have been compared with DK2's own: `measured` means a replay or capture comparison over many events, `spot-checked` a few observations |
| Unity | `drawn` · `partial` · `not drawn` | Whether the Unity presentation shows it |

A chip states the page author's judgment on the date in the footer. The
[[Status Matrix|Status-Matrix]] gives the counts behind it.

## The footer

`Last verified against <sha>` names the commit the page was last checked
against: every link opened, every claim compared with the note it summarises.
Nothing checks it automatically. A page whose footer is old may still be right,
but it has not been re-read since.
