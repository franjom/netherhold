# Imp walks: how a job walk lands exactly on its point

**Source:** `DKII.EXE` v1.70 (GOG), read-only pyghidra-mcp and headless Ghidra 12.1.4 on
Claude's clone, 2026-09-30. Decompiler output stayed in the conversation and the scratchpad
(ADR-0046); every fact below is in our words with its address. Capture
`level1-live1/seed01`, used only to check what the binary says.

**Status: CONFIRMED (binary + capture). Nothing built; the last section says what to change.**

Q490. Continues `imp-task-objects-and-walks.md` §4, which gives the three ways a walk ends
and left open how the 13-tick claim lands exactly on the tile centre. Uses the body layout
and names of `steering-behaviours.md` §4–§7.

## What was read


Read 2026-09-30: the state-level route loader `0x004D0F60`, the integrator `0x004D4760`
(`Steering_Integrate`, run from `0x004D5A40`), `Follow_Path` `0x004D3C40`, the clamp
`0x004D4F60` and the body setter `0x004D59A0`; checked against every claim walk in
`level1-live1/seed01` (body at `creature+0x124`: position `+0x84`, velocity `+0x8C`,
`max_speed` `+0xE2`, route count `+0x10C`, flags `+0x10D`).

## 1. The option word becomes `path_flags` (`0x004D0F60`)

Arguments: the creature, the
target, the word. In order it:

1. puts option bits 1 and 2 into `creature+0x3E6` bits 17 and 18 and option bit 7 into
   bit 19 (the door globals, `steering-behaviours.md` §7.6), clearing the three first;
2. sets the navigation globals from the creature: `0x006EC9D0` the owner's compact number
   (`+0x10FC`), `0x006EC9D4`/`D8`/`E0` the three bits just stored, `0x006EC9DC` definition
   `+0x686` bit 5 and `0x006EC9E4` definition `+0x686` bit 4 (the last is not among the
   globals `0x004D5A40` sets);
3. writes the entity's position into the body and **zeroes the body's velocity**
   (`0x004D59A0(x, y, 0, 0)`, each shifted up four);
4. builds the flags byte: option bit 3 gives flag bit 0, option bit 4 flag bit 1, option bit
   5 **or** bit 6 flag bits 2 and 3 together; option bit 0 is not read here;
5. requests the route through `0x004D59E0`, which shifts the target up four and calls
   `0x004D5100`, where the byte is stored at body `+0x10D` (`steering-behaviours.md` §7).

Our names for the flag bits: bit 0 `TightArrival` (the creature arm's arrival radius; the imp
arm never reads it), bit 1 no final deceleration (creature arm), bit 2 `SnapsOnArrival`
(below), bit 3 Separation's half-tile stop. So `0xE8` gives `0xD` and `0xE0` gives `0xC`.
Every job walk an imp handler starts through `0x004651A0` passes `0xE8`, `0xE0`, `0xE4` or
`0xEC` (`travel-state-call-sites.md` §3), all with bit 2; claims (kind 3, `0x0046AD0E`,
`0x0046AE26`) pass `0xE0`. In `seed01` the flags byte reads `0xC` on every state-6 frame of
the 89 claim walks except the first frame of three (`0xD`, left by the previous request), and
0 again on the landing frame (the stop routine's own-position request with word 0,
`0x004D1070`).

## 2. The snap (`0x004D4760`)

After `vel` is clamped to `max_speed` and `next = pos + vel`
is formed, and before the wall clamp: when `path_flags` has bit 2, the window index (`+0x10E`
bits 3–5) equals the route count minus one, and `octile(subpath[idx] - pos) < octile(vel)` —
`pos` the position **before** this tick's move, `vel` the **new** velocity, both octiles on
the raw 16.16 components, strictly less — `next` becomes `subpath[idx]` and both velocity
components are set to zero. The wall clamp then runs on that `next` like any other step. The
test does not read `path_finished`.

## 3. Which tick

The imp arm of `Follow_Path` runs earlier in the same integration. On the
last point it aims at `path_target_pos`; if the body is within `0x1999` (octile) it sets
`path_finished` **and still returns the seek force** `(aim - pos) x (1.825 - crowd) - vel`
that tick (after the turn test). The brake `-vel` (`0x004D4550` negates) is returned only
when `path_finished` was **already** set on entry, when there is no route, or while the imp
is still turning. So:

- a 3-frame claim: the second move leaves 0.076 tile; on the third tick `Follow_Path` sets
  `path_finished` and seeks, the new velocity (about 0.35 after the 0.35 weight) exceeds
  0.076, and the snap puts the body on the point with zero velocity **in the same tick**;
  state 6 then reads 1.0 and resumes;
- a 2-frame claim (a fast imp, `max_speed` 0.69): after the first move 0.32–0.47 tile is
  left, more than 0.1, so `path_finished` is not set, but the new velocity exceeds what is
  left, so the snap lands the body exactly; the remaining distance is then zero and the
  progress is 1.0 by the third case of `imp-task-objects-and-walks.md` §4;
