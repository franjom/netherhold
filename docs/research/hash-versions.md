# State-hash versions

`GameSimulation.HashVersion` numbers the shape of the simulation state that
`GameSimulation.StateHash()` folds, and `WorldSnapshot.FormatVersion` is kept
equal to it. `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` pins
one literal hash for a 500-tick run of `TestWorld.Simulation(seed: 0xDEADBEEF)`.

**The rule.** Change simulation state or hashing only with matching hash/save
version bumps and a new pin in the same commit. Unexplained pin failures block
shipping. Rationale: `docs/architecture/simulation-loop.md`.

**Composition versus behaviour.** A version bump changes the pin even when a
new field is omitted from hashing. State absent from its fixture needs separate
sensitivity tests (`PlayerMessageTests`); snapshot round trips cannot prove coverage.

## Versions

**175** — `0x6F9C6A3B4D22F803`. Creature possession/influence flags and influence deadline; entry/exit and stat modifiers are built. All42 scenarios change hash composition only; see `keeper-spell-possession.md`. ADR-0350.

**174** — `0x8213FCEC0D1B3EA0`. Player OneShotReaper-used flag survives removal and snapshots; variant creation and its countdown are built. Every scenario hash changes composition; see `keeper-spell-reaper.md`. ADR-0350.

**173** — `0x901D3950F7F8504D`. Restore constructor combat-bit defaults; the summon callback explicitly clears them. Authored Horny keeps fight acquisition. Every scenario's hashes change composition; see `keeper-spell-reaper.md`. ADR-0350.

**172** — `0x7C8753E946D1DCA4`. Reaper arrival, grace deadline, action, fade and combat-control state, plus the timer/variant words for the shared creation arm. Every scenario hash changes composition; see `keeper-spell-reaper.md` for stage-0 comparisons. ADR-0350.

**171** — `0xE109C983710B8617`. Creature Turncoat countdown and retained combat-side tag, both unsigned words; actual ownership stays unchanged. Every scenario hash changes composition; behavioral comparisons against stage 0 are in `keeper-spell-turncoat.md`. ADR-0350.

**170** — `0x08B3B3184DD609AE`. Eight world Tremor records and creature `Fleeing`, `ShouldDelayGetUp` and retained-route pause; the fixed records and false flags change hash composition even without a cast. See ADR-0350 and `keeper-spell-tremor.md`.

