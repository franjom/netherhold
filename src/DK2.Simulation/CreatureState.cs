// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>
/// What a creature is currently doing.
///
/// Four states, because four is what the tile-work loop needs: look for
/// something to do, walk to it, do it, and carry away what doing it produced.
/// DK2's own creature state machine is much larger — Flame names dozens of state
/// constants — and reconstructing it is a later job (Q2, Q7). Nothing here
/// claims to be that machine.
/// </summary>
public enum CreatureActivity
{
    /// <summary>Standing still, waiting to re-evaluate what to do.</summary>
    Idle = 0,

    /// <summary>Walking towards a tile it intends to work on.</summary>
    MovingToJob = 1,

    /// <summary>Standing next to its target tile, applying work to it.</summary>
    Working = 2,

    /// <summary>
    /// Carrying gold to somewhere its keeper can keep it.
    ///
    /// Not a job an imp chooses — it is the tail of a mining job (Q31,
    /// ADR-0018), which is why it is a state rather than a
    /// <c>JobPriority</c>.
    /// </summary>
    DeliveringGold = 3,

    /// <summary>Walking to its own bed in a lair, to sleep in it.</summary>
    GoingToSleep = 4,

    /// <summary>
    /// Asleep in its own bed. The only state in which a lair heals a creature
    /// (ADR-0049, CONFIRMED by the owner).
    /// </summary>
    Sleeping = 5,

    /// <summary>
    /// Collapsed at zero health, lying on the ground, and not yet dead —
    /// DK2's <c>CreatureDyingStateDurationSeconds</c> window (ADR-0051,
    /// CONFIRMED by the owner).
    ///
    /// <para>A dying creature does nothing at all: it does not move, work,
    /// sleep, regenerate or grow hungry. It <b>keeps its bed</b>, because the
    /// way out of this state is an imp carrying it back to that bed. What ends
    /// the window is either that rescue or death.</para>
    /// </summary>
    Dying = 6,

