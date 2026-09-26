# Research: every route-helper call site, with the resume state it passes

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-24. Addresses and
  values are **facts about the binary in our own words**; no decompiler output
  enters this repository (ADR-0046). State labels are the executable's own
  `Text.str` strings (creature-state-table.md); job names are `JobType`'s.

Original version: DK2 v1.70 (GOG)

Questions: opens **Q418** (the Imp's teleport branch is read and not built).
Companion to `travel-states.md`, which owns the protocol; this note is its call-site
table, the list §9 said the caller scan reproduces. `imp-task-priority.md` owns the
imp task kinds, `job-selection.md` the job handlers.

**What this settles:** the resume state for every call of the two route helpers, and
for every call of the Imp's route-or-teleport wrapper that most imp walks go
through. It names the resume state for each of the eight travelling activities
the next retirement slices need (§5).

---

## 1. The count, and how each row was read — CONFIRMED

**134 sites, not 138:** 112 calls of `0x00471890` (to a position, state 6) and 22 of
`0x00471A70` (to a room, state 7), as `travel-states.md` §2.1–2.2 give them. A raw
E8/E9 rel32 byte scan of every initialised block (`.text` and `cseg` included)
finds the same 134 and no others, and no absolute occurrence of either address
exists. §9's "138" was a miscount.

Each helper takes three stack arguments (`RET 0x0C`), pushed options first, then
resume, then target. So **the resume is the second push before the call**. Every
row was read from the pushes back to their source:

- **Literal pushes** were taken as they stand, after checking that no jump target
  lies between the first push and the call. Where one does (thirteen helper sites, two wrapper sites), every
  incoming branch lands on or before the options push. The one shared tail with
  different values, `0x0046E7A0`, has both of them read and listed.
- **Register pushes** were traced to their last write in the function, including
  loop back-edges past the call.
- **Two pushes interleave with another call.** At `0x0046330D` and `0x00476309`
  the target is the result of `0x004E4D90` (`RET 0x0C`, three arguments of its
  own), pushed after it returns.
- **`0x0046178D`** shares its options push (`0xE8`, at `0x00461741`) with
  `0x0046175F`. The push comes before the owner branch.

**Computed option words.** Eight sites compute the options as
`creature+0x36F == 1 ? 0x80 : 0`, with `+0x36F` being the creature type (1 is the
Imp, ADR-0115). They are shown as `Imp ? 0x80 : 0`. The mechanism is
`(t-1)` → `NEG`/`SBB` → `AND 0xFFFFFF80` → `ADD 0x80`. Option bit 0 is the tunnelling
fallback (`travel-states.md` §2.3); the other bits go to the route request
`0x004D0F60` unread.

**Caller state** is the state whose handler contains the call, found from the
state table's installer (`0x0047BBE0`, which holds a DATA reference to every
handler). Where the call is in a subroutine, the column names the states or
jobs that call it. A job handler runs from the job dispatcher `0x00478800`, whose
29-entry jump table at `0x004789A8` gives the job id. It runs in whatever state
the chooser runs in (`job-selection.md` §1). `UNKNOWN` marks a caller reached only
through a vtable slot or from code with no references.

## 2. The two helpers — 134 rows

`travel-states.md` §8 already traces the eating rows and §9 the lair rows. They are
listed here without re-deriving them.

