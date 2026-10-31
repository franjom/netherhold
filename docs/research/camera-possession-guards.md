# Scripted camera requests during possession

DKII.EXE v1.70, fresh reads recorded 2026-10-31. Related playback and request
flow: [camera-paths.md](camera-paths.md).

**STRONGLY INFERRED**, fresh reads: `0x0044D320` changes signed word
`+0xE48` before testing timed transition `+0xDD8` and modes 2, 10 and 11;
either condition skips the viewport animation, including force-off through
`0x004426F0`. `ScriptedViewDirector` mirrors the possession-mode guard while
`CinematicLetterbox` retains the count; the `+0xDD8` gap is recorded under
[remaining deviations](#implementation-and-remaining-deviations).
`0x00442650` calls target setter `0x0044A150` and timing `0x0044A270`;
both refuse modes 1, 2, 10 and 11. The director discards the move in those modes.
`0x0044A550` refuses the same modes for the yaw deadline. The yaw target
setters `0x0044A500` and `0x0044A520` themselves have no mode guard.

Two leads were incorrect: handler 5, `0x00442730`, calls guarded timing at
`0x00442765` but still writes the creature target at `0x0044276A` after checking
the creature exists and its `+0x12` is zero. It has no independent mode check.
Absolute zoom action 6 (`0x00442290`, call `0x004422A1`) reaches `0x00449C30`,
which has no mode guard. These are distinct from action 26's target move.
Action 38 in `0x004C8620`, read at `0x004C8955..0x004C89DF`, always issues
its local player's GUI setter, independently of the viewport animation guard:
`0x00402B70` shows and `0x00402B80` hides. Balanced on/off requests restore
the GUI after possession; suppressing these setters would contradict this path.
Handler 5's target-follow consumption during possession remains unread here.

## Mode changes and viewport retargeting

Whole `0x0044ACE0` and `0x0044BD30`, with instructions
`0x0044AE30..0x0044AF30`, were reread 2026-10-31. Every target mode
selects full-viewport slot `+0xE92` for modes 3-5 and 7, `+0xE82` otherwise.
Equality against destination `+0xE72` skips retargeting; otherwise the source
is current rect `+0xE62`, with start/duration `+0xE4A/+0xE4E`. This common
branch ignores the letterbox count and `+0xDD8`.

The duration is 500 ms within a mode class, 2000 across classes. Leaving mode
7 for another mode, or camera flag `+0xE3C` bit `0x40`, selects zero duration.
The classes are those already recorded in [hud-visibility.md](hud-visibility.md#camera-gate-confirmed-binary).
Thus possession can blend raised bars back to full while leaving the count
nonzero. A later return to the dungeon blends toward its full-viewport slot,
then the next idle frame restores the bars while the count remains nonzero.
These interpolation expectations come from our binary reading; the owner
has not observed this case.

Whole `0x0044A5D0` forwards even a same-mode request to the mode setter;
while a path plays, it retains the last request until the default path end.
Consequently an explicit mode3 request can temporarily close bars in mode3 without
changing the count. The director now consumes these requests, including
deferred requests, rather than treating mode3 as a no-op.

CallScanAll finds the mode setter at `0x0044A7AA`; letterbox callers are
`0x0044270C`, `0x00442720`, `0x0044A495` and `0x0044DAF2`.
Literal-displacement writer scans cover `+0xDD8`, `+0xE48` and `+0xE4E`.
The `+0xE72` and full-slot scans are partial: computed-base writes are missed.
A constructor window in `0x00449470` zeros the rects;
whole reset `0x00449670` initializes both full slots to the screen rectangle.
Its computed-base stores are not all found by literal-displacement StoreAt.
Whole `0x0044DB90` separately restores or clears rects without changing count.

Whole `0x0044BED0`, with idle instructions `0x0044BFE1..0x0044C17B`,
confirms the snap rule: outside a blend, a nonzero signed count gives bars
unless the mode is 2, 10 or 11. Zero gives the selected full slot. The frame
that completes a blend retains its target; the following idle frame snaps.
See [the function record](functions/0044BED0.md) for the flag/global guards.

Full-slot setters [0x00449840](functions/00449840.md) and
[0x004498F0](functions/004498F0.md) also write the live rectangles.
Their direct calls are respectively `0x0043ABE4`, `0x0044135B`, `0x00442F27`
and `0x0043ABD8`, `0x00442F1B`. Wrapper
[0x00441350](functions/00441350.md) is stored at `0x0066CCC8`;
its indirect slot consumers remain unaudited. This does not establish a
complete census of either full slot's writers or runtime geometry.

## Negative counts and shipped requests

Whole `0x0044D8F0` restores the saved mode directly, then normally lowers the
count. Whole `0x004426F0`'s instruction range establishes ForceOff as setting
the count to one before lowering. ForceOff during a path followed by its
default end therefore leaves -1. A subsequent raise reaches zero. This is a
binary-derived control sequence, not an owner observation of its timing.
`0x00403FB0` at `0x00404D2D` tests nonzero, so -1 still requests the letterbox
overlay; the established normal-HUD predicate also suppresses the HUD.

The repository parser surveyed all 66 shipped `*Triggers.kld` files: 34
action-38 records, 22 on and 12 off, across Level1, Level6b, Level14,
Level15a, Level15b and Level20. Whole action-38 arm `0x004C8955..0x004C89E2`
passes boolean 0/1, not ForceOff 2. Level14 has only off record 65500, below
timer condition 28 (timer1, threshold2); it also starts path214 at record65532.
The survey establishes an authored off without an explicit script on; path
starts add implicit raises, so record counts alone do not prove an executed
negative-count timeline. Native gameplay reachability remains unmeasured.

## Implementation and remaining deviations

The presentation mirrors observed mode changes and path entry through the
count-independent viewport blend. Its symmetric bar-height representation
uses zero for either native full-viewport slot; distinct native rectangle
geometry and flag `0x40` instant transitions are not represented. Named
`CameraModeId` predicates own the possession and scripted-move restrictions.
Negative counts remain visible to the HUD predicate and overlay-state flag.
The idle bar also follows the nonzero count, including -1. The camera's
bit `0x10` empty-rect arm and global `0x0075AEAF` write suppression are
unrepresented; their native guards are recorded in `0x0044BED0`.
No simulation state changes; hash/save 353 is unchanged.

The `+0xDD8` letterbox skip is **not mirrored**: native timed position/lens
mode transitions are unmodelled, so the renderer has no equivalent transition
state to test. Whole `0x0044BD70` writes this deadline on a delayed commit and
clears it on commit. Action26's represented glide uses a different native
deadline, `+0xDFA`, and cannot stand in for it. This remains a known deviation
until those mode transitions are implemented.

External mode changes are sampled per render `Update`; an unchanged adapter
sample preserves action 15's selected mode. DK2 tests the mode when the action
is delivered on a tick, so requests queued across a possession boundary can
see different modes. The initial sample establishes a baseline. Full timing
parity is not claimed.