    /// <summary>
    /// Walking out of the dungeon for good, having drawn <c>Leave</c> from its
    /// angry job table.
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-04, Q116): <i>"angry evil
    /// creature goes to the nearest available portal and leaves the dungeon"</i>
    /// — so desertion is a walk to a place, not a disappearance, and a keeper
    /// whose portal is unreachable keeps a creature that wanted to go.</para>
    ///
    /// <para>The original tracks the same thing: <c>Creature.cpp</c>'s debug
    /// dump prints a per-creature <c>Leaving</c> field of its own, alongside
    /// <c>LeaveNoTrace</c> and <c>CannotLeaveHealthLoss</c> (binary, ADR-0046 —
    /// facts only). Which says leaving is a state a creature is *in*, exactly
    /// as it is here, rather than an event that happens to it.</para>
    ///
    /// <para>A leaver has already given up its bed, will not claim another, and
    /// is skipped by <see cref="Systems.CreatureSleepSystem"/> entirely.</para>
    /// </summary>
    Leaving = 7,

    /// <summary>
    /// Walking to a pile of gold lying on the floor, to pick it up.
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-05, Q141): <i>"if there is
    /// gold on the ground an imp will first take from that gold pile and take
    /// it to a treasury, until all of it is gone"</i> — watched with a marked
    /// gem tile standing right there and nobody mining it. So fetching does not
    /// merely belong in the priority order, it sits <b>above</b> marked
    /// excavation, which had been the top of it since ADR-0018.</para>
    ///
    /// <para>Only the walk and the pick-up are this state. What happens next is
    /// <see cref="DeliveringGold"/>, unchanged — carrying gold home is one
    /// mechanic whatever put the gold in the hands.</para>
    /// </summary>
    FetchingGold = 8,

    /// <summary>
    /// Walking to a lair to put its bed down — a journey with a test at the
    /// end, and the state every non-worker enters first.
    ///
    /// <para><b>Distinct from <see cref="GoingToSleep"/> on purpose.</b> That
    /// state walks to a bed the creature already owns; this one walks to a
    /// <i>room</i>, chosen by distance, and only decides which tile on
    /// arrival — the tile underfoot, then eight neighbours in a fixed order,
    /// then a walk of the room from a random start. Nothing is reserved: two
    /// creatures heading for the same room is normal and one of them finding
    /// it full on arrival is normal. Read from the original's own lair state
    /// (<c>docs/research/lair-acquisition.md</c> §4, §5; ADR-0060).</para>
    ///
    /// <para>The state ends with a bed placed — and sleep at once if the
    /// creature is hurt past <c>CreatureSleepsWhenBelowPercentHealth</c>,
    /// idleness otherwise — or with nothing, when no reachable lair had a free
    /// tile, in which case the creature is idle and tries again at its next
    /// idle decision.</para>
    /// </summary>
    SeekingLair = 9,

    /// <summary>
    /// Dead, and lying on the floor as a corpse — the second half of the
    /// owner's pipeline (Q99, 2026-09-04):
    ///
    /// <para><i>"Once that timer runs out a creature dies and its corpse remains
    /// there lying, that gives imps an opportunity to carry it to graveyard. If
    /// that timer runs out and no one carries the corpse it just
    /// disappears."</i></para>
    ///
    /// <para>The window is <c>DeadBodyDiesAfterSeconds</c>, which is also 60,
    /// and it is a number this project pinned five sittings before anything
    /// could read it. Only creatures whose record carries
    /// <see cref="Data.Model.CreatureFlags.GenerateDeadBody"/> ever reach this
    /// state — an Imp is <i>"dead right away"</i>, and so is a Vampire, which
    /// reincarnates instead.</para>
    ///
    /// <para><b>A corpse is a creature in a state and not an object</b>, which
    /// this project spent five sittings believing the other way round and named
    /// as the cause of three separate gaps. None of the 145 OBJECTS records is a
    /// corpse (docs/research/objects.md §4).</para>
    /// </summary>
    Dead = 10,

    /// <summary>
    /// Walking to a body — collapsed or dead — in order to pick it up. DK2's
    /// imp task kinds 13 and 12.
    /// </summary>
    FetchingBody = 11,

    /// <summary>
    /// Carrying a body to where it has to go: a collapsed creature to its own
    /// bed, a corpse to a graveyard (<see cref="CarryRules"/>).
    ///
    /// <para>Distinct from <see cref="DeliveringGold"/> on purpose, even though
    /// both are "walk somewhere with something". Gold has no state of its own
    /// and merges into one pile at the end; a body is a creature that is still
    /// running its own clock while it is carried, and what happens on arrival
    /// depends on which clock it is.</para>
    /// </summary>
    CarryingBody = 12,

    /// <summary>
    /// Standing in somebody else's prison, losing health.
    ///
    /// <para>The far end of DK2's imp task <b>kind 13</b>: an imp carries its
    /// keeper's own unconscious creatures to a Lair and an enemy's to a Prison,
    /// and this is what the enemy becomes. <b>Only an imp puts a creature
    /// here</b> — CONFIRMED by the owner (2026-09-05) — but once it is here the
    /// keeper's hand may lift it out again, which is the one case in which the
    /// Hand of Evil touches a creature its keeper does not own
    /// (<see cref="HandRules.CanPickUp"/>).</para>
    ///
    /// <para>It arrives at <b>a sixth of its maximum</b> —
    /// <see cref="CreatureHealthRules.KnockoutHealth"/>, which is what DK2's
    /// damage path writes when it takes a creature below zero (Q167) — and
    /// <c>PrisonModifyCreatureHealthPerSecond</c> takes
    /// three health off it every game second. At zero it rises as a Skeleton
    /// for the keeper whose prison it stood in: the Prison's
    /// <c>CreatedCreatureId</c> is <b>9</b>, and it is one of only two rooms in
    /// the shipped table that names a creature at all.</para>
    ///
    /// <para><b>A prisoner here does nothing else at all — and in DK2 it
    /// does.</b> The owner watched prisoners <i>"rush toward that chicken, the
    /// first one to it gets it, eats it, and its health replenishes"</i>, so
    /// they walk about their cell, they compete, and they eat. <b>The chicken
    /// stopped being the blocker on 2026-09-06</b> — a hatchery lays one, an imp
    /// can carry one, and a free creature eats one (ADR-0066, ADR-0067) — so
    /// what is left is the movement: a prisoner has no wander and no search.
    /// The prison's own eating number is already written down in
    /// <see cref="EatingRules.EatInPrison"/>, because <b>one bird pays off the
    /// whole of <c>HungerFill</c></b> there where a hatchery bird pays off one.
    /// <para><b>It wanders</b> since ADR-0073 - one tile at a time,
    /// orthogonally, never out of the cell
    /// (<see cref="PrisonRules.TryWanderStep"/>). The pause between steps is a
    /// <b>20-in-100 roll per tick</b>, read out of the original's state 116.
    /// </para>
    ///
    /// <para><b>It is NOT <c>MotionlessInPrisonSeconds</c>, and that variable
    /// is a trap.</b> This file used to guess that its 8 was <i>"presumably how
    /// long one stands still between moves"</i>. A raw scan of <c>.text</c> for
    /// its global (<c>0x006F25E9</c>) finds <b>exactly one</b> hit - the setter
    /// stub writing it. Nothing in DK2 reads it, which makes it the third
    /// shipped balance variable found dead, after
    /// <c>CreatureCriticalHealthPercentageOfMax</c> (which cost this project a
    /// wrong prisoner clock) and <c>CriticalHealthPercentageForHeroLair</c>.
    /// </para>
    /// </summary>
    Imprisoned = 13,

    /// <summary>
    /// Walking to a loose object in order to pick it up — DK2's imp task
    /// <b>kind 8</b>.
    /// </summary>
    FetchingObject = 14,

    /// <summary>
    /// Carrying a loose object to the room it belongs in
    /// (<see cref="ObjectCarryRules"/>).
    ///
    /// <para>Distinct from <see cref="CarryingBody"/> because DK2 keeps two
    /// slots: <c>CarryingId</c> for a thing and <c>DraggingId</c> for a
    /// creature, side by side in its creature record. A book and a body are not
    /// the same load.</para>
    /// </summary>
    CarryingObject = 15,

    /// <summary>
    /// Walking to a hatchery, because it is hungry — the creature half of the
    /// room, and the first thing a non-worker does for a reason other than
    /// sleep.
    ///
    /// <para><b>A walk to a room, not to a bird.</b> The original chooses the
    /// nearest reachable hatchery of the creature's own keeper and only looks
    /// for a chicken once it is standing in it (<c>HungerState.cpp</c>; see
    /// <see cref="EatingRules"/>). Arriving is therefore not the end of
    /// anything: an empty hatchery leaves the creature standing in it, hungry,
    /// asking again a second later.</para>
    /// </summary>
    SeekingFood = 16,

    /// <summary>
    /// Walking to one particular chicken it has picked out, inside the hatchery
    /// it is standing in.
    ///
    /// <para><b>Nothing is reserved</b>, which is the whole character of this
    /// state. The chosen bird's id lives on the creature and nowhere else, so
    /// two hungry creatures may pick the same one and the one that gets there
    /// second finds it gone and looks again — which is exactly what the owner
    /// watched in a prison (Q172): <i>"they rush toward that chicken, the first
    /// one to it gets it"</i>.</para>
    /// </summary>
    FetchingChicken = 17,
}

