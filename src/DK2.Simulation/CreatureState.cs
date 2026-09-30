// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a creature is currently doing.</summary>
/// <remarks>Not DK2's own creature state machine, which is much larger; reconstructing it
/// is a later job (Q2, Q7, docs/research/creature-state-table.md). Several
/// members fold two or three of the original's states into one, which is said
/// where it happens.</remarks>
public enum CreatureActivity
{
    /// <summary>Standing still, waiting to re-evaluate what to do.</summary>
    Idle = 0,

    /// <summary>Walking towards a tile it intends to work on.</summary>
    MovingToJob = 1,

    /// <summary>Standing next to its target tile, applying work to it.</summary>
    Working = 2,

    /// <summary>Carrying gold to somewhere its keeper can keep it. A state rather than a
    /// <c>JobPriority</c> because it is the tail of a mining job, not a job an
    /// imp chooses (Q31, ADR-0018).</summary>
    DeliveringGold = 3,

    /// <summary>Walking to its own bed in a lair, to sleep in it.</summary>
    GoingToSleep = 4,

    /// <summary>Asleep in its own bed — the only state in which a lair heals a creature
    /// (ADR-0049, CONFIRMED by the owner).</summary>
    Sleeping = 5,

    /// <summary>Collapsed at zero health, lying on the ground, not yet dead: DK2's
    /// <c>CreatureDyingStateDurationSeconds</c> (60) window, which ends in
    /// rescue or death (ADR-0051, CONFIRMED by the owner).
    /// <para>Does nothing at all — no moving, working, sleeping, regenerating or
    /// growing hungry — but keeps its bed, because the way out is an imp
    /// carrying it back to that bed (docs/research/creature-death.md).</para></summary>
    Dying = 6,

    /// <summary>Walking out of the dungeon for good, having drawn <c>Leave</c> from its
    /// angry job table: to the nearest available portal, so a keeper whose
    /// portal is unreachable keeps a creature that wanted to go (Q116, CONFIRMED
    /// by the owner 2026-09-04; docs/research/portals.md).
    /// <para>A leaver has given up its bed, will not claim another, and is
    /// skipped by <see cref="Systems.CreatureSleepSystem"/> entirely.</para></summary>
    Leaving = 7,

    /// <summary>Walking to a pile of gold lying on the floor, to pick it up. Fetching
    /// floor gold outranks marked excavation, which had been the top of the
    /// priority order since ADR-0018 (Q141, CONFIRMED by the owner 2026-09-05;
    /// docs/research/gold-on-the-floor.md).
    /// <para>Only the walk and the pick-up; carrying it home is
    /// <see cref="DeliveringGold"/> whatever put the gold in the hands.</para></summary>
    FetchingGold = 8,

    /// <summary>Walking to a lair to put a bed down, and the state every non-worker
    /// enters first. Unlike <see cref="GoingToSleep"/> it walks to a
    /// <i>room</i>, chosen by distance, and picks the tile on arrival,
    /// reserving nothing — so two creatures may head for the same room and one
    /// may find it full (ADR-0060, docs/research/lair-acquisition.md §4, §5).
    /// <para>Ends with a bed placed — sleeping at once below
    /// <c>CreatureSleepsWhenBelowPercentHealth</c>, idle otherwise — or with
    /// nothing, and then retries at the next idle decision.</para></summary>
    SeekingLair = 9,

    /// <summary>Dead and lying on the floor as a corpse, for
    /// <c>DeadBodyDiesAfterSeconds</c> (60): an imp may carry it to a graveyard
    /// inside that window, and it disappears if nobody does (Q99, CONFIRMED by
    /// the owner 2026-09-04). Only records carrying
    /// <see cref="Data.Model.CreatureFlags.GenerateDeadBody"/> reach this state;
    /// an Imp dies at once, and so does a Vampire, which reincarnates.
    /// <para>None of the 145 OBJECTS records is a corpse (docs/research/objects.md §4).
    /// DK2 makes it a <c>CDeadBody</c> thing; ours keeps the creature, holding no state
    /// id (ADR-0206).</para></summary>
    Dead = 10,

    /// <summary>Walking to a body — collapsed or dead — in order to pick it up. DK2's
    /// imp task kinds 13 and 12.</summary>
    FetchingBody = 11,

    /// <summary>Carrying a body to where it has to go: a collapsed creature to its own
    /// bed, a corpse to a graveyard (<see cref="CarryRules"/>). Distinct from
    /// <see cref="DeliveringGold"/> because a body is a creature still running
    /// its own clock, and what happens on arrival depends on which clock it is.</summary>
    CarryingBody = 12,

    /// <summary>Standing in somebody else's prison, losing health — the far end of DK2's imp task
    /// kind 13, and put here only by an imp (CONFIRMED, owner 2026-09-05). The holding keeper's
    /// hand may lift it out, the one case where the Hand of Evil touches a creature its keeper does
    /// not own (<see cref="HandRules.CanPickUp"/>). Rules, rates and what is unbuilt:
    /// <see cref="PrisonRules"/>, docs/research/prison.md.</summary>
    Imprisoned = 13,

    /// <summary>Walking to a loose object in order to pick it up — DK2's imp task
    /// kind 8.</summary>
    FetchingObject = 14,

    /// <summary>Carrying a loose object to the room it belongs in
    /// (<see cref="ObjectCarryRules"/>). Distinct from
    /// <see cref="CarryingBody"/> because DK2 keeps two slots side by side in
    /// its creature record: <c>CarryingId</c> for a thing, <c>DraggingId</c> for
    /// a creature.</summary>
    CarryingObject = 15,

    /// <summary>Walking to a hatchery, because it is hungry — to a room, not to a bird.
    /// <para>The original picks the nearest reachable hatchery of the creature's
    /// own keeper and looks for a chicken only once standing in it, so an empty
    /// hatchery leaves the creature standing in it, hungry, asking again a
    /// second later (docs/research/eating.md; <see cref="EatingRules"/>).</para></summary>
    SeekingFood = 16,

    /// <summary>Walking to one particular chicken it has picked out, inside the hatchery
    /// it is standing in.
    /// <para>Nothing is reserved: the chosen bird's id lives on the creature and
    /// nowhere else, so two hungry creatures may pick the same one and the
    /// second to arrive finds it gone and looks again (Q172, CONFIRMED by the
    /// owner).</para></summary>
    FetchingChicken = 17,

    /// <summary>Walking to a treasury to collect wages, and taking them off the floor on
    /// arrival — the original's states 187 (choose a room and walk), 188 (notice
    /// arrival, re-check the room) and 189 (take the gold) folded into one
    /// (ADR-0078, docs/research/payday.md).
    /// <para>The wage is credited before the walk, so a creature that never
    /// reaches a treasury stays owed rather than losing it.</para></summary>
    SeekingWages = 18,

    /// <summary>Walking to the room a job is done in — the non-worker's equivalent of
    /// <see cref="MovingToJob"/>, which belongs to workers and the tile-work
    /// loop (ADR-0082).
    /// <para>Which room is <see cref="JobRules.RoomFor"/>; the walk is the
    /// hatchery walk exactly — nearest room of that type belonging to this
    /// creature's own keeper, first reachable one wins.</para></summary>
    GoingToJob = 19,

    /// <summary>Standing in the room, doing the job in
    /// <see cref="CreatureState.CurrentJob"/> — one state where DK2 has one per
    /// job (0x35 research, 0x3f the workshop, 0x61 guarding, 0x65 the temple,
    /// 0x6c training, 0x91 the casino, 0x55 sulking in a lair).
    /// <para>Three jobs act here: Praying (ADR-0081, the only source in the game
    /// that drains an anger bucket it did not fill), Train and Research. Not
    /// implemented: guarding, the workshop and the casino — a creature at those
    /// stands in the room, which is still a second it is not idle and not taking
    /// <c>AngerNoWork</c>.</para></summary>
    AtWork = 20,