**169** — `0x1D8367ACE6D6D7CB`. Master's 168 merged into the room-health branch: both shapes, a room tile's `RoomHealthLost` and its `KeptManaGain` (ADR-0336, ADR-0334). **167** — `0x5127FACEAEC8EA6B`. A room's health, DK2's `room+0x35`, held as each room tile's `RoomHealthLost` (ADR-0336); no behaviour moves, as an untouched room's deficit is zero. **168** — `0x958573FFD0B603A6`. The m7/close merge (its 163–165 were taken first): an object's health and position and the Boulder's roll, speed, wear and signs (ADR-0333); a creature's heart player (`+0x2F5`) and death-shot flag (ADR-0337); the alarm's route word (ADR-0332) and area damage's 32-bit arithmetic change behaviour, not shape. **166** — `0x32B476C1ECE5CC30`. A hatchery's `ChickensGenerated`, `room+0x51`, on its lowest tile; a coop no longer stocks a hatchery, and the Hatchery finder posts 41–43 (ADR-0338; 163–165 were taken first). **165** — `0x3112DA3F61990DB7`. A tile's `KeptManaGain`, the floor's mana a room built over it keeps paying (ADR-0337); a Claimed Vault's `MaxManaGain` raises its owner's gain cap. **162** — `0x323B5E88EF7AFB82`. A keeper's trap no-mana clock, player `+0xAA` (ADR-0326; 159 before master took 160); trap upkeep, trap and door area damage and trap collision change behaviour, not shape. **161** — `0x1F7722DF305E58E1`. A Guard Post's room, trap `+0x113` (ADR-0330); it was 158 before master took 160. No scenario's event log or world changes (measured on 1aa98db). **160** — `0xA846A4DFE0257674`. A room tile's `RoomSizeChangedTick`, DK2's `room+0x9F` stamped at every rebuild; room messages 41–43 are retained (ADR-0327). **156** — `0xA8639EEF2B7A2350`. A trap's runtime words (health, heading, recharge and recovery deadlines, charge, Trigger count, hidden, threat and its clock, the Trigger's countdown and creature, the sight cache), a creature's tile-chain link and its counter (ADR-0321). **155** — `0x72C7236DC18B69AF`. A creature's Casino fearless countdown, flag and `FearlessUntil`, its `TortureVoluntary`, and a keeper's `NumberOfManufacturingCreatures` (ADR-0322). **153** — `0x1149D4B4FA9DC991`. A tile's `RoomRuined`, the ruin trigger action 32 writes on a Hero Gate (ADR-0320). **152** — `0xE459726930E9CA06`. A door's id, health and recovery deadline, a trap's id, both id counters, a creature's forced-target kind and `RebelActive`; blueprints name ids (ADR-0318). **149** — `0x62BD4E4816875CDD`. A keeper's `PortalsClosed` (DK2's `PortalsOpen`, player `+0x76` bit 10), written by action 37; a portal's clock restarts at every due roll (ADR-0314). **148** — `0xCF0197A5CA7ACEB4`. Training and Casino enter hooks refuse before their claims (ADR-0313); no new field: the countdown seed moves after their tests. No scenario's series and no rig log changes on efa3157. **147** — `0x75745303F30520A9`. A creature's held room place (ADR-0309). **146** — `0x31889C236E089472`. A tile's dig task that outlives its wall (ADR-0305). **145** — `0xC3DB6013EF7EF967`. The Angry flourish follows the art type (ADR-0301, Q188); no new field. **144** — `0xAFA4D2781BF3CBA8`. A creature's chicken timer (ADR-0302). **143** — `0x6A93081534CC88BD`. The Casino big-winner countdown the walk-over pick-up reads (ADR-0300). **142** — `0x5C3E476976F79986`. The payday clock armed by a keeper's first non-worker (ADR-0295). **141** — `0x1B3AF57D6F23BE5E`. A tile's mark order, and the gold an imp keeps in its hands (ADR-0288). **140** — `0x25381F6E02610FEC`. A shot's forced-collision word (ADR-0287). **151** — `0x2F51C47030E5B38B`. The Combat category's enter and leave hooks on every route in and out, and a fight's re-selection kept inside 199 (ADR-0319); no new field. **149** — `0x62BD4E4816875CDD`. A keeper's `PortalsClosed` (DK2's `PortalsOpen`, player `+0x76` bit 10), written by action 37; a portal's clock restarts at every due roll (ADR-0314). **148** — `0xCF0197A5CA7ACEB4`. Training and Casino enter hooks refuse before their claims (ADR-0313); no new field: the countdown seed moves after their tests. No scenario's series and no rig log changes on efa3157. **147** — `0x75745303F30520A9`. A creature's held room place (ADR-0309). **146** — `0x31889C236E089472`. A tile's dig task that outlives its wall (ADR-0305). **145** — `0xC3DB6013EF7EF967`. The Angry flourish follows the art type (ADR-0301, Q188); no new field. **144** — `0xAFA4D2781BF3CBA8`. A creature's chicken timer (ADR-0302). **143** — `0x6A93081534CC88BD`. The Casino big-winner countdown the walk-over pick-up reads (ADR-0300). **142** — `0x5C3E476976F79986`. The payday clock armed by a keeper's first non-worker (ADR-0295). **141** — `0x1B3AF57D6F23BE5E`. A tile's mark order, and the gold an imp keeps in its hands (ADR-0288). **140** — `0x25381F6E02610FEC`. A shot's forced-collision word (ADR-0287).
**139** — `0xF3B644CAA2F6FE61`. A worker's last task tile, and the job choice of ADR-0282.
**138** — `0xB858D2093C84A232`. The Call to Arms, its flag's process states and the heeding bit (ADR-0279).
**137** — `0x5113A130122CFD15`. The tick a creature was spawned or last put down (ADR-0278).
**136** — `0x4C67CCBC9B5CA5C4`. Mana-pressure clocks, removal stamp and corpse bit (ADR-0277).
**135** — `0x4289BE0A7A244163`. Stored worker idle flag (ADR-0274).
**134** — `0xBBEF0554E88AFB16`. Creature acquisition order, including transfers (ADR-0273).
**133** — `0x584EDA75014EEB18`. A keeper's portal entrance rows: boost and sack block (ADR-0272).
**132** — `0xFFF1AFE1DFB6A231`. A miner's standing slot and a floor pile's cell (ADR-0264).
**131** — `0xC0AF4F951938F85C`. Worker pickup flags on task exit and prison delivery (ADR-0263).

