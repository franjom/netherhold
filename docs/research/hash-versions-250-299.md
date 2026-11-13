---
systems: [simulation-core]
---
# State-hash versions 250–299

Earlier pins and reasons from [the current version record](hash-versions.md).

**299** — `0xC9315DE4157C529E`. FormatVersion stays 297. Behaviour only: every live store's turn draws the sparkle `0x004F3A00` on the world stream before its sweep — `random(100)` per Treasury and unruined Heart, and below 30 one more per tile holding gold, below 10 requesting effect 34 at the pile (ADR-0534); `PickUpStoreGoldCommand` lifts a store tile's whole gold into the hand as a neutral Gold Pile 1 (`0x004C0CB0`). Moves every replay rig: each level's Heart now draws every turn. No new fields.

**298** — `0x3E2A6741A425B7F1`. FormatVersion stays 297. Behaviour only: the keeper's own dig mark on a goldless wall ends reinforce and repair work there (a rival's tag and a gold wall do not) — the pool raise of kind 1 (`0x00507BB0`) removes the tile's kind-6/16 entries and refuses new ones, and states 22 and 24 drop their task once its action ends (`0x004663C4`); the wall keeps its health (ADR-0533, Q621). No new fields.

**297** — `0xC05DD2EF3BEE644C`. FormatVersion also 297. `TriggerState.SpecialInfoPlayerId` (action 46) and `MaximumSlaps` (player action 50, init -1); `CreatureState.HealthSourcePlayerId` (`+0x36`, stamped at each health-change call site) and `DeathUncounted` (`+0x3E6` bit 0x40, nothing sets it yet); `PlayerState.HeroesDestroyed` (`+0x14A`), credited in `CreatureHealthSystem.Die`/`CreditHeroKill` (ADR-0532). The level-end pass `0x004BE9E6`, the My Pet Dungeon score, `+0x146` and the `0x19E`/`0x173` tallies are not built.