    /// <summary>Engaged with an enemy — DK2's states 199 and 200, "engaging in battle", which
    /// a creature enters by acquiring a target and never by being told to fight
    /// (docs/research/combat-acquisition.md). What it does in the state — approach, swing,
    /// flee — is <c>spFighting</c> (docs/research/combat-fight-state.md).</summary>
    Fighting = 21,

    /// <summary>DK2's state 203, "fleeing": backing off from its attacker for <see cref="BackOffTimer"/>
    /// ticks, then running (docs/research/combat-fear.md §4).</summary>
    Fleeing = 22,

    /// <summary>DK2's state 6 on the way to 202: running to the withdraw destination — lair, heart,
    /// a room or a nearby tile — and idle on arrival (docs/research/combat-fear.md §5).</summary>
    Withdrawing = 23,

    /// <summary>DK2's state 6 on the way to 251: a Scout running to its nearest room to raise the
    /// alarm; on arrival that room's creatures are sent at the threat (combat-alarm.md §3).</summary>
    RaisingAlarm = 24,

    /// <summary>DK2's state 6 on the way to 254, "reporting danger": a creature sent by the alarm
    /// running to the threat's position, then standing thirty ticks there looking for a fight
    /// (combat-alarm.md §4).</summary>
    Reporting = 25,

    /// <summary>Standing frozen — DK2's state 218, whose handler stands while the word
    /// <see cref="CreatureState.FrozenTicks"/> runs and then goes idle (creature-status-effects.md §2).</summary>
    Frozen = 26,

    /// <summary>Stunned by Lightning or the Reaper Fireball — DK2's state 255, standing for
    /// <c>StunDuration</c> (<see cref="CreatureState.StunTicks"/>) at double damage, then idle.</summary>
    Stunned = 27,

    /// <summary>The victory pause — DK2's state 260 with action 55, one animation instance after a
    /// kill; ours lasts <see cref="Combat.VictoryRules.PauseTicks"/> (combat-approach-and-victory.md §7, Q376).</summary>
    Celebrating = 28,

    /// <summary>Walking to a nearby corpse to celebrate over it — DK2's state 6 resuming in 259, taken
    /// by <c>NeedBodyForFightIdle</c> creatures (combat-approach-and-victory.md §5—§6).</summary>
    GoingToBody = 29,

    /// <summary>A captive strapped to a torture device in its keeper's Torture Chamber: DK2's
    /// states 130 and 131, category 15 (ADR-0193, <see cref="TortureRules"/>). It stays that
    /// keeper's captive, so everything that leaves a prisoner alone leaves it alone too.</summary>
    BeingTortured = 30,

    /// <summary>Freeing an ally from an enemy's torture device: DK2's state 134 and its walk
    /// (ADR-0201, <see cref="RescueRules"/>).</summary>
    Rescuing = 31,

    /// <summary>In a Combat Pit: DK2's states 137, 138, 139 and 141 and 139's walks, category 16
    /// (ADR-0213, <see cref="CombatPitRules"/>). The fight itself is <see cref="Fighting"/>.</summary>
    PitFighting = 32,

    /// <summary>Heeding its keeper's Call to Arms: states 190 to 192 and the walks resuming them
    /// (<see cref="CallToArmsRules"/>).</summary>
    HeedingCallToArms = 33,

    /// <summary>State 75, the hand's preparation (<see cref="HandRules.PreparationState"/>): entered
    /// before the hand's capacity is tested, and inert while it lasts.</summary>
    BeingLifted = 34,
}

/// <summary>One creature in the world.</summary>
/// <remarks><para>A class rather than a struct because a creature carries a path, and
/// because creatures are few where tiles are tens of thousands.</para>
/// <para>Stored in a list in spawn order and never sorted: the order creatures
/// are updated in is behaviour, as the work queue's order is (ADR-0010).</para>
/// <para>Only fields some implemented mechanic reads are here, as for
/// <see cref="TileState"/>.</para></remarks>
public sealed class CreatureState
{
    /// <summary>Stable identity for the life of the level. Assigned by <see cref="WorldState"/>.</summary>
    public int Id;

    /// <summary>Index into the CREATURES table — the definition this creature is an instance of.</summary>
    public byte CreatureTypeId;

    // Change a live creature's owner through WorldState.ChangeCreatureOwner so list order follows it.
    public byte OwnerPlayerId;

    /// <summary>The root of this creature's trigger tree, or zero for none.</summary>
    /// <remarks>Copied from the placed THING and walked once per game turn. The binary's
    /// root setter also snapshots the resolved condition record's repeat byte into
    /// <see cref="TriggerRepeat"/> (docs/research/trigger-walkers.md).</remarks>
    public ushort TriggerId;

    /// <summary>The repeat argument snapshotted when <see cref="TriggerId"/> is attached.</summary>
    public byte TriggerRepeat;

    /// <summary>Position within the dungeon in <see cref="Fp32"/> units, where
    /// <see cref="Fp32.One"/> is one tile: the centre of tile (x,y) is
    /// <c>(x * 4096 + 2048, y * 4096 + 2048)</c>. Never negative — the dungeon
    /// starts at the origin.
    /// <para>Sub-tile because travel time is gameplay; fixed point rather than
    /// floating so the same walk arrives on the same tick everywhere
    /// (ADR-0001).</para></summary>
    public int PosX;
    public int PosY;

    /// <summary>DK2's steering body, which the original embeds in the creature at <c>+0x124</c>
    /// (docs/research/steering-behaviours.md §7.5): velocity, the route window, the stuck history
    /// and the path flags. Its position is re-derived from <see cref="PosX"/> and <see cref="PosY"/>
    /// before every step, as the original does, so it is not hashed; everything else in it is.</summary>
    public readonly Steering.SteeringBody Body = new();

    /// <summary>Facing, 2048 to a full turn (<see cref="Steering.Bearing"/>). A creature turns to
    /// face its waypoint before it moves (steering-behaviours.md §5.3).</summary>
    public int Heading;

    /// <summary>The option word of this creature's last route request
    /// (<see cref="Navigation.MoveOptions"/>): its door bits stay with the creature, as
    /// <c>creature+0x3E6</c> bits 17–19 do, and drive every later re-request and wall probe.</summary>
    public int RouteOptions;

    /// <summary>Gold in this creature's hands: mined into its own hands up to
    /// <see cref="Data.Model.CreatureDef.MaxGoldHeld"/>, carried to a place its
    /// keeper can keep gold, put down there (Q31, CONFIRMED by the owner).
    /// <para>On the creature rather than the player for the same reason gold is
    /// on tiles (ADR-0015); while it is here it is not spendable. Survives
    /// <see cref="ClearJob"/> — an imp whose seam collapsed still holds what it
    /// mined.</para>
    /// <para>Also every creature's purse, <c>creature+0x3FB</c>: half of each wage and the
    /// Casino's winnings land here (<see cref="PurseRules"/>, ADR-0187). Not seeded from
    /// <c>InitialGoldHeld</c>, which DK2's loader copies and nothing reads.</para></summary>
    public int GoldHeld;

    /// <summary>Current health, seeded from the creature record's <c>Hp</c> on spawn and
    /// clamped to <see cref="CreatureHealthRules.MaxHealth"/> — DK2 has no
    /// separate max-health field, the maximum is <c>Hp</c> scaled by the level's
    /// Health percentage.
    /// <para>Reaching zero is not automatically death: see
    /// <see cref="CreatureHealthRules"/> and Q99.</para></summary>
    public int Health;

    /// <summary>Ticks since the hunger clock last ticked over, counting up towards the
    /// record's <c>HungerRate</c> in seconds — DK2's <c>LastAteTime</c> read as
    /// an age rather than a stamp.
    /// <para>Reset both by each helping of hunger the clock adds and by each
    /// chicken swallowed, so a creature part-way through a Bile Demon's five
    /// birds is not also accumulating a sixth. Counts from spawn, so a creature
    /// arrives fed.</para></summary>
    public int HungerTicks;