- an off-point end: when the last move cannot cover what is left (the speed boost has run
  out, or 0.48–0.49 tile is left at `max_speed` 0.46), there is no snap; the move stops
  within 1/16 tile and the truncated progress ends the walk off the point.

## 4. Check against `seed01`

89 state-6 walks resume into 15, 17 or 19. **85 end with the body
exactly on the target and zero velocity; 4 end off it**, 0.014–0.054 tile short (0 whole
sixteenths), after a tick that began 0.475–0.490 tile away. Three walks by three imps, body
position by frame (the last row is the landing, in state 15):

| imp | frame | state | x | y | velocity |
|---|---|---|---|---|---|
| 455 | 216 | 6 | 14.5000 | 24.5000 | 0 |
| | 217 | 6 | 14.5000 | 24.0383 | (0, -0.4617) |
| | 218 | 6 | 14.5000 | 23.5770 | (0, -0.4613) |
| | **219** | 15 | 14.5000 | **23.5000** | 0 |
| 452 | 709 | 6 | 14.5000 | 18.5000 | 0 |
| | 710 | 6 | 14.9615 | 18.5000 | (0.4615, 0) |
| | 711 | 6 | 15.4238 | 18.5000 | (0.4623, 0) |
| | **712** | 15 | **15.5000** | 18.5000 | 0 |
| 454 | 858 | 6 | 18.5000 | 28.5000 | 0 |
| | 859 | 6 | 18.9615 | 28.5000 | (0.4615, 0) |
| | 860 | 6 | 19.4238 | 28.5000 | (0.4623, 0) |
| | **861** | 15 | **19.5000** | 28.5000 | 0 |

Each lands on its third walk frame, exactly on the centre. The off-point counter-example is
imp 452 at 1534–1536: 14.5, then 13.9746 (`max_speed` 0.69, velocity 0.5254: the 0.35 weight
times the 1.5 force clamp), then 13.5144 with `max_speed` back at 0.46 on the landing frame:
0.4746 left against a step of at most about 0.46, so no snap. A recorded speed of
0.4615–0.4623 against a `max_speed` of 0.4600 is the three-band length in `0x004D4F60`:
between 1/16 and 64 tiles it roots the components shifted down 8, so it undershoots and the
clamp lets slightly more through. `Fixed16.Truncate` already uses the same length.

## 5. Why ours overshoots

Our integrator and imp arm already do what is above:
`SteeringIntegrator.Step` has the snap with the same test, and `FollowPathRules`'s imp arm
sets `PathFinished` and still seeks, braking only when it was set on entry. The snap never
fires because every imp job walk in ours requests its route with `MoveOptions.Plain` (0), so
`PathFlags` bit 2 is clear (`ImpTaskSystem.cs` lines 530, 787, 793, 1534, 1539, 1543, 1975,
2174, 2472, and `ImpTaskSystem.Crates.cs` 171). The trace 5.23, 5.69, 5.88, 5.53 is the
unsnapped seek: 0.27 tile left at 0.46 a tick carries the imp 0.19 past the point, and the
seek swings it back.

## 6. What must change

For the builder; nothing in `src/` is changed by this note.

- `SteeringIntegrator`: nothing. It already tests `SnapsOnArrival`, `OnLastPoint` and
  `octile(CurrentPoint - Pos) < octile(Vel)` on the pre-move position and the clamped
  velocity, zeroes `Vel`, and runs before the wall clamp.
- `FollowPathRules`: nothing. "Set `PathFinished` and still seek this tick; brake with `-Vel`
  from the next" is DK2's. The entry brake never fights the snap, which has already zeroed
  the velocity.
- The request sites: an imp's job walk passes its handler's word — `0xE0` for claims and for
  taking prisoners, `0xE8` for digging, mining, reinforcing, repairing, gold, objects,
  crates, traps, doors, corpses and rescue, `0xE4`/`0xEC` for the prisoner legs, as
  `travel-state-call-sites.md` §3 lists. `Plain` stays for states 8 and 9 and the other
  word-0 sites. The door bits these words carry are inert until doors enter the navigation
  byte, so the visible changes are the snap and Separation's bit 3.
- `WorldSteering.TryRouteTo` and `TryRouteToStance`: `0x004D0F60` zeroes the body's velocity
  on every state-level request, found or not; ours does too since ADR-0513. An imp coming
  from state 10 already has zero velocity, so claims do not show it; a request made while
  moving does.
- `0x004D0F60` has no "already on the target's tile" shortcut, where ours returns a finished
  route. Nor does the query: within one triangle `0x004CF490` returns the target as a one-point
  route (2026-10-06), so the body walks to it. Ours does so for stances and, since ADR-0388, the
  fight approach; whether DK2's other callers skip the request when already there is Q565.

The prediction the build should meet: with the snap, a neighbouring claim is 13 ticks (3 walk
frames), 12 when the imp is fast, as in the table of `imp-task-objects-and-walks.md` §4
(41 and 8 of 49).