**296** — `0x9182B5916A8366B9`. Behaviour only (295 on the step6 branch; renumbered when merged after ADR-0530's 295): a keeper's Imp dropped on one of its own Heart's eight platform tiles lands there instead of being refused; only the centre disposes of it, now crediting Create Imp's mana cost as state 71 does, and never on a ruined Heart (ADR-0526 amendment, owner 2026-10-23). No new fields.

**295** — `0x4E209623A4874882`. Behaviour only: a room tile is refused where `0x0044F800` finds a trap or door of any owner, or another player's living non-Imp creature; a dragged span tests only its seeds (ADR-0530, Q501). No new fields.

**294** — `0xA9B05FB68CF7011B`. Four saved tutorial-highlight rectangles, ids and signed tick timers (ADR-0528), plus predictive crystal contacts replacing oversized steering circles (ADR-0527 amendment). The pinned fixture has no active highlights or portal route; separate tests cover both behaviors.

**293** — `0xAA764D602CDE17DC`. Behaviour only: swept portal-crystal bounds, body clearance, target projection and steering contacts (ADR-0527). Bounds are derived from room tiles; no new saved fields.

**292** — `0x3CA3464C77E0C2AD`. Behaviour only: a hand drop obeys DK2's drop gate `0x0040D8F0` (alliance, solid and door tiles, the Heart, Portal, Prison, Temple, Torture and Combat Pit arms; ADR-0526). No new fields.

**291** — `0x4F7FEF46308B6B76`. The rooms' border cursors (room `+0x81..+0x89`: tile, orientation, phase), hashed and saved after the marked-creature slots: each room with HasFlame and an owner runs one round its contour and asks for a Flame Round Room on odd turns (ADR-0524). The pin's fixture has no rooms, so the change is the version and an empty list.

**290** — `0xE6FC955DAC6ECF7F`. The world's six marked-creature slots (world `+0x72BD`, creature id and value), hashed and saved after the Tremor slots: trigger action 42 fills them and a creature's removal frees its slot (ADR-0523). The pin's fixture marks nobody, so the change is the version and six empty slots; `PortalGemOnRealDataTests` round-trips Level 1's marked Lord.

**289** — `0xBD66BD442BC1F4D0`. Player `LastPaydayTick` and native-width creature name/blood indices, hashed and saved (ADR-0295 amendment, ADR-0521); master's 283–288 merged under it. Numbered 275, then 283, on classic-look/step6.

**288** — `0x9FB00F71132E91E9`. Native wet/blood footprint counters, saved and hashed; see [creature-footprints.md](creature-footprints.md).

**287** — `0xC428FD4390FADF96`. Behaviour only: Tier B's tile chain tries the tile's loose objects after its gold (`0x00469580`'s third step, ADR-0519). No new fields.

**286** — `0xBD6FBFFB91F1AADF`. Behaviour only: speed no longer scales with level; the body's cap is the definition's speed (`0x004887D0`, ADR-0518). No new fields.

**285** — `0xE44D70CE6838C66C`. Behaviour only: an alarm goes only to a Guard Room (`0x00475336`, ADR-0517). No new fields.

**284** — `0xA1D6204A54297315`. Behaviour only: walkers and dig turners keep a gone task, as states 6 and 13 do (ADR-0516). No new fields.

**283** — `0x07E0196E6BEA9DCA`. `CreatureState.ActionCount`, hashed and saved after `ActionHandlerDone`; idle actions play their clip and installs draw their coin (ADR-0515).

**282** — `0xA98DDD15FD7EB37B`. `CreatureState.ActionArtSlot`, `ActionAnimationPhase`, `ActionAnimationRate`, `ActionAnimationFlags` and `ActionHandlerDone`, hashed and saved after `StateEnteredTick`: the dig and mining actions end on the stored clip (ADR-0514).

**281** — `0x781C971C1E408A00`. Behaviour only: route requests and stops zero the body's velocity (`0x004D0F60`, `0x0048ECE0`; ADR-0513). No new fields.

**280** — `0xA9BE08812B0B9989`. Eight verified Imp corrections integrated under ADR-0512; see [imp-fidelity-integration.md](imp-fidelity-integration.md). `CreatureState.PosZ` and `CreationVelocityZ` are saved and hashed. Versions 275–279 were isolated experiments; their pins do not describe this composition.

**274** — `0x34A682FB8867CE5B`. Steering axes use resulting velocity after clamping (ADR-0509); no new fields. Versions 270–273 belong to preserved Imp experiments.

**269** — `0xFA0172F155ECAA5C`. Native-byte Heart routing and the earlier small core guard (ADR-0505); no new fields. Versions 266–268 belong to earlier Imp experiments; ADR-0512 records their integrated and provisional parts.

**265** — `0x10A9C84139F8DF10`. Behaviour only: creature neighbours come from the native tile rectangle before combined-radius contact checks; the torturer's victim route retains native arrival flags (ADR-0500). No new saved field.

**264** — `0xC53FC1CC3EABDE59`. Behaviour only: a portal admits a creature whose room entry asks size 0 with none of the room owned, so a Lair creature comes through before the first Lair (`0x004DC810`, ADR-0499). Snapshot format is 264.

**263** — `0x688ADFB86353DF0E`. `CreatureState.GemCarrier`, DK2's creature `+0x92` bit 2, hashed and saved after the standing job: trigger action 20 hands a creature a Portal Gem and sets it (ADR-0495). Snapshot format is 263. The pin's fixture has no carrier, so the change is the version alone; `PortalGemOnRealDataTests` round-trips a set flag.

**262** — `0x6A54498299CB6FDD`. `PlayerState.CameraPathEndTick`, saved after PortalsClosed: action 31 starts a scripted camera path and condition 72 waits for its end (ADR-0496).

**261** — `0xFD8573748CE724A2`. A level's VARIABLES id 77 records set creature definition fields (ADR-0491): Level 1's Lord of the Land Threat 125 and MeleeDamage 200, Level 4's Threat 150; no new saved field. The fixture has no level variables, so only the version moves the pin.

**260** — `0xBC2CC79AE5E4E213`. Exploration is saved and hashed alongside master's navigation stamps.
See `fog-reveal-sources.md`. Snapshot format is260.

**259** — `0xC8D3AFFC7D3F1C08`. A hero party's members stand on their spawn tile's corner, the tile times 4096, as `0x00519F90` places them, not its centre; a 3x1 gate's step-off keeps its tile centre (ADR-0490); no new saved field. The fixture raises no party, so only the version moves the pin.

**258** — `0xD08AD6BA391493C1`. A creature's spell shot leaves from its slot's origin turned by its heading (`0x00498989`), not its centre (ADR-0489); no new saved field. The fixture casts nothing, so only the version moves the pin.

**257** — `0x11DCC450617C3D56`. A creature getting up (257) may look for a fight: the may-look gate tests the state flag's bit 6, which 257 lacks, and the freeze timer, where ours refused every stunned and frozen activity (ADR-0488); no new saved field.

**256** — `0x766C3AB62E635E07`. `WorldState.NavStamps`, hashed and saved after the doors: navigation bytes that outlive their writer until the tile's terrain changes, a door's 0 left on water or lava by `0x004A66F0(0)` (Q600) and the Heart builder's 3 on its centre (`0x004DB700`, Q601); `WorldSnapshot.FormatVersion` 256. The fixture has no doors and no Heart record, so only the version moves the pin.

**255** — `0xE09E9405FB971F17`. The navigation mesh's locate cache (the original's
`DAT_006E5AC0`, sixteen cells) is state, saved and hashed in place of the locate
hint, and the whole-map build takes the original's reset diagonal, corner order,
split slots and constraint insertion (ADR-0486); `WorldSnapshot.FormatVersion`
255. The fixture routes nothing, so only the version moves the pin.
**254** — `0x14705036ECD12242`. The hero and neutral players' creatures run no needs, need-anger or mood choosers, the dispatcher's owner test (ADR-0485, Q599); no new saved field.

**253** — `0xEA82DB139335F65D`. The approach is state 8, a walk resuming 199, instead of a flag on 199; the engagement re-enters 199 only outside the Combat category read through a walk (ADR-0483). `Approaching` is derived from the state and left the snapshot: `WorldSnapshot.FormatVersion` 253. `CombatSystem` skips corpses (Q598).

**252** — `0x7326CEF9676EA896`. A creature walking to its fight target (our `Approaching`, DK2's state 8) earns no combat experience; ours paid it as if in state 199 (ADR-0366 amendment); no new saved field.

**251** — `0x3DFD1B0E0D7517C9`. The store sweep runs when DK2's turn, our tick plus one, is a multiple of 16, one tick earlier than under 250 (ADR-0285 amendment, Q596); no new saved field.

**250** — `0xBD323E55AFE88884`. The stun (255, 256) counts from the turn after it is set, as 255's Age does, so a hand drop lies one turn longer than under 249, as Level 1's capture measured; 255's first call asks for the Creature Dazed Effect (ADR-0480); no new saved field.
