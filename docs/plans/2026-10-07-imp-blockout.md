# Imp blockout — review note

Status: **paused at v015, unapproved** (2026-10-07). The owner judged the procedural blockouts
far from the approved concepts. They are kept as comparisons, and primitive-based construction
stops here; the next route is an image-to-3D trial (`2026-10-07-imp-image-to-3d-trial.md`).
v015 revises v007's head, expression and posture; v007 stays the earlier baseline. The decision
behind both is ADR-0398, written while the DK2 portrait was still the appearance source.

## Where it is

All art is EA-derived and private, under `build/modern/creature/imp/` (gitignored):

| what | path |
|---|---|
| editable scene | `blockout/v015/imp_blockout.blend` (collections `modern`, `original`, `reference`, `cameras`) |
| parameters, face counts, the script that built it | `blockout/v015/params.json`, `blockout/v015/imp_blockout.py` |
| review sheets | `blockout/v015/sheets/`: `portrait_vs_head`, `head_vs_v007_colour`, `head_vs_v007_clay`, `head_colour`, `head_clay`, `body_colour`, `body_clay`, `variants`, `compare` (with the gameplay view), `overlay` |
| single renders | `blockout/v015/renders/` (`head/`, `body/`, `variants/`, `gameplay/`); v007 re-shot with the same cameras in `blockout/v015/baseline_v007/` |
| earlier versions | `blockout/v001` … `v014`, each with its note in `params.json`; v001–v007 were built by `blockout/scripts/imp_blockout_v007.py` |
| the portrait | `reference/portrait.png` |

Rebuild (a new version each run; none is overwritten):

```
<blender> -b --factory-startup --python-exit-code 1 --python tools/blender/imp_blockout.py -- --reference build/reference/imp/imp_reference.blend --portrait build/modern/creature/imp/reference/portrait.png --out build/modern/creature/imp/blockout --note "<what changed>"
<blender> -b --factory-startup --python-exit-code 1 --python tools/blender/review_renders.py -- --blend build/modern/creature/imp/blockout/v007/imp_blockout.blend --head-only --out build/modern/creature/imp/blockout/<vNNN>/baseline_v007
<blender> -b --factory-startup --python-exit-code 1 --python tools/blender/review_renders.py -- --blend build/modern/creature/imp/blockout/<vNNN>/imp_blockout.blend --portrait build/modern/creature/imp/reference/portrait.png --baseline build/modern/creature/imp/blockout/<vNNN>/baseline_v007
```

## What changed since v007

- **Eyes.** v007's red patches above the brows were the eyeballs' top caps (26 of 642 vertices
  each, above z 0.946) poking through a forehead that lay inside the ball: each ball sat only half
  its radius behind a dented sphere, so the skin ran through its middle. Now flesh covers each ball
  (a socket mass), the opening is cut along curved upper and lower lid arcs, skin inside it is
  tucked behind the ball, and no skin elsewhere may lie inside the ball. In v015 only the opening
  shows the ball (232 and 234 vertices, 0 above or behind). The lids are continuous rings with a
  thin margin; the upper lid sits low (weary). The eyes are larger than v007's (radius 0.100 at
  height 1, against the portrait's near eye at ~45% of the face's width).
- **Skull and face.** Built from masses instead of a sculpted sphere: a low dome, a broad forehead
  sloping to heavy brows, sockets, cheekbones, a protruding muzzle, a small flat nose with nostrils,
  heavy lips, jaw, chin and nape. Eyes sit 30% down the head and the mouth a short nose-length
  below them, as in the portrait's close-up.
- **Mouth.** A real opening cut between the lips, a dark interior behind the teeth, and broad,
  blunt, irregular teeth in both rows rooted in the gums; corners pulled down into a tense
  grimace. Broad folds only: nostrils, nasolabial grooves, a short frown.
- **Ears.** Thick at the base with a raised rim around a dished inner fold.
- **Posture.** Head lower and forward on a thicker neck; shoulders rolled forward; one curve from
  pelvis through a hunched upper back into the neck; knees bent, feet planted. Hands and pick keep
  the original's placement.
- **Clothing and sack.** Flat, flat-shaded leather straps over the shoulders and round the waist
  into the sack; waist cloth with thickness, curvature, broad folds and an uneven hem; the sack
  sags, has packed bulges and gathered fabric at a ruffled tie, and rests against the back.

## Still visibly wrong

- In profile the brow still reads as a separate bump above the eye, and the dome behind is large
  and smooth; the forehead's union with the brows is lumpy in clay.
- The lower face is lumpy where masses meet (cheeks, nose wings, muzzle); folds are broad.
- The ears' inner fold is shallow and hardly reads at the review angles.
- The eyes have no iris or pupil; the portrait shows a darker centre but too blurred to model.
- Arms are thin tubes with mitten hands; the grip on the pick is approximate.
- The sack's bulges are mild, and it is larger than the portrait's hump (the original's size).
- One remeshed skin surface of 228k faces (314k for all objects): no clean topology, no UVs.

## Inferred, not seen

- Profile shape of the head and the back of the skull: the portrait shows neither.
- Teeth count and exact shapes: the portrait is too low-resolution; blunt and irregular kept.
- Weary expression: from the portrait's low upper lids and drawn mouth; the brows' outer droop is
  a conservative reading.
- Back straps into the sack, a back waist flap, three fingers and toes, the bone disc on the chest,
  the pick head's shape: neither source shows them clearly (unchanged from v007).

## Versions

| | change |
|---|---|
| v001–v007 | first blockout (2026-10-07): sculpted-sphere head, capsule limbs, see v007's `params.json` notes |
| v008 | head from masses, mouth cavity with interior and teeth, eyelids wrapped on the balls, posture, clothing, sack |
| v009 | portrait proportions (eyes raised, low dome, mouth up); the balls ended outside the skin |
| v010 | socket masses cover each ball; forehead mass; the openings read as rectangular spectacles |
| v011 | round openings from curved lid arcs; thinner lid margin |
| v012 | eyes bulge from the lids; brows lower; nape and thicker neck |
| v013 | larger, nearly touching eyes; wide grimace, bigger teeth; small flat nose |
| v014 | broad, blunt teeth |
| v015 | sack bulges kept (v014 smoothed them away) |

## After the review

Corrections from the review first. Then, in `ASSET_PIPELINE.md` §10 order: sculpt detail,
retopology and UVs, textures, rig fitting against the original frames, LODs, Unity import.

Targets for the next modelling round (owner, 2026-10-07; ADR-0398). "The portrait" above is the
DK2 portrait, `reference/dk2_portrait.png`. The appearance target is now the approved AI concept
renders, `reference/portrait.png` and `reference/front_side_back.png`. They are design targets,
not DK2 evidence.
- Sack: the concept's smaller sack, sitting high between the shoulders and tied with rope; the
  original's large sack is a later equipment variant.
- Chest: a recognisable skull ornament; the concept's egg-shaped belly plate is not used.
- Costume per the concepts: riveted leather braces, a buckled belt, a tattered loincloth front
  and back, cloth ankle wraps over straps under the feet.

Handedness (ADR-0399): right-handed by default, with an optional left-handed variant on the same
mesh and rig. From the next modelling round, the pick stays a separate object and both hands are
modelled able to hold it. In v015 only the right hand grips; the left hangs open. The rig gets a
pick attachment point on each hand.
