---
systems: [simulation-core]
---
# State-hash versions 170–249

Earlier pins and reasons from [the current version record](hash-versions.md).

**249** — `0x8C0EAA543500440F`. The room ticks run after the creatures and before the player frame, as `0x004EC450` does: the Casino's, the Portal's and room health move there, and the Library, Training Room, Guard Room and Workshop step `0x004E8C60` angers a room's place-holders when both alignments meet in it (ADR-0479); no new saved field.

**248** — `0xC51BD03052ECA96A`. The Heart's opening sequence is timed in game turns (DK2's turn 1 is our tick 0), so the ordinary frame's wait ends one tick earlier than under 247; no new saved field.

**247** — `0x9B410CB31AADB8BD`. The Dungeon Heart room's ordinary frame (regeneration, ruin) waits until nine seconds after its opening sequence starts, as `0x004D9A60` does (ADR-0475); no new saved field. The view effects of the sequence are neither saved nor hashed.

**246** — `0xD6C4B6C05FD11458`. The flee run and the hero reluctance count only Guard Rooms near the
attacker (`0x00460B50`); no new saved field. ADR-0473.

**245** — `0xF345C33D7F7605C3`. Worker takes need a route (ADR-0471) and a reachable dig side, object or body (ADR-0472); no new saved field.

**244** — `0x2B86A02DFCAA09DE`. A creature's `ClaimActionTile`, the tile a possessed worker's
claim strikes each tick, and a work order's multiplier and strike flags are saved and hashed
(ADR-0469); the gem rule (ADR-0470) and the possessed object arms (ADR-0468) add no field.

**243** — `0x768B6A51253B3081`. The navigation mesh is simulation state: a
refresh edits the standing mesh around each changed tile (the original's
partial rebuild, `0x004CE770`) instead of building a new one, so the mesh
depends on the order of edits and the hash folds in every triangle, point
and free list; a save carries them and the locate hint too (ADR-0461).
**242** — `0xC2A0D662C94D7F10`. Sleep entry aligns ordinary creatures to
their bed and uses its heading; Mistress entry places her after the bed's
door animation starts; departure selection rejects stale wander routes
(ADR-0460). No new stored field.

**241** — `0xD4E74C513D204899`. A tunneller's walks keep their destination within
its own tile (`TryRouteToStance`), so a worker off its stand tile's centre walks
there and digs instead of re-planning the walk every period. No new persisted
field; the version distinguishes the changed resumed behaviour. ADR-0459.

**240** — `0x8FBB003CBA25CFD2`. Ownership-loss completion releases unsupported
walls using the native neighbour/corner rules; live Heart platforms are
excluded from movement and routing, with ruin reopening them (ADR-0451/0452).
No new persisted field; the version distinguishes changed resumed behaviour.

**239** — `0xBE0B8CC626E76E53`. Lair drops enter state 79, select a bed in
the landing room and release an old bed before the jittered walk to state 80.
No new saved field. ADR-0450.

**238** — `0xE834B736C858816C`. Claim, enemy-floor attack and room conversion
retain a centre destination for same-tile approaches. Visual effect requests
do not enter saved or hashed state. ADR-0449.

**237** — `0xBC650457E0557295`. Route-less fight states integrate contact;
in-range fighters face their target and hold melee through the authored
animation-completion poll. No new saved field. ADR-0447.

**236** — `0xC3C0A8A82912396E`. Combines possession followers/inputs with failed hero
jobs running the native idle wrapper;
same-tile wander and training routes retain their destination waypoint.
ADR-0641, ADR-0445–0446.

**235** — `0xF586FE56CCF63F2F`. The possession/arms branch: followers (leader, route deadline, count, shared melee target; ADR-0445) and inputs (disguise, sniper mode, hypnosis hold, old owner, mark and attack counts on creatures, the sniper bit on shots; ADR-0446) are saved and hashed. Classic's unmerged idle fixes used `0x7033026B2672013F`; 236 separates the combined snapshot.