    /// <summary>How much hunger this creature has stored up — DK2's own
    /// <c>HungerNeed</c>, by the name its debug dump prints.
    /// <para>An accumulator, not a countdown: every <c>HungerRate</c> seconds
    /// the original adds <c>HungerFill</c> and nothing caps it, a creature is
    /// hungry at <c>HungerFill</c> or more, and each chicken pays off exactly
    /// one (docs/research/eating.md). Going unfed costs no health; the price is
    /// anger (the record's <c>angerNoFood</c>), and no balance variable turns
    /// hunger into damage.</para></summary>
    public int HungerNeed;

    /// <summary>Countdown to the next attempt to go and eat — DK2's
    /// <c>LastAttemptedHunger</c> read as a countdown rather than a stamp.
    /// <para>A hungry creature that cannot find a hatchery does not search again
    /// until this runs out: <see cref="EatingRules.RetryTicks"/>, one game
    /// second. Separate from <see cref="TimerTicks"/>, the shared idle
    /// re-decision, because a creature must be able to wait on both at once.</para></summary>
    public int FoodAttemptTicks;

    // No LairTileIndex: the bed records whose it is, not the creature, as DK2
    // does (Lair.CreatureId, a u16 at the first offset of the object's union).
    // Ask LairRules.BedTileIndex or LairRules.BedOf; docs/research/objects.md
    // section 7, Q130.

    /// <summary>Ticks this creature has been awake, counting up towards the record's
    /// <c>TimeAwake</c> in seconds (240 for a Goblin). Reset on waking, not on
    /// falling asleep, so the cycle is "awake for TimeAwake, then asleep for
    /// TimeSleep" rather than a fixed period. Whether that is DK2's cycle is
    /// UNKNOWN (Q103).</summary>
    public int AwakeTicks;

    /// <summary>Ticks this creature has been asleep, counting up towards <c>TimeSleep</c>.</summary>
    public int SleepTicks;

    /// <summary>Ticks spent in the current death state, counting up — two clocks in one
    /// field, because they never run at once (Q99, CONFIRMED by the owner;
    /// <see cref="CreatureDeathRules"/>).
    /// <para>While <see cref="CreatureActivity.Dying"/>, the rescue window
    /// <c>CreatureDyingStateDurationSeconds</c> (60), in which an imp may return
    /// the creature to its bed or, for an enemy, to a prison. While
    /// <see cref="CreatureActivity.Dead"/>, the corpse window
    /// <c>DeadBodyDiesAfterSeconds</c> (also 60). Reset to zero at the
    /// transition, so a corpse gets its own full sixty seconds.</para></summary>
    public int DyingTicks;

    /// <summary>How cross this creature is — six counters, one per grievance, each
    /// running from 0 to <see cref="CreatureAngerRules.Saturation"/> (65533).
    /// <para>The band comes from the largest of the six, never from their sum
    /// (ADR-0081, <see cref="CreatureAnger"/>). The boundaries are the owner's
    /// stopwatch: a lairless Goblin accruing 100 a second turned unhappy at 2:45
    /// and angry at 5:30, which is 32767/2 and 32767 to within a percent (Q106,
    /// CONFIRMED).</para></summary>
    public CreatureAnger Anger;

    /// <summary>The mood band this creature was last <i>reported</i> in — DK2's own
    /// <c>this[7]</c>, the eighth slot of the anger object, which its anger-add
    /// routine rewrites on every add (ADR-0081).
    /// <para>Stored rather than recomputed so the report is honest: anger is
    /// applied from four places and three of them run before
    /// <see cref="Systems.CreatureAngerSystem"/> in the same tick, so a system
    /// deriving "the mood before" from the anger it can see would find the
    /// change already made. The band itself stays derived —
    /// <see cref="CreatureAngerRules.MoodOf"/> is the truth at any instant.</para></summary>
    public CreatureMood Mood;

    /// <summary>The job this creature is currently doing, or
    /// <see cref="Data.Model.JobType.None"/> — DK2's own field at
    /// <c>creature+0x64</c>, written by its job dispatcher the moment a handler
    /// accepts and nowhere else (ADR-0082). Meaningful only while the activity
    /// is <see cref="CreatureActivity.GoingToJob"/> or
    /// <see cref="CreatureActivity.AtWork"/>.</summary>
    public JobType CurrentJob;

    /// <summary>DK2's <c>DroppedToDoJobId</c> (<c>creature+0x2BC</c>): the job of the room the hand
    /// last dropped this creature on (<c>0x004E8DA0</c>). Idle's chooser 89 tries it first and
    /// clears it only when it is refused, so a creature dropped in a Workshop keeps going back
    /// to it; the pick-up leaves it alone (ADR-0215).</summary>
    public JobType DroppedToDoJob;

    /// <summary>The tile of the room this creature is walking to for its job, or -1.</summary>
    public int JobTileIndex = -1;

    /// <summary>When this creature last chose a job while <b>content</b>, and the gate on
    /// choosing another: five game seconds
    /// (<see cref="JobRules.ReselectionGateTicks"/>).
    /// <para>Three timestamps, not one, which is the original's own layout —
    /// <c>creature+0xfd</c> happy, <c>+0xf9</c> unhappy, <c>+0x101</c> angry —
    /// so the gate never silences a mood change. Zero means never, so tick zero
    /// reads as never: the same sentinel and one-tick collision as
    /// <see cref="LastAngryChoiceTick"/> (ADR-0061), unreachable because only a
    /// succeeded selection stamps anything.</para></summary>
    public long LastHappyChoiceTick;

    /// <summary>When this creature last chose a job while <b>unhappy</b>. See
    /// <see cref="LastHappyChoiceTick"/> for why there are three.</summary>
    public long LastUnhappyChoiceTick;

    /// <summary>Wages this creature has been credited and has not yet collected.
    /// <para>It accumulates: the original's accrual adds rather than assigns, so
    /// a creature that misses a payday is owed two and an unpayable keeper
    /// builds a compounding debt. Clamped at zero by DK2 and by
    /// <see cref="PaydayRules"/>. Zero for an Imp for ever, by the data rather
    /// than by a rule: the Imp's record carries <c>pay=0</c>.</para></summary>
    public int WageOwed;

    /// <summary>How many times this creature has walked to a treasury this payday and
    /// found the tile it reached empty. At
    /// <see cref="PaydayRules.MaxFailedAttempts"/> it stops trying until the
    /// next payday, which resets this to zero for everybody; the original keeps
    /// the same counter in a byte beside the wage.</summary>
    public int PayAttemptsFailed;

    /// <summary>DK2's <c>GetPaid</c> (<c>creature+0x3E2</c>, a dword read as a flag): a one-shot
    /// ticket that suppresses exactly one payday. The original's clock consumes it whether or
    /// not it was needed — the creature is not credited a wage at all and the field is zeroed
    /// in the same breath. Called <c>SkipNextPayment</c> here until 2026-09-25, after Flame's
    /// map; DK2's own dump gives that name to <see cref="SkipNextPayment"/>.
    /// <para>Not implemented: nothing sets it. The thing that should is a treat, which the
    /// manual (§7.6.2.2) describes as buying off wages on the next payday; keeper spells do
    /// not exist, so the field is carried, hashed, read by the clock and written by tests
    /// only.</para></summary>
    public bool GetPaid;

    /// <summary>DK2's <c>SkipNextPayment</c>, <c>creature+0x3EA</c> bit 5: skip the next
    /// decision to walk to a treasury. Set when dropped gold pays this creature
    /// (<see cref="DropDeliveryRules"/>); the collection decision clears it whatever it decides
    /// (<c>0x0047237F</c>). It never stops a wage being credited (hand-drop-earmark.md §9).</summary>
    public bool SkipNextPayment;