/// <summary>
/// One creature in the world.
///
/// A class rather than a struct because a creature carries a path, and because
/// creatures are few where tiles are tens of thousands. Stored in a list whose
/// order is spawn order and is never sorted: the order creatures are updated in
/// is behaviour, in exactly the way the work queue's order is (ADR-0010).
///
/// <para>Only the fields some implemented mechanic actually reads are here.
/// Health, mood, anger, experience, hunger and the rest of DK2's creature record
/// arrive with the mechanics that read them, not before — the same discipline
/// <see cref="TileState"/> follows.</para>
/// </summary>
public sealed class CreatureState
{
    /// <summary>Stable identity for the life of the level. Assigned by <see cref="WorldState"/>.</summary>
    public int Id;

    /// <summary>Index into the CREATURES table — the definition this creature is an instance of.</summary>
    public byte CreatureTypeId;

    public byte OwnerPlayerId;

    /// <summary>
    /// Position within the dungeon in <see cref="Fp32"/> units, where
    /// <see cref="Fp32.One"/> is one tile. The centre of tile (x,y) is
    /// <c>(x * 4096 + 2048, y * 4096 + 2048)</c>.
    ///
    /// Sub-tile rather than per-tile because how long a creature takes to reach
    /// its work is gameplay: an imp crossing a dungeon is not teleporting from
    /// tile to tile. Fixed point rather than floating (ADR-0001) so that the
    /// same walk produces the same arrival tick on every machine. Never
    /// negative — the dungeon starts at the origin.
    /// </summary>
    public int PosX;
    public int PosY;