| site | caller | caller state | helper | resume | options | note |
|---|---|---|---|---|---|---|
| `0x00455E1A` | `0x00455DA0` | 190 heeding Call To Arms | 6 | 191 heeding Call To Arms | 0 |  |
| `0x00456092` | `0x00455F30` | 192 heeding Call To Arms | 6 | 192 heeding Call To Arms | 0 |  |
| `0x004561C7` | `0x00456160` | 145 drinking | 6 | 153 dancing | 0x80 |  |
| `0x0045642E` | `0x00456160` | 145 drinking | 6 | 150 gambling | 0x80 |  |
| `0x00456671` | `0x00456500` | 145 (via its handler) | 6 | 148 gambling | 0x80 |  |
| `0x0045679D` | `0x00456690` | 145 (via its handler) | 6 | 146 drinking | 0x80 |  |
| `0x00457445` | `0x00457010` | 155 gambling | 6 | 156 gambling | 0xE8 |  |
| `0x00457CDE` | `0x00457B40` | 139 pit-fighting | 6 | 139 pit-fighting | 0xE8 |  |
| `0x00457F08` | `0x00457D60` | 142 cheering on a fight | 6 | 143 cheering on a fight | 0x80 |  |
| `0x0045858B` | `0x00458510` | 181 attacking rooms | 6 | 180 attacking rooms | 0 |  |
| `0x00458609` | `0x004585C0` | UNKNOWN (`0x00488930`, a vtable slot at `0x0066D538`) | 6 | 267 destroying rooms | 0 |  |
| `0x00458990` | `0x004586B0` | 183 attacking walls | 6 | 184 attacking walls | 0x1 |  |
| `0x00458C4F` | `0x004589C0` | 182 attacking walls | 6 | 184 attacking walls | 0x1 |  |
| `0x00458E7E` | `0x00458DF0` | 204 disarming a Trap | 6 | 205 disarming a Trap | 0 |  |
| `0x004593C7` | `0x00459320` | 72 idle | 6 | 73 idle | 0xE8 |  |
| `0x00459F9B` | `0x00459DE0` | 194 leaving Dungeon | 6 | 195 leaving Dungeon | 0x8 |  |
| `0x0045A0B2` | `0x0045A000` | 195 leaving Dungeon | 6 | 196 leaving Dungeon | 0x6E |  |
| `0x0045A3C4` | `0x0045A290` | job 24 Explore | 6 | 225 exploring | 0x80 |  |
| `0x0045A531` | `0x0045A3E0` | 225 exploring | 6 | 225 exploring | 0x80 |  |
| `0x0045ADA7` | `0x0045AA20` | 200 (twice); `0x004A80E0` | 6 | 1 idle | Imp ? 0x80 : 0 |  |
| `0x0045FB07` | `0x0045F030` | 200 engaging in battle. | 6 | current state (`+0x10`): 200, or 199 when 199 runs this handler | 0 |  |
| `0x004610C1` | `0x00460FE0` | 200 | 6 | 259 celebrating | 0 |  |
| `0x0046175F` | `0x004611D0` | 200, 203, and `0x0045CB00` (combat) | 6 | 266 recuperating | 0xE8 | hero owner |
| `0x00461776` | `0x004611D0` | 200, 203, and `0x0045CB00` (combat) | 6 | 266 recuperating | 0x68 | hero, retry |
| `0x0046178D` | `0x004611D0` | 200, 203, and `0x0045CB00` (combat) | 6 | 202 fleeing | 0xE8 | not hero |
| `0x004617A4` | `0x004611D0` | 200, 203, and `0x0045CB00` (combat) | 6 | 202 fleeing | 0x68 | not hero, retry |
| `0x00461A8B` | `0x00461A10` | 246 idle | 6 | 246 idle | 0x80 |  |
| `0x00461B9A` | `0x00461B50` | 266 recuperating | 6 | 266 recuperating | 0x80 |  |
| `0x004620D4` | `0x00461F00` | 222 advancing | 6 | 222 advancing | 0x80 |  |
| `0x004622FB` | `0x00462130` | 223 advancing | 6 | 223 advancing | 0x80 |  |
| `0x0046246F` | `0x00462310` | job 18 KillCreatures | 6 | 177 attacking Dungeon Heart | 0x41 |  |
| `0x00462544` | `0x00462490` | job 19 KillPlayer | 6 | 178 attacking Dungeon Heart | 0x1 |  |
| `0x004625E2` | `0x00462560` | 177 attacking Dungeon Heart | 6 | 177 attacking Dungeon Heart | 0 |  |
| `0x00462642` | `0x00462560` | 177 attacking Dungeon Heart | 6 | 178 attacking Dungeon Heart | 0 |  |
| `0x00462B81` | `0x00462930` | 97 guarding | 6 | 98 guarding | 0x80 |  |
| `0x004630D1` | `0x00462D00` | 98 guarding | 6 | 98 guarding | 0x80 |  |
| `0x00463265` | `0x00463190` | 98 (via its handler) | 6 | 100 on patrol to a Guard Post | 0x80 |  |
| `0x0046330D` | `0x004632A0` | 100 on patrol to a Guard Post | 6 | 98 guarding | 0x80 | target from `0x004E4D90` (3 args) |
| `0x004636AA` | `0x00463500` | 92 eating | 6 | 93 eating | 0xE8 | eating, travel-states section 8 |
| `0x00463D8C` | `0x00463BE0` | 96 eating | 6 | 96 eating | 0x80 |  |
| `0x004653DD` | `0x004651A0` | imp wrapper (section 3) | 6 | wrapper argument 2 | wrapper argument 3 | the walk half of the imp wrapper |
| `0x0046E8C8` | `0x0046E820` | 273 (no label) | 6 | 273 (no label) | 0 |  |
| `0x0046EA3A` | `0x0046E8E0` | job 27 JailBreak | 6 | 1 idle | 0x41 |  |
| `0x0046F645` | `0x0046F4F0` | 79 looking for the Lair | 6 | 80 making a home | 0xE8 |  |
| `0x0046F7D3` | `0x0046F6B0` | 80 making a home | 6 | 80 making a home | 0xE0 |  |
| `0x0046F8EC` | `0x0046F810` | 81 going to bed | 6 | 82 sleeping | 0xE8 |  |
| `0x004703BB` | `0x00470110` | 63 manufacturing | 6 | 64 manufacturing | 0x80 |  |
| `0x0047040C` | `0x00470110` | 63 manufacturing | 6 | 64 manufacturing | 0x80 |  |
| `0x00472111` | `0x00471EB0` | 173 idle | 6 | 174 idle | 0x1 |  |
| `0x004722E0` | `0x004721C0` | 175 idle | 6 | 175 idle | 0 |  |
| `0x00472645` | `0x00472390` | 187 collecting wages | 6 | 188 collecting wages | 0xE8 |  |
| `0x00472C98` | `0x00472AC0` | 103 praying | 6 | 104 praying | 0x80 |  |
| `0x00472CBE` | `0x00472AC0` | 103 praying | 6 | 104 praying | 0x80 |  |
| `0x004730BE` | `0x00473010` | 106 praying | 6 | 1 idle | 0x80 |  |
| `0x00473331` | `0x004731F0` | 115 being held captive | 6 | 116 being held captive | 0x80 |  |
| `0x004736A3` | `0x00473550` | 117 being held captive | 6 | 118 being held captive | 0x80 |  |
| `0x00473A9E` | `0x004739E0` | 121 freeing comrades | 6 | 121 freeing comrades | 0x6C |  |
| `0x00473AEB` | `0x004739E0` | 121 freeing comrades | 6 | 87 wandering | 0x80 |  |
| `0x00473B38` | `0x004739E0` | 121 freeing comrades | 6 | 121 freeing comrades | 0x6C |  |
| `0x00473C10` | `0x004739E0` | 121 freeing comrades | 6 | 121 freeing comrades | 0x68 |  |
| `0x00473C2D` | `0x004739E0` | 121 freeing comrades | 6 | 121 freeing comrades | 0x6C |  |
| `0x00473D0F` | `0x00473C60` | 122 freeing comrades | 6 | 122 freeing comrades | 0x80 |  |
| `0x00473E5B` | `0x00473DF0` | 123 being held captive | 6 | 124 being held captive | 0x68 |  |
| `0x00473F0F` | `0x00473E80` | 124 being held captive | 6 | 125 being held captive | 0x6C |  |
| `0x004749CD` | `0x00474960` | 239 idle | 6 | 240 idle | 0 |  |
| `0x00474ACA` | `0x00474960` | 239 idle | 6 | 238 idle | 0 |  |
| `0x00475217` | `0x00475170` | job 16 Rebel | 6 | 194 leaving Dungeon | 0 |  |
| `0x0047522E` | `0x00475170` | job 16 Rebel | 6 | 194 leaving Dungeon | 0x2 |  |
| `0x00475448` | `0x004752B0` | 203; `0x0045BDF0` (combat) | 6 | 251 responding to a threat | Imp ? 0x80 : 0 |  |
| `0x00475861` | `0x00475660` | 251 responding to a threat | 6 | 254 reporting danger | Imp ? 0x80 : 0 |  |
| `0x00475C2E` | `0x00475A20` | 53 researching | 6 | 54 researching | 0x80 |  |
| `0x00475C78` | `0x00475A20` | 53 researching | 6 | 54 researching | 0x80 |  |
| `0x00476309` | `0x004762D0` | 59 researching | 6 | 53 researching | 0x80 | target from `0x004E4D90` |
| `0x0047648B` | `0x00476390` | 220 fleeing | 6 | 221 fleeing | 0x80 |  |
| `0x004767CE` | `0x004764A0` | UNKNOWN (`0x00510440`, caller outside any function) | 6 | 1 idle | 0x80 |  |
| `0x00476981` | `0x004764A0` | UNKNOWN (`0x00510440`, caller outside any function) | 6 | 1 idle | 0x80 |  |
| `0x00477565` | `0x004773D0` | 1 idle | 6 | 1 idle | Imp ? 0x80 : 0 |  |
| `0x004779D0` | `0x004778E0` | 89 idle | 6 | 191 heeding Call To Arms | Imp ? 0x80 : 0 |  |
| `0x004787D8` | `0x00478680` | 89 (via its handler) | 6 | 1 idle | 0x80 |  |
| `0x0048088F` | `0x00480750` | 86 wandering | 6 | 86 wandering | 0x80 |  |
| `0x004809EC` | `0x004808E0` | 87 wandering | 6 | 238 if IsHorny, else 1 | 0x80 |  |
| `0x00481939` | `0x004818E0` | 215 idle | 6 | 214 idle | 0 |  |
| `0x004819C0` | `0x00481970` | 219 idle | 6 | 1 idle | 0x80 |  |
| `0x00481C50` | `0x004819E0` | 243 burning in Lava | 6 | 244 burning in Lava | 0 |  |
| `0x0048214C` | `0x00482080` | 170 stealing booty | 6 | 171 stealing booty | 0x68 |  |
| `0x00482227` | `0x00482190` | 164 stealing booty | 6 | 165 stealing booty | 0x68 |  |
| `0x0048247C` | `0x004822F0` | 171 stealing booty | 6 | 172 escaping with stolen booty | 0x68 |  |
| `0x0048262D` | `0x004824E0` | 165 stealing booty | 6 | 169 escaping with stolen booty | 0 |  |
| `0x004827C8` | `0x00482760` | 161 stealing booty | 6 | 162 stealing booty | 0 |  |
| `0x0048295B` | `0x00482880` | 162 stealing booty | 6 | 169 escaping with stolen booty | 0 |  |
| `0x004829F6` | `0x00482880` | 162 stealing booty | 6 | 169 escaping with stolen booty | 0 |  |
| `0x00482B98` | `0x00482B30` | 167 stealing booty | 6 | 168 stealing booty | 0 |  |
| `0x00482D43` | `0x00482C50` | 168 stealing booty | 6 | 169 escaping with stolen booty | 0 |  |
| `0x00483177` | `0x00482F80` | 85 sulking | 6 | 85 sulking | 0xE0 |  |
| `0x0048381B` | `0x00483790` | 274 (no label) | 6 | 275 (no label) | 0x80 |  |
| `0x00483A6B` | `0x004839E0` | 276 (no label) | 6 | 275 (no label) | 0x80 |  |
| `0x00483DA7` | `0x00483B90` | 127 torturing a captive | 6 | 128 torturing a captive | 0x20 |  |
| `0x00483E59` | `0x00483B90` | 127 torturing a captive | 6 | 130 torturing a captive | 0x68 |  |
| `0x00484C49` | `0x00484950` | 134 torturing a captive | 6 | 134 torturing a captive | 0xE8 |  |
| `0x00485532` | `0x00485310` | 108 training | 6 | 111 training | 0x80 |  |
| `0x00485A3A` | `0x004859E0` | 109 training | 6 | 108 training | 0 |  |
| `0x00485CAB` | `0x00485C10` | 186 tunnelling | 6 | `+0xD3`, the errand the tunnel carries | 0 |  |
| `0x00485D28` | `0x00485C10` | 186 tunnelling | 6 | `+0xD3` | 0 |  |
| `0x00485D6A` | `0x00485C10` | 186 tunnelling | 6 | 186 tunnelling | 0 |  |
| `0x00485EC4` | `0x00485C10` | 186 tunnelling | 6 | 186 tunnelling | 0 |  |
| `0x00486DBA` | `0x00486CF0` | 176 idle | 6 | 176 idle | 0x80 |  |
| `0x00489E17` | `0x00488930` | UNKNOWN (vtable slot at `0x0066D538`) | 6 | 221 fleeing | Imp ? 0x80 : 0 |  |
| `0x004A8A06` | `0x004A84E0` | UNKNOWN (vtable slot at `0x0066D768`) | 6 | 254 reporting danger | Imp ? 0x80 : 0 |  |
| `0x004D9A1A` | `0x004D9690` | the Dungeon Heart room's per-tick update `0x004D9A60`, reached through slot `+0x15` of the room handler table (`0x006A1F81`, record 5; `portals-room-handler-table.md`) | 6 | 254 reporting danger | Imp ? 0x80 : 0 |  |
| `0x004E2F69` | `0x004E2E70` | 122; `0x004E2C30`, `0x004E2E60` | 6 | 121 freeing comrades | 0x6C |  |
| `0x004E307C` | `0x004E2E70` | 122; `0x004E2C30`, `0x004E2E60` | 6 | 121 freeing comrades | 0x68 |  |
| `0x004E309E` | `0x004E2E70` | 122; `0x004E2C30`, `0x004E2E60` | 6 | 121 freeing comrades | 0x6C |  |
| `0x0045613D` | `0x004560F0` | job 9 Drink; state 189 | 7 | 145 drinking | 0x80 |  |
| `0x00457853` | `0x00457780` | job 26 CombatPitSpectate; 142 | 7 | 142 cheering on a fight | 0x80 |  |
| `0x00458401` | `0x00458370` | 179 attacking rooms | 7 | 180 attacking rooms | 0x61 if room `+0x18` is 2, else 1 |  |
| `0x004628AF` | `0x00462850` | job 6 Guard | 7 | 97 guarding | 0x80 |  |
| `0x004628F9` | `0x00462850` | job 6 Guard | 7 | 97 guarding | 0x80 |  |
| `0x004634E3` | `0x004634B0` | 91 eating | 7 | 92 eating | 0x80 | eating, travel-states section 8 |
| `0x00463C2E` | `0x00463BE0` | 96 eating | 7 | 96 eating | 0x80 |  |
| `0x0046F422` | `0x0046F390` | lair attempt: job 1, the unhappy and angry choosers, states 80, 89, 193, 280 | 7 | 80 making a home | 0xE8 | travel-states section 9 |
| `0x0046F4B0` | `0x0046F440` | job 1 Sleep | 7 | 81 going to bed | 0xE0 | travel-states section 9 |
| `0x004700C0` | `0x00470080` | job 5 Manufacture | 7 | 63 manufacturing | 0x80 |  |
| `0x00472913` | `0x004728E0` | job 8 Praying | 7 | 101 praying | 0x80 |  |
| `0x00472D0E` | `0x00472AC0` | 103 praying | 7 | 101 praying | 0x80 |  |
| `0x00475A05` | `0x004759D0` | job 3 Research | 7 | 53 researching | 0x80 |  |
| `0x00481F61` | `0x00481EF0` | job 25 StealEnemyGold | 7 | 170 stealing booty | 0x69 |  |
| `0x00482058` | `0x00482010` | 163 stealing booty | 7 | 164 stealing booty | 0x69 |  |
| `0x00482418` | `0x004822F0` | 171 stealing booty | 7 | 172 escaping with stolen booty | 0x69 |  |
| `0x00482748` | `0x00482700` | 160 stealing booty | 7 | 161 stealing booty | 0x1 |  |
| `0x00482B18` | `0x00482AD0` | 166 stealing booty | 7 | 167 stealing booty | 0x1 |  |
| `0x00482F40` | `0x00482EF0` | job 15 Sulk | 7 | 85 sulking | 0xE0 |  |
| `0x00483B27` | `0x00483AE0` | job 7 Torture | 7 | 127 torturing a captive | 0xE0 |  |
| `0x00483B69` | `0x00483AE0` | job 7 Torture | 7 | 127 torturing a captive | 0x80 |  |
| `0x004852BF` | `0x00485240` | job 4 Train | 7 | 108 training | 0x80 |  |