    /// <summary><c>DieInstantly</c>, bit 11 of the creature's second flag dword: this one
    /// skips the collapsed, rescuable window and dies where it stands. Set by trigger action
    /// type 1 from its <c>Flag</c> byte's bit 2, on 22 of the 268 shipped records
    /// (ADR-0138). Three creature types carry it whatever the flag says —
    /// <see cref="CreatureDeathRules.DiesInstantly"/>.</summary>
    public bool DieInstantly;

    /// <summary>Experience level, 1 to <see cref="CreatureLevelRules.MaxLevel"/> (10). A
    /// level multiplies the creature's stats by percentages the shipped data
    /// states outright (<see cref="CreatureLevelRules"/>); CONFIRMED by the
    /// owner, <i>"imps gain speed as they gain more levels"</i> (Q119).
    /// <para>Raised by working and by training; every creature still spawns at
    /// 1. Not implemented: fighting, so nothing here reaches level 5
    /// (ADR-0083).</para></summary>
    public int Level = CreatureLevelRules.FirstLevel;

    /// <summary>Experience banked towards the <b>next</b> level, not a lifetime total —
    /// see <see cref="CreatureLevelRules.Grant"/> (Q127).
    /// <para>A working imp earns 80 a game second, so its second level costs
    /// 3000 / 80 = 37.5 seconds of work (Q124, ADR-0083 item 6). Trap:
    /// <c>ImpExperienceGainPerSecond</c> is 20 per <i>original tick</i> (250 ms,
    /// ADR-0089) despite the name — see
    /// <see cref="CreatureLevelRules.WorkExperiencePerOriginalTick"/>.</para></summary>
    public int ExperiencePoints;

    /// <summary>The tick on which this creature last <b>successfully</b> chose an angry
    /// job, or 0 for never — DK2's own
    /// <c>LastGameTickSuccessfullyChoseAngryJob</c>, the only thing its angry
    /// chooser keeps between selections (ADR-0061, Q122).
    /// <para>A gate, not a decision: five game seconds after the last selection
    /// that came to something. A selection that starts nothing does not stamp
    /// this, so a creature that cannot act on its anger re-rolls at every
    /// opportunity — which is how a portal built later becomes an escape route.
    /// 0 means never and tick 0 is a real tick, which costs nothing because the
    /// anger ladder never runs on tick 0.</para></summary>
    public long LastAngryChoiceTick;

    /// <summary>Whether this creature is in a keeper's hand — DK2's <c>StateFlags1.InHand</c>. A
    /// held creature is out of the dungeon: it does not walk, work, sleep or look for a lair, and
    /// every system that would make it act tests <see cref="HandRules.IsHeld"/> beside
    /// <see cref="CreatureDeathRules.IsDown"/>. It does keep getting hungry and minding its mood,
    /// which is how <c>AngerInHand</c> reaches it (ADR-0062).</summary>
    public bool InHand;

    /// <summary>The tick this creature was last picked up <b>or put down</b> — DK2's
    /// <c>PickupTick</c>, which its drop handler writes as well as its pick-up handler, so the name
    /// is narrower than the field. Read by the hand's grip on a creature's mood, which waits
    /// <see cref="HandRules.AngerGraceSeconds"/> after the pick-up.</summary>
    public long PickupTick;

    /// <summary>The tick this creature was spawned or last put down: the basis of DK2's
    /// <c>LastDroppedCounter</c>, <see cref="HandRules.LastDroppedCounter"/> (ADR-0278).</summary>
    public long DroppedTick;

    /// <summary>DK2's <c>WasLastDroppedOnARoom</c> (<c>+0x3E6</c> bit 26): the last drop landed on
    /// a room tile (ADR-0202).</summary>
    public bool DroppedOnARoom;

    /// <summary>The tick from which pay may be tried again, DK2's <c>+0x60</c> (ADR-0202).</summary>
    public long NextPayTryTick;

    /// <summary>The tick of the last slap that landed, or 0 for never — DK2's
    /// <c>LastSlapTick</c>. A tally, not a cooldown: every slap counts however fast they come
    /// (<see cref="HandRules.KnockBackWindowSeconds"/>).</summary>
    public long LastSlapTick;

    /// <summary>Ticks of raised Efficiency left from a slap, counting down — DK2's
    /// <c>WorkFasterTimer</c>, which its slap handler sets to a hardcoded 100
    /// and its creature update decrements once a tick.
    /// <para>Worth <see cref="EfficiencyRules.BonusPercent"/> while running, and
    /// it does not stack: a second slap rewrites it rather than adding, so an
    /// imp slapped ten times works exactly as fast as one slapped once —
    /// CONFIRMED by the owner (docs/research/tile-work.md).</para></summary>
    public int WorkFasterTicks;

    /// <summary>A one-shot ticket, set when the keeper puts this worker down and spent by
    /// its very next job evaluation — DK2's <c>ImpJobData.JustDropped</c>.
    /// <para>Not the Efficiency bonus: it only skips the idle throttle so a
    /// dropped worker evaluates on the spot, and turns that first evaluation
    /// into the seven-by-seven drop scan
    /// (<c>ImpTaskSystem.TryTakeDroppedJob</c>). The bonus is
    /// <see cref="DroppedTask"/>. The distinction is DK2's: its job chooser
    /// clears this unconditionally on its first run, and the field the
    /// Efficiency arithmetic reads is four bytes further on (ADR-0069).</para></summary>
    public bool JustDropped;

    /// <summary>Stored worker idle flag; remains set through wandering (imp-idle-and-wandering.md).</summary>
    public bool WorkerIdle;

    /// <summary>DK2's <c>+0x4B3</c>: the turn the mana-removal scan chose this creature, 0 for
    /// none. Refuses pickup while set; the countdown reader clears it or zeroes the
    /// creature's health (imp-mana-removal.md, ADR-0277).</summary>
    public uint ManaRemovalStamp;

    /// <summary>Runtime bit <c>0x20</c> of <c>+0x3E6</c>, cleared: the constructor copies the
    /// definition's <c>GenerateDeadBody</c> flag there and mana-removal selection clears it, so
    /// the death bookkeeping makes no corpse (<see cref="CreatureDeathRules.LeavesBody"/>).</summary>
    public bool CorpseSuppressed;

    /// <summary>The kind of task this worker was <b>dropped onto and is still doing</b>,
    /// or <see cref="ImpTaskKind.None"/> — DK2's <c>ImpJobData.DroppedTask</c>,
    /// the field <see cref="EfficiencyRules.Percent"/> reads, worth
    /// <see cref="EfficiencyRules.BonusPercent"/> (12.5%, as a slap is, and
    /// stacking with it to 100%).
    /// <para>The condition is the job, not the drop: an imp dropped beside a
    /// keeper-marked tile works at 90%, on an unmarked tile at 80% (CONFIRMED,
    /// owner 2026-09-05, docs/research/tile-work.md). Cleared only where this
    /// kind still matches <see cref="LastTaskKind"/> and no such work remains,
    /// so it survives the walk and finishing the tile.</para></summary>
    public ImpTaskKind DroppedTask;

    /// <summary>The kind of the task this worker most recently took — DK2's
    /// <c>ImpJobData.LastTaskType</c>, written by every one of its
    /// task-accepting routines on the way in.
    /// <para>Here for one reason: it is the guard on <see cref="DroppedTask"/>'s
    /// only clear. A worker that has wandered onto another kind of work keeps
    /// the aimed bonus, because the comparison fails and DK2 falls through.</para></summary>
    public ImpTaskKind LastTaskKind;