**234** — `0x9EE6056B64A1FD38`. The claim priority refuses a tile holding a closed, completed door (`0x0046AB50`'s door gate); no new saved field. ADR-0443.

**233** — `0x538823E3806E3E01`. Combines master's Receive Imps with saved/hashed
room-build waves, shared ambient rat updates and original Portal heading.
Includes classic versions 226–230. ADR-0442. Branch 96fa1942 reused 232
with pin `0x745E1A08B1224DFA`; 233 separates the merged snapshot layout.

**232** — `0xE5DB51390ABD6056`. Special 124, Receive Imps, creates ten level-5 Imps round the Heart; no new saved field. ADR-0441.

**231** — `0xB8FA72F6DE9C12A1`. A room on `Room_ComputeTotalCapacity`'s default arm (Guard Room, Hatchery, Treasury, ...) holds its tile count; no new saved field. ADR-0440; versions 226–230 were used by the parallel classic-look branch.

**230** — `0x0117CFA8C106044C`. Alarm retreat routes to the random room-chain
point instead of the fixed centre; Portal, Heart and bridge tiles are never
floor slots for that selector (combat-alarm.md §2).

**229** — `0xF2A7DEB8B43F2FC3`. Registered attackers are acquired before the
fight-style scan; Imps bypass its distance cap while retaining visibility and
route checks (combat-acquisition.md §6).

**228** — `0x315162363F8047CA`. Heart combat targets resolve the three Arch
Pillars at their builder positions instead of the outer room ring;
acquisition, approach, melee and shots use the same point (combat-acquisition.md §9).

**227** — `0xCBBDEF9956A975B1`. Live Heart cores reject movement segments and
route targets resolve to their edge; ruins release the structure mask and obstacles. ADR-0439.

**226** — `0xB369A6E155352AB0`. Supported animation lifecycles retain training actions, object animation stamps and physical poses, and the Prison door opening stamp. ADR-0438.

**225** — `0x2B74D4ED96BA549B`. Imps turn toward the task tile before digging, mining, reinforcing or repairing its wall, holding the entry state until aligned. ADR-0435.

**224** — `0x236F0479C5BE1A9E`. Owned Hatchery chickens run their movement/reaction loop, with saved and hashed heading, age, reaction state, movement block and room membership. ADR-0434.

**223** — `0x4A7D0294815B3F49`. Hero followers retain sub-tile destination walks after reaching their trailing tile. ADR-0433.

**222** — `0xD423AF7C779275F4`. Hatchery meals hold state 94 until action 26's first animation cycle completes, then use their captured remaining-food decision to resume the chicken search or idle. The captured decision is hashed and saved. ADR-0432.

**221** — `0xFB311154736B9A1B`. Waiting heroes walk to their chosen sub-tile points instead of receiving an already-finished route. No new saved field. ADR-0430.

**220** — `0xA2446806EE35E55E`. Combat updates creatures newest first, matching the original head-linked list. No new saved field. ADR-0429.

**219** — `0x83702433083CA7D9`. Hatchery eggs use the normal non-pillar slab path and two sub-tile draws; hatching and presentation retain coordinates. Existing saved position fields, no new field. ADR-0428.

**218** — `0x6ECAB92D5B5D6E64`. An outmatched fleeing Scout runs to raise the alarm; the near-room gate uses the attacker's tile and respects Call to Arms. No new saved field. ADR-0425.

**217** — `0xACDC3C26931DA73F`. Ordinary obstacle queries preserve tile-head order and retained set/toggle flags; grounded water speed is halved in entity units. No new saved field. ADR-0423 and ADR-0424.

**216** — `0x4C65FAEF24CB1622`. Torture attachment uses actual device positions and wall facing; wheel/chair obstacles participate in continuous steering. No new saved field. ADR-0420.

**215** — `0x867E1B17FFDAB65D`. Level-placed flag 0x40 creatures land into cells/devices; involuntary torture and state 76 prevent attackability; DoubleQuad interiors carry nav byte 4, with the Prison delivery option; wall torture companions are simulation devices; wall machines use their working-position inset (Q581); ruined-at-load free casts reject corner rock/outside-map points. No new saved field. ADR-0419.

**214** — `0xF18C419AE1575DF8`. A corpse is laid only on a floor slot with a tombstone, objects 31–34 (`0x004537E0` in `0x004E6380` and `0x004E6490`); the Graveyard's crypt (object 75, `0x004DD5C0`) placed on a rebuild, refusing its cell as a slot and raising the Vampire; a Graveyard rebuilt in play takes in the dead bodies on its tiles not yet its owner's (`0x004DD750`), through the new saved `CreatureState.BodyOwnerPlayerId` (the dead body's `+0x28`, neutral at death), which the corpse selections also require neutral; the test fixture's Graveyard takes its shipped flags and ObjectIds; no new field. ADR-0418 amendment.
**213** — `0x973803F63EBFD481`. A delivered corpse lies on a Graveyard floor slot (new `CreatureState.GraveyardSlot`, the dead body's `+0x132`, saved) until its lifetime ends and it is buried; a Graveyard's place counts its lying corpses; the pick-up takes the first Graveyard in room order with a free slot; a freed room lets its corpses go. ADR-0418.
**212** — `0x1CE851EB002A6064`. A collapsed own creature with no bed is carried to the nearest Lair with space and makes its bed there (`0x0046D810` result 4, `0x0046E610`, state 45 and 79); a bed lost during the carry re-routes to such a Lair; the room point `0x004E8430` is shared; no new field. ADR-0417.
**211** — `0x8800AAF8F8F81A4F`. A researcher's Library is the Library finder's (`0x004E2500`): a shelf draw per reachable Library on every search, the nearest by first slab, one with no holder of the other alignment first; no new field. ADR-0416.
**210** — `0x0D827AF91A745AD2`. A Casino's party moves with the room: kept as it grows, the joined room's kept in a merge, ended in every piece when a tile leaves (`Room_Construct`, `0x004ED440`); no new field. ADR-0415.
**209** — `0xE29E7F167923B105`. The corpse selections' Graveyard messages (41 once a level through the new `PlayerState.GraveyardWantedPosted`, player `+0xF4E`, saved; 42 with no place), a corpse taken only from its keeper's tile and only to a Graveyard with a floor slot. ADR-0414.
**208** — `0xDF2D50DC47317F6E`. The job-room finders' remaining messages (Workshop behind its queue, the Library's shelf test and 45, the Casino's party branch, the Mistress's 99, both Combat Pit finders) and Temple and Combat Pit capacity (their rim, `0x004E3ED0`); no new field. ADR-0413.
**207** — `0xEC031DFFFBBCFE6B`. The rescue selector's room messages (`0x0046D810`: 41/43/42 for the Prison or a bedless body's Lair) and the Prison's `Activation` (`+0x59`): on at a Prison's first tile and at map load (`0x004E2B30`), kept with the room as it grows or is sold, and required by the carry's Prison search; a casualty is taken only from a tile its keeper owns; no new field. ADR-0412.
**206** — `0x817A546857D0D020`. A Dungeon Heart's update (`0x004D9690`) sends its owner's creatures, workers included, running to an attackable enemy within `DungeonHeartReportingDistanceTiles` of it every fourth turn; no new field. The pinned world carries neither radius, so the value moved on composition. ADR-0407.
**205** — `0xA3E9B6DDE3F06795`. A room's place-holders are kicked by a neighbouring tile's terrain change (`0x004E44E0`, the latest claimant only), a new owner (slot `+0x0D`) and the room being freed (slot `+0x05`), and a Torture Chamber's victims through 136; new fields `TileState.RoomKickedTick`, `CreatureState.HeldPlaceSerial` and `WorldState.NextPlaceClaim`. ADR-0406.
**204** — `0x5C3A1B89341D720F`. The keeper's slap step reads a worker's last task kind (`+0x98`) rather than a held reinforce target, and a repair records kind 16, not 6 (`0x0046AEA0`); no new field. ADR-0405.
**203** — `0xAEBBF589AE314BD2`. The slap's state moves 67, 77 and 120 and their reaction (`slap-states.md`): a slap whose move is refused does nothing more; the reaction plays action 19 and goes back to its room's state; no new field. ADR-0401. Built on 202; 201 was this work's number while it waited on a side branch and is unused.
**202** — `0x6967DA71375E1109`. The room return states 62, 70 and 154 through `RoomEvictRules`, and the research cheer 60 on a shelved item (`room-return-states.md`); no new field. ADR-0402. (201 unused: see 203.)

**200** — `0x587802C58EC02B4B`. Category 0 states 5, 71, 86, 113, 126 and 135 (`refused-states.md`): every neutral creature idles in 5, the Heart's drop 71, the tunneller's 86, the evicted holders' 113 and 126 through `RoomEvictRules`, the willing torturee's 135, the gold cheer 211; the Bile Demon's idle shot; the Temple's offering only in its pool; no new field. ADR-0400.

**199** — `0xF759489159B4EB3E`. The Workshop's states 63–65 (`workshop-states.md`): the machine mode, state `+0xB9` (creature `+0x307`), and the Library's target point renamed to the shared `StateTarget`. ADR-0404.

**198** — `0x6B9E7F7D302E3BF3`. The Temple's states 101–107 (`temple-states.md`): creature `HeldPlaceStamp` (ours, the held room's size stamp at the claim, for 107), and every job's walk to a room ends at DK2's room point `0x004E4C10` rather than the room's centre. ADR-0403.

