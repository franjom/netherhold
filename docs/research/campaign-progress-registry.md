# Unlocking campaign levels for capture (registry)

**Scope:** how DK2 v1.70 decides which campaign level the world map offers, and
how to move that without playing the campaign. This exists for one reason: a
two-sided capture needs a *choice* of level, and until 2026-09-10 only level 1
— the tutorial, whose script is gated on the player digging — could be reached
(`docs/experiments/level1-idle-001`).

It is not a game rule and nothing in the simulation reads it.

## Confidence

| Claim | Confidence | Basis |
|---|---|---|
| The key is `HKCU\Software\Bullfrog Productions Ltd\Dungeon Keeper II\Configuration\Player` | **Known** | Read from the owner's install |
| `Player Level Status` is what gates the world map | **CONFIRMED** (owner, 2026-09-10) | Two writes, two predicted outcomes, both observed |
| It is **160 bytes**, forty dwords | **Known** | The loader's own clamp, below |
| Entry *n* set to 1 means that map is done | **CONFIRMED** (owner) | 20 entries offered level 17; 23 offered level 20 |
| The per-index map assignment below | **Strongly inferred** | Derived from those two points plus the shipped corpus; the count is confirmed, each individual slot is not |
| `Level Number` and `Total Evil Rating` do **not** gate it | **CONFIRMED** (owner) | Both were set and neither moved the map |

## The size comes from the loader, not from a write-up

A widely repeated description says this value holds **25** four-byte integers.
It does not, and the difference matters: the same description warns that a wrong
size makes DK2 discard the value, so following it would have destroyed what it
was trying to set.

`0x0055FED3` is the load:

```
0055fed3  MOV EDX,[0x0066f470]      ; -> "Player Level Status"
0055fedc  CALL 0x005622b0           ; how many bytes are stored?
0055fee1  CMP EAX,0xa0              ; 160
0055fee6  JNC 0055fef7              ;   >= 160 -> read 160
0055fef7  MOV EAX,0xa0
0055ff02  LEA ECX,[ESI + 0x881]     ; destination on the profile
0055ff12  CALL 0x00562260           ; read that many bytes
```

**160 bytes, forty dwords**, landing at `profile + 0x881`. Two independent
checks agree: `Total Evil Rating` and `Level Attempts` are already 160 bytes in
a real registry, and the value read immediately after this one, `Secret Levels`,
clamps to `0x40` and is 64 bytes there.

The value names are **not** an indexed table — each is loaded by absolute
address (`0x0066F470` is this one), so finding a neighbour's code means scanning
for its own pointer, not walking an array.

## The array is indexed by map, not by level number

Three campaign levels branch, so twenty numbered levels occupy twenty-four
entries:

```
idx   0  1  2  3  4   5   6   7  8  9  10   11   12   13  14  15  16   17   18  19  20  21  22  23
     L1 L2 L3 L4 L5 L6a L6b L7 L8 L9 L10 L11A L11B L11C L12 L13 L14 L15a L15b L16 L17 L18 L19 L20
```

All four extra maps — `Level6b`, `level11B`, `level11C`, `Level15b` — are in the
shipped corpus and `dk2tools kwd oracle` lists them.

This layout was not assumed. Setting the first **20** entries offered level
**17**, which is what the table predicts (20 entries end at L16); setting **23**
then offered level **20**, and every earlier level stayed selectable. A layout
without the variant slots would have predicted 21 and 24.

## What does not gate it

Both of these were set and observed to change nothing, so neither is worth
trying again:

- **`Level Number`** is the current position, not progress. Setting it to 20
  with two levels beaten still offered level 3.
- **`Total Evil Rating`** is the score per map — forty dwords, non-zero for each
  map played. Marking seventeen more of them non-zero still offered level 3.

`Player Level Status` may not even exist until something writes it; on this
install it was absent while the owner had genuinely completed two levels.

## Procedure

**Close DK2 first** — it rewrites this key on exit and will overwrite an edit
made while it is running.

```powershell
# 1. Back up the whole key. Do not skip this.
reg export 'HKCU\Software\Bullfrog Productions Ltd\Dungeon Keeper II' dk2-key.reg /y

# 2. Mark the first N maps done, at the size the loader expects.
$p = 'HKCU:\Software\Bullfrog Productions Ltd\Dungeon Keeper II\Configuration\Player'
[byte[]]$v = New-Object byte[] 160          # 160, never 100
for ($i = 0; $i -lt 23; $i++) { [Array]::Copy([BitConverter]::GetBytes([uint32]1), 0, $v, $i*4, 4) }
Set-ItemProperty -Path $p -Name 'Player Level Status' -Value $v -Type Binary
```

`N = 23` leaves level 20 as the next mission with everything below it
selectable. For a capture of level *k*, enough entries to reach it is all that
is needed; more is harmless.

To undo: `reg import dk2-key.reg`.

## What this does not restore

Marking a map done is not the same as having played it. The profile's other
campaign state — evil ratings, level attempts, transfer creature — is untouched,
so the world map's *statistics* will not match its *progress*.

For capture work that does not matter: the probe reads the trigger flag and
timer banks, which a level initialises from its own script at load. It would
matter for anything that reads campaign history, and no experiment here does.