    /// <summary>The tile of the task this worker last took, or -1: DK2's task position
    /// (<c>+0x8C</c>/<c>+0x90</c>), which outlives the task. The dig walk measures seams from it
    /// after a mining job (ADR-0282).</summary>
    public int LastTaskTileIndex = -1;

    /// <summary>The id of the body this worker is carrying, or -1 for none. One slot, as
    /// DK2's imp has: a worker holding a body is not offered another, and a
    /// worker holding gold is not offered a body at all
    /// (<see cref="CarryRules.CanCarry"/>).</summary>
    public int CarriedCreatureId = -1;

    /// <summary>The id of the worker carrying this body, or -1 for none — the other end
    /// of <see cref="CarriedCreatureId"/>. Both ends are stored because both are
    /// asked: the carrier needs to know what it holds on arrival, and the body
    /// needs to be seen as held so nobody else crosses the dungeon for it. Kept
    /// in step by <see cref="CarryRules.PickUp"/> and
    /// <see cref="CarryRules.PutDown"/>, the only two writers of either.</summary>
    public int CarriedByCreatureId = -1;

    /// <summary>The id of the body this worker is walking towards, or -1 for none.
    /// Separate from <see cref="CarriedCreatureId"/> because the target can go
    /// wrong on its own — the body may rot, be rescued by a nearer imp, or be
    /// picked up by the keeper first — and because naming it at the start keeps
    /// two bodies on one tile distinct.</summary>
    public int TargetBodyId = -1;

    /// <summary>The keeper whose prison holds this creature, or
    /// <see cref="PrisonRules.NoPrison"/> for none.
    /// <para>Not <see cref="OwnerPlayerId"/>: a prisoner is still its own
    /// keeper's creature, merely somebody else's problem to feed. The original's
    /// creature dump names the field <c>PrisonOwner</c> at offset <c>0x3cb</c>
    /// (ADR-0046). Survives being picked up: dropping a prisoner back on a
    /// prison tile puts it straight back, and only dropping it elsewhere frees
    /// it (<see cref="PrisonRules.Release"/>).</para></summary>
    public int PrisonOwnerPlayerId = PrisonRules.NoPrison;

    /// <summary>Casualty priority per compact player id minus one; initially zero (creature +0x3F7).</summary>
    public ushort RescuePriorityMask;

    /// <summary>The id of the object this worker is carrying, or -1 for none — DK2's
    /// <c>CarryingId</c> at creature offset <c>0x3c3</c>, a separate slot from
    /// <see cref="CarriedCreatureId"/> (DK2's <c>DraggingId</c>, two bytes
    /// along), because the original distinguishes carrying a thing from dragging
    /// a creature.</summary>
    public int CarriedObjectId = -1;

    /// <summary>The object this worker is walking towards, or -1 for none.</summary>
    public int TargetObjectId = -1;

    /// <summary>What the creature is doing, named by its DK2 state (<see cref="ActivityRules"/>,
    /// ADR-0211). Not stored: the systems move the state, and this follows it.</summary>
    public CreatureActivity Activity =>
        ActivityRules.Of(this)
        ?? throw new InvalidOperationException($"Creature {Id} is in state {Dk2StateId}, which names no activity.");

    /// <summary>DK2's own state id, 0–280 (<c>creature-state-table.md</c>), and the
    /// authoritative representation of what a creature is doing (ADR-0135);
    /// <see cref="Activity"/> is derived from it (ADR-0211).
    /// <para><b>1, idle, from the constructor</b> (<c>0x00476B40</c> writes <c>+0x10</c> = 1,
    /// <c>travel-states.md</c> §2.4). A corpse holds none, -1 (ADR-0206).</para></summary>
    public int Dk2StateId = 1;

    /// <summary>DK2's own <c>StateBeforeUtilityState</c> (state object <c>+0x40</c>): the
    /// state this creature was in when it entered a utility state. <c>SetState</c> writes
    /// it and <b>never clears it on the way out</b>, so a stale save is load-bearing.
    /// <b>0, not -1</b>, before the first walk (<c>travel-states.md</c> §3).</summary>
    public int StateBeforeUtilityState;

    /// <summary>DK2's own <c>UtilityNewState</c> (state object <c>+0xDF</c>): the state
    /// control is handed to when the utility state finishes. It is what makes travel
    /// composable — a creature walking somewhere is in one of four <c>en route</c> states
    /// with its errand held here, rather than in a state per errand — and the transition
    /// rule reads <b>this</b>, not the current state, so a creature in transit is judged by
    /// where it is heading (<c>travel-states.md</c>).
    /// <para><b>There is no "none".</b> The constructor writes 0, every arrival writes
    /// <b>1</b>, and the route helper fills it as the walk starts, so the rule may read it
    /// unconditionally (ADR-0175).</para></summary>
    public int UtilityNewState;

    /// <summary>The countdown of the category update hook the creature is under, DK2's state
    /// bytes <c>+0xAB</c>, <c>+0xAC</c>, <c>+0xAF</c>, <c>+0xB0</c> and <c>+0xB3</c>: one per
    /// category, only one live at a time, so one field. Seeded by entering the category and
    /// run by <see cref="CreatureStateMachine.HookFires"/> (Q424, ADR-0192).</summary>
    public int HookCountdown;

    /// <summary>How long this creature has been tortured, DK2's <c>TorturedDuration</c> at
    /// <c>creature+0x4F6</c>: variable 19 more each time the Torture hook acts, zeroed only by a
    /// conversion, so it carries over from one session on the device to the next (ADR-0193).</summary>
    public int TorturedDuration;

    /// <summary>The tile of the victim a torturer is whipping, DK2's target at the state
    /// object's <c>+0x119</c>/<c>+0x11D</c>, or -1 (ADR-0196).</summary>
    public int TortureTargetTile = -1;

    /// <summary>Turns left of the whip, action 63, or 0 (ADR-0196).</summary>
    public int WhipTurnsLeft;

    /// <summary>The tile the contact test last ran on, or -1: DK2 runs <c>0x004E8660</c> on the
    /// turn a creature steps onto a new tile (ADR-0201).</summary>
    public int ContactTileIndex = -1;

    /// <summary>Turns left before 134 acts, or 0 (ADR-0201).</summary>
    public int RescueTurnsLeft;

    /// <summary>Ticks until a rebelling creature with no reachable Portal defects to the
    /// hero side, or 0 when it is not rebelling. DK2 keeps the same clock the other way
    /// up, as an absolute deadline at its state object's <c>+0xB7</c>
    /// (<c>rebellion.md</c> §2).</summary>
    public int RebelCountdownTicks;

    /// <summary>The keeper a defected creature was given as its <see cref="JobType.KillPlayer"/>
    /// target, or <see cref="PlayerIds.Unowned"/> for none. DK2 stores it beside the job id
    /// in the remembered-job triple at <c>creature+0x409</c> (<c>rebellion.md</c> §3).</summary>
    public byte RebelAgainstPlayerId;

    /// <summary>The tile this creature intends to work on, or -1 for none.</summary>
    public int TargetTileIndex = -1;

    /// <summary>Which kind of work it intends to apply. Meaningless when <see cref="TargetTileIndex"/> is -1.</summary>
    public TileWorkKind TargetWork;

    /// <summary>The standing slot a digger or miner holds on its wall, <c>face × 3 + slot</c>
    /// (0–11), or <see cref="MiningSlots.None"/>. DK2's chooser <c>0x00469AF0</c> gives each of a
    /// wall's four faces three slots a third of a tile wide; the miner's overflow lands in its
    /// slot's cell (gold-on-the-floor.md §10).</summary>
    public byte WorkSlot = MiningSlots.None;

    /// <summary>Countdown in ticks. What it counts down to depends on
    /// <see cref="Activity"/>: the next re-evaluation when idle, the next work
    /// application when working.</summary>
    public int TimerTicks;