    /// <summary>
    /// Fractional movement carried between ticks, in units of
    /// <c>1 / SimulationTiming.TicksPerSecond</c> of an <see cref="Fp32"/> unit.
    ///
    /// A creature's speed is given per second and spent per tick, and integer
    /// division would throw the remainder away every tick — an imp at 1.84
    /// tiles/second would quietly walk 0.4% slow forever. Keeping the remainder
    /// makes the speed exactly what the data says without leaving integer maths.
    /// </summary>
    public int MoveRemainder;

    /// <summary>
    /// Gold in this creature's hands.
    ///
    /// CONFIRMED that an imp carries mined gold itself rather than pushing it
    /// straight into a treasury (Q31, owner observation): it mines into its own
    /// hands up to <see cref="Data.Model.CreatureDef.MaxGoldHeld"/>, runs to a
    /// place its keeper can keep gold, and puts it down there. That is why gold
    /// is on the creature and not on the player — the same reason it is on tiles
    /// and not on the player (ADR-0015). Gold in a creature's hands is gold that
    /// is <i>somewhere</i>, and while it is here it is not spendable.
    ///
    /// <para>Survives losing a job on purpose: <see cref="ClearJob"/> does not
    /// touch it. An imp whose seam collapsed is still holding what it mined.</para>
    ///
    /// <para><b>Not</b> seeded from <c>InitialGoldHeld</c> (Imp: 500). What that
    /// field is for is UNKNOWN and nothing here reads it.</para>
    /// </summary>
    public int GoldHeld;

    /// <summary>
    /// Current health. Seeded from the creature record's <c>Hp</c> on spawn and
    /// clamped to it, so <c>Hp</c> is the maximum as well as the starting value.
    ///
    /// <para>DK2 has no separate max-health field — a creature's level scales
    /// its stats through the <c>CreatureStats</c> table, which this project does
    /// not model yet, so today max health is simply the record's number. When
    /// levels arrive, the maximum becomes a function of level and this stays the
    /// current value.</para>
    ///
    /// <para><b>Reaching zero is not automatically death</b> — see
    /// <see cref="CreatureHealthRules"/>, and Q99 for the part that is still
    /// the owner's to confirm.</para>
    /// </summary>
    public int Health;

    /// <summary>
    /// Ticks since the hunger clock last ticked over, counting up. Compared
    /// against the record's <c>HungerRate</c> in seconds.
    ///
    /// <para>This is DK2's <c>LastAteTime</c> read as an age instead of a
    /// stamp, and it is reset by <b>both</b> of the things that reset that:
    /// each helping of hunger the clock adds, and each chicken swallowed. So a
    /// creature part-way through a Bile Demon's five birds is not also
    /// accumulating a sixth.</para>
    ///
    /// <para>Counts from spawn, so a creature arrives fed.</para>
    /// </summary>
    public int HungerTicks;

    /// <summary>
    /// How much hunger this creature has stored up — DK2's own
    /// <c>HungerNeed</c>, by the name its debug dump prints.
    ///
    /// <para><b>An accumulator, not a countdown from a fixed start</b>, and the
    /// difference is observable. Every <c>HungerRate</c> seconds the original
    /// <i>adds</i> <c>HungerFill</c> to this and nothing caps it; a creature is
    /// hungry once it holds <c>HungerFill</c> or more, and each chicken pays off
    /// exactly one. So a creature that goes unfed for three periods owes three
    /// times as many birds, and eats them all when a hatchery finally
    /// appears. Until 2026-09-06 this field was called <c>ChickensNeeded</c>
    /// and was <i>set</i> to <c>HungerFill</c>, which saturates instead of
    /// accumulating (<c>docs/research/eating.md</c>).</para>
    ///
    /// <para>Going unfed still costs no health: the price is <i>anger</i> (the
    /// record's own <c>angerNoFood</c>), and there is no variable anywhere in
    /// the balance table that turns hunger into damage.</para>
    /// </summary>
    public int HungerNeed;

    /// <summary>
    /// Countdown to the next attempt to go and eat — DK2's
    /// <c>LastAttemptedHunger</c> read as a countdown instead of a stamp.
    ///
    /// <para>A hungry creature that cannot find a hatchery does not search
    /// again until this runs out, which is <see cref="EatingRules.RetryTicks"/>
    /// — one game second, the original's four game ticks. It is a separate
    /// countdown from <see cref="TimerTicks"/> on purpose: that one is the idle
    /// re-decision an imp shares, and a creature must be able to be waiting on
    /// both at once.</para>
    /// </summary>
    public int FoodAttemptTicks;