## 3. The Imp's wrapper `0x004651A0(position*, resume, options, noTeleport)` — CONFIRMED

`RET 0x10`, four stack arguments, 45 call sites. Every imp task handler walks through
it. Only one of the 134 helper rows is the Imp's own: `0x004653DD`, the wrapper's
walk half.

1. It walks three **ability slots** of the creature's definition
   (`0x004943F0(slot)`, slot 0–2). A slot is skipped when its byte `+1` exceeds
   the creature's byte `+0x403`, or when its id `+0` is 0. Otherwise the slot's
   record is looked up and must pass `0x0048C800`, and then its flag dword `+0x27`
   is read. Bit `0x20` selects the slot and ends the loop. Bit `0x40` instead
   stamps the slot's time at `creature+0x41B + 4·slot`, calls `0x0048CAD0` with
   the record's `+0x1F`/`+0x23`, and copies `+0x1F` to `creature+0x4DD` when it
   exceeds 100.
2. **Teleport:** requires a selected slot, `noTeleport` 0, the target more than two
   tiles away on either axis, and `0x00471630(target, options)` accepting. Then:
   `+0xDF` = resume, `SetState(1)`, `0x0048DFA0(0)`, `0x0048DF10(0)`, the slot's
   time stamped, action 60 at the target (`0x00496980`), and `SetState(51)`,
   *teleporting*. It then calls the world method `+0x38C` with `0x141` and a
   second method with `0x343`, and returns 1. State 52's handler resumes through
   `+0xDF` (`travel-states.md` §2.4). A refused `SetState(1)` falls through to
   step 3; a refused action or `SetState(51)` returns 0.
