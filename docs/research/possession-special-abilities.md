---
systems: [creatures, camera-lighting, simulation-core, presentation-godot]
---
# Possession specials, sniper and foreground attacks

DKII.EXE v1.70, read 2026-11-18 with the headless Ghidra runner. Confidence is recorded per function in the linked records;
no native experiment was run.
The client key dispatch is already [possession-client section 4](possession-client.md#4-attacking-and-abilities).

## Definition fields

The field provenance and source-to-definition mapping live in
[the creature loader record](functions/00492320.md). `CreatureDef` now exposes the two
ability kinds and their two parameter dwords, and the sniper multiplier; the KWD writer
preserves and round-trips all five fields. Reading the 46 shipped editor records gives:

| types | primary / secondary | sniper multiplier |
|---|---|---|
| Vampire, Unique Vampire | 3 / 5 | 0 |
| Bile Demon, Giant, Unique Bile Demon | 1 / 0 | 0 |
| Rogue, Thief, Unique Rogue | 6 / 8 | 0 |
| Monk, Fairy | 7 / 0 | 0 |
| Maiden Of The Nest | 9 / 0 | 0 |
| Firefly, Unique Firefly | 4 / 0 | 0 |
| Dark Elf, Elven Archer | 0 / 0 | 4 |
| Unique Dark Elf | 0 / 0 | 6 |

Other editor records have no special or sniper multiplier. Per-level overrides were not swept.
The two ability-parameter dwords are zero in all 46 editor records.

## Simulation arms

- Action 27 hypnosis and action 29 sneak were built earlier; their commands are now
  reachable through the two ability bindings as the client dispatch specifies.
- Action 30 toggles flag 11 and requests effect 201. The flag is the Vampire flight-form
  arm of [the flying accessor](functions/0048ED40.md), and prevents client attack submission.
  [Animation selection](functions/0048BE50.md) substitutes object 12's art while it is set.
- Action 31 toggles flag 13. While possessed and set, the creature update starts action 50
  if necessary. [Its handler](functions/00499840.md) heals every turn until the flag clears;
  it uses art 23 and sound 0xD4. The rate divisor is variable 33's unsigned low word;
  positive healing is capped at level maximum through the shared health rule.
  Movement keys, waddle and walk cycle stop while praying.
  Leaving clears flags 11 and 13 alongside creep and sniper, as `0x0048EE70` does.
- [Action 28](functions/00516EC0.md) is a carrying toggle: release the creature in player
  `+0x136`; otherwise throw object 64 when player flag 1 is set; otherwise pick that object
  or the nearest eligible owned worker within one tile and the aim cone. Its carried-body
  lifecycle still needs reading before this arm can be built faithfully.
- [Action 137](functions/00514470.md) gives a valid creature target twenty turns of fear.
  [Timed effects](functions/0048E160.md) decrements and adds the amount to temporary fear,
  including on expiry, recording the source creature. Effect 239 starts only when no old timer
  runs; effect 336 is requested whenever bit 2 of the remaining timer is clear, including zero.

ADR-0819 added these state fields under hash/save 531: both flags, the active prayer action
and the fear timer/amount/source are saved and hashed. New journal command tags are 38-41.
The five special animation exclusions in the flying accessor remain outside the existing
flight abstraction. Trap selection now consumes the same supported subset ([traps](traps.md#5-the-selectors), ADR-0820).
The flight-form model substitution is not wired to Godot yet. The inherited
Crypt occupant discount in fear remains unbuilt; no shipped level has a Crypt (combat-fear.md).
Native packet actions 27, 29-31, 38-39 and 137 decode to the shared commands; action 137 requires
observed mappings for both creature ids. Native action 136's room-id mapping remains unsupported.

## Pick Lock is the Prison exception

The earlier Library label was wrong: room type 11 is the **Prison**, while Library is 6 in
the shipped room data. [The client helper](functions/00410020.md) posts action 136 for that
room. [The handler](functions/00514420.md) unlocks and opens the Prison if needed and invokes
[the all-prisoner release](functions/004E2E60.md). No special-ability-kind test gates the key.
`OpenPossessedPrisonCommand` captures the creature's Prison tile when the key is read and uses the existing
`JailBreakRules` door/release model. Its inherited scope limits are in [jail-break.md](jail-break.md).
Outside a Prison the key's action 36 is inert; it does not unlock ordinary doors.

## Sniper

The earlier claim that the key had no definition/spell gate was wrong; see the corrected
[input record](functions/0040FDF0.md). The key requires an integral multiplier above one
and an available nonzero spell among the first three slots. It posts the new sniper bit
and a camera action; it does not change the selected spell.

[Camera action 14](functions/004424A0.md) changes view distance immediately by ten tiles
up or down according to requested versus current lens, clamped by the camera preset's
distance bounds. It starts a 300 ms real-time lens move to the first-person preset's lens
times the integral multiplier on entry, or to the preset lens on exit. The move uses the
same incremental signed division as the other camera moves; see
[camera update](functions/0044BED0.md). `PossessionSniper` applies this to the settled
camera, including the distance used by its fade and tile walk.

## Foreground attacks

[The foreground loader](functions/005074D0.md) loads 22 named animation resources (plus
empty slot 0), including Bite, Claw_Slash, punches, sword/scythe/staff strikes, arrows,
Bile smashes and Hypnotise. They are animated KMF meshes in `Meshes.WAD`.
[The starter](functions/005075A0.md) installs a screen render block at the requested frame
rate for `22000 / fps` milliseconds; its packed animation setup also writes constant 22.
[Its update](functions/00507710.md) subsequently fades it over 500 ms.
The decoded Bite has 18 frames; R-Punch, Sword_Slash and Hypnotise have 15 each. Their textures
are additive. Inspected pixels show red fangs, a cyan punch smear, a cyan slash and a coloured
spiral respectively, without a persistent hand or weapon model in these samples. This is an
animated foreground effect, not evidence of a first-person skeletal rig. The other resources'
visible contents and the screen-space mesh transform remain UNREAD; the renderer is unbuilt.
Decoded, inspected textures: `F:\DK2Evidence\possession-finish-20261118-abilities\*.png`.

## Remaining reads

The carrying read covered action 28, the carried handler [247](functions/004831D0.md), the
thrown handler [248](functions/004833C0.md), and field-reader scans. The worker attaches half
a tile ahead of its possessor at eye height; release launches a physical vector. The thrown
path spins, tracks velocity reversals, hits creatures and applies landing damage. Its collision
helper `0x00451570`, the landing state 249 and native physical-vector integration were not
closed by this bounded search. The entire carry/throw arm stays unbuilt. The player's `+0x136`
also participates in [its checksum](functions/004BCB60.md), not in another cleanup routine.

The bounded foreground search read the loader, starter, updater, four KMF resources and their
textures. The render queue's foreground transform is still UNREAD. It also read
[a statistics formatter](functions/00507240.md), unrelated
to the foreground class, and [its neighbouring foreground teardown](functions/00507450.md).