    // Where LairTileIndex used to be.
    //
    // A creature no longer records which bed is its; the bed records whose it
    // is. DK2 keeps the relation on the object - Lair.CreatureId, a u16 at the
    // first offset of the object's union - and this project kept a second copy
    // of it here until 2026-09-04, which is the shape that has already cost it
    // three long-lived defects. Ask LairRules.BedTileIndex or LairRules.BedOf
    // instead; see docs/research/objects.md section 7, and Q130 for the
    // decision.

    /// <summary>
    /// Ticks this creature has been awake, counting up towards the record's
    /// <c>TimeAwake</c> in seconds (240 for a Goblin).
    ///
    /// <para>Reset when it wakes, not when it falls asleep, so the cycle is
    /// "awake for TimeAwake, then asleep for TimeSleep" rather than a fixed
    /// period. Whether that is DK2's cycle is Q103.</para>
    /// </summary>
    public int AwakeTicks;

    /// <summary>Ticks this creature has been asleep, counting up towards <c>TimeSleep</c>.</summary>
    public int SleepTicks;

    /// <summary>
    /// Ticks spent in the current death state, counting up — <b>two clocks in
    /// one field, because they never run at once.</b>
    ///
    /// <list type="bullet">
    /// <item>While <see cref="CreatureActivity.Dying"/>, the rescue window:
    /// <c>CreatureDyingStateDurationSeconds</c> (60). <i>"it remains lying on
    /// the ground for a period of time, that gives imps an opportunity to return
    /// a creature to its bed in the lair for healing, or to prison if it is an
    /// enemy creature"</i>.</item>
    /// <item>While <see cref="CreatureActivity.Dead"/>, the corpse window:
    /// <c>DeadBodyDiesAfterSeconds</c> (also 60). <i>"If that timer runs out and
    /// no one carries the corpse it just disappears."</i></item>
    /// </list>
    ///
    /// <para>Reset to zero at the transition between them, so a corpse gets its
    /// own full sixty seconds rather than inheriting a spent clock. Both quotes
    /// are the owner's (Q99); see <see cref="CreatureDeathRules"/>.</para>
    /// </summary>
    public int DyingTicks;

    /// <summary>
    /// How cross this creature is, from 0 to <see cref="CreatureAngerRules.Ceiling"/>
    /// (32767).
    ///
    /// <para>The ceiling is the owner's stopwatch, not a guess: a lairless
    /// Goblin accruing 100 a second became unhappy at 2:45 and angry at 5:30,
    /// which is 32767/2 and 32767 to within a percent (Q106). See
    /// <see cref="CreatureAngerRules"/>.</para>
    /// </summary>
    public int Anger;

    /// <summary>
    /// Experience level, 1 to <see cref="CreatureLevelRules.MaxLevel"/>.
    ///
    /// <para><b>What it does is multiply the creature's stats</b>, by
    /// percentages the shipped data states outright — see
    /// <see cref="CreatureLevelRules"/>. CONFIRMED by the owner, who mentioned
    /// in passing that <i>"imps gain speed as they gain more levels"</i> and
    /// sent this project looking at a table it had been parsing and never
    /// reading (Q119).</para>
    ///
    /// <para><b>Nothing raises it yet</b> and every creature spawns at 1. The
    /// experience rate's unit is ambiguous in the data — the creature record
    /// says 1200 for an Imp where the global variable says 20 a second, and
    /// 20 x 60 = 1200 — so levelling would have to guess between two-and-a-half
    /// seconds and two-and-a-half minutes per level (Q124). The field is real
    /// and hashed regardless, because a scenario can set it and because every
    /// stat read in this project was silently the level-1 value while claiming
    /// to be the creature's.</para>
    /// </summary>
    public int Level = CreatureLevelRules.FirstLevel;

    /// <summary>
    /// Experience banked towards the <b>next</b> level, not a lifetime total —
    /// see <see cref="CreatureLevelRules.TryLevelUp"/> for which of the two this
    /// is and why (Q127).
    ///
    /// <para>An imp earns 20 a second while it works, so 150 seconds of digging
    /// buys its second level (Q124, CONFIRMED). Nothing else earns any, because
    /// training and fighting do not exist.</para>
    /// </summary>
    public int ExperiencePoints;

