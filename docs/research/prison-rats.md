# Prison rats

Status: STRONGLY INFERRED from DKII.EXE v1.70, read 2026-10-12.
Shipped record values are CONFIRMED from Objects.kwd. No original-game
population or timing experiment has been performed.

## 1. Generation

Player_Tick (0x004B9EC0) calls 0x004BA0D0 once each keeper tick, after
computer-keeper AI and before object decay/mana work. Neutral and hero tags
return before the call. KilledByPlayerId and defeat status do not gate this
call: 0x004B9F5E skips only the gold resync, and the AI status guards join at
0x004B9FFE immediately before the ambient call at 0x004BA000. The guarded
keeper routine 0x004BB2A0 runs later; its defeat branch is not this spawner's
parent. World_TickPlayers 0x004C2310 walks every player without those guards.
The rat branch sums the keeper's Prison and Torture
Chamber tile counts, divides by nine and caps the result at sixteen. It counts
all that keeper's existing object-11 instances, including any outside rooms.

Below that population limit it draws random(100), and rolls 95 through 99
continue. It selects a random owned Prison through 0x004C6460, then a random
slab without a slot test through 0x004E4D90(out,1,0). It creates object 11 at
that slab's centre, with z=4096. Prisoners, food and room capacity do not gate
this branch. Torture tiles contribute to the limit, but a Torture-only keeper
has nowhere to spawn rats. Population above the limit is not culled.

Both the chance and movement draws use the shared world simulation seed at
byte +0x15A0. Disassembly 0x0049FE71 confirms the address; the decompiler's
int-pointer +0x568 means that same byte offset. The two world pointer caches
are already resolved in Q231. Room-list and slab-chain ordering use the
existing derived-room approximation documented in 0x004C6460's record.

## 2. Movement

Objects.kwd object 11 has StartState 11, speed 819, health 300, flags 0x210601
and only floor art Rat_Run (animated type 6). The loader's movement-speed
conversion gives 204 position units per tick (819 divided by four).

Object process 11 is 0x0049FE60. Each invocation first draws random(4).
If the current tile's owner differs from the rat's owner it destroys the rat.
Otherwise it scans north, east, south and west, starting at that random index.
A candidate must share the owner and have navigation bit 3 clear. A room
whose Construction is DoubleQuad (7) also rejects an interior tile (tile
byte +0x0C is 0xFF); this is a construction rule, not a Prison-id test.

For each accepted candidate it draws x then y offsets with random(4096),
covering the full tile, and starts common MoveTo with resume process 11,
extra speed zero and interruptible flag true. There is no path search.
Process 9 uses the common turn/countdown/velocity rules in
[hatchery-roaming.md](hatchery-roaming.md#2-movement-timing--confirmed).
Chicken-only avoidance remains gated by object type 9. A rat immediately
chooses another point on its next process-11 update after arrival.

A failed search returns zero: 0x0049EED0 clears the process, and the following
update initializes the definition's StartState without dispatching it.

## 3. Life and presentation

Object_Update (0x0049BD20) runs the process, physics, then health decay.
Flag bit 0 gives unconditional decay of one health per unheld tick. The
shipped 300 health therefore lasts 300 object updates. The simulation removes
a rat at zero health and reuses the existing world unlink operation.

RatRules/RatSystem implement the movement and lifetime; RatSpawnSystem runs
the keeper generation branch. The default ObjectMovementSystem walks Chickens
and Rats together in newest-first object order, preserving their shared random
draw order (World_LinkThing 0x004B4A10; World_UpdateThings 0x004B7830). Common
movement lives in ObjectMovementRules.
The existing object presentation samples Rat_Run and interpolates retained
position/heading at unit scale. Simulation spawn z is retained; drawing uses
the established floor-height approximation rather than reproducing the
original initial falling physics. Rat sounds and the accompanying Bat branch
are not implemented by this change.

## 4. Nearby handlers checked while locating the rat

These were excluded by their primary binary bodies, not by their names.

| Address | Read result |
|---|---|
| 0x004E2AC0 | Prison door auto-close countdown; no rat generation. |
| 0x004E2940 | An owned worker in state 43 at the closed door opens it and sets its auto-close count. |
| 0x004E2C70 | Prison admission checks total/safe capacity, sets state 114 and restores one tenth of level-scaled health. |
| 0x004E7CD0 | The room-flag-bit-4 border-effect updater emits effect 20 on odd turns; no rat generation. |
| 0x004A29E0 | Process 33's object-70/110/111 effect-generation countdown; no rat movement. |
| 0x004A2AD0 | Process 34 grows two draw-scale words toward 6144 and clears two other draw words. |
| 0x004A2B40 | Process 35 shrinks two draw-scale words toward 4096 and clears two other draw words. |
| 0x004A2C00 | Process 36 updates a draw flag using terrain bit 8 and emits effect 204 every sixteenth id-staggered turn. |