    /// <summary>The creature this one is engaged with, or 0 — DK2's <c>creature+0x502</c>, the
    /// engagement's target, written by <c>SelectAVictim</c> and copied into the swing when an
    /// attack begins (docs/research/combat-acquisition.md §7).</summary>
    public int FightTargetId;

    /// <summary>How the engagement attacks — the low nibble of <c>creature+0x529</c>: 0 spell at a
    /// creature, 1 melee at a creature, 2 spell at a thing, 3 spell at an ally, 4 melee at a thing
    /// (<see cref="Combat.CombatRules.AttackTypes"/>). The high nibble, the spell slot, is not kept.</summary>
    public byte AttackType;

    /// <summary>DK2's own <c>AttackRangeSquared</c> (<c>+0x4FE</c>): how close the engagement has to
    /// get, in squared 1/4096 units — melee reach plus half the target's footprint, squared.</summary>
    public int AttackRangeSquared;

    /// <summary>DK2's own <c>FightProcessCounter</c>, the nibble at bits 22–25 of the flag dword:
    /// a creature standing on the tile it stood on last tick looks for a fight only every tenth
    /// tick, an Imp every fourth; one that has moved tile looks at once (§3).</summary>
    public byte FightProcessCounter;

    /// <summary>The tile this creature stood on when the fight process last ran — the position
    /// DK2 compares with the previous frame's to tell a mover from a stander.</summary>
    public int LastFightScanTileIndex = -1;

    /// <summary>What <see cref="FightTargetId"/> or <see cref="FightTargetTileIndex"/> names
    /// (<see cref="Combat.FightTargetKind"/>): a creature, or an enemy dungeon heart.</summary>
    public byte FightTargetKind;

    /// <summary>For a heart target, the heart's owner and the heart tile stood in for its pillar
    /// (Q363); -1 and 0 otherwise.</summary>
    public byte FightTargetPlayerId;
    public int FightTargetTileIndex = -1;

    /// <summary>DK2's own <c>TimeLastUsedMelee</c> (<c>+0x427</c>): the tick the last melee swing
    /// began, which <c>MeleeRecharge</c> is measured from (combat-fight-state.md §4).</summary>
    public long TimeLastUsedMelee;

    /// <summary>A melee swing in progress — DK2's creature action 17 being the current action —
    /// with the tick it began (<c>+0x3A5</c>) and the target it was started at (<c>+0x399</c>),
    /// which the blow lands on whether or not the engagement has moved since.</summary>
    public bool SwingInProgress;
    public long SwingStartTick;
    public byte SwingTargetKind;
    public int SwingTargetId;
    public byte SwingTargetPlayerId;

    /// <summary>Fear taken in this tick from everyone fighting me — DK2's <c>TempFear</c>
    /// (<c>+0x524</c>), a word that wraps. Handed to <see cref="CurrentFear"/> next tick.</summary>
    public ushort TempFear;

    /// <summary>Last tick's intake scaled by my distance from my heart — DK2's <c>CurrentFear</c>
    /// (<c>+0x526</c>), measured against the level-scaled <c>Fear</c> stat.</summary>
    public ushort CurrentFear;

    /// <summary>Ticks in a row over the threshold, clamped — DK2's <c>OverThreatCounter</c>
    /// (<c>+0x528</c>); at three the creature flees.</summary>
    public byte OverThreatCounter;

    /// <summary>Ticks of backing off left before the creature runs — DK2's <c>BackOffTimer</c>
    /// (<c>+0x532</c>), set on entering <see cref="CreatureActivity.Fleeing"/>.</summary>
    public byte BackOffTimer;

    /// <summary>Whether the body's speed cap is the run speed: set on entering a fight or a
    /// withdrawal (<c>0x004887D0(1)</c>), cleared when the creature stands down.</summary>
    public bool Running;

    /// <summary>The tick the current combat activity was entered — DK2's <c>state+0x18</c>;
    /// a withdrawal is refused in the state's first tick.</summary>
    public long CombatStateEnteredTick;

    // State-object +0x18; -1 when a clockless setup transition supplies no turn.
    public long StateEnteredTick = -1;

    /// <summary>The alarm a Scout carries to its room (DK2's state fields <c>+0xA7</c>, <c>+0xAF</c>,
    /// <c>+0xB1</c>): the threat's position, the strength still wanted there, and the room by its
    /// first tile. Read on arrival by <see cref="Combat.FearRules.RespondToThreat"/>.</summary>
    public int AlarmX;
    public int AlarmY;
    public int AlarmDeficit;
    public int AlarmRoomTile = -1;

    /// <summary>Ticks stood at the threat's position by a creature sent there — DK2's
    /// <c>state+0x7E</c> in state 254; over thirty and it stands down.</summary>
    public int ReportTicks;

    /// <summary>A guard's facing, 0–7 — DK2's <c>state+0xAB</c> in states 97 and 98: the post
    /// slot's facing, then each leg's (<c>guard-room.md</c>).</summary>
    public int GuardFacing;

    /// <summary>The turn after which a patrolling guard looks for a Guard Post — DK2's
    /// <c>state+0xB1</c>.</summary>
    public long GuardDeadlineTick;

    /// <summary>DK2's state deadline (state object <c>+0x7E</c>) as a hero uses it: state 90's
    /// twelve-turn retry and state 176's next stroll. One word, so a hero entering 176 after a
    /// failed job keeps the retry's deadline (hero-party-walk.md).</summary>
    public long StateDeadlineTick;

    /// <summary>When a hero leaves an action point for the next (<c>creature+0x413</c>, state 174).</summary>
    public long WaypointDeadlineTick;

    /// <summary>A hero party's shared pace, the slowest member's walk speed in the record's units,
    /// written once by the spawn (<c>0x004887D0(0, slowest)</c>); 0 for none. Running replaces it
    /// for good, as a fight's speed call does in DK2.</summary>
    public int PartySpeedRaw;

    /// <summary>The tile the creature was made on (<c>+0x404</c>/<c>+0x406</c>, written only by the
    /// constructor at <c>0x004875DA</c>), -1 for none. A thief escapes to it (state 169).</summary>
    public int OriginTileIndex = -1;

    /// <summary>A tunnelling worker's plan, state 186's controller fields (tunnelling.md §2–§5).</summary>
    public TunnelRecord Tunnel;

    /// <summary>State 12's wander stamp (state <c>+0xA7</c>): set to now + 32 when 87 is entered and
    /// tested only for non-zero, so after a trip the next rest rolls for an idle action; cleared when
    /// one plays (imp-idle-and-wandering.md §3).</summary>
    public long WanderStamp;

    /// <summary>When the creature action playing now ends (DK2's running action, <c>0x00496D00</c>);
    /// ours for the idle actions 43 and 44, whose length is UNKNOWN.</summary>
    public long ActionUntilTick;

    /// <summary>DK2's <c>OldFightPos</c> (<c>+0x52A</c>, <c>+0x52C</c>, a tile; 0 for none) and
    /// <c>OldFightTick</c> (<c>+0x52E</c>): where a creature withdrew from a fight, and when
    /// (<see cref="Combat.FightReturnRules"/>).</summary>
    public int OldFightTileX;
    public int OldFightTileY;
    public long OldFightTick;

    /// <summary>The turn state 99 was entered, for its <c>Age</c> test.</summary>
    public long GuardLostTick;

    /// <summary><c>+0x3E6</c> bit 20: heeding its keeper's Call to Arms, set when the flag's
    /// recruit or idle's walk sends it to 190 and cleared by the release, by idle and by the
    /// three states when the call is gone or unreachable (<see cref="CallToArmsRules"/>).</summary>
    public bool HeedingCallToArms;

    /// <summary><c>CombatPitFighter</c>, <c>+0x3E6</c> bit 15: set on a Combat Pit's floor by 138
    /// and by 139's pairing, cleared by the pick-up, 217, idle's refused 138 and the room reset
    /// (<c>combat-pit.md</c> §7).</summary>
    public bool CombatPitFighter;