    /// <summary>
    /// The tick on which this creature last <b>successfully</b> chose an angry
    /// job, or 0 for never — DK2's own
    /// <c>LastGameTickSuccessfullyChoseAngryJob</c>, which is the only thing
    /// its angry chooser keeps between selections (ADR-0061, Q122).
    ///
    /// <para>It is a gate, not a decision: an angry creature may make a new
    /// selection only five game seconds after the last one that came to
    /// something. A selection that starts nothing does not stamp this, so a
    /// creature that cannot act on its anger re-rolls at every opportunity —
    /// which is how a portal built later becomes an escape route.</para>
    ///
    /// <para><b>0 means never, and tick 0 is a real tick</b> — the original
    /// makes the same conflation, and it costs nothing here because the anger
    /// ladder runs on the last tick of a game second and never on tick 0.</para>
    /// </summary>
    public long LastAngryChoiceTick;

    /// <summary>
    /// Whether this creature is in a keeper's hand — DK2's
    /// <c>StateFlags1.InHand</c>, the first bit of the first flag word its own
    /// debug dump prints.
    ///
    /// <para>A held creature is out of the dungeon: it does not walk, work,
    /// sleep or look for a lair, and every system that would make it act tests
    /// <see cref="HandRules.IsHeld"/> beside
    /// <see cref="CreatureDeathRules.IsDown"/>. What it does keep doing is
    /// getting hungry and minding its mood, which is how <c>AngerInHand</c>
    /// reaches it (ADR-0062).</para>
    /// </summary>
    public bool InHand;

    /// <summary>
    /// The tick this creature was last picked up <b>or put down</b> — DK2's
    /// <c>PickupTick</c>, which its drop handler writes just as its pick-up
    /// handler does, so the name is narrower than the field.
    ///
    /// <para>What reads it is the hand's grip on a creature's mood: the
    /// original waits <see cref="HandRules.AngerGraceSeconds"/> after the
    /// pick-up before <c>AngerInHand</c> starts to apply, so a keeper who moves
    /// a creature across the dungeon and puts it down has not touched its
    /// temper at all.</para>
    /// </summary>
    public long PickupTick;

    /// <summary>
    /// The tick of the last slap that landed, or 0 for never — DK2's
    /// <c>LastSlapTick</c>.
    ///
    /// <para>A cooldown, not a tally: a second slap inside
    /// <see cref="HandRules.SlapCooldownTicks"/> is refused outright, which is
    /// what makes slaps countable and is why the owner's Salamander took a
    /// clean six of them to sulk (Q114).</para>
    /// </summary>
    public long LastSlapTick;

    /// <summary>
    /// Ticks of raised Efficiency left from a slap, counting down — DK2's
    /// <c>WorkFasterTimer</c>, which its slap handler sets to a hardcoded 100
    /// and its creature update decrements once a tick.
    ///
    /// <para>Worth <see cref="EfficiencyRules.BonusPercent"/> while it is
    /// running, and <b>it does not stack</b>: a second slap rewrites it rather
    /// than adding to it, so an imp slapped ten times works exactly as fast as
    /// one slapped once. CONFIRMED by the owner, who tried
    /// (docs/research/tile-work.md).</para>
    /// </summary>
    public int WorkFasterTicks;

    /// <summary>
    /// A one-shot ticket, set the moment the keeper puts this worker down and
    /// spent by its very next job evaluation — DK2's
    /// <c>ImpJobData.JustDropped</c>.
    ///
    /// <para><b>This is not the Efficiency bonus.</b> It only says <i>"look
    /// around where you landed before you look anywhere else"</i>: it skips the
    /// idle throttle so a dropped worker evaluates on the spot, and it turns the
    /// first evaluation into the seven-by-seven drop scan around where it landed
    /// (<c>ImpTaskSystem.TryTakeDroppedJob</c>). The bonus is
    /// <see cref="DroppedTask"/>, and it is set only if that scan actually finds
    /// something.</para>
    ///
    /// <para>The distinction is DK2's, not ours: its job chooser clears this
    /// field unconditionally the first time it runs, whether the scan succeeds
    /// or fails, and the field the Efficiency arithmetic reads is a different
    /// one four bytes further on. Reading them as one thing is what this project
    /// did until 2026-09-06, and it is why the aimed bonus used to be worth
    /// exactly one job.</para>
    /// </summary>
    public bool JustDropped;

