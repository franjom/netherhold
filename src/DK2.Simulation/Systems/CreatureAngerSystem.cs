// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// The ladder: <b>content → unhappy → angry → gone</b>. Once a game second.
///
/// <para><b>Confirmed three times from three different causes before it was
/// ever built</b>, which is unusual and is why the shape was never in doubt —
/// only the unit was:</para>
///
/// <list type="bullet">
/// <item><b>Q9</b> (pay): <i>"Unpaid → unhappy → angry → leaves; a converted
/// creature rebels instead."</i></item>
/// <item><b>Q105</b> (lairs): <i>"If a creature can't get a lair it becomes
/// unhappy, it can't go to sleep, then angry, then it leaves the dungeon."</i></item>
/// <item><b>The bed-loss answer</b>: a creature whose lair tile is sold looks
/// for another, <i>"otherwise anger mechanic is triggered, it gets upset, then
/// angry then it leaves the dungeon."</i></item>
/// </list>
///
/// <para>And then <b>Q106</b> gave the unit with a stopwatch — 32767 — which is
/// the whole reason this file exists. See <see cref="CreatureAngerRules"/> for
/// the arithmetic.</para>
///
/// <para><b>What an angry creature does comes from its own record</b>, not from
/// a rule here: a Goblin's <c>AngryJobs</c> table is <c>Leave(50%)</c> and
/// <c>Rebel(50%)</c>. Leaving is implemented. Rebelling is <b>not</b> — it needs
/// a creature to change sides and become a hero, and the three variables that
/// govern the outcome (<c>RebelLeavingPercentage</c> 70,
/// <c>RebelBecomeGoodPercentage</c> 15, <c>RebelDefectPercentage</c> 15) have
/// nothing to act on. So a creature that draws Rebel stays angry and says so
/// rather than being quietly made to leave instead — and then rolls again five
/// game seconds later, which is the original's cadence and means a Goblin does
/// leave in the end, by the coin toss its record describes rather than by a
/// draw that stuck (ADR-0061, Q122).</para>
///
/// <para><b>Cadence and order.</b> The last tick of each game second, the phase
/// <see cref="ManaSystem"/> and <see cref="CreatureHealthSystem"/> use, because
/// every rate it reads is written per second. Registered <b>after</b>
/// <see cref="CreatureSleepSystem"/> so that a creature which claimed a bed this
/// tick is not charged <c>AngerNoLair</c> for the second it arrived, and
/// <b>after</b> <see cref="CreatureHealthSystem"/> so that a creature which
/// collapsed this second is already <see cref="CreatureActivity.Dying"/> and is
/// skipped.</para>
///
/// <para><b>A creature that is down is skipped entirely.</b> Something lying on
/// the floor waiting to be carried to bed is not accruing a grievance about not
/// having got there — and more practically, a dying creature that deserted would
/// vanish out of the rescue window by a second route (ADR-0051).</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-anger.md")]
public sealed class CreatureAngerSystem : ISimulationSystem
{
    private const string SystemName = "CreatureAnger";

    private readonly IPathfinder _pathfinder;
    private readonly LairSeeker _seeker;
    private readonly List<TilePos> _route = [];
    private readonly List<(JobType Job, int Low, int High)> _ranges = [];

    public CreatureAngerSystem() : this(new GridPathfinder()) { }

