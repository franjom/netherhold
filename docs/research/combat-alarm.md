# Mechanic: the NonFighter arm and the alarm — a worker's reluctance, and the room that answers

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-23. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046). State names are the executable's own `Text.str` labels
  (creature-state-table.md); field names its creature dumper's (creature-object.md).

Original version: DK2 v1.70 (GOG)

Questions: answers Q374; opens Q390–Q391; corrects combat-target-selection.md §3.2,
which read the alarm's call as a commit to a fight. `combat-fear.md` owns the
head-count and the withdraw state this shares; `combat-target-selection.md` §3.1 the
near-the-heart path, restated in §1 only as far as the build needs.

**What this settles:** the Imp's arm **never fights a hero**. Away from its rooms it
withdraws; near them it counts heads, and when its side is outnumbered a `Scout`
runs to the nearest room and **raises the alarm**: that room's creatures are sent
running to where the hero stood, until their summed `Threat` covers the deficit,
and stand there thirty ticks looking for a fight the ordinary way. Only within
about three and a half tiles of its own heart does a worker fight, and then
anything hostile within a tile.

---

## 1. The NonFighter arm (`0x0045BDF0`), as built

**Setup.** `nearHeart` = my distance² to my owner's `DungeonHeartPosition` (tile
centre) `< 0xD000`, 3.6 tiles (§3.1 of the selection doc). Sight² and hearing² as
every arm, except an Imp's sight is forced to `0x1000` (one tile²) and its hearing
to nothing. My side is the Turncoat player while `TurncoatTimer` runs.

**Per hostile candidate**, over every player's creatures:

1. `nearHeart`, **or** a definition byte read by `0x00494360` equal to 1 — `def+0x5B3`
   for an `IsUnique` creature, `def+0x770` otherwise, files `0xCE2`/`0xE9F`, unparsed
   and unnamed (Q390) — takes the **shared template**: distance with the alignment
   discount (7/8), `IsAttackable`, not my `+0x4A3`, seen within sight or reached
   within hearing, lowest wins. The winner goes to the acquirer's common tail (route,
   the `d/3` gate, the fight-starter) and **the worker fights**.
2. Otherwise the candidate must be **owned by the Good player**; anyone else is
   skipped. Then the **hero path**: `d < sight²` and a line of sight (no hearing),
   `d < best`, the hero has no target or targets me, `IsAttackable`. Then:
   - my `Attacking` is set to the hero, tentatively, and the old tag stashed;
   - **not within `GuardRoomReportingDistanceTiles` of a room of mine**
     (`0x00460B50`, combat-fear.md §4 step 9) → the withdraw state; success returns
     `0xEA60`, "I left", which the acquirer returns as 1; failure restores the tag
     and the scan continues;
   - **near a room:** `theirs` = the hero's owner's strength within five tiles of
     me, `ours` = my owner's × 3/4 (`0x004FAC40`, summed level-scaled `Threat`).
     `ours > theirs` → restore the tag, **keep scanning** — nothing happens;
   - else `deficit = (theirs − ours) × 3/2` and **the alarm** (§2); failing that
     the withdraw state; failing that restore and continue.

So on the hero path `best` is never lowered and no tag is ever kept: the arm's
three `0xEA60` exits are the disengagement the earlier reading called a commit.

## 2. Raising the alarm (`0x004752B0(state, &threatPos, deficit)`)

Refused unless my definition's `JobClass` is **3, `Scout`** — the Imp's — and my
owner's `CallToArmsOn` (`player+0x76` bit 2) is clear. Refused when **someone is
already responding** (`0x004754D0`): any creature of my owner in state 6 heading
for 251 or 254, or in 251 or 254, whose destination (`0x004755C0`: the state's
`+0xE3` for state 6, else its position) lies within `0x19000` — five tiles² — of
the threat. Then the **nearest room of mine** to the threat by distance² to its
`+0x27/+0x29` tile (all class-9 things on `RoomList`); `0x004E4D90(room, &out, 0,
0)` gives a position in it (Q391); enter 199 if not already in the combat group;
route there with `0x00471890(&pos, 251, 0x80)` — Imp word 0 — and stash the
threat's position at `state+0xA7/+0xAB`, the deficit at `+0xAF` and the room's tag
at `+0xB1`; **run speed**; the definition's `+0x6E3` sound. Return 1.

