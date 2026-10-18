# DK2's controls and the default key list

| | |
|---|---|
| Source(s) | `<install>/ReadMe.txt` §7 "FULL KEY LIST" and `<install>/reference_card.pdf` (both shipped, read 2026-09-09); `original-reference/Dungeon_Keeper_2_Manual.pdf` p.3 command card and pp.37, 40–41 |
| Observed behaviour | Not yet observed in the running game. The three documents are EA's own and agree |
| Confidence | **CONFIRMED** for the mapping below, except the four rows marked in [Where the sources disagree](#where-the-sources-disagree) |
| Implementation | The researched G/H/M and campaign Options controls are integrated; see [minimap-controls.md](minimap-controls.md). `PlayerToolMode` and the numeric debug bindings in `DungeonRunner` remain ours. |
| Tests | ClassicHudControlsTests covers visibility and modal routing; the broader original key list is not fully implemented. |
| Open questions | four, in [What is open](#what-is-open); none has a Q number yet |

Legal standing of the manual PDF: `manual-1999.md`, which is the one home for it.
The other two sources live in the install and are never committed, so the existing
"NEVER COMMIT: original Dungeon Keeper 2 material" rule already covers them. A
key mapping is functional fact, and that is all this document takes.

---

## The three sources, and which one wins

The repo cited none of these before 2026-09-09.

| Source | Era | Standing |
|---|---|---|
| `<install>/ReadMe.txt` §7 | ships with the patched install, and its §5 is the patch notes | **The superset and the tiebreak.** Most specific, latest, and the only one with the six ally-messaging bindings |
| `<install>/reference_card.pdf` | 1999 | **Key→function ordered**, so it settles the manual card's layout with no reconstruction |
| Manual p.3 command card | 1999, DK2 1.0 | Complete but two-column; see the extraction note below |

`pdftotext -layout` mis-pairs the manual card's right-hand column by one row —
labels and keys land on different lines — so read the reference card instead. Four
independent cross-references inside the manual (p.16 `M`, p.35 `F`, p.40 `ESC`,
p.41's camera combos) are what caught it.

---

## The default key list

Rebindable in-game: **Options → Control → Define Keys** picks an action from a
scrolling list and takes the next key pressed (manual §16.1.4.3). So this is the
default map, not the only one. The roster that screen lists is unread.

### Possessed creature, first person

| Function | Key |
|---|---|
| Move forward / back | UP / DOWN arrow |
| Strafe left / right | LEFT / RIGHT arrow |
| Fire | SPACE |
| Creep | RCTRL |
| Run | RSHIFT |
| Toggle Sniper Mode | INSERT |
| Pick Lock / Disarm / Jailbreak | NUM 0 |
| Melee Attack Select | 1 |
| Creature Spell Select 1 / 2 / 3 | 2 / 3 / 4 |
| Creature Primary Ability | 5 |
| Creature Secondary Ability | 6 |
| Select Ally For Group | 7 |
| Remove From Group | CTRL + G |

Movement is from manual p.37; the rest is the ReadMe list.

### Keeper mode — camera and view

The overhead mode has three names across the sources: "God Mode" (manual card),
"Dungeon View Mode" (reference card), "Keeper Mode (overview)" (ReadMe).

| Function | Key |
|---|---|
| Scroll up / down / left / right | UP / DOWN / LEFT / RIGHT arrow |
| Speed Scroll Key | SHIFT + arrow |
| Zoom In / Out | HOME / END |
| Zoom Key | CTRL + UP/DOWN arrow |
| Rotate View Left / Right | DELETE / PG DN |
| Rotate Key | CTRL + LEFT/RIGHT arrow |
| Camera Mouse Rotate | hold X, move mouse left/right |
| Camera Mouse Zoom | hold Z, move mouse up/down |
| Camera Isometric / Top Down / Oblique | F1 / F2 / F3 |
| User Camera 1 / 2 / 3 | F4 / F5 / F6 |
| Increase / Decrease Ambient Light | + / - |
| Increase / Decrease Gamma | SHIFT + . / SHIFT + , |

Free camera, and the same keys set a user camera (manual §16.2.2.4 stores the
result with a STORE button):

| Function | Key |
|---|---|
| Pitch Up / Down | CTRL + HOME / CTRL + END |
| Yaw Left / Right | CTRL + PG UP / CTRL + PG DN |
| Roll Left / Right | CTRL + INSERT / CTRL + DELETE |

### Keeper mode — everything else

| Function | Key |
|---|---|
| Ally Window | A |
| Toggle Control Panel | G |
| Toggle Player Information | I |
| Toggle Full Screen Map | M |
| Zoom To Dungeon Heart | H |
| Zoom To Next Fight | F |
| Zoom To Portal | P |
| Pause And Options | ESC |
| Take Screen Shot | PRT SCRN |
| Broadcast Message To All | TAB |
| Send Message To All Allies | ALT + A |
| Send Message To Ally 1–4 | ALT + 1 … ALT + 4 |
| Quick Save / Quick Load | CTRL + S / CTRL + L |
| Restart Level | CTRL + R |

The seven letters are mnemonic — Ally, Fight, GUI, Heart, Information, Map,
Portal — which is a useful internal check on any future transcription.

### Hand of Evil modifiers

Held while clicking a creature portrait on the Control Panel:

| Function | Key |
|---|---|
| Pick Highest Level Creature | hold CTRL + . |
| Pick Lowest Level Creature | hold CTRL + , |

These are the only two bindings in keeper mode that change simulation state.

---

## Where the sources disagree

| Row | Disagreement | Reading |
|---|---|---|
| Creep, Run | ReadMe **RCTRL / RSHIFT**; both 1999 cards say bare CTRL / SHIFT | Take the ReadMe. It is later and more specific, and SHIFT + arrow is already speed-scroll, so a side distinction has a reason. Whether the left keys also work is unread |
| Ambient light | ReadMe `+` / `-`; reference card `=` / `-` | Not a real disagreement: unshifted `=` is the `+` key on a US layout |
| Zoom In / Out | ReadMe and manual give HOME / END; the reference card omits both | Present. Two sources to none, and the reference card omits nothing else |
| Pick up Objects (Not creatures) → PAGE UP | **Manual card only.** Absent from both v1.70-era documents | Unresolved. Either dropped by v1.70 or a fourth extraction artefact. First item in [What is open](#what-is-open) |

"Toggle GUI" (manual, reference card) and "Toggle Control Panel" (ReadMe) are the
same `G`; the ReadMe's name is the one the game's own UI uses.

---

## What this means for us

**No keeper-mode hotkey selects a room, spell, trap, door or creature.** All three
sources agree, and the reference card's keeper section is headed "HOTKEYS", so it
is not an omission. DK2's keyboard drives the camera, the view toggles, the
zoom-to jumps and the meta commands; every gameplay verb is mouse-driven.

That is the fact that governs any port of these bindings. Implementing DK2's
hotkeys faithfully will not produce a keyboard-driven feel, because DK2 had none.
Wanting one is a deviation to document under the `AGENTS.md` fidelity rule, not a
port.

It also means `DungeonRunner`'s `1`–`7` tool-mode bindings have no faithful
counterpart to move to: DK2 spends `1`–`7` on possess-mode ability selects and
offers nothing for room or tool selection. `PlayerToolMode` already says it is an
interface choice rather than DK2 behaviour, which stays true — but the keys are
ours and collide with DK2's meaning, so a faithful pass has to decide, not
translate.

Against what exists today, of the 62 default bindings: three act on state we have
(`H`, `P`, `ESC`), about thirty are camera and renderer work whose constants are
all unread, and about twenty-nine need features that do not exist — possession and
combat, multiplayer, the Control Panel, the map screen, the creature panel, and a
save format of our own.

---

## What is open

1. **Does PAGE UP pick up objects in v1.70?** The manual card's row is absent from
   both patched-era documents. Observable, and the owner has the install.
2. **Where does DK2 persist a rebound keymap?** No `.cfg`, `.ini` or keymap file at
   the install root, so the registry is the candidate. Unread.
3. **The Define Keys action roster.** Reading it would confirm whether the shipped
   key list is the complete set of bindable actions.
4. **Every camera constant.** Scroll speed, rotation rate, the speed-scroll
   multiplier, the F1/F2/F3 preset tilt and zoom, and the ambient-light and gamma
   step sizes. None read from the binary and none measured. The Options screen
   exposes Scroll Speed and Sensitivity sliders, so several are settings with a
   default rather than a single value.
