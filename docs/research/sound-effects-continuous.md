# Sound effects: footsteps, the action table, loops and first person

Sources: DKII.EXE v1.70, read through Ghidra on 2026-11-03; shipped `Data/Sound/Sfx/Global/*SFX.map`, `Data/editor/Creatures.kwd`.
Plan Phase W7 of [world-sounds](../plans/2026-10-31-world-sounds.md). Conventions as in
[sound-effects.md section 6](sound-effects.md#6-the-hand-and-its-cursor): a request is one slot `+0x20` call of the sound manager
(handle, category, event, position); a row is silent when no `SFX.map` holds the group with samples. Section 1 is the footsteps,
2 the action table, 3 the looping sounds, 4 the terrain ambience grid and first person.

## 1. Footsteps

Presentation only: no simulation state is read or written. Native evidence is in [sound-effects.md section 1](sound-effects.md#1-creature-footstep-events)
([render pass](functions/0043CC30.md), [crossing helper](functions/004B4630.md), [event wrapper](functions/0048FF10.md)).

**The marks (CONFIRMED).** The 48 pairs of unsigned words at CREATURES file `+0x1323` are definition `+0x7A3 + 4 x slot` (first mark) and `+0x7A5 + 4 x slot`
(second). The install of a type-6 clip ([chooser](functions/0048BE50.md), at `0x0048C5D0`) indexes the pair by the slot **asked for**, not the
slot the fallback resolver ([0x00494860](functions/00494860.md)) returns, and divides each word, shifted left 12, by the **resolved** clip's frame count, into
animation object `+0x7F` / `+0x83`; a clip that is not type 6 clears both. Ours: `CreatureDef.FootstepMarks` (`KwdCreatures` reads and writes all 48 pairs;
`FootstepMarkPair.Normalize` is the division). Slots are the file's art blocks (block `0x20 + 84 x slot`); slot 1 is the walk, 2 the run, 4 the recoil,
9 the angry clip, 38 the back-off (`ArtSourceMap`'s resource index is the slot minus one).

Shipped data: authored pairs for slot 1 (walk) and 2 (run) exist for the Imp (2, 8 for both), Horny (slot 1 only), the Giant, the Knight, the Black Knight, the Stone Knight, King Reginald, the three Princes, the Lord of the Land and the Unique Black Knight (the humanoids' 16, 27 and 13, 25 or near); no other definition has authored ones (the uniques hold `0xCDCD`), so a Goblin or a Warlock never steps on land. Slots 4 (the Imp's 7, a clip of 11 frames), 9 (the Giant's 15) and 38 (the knights', a type-1 resource, which clears marks) carry pairs for clips this view does not play. Slots 46 and 47 of the 48 hold the creature's pose name as ASCII (`GOB-Pose` for the Goblin, a shorter string for the Imp) (the copy of 48 pairs runs over a string, so no art slot reaches them), and the unique creatures' unauthored slots hold `0xCDCD`, whose normalized mark lies far past one clip and never crosses. Slot 2 of the Imp has pair (2, 8) but no run clip
(resource type 0): the run is requested, the walk resolved, and the marks are slot 2's over the walk's 12 frames.

**The crossing (CONFIRMED).** [0x004B4630](functions/004B4630.md) reads the animation object's previous-turn phase `+0x90` and current phase `+0x94` (12.12, wrapped at 4096),
takes the step between them (4096 added when negative) and scales it by two frame fractions in 1/256ths (each product shifted right 8 after adding 255 when negative, a
division toward zero) to an interval `[start, end]` from the previous phase. Either mark strictly inside, and the result is 1; otherwise, while `end` exceeds 4096, both ends drop
4096 and a mark inside the lowered interval gives 2; no mark gives 0. A zero first mark with a zero second is none; a nonzero first mark with a zero second takes the first for the second.
Ours: `AnimationMarkCrossing.Test`, with `RenderAnimationClock.PreviousPhase` and `Phase`.

**The request (CONFIRMED).** [0x0043CC30](functions/0043CC30.md) calls the helper at `0x0043CD84` for each thing of class 2 it draws, with the last frame's fraction (plus 250 x the game
turns since, a constant whose sign and unit are unread) and this frame's, and requests `0x314` for 1 and `0x315` for 2 through [0x0048FF10](functions/0048FF10.md), which substitutes `0x337` / `0x338` while creature `+0x3EA` bit 21
is set. The request is the creature's handle `+0x2E`, category `+0x6E3` and position `+0x1A`. Ours: `CreatureFootsteps.For`, asked by `DungeonScene` after each creature clip draws.

**The wading flag (STRONGLY INFERRED).** [Creature_Update](functions/00488930.md) clears bit 21 each update, then sets it at `0x00488CB2` when the creature's `+0x22` is at most the signed word at `+0x26` and the terrain under it
has flag `0x80` (water, the bit the water speed tests). `+0x22` and `+0x26` are read as the creature's height and the floor's; the comparison is unread beyond that. Ours: not flying and the tile's terrain is water
(`CreatureView.Wading`), a bridge being its own non-water terrain.

**Samples.** `0x314`/`0x315` have one or two samples in `creature_black_knight`, `_giant`, `_horned_reaper`, `_imp`, `_king`, `_knight` and `_skeleton` only; `0x337`/`0x338` one or two in every creature category but the Firefly's and the Jack in the Box's, so a wading creature of any other kind sounds, and a walking Stone Knight or Horny, which have marks but no `0x314`, does not.

**Built:** `CreatureFootsteps`, the marks, `AnimationMarkCrossing`, `AnimationMarkWatcher` (the last drawn frame's fraction) and the Unity wiring
(`ClassicCreatureAnimation.FootstepCrossing`, `DungeonScene.CreatureFootstepRequested`, `DungeonRunner`); the request goes through `ClassicSoundPlayer.Play`, so the throttle and the
voice retention judge it. Marked deviations: the interval's earlier fraction is moved back by 256 per turn, not 250; the clip's marks are tested for the walk and run clips only (the view plays no
recoil, angry or back-off clip; slot 4's, 9's and 38's wait for a clip that plays them); only creatures inside the camera's frustum sound, where the pass draws tiles in view; DK2 tests
each drawn frame, ours each `Draw` of the clip. Not imported: OpenKeeper's three footsteps per 200 ms and fixed `mark / 30` ([sound-effects.md section 4](sound-effects.md#4-slow-frame-throttle-and-unverified-playback-budgets)).