## 3. Arriving: state 251, "responding to a threat" (`0x00475660`)

1. If any creature of my owner **eight or more tiles from me on either axis** is in
   state 252 → return 0 (idle): another alarm has just been raised elsewhere.
2. **Dispatch**, over my owner's creatures in list order, while the deficit is not
   yet negative. A candidate is skipped when: `IsWorker` or `IsHorny`; under Call to
   Arms (`0x004888C0`: owner's `CallToArmsOn`, the creature's `+0x3E6` bit 20, or
   in states 190–192); in the combat group; the gate `0x0045A6D0(0)` refuses; its
   `RoomId` (`+0x3B9`) is not the alarm's room and it is not in state 6 heading for
   a job there (`state+0x68 == 6`, `+0x6C` the room); its health is not above a
   **quarter** of its level-scaled `Hp`; `state+0x3C == 2` (UNKNOWN, Q391); its
   state's group tag is 3 (the worker jobs) or 7 (idle, eating, joining, leaving,
   stunned, burning); or it is already en route to 254. Otherwise it is routed to
   the threat's position with `0x00471890(&pos, 254, 0x80)` (Imp 0), given **the
   raiser's run speed** (`def+0x5B8` of the Scout, not its own — a slip), its
   `+0x2CC` zeroed, and the deficit reduced by its level-scaled `Threat`.
3. **Stand down.** The `+0x6E3` sound; `SetState(1)` then `SetState(252)`; a
   worker starts action 79 (`0x004969D0(0x4F, 1, 0, 0, 0)`) — the alarm animation.
   State 252's handler (`0x00458680`) returns 1 only while an action is current, so
   a non-worker is idle at once and a worker when the action ends (length unread).

## 4. Answering: state 6 to state 254, "reporting danger" (`0x004759A0`)

State 6 (`0x00470C80`) walks the route and, when the path is finished
(`0x004D5B20 == 0x10000`), enters the arrival state at `state+0xDF`; on the way the
fight gate allows a look (only 202 and 251 are refused), so a responder acquires
the hero as soon as it sees or hears it. Arrived, state 254 counts `state+0x7E` up
each tick and **returns 1 for thirty ticks**, then sets the walk speed and returns
0: idle. Standing there it is outside the combat group, so `ProcessFight` runs
every tenth tick and the ordinary acquisition takes over.

## 5. Built and not built (ADR-0166)

**Built** (`CombatTargetRules.NonFighter`, `FearRules.RaiseAlarm`,
`RespondToThreat`, `RaisingAlarm`, `Reporting`): §1 without the unnamed byte's
escape; §2 with the room's bounding-box centre for its position; §3 steps 2–3 with
the room match as a room job whose tile lies in the alarm's room, the group-tag
exclusions as the room-job activities, and the raiser's-speed slip replaced by the
responder's own run speed; §4 with `CreatureActivity.Reporting` covering both the
walk and the thirty ticks.

**Not built, named:** step 1's eight-tile test and action 79 (state 252 ends at
once here); Call to Arms; the Turncoat side; `state+0x3C`; the `+0x6E3` sounds;
Jack, the other NonFighter.

## Confidence

**CONFIRMED (binary):** §1's order, the Good-owner filter, the hero path's three
exits and their `0xEA60` return; §2's conditions, the five-tile check and the
state fields; §3's every skip, the quarter-health test, the raiser's run speed,
the deficit countdown, the two `SetState`s and action 79; §4's thirty ticks and
the walk speed; state 6's arrival.

**STRONGLY INFERRED:** that `RoomList` entries are class 9 and the alarm's
"nearest room" is over all rooms, not guard rooms only; that `RoomId` is the room
a creature works in.

**UNKNOWN:** Q390 (the escape byte), Q391 (`0x004E4D90`'s spot, `state+0x3C`,
action 79's length).