**197** — `0x7E3268744B92B012`. The Library's states 53–59 (`library-states.md`): the researcher's target point (state `+0xA9`/`+0xAD`), the watched creature (`+0xA7`), and the Casino's wait position renamed to the shared `WaitPos`. ADR-0397.

**196** — `0xC44B4149D9B1725D`. The Casino's states 145–153, the jackpot, its party and its winner's 152 and 155–157 (`casino.md`): creature `+0x2FF`, state `+0xAC`/`+0xB4`/`+0xB8`, the drunk timer `+0x4CE`, the winner's `+0x3F2`/`+0x3F4`, room `+0x41`/`+0x45`/`+0x3D` on the Casino's lowest tile and player `+0xF4A`. ADR-0396.

**195** — `0x95D304A3DA5B37B4`. A trainee at the Training Room's level cap goes from state 108 through 110 to idle (`0x00485310`, `0x00485580`); no new field. ADR-0393.

**194** — `0x17E9192AEC50EC93`. An imp's gold fetch walks to the first neutral pile's point, state 27 runs when that walk ends (the pile need not still be there) and takes only the piles strictly within `0x555` (`0x00466850`, `0x0046B6D0`). No new field. ADR-0389.

**193** — `0xC5E69C4191F2E3A6`. Only a keeper's own workers hold, crowd or lose its tasks; the fight approach walks to a target on its own tile (one-point route, `0x004CF490`); no new field. ADR-0388.

