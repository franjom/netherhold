# The Hero Portal is an entrance too, and one implementation serves both

| | |
|---|---|
| Source(s) | Owner, 2026-09-07 (words below, and one confirmation in play); `DKII.EXE` v1.70 `FUN_004dbf90` and the handler table at `0x006a1e9f` (ADR-0046); `Rooms.kwd`, `Terrain.kwd`, CREATURES and per-level `VARIABLES`; `Data/Text/Default/Text.str` |
| Confidence | **CONFIRMED** from four independent lines: the binary's one-condition gate, the roster search, the levels' own `CreaturePool`, and the running game |
| Implementation | `PortalRules.CanComeFromPortal` (takes the portal's `RoomDef`), `PortalRules.IsEntrance`, `CreatureDef.Flags2`, `RoomTypeId.HeroPortal`, `CreatureFlags2.AvailableViaHeroPortal`; ADR-0086 amendment 2 |
| Tests | `PortalOnRealDataTests.NineCreaturesCarryTheHeroPortalFlagAndTheyAreTheNineThePortalRefuses`, `.TheCampaignUnlocksNineCreaturesOnlyTheFlagGateKeepsOut`, `.FrostysCastleHasOnlyHeroPortalsAndPoolsExactlyTheirNineCreatures`, `.AHeroPortalCountsTowardTheCapJustLikeAPortal` |
| Open questions | none; Q211 closed |

The mechanic: `portals.md`. The interval: `portals-interval.md`.

> **Owner, 2026-09-07:** *"hero portal IS AN ENTRANCE, but for goodly heroes
> (rival faction), until it is destroyed by evil keepers by digging and claiming
> tiles around it, if possible."*
>
> *"'hero portal' is not something a keeper can use, nor can an 'evil' keeper
> summon 'good' creatures. If a keeper claims a hero portal room that means that
> it is already destroyed by claiming all its adjacent tiles, and it just becomes
> an indestructable dead room that can't be sold or repurposed."*
>
> *"hero portal and evil portal don't look the same, they have different
> textures."*

## 1. The data draws no distinction at all

| | Portal | Hero Portal |
|---|---|---|
| room id | 3 | 25 |
| room flags | `0x00001004` | `0x00001004` |
| terrain id | 12 | 40 |
| terrain flags | `0x20000034` | `0x20000034` |
| handler record | `006a1f1a` | `006a22a0` |

**Both flag words are byte-identical**, and so is the whole 41-byte handler
record: `FUN_004dbb90 | - | FUN_004dcd20 | FUN_004dbbc0 | - | code:004dbbe0 |
197 | 197 | 52 | -`. Same constructor, same ownership-change reset, same
per-tick unit, same three integers. **DK2 ships one portal implementation and
runs it for both room types**, which is what "a hero portal is an entrance for
the other side" predicts.

Being separate *terrains* rather than one terrain with an owner-dependent skin is
what the owner's texture observation reports from the outside, and it carries a
consequence: **claiming a Hero Portal tile does not turn it into a Portal.** The
terrain stays 40 and the room stays type 25.

## 2. What separates the sides is the room type, inside the shared code

The candidate builder `FUN_004dbf90`, per creature type, in one condition:

```
(def+0x686 & 0x8000000) == 0                          // not unique
&& ( ((def+0x686 & 0x400) && room+0x18 == 3)          // portal flag, room type 3
   || ((def+0x68a & 8)    && room+0x18 == 0x19) )     // hero flag,   room type 25
```

`room+0x18` is the room type id. **The room type selects which creature flag is
required, and the two are exclusive.** A Portal can never emit a hero and a Hero
Portal can never emit an evil creature — **by room type, not by owner** — so it
holds however the room changed hands. The uniqueness test sits outside the `or`
and refuses both sides.

That is the mechanism behind *"not something a keeper can use"*: not a
prohibition on keepers, but that a keeper's claimed Hero Portal is still room
type 25, so the only creatures it may emit are ones the keeper does not have.

## 3. The hero flag: CREATURES `0x1545` bit 3

The binary reads `byte ptr [def + 0x68a] & 8`. **The runtime offset is no guide
to the file offset** — the loader does not preserve record layout — and
reasoning from adjacency gave a wrong answer first: file `0xF00`, four bytes
after the known flag dword at `0xEFC`, holds a 3000/2800 value and not flags.

What located it was the **roster**. The flag has to select the creatures a Hero
Portal generates, so `dk2tools kwd creaturebits` searched all 5449 bytes x 8
bits of the CREATURES record for a matching set:

```
0x0EFF bit6 EXACT     n=10 : Knight Dwarf Giant Wizard Elven Archer Thief Monk Fairy Guard Royal Guard
0x1545 bit3 off-by-1  n=9  : Knight Giant Wizard Elven Archer Thief Monk Fairy Guard Royal Guard
```

**`0x1545` bit 3 is the flag.** It is bit 3 of a *byte*, which is the shape the
binary tests; `0x1545` is the record's **last dword**, inside the 72-byte tail
Q70 says nobody had accounted for; and only the low four bits are ever set
across all 46 creatures, the upper three bytes being zero everywhere. (The other
three bits: Q210, ADR-0087.)

**The Dwarf is the discriminator.** Bit 30 of the *first* flag dword (`0x0EFF`
bit 6) selects those nine **plus the Dwarf** and is named `MpdRandomInvader` in
our enum, from OpenKeeper. Gating on it — the tempting move, the bit being
already parsed and already named — would give a Hero Portal the heroes'
**worker**. Each side's portal excludes its own worker: the Imp likewise lacks
`AvailableViaPortal`. **An available, named, plausible flag is not evidence; the
roster is.**

Corroborating the file-to-runtime mapping separately: bits 0 and 2 of the same
byte have readers at the same runtime offset with masks 1 and 4, and the def
pointer is `creature+0x370`.

## 4. The dead room needs no new rule

Everything the owner describes about a claimed Hero Portal falls out of flags
already parsed:

- **Cannot be sold.** `RoomRules.CanSell` refuses any room without `Buildable`,
  and `0x00001004` has none. Portal (3), Dungeon Heart (5) and Hero Portal (25)
  are the only rooms with cost 0 and no flags at all, and `RoomRules.CanBuild`
  returns `NotBuildable` for all three (Q207).
- **Cannot be repurposed.** `CanBuild` refuses any tile whose terrain is a room,
  and repurposing means selling first.
- **Indestructible.** The room flags carry no `Attackable`, unlike a Lair or a
  Treasury. The *terrain* is `Attackable`, which is tile-level and is how an imp
  claims it.
- **"Already destroyed by claiming the tiles around it."** A `RoomInstance` is
  tiles of one terrain with **one owner**, so claiming the ring leaves the hero
  side the centre and the keeper a ring that fails `CanProduce` — it does not
  hold the tile a creature climbs out of.

The owner also confirms a portal cannot be sold or rebuilt at all: *"it is a
'resource', a 'room' on a map that a player has to claim. it can be taken over
by a rival keeper, but that's about it."*

## 5. Frosty's Castle: three maps where keepers are *meant* to use them

`dk2tools kwd portals` on **Frosty's Castle**:

```
Frosty's Castle        base= 25s  portals=4
    Hero Portal  owner=2 tiles= 9 at (4,80)
    Hero Portal  owner=2 tiles= 9 at (17,3)
    Hero Portal  owner=2 tiles= 9 at (80,93)
    Hero Portal  owner=2 tiles= 9 at (93,16)
```

**Four Hero Portals, no Portal, and every one unowned** (player 2) and therefore
claimable. The level's own `VARIABLES` pool, for each of keepers 3, 4, 5 and 6:

```
creature13 creature15 creature16 creature17 creature18 creature19 creature20 creature25 creature30
= Knight Giant Wizard Elven Archer Thief Monk Fairy Guard Royal Guard
```

**Those are precisely the nine `AvailableViaHeroPortal` carriers, and creature
14 — the Dwarf — is pointedly absent.** The map has no other creature source, so
a keeper there *must* claim a Hero Portal. Frosty's Lair and Frosty's Realm do
the same.

It is a **skirmish** map (owner: *"frosty's castle is a skirmish map which I
didn't play"*), which is why *"an evil keeper cannot summon good creatures"*
stays exactly true of the campaign: these three maps grant the roster
deliberately. Not MPD — `mpd1`..`mpd7` are separate maps.

## 6. It is called the Mercenary Portal in the game

**Owner screenshot, 2026-09-07.** Hovering an unclaimed portal on Frosty's
Castle shows the tooltip **"Mercenary Portal"** and *"This room is owned by
another power. Its details are unknown."*

**There is no "Hero Portal" string in the game at all.** Against the global text
bank (`Data/Text/Default/Text.str`): zero matches for *"Hero Portal"*, and five
for *"Mercenary"* as a room name:

| string id | value |
|---|---|
| 3080 | `Mercenary Portal` |
| 3081 | `Zoom To Mercenary Portal` |
| 3082 | `MercenaryPortal` |
| 3084 | `Mercenary Portal` |
| 3085 | `Mercenary Portal.  Health: %37%.  Portals owned: %44.  Up to %43 creatures may enter through them.` |

So **"Hero Portal" is the editor's internal label**, which is what `Rooms.kwd`
record 25 carries and what `RoomTypeId.HeroPortal` and
`CreatureFlags2.AvailableViaHeroPortal` are named from. The identifiers stay —
they name the shipped record — but **every player-facing reference should say
Mercenary Portal**, and anyone searching the game's own text for "Hero Portal"
will find nothing.

**The info string is a third confirmation of the capacity rule.** The two
portals' info-panel strings are word for word identical apart from the name:

```
2790  Portal.            Health: %37%.  Portals owned: %44.  Up to %43 creatures may enter through them.
3085  Mercenary Portal.  Health: %37%.  Portals owned: %44.  Up to %43 creatures may enter through them.
```

DK2 tells the player, in the room's own tooltip, that a Mercenary Portal
contributes to "Portals owned" and to the creature cap — the rule of ADR-0086
amendment 2, read off the instructions the same day (`PUSH 3` / `PUSH 0x19` /
`ADD ESI,EAX` before `(n − 1) × 5 + 15`).

String **2433** is a *different* Portal info line — `Portal.  Health: %37%.
Creatures attracted: %42 / %43.  Portals owned: %44.` Two info strings exist for
the same room, one reporting *attracted / max* and one *up to N may enter*.
Which is shown when is UNKNOWN.

A keeper who claims one is not summoning heroes; it is **hiring mercenaries**,
and the Dwarf's absence reads better under that name: you can hire a Knight, not
somebody's workman.

## 7. Confirmed in play

> **Owner, 2026-09-07**, after claiming one on Frosty's Castle: *"hero units (in
> this case mercenary units) arrive through the portal and they are keeper's
> units, no dwarf comes."*

Three claims, each closing something no file could settle:

1. **Creatures come out at all**, so the gate is the **room type** and not the
   owner. The intuitive reading of *"a keeper cannot use a hero portal"* is a
   prohibition on the owner, and it would have produced a dead room here.
2. **No Dwarf**, so `CreatureFlags2.AvailableViaHeroPortal` is on the right bit.
3. **They are the keeper's own units**, not a rival faction's — which is what
   `PortalSystem` does by giving each arrival the portal's owner.

**Two divergences this work found, both fixed.**
`PortalRules.CanComeFromPortal` took a creature and no room, so it applied the
Portal's flag gate to every entrance and a keeper's claimed Hero Portal produced
the keeper's *evil* creatures — and produced **nothing at all** on the three
Frosty maps, where the nine lack `AvailableViaPortal`. Capacity counted only
portals that could produce; DK2 tests the room type and nothing else.

`CanComeFromPortal` had been marked `Confidence.Confirmed` on the binary read
alone, which is premature by this project's convention reserving CONFIRMED for
owner observation. It is now accurate rather than optimistic.
