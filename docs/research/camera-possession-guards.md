# Scripted camera requests during possession

DKII.EXE v1.70, fresh reads recorded 2026-10-31. Related playback and request
flow: [camera-paths.md](camera-paths.md).

**STRONGLY INFERRED**, fresh reads: `0x0044D320` changes signed word
`+0xE48` before testing modes 2, 10 and 11; those modes skip the viewport
animation, including force-off through `0x004426F0`. `CinematicLetterbox`
retains that count while `ScriptedViewDirector` suppresses the animation.
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