    public CreatureAngerSystem(IPathfinder pathfinder)
    {
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        _seeker = new LairSeeker(_pathfinder);
    }

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond != 0) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;

        // Descending, so a creature that walks out cannot skip its neighbour.
        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState state = world.Creatures[i];
            CreatureDef creature = database.CreatureById(state.CreatureTypeId);

            if (CreatureDeathRules.IsDown(state)) continue;
            if (!CreatureAngerRules.CanBeAngered(creature)) continue;

            CreatureMood before = CreatureAngerRules.MoodOf(creature, state);

            int delta = CreatureAngerRules.PerSecond(world, database, creature, state);
            if (delta != 0) CreatureAngerRules.Apply(state, delta);

            CreatureMood after = CreatureAngerRules.MoodOf(creature, state);

            if (after != before)
            {
                context.Emit(SystemName, "creature_mood",
                    $"creature={state.Id} type={creature.Name} " +
                    $"player={state.OwnerPlayerId} {before}->{after} " +
                    $"anger={state.Anger}/{CreatureAngerRules.Ceiling} " +
                    $"unhappy_at={CreatureAngerRules.UnhappyAt(creature)}");
            }

            // A creature in the hand still minds - the ladder above ran, and
            // AngerInHand rides the same clock in HandSystem - but it can act
            // on none of it while it is off the ground. It rolls when it lands.
            if (after == CreatureMood.Angry && !HandRules.IsHeld(state))
                Decide(context, creature, state);
        }
    }

    /// <summary>
    /// <b>A fresh roll on every allowed selection</b>, five game seconds apart,
    /// with a lair attempt before it — the original's angry chooser
    /// (<c>FUN_004784c0</c>), read on 2026-09-05 (Q122, ADR-0061).
    ///
    /// <para>The order is the original's, and each line of it is a rule:</para>
    ///
    /// <list type="number">
    /// <item><b>A creature already walking out does not choose again.</b> The
    /// chooser is reached only when the creature's current state had nothing to
    /// say, and leaving is a state (<c>lair-acquisition.md</c> §5).</item>
    /// <item><b>The five-second gate</b>, against
    /// <see cref="CreatureState.LastAngryChoiceTick"/> — and it is a gate on
    /// <i>success</i>. A selection that starts nothing does not stamp it, so a
    /// creature that cannot act on its anger tries again at every
    /// opportunity.</item>
    /// <item><b>No lair? Go and get one</b>, before any roll and instead of it
    /// — the same attempt an idle creature makes, which is why it is
    /// <see cref="LairSeeker"/> and not a copy. Setting off spends the
    /// selection.</item>
    /// <item><b>Roll <c>rand(100)</c></b> and take the first entry of the
    /// table whose inclusive cumulative range holds it
    /// (<see cref="CreatureAngerRules.CumulativeRanges"/>).</item>
    /// <item><b>Fall through in record order</b> when the roll lands outside
    /// every range, which a table whose chances sum to less than 100 can
    /// do.</item>
    /// </list>
    ///
    /// <para><b>ADR-0053's draw-once-and-keep was wrong in shape</b>, and the
    /// reason it was written that way is a fact about DK2 rather than an
    /// argument against re-rolling: with <c>Leave(50%)</c> an angry Goblin
    /// <i>does</i> walk out within a few rolls. What was right about it is kept
    /// — an unbuilt outcome is still never reported as a built one.</para>
    ///
    /// <para><b>Where ours stops short, and why it is one sentence.</b> The
    /// original's fall-through also runs when a chosen job's own start routine
    /// <i>fails</i> — no training room, no shrine. Ours cannot: every job in
    /// this simulation either starts (<c>Leave</c>, which the original starts
    /// unconditionally too — its routine sets the leaving state and returns
    /// success, leaving the portal search to the state) or does not exist at
    /// all. <b>A job that does not exist ends the selection where DK2 would
    /// have succeeded</b>, rather than falling through to <c>Leave</c> and
    /// turning half of every angry Goblin into a deserter it should not be. So
    /// a Goblin that draws <c>Rebel</c> stands there for five seconds and rolls
    /// again, and leaves after a couple of rounds instead of at once.</para>
    /// </summary>
    private void Decide(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (state.Activity == CreatureActivity.Leaving)
        {
            Leave(context, creature, state);
            return;
        }

        long now = context.Tick.Value;
        if (state.LastAngryChoiceTick != 0
            && now - state.LastAngryChoiceTick < CreatureAngerRules.ReselectionGateTicks)
        {
            return;
        }

        if (_seeker.TryGoForALair(context, SystemName, state, creature))
        {
            state.LastAngryChoiceTick = now;
            return;
        }

        CreatureAngerRules.CumulativeRanges(creature.AngryJobs, _ranges);
        int roll = CreatureAngerRules.RollPercent(context.Rng);

        int picked = -1;
        for (int i = 0; i < _ranges.Count; i++)
        {
            (JobType job, int low, int high) = _ranges[i];
            if (job != JobType.None && low <= roll && roll <= high)
            {
                picked = i;
                break;
            }
        }

        if (picked < 0)
        {
            for (int i = 0; i < _ranges.Count; i++)
            {
                if (_ranges[i].Job == JobType.None) continue;
                picked = i;
                break;
            }
        }

        // A table of nothing at all. No selection was made, so nothing is
        // stamped and the creature is free to try again next second — which
        // costs nothing, since the answer will be the same until its record
        // changes.
        if (picked < 0) return;

        JobType chosen = _ranges[picked].Job;
        state.LastAngryChoiceTick = now;

        context.Emit(SystemName, "creature_angry",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} roll={roll} chose={chosen} " +
            $"result={(chosen == JobType.Leave ? "started" : "unbuilt")} " +
            $"from=[{string.Join(",", creature.AngryJobs
                .Select(j => $"{j.Job}:{j.ChancePercent}%"))}]");

        // Rebel, Sulk, DestroyWalls, StealGold and the rest are real DK2
        // outcomes with nothing here to act on. Reported by the event above and
        // then left alone: the creature stays in the dungeon, angry, doing
        // nothing, until its gate opens and it rolls again. Honest, and
        // visible.
        if (chosen == JobType.Leave) Leave(context, creature, state);
    }

    /// <summary>
    /// The creature walks out — to <b>the nearest portal its keeper owns</b>,
    /// and it only actually goes when it gets there.
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-04, Q116): <i>"angry evil
    /// creature goes to the nearest available portal and leaves the dungeon.
    /// Angry converted good creature turns turncoat and starts attacking the
    /// dungeon and its creatures."</i> Which settles a question this method
    /// previously answered with a shrug — it removed the creature on the spot
    /// and said <c>via=nowhere_in_particular</c> in its own event so the lie
    /// would at least be greppable.</para>
    ///
    /// <para><b>Two consequences, and both are behaviour rather than
    /// bookkeeping.</b> A keeper whose portal is unreachable — walled off, taken
    /// by a rival, never claimed — <b>keeps a creature that wanted to leave</b>,
    /// standing there angry. And a deserter is <i>visible</i> for the length of
    /// its walk: it can be killed, and it is still on the payroll, which is
    /// exactly the window in which a player would notice and do something about
    /// it.</para>
    ///
    /// <para><b>Nearest is by path, not by line</b> — <see cref="IPathfinder"/>'s
    /// Dijkstra over the same <see cref="WorldPathGrid"/> the creature would walk
    /// on, so "nearest" already accounts for the walls in the way and for what
    /// this particular creature can cross. A route is planned only when the
    /// creature has run out of one, so an existing walk is never restarted; if a
    /// route cannot be found at all it is retried, silently, every second, and
    /// the one report of the failure goes out on the second it first happened.
    /// A portal built later is therefore an escape route, which is the
    /// forgiving reading and the one a player would expect.</para>
    ///
    /// <para><b>Its bed goes at the moment it decides</b>, not when it arrives,
    /// for two reasons. A lair that leaks a tile every time somebody storms off
    /// would quietly shrink; and a leaver still holding a bed would keep
    /// <i>curing itself</i> at −550 a second the moment it walked past its own
    /// lair, which would make the walk out reversible by accident rather than by
    /// any rule anybody wrote (Q121).</para>
    ///
    /// <para><b>The other branch is CONFIRMED and unbuilt.</b> A converted good
    /// creature does not walk out — it <i>turns turncoat and attacks the
    /// dungeon</i>. That is the <c>Rebel</c> entry in the angry job tables (a
    /// Goblin draws it half the time, a Vampire always), and the original tracks
    /// it as its own per-creature state: <c>Creature.cpp</c>'s debug dump prints
    /// <c>TurncoatPlayer</c> and <c>TurncoatTimer</c>, so becoming a turncoat
    /// means belonging to another player for a time (binary, ADR-0046 — facts
    /// only). It needs conversion, which needs the torture chamber, and the
    /// three shipped percentages that split the outcome
    /// (<c>RebelLeavingPercentage</c> 70, <c>RebelBecomeGoodPercentage</c> 15,
    /// <c>RebelDefectPercentage</c> 15) have nothing to act on. So a creature
    /// that draws Rebel still stands there.</para>
    /// </summary>
    private void Leave(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        bool decidingNow = state.Activity != CreatureActivity.Leaving;
        if (decidingNow)
        {
            LairRules.ReleaseBed(context.World, context.Database, state);
            state.Activity = CreatureActivity.Leaving;
        }

        // Standing on the way out already — either it walked here, or the anger
        // ran out while it happened to be on the portal.
        if (PortalRules.IsExitTile(world, database, state.Tile, state.OwnerPlayerId))
        {
            Gone(context, creature, state);
            return;
        }

        // Mid-walk. Leave it alone; the movement system is carrying it.
        if (state.PathIndex < state.Path.Count) return;

        var grid = new WorldPathGrid(database, world, creature);
        bool routed = _pathfinder.TryFindNearest(
            grid,
            state.Tile,
            at => PortalRules.IsExitTile(world, database, at, state.OwnerPlayerId),
            _route);

        if (routed && _route.Count > 0)
        {
            state.Path.Clear();
            state.Path.AddRange(_route);
            state.PathIndex = 0;

            if (decidingNow)
            {
                TilePos exit = _route[^1];
                context.Emit(SystemName, "creature_leaving",
                    $"creature={state.Id} type={creature.Name} " +
                    $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
                    $"anger={state.Anger} portal=({exit.X},{exit.Y}) " +
                    $"steps={_route.Count}");
            }

            return;
        }

        if (decidingNow)
        {
            context.Emit(SystemName, "creature_leave_blocked",
                $"creature={state.Id} type={creature.Name} " +
                $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
                $"anger={state.Anger} reason=no_reachable_portal");
        }
    }

    /// <summary>It reached the portal. Gone, with whatever gold it was carrying.</summary>
    private static void Gone(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        context.Emit(SystemName, "creature_left",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"anger={state.Anger} gold_lost={state.GoldHeld} via=portal");

        context.World.RemoveCreature(state.Id);
    }
}
