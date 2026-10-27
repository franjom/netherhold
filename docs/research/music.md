# Mechanic: the dynamic music — what the game asks for, and how the segments follow

Source(s):
- `DKII.EXE` v1.70 (SHA-256 `B5E4E2F6…B449`), read in Ghidra (pyghidra-mcp and the headless
  `CallSlot`/`SlotCallCtx`/`ScanAll` scripts), 2026-10-27
- The shipped `Data/Sound/Sfx/Global/{music,ambience,one_shot_atmos,options_music}{SFX,BANK}.map`,
  parsed byte-exact with a scratch reader, 2026-10-27
- Lead, not a source: the Dethware `DK2` project's `docs/runtime-behaviour.md`,
  `file-formats.md` and `open-questions.md` (github.com/Dethware/DK2, read 2026-10-27; no
  licence, so facts only, re-derived here, none of its identifiers; `external-resources.md`).
  Same binary (its stated SHA-256 is ours). Where this note differs from it, §7 says so

Original version: DK2 v1.70 (GOG)

Presentation only, and not built (no audio playback exists yet; `RESEARCH_STATUS.md`,
*Audio event semantics*). Nothing here feeds a rule: the battle records that drive the music
are presentation too (`binary-census.md` §3). Facts in our own words; no decompiler output
(ADR-0046). Every name below is ours.