    /// <summary><c>AttackAbnormalTargets</c>, <c>+0x3E6</c> bit 2, with <see cref="AttackingId"/>:
    /// the forced target 139 hands both fighters (<c>0x0048F390</c>).</summary>
    public bool AttackAbnormalTargets;

    /// <summary><c>AttackingId</c>, <c>+0x3F9</c>: the forced target's creature id, or
    /// <see cref="Combat.CombatRules.NoTarget"/>.</summary>
    public int AttackingId;

    /// <summary>The turn the current pit state was entered, for the pit states' <c>Age</c>; the
    /// spectators' 142–144 use it too.</summary>
    public long PitStateEnteredTick;

    /// <summary>State 144's remembered position (state <c>+0xAC</c>, <c>+0xB0</c>): where the
    /// spectator stood when it last looked; moved since, it picks a new spot
    /// (<see cref="CombatPitSpectatorRules"/>).</summary>
    public int SpectatorPosX;
    public int SpectatorPosY;

    /// <summary>The turn each of the three spell slots was last cast at — DK2's <c>+0x41B + 4i</c>;
    /// zero means never, and a cast stamps it when the action starts, not when the shot leaves
    /// (combat-spells.md §4, §6).</summary>
    public readonly long[] SpellStamps = new long[3];

    /// <summary>The high nibble of <c>creature+0x529</c>: which slot the engagement's spell nibble
    /// casts from. Meaningless while <see cref="AttackType"/> is a melee nibble.</summary>
    public byte AttackSpellSlot;

    /// <summary>What the current action is: 0 the melee swing (action 17), 1 a spell cast (action
    /// 16) from <see cref="SwingSpellSlot"/>, whose shot leaves <c>ShotDelay</c> ticks after
    /// <see cref="SwingStartTick"/> (combat-spells.md §7).</summary>
    public byte SwingKind;
    public byte SwingSpellSlot;

    /// <summary>Where the creature stood at the start of the last movement tick — the previous
    /// position DK2's <c>vtable+0x2C</c> returns, which a shot's aim leads by (shots.md §7).</summary>
    public int LastPosX;
    public int LastPosY;

    /// <summary>The status words of creature-status-effects.md §1, tick countdowns stepped by
    /// <see cref="Systems.CreatureStatusSystem"/>: armour <c>+0x443</c> (damage halved), invulnerable
    /// <c>+0x445</c> (blows and shots refused), frozen <c>+0x447</c>, invisible <c>+0x44F</c> (not
    /// attackable), the speed effect <c>+0x459</c> with its percent (DK2 stores the pinned speed
    /// itself), poison <c>+0x45D</c>/<c>+0x45F</c>/<c>+0x461</c>, and the stun state's clock.</summary>
    public int ArmourTicks;
    public int InvulnerableTicks;
    public int FrozenTicks;
    public int InvisibleTicks;
    public int SpeedEffectTicks;
    public int SpeedPercent = 100;
    public int PoisonTicks;
    public int PoisonAmount;
    public byte PoisonOwner;
    public int StunTicks;

    /// <summary>State 8, the walk to a fight, as a flag on the fighting creature; while set
    /// <see cref="Combat.ApproachRules"/> runs instead of <c>spFighting</c>. <see cref="RouteRefreshCountdown"/>
    /// is the state's <c>+0xF5</c>, <see cref="ChaseTicks"/> the creature's <c>+0x53D</c>, and
    /// <see cref="RetreatingCreature"/> DK2's own name for the target given up (combat-approach-and-victory.md).</summary>
    public bool Approaching;
    public int RouteRefreshCountdown;
    public int ChaseTicks;
    public int RetreatingCreature;

    /// <summary>The victory pause left to stand, and the corpse a <c>NeedBodyForFightIdle</c> creature walks
    /// to (DK2's <c>state+0xA7</c>).</summary>
    public int CelebrationTicks;
    public int BodyTargetId;

    /// <summary>The hero party this creature was raised in — DK2's <c>PartyId</c> (<c>+0x417</c>),
    /// <see cref="HeroPartyRules.NoParty"/> for none — with its <c>PositionInParty</c> (<c>+0x3E1</c>,
    /// a follower's number from 1, the leader's the follower count), <c>MyLeadersId</c>
    /// (<c>+0x3DF</c>, -1 for none) and <c>WanderRadius</c> (<c>+0x418</c>, 0 in every shipped
    /// member). Written by <see cref="HeroPartyRules"/>, and re-led on a knock-out (ADR-0218).</summary>
    public byte PartyId = HeroPartyRules.NoParty;
    public byte PositionInParty;
    public int LeaderId = -1;
    public byte WanderRadius;

    /// <summary>DK2's creature <c>Flags</c> byte (<c>+0x46F</c>, Q302): bit 1 party leader, bit 2
    /// follower, bit 5 frees friends (<see cref="RescueRules"/>), bit 6 unread here. A party spawn and
    /// a placed creature write it (ADR-0201); the constructor seeds bits 0 and 3 (ADR-0262).</summary>
    public byte Flags = 9;

    /// <summary>The member's <c>Flags2</c> bits landed on the second flag dword: <c>DestroysRooms</c>
    /// (bit 7), <c>IsTool</c> (bit 8), <c>IsMercenary</c> (bit 9); <see cref="DieInstantly"/> is bit 11.
    /// IsTool also permits foreign pickup (hand-of-evil.md §3).</summary>
    public bool DestroysRooms;
    public bool IsTool;
    public bool IsMercenary;

    /// <summary>DK2's standing job triple at <c>+0x409</c>/<c>+0x40D</c>/<c>+0x40F</c>, named
    /// <c>GoodJob</c> by the debug dumper: the job, the player it names and its value, an action
    /// point id for <see cref="JobType.SendToActionPoint"/>. A party member's comes from its record;
    /// a rebel's <see cref="JobType.KillPlayer"/> is still kept in <see cref="CurrentJob"/> and
    /// <see cref="RebelAgainstPlayerId"/> (rebellion.md). Remembered, not performed.</summary>
    public JobType GoodJob;
    public byte GoodJobPlayerId;
    public int GoodJobActionPointId;

    /// <summary>The eight melee attackers engaged with this creature, by id, 0 for a free slot —
    /// DK2's <c>+0x504</c> array. An attacker takes the first free slot when it engages and is
    /// struck out when it disengages; a ninth melee attacker cannot engage at all.</summary>
    public readonly int[] EngagedMelee = new int[Combat.CombatRules.Slots];

    /// <summary>The eight ranged attackers engaged with this creature — DK2's <c>+0x514</c>.</summary>
    public readonly int[] EngagedRanged = new int[Combat.CombatRules.Slots];

    /// <summary>Walking somewhere it has not reached: a route is loaded and <c>path_finished</c>
    /// is clear. The mover's arrival test sets the flag (steering-behaviours.md §5.2).</summary>
    public bool HasRoute => Body.PathTotalCount != 0 && !Body.PathFinished;

    /// <summary>A route was requested and found, arrived or not. False after <see cref="StopMoving"/>.</summary>
    public bool IsRouted => Body.PathTotalCount != 0;

    /// <summary>The tile the loaded route ends on.</summary>
    public TilePos Destination => new((Body.PathTargetPos.X >> 4) / Fp32.One, (Body.PathTargetPos.Y >> 4) / Fp32.One);

    /// <summary>Drops the route: the body keeps its velocity and brakes under <c>Follow_Path</c>'s
    /// no-route force (steering-behaviours.md §5.2), as the original's cleared window does.</summary>
    public void StopMoving() =>
        Steering.RouteWindow.Load(Body, [], new Steering.Fixed16Vector(PosX << 4, PosY << 4), 0);