3. **Otherwise** it calls `0x00471890(position, resume, options)` and returns that
   result.

That the selected slot is the Imp's `ImpTeleport` ability is STRONGLY INFERRED,
from the manual ("veterans get Teleport") and the wrapper's shape. The bits of
`+0x27` are unread, and this project has no imp teleport (Q418).

**`noTeleport` is 1** on every carry leg, so an imp carrying a body, an object or a
crate walks. The one exception is state 41's re-route (`0x00467CB6`), which passes
0. It is 0 on every walk to work, to gold, to a crate and to a treasury.

**The table, 45 rows.** "10 (the selection)" means the kind handler is reached from
state 10's handler `0x00463DF0`, the imp's selection (`imp-task-priority.md` §4):
Tier A through `0x00468520`, Tier B and the dropped-imp scan through the two chains.
The claim resume is chosen inside kind 3's handler: 15 for plain path
(`0x0046AC86`), 19 for enemy floor (`0x0046ACB0`), 17 for an enemy room tile
(`0x0046ACDE`), as `imp-task-priority.md` §3 gives the three flavours.

| site | caller | caller state | resume | options | no-teleport | note |
|---|---|---|---|---|---|---|
| `0x004647A4` | `0x00464400` | 10 (hands full), 26, 27, 76 | 28 taking Gold to the Treasury. | 0xE8 | 0 | deposit gold |
| `0x00464B82` | `0x00464A80` | 76 | 34 installing a Trap | 0xE8 | 0 | trap/door crate |
| `0x00464C4A` | `0x00464A80` | 76 | 36 installing a Door | 0xE8 | 0 | trap/door crate |
| `0x00464DF2` | `0x00464A80` | 76 | 34 installing a Trap | 0xE8 | 0 | trap/door crate |
| `0x00464F52` | `0x00464A80` | 76 | 36 installing a Door | 0xE8 | 0 | trap/door crate |
| `0x004670F6` | `0x00467040` | 34 installing a Trap | 35 installing a Trap | 0xE8 | 1 |  |
| `0x004673A5` | `0x004672F0` | 36 installing a Door | 37 installing a Door | 0xE8 | 1 |  |
| `0x00467A1C` | `0x004678A0` | 40 collecting corpses | 41 collecting corpses | 0xE8 | 1 |  |
| `0x00467CB6` | `0x00467AA0` | 41 collecting corpses | 41 collecting corpses | 0xE8 | 0 |  |
| `0x00467F02` | `0x00467DD0` | 43 taking prisoners | 44 taking prisoners | 0xE4 | 1 |  |
| `0x00467F63` | `0x00467DD0` | 43 taking prisoners | 43 taking prisoners | 0xE0 | 1 |  |
| `0x00467FC1` | `0x00467DD0` | 43 taking prisoners | 43 taking prisoners | 0xE0 | 1 |  |
| `0x00468139` | `0x00468010` | 44 taking prisoners | 46 taking prisoners | 0xEC | 1 |  |
| `0x00468195` | `0x00468010` | 44 taking prisoners | 43 taking prisoners | 0xE0 | 1 |  |
| `0x00468218` | `0x00468010` | 44 taking prisoners | 47 taking prisoners | 0xEC | 1 |  |
| `0x004682F4` | `0x00468270` | 47 taking prisoners | 48 taking prisoners | 0xEC | 1 |  |
| `0x00468A95` | `0x00468910` | 10 (via kind 8) | 34 installing a Trap | 0xE8 | 0 | crate branch of kind 8 |
| `0x00468C89` | `0x00468910` | 10 (via kind 8) | 36 installing a Door | 0xE8 | 0 | crate branch of kind 8 |
| `0x0046990F` | `0x00469600` | UNKNOWN: only via the kind switch `0x004687C0` ← `0x00464170` ← `0x00488930` | 15 claiming territory | 0xE0 | 0 | kind 14 |
| `0x00469A85` | `0x00469600` | UNKNOWN: only via the kind switch `0x004687C0` ← `0x00464170` ← `0x00488930` | 15 claiming territory | 0xE0 | 0 | kind 14 |
| `0x0046A454` | `0x00469AF0` | 10 (the selection) | 13 digging | 0xE8 | 0 | kinds 1/2 (task list) |
| `0x0046A4CE` | `0x00469AF0` | 10 (the selection) | 25 mining | 0xE8 | 0 | kinds 1/2 (task list) |
| `0x0046AA1A` | `0x0046A550` | 10 (the selection) | 13 digging | 0xE8 | 0 | kinds 1/2 (per tile) |
| `0x0046AA3A` | `0x0046A550` | 10 (the selection) | 25 mining | 0xE8 | 0 | kinds 1/2 (per tile) |
| `0x0046AD0E` | `0x0046AB50` | 10 (the selection) | 15, 19 or 17 | 0xE0 | 0 | kind 3: plain path / enemy floor / enemy room |
| `0x0046AE26` | `0x0046AB50` | 10 (the selection) | 15, 19 or 17 | 0xE0 | 0 | kind 3, second route |
| `0x0046B093` | `0x0046AEA0` | 10 (the selection) | 21 reinforcing walls | 0xE8 | 0 | kinds 6/16 |
| `0x0046B1BD` | `0x0046AEA0` | 10 (the selection) | 21 reinforcing walls | 0xE8 | 0 | kinds 6/16 |
| `0x0046B3A2` | `0x0046AEA0` | 10 (the selection) | 23 repairing walls | 0xE8 | 0 | kinds 6/16 |
| `0x0046B4CC` | `0x0046AEA0` | 10 (the selection) | 23 repairing walls | 0xE8 | 0 | kinds 6/16 |
| `0x0046B920` | `0x0046B6D0` | 10 (the selection) | 27 collecting Gold | 0xE8 | 0 | kind 7 |
| `0x0046BCC6` | `0x0046B9C0` | 10 (the selection) | 30 collecting an item | 0xE8 | 0 | kind 8 |
| `0x0046BED8` | `0x0046BD70` | 10 (the selection) | 34 installing a Trap | 0xE8 | 0 | kinds 9/10 (whole map) |
| `0x0046C025` | `0x0046BD70` | 10 (the selection) | 36 installing a Door | 0xE8 | 0 | kinds 9/10 (whole map) |
| `0x0046C3D1` | `0x0046C1A0` | 10 (the selection) | 34 installing a Trap | 0xE8 | 0 | kinds 9/10 (per tile) |
| `0x0046C5F2` | `0x0046C1A0` | 10 (the selection) | 36 installing a Door | 0xE8 | 0 | kinds 9/10 (per tile) |
| `0x0046CB10` | `0x0046C720` | 10 (the selection) | 38 digging | 0xE8 | 0 | kind 11 (whole map) |
| `0x0046CE9C` | `0x0046CB80` | 10 (the selection) | 38 digging | 0xE8 | 0 | kind 11 (per tile) |
| `0x0046D2B0` | `0x0046CF40` | 10 (the selection) | 40 collecting corpses | 0xE8 | 0 | kind 12 (per tile) |
| `0x0046D6FC` | `0x0046D3C0` | 10 (the selection) | 40 collecting corpses | 0xE8 | 0 | kind 12 (whole map) |
| `0x0046DC2C` | `0x0046D810` | 10 (the selection) | 42 rescuing wounded creatures | 0xE8 | 0 | kind 13 |
| `0x0046DF59` | `0x0046D810` | 10 (the selection) | 42 rescuing wounded creatures | 0xE8 | 0 | kind 13 |
| `0x0046E278` | `0x0046D810` | 10 (the selection) | 42 rescuing wounded creatures | 0xE8 | 0 | kind 13 |
| `0x0046E5AB` | `0x0046E480` | 30, 31, 32, 33 | 31, 33 or 32 | 0xE8 | 1 | spell book / special / other object (`0x1F`/`0x21`/`0x20`) |
| `0x0046E7A0` | `0x0046E610` | 42, 45 | 45 or 43 | 0xE0 | 1 | shared tail: `0x0046E738` pushes 45 (Lair); `0x0046E790` pushes 43 after a room-type-11 (Prison) search |