**130** — `0x1E3B2DF848B4A2F9`. Constructor worker flags and pickup gates (ADR-0262).

**129** — `0x1E64CAC720660CBC`. Per-player casualty-priority mask (ADR-0261).

**128** — `0xDBFD17A3B1351515`. Keeper-spell last-cast/last-checked clocks and
one-shot mana expenditure (ADR-0260). 127 was reserved for the doors/traps
branch, which changed no hashed state (ADR-0251), so it was never used.

**126** — `0x1C559815D4AB992B`, the research tree: a keeper's target, each availability row's `Upgraded`, a book's research kind and type (ADR-0247). **125** — `0x5D8E842FA7697068`. Persist the state-entry turn used by the
state-199 message prologue; ADR-0246.

**124** — `0x00C706A5C1D92221`, retained player messages and their next id (ADR-0245). **123** — `0x14316E84B1FDB0F7`, each room tile's place in its slab chain and the next one to hand out (ADR-0240). **122** — `0xED95A480A9DB92C5`, `LevelTimeLimit` and `SpecialInfoNextRoomTypeId` (ADR-0235). **121** — `0x954849F97CFC0296`, a door's orientation (ADR-0233). **120** — `0x88F728EFC351D95D`, what a keeper has learned from torture (ADR-0230). **119** — `0xE5005E1E7800B920`, a Prison's door (ADR-0229). **118** — `0x89637C687FBCC88D`, a spectator's remembered position and a Combat Pit's fight stamp (ADR-0227). **117** — `0xFA805D7B0DCA5668`, OldFightPos and OldFightTick (ADR-0225). **116** — `0xAACD3B15D9CFDCFF`, an idle worker's wander stamp and action timer (ADR-0224). **115** — `0x9D966A6EC7AC034A`, a tunnelling worker's plan (ADR-0222). **114** — `0x7AE4DE645C0A63A1`, a creature's origin tile, where a thief escapes to (ADR-0219). **113** — `0xFBE9C5B2CC6DE5EC`, `SkipNextPayment` joins `GetPaid` (ADR-0217). **112** — `0x2EF320B63DEDBBD1`, a hero's standing job and its party walk (ADR-0216). **111** — `0x92F1E00A712726B0`, the job a creature was dropped on (ADR-0215). **110** — `0x2B65E45AF14CB7A7`, a keeper's blueprint list (ADR-0214). **109** — `0x1754FA244591A3A6`, a Combat Pit fighter, its forced target and its state's turn (ADR-0213). **108** — `0xEE02B16BC2932E35`, a guard's facing, deadline and lost turn (ADR-0212). **107** — `0x01B0EBC839B5A5B4`, the activity is derived from the state and leaves the hash and the save (ADR-0211). **106** — `0x52719F6004D8BDBB`, every creature holds a state from its constructor on: 1 where it had held none, so the pinned world's two creatures moved it (ADR-0209). **105** — `0xDBC19E3981F90A36`, the Workshop's points, list and crates (ADR-0203). **104** — `0xFB68B3CB26783D05`, pay's cadence and the drop grace (ADR-0202). **103** — `0x1A5F99A4C9E39974`, freeing comrades (ADR-0201). **102** — `0x91DAFF09144DBA4F`, needs take a creature off its job (ADR-0198). **101** — `0x4543E72F3F20FE22`, a hero party raised by action 14 (ADR-0197); **100** — `0xFB318DBAE28A937F`, The Mistress's Torture job (ADR-0196).
**99** — `0xD079580BE0144072`, a prisoner walks each step in 116 (ADR-0194); **98** — `0xD686048703B6ED8D`, a captive on a torture device (ADR-0193); **97** — `0x99249045AA085298`, the countdown hooks (ADR-0192); **96** — `0xA2D20DF5116200CB`, a captive on DK2's state ids (ADR-0191).
**95** — `0xA22834D9084DFA9E`, the lever on a furniture slot (ADR-0190); **94** — `0x71F6772A33262059`, paid then to the Casino (ADR-0189).
**93** — `0xD783D2057BC97FE4`, the Casino's lever (ADR-0188); **92** — `0x669BBBA8B0751A77`, a creature's purse and the Casino (ADR-0187); **91** — `0xA5F9A90F3ED7133A`, work effects by category (ADR-0186); **90** — `0xFD1E03A96BC65015`, the dispatcher's category rule (ADR-0185); **89** — `0x584E9B5AC2195900`, an imp's bodies (ADR-0184).
**88** — `0x89B5AB8D13B66793`, objects by their flags (ADR-0183); **87** — `0x1B90606BEF13CDC6`, an imp's gold (ADR-0182); **86** — `0xDEB6A8B84200ED21`, its tile work (ADR-0181).
**85** — `0xE0EDFEC6BD215E6C`, collecting wages (ADR-0180); **84** — `0xDB5AAE21ABF713BF`, the anger wipes (ADR-0179); **83** — `0xDFC977B66E524362`, the lair and sleep (ADR-0178); **82** — `0xF1F260238F3D713D`, a job's walk and work (ADR-0177).
**81** — `0xE54D6E7BACCB5868`. Eating on DK2's state ids (ADR-0176). From 81 on, traces pin the chains.

