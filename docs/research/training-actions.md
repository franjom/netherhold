# Training machine approaches and actions

Primary source: retail v1.70 `DKII.EXE`, whole-function decompilation and
caller instruction checks on 2026-10-11. The payment hook remains described
only in [training.md](training.md); this is the natural machine cycle.

## 1. State cycle — CONFIRMED

State 108 (`0x00485310`) stops movement. Below the training cap, when the
keeper can afford training and the creature stands in its own Training Room,
it chooses a machine through `0x004F2FF0`. The machine position is saved in
state `+0xAB/+0xAF`. Object 14 uses a random angle in 0–2047 and radius
`0x7FF` around that position. Object 29 uses `0x004F3310` to select a room
cell with a clear line to the machine. The route resumes in state 111 with
movement option `0x80`. Without a machine, the fallback is a random room
point and radius `0x400` offset; it does not replace the saved machine point.

State 111 (`0x004855B0`) looks for object 29, then object 14, on the saved
machine cell. It turns toward that position through `0x004B2AE0`, waiting
while any angle remains. With object 14 and resolved art slot 5 present it
starts action 30, count one, targeting that object, then enters 112. With
object 29, a ready attack spell and resolved art slot 6, it starts action 31
and stamps that spell's cooldown at action entry, then enters 112. Both use
sound 211. With no object, it returns to 108 and starts action 30 with target
zero. States 111 and 112 return failure at the training cap.

State 112 (`0x004857F0`) stores the creature's current XY position every
turn. After age one, movement away from that saved position aborts an active
action. It stays while an action exists, otherwise returns to 108. This is
a repeating choose, approach, face, strike/cast, choose cycle.

## 2. Machine and spell selection — CONFIRMED

`0x004F2F50` scans the first three creature spell slots in order, requiring
the level, a nonzero spell definition, its attack flag and casting readiness.
No ready attack spell makes `0x004F2FF0` prefer object 14 then 29. Otherwise
an RNG draw with bound two chooses the type order. Within each type it scans
the room's linked object list, draws RNG(100) for every matching candidate
even with chance 100, and keeps the nearest squared fixed-point XY distance.
Strict comparison preserves the first object on a distance tie. A chance
failure across an existing type repeats that type's scan.

`0x004F3310` walks the room slab chain, excluding floor and wall furniture
slots. It probes a line from each cell centre to the target's actual position
through world virtual slot `+0x30C`. An improving distance candidate draws
RNG(100); chance is halved on the creature's current cell. If eligible cells
exist but none passes the chance, it repeats. Caller pushes confirm that its
target argument is the machine position, rather than a terrain identifier.

## 3. Action gates — CONFIRMED

Action 30's handler (`0x00498CA0`) waits until unsigned current tick exceeds
`start - 1 + def+0x5CA`, then sets the valid target object's process state to
18 through `0x0049EF60`. It completes its handler once. This does not itself
end action 30: action-table mode three waits for both the handler and counted
animation completion. Its art slot is 5. See
[object-drawing.md](object-drawing.md) for the dummy's resulting reaction.

Action 31 uses `0x00498870`, the targeted-spell handler, and art slot 6. It
waits until current tick is strictly greater than start plus the slot's shot
delay, then emits the spell shot toward the target object. Cooldown starts
before that emission. The common action-table/count timing is described in
[creature-action-table.md](creature-action-table.md).

`0x004B7830` updates creature class 0 before shot class 1 and object class 2.
The default host therefore polls training actions in `TrainingActionSystem`
before `ShotSystem` and `ObjectReactionSystem`: an emitted shot takes its
first step and a struck dummy initializes its reaction on that same turn.
The isolated job-system host retains a guarded poll fallback; completed
handler work never runs twice.

## 4. Implementation boundary — STRONGLY INFERRED

`TrainingStep` implements the states and the separate one-shot action gate.
Action fields participate in hashing and serialization. The room machine
list uses newest-first object order; the room position/visibility adapters
are existing simulation abstractions rather than binary structures.

Spell training launches through the existing shot system. That system does
not retain creature spell emission offsets or expose an object target kind
and object collision path. The action timing and art-6 playback are built;
original three-dimensional emission and object impacts remain that existing
projectile boundary. Q582's combat lifetime is not altered by this cycle.

`TrainingCycleTests` covers the natural dummy and spell-machine paths,
delayed spell release, moved-position abort and cap refusal in 111/112.