## 4. The eight activities the next slices retire — CONFIRMED (sites)

"Entered from" says which code makes the call. A **task** row is an imp task kind's
handler, reached from state 10's selection. It is not `0x006E4198`, which is the
creature action table (`creature-action-table.md`, Q338). A **state** row is a
state handler. Every imp row goes through §3's wrapper, so each walk is state 6, or
a teleport (51 → 52) when §3 step 2 applies.

| our activity | resume state(s) | helper | entered from | sites |
|---|---|---|---|---|
| `MovingToJob` | by kind: **13** dig (1), **25** mine (2), **15 / 19 / 17** claim (3; 14 takes 15), **21** reinforce (6), **23** fix (16), **38** kind 11 | wrapper → 6 | **task**, state 10; kind 14's own handler only from `0x00488930` (§5) | `0x0046A454`–`0x0046AA3A`, `0x0046AD0E`, `0x0046AE26`, `0x0046B093`–`0x0046B4CC`, `0x0046CB10`, `0x0046CE9C`, `0x0046990F`, `0x00469A85` |
| `FetchingGold` | **27** collecting Gold | wrapper → 6 | **task** (kind 7, Tier B), state 10 | `0x0046B920` |
| `DeliveringGold` | **28** taking Gold to the Treasury | wrapper → **6**, a point, not a room | **both**: the task selection when the hands are full (state 10), and states **26** (mining), **27** (just picked up) and **76** | `0x004647A4` |
| `FetchingBody` | **40** a corpse (kind 12), **42** an unconscious creature (kind 13) | wrapper → 6 | **task**, state 10 | `0x0046D2B0`, `0x0046D6FC`; `0x0046DC2C`, `0x0046DF59`, `0x0046E278` |
| `CarryingBody` | **41** corpse to the Graveyard; **45** own creature to the Lair; **43** enemy to the Prison | wrapper → 6, no teleport except state 41's re-route | **state**: 40 (→ 41), 41 (re-route); 42 and 45 via `0x0046E610` | `0x00467A1C`, `0x00467CB6`, `0x0046E7A0` |
| `FetchingObject` | **30** collecting an item (kind 8); its crate branch **34** / **36** for a placed trap / door | wrapper → 6 | **task** (kind 8), state 10 | `0x0046BCC6`; `0x00468A95`, `0x00468C89` |
| `CarryingObject` | **31** a spell book, **33** a special, **32** anything else | wrapper → 6, no teleport | **state**: 30–33, via `0x0046E480` | `0x0046E5AB` |
| `SeekingWages` | **188** collecting wages | **position helper directly** | **state** 187 (`payday.md` §4.2) | `0x00472645` |