**80** — `0xF53826B464578C5B`. The resume pair stops carrying a sentinel
(ADR-0175). `StateBeforeUtilityState` and `UtilityNewState` default to **0**, as
DK2's state-object constructor writes, not to -1; the resume slot is never unset
while a creature travels, so the transition rule may read it unconditionally.
A composition bump: both fields hold their default for the whole pinned run.

**79** — `0x6ACE9686C2A3E64E`. A bed is a hospital (ADR-0173). Waking needs
both an elapsed `TimeSleep` and full level-scaled health, as DK2's state 83
does. **The pinned world exercises none of it** — nothing in it sleeps
wounded — so the literal moved on the version alone. The behaviour is pinned
by two tests instead, both verified to fail with the predicate flipped off.

**78** — `0x00C68551383ADF89`. Rebellion (ADR-0172). Angry job 16 walks out
through its own keeper's nearest Portal, and with none reachable waits
`RebelCountdownTicks` and changes owner to the hero side, carrying
`JobType.KillPlayer` and `RebelAgainstPlayerId`. Two new hashed fields.

**77** — `0x2EC7D308858908C2`. The two Q402 divergences the hook read found (ADR-0171):
a cell bites `4 × PrisonModifyCreatureHealthPerSecond` every four game seconds instead of
one second's worth each second, and adds `ModifyAngerInCompanyOfHatedCreaturesPerSecond`
with it; anger cools in all seven states of categories 6, 9 and 10 rather than only asleep
in one's own bed. `CreatureState.PrisonWitherTicks` carries the per-creature phase.

**76** — `0xFD44E939D714F3F1`. The utility-state pair beside it — DK2's own
`StateBeforeUtilityState` (`+0x40`) and `UtilityNewState` (`+0xDF`). The second is the
resume slot the transition rule reads in place of the current state while the category
is 2, and it is where the thirteen travelling `CreatureActivity` entries land when they
retire together. Nothing writes either; composition only.

**75** — `0x51E5A81A5FE19DE8`. `CreatureState.Dk2StateId`, DK2's own state id beside
the retiring `Activity` enum (ADR-0135, decided 2026-09-10 and landed 2026-09-23).
Nothing writes it yet, so every creature hashes the unassigned `-1`: the bump is the
field's arrival, not a behaviour change. `-1` rather than `0` because state 0 is a
real row.

