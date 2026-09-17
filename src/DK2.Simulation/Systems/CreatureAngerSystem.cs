// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>The anger ladder — content, unhappy, angry, gone — stepped once a game
/// second.</summary>
/// <remarks>CONFIRMED: three owner answers give the shape from three separate causes
/// (Q9 pay, Q105 lairs, the bed-loss answer), and Q106 gave the unit by stopwatch,
/// 32767; the arithmetic is <see cref="CreatureAngerRules"/>'s. What an angry
/// creature does comes from its own record, not from a rule here (ADR-0061, Q122):
/// see <see cref="Decide"/> and <see cref="Leave"/>. Runs on the last tick of each
/// game second, the phase <see cref="ManaSystem"/> and
/// <see cref="CreatureHealthSystem"/> use because every rate it reads is per second,
/// and after <see cref="CreatureSleepSystem"/> so a creature that claimed a bed this
/// tick is not charged <c>AngerNoLair</c>, and after the health system so one that
/// collapsed is already <see cref="CreatureActivity.Dying"/> and skipped (ADR-0051).</remarks>
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

            // The band this creature was last reported in, not the one it is in
            // at the top of this pass. Anger reaches a creature from four places
            // and three of them run earlier in the same tick - a slap, the
            // prison, and having no work (ADR-0082) - so a "before" read from
            // the anger visible here would miss every crossing it did not cause.
            // DK2 keeps the band in its anger object and rewrites it on each add.
            CreatureMood before = state.Mood;

            CreatureAnger delta = CreatureAngerRules.PerSecond(world, database, creature, state);
            CreatureAngerRules.Apply(creature, state, delta);

            CreatureMood after = CreatureAngerRules.MoodOf(creature, state);
            state.Mood = after;

            if (after != before)
            {
                context.Emit(SystemName, "creature_mood",
                    $"creature={state.Id} type={creature.Name} " +
                    $"player={state.OwnerPlayerId} {before}->{after} " +
                    $"anger={state.Anger.Max}/{CreatureAngerRules.Ceiling} " +
                    $"worst={state.Anger.Worst} " +
                    $"unhappy_at={CreatureAngerRules.UnhappyAt(creature)} " +
                    $"[{state.Anger}]");
            }

            // A creature in the hand still minds - the ladder above ran, and
            // AngerInHand rides the same clock in HandSystem - but it can act
            // on none of it while it is off the ground. It rolls when it lands.
            if (after == CreatureMood.Angry
                && !HandRules.IsHeld(state)
                && !PrisonRules.IsImprisoned(state))
                Decide(context, creature, state);
        }
    }

    /// <summary>A fresh roll on every allowed selection, five game seconds apart,
    /// with a lair attempt before it: the original's angry chooser
    /// <c>FUN_004784c0</c> (Q122, ADR-0061).</summary>
    /// <remarks>Its order: a creature already walking out does not choose again, since
    /// leaving is a state (lair-acquisition.md §5); the five-second gate on
    /// <see cref="CreatureState.LastAngryChoiceTick"/> gates success only, so a
    /// selection that starts nothing stamps nothing; no lair means going for one
    /// instead of rolling (<see cref="LairSeeker"/>); then <c>rand(100)</c> takes the
    /// first inclusive cumulative range and otherwise falls through in record order
    /// (<see cref="CreatureAngerRules.CumulativeRanges"/>). Deviates: the original also
    /// falls through when a chosen job's start routine fails; here an unbuilt job ends
    /// the selection instead of turning half of every angry Goblin into a deserter.</remarks>
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

        // Not implemented: Rebel, Sulk, DestroyWalls, StealGold and the rest are
        // real DK2 outcomes with nothing here to act on. The event above reports
        // them as unbuilt and the creature stays in the dungeon, angry, until
        // its gate opens and it rolls again.
        if (chosen == JobType.Leave) Leave(context, creature, state);
    }

    /// <summary>Walks the creature out to the nearest portal its keeper owns; it only
    /// actually goes when it gets there.</summary>
    /// <remarks>CONFIRMED by the owner (2026-09-04, Q116): an angry evil creature goes to
    /// the nearest available portal and leaves; an angry converted good creature turns
    /// turncoat instead — not implemented, since it needs conversion
    /// (docs/research/creature-anger.md). A keeper whose portal is unreachable therefore
    /// keeps a creature that wanted to leave, and a deserter is visible and still on the
    /// payroll for the length of its walk. Nearest is by path, not line: Dijkstra over
    /// this creature's own <see cref="WorldPathGrid"/>. A route is planned only when it
    /// has none, and a failure is retried silently each second but reported once. Its bed
    /// goes when it decides, not on arrival, so a lair cannot leak a tile per storm-off
    /// and a leaver cannot cure itself at −550 a second by walking past its own lair.</remarks>
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
        if (state.HasRoute) return;

        var grid = new WorldPathGrid(database, world, creature);
        bool routed = _pathfinder.TryFindNearest(
            grid,
            state.Tile,
            at => PortalRules.IsExitTile(world, database, at, state.OwnerPlayerId),
            _route);

        if (routed && _route.Count > 0 && context.Navigation.TryRoute(state, _route[^1], Navigation.MoveOptions.Leaving))
        {

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