Five things a slice needs:

- **The gold and wage walks target a point, not a room.** Both `DeliveringGold` and
  `SeekingWages` use state 6. State 6 does not check the room on arrival, as
  state 7 does (`travel-states.md` §5); the checks sit in the resume states (188
  re-tests the Treasury, `payday.md` §4.3).
- **`SeekingWages` never teleports**, even for an Imp: 187 calls the helper
  directly, not the wrapper.
- **The body chain continues past the first leg.** 43 → 44 or 43, 44 → 46, 43 or 47,
  and 47 → 48, all "taking prisoners" (options `0xE0`–`0xEC`, no teleport). Which
  leg is the walk to the Prison and which is the drop is not read here, so a
  `CarryingBody` slice should take the first resume (43 / 45 / 41) and leave the
  rest to a reading of states 43–48.
- **Traps and doors are a ninth imp walk** (34 / 36 from kinds 9 / 10 and from state
  76, then 34 → 35 and 36 → 37), and none of our activities names it.
- **No category hook runs on an imp walk, except from state 76.** Every imp caller
  state and every imp resume above is category 3, so the travel state's arrival
  commits with no hooks (`travel-states.md` §3). State 76 is category 7, so its
  deposit and crate walks run category 7's leave hook and category 3's enter hook
  on a walk's arrival.