**192** — `0x40EF9AF6D79DA685`. Imps repair their keeper's damaged wall (kind 16, `0x00453E30`), and a work application truncates the tabled rate over four before flooring it times the work rate (`0x0051CBA0`); no new field. ADR-0387.

**191** — `0xC5322686209DAEB0`. An imp task walk runs the walk wrapper's ability scan and casts Imp Haste from level 4 (`0x004651A0`); work experience is paid once per work hit, not per tick of work. No new field. ADR-0385, ADR-0386.

**190** — `0xEE1C266107F94AAF`. The binary sweep's fourth fixes, the imp's: the claim on the way before the walk-over pick-up, from a worker job only, and the pick-up from the imp's own tile only; the ring scan's own tile unfiltered; a failed walk to a free reinforce side deletes the wall's task; a crate's blueprint before the shared latch; state 27's take stopping at the pile that fills the hands. No new field. ADR-0378 to ADR-0382.

**189** — `0xFE655F271EBBEB12`. Trigger action 18 makes or breaks an alliance (`0x00519376`), where it was left unperformed; no new field. ADR-0377.

**188** — `0xCAAC539B930BB381`. The binary sweep's third fixes: the Graveyard's level-scaled burial, capacity gate, zeroed tally, random Vampire tile and tally lost on a sale; Vampire reincarnation; trigger conditions 20 and 21; slaps counted for the slapped creature's owner; flag 128 as the level score. No new field. ADR-0370 to ADR-0375.

**187** — `0xDCB551C8DB3F790C`. The binary sweep's second fixes: non-ownable ground to the neutral player, trigger action 22's sale and room build, no victory at a collapse, the forced creature target's spell, combat experience, shots newest first, the victory pause by clip, the Flanker's heading and the turn to the body; three effect-generator fields DK2 never reads leave the hash. No new field. ADR-0362 to ADR-0369.