**74** — `0x434B838F6410D5F7`. State 8's flag and route-refresh countdown, the chase
counter, the retreating target given up, the celebration clock and the body target
entered on every creature (ADR-0169).

**73** — `0x187461CFD5C6ADF0`. The ten status words on every creature (armour,
invulnerable, frozen, invisible, speed ticks and percent, poison ticks, amount and
owner, the stun clock) entered the hash and the snapshot; every blow and shot passes
the health routine's multipliers (ADR-0168).

**72** — `0xE13D73F01C848BC5` (current). Every creature's three spell stamps, its
spell slot, the kind and slot of its current action and its last position; the shots in
flight with their own id counter (ADR-0167). Composition only in the pinned fixture.

**71** — `0xF7F8F30AF29050DF`. The alarm a Scout carries — the threat's
position, the deficit, the room — and the ticks stood reporting danger entered
(ADR-0166). Composition only in the pinned fixture.

**70** — `0x05719C220035BA50`. `TempFear`, `CurrentFear`,
`OverThreatCounter`, `BackOffTimer`, the run flag and the combat state's entry
tick entered (ADR-0165). Composition only in the pinned fixture.

**69** — `0xA02044E4AE17A79B`. The target's kind, the heart it names,
`TimeLastUsedMelee`, and the swing in progress with its start tick and its own
target entered (ADR-0164). Composition only in the pinned fixture.

**68** — `0xD8CDBD3CD4546586`. A creature's fight target, attack type,
`AttackRangeSquared`, `FightProcessCounter`, last fight-scan tile and its two
eight-slot engagement arrays entered (ADR-0163). Composition only in the pinned
fixture: no Blocker stands near an enemy there.

**67** — `0x8E6F0889810E2C53`. A player's `Status` and its tick, and
the dungeon heart's health, last attacker, death tick and ruin entered
(ADR-0162). Composition only in the pinned fixture: its players are not
keepers, so every new field is zero.

**66** — `0x8FE50E548AC844F2`. Each keeper's room, creature, door,
trap and spell availability rows entered, copied from the level and made mutable
so trigger action 6 can write them (ADR-0161). Composition and initial values
only: the pinned fixture fires no action 6.

**65** — `0x15441322D8A9D319`. The world byte holding the
trigger-controlled anger and notification gates entered (ADR-0160). Both stay
at their initial enabled values in the pinned fixture, so this is composition only.

**64** — `0x8FAF223321EE1523`. A keeper's alliance bits and a door's
lock-pick timer entered (ADR-0159: the rest of the door model). The pinned world
has no doors and makes no alliance, so this is composition only.

**63** — `0xE53ED0F1EDDD2036`. A creature's route option word and a door's lock
mode and open state entered (ADR-0158: doors in the navigation byte). The pinned
world has no doors and requests no route, so this is composition only.

**62** — `0xE4BA93698075EF4F`. The tile path, its index and the
movement remainder left the creature; its heading and the steering body's
surviving fields entered (ADR-0157 phase 3). The pinned world moves nobody, so
this is composition only.

**61** — `0x57435BC71CE1BBDC`. Creatures gained their saved trigger root
and repeat snapshot for the creature trigger walker (ADR-0152). The fixture's
creatures have no roots, so this is composition only.

**60** — `0xE44F66372F8963D9`. Graveyard rooms gained ordered, saved
per-room decomposition tallies (ADR-0151). The fixture has no Graveyard tally,
so this is composition only: the empty collection count and version move the pin.

**59** — `0x659AB04378E4C2FE`. Temple rooms gained ordered, saved
three-offering histories (ADR-0149). The fixture has no Temple history, so this
is composition only: the empty collection count and version move the pin.

**58** — `0xD5508579D84F193F`. Objects gained the saved `AutoPlaced`
ownership bit used by room furniture reconciliation (ADR-0146). The fixture has
no objects, so this is composition only.