    /// <summary>
    /// The kind of task this worker was <b>dropped onto and is still doing</b>,
    /// or <see cref="ImpTaskKind.None"/> — DK2's <c>ImpJobData.DroppedTask</c>,
    /// and <b>the field its Efficiency arithmetic actually reads</b>
    /// (<see cref="EfficiencyRules.Percent"/>).
    ///
    /// <para>Worth <see cref="EfficiencyRules.BonusPercent"/>, and the condition
    /// is exact (owner, 2026-09-05): <i>"after I mark a tile for excavation and
    /// drop an imp adjacent to it its efficiency jumps to 90%. But if I drop it
    /// on a random tile where there is nothing marked by me its efficiency
    /// remains at 80%."</i> <b>It is the job, not the drop.</b></para>
    ///
    /// <para><b>It is set once and cleared once, and nothing else in DK2 touches
    /// it</b> — the whole binary has exactly two writers, both inside the job
    /// chooser. It is set to the kind the drop scan took; it is cleared only at
    /// a later evaluation where this kind still matches
    /// <see cref="LastTaskKind"/> and no more work of that kind exists anywhere
    /// on the map. So it survives the walk, survives the digging, and survives
    /// finishing the tile — which is what the owner watched on 2026-09-06:
    /// <i>"it stays at 90% until it gets to that marked tile and starts
    /// excavating, it doesn't revert to 80%."</i></para>
    ///
    /// <para>So DK2 pays a keeper 12.5% for aiming: marking work and placing a
    /// worker on it beats letting the worker find it, by exactly what a slap is
    /// worth and stacking with it to 100% — and it keeps paying for as long as
    /// that worker stays on that kind of work.</para>
    /// </summary>
    public ImpTaskKind DroppedTask;

    /// <summary>
    /// The kind of the task this worker most recently took — DK2's
    /// <c>ImpJobData.LastTaskType</c>, which every one of its task-accepting
    /// routines writes on the way in.
    ///
    /// <para>Here for one reason: it is the guard on
    /// <see cref="DroppedTask"/>'s only clear. A worker that has wandered onto
    /// some other kind of work keeps the aimed bonus rather than losing it,
    /// because the comparison fails and DK2 falls straight through.</para>
    /// </summary>
    public ImpTaskKind LastTaskKind;

    /// <summary>
    /// The id of the body this worker is carrying, or -1 for none.
    ///
    /// <para><b>One slot.</b> DK2's imp carries one thing and so does this: a
    /// worker holding a body is not offered another, and a worker holding gold
    /// is not offered a body at all (<see cref="CarryRules.CanCarry"/>).</para>
    /// </summary>
    public int CarriedCreatureId = -1;

    /// <summary>
    /// The id of the worker carrying this body, or -1 for none — the other end
    /// of <see cref="CarriedCreatureId"/>.
    ///
    /// <para>Both ends are stored because both are asked. The carrier needs to
    /// know what it is holding on arrival; the body needs to know it is held so
    /// that nobody else walks across the dungeon for a body already in
    /// somebody's arms. Kept in step by <see cref="CarryRules.PickUp"/> and
    /// <see cref="CarryRules.PutDown"/>, which are the only two things that
    /// write either.</para>
    /// </summary>
    public int CarriedByCreatureId = -1;

    /// <summary>
    /// The id of the body this worker is walking towards, or -1 for none.
    ///
    /// <para>Separate from <see cref="CarriedCreatureId"/> because they are
    /// different halves of one job and both can be wrong in different ways: the
    /// body an imp set off for can rot, be rescued by a nearer imp, or be picked
    /// up by the keeper before the imp arrives. Naming it at the start rather
    /// than looking again on arrival is what stops two bodies on one tile from
    /// being confused for each other.</para>
    /// </summary>
    public int TargetBodyId = -1;

    /// <summary>
    /// The keeper whose prison holds this creature, or
    /// <see cref="PrisonRules.NoPrison"/> for none.
    ///
    /// <para><b>Not the same thing as <see cref="OwnerPlayerId"/></b>, and DK2
    /// keeps both for the same reason: a prisoner is still its own keeper's
    /// creature, it is merely somebody else's problem to feed. The original's
    /// creature dump names this field <c>PrisonOwner</c> and puts it at offset
    /// <c>0x3cb</c>, four fields along from <c>DraggingId</c> and
    /// <c>BeingDragged</c> — which is the imp that put it there (facts about
    /// the binary in our own words, ADR-0046).</para>
    ///
    /// <para>It survives being picked up, because the keeper who lifts a
    /// prisoner out of a cell has not freed it yet: dropping it back on a prison
    /// tile puts it straight back, and only dropping it anywhere else lets it
    /// go (<see cref="PrisonRules.Release"/>).</para>
    /// </summary>
    public int PrisonOwnerPlayerId = PrisonRules.NoPrison;

