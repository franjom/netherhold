# Format and engine: how DK2 plays a KMF animation

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra (pyghidra-mcp), 2026-10-05 and 2026-10-10
- `keeper-spell-tremor.md` § Animation clock, for the simulation-side clip clock

Original version: DK2 v1.70 (GOG)

Presentation only: nothing here changes a rule. It decides how the asset
pipeline (`src/DK2.Convert`) turns an animated KMF into a clip.

## 1. The key byte is a key-frame time — CONFIRMED

Each `GEOM` entry is a packed position followed by one byte. The engine's
vertex evaluator `0x0057E5B0` takes a float frame `f` and its integer part `i`:

1. `g = ITAB[i >> 7][index] + VGEO[index][i]` — the geometry entry, as
   `KmfReader` already decodes it.
2. It reads the positions of entries `g` **and `g + 1`**, and their bytes
   `kA` and `kB`.
3. With `base = (i >> 7)·128`, `frameA = base + kA`, it computes
   `t = (f − frameA) · 1/((base + kB) − frameA)`, the reciprocal coming from the
   table at `0x007810A0` (`1/n`, filled by `0x0058F480`).
4. The vertex is `pos[g] + (pos[g+1] − pos[g])·t`.

So the byte is the frame, within the current 128-frame block, at which that
entry is exact, and the engine **interpolates linearly** between consecutive
key entries. A file need store only key poses. The byte does not change the
playback rate.

## 2. The frame is continuous — CONFIRMED

The mesh drawer `0x00577F10` reads the instance's float time at `+0x44`,
clamps it to [0, 1] (or wraps it into [0, 1] when the mesh's loop flag, mesh
`+0x1C` bit 0, is set), sets `f = (frames − 1)·time` and
`i = floor(f)` clamped to [0, frames − 2], and evaluates every vertex at `f`.
One pass of the time covers frame 0 to the last frame.
Wrapping subtracts 1 only while time is greater than 1: exact loop endpoints
retain the last frame (rechecked 2026-10-10).

## 3. The rate

For a creature art clip, the simulation advances a 12.12 phase each turn by a
step derived from the art descriptor's FPS and frame count (`0x004B41E0`,
`0x004B3C10`; `keeper-spell-tremor.md` § Animation clock). One pass takes
4096/step = duration/1024 = 4·frames/FPS turns, which at 4 turns a second is
nominally **frames/FPS seconds**; the integer phase step quantizes this rate.

CONFIRMED on 2026-10-10: ordinary type-6 drawing is `0x0059B880` to
`0x0059C880`. It reads previous/current phase through `0x00496960` and
`0x0043D730`, divides by 4096, unwraps across animation segments and blends
with global `0x00760B7C`. `0x00575A00` writes that global from its caller's
frame interpolation argument. `0x00583120` interpolates position, turns and
phase into the 0x99 draw object, storing phase at `+0x38`.

CONFIRMED (2026-10-11): the constructor ends in `0x00585ED0`, which converts
that phase directly to continuous frame at draw `+0x64` and integer lookup at
`+0x68`, using the same clamp/wrap and frame-count formula as §2. The part
drawer `0x005836A0` evaluates that frame through `0x0057E5B0`; no secondary
sprite-instance transfer is needed. `0x00598880` supplies input dword `+4`
divided by 256 to the render factor's writer. The producer and its smoothed,
quantized clock are confirmed in animation-transitions.md §3.

Segment resources are separate meshes: `0x004B44E0` chooses compact art
`+0x13` for start, `+0xF` for main, `+0x17` for end. `0x00492320` loads flagged
segments using base name plus `Start`/`End`. The draw interpolates segment
number plus phase and keeps the previous segment at an exact boundary.
The secondary dispatcher `0x0059B0C0` uses the simple-instance path
`0x00579CF0`/`0x00578530`; it is not the ordinary phase interpolation path.

## 4. What this means for ours

Built (ADR-0390):

- `KmfReader` keeps the key byte and evaluates each integer frame as §1 does,
  the last through frame − 1's lookup as §2's clamp has it. Over every shipped
  KMF, 12.05 M of 28.4 M vertex lookups fall between keys and so move; equal
  keys, a key that runs backwards and a last entry with no `g + 1` occur only
  on the key's own frame, where the numerator of `t` is 0.
- `GlbWriter` samples the frames `LINEAR` and spaces them so one pass lasts the
  art descriptor's frame count over its FPS (`+0x44`, `+0x48`, type 6) from
  frame 0 to the last (§2, §3). That count differs from the KMF's in 300 of 906
  creature clips. A KMF with no descriptor still plays at an assumed 30 fps.
  The phase-to-time link is established in §3.

Initial classic playback (ADR-0431): `RenderAnimation` interpolates adjacent
decoded frames into each instance's reusable buffer. Each clip retains its
own topology, materials and UVs; this does not require fitting a second UV
family onto a modern rig (Q443). Unity uploads positions to an instance mesh.
`RenderAnimationClock` advances the ordinary integer phase and its start/main/end
segments, and Unity uses each segment's own mesh/UVs. Playback starts on clip
changes with the same-turn first advance. Ordinary movement rate changes are
built as §6 describes. Queued landing changes, frozen holds, stop-loop flags and
possessed displacement rates are built; see animation-transitions.md. Meal,
combat and training actions restart from their entry stamps. See §5 for the
built choices; the complete status/action chooser remains a broader engine
boundary. Actual Unity sampling/upload and standalone measurements are in
tools/classic-unity-probe/README.md; shared arrays allocate nothing per sample.