**57** — `0xE9C81166EA405084`. Doors and traps gained derived, saved
`UnderConstruction` state for player trigger conditions 27 and 28 (ADR-0141). The
fixture contains neither kind, so this is composition only. **The literal was
computed on the merge**: 56 and 57 were developed in parallel from the same
55-based tree, so neither branch's own pin describes the merged state.

**56** — `0xAEC253948973DF35`. `CreatureState.DieInstantly`, a new hashed and
snapshotted bool, and with it a collapsed-window rule that reads the world rather than
the definition alone: four gates where there was one (Q303, ADR-0138). A state field
was added, so the snapshot format moves to 56 with it.

**55** — `0xCB555B61573BE9F8`, and `0xE5035AF2FB584476` until 2026-09-10,
when the gate's world gained two creatures and the literal was re-pinned **without**
a version change: the fixture moved, not the hash or the shape of the state (Q206).
A continuing marked dig task re-resolves the tile before each
application, so Reinforced Wall proceeds through Rock to Dirt Path (ADR-0134).
No state field changed; the version separates snapshots whose resumed jobs have
different behaviour.

**54** — `0x2820694B7F459227`. `TileState.WorkRemainder` **removed**. It
carried the fraction a tabled per-second delta left over when divided by five, so
the per-second rate held exactly. Run 15 read a reinforcing tile's health word 20
times a second and counted 54 consecutive steps of 9 against a tabled 9.6: DK2
loses the fraction every application. The accumulator was modelling a device the
original does not have, and the arithmetic that needed it is gone too (ADR-0133).

**53** — `0x94D2541428918DAC`. `PlayerState.SlapCount` (`player+0xEE6`,
condition type 71) and `PlayerState.PossessedCreatureId` (`player+0x134`, condition
type 73). Nothing writes either: no keeper here slaps or possesses, so both are
truly zero rather than stubbed, and the conditions read state instead of answering
a constant — the same reasoning as version 52's `KilledByPlayerId` (ADR-0121).

**52** — `0xB5C88C09424100AD`. `PlayerState.KilledByPlayerId`, the keeper
that defeated this one (`player+0x132`), read by condition type 47. Nothing sets it:
no keeper can be defeated yet, so it is always zero and the condition always false —
which is DK2's own answer for a living keeper. Hashed anyway, because a field the
simulation can be *given* is testable where a hardcoded false is not (ADR-0116).

**51** — `0x5C6A03ED5BD250BC`. `PlayerState.GoldMined`, the
counter behind trigger condition type 31 (ADR-0112). Composition: the fixture
mines nothing, so the field is zero for its whole run and the hash moves anyway.

**50** — `0x98DE8BA2B3BDC7BF`. Player condition type 32 reads the
walked player's mana, named `CurrentMana` by DK2's own debug dumper (ADR-0111).
Behaviour, not composition.

**49** — `0xDDC3EA20017640B6`. Hero parties and effect generators enter
ordered world state, every parsed field, nested member and variable effect-id
list hashed and snapshotted verbatim (ADR-0108). Composition: the fixture has
neither kind, so only their empty counts enter.

**47 and 48 were both taken and neither is what shipped.** Two branches ran in
parallel — 47 was reserved for hero parties, 48 taken by ADR-0110 when the gold
condition landed first — so the merge renumbered hero parties to 49 and this is
the only pin either change ever had in master. `0x7DD1C7A2B6EF5CE1` was 48's
value on its own branch.

**48** — Player condition type 30 reads the walked player's gold, so a shipped
level's script can branch on it (ADR-0110). Behaviour, not composition.

**46** — `0x52A068B601960E53`. An action DK2's dispatcher succeeds at
returns true whether or not this executor performs it, so the walker retires it
under an inherited repeat of 1 (ADR-0107). Behaviour, not composition: the
fixture runs no triggers, so only the version bump moves the value here, and the
campaign-wide measurement moved nothing either — every shipped action is a leaf.

**45** — `0x57E013D74DC65BAA`. Doors and traps enter ordered
world state with every parsed field copied verbatim, hashed and snapshotted.
Even the fixture's two empty collections add their counts to the hash.
`docs/research/doors-and-traps.md`.