## 5. What this table does not settle

- **Eight caller-state cells are `UNKNOWN`.** Five of them (`0x00458609`,
  `0x00489E17`, `0x004A8A06`, and kind 14's `0x0046990F`/`0x00469A85` through
  `0x00464170`) are reached from `0x00488930` or `0x004A84E0`, methods in vtables at
  `0x0066D538`/`0x0066D768`. One (`0x004D9A1A`) is in a function with no
  references. Two (`0x004767CE`/`0x00476981`) are reached from code outside any
  function. The resume and option values in these rows are read; only the
  creature's state at the call is not. Reading the vtables' owners settles them.
- **Resume values chosen at run time** are given as their sources: the tunnel's
  `+0xD3` (the errand, `travel-states.md` §2.3), the fight's own state at
  `0x0045FB07`, the Horny test at `0x004809EC`, and the three claim flavours. None
  depends on data outside the binary.
- **Option bits other than bit 0** are passed to `0x004D0F60` and not decoded here.
  The words seen are `0`, `1`, `2`, `8`, `0x20`, `0x41`, `0x61`, `0x68`, `0x69`,
  `0x6C`, `0x6E`, `0x80`, `0xE0`, `0xE4`, `0xE8`, `0xEC`, plus the two computed forms.

## Implementation and tests

Nothing built; research only. Q418 records the teleport gap. The retirement slices
(ADR-0135 step two) copy the resume column above.