The Release probe on this machine (200 instances, 300 renders, 2026-10-10) measured
sampler-only cost: Imp 0.121 ms, Giant 0.132 ms, Salamander 0.125 ms, Mistress
0.196 ms per render, with zero measured sampling allocations. Added shared position
arrays were 50,544/165,816/186,984/289,296 bytes respectively. These figures exclude
the provider's retained decoded KMF frames and Unity conversion/upload/bounds work.

## 5. Classic action selection

CONFIRMED (binary, 2026-10-11): `0x0048BE50` chooses movement slots 1/2 for
walk/run modes 0/1. A current action overrides the movement slot through
`0x00496DE0`, which reads the action-table art byte. `0x004950C0` installs
actions 4/5 with slot 13 (claim), 7/8 with slot 12 (reinforce/repair).
Catalogue indices are those slots minus one. The shipped Imp has no slot 2;
the existing resolver falls back to slot 1. The classic art map now follows
these choices instead of using dig for every Imp work activity.

Hatchery eating plays resolved art 13 during the persistent action-26 wait,
restarted from the state-entry stamp (ADR-0432). Its timing evidence is eating.md §7.
The owner's eating comparison remains outstanding; prison eating is still static.

Prisoner routes now select walk/run and the body's movement rate even though
their activity remains Imprisoned (2026-10-11). State 116 retains its route while
holding the prisoner state; see prison.md §6. The ordinary movement selection
in `0x0048BE50` uses velocity and movement mode, not our activity label.
Standing prisoners and their action flourishes remain outside this mapping.
Travel states 6/7 also play movement when their resumed job labels the activity
AtWork or PitFighting. This covers Level8's waiting Hatchery Guard, hero waypoint
roaming, Guard Post patrol and Combat Pit approach; standing job actions remain
outside this addition.
Walks resuming idle also select movement playback, including Level8's Imp,
Mistress, Salamander and Troll around the Heart. Travel states 6/7 carry the
physical movement even when the resumed activity is Idle.

## 6. Movement rates — CONFIRMED (binary, 2026-10-11)

For requested slots 1/2, `0x0048BE50` gets the body's velocity through
`0x004D5AF0`, converting 16.16 to 12.12 with arithmetic shifts by four.
Its speed approximation is `abs(x) + abs(y) - (min(abs(x), abs(y)) >> 1)`.
It advances through `0x004B3AD0`, then recalculates the next rate through
`0x004B41E0`. Forced-speed and status overrides remain outside this implementation.

The movement scale is compact descriptor word +0, copied from file word
+0x4C by `0x00504D80`. The rate setter divides that word by the tick rate
in 12.12, divides current speed by that result, multiplies the nominal
unclamped rate by that ratio, shifts down twelve, then caps at 4095.
A zero authored scale makes the rate zero; a nonzero scale whose per-tick
value truncates to zero leaves the nominal rate unchanged.

Fresh installation in `0x004B37D0` sets the requested slot before computing
its rate. Ordinary movement's caller passes scalar 4096, so the first
advance uses that scalar, not yet the actual velocity. Later turns use
the velocity after their advance. The tests preserve both details.

## 7. Melee and Chicken playback

CONFIRMED (binary re-read, 2026-10-11): the action-table initializer
`0x004950C0` gives action 17 art slot 5 and mode 3. The getter `0x00496DE0`
passes that slot to the chooser; catalogue index 4 is the melee resource.
`0x00496AA0` marks the handler complete when the blow routine returns zero,
but holds the action until the animation also completes. A damage-pending
flag alone is therefore insufficient to describe visible melee playback.

The classic view uses the melee start stamp and authored action duration to
keep a nonlooping swing visible after impact. Melee-mode and state-entry
guards reject ranged resets and interrupted actions. Combat routes select
walk/run independently of the Fighting activity label. ADR-0436.
This is a presentation bridge: the simulation still releases its pending
blow at impact without the full mode-3 action lifetime. The consequence for
combat timing remains open under Q582.

Chicken Walk/Eat/Scared art selection is documented in object-drawing.md §8.
Their meshes use the shared fractional sampler. Positions and headings are
captured before each simulation tick and interpolated on render frames;
held/carried birds are excluded from the snapshot, avoiding drop slides.
The snapshot is presentation-only and does not enter saves or state hashes.

Built under ADR-0431: travel playback receives the body's speed without
restarting its clip when speed changes. Frame catch-up uses the latest
observed velocity for skipped render ticks; it does not reconstruct their
individual speeds. Shared sample buffers still allocate nothing per sample.

## 8. Spell-casting playback

**CONFIRMED (binary, 2026-10-11):** `0x004950C0` assigns action 16 art slot 6
and mode 3; action 15, the free-aim cast, also uses slot 6, with mode 0.
`0x00498870` is action 16's targeted shot handler. Missing slot 6 resolves to
slot 5 under `0x00494880`; the classic catalogue therefore uses file resource
5, or resource 4 when absent.

Pending targeted/free-aim casts override travel and play once from their
start stamp. After a targeted shot, the view uses its retained spell-slot
stamp, spell attack mode and state-entry stamp to keep the authored clip
visible. Later state entries and unmatched stamps reject that continuation.
This is the same scoped presentation bridge as §7; it does not add an action
lifetime to simulation. Completed possessed casts lack a matching spell-slot
stamp and are not retained by this bridge. ADR-0437.