**186** — `0x540CF5C4A50095C3`. The binary sweep's first fixes: the carrier's store tile and walk point, a draw per new store pile, the sweep's destroy and Treasury gate, the claim search's fall-through, the mana clamp, the portal tally on death, carried bodies' clocks, the heart's start, and the trigger root order; new field `CreatureState.PortalEntranceTile`. ADR-0355 to ADR-0361.

**185** — `0xD840FA98E806236E`. No imp sets off for an enemy while every Prison is full; a body refused at the door is put down on the room point 0x004E8430 draws from the world stream; no new field. ADR-0354.

**184** — `0xB22ED6D42A6AD975`. Tile work posts 12 to the owner of a Solid, Ownable tile another player's work brought down and 34 to the owner of a room a Good creature's blow left standing; no new field. ADR-0353 stage 6.

**183** — `0x683FC33F7E712118`. The Training, Temple, Torture, Casino, Library and Workshop finders post 41, 43, 42 or the new 44 (its subtable) when they find no room; no new field. ADR-0353 stage 5.

**182** — `0xDC5ABB62C9123E6F`. The claim 0x004E5A70 posts 42 when no other room of the type has a place the creature can reach, and for a Prison past its limit; a 42 for a type off its subtable keeps its terminator record a sweep; no new field. ADR-0353 stage 5.

**181** — `0x402E9BC4C3C96C2A`. The room transfer and the defeat handover post 35 to the old owner and 32 or 33 to the new, with the 32 to 35 subtables; no new field. ADR-0353 stage 4.

**180** — `0x68CC6F319486D231`. `MintWageCommand`: the keeper's wage spent from the stores in shares and put in its hand as a pile; no new field. ADR-0353 stage 2.

**179** — `0x2D8E29670FC89364`. The anger wrapper posts 17 and 18 on an upward band change and the sweep frees one whose creature left the band; no new field. ADR-0353 stage 1.

**178** — `0x030F88C2BFE9ADEB`. A Sight of Evil cast (spell 1) is accepted at a place: permission, payment, one-shot mana and both clocks, with no shot made; no new field. The pinned world casts nothing, so the value moved on the version alone. ADR-0352.

**177** — `0x3C779015EE115C66`. The mixed-class tile chain link on creatures, objects, traps, doors and shots with its world counter, and action 15's point, pitch and range on the creature; the possessed action command (action 26) is built. Scenario event/world outputs are unchanged; see `keeper-spell-possession.md`, Possessed actions.

**176** — `0x207022305EFDD86A`. A creature's height and previous height, written by possessed movement (action 25); the move command is built. All 42 scenarios keep their event/world outputs and change hash composition only; see `keeper-spell-possession.md`. ADR-0350.

**175** — `0x6F9C6A3B4D22F803`. Creature possession/influence flags and influence deadline; entry/exit and stat modifiers are built. All42 scenarios change hash composition only; see `keeper-spell-possession.md`. ADR-0350.

**174** — `0x8213FCEC0D1B3EA0`. Player OneShotReaper-used flag survives removal and snapshots; variant creation and its countdown are built. Every scenario hash changes composition; see `keeper-spell-reaper.md`. ADR-0350.

**173** — `0x901D3950F7F8504D`. Restore constructor combat-bit defaults; the summon callback explicitly clears them. Authored Horny keeps fight acquisition. Every scenario's hashes change composition; see `keeper-spell-reaper.md`. ADR-0350.

**172** — `0x7C8753E946D1DCA4`. Reaper arrival, grace deadline, action, fade and combat-control state, plus the timer/variant words for the shared creation arm. Every scenario hash changes composition; see `keeper-spell-reaper.md` for stage-0 comparisons. ADR-0350.

**171** — `0xE109C983710B8617`. Creature Turncoat countdown and retained combat-side tag, both unsigned words; actual ownership stays unchanged. Every scenario hash changes composition; behavioral comparisons against stage 0 are in `keeper-spell-turncoat.md`. ADR-0350.

**170** — `0x08B3B3184DD609AE`. Eight world Tremor records and creature `Fleeing`, `ShouldDelayGetUp` and retained-route pause; the fixed records and false flags change hash composition even without a cast. See ADR-0350 and `keeper-spell-tremor.md`.