Labels: **CONFIRMED** (read in the code or parsed from the data), **STRONGLY INFERRED**,
**LIKELY** (the lead's reading, consistent with what was checked, not re-read), **UNKNOWN**.

---

## 1. The sound manager and its music level

The in-game sound manager is the object behind the pointer `0x0075B468` (vtable `0x0066F7FC`).
Its music level `L` is the dword `+0x2C`. **CONFIRMED.**

| Slot | Function | Does |
|---|---|---|
| `+0x18` | `0x005674F0` | the update (§3) |
| `+0x4C` | `0x00567BC0` | **raise**: `L = max(L, x)`, unsigned |
| `+0x50` | `0x00567BE0` | **set**: `L = x` |

`L` takes `0xFF` (silent) or `0x100`..`0x103` (music levels 1–4). The update maps it to the
intensity **0, 20, 40, 60, 80** through a jump table at `0x005676FC` (`0xFF` and anything
else → 0). **CONFIRMED.**

## 2. Every request (the complete list)

Every indirect call through slots `+0x4C` and `+0x50` in `.text` and `cseg`, with the object
each is made on (`SlotCallCtx`, 25 instructions of context). These are the ones on the sound
manager; the rest are other classes. **CONFIRMED** for the list. A call on a copy of the
pointer held elsewhere could escape the scan; none was seen.

| Request | Site | When |
|---|---|---|
| raise `0x100` (level 1) | `0x00458417` in `0x00458370` | state 179 finds a route to an enemy room that resumes in **180 attacking rooms** |
| raise `0x100` | `0x004589A6` in `0x004586B0` | a route found that resumes in **184 attacking walls** |
| raise `0x100` | `0x0048206E` in `0x00482010` | a route to a Treasury found that resumes in **164 stealing booty**, gated on a world word at `(+0x84)+0x224E` differing from the creature's player (unread) |
| raise `0x100` | `0x00485ADF` in `0x00485AB0`, `0x00485BA0` in `0x00485B80` | `SetState(186)` accepted: **tunnelling** |
| raise `0x101`/`0x102`/`0x103` | `0x005054CD`, `0x005054E7` in `Battle_Update` `0x00505310` | a battle's enemy threat reaches variable 215, 216 or 217 (§4) |
| raise `0x103` (level 4) | `0x00488A44` in `Creature_Update` | every turn while a creature whose definition flag `+0x686` has `0x4000` (the Horned Reaper) exists, unless the next row |
| set `0xFF` (silent) | `0x00488A38` in `Creature_Update` | the same creature, when its player's `+0x76` bit `0x40` is set: **`GameWonSequenceActive`** |
| set `0xFF` | `0x004D9EE0` in `DungeonHeart_Update` | the end of a Heart's opening sequence (elapsed `9 × tps`, `heart-intro.md` §1); no local-player test |
| set `0xFF` | `0x00538504` in `0x005384F0` | the top of every front-end station frame, unconditionally |

States are the executable's labels (`creature-state-table.md`). **CONFIRMED.**

So level 1 is not a fight: it is a creature **setting out on a raid** — the raise comes when
the route is found, as the walk starts, except tunnelling, a direct `SetState`. No raise tests
the owner (bar the Treasury gate), so any player's raider starts your music. Fighting itself
raises nothing below level 2. **CONFIRMED.** Bit `0x40` of player `+0x76` is printed as
`GameWonSequenceActive` by the player dumper (`0x004BD5CD`, `SHR 6`) and set by the win
sequence (`campaign-win-sequence.md`), so the end-sequence Reaper silences the music rather
than raising it: **STRONGLY INFERRED** (that the Reaper belongs to the player the win sequence
marks was not read).

## 3. The update `0x005674F0`, in its order

Called from the session loop right after the world frame (`0x00526B4E`, `GameSession_UpdateLoop`),
so once per world frame. **CONFIRMED** (call site); the frame's rate is the session loop's.

1. If the music switch (`0x0075AD37`, settings object `0x0075AD0B` field `+0x2C`, written by
   `Settings_SetMusicSwitch` `0x00563A20`) is on:
   - with a level loaded (`+0x08`) and no music instance (`+0x30`): start group
     `theme[track]` in the music category (`+0x3C`), `theme` at `0x006BDB70` = {341, 343, 344,
     345}, `track` the level's byte `+0x720D` with 0 read as 1;
   - otherwise set parameter 2 of the music instance to `intensity(L)` and update it;
   - then **decay**: `L > 0x100` becomes `0x100`. **CONFIRMED.**
2. Ambience (`+0x34`): none yet → start group **341** (`0x155`); else set its parameter 2 to
   `intensity(L) + (q == 1 ? 20 : 0)`, `q` = `0x0075AD27`, settings field `+0x1C`, the one
   **`SoundQuality`** writes (`0x005637E0`). **CONFIRMED** (offsets); its three menu labels are
   Text.str 1443 *Low*, 281 *Medium*, 1442 *High*; which one is value 1 is **UNKNOWN** (the lead
   orders them Low first).
3. One-shot atmosphere: none → start group **746**; else update it. **LIKELY** (lead; the
   branch was not re-read past the ambience).

Consequences:
- The decay sits inside the music branch: with music switched off `L` never decays, which
  matters to nothing audible. A level above 1 lasts only while something re-requests it each
  frame; level 1 is never decayed and stays until a set to `0xFF`.
- Group 344 (track 2) is not in `musicSFX.map` (§5): a level asking for track 2 gets no music.
  The table holds four dwords, so a track byte of 4 or more reads past it. Which shipped levels
  set which track byte: **UNKNOWN** (not read here).
- With no raid, battle or Reaper, `L` is `0xFF` (after the Heart's opening) and the intensity 0;
  neither track's item 1 has a link at 0, so the music instance stays stopped and only the
  ambience plays. **CONFIRMED** (code and data); `L`'s value before the Heart opens is unread.

## 4. The battle threat (`Battle_Update` `0x00505310`)

Per battle record, the members that are in category 22 (fighting), with neither `+0x25E` nor
`+0x32D` equal to 202, without flag `+0x3E6 & 0x8000`, and **not owned by the local player**
(world slot `+0x264` through the record's world pointer `+0x00`, `0x005053AB`) add
`definition +0x698`, scaled above level 1 by the percentage at `[0x006E4154] + 0x105 + 2·level`
over 100. Then raise `0x103` if the sum ≥ variable 217 (1500), else `0x102` if ≥ 216 (500), else
`0x101` if ≥ 215 (100). **CONFIRMED** (read whole; `binary-census.md` §3). All eight records
are scored every turn, including those with no local creature (`battle-tab.md` §2), so a fight
between two other players — heroes against a computer keeper — raises your music too. How a
creature joins (5 tiles) and the HUD tab are in `battle-tab.md`.

## 5. The data (`SFX.map`, `BANK.map`)

The layout parses all four background maps to their last byte. **CONFIRMED** (data).
`BANK.map`: 16-byte id, two zero dwords, `count` at `0x18`, then per archive a length-prefixed
name (`Global\Track_1_1` … `Global\Track_3_4`, 9 for music); a sound's bank is a 1-based index.
`SFX.map`: header to `0x18`, `count`, then entries (24 bytes: group count, …), groups (20 bytes),
then per group its items (42 bytes) and per item its sounds (16 bytes) and links (8 bytes).

| Field | Offset | Meaning |
|---|---|---|
| group | `+0x00`, `+0x04`, `+0x10`, `+0x12` | id, item count, flags (player class), parameter-0 id (2 = intensity) |
| item | `+0x00`, `+0x04`, `+0x0C/0D`, `+0x10/12`, `+0x26` | sound count, link count, volume min/max %, delay min/max ms, links pointer at run time |
| sound | `+0x00`, `+0x04`, `+0x08`, `+0x0C` | archive entry (1-based), cumulative weight (low word), length ms, bank |
| link | `+0x00`, `+0x06`, `+0x07` | target item (1-based), min and max intensity, inclusive |

| Group | Map | Flags | Items | Shape |
|---|---|---|---|---|
| 343 track 1 | music | `0x0606` | 135 | 13,235 ms segments, banks 1–5 (`Track_1_1..1_4`, `Track_1SG`); item 1 → 2 at 20–100 |
| 345 track 3 | music | `0x0606` | 156 | 10,663 ms segments, banks 6–9; item 1 → 13 targets at 20–39 (one itself) and 88 at 40–100 |
| 341 | ambience | `0x0606` | 21 | 6.6–125 s drones, volume 40, links only at 0–10 and 0–39 |
| 746 | one_shot_atmos | `0x0204` | 1 | 19 sounds, volume 21, delay 8,000–20,000 ms, no links |
| 838 | options_music | `0x021E` | 16 | links 0–100 |

No music item has zero links. In track 3 the part-4 items (129–152) link to item **153** at
0–19, and 153 has no link below 20: with the music silenced mid-part-4, one outro segment
plays and the music stops. Repeated targets within one range weight the choice (track 3
item 130 lists 131 twice at 80–100). **CONFIRMED** (data).

## 6. The segment picker `0x006163C0` (linked player, vtable `0x00674180`)

`pick(intensity)` on a playing instance (`+0x04` group, `+0x08` current item):

1. No current item: take the group's first item (the entry point). **CONFIRMED.**
2. The item has no links: current := none, return 0 (no stop flag). Inert for the shipped
   music (§5). **CONFIRMED.**
3. Count the links with `min ≤ intensity ≤ max`. None: write `0x7F` to the intensity slot
   `+0x44`, set flag `+0x28 |= 0x100` (stopped), return 0. **CONFIRMED.**
4. Advance the sound LCG at `0x007A53B0` (`× 0x19660D + 0x3C6EF35F`), `k = rotr(seed, 13) mod n`,
   take the `k`-th matching link in file order: uniform over matching links. **CONFIRMED.**

Start (slot `+0x3C`, `0x006165D0`) is the picker with the instance's own intensity `+0x44`; for
a fresh instance with no current item the first segment is a target of item 1's links
(**STRONGLY INFERRED**: that a new instance's `+0x08` is 0 was not read). Resume (slot `+0x20`, `0x00616560`): while stopped and the intensity slot is
no longer `0x7F`, clear the current item, pick, and queue the result. **CONFIRMED.** The
segment end (slot 0, `0x006164D0`) picks with the intensity in `+0x44` and queues the item's
sound on the stream, so a level change is heard at the next segment boundary. **LIKELY**
(lead; slot 0 not re-read). The intensity reaches `+0x44` through parameter 2 (`0x00608DE0`).
**LIKELY.** The LCG is the sound system's own, not a game stream.

## 7. Where the lead differs

- It files the level-1 states as "probably the states that start a fight". They are the raid
  states 164, 180, 184 and 186, and three of the four raise as the walk there starts (§2).
- It guesses `0x005384F0` runs at level start. That is the front-end station tick; the in-game
  silence at level start comes from the Heart's opening (`0x004D9A60`), a caller it does not list.
- It leaves player `+0x76` bit `0x40` open: `GameWonSequenceActive` (§2).
- It names `0x0075AD27` an unknown 1/2/3 option: `SoundQuality` (§3).
- It merges the no-links and no-match cases of the picker (§6, step 2).

## 8. Pseudocode (ours)

```
// once per world frame
Update():
    if MusicSwitch:
        if LevelLoaded && music == null: music = Start(MUSIC, Theme[max(1, level.Track)])
        else: SetIntensity(music, Intensity(L))
        if L > 0x100: L = 0x100
    if ambience == null: ambience = Start(AMBIENCE, 341)
    else: SetIntensity(ambience, Intensity(L) + (SoundQuality == 1 ? 20 : 0))
    if atmos == null: atmos = Start(ONE_SHOT_ATMOS, 746) else Tick(atmos)

Intensity(L) = L switch { 0x100 => 20, 0x101 => 40, 0x102 => 60, 0x103 => 80, _ => 0 }

// requests, from §2
OnRaidSetOut(164 | 180 | 184 | 186): L = max(L, 0x100)   // route found, or SetState(186)
PerBattle(sum): L = max(L, sum >= V217 ? 0x103 : sum >= V216 ? 0x102 : sum >= V215 ? 0x101 : L)
PerReaper(c):  if c.Player.GameWonSequenceActive: L = 0xFF else L = max(L, 0x103)
OnHeartOpened, EveryFrontEndFrame: L = 0xFF

// linked player, at each segment end and on resume
Pick(item, i):
    if item == null: item = group.Items[0]
    if item.Links.Count == 0: return none
    m = item.Links.Where(l => l.Min <= i && i <= l.Max)
    if m.Count == 0: stopped = true; return none      // resumes when i is set again
    return m[SoundRand() % m.Count].Target
```

## 9. UNKNOWN

- The Treasury gate's world word in `0x00482010`.
- Which levels set which track byte `+0x720D`, and whether any asks for track 2.
- The one-shot atmosphere's delay-chained player (vtable `0x006740B0`) and the options-screen
  player (`0x006741C8`), not re-read.
- Whether `SoundQuality == 1` is Low (the value order of its three labels).
- Item fields `+0x14/15`, `+0x22` (a float: 20.0, 100.0, 0.1) and group `+0x0C` (6000, 1000, 149).
- Whether a level's variables override 215–217 (no shipped level was checked).