    /// <summary>The tile the creature is standing on.</summary>
    public TilePos Tile => new(PosX / Fp32.One, PosY / Fp32.One);

    /// <summary>The centre of a tile, in position units.</summary>
    public static int CentreOf(int tile) => (tile * Fp32.One) + (Fp32.One / 2);

    /// <summary>Forgets what this creature was doing. Deliberately leaves
    /// <see cref="GoldHeld"/> alone: what is in an imp's hands is not part of
    /// its job.</summary>
    public void ClearJob()
    {
        // DroppedTask survives: DK2 clears it in exactly one place, a later
        // evaluation that finds no more work of the same kind, and giving a job
        // up is not that place (ADR-0069). JustDropped is spent by the drop scan
        // itself, the only thing that reads it. The state is the caller's to move.
        TargetTileIndex = -1;
        TargetWork = default;
        WorkSlot = MiningSlots.None;
        StopMoving();
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(CreatureTypeId);
        hash.Add(OwnerPlayerId);
        hash.Add(TriggerId);
        hash.Add(TriggerRepeat);
        hash.Add(PosX);
        hash.Add(PosY);
        hash.Add(Heading);
        hash.Add(RouteOptions);
        Body.HashInto(ref hash);
        hash.Add(GoldHeld);
        hash.Add(Health);
        hash.Add(HungerTicks);
        hash.Add(HungerNeed);
        hash.Add(FoodAttemptTicks);
        hash.Add(AwakeTicks);
        hash.Add(SleepTicks);
        hash.Add(DyingTicks);
        Anger.Hash(ref hash);
        hash.Add(WageOwed);
        hash.Add(PayAttemptsFailed);
        hash.Add(GetPaid);
        hash.Add(SkipNextPayment);
        hash.Add(DieInstantly);
        hash.Add(Level);
        hash.Add(ExperiencePoints);
        hash.Add((int)Mood);
        hash.Add(LastAngryChoiceTick);
        hash.Add(LastHappyChoiceTick);
        hash.Add(LastUnhappyChoiceTick);
        hash.Add((int)CurrentJob);
        hash.Add((int)DroppedToDoJob);
        hash.Add(JobTileIndex);
        hash.Add(InHand);
        hash.Add(PickupTick);
        hash.Add(DroppedTick);
        hash.Add(DroppedOnARoom);
        hash.Add(NextPayTryTick);
        hash.Add(LastSlapTick);
        hash.Add(WorkFasterTicks);
        hash.Add(JustDropped);
        hash.Add(WorkerIdle);
        hash.Add(ManaRemovalStamp);
        hash.Add(CorpseSuppressed);
        hash.Add((byte)DroppedTask);
        hash.Add((byte)LastTaskKind);
        hash.Add(LastTaskTileIndex);
        hash.Add(CarriedCreatureId);
        hash.Add(CarriedByCreatureId);
        hash.Add(TargetBodyId);
        hash.Add(PrisonOwnerPlayerId);
        hash.Add(RescuePriorityMask);
        hash.Add(CarriedObjectId);
        hash.Add(TargetObjectId);
        hash.Add(Dk2StateId);
        hash.Add(StateBeforeUtilityState);
        hash.Add(UtilityNewState);
        hash.Add(HookCountdown);
        hash.Add(TorturedDuration);
        hash.Add(TortureTargetTile);
        hash.Add(WhipTurnsLeft);
        hash.Add(ContactTileIndex);
        hash.Add(RescueTurnsLeft);
        hash.Add(RebelCountdownTicks);
        hash.Add(RebelAgainstPlayerId);
        hash.Add(TargetTileIndex);
        hash.Add((int)TargetWork);
        hash.Add(WorkSlot);
        hash.Add(TimerTicks);
        hash.Add(FightTargetId);
        hash.Add(AttackType);
        hash.Add(AttackRangeSquared);
        hash.Add(FightProcessCounter);
        hash.Add(LastFightScanTileIndex);
        hash.Add(FightTargetKind);
        hash.Add(FightTargetPlayerId);
        hash.Add(FightTargetTileIndex);
        hash.Add(TimeLastUsedMelee);
        hash.Add(SwingInProgress);
        hash.Add(SwingStartTick);
        hash.Add(SwingTargetKind);
        hash.Add(SwingTargetId);
        hash.Add(SwingTargetPlayerId);
        hash.Add(TempFear);
        hash.Add(CurrentFear);
        hash.Add(OverThreatCounter);
        hash.Add(BackOffTimer);
        hash.Add(Running);
        hash.Add(CombatStateEnteredTick);
        hash.Add(StateEnteredTick);
        hash.Add(AlarmX);
        hash.Add(AlarmY);
        hash.Add(AlarmDeficit);
        hash.Add(AlarmRoomTile);
        hash.Add(ReportTicks);
        hash.Add(GuardFacing);
        hash.Add(GuardDeadlineTick);
        hash.Add(StateDeadlineTick);
        hash.Add(WaypointDeadlineTick);
        hash.Add(PartySpeedRaw);
        hash.Add(OriginTileIndex);
        hash.Add(Tunnel.StandX);
        hash.Add(Tunnel.StandY);
        hash.Add(Tunnel.DigX);
        hash.Add(Tunnel.DigY);
        hash.Add(Tunnel.TargetX);
        hash.Add(Tunnel.TargetY);
        hash.Add(Tunnel.Phase);
        hash.Add(Tunnel.ExitX);
        hash.Add(Tunnel.ExitY);
        hash.Add(Tunnel.HitX);
        hash.Add(Tunnel.HitY);
        hash.Add(Tunnel.Facing);
        hash.Add(Tunnel.Resume);
        hash.Add(Tunnel.Reach);
        hash.Add(Tunnel.Hand);
        hash.Add(Tunnel.ActionUntilTick);
        hash.Add(WanderStamp);
        hash.Add(ActionUntilTick);
        hash.Add(OldFightTileX);
        hash.Add(OldFightTileY);
        hash.Add(OldFightTick);
        hash.Add(GuardLostTick);
        hash.Add(HeedingCallToArms);
        hash.Add(CombatPitFighter);
        hash.Add(AttackAbnormalTargets);
        hash.Add(AttackingId);
        hash.Add(PitStateEnteredTick);
        hash.Add(SpectatorPosX);
        hash.Add(SpectatorPosY);
        for (int i = 0; i < SpellStamps.Length; i++) hash.Add(SpellStamps[i]);
        hash.Add(AttackSpellSlot);
        hash.Add(SwingKind);
        hash.Add(SwingSpellSlot);
        hash.Add(LastPosX);
        hash.Add(LastPosY);
        hash.Add(ArmourTicks);
        hash.Add(InvulnerableTicks);
        hash.Add(FrozenTicks);
        hash.Add(InvisibleTicks);
        hash.Add(SpeedEffectTicks);
        hash.Add(SpeedPercent);
        hash.Add(PoisonTicks);
        hash.Add(PoisonAmount);
        hash.Add(PoisonOwner);
        hash.Add(StunTicks);
        hash.Add(Approaching);
        hash.Add(RouteRefreshCountdown);
        hash.Add(ChaseTicks);
        hash.Add(RetreatingCreature);
        hash.Add(CelebrationTicks);
        hash.Add(BodyTargetId);
        hash.Add(PartyId);
        hash.Add(PositionInParty);
        hash.Add(LeaderId);
        hash.Add(WanderRadius);
        hash.Add(Flags);
        hash.Add(DestroysRooms);
        hash.Add(IsTool);
        hash.Add(IsMercenary);
        hash.Add((int)GoodJob);
        hash.Add(GoodJobPlayerId);
        hash.Add(GoodJobActionPointId);
        for (int i = 0; i < EngagedMelee.Length; i++) hash.Add(EngagedMelee[i]);
        for (int i = 0; i < EngagedRanged.Length; i++) hash.Add(EngagedRanged[i]);
    }
}