**44** — `0x48D7950540013015`. Every action point's trigger tree is
walked each turn too, in the area context, and five of the seven condition types
that context owns are computed: 40, 41, 42, 62 and 69. No hashed field changed;
the literal moved because the version is hashed, and it had to move because a
level with an action-point root now evolves differently from one saved under 43.
`docs/research/trigger-walkers.md`.

**43** — `0x6E963365B44FD724`. Every player's trigger tree is walked
each turn as well, in the player context. No hashed field changed; the literal
moved because the version is hashed, and it had to move because a level with a
player root now evolves differently from one saved under 42.
`docs/research/trigger-walkers.md`.

**42** — `0xFFCC4119CA233DA7`. The world walker gains condition type 34
(level time), the arming half of action type 14, and the twenty-four party roots
— with the load-time sweep that starts every party subtree disabled. Only the
version constant moves this fixture's literal: it has no trigger tree and no
parties, so none of the new state exists in it. `docs/research/trigger-tree.md`.

**41** — `0xDEF9B4B1985D90DE`. Trigger-record active and repeat state,
including one-shot action retirement, enters the hash and snapshot. The fixture
has no trigger tree, so its record count remains zero.

**40** — `0x6445940E0B0F23A9`. The level's action points, in
THINGS-file order: rectangles, ids, loader-filtered flags, passage counters and
wait timers. Even this fixture's empty list adds its count to the hash.
`docs/research/action-points.md`.

**39** — `0x81D58B966898BEC8`. Corrected the trigger timer bank from 256 slots
to **sixteen**, 240 fewer dwords in the hash. Two readings agree on sixteen: the
next field on the owning object sits 0x40 bytes after the bank, and the
condition path compares the id against 0x10 before indexing.

**38** — reserved, never on master. Taken by the action-point branch in the
codex worktree so the two could not collide; the merged result took 40 rather
than reusing it.

**37** — `0xD17C2E424DDE0AF2`. `WorldState.Triggers`: the level's trigger flag
and timer banks (ADR-0102). They are hashed because an action record writes a
flag and a later condition reads it, so two worlds disagreeing about one diverge
as soon as that condition is evaluated. 128 flag bytes and — at this version —
256 timer dwords enter the hash even when every slot is zero. Version 39
corrected the timer bank to sixteen.

**36** — `0xD893704FB5D00309`. `PlayerState.ResearchPoints`, the Library's
accrual. A new player field moves the value by itself.

**35** — `0x368FDF9DF3D0A6EA`. Two changes, either of which moves the value
alone: `SimulationTiming.TicksPerSecond` went 20 to **four**, DK2's own rate,
re-timing every scheduled system; and `TileState.UpwardWork` was **removed**,
because DK2 accumulates an upward job in the tile's own health and converts at
the terrain's `MaxHealth`, so the cost is the deficit and not the whole maximum
(Q1; ADR-0024 reversed by ADR-0089).

**34** — `0x46FAACC93F8323FF`. Reason not recorded here; see git history.

**33** — `0x21A7E4D24F37DF48`. Job selection for non-workers (ADR-0082):
`CurrentJob`, `JobTileIndex`, a choice timestamp per mood, and the mood band
itself, which DK2 stores rather than recomputing and which this now stores for
the same reason — anger reaches a creature from four places and three of them
run before the anger system in the same tick.

**32** — `0x30722F6054924CC5`. The anger split (ADR-0081): one int becomes six.
DK2 keeps a creature's anger in an `int[6]` indexed by grievance and bands the
**largest** of them; this simulation kept one pooled counter. Two half-grievances
no longer add up to desertion, and a kindness in one bucket no longer pays off a
grievance in another.

**31** — `0xC026542547419606`. Payday (ADR-0078): three creature fields — the
wage owed, the failed-attempt counter and `SkipNextPayment` — and the next
payday deadline on the player. The pinned world has no treasury and its
creatures are level 1, so the value moved on composition.

[Versions 9–30](hash-versions-early.md) retain the earlier pins and their reasons.