    /// <summary>
    /// The id of the object this worker is carrying, or -1 for none — DK2's
    /// <c>CarryingId</c> at creature offset <c>0x3c3</c>.
    ///
    /// <para><b>A separate slot from <see cref="CarriedCreatureId"/></b>, which
    /// is DK2's <c>DraggingId</c> two bytes along. The original distinguishes
    /// carrying a thing from dragging a creature, and so does this.</para>
    /// </summary>
    public int CarriedObjectId = -1;

    /// <summary>The object this worker is walking towards, or -1 for none.</summary>
    public int TargetObjectId = -1;

    public CreatureActivity Activity;

    /// <summary>The tile this creature intends to work on, or -1 for none.</summary>
    public int TargetTileIndex = -1;

    /// <summary>Which kind of work it intends to apply. Meaningless when <see cref="TargetTileIndex"/> is -1.</summary>
    public TileWorkKind TargetWork;

    /// <summary>
    /// Countdown in ticks. What it counts down to depends on
    /// <see cref="Activity"/>: the next re-evaluation when idle, the next work
    /// application when working.
    /// </summary>
    public int TimerTicks;

    /// <summary>
    /// The route being walked, excluding the tile it started from. Empty when
    /// the creature is not going anywhere.
    /// </summary>
    public readonly List<TilePos> Path = [];

    /// <summary>How far along <see cref="Path"/> the creature is.</summary>
    public int PathIndex;

    /// <summary>The tile the creature is standing on.</summary>
    public TilePos Tile => new(PosX / Fp32.One, PosY / Fp32.One);

    /// <summary>The centre of a tile, in position units.</summary>
    public static int CentreOf(int tile) => (tile * Fp32.One) + (Fp32.One / 2);

    /// <summary>
    /// Forgets what this creature was doing. Deliberately leaves
    /// <see cref="GoldHeld"/> alone: what is in an imp's hands is not part of
    /// its job.
    /// </summary>
    public void ClearJob()
    {
        // DroppedTask deliberately survives this. The aimed bonus is not worth
        // one job: DK2 clears it in exactly one place - a later evaluation that
        // cannot find more work of the same kind - and giving a job up is not
        // that place (ADR-0069). JustDropped is not cleared here either; it is
        // spent by the drop scan itself, which is the only thing that reads it.
        Activity = CreatureActivity.Idle;
        TargetTileIndex = -1;
        TargetWork = default;
        Path.Clear();
        PathIndex = 0;
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(CreatureTypeId);
        hash.Add(OwnerPlayerId);
        hash.Add(PosX);
        hash.Add(PosY);
        hash.Add(MoveRemainder);
        hash.Add(GoldHeld);
        hash.Add(Health);
        hash.Add(HungerTicks);
        hash.Add(HungerNeed);
        hash.Add(FoodAttemptTicks);
        hash.Add(AwakeTicks);
        hash.Add(SleepTicks);
        hash.Add(DyingTicks);
        hash.Add(Anger);
        hash.Add(Level);
        hash.Add(ExperiencePoints);
        hash.Add(LastAngryChoiceTick);
        hash.Add(InHand);
        hash.Add(PickupTick);
        hash.Add(LastSlapTick);
        hash.Add(WorkFasterTicks);
        hash.Add(JustDropped);
        hash.Add((byte)DroppedTask);
        hash.Add((byte)LastTaskKind);
        hash.Add(CarriedCreatureId);
        hash.Add(CarriedByCreatureId);
        hash.Add(TargetBodyId);
        hash.Add(PrisonOwnerPlayerId);
        hash.Add(CarriedObjectId);
        hash.Add(TargetObjectId);
        hash.Add((int)Activity);
        hash.Add(TargetTileIndex);
        hash.Add((int)TargetWork);
        hash.Add(TimerTicks);
        hash.Add(Path.Count);
        foreach (TilePos p in Path)
        {
            hash.Add(p.X);
            hash.Add(p.Y);
        }
        hash.Add(PathIndex);
    }
}
