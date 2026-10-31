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
/// see <see cref="Decide"/> and <see cref="Leave"/>. Runs on the last tick of each game
/// second, as every rate it reads is per second, in the creature's update after the needs
/// routine, as DK2's angry chooser does; ours charges the need-anger there too, where
/// <c>0x00478D00</c> runs before the needs routine, and before the health system (ADR-0554).</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-anger.md")]
public sealed class CreatureAngerSystem : ICreatureUpdateStep
{
    private const string SystemName = "CreatureAnger";

    private readonly IPathfinder _pathfinder;
    private readonly LairSeeker _seeker;
    private readonly List<TilePos> _route = [];
    private readonly List<(JobType Job, int Low, int High)> _ranges = [];
    private bool _onTheSecond;

    public CreatureAngerSystem() : this(new GridPathfinder()) { }

    public CreatureAngerSystem(IPathfinder pathfinder)
    {
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        _seeker = new LairSeeker(_pathfinder);
    }

    public string Name => SystemName;

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    // Every tick, because DK2's defection deadline is 30 ticks and 7.5 game seconds is not
    // something a per-second step can express.
    public void BeginTurn(SimulationContext context)
    {
        Defections(context);
        _onTheSecond = (context.Tick.Value + 1) % SimulationTiming.TicksPerSecond == 0;
    }

    /// <summary>The ladder and the angry chooser, in the creature's update after the needs routine
    /// and before its state handler (<c>0x00476FC0</c>).</summary>
    public void Update(SimulationContext context, CreatureState state)
    {
        if (state.Dk2StateId == LeavingRules.PortalDropState) return;
        if (!_onTheSecond) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (state.Possessed) return;
        CreatureDef creature = database.CreatureById(state.CreatureTypeId);

        if (CreatureEligibility.IsExcluded(state, CreatureGuards.Down | CreatureGuards.Preoccupied)) return;
        if (!CreatureAngerRules.CanBeAngered(creature)) return;

        // The band this creature was last reported in, not the one it is in
        // at the top of this step. Anger reaches a creature from four places
        // and three of them run outside it - a slap, the prison, and having
        // no work (ADR-0082) - so a "before" read from the anger visible here
        // would miss every crossing it did not cause. DK2 keeps the band in
        // its anger object and rewrites it on each add.
        CreatureMood before = state.Mood;

        CreatureAnger delta = CreatureAngerRules.PerSecond(world, database, creature, state);
        CreatureAngerRules.Apply(world, creature, state, delta, CreatureAngerRules.NeedComplaints);

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
        // A pit fighter's idle goes back to the pit before any chooser (0x004773D0).
        // The mood choosers run for a keeper's creatures only (0x00476FC0, ADR-0485).
        if (after == CreatureMood.Angry
            && NeedRules.RunsNeeds(state)
            && state.Activity != CreatureActivity.UnbuiltHandler
            && !HandRules.IsHeld(state)
            && !PrisonRules.IsImprisoned(state)
            && !state.CombatPitFighter
            && !context.TremorMoodDispatchedThisTick.Contains(state))
        {
            context.AngryChooserRanThisTick.Add(state);
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
            $"result={(chosen is JobType.Leave or JobType.Rebel ? "started" : "unbuilt")} " +
            $"from=[{string.Join(",", creature.AngryJobs
                .Select(j => $"{j.Job}:{j.ChancePercent}%"))}]");

        // Not implemented: Sulk, DestroyWalls and the rest are real DK2 outcomes with
        // nothing here to act on; no shipped angry table holds StealGold, which the
        // unhappy chooser runs (ADR-0223). The event above reports them as
        // unbuilt and the creature stays in the dungeon, angry, until its gate
        // opens and it rolls again.
        if (chosen == JobType.Leave) Leave(context, creature, state);
        else if (chosen == JobType.Rebel) Rebel(context, creature, state);
    }

    /// <summary>Angry job 16. The Portal first, and turning Good only when there is no
    /// way out: <c>0x00475170</c> (rebellion.md §2).</summary>
    /// <remarks>Its three guards refuse silently, and the original returns success either
    /// way it goes — so the chooser never falls through past a rebellion, and a creature
    /// that gets as far as the countdown has spent its selection. What it does <i>not</i>
    /// reproduce: <c>RebelLeader</c>, which nothing reads, and creature action 43.
    /// <c>RebelActive</c> is set past the guards, as DK2 marks the creature before it looks
    /// for the exit; the door contact reads it.</remarks>
    private bool Rebel(
        SimulationContext context, CreatureDef creature, CreatureState state, bool deferLeavingHandler = false)
    {
        if (!RebellionRules.CanRebel(state)) return false;
        if (RebellionRules.IsDefecting(state)) return false;

        // Ours, not DK2's: a creature already on the side it would defect to has
        // nowhere to go, and running the countdown again would overwrite the keeper it
        // rebelled against with itself. DK2 has no such guard - it also has no angry
        // hero creatures to need one.
        if (state.OwnerPlayerId == RebellionRules.DefectsTo) return false;
        state.RebelActive = true;

        // The same walk job 10 takes, into the same state: DK2 routes a rebel to the
        // Portal with state 194 "leaving Dungeon", which is what Leave sets. The probe
        // comes first and matters: job 10 sets that state blind, where job 16 only goes
        // if it can get there, so a rebel with no way out never gives up its bed.
        if (CanReachAnExit(context, creature, state))
        {
            if (deferLeavingHandler)
            {
                StartLeaveSelection(context, state);
                return true;
            }
            if (Leave(context, creature, state)) return true;
        }

        CreatureStateMachine.TrySetState(context.Hooks, state, CreatureStateMachine.Idle);
        state.RebelCountdownTicks = RebellionRules.DefectDelayTicks;

        context.Emit(SystemName, "creature_rebelling",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"anger={state.Anger.Max} defects_in={RebellionRules.DefectDelayTicks}");
        return true;
    }

    internal bool TryStartMoodJob(
        SimulationContext context, CreatureDef definition, CreatureState creature, JobType job)
    {
        if (job == JobType.Rebel)
        {
            bool accepted = Rebel(context, definition, creature, deferLeavingHandler: true);
            if (accepted && creature.RebelCountdownTicks > 0)
                context.TremorRebellionStartedThisTick.Add(creature);
            return accepted;
        }
        if (job != JobType.Leave) return false;
        // Job 10 accepts before states 194–196 try to find an exit.
        StartLeaveSelection(context, creature);
        return true;
    }

    private static void StartLeaveSelection(SimulationContext context, CreatureState creature)
    {
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
        if (LeavingRules.Start(context, creature, SystemName))
            LairRules.ReleaseBed(context.World, context.Database, creature);
    }

    /// <summary>Is there a Portal of this creature's own keeper it can walk to?
    /// <c>0x004C61C0(3, position, ...)</c> searches the creature's own player's rooms, so
    /// an enemy portal is never a candidate.</summary>
    private bool CanReachAnExit(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        if (PortalRules.IsExitTile(world, database, state.Tile, state.OwnerPlayerId)) return true;

        var grid = new WorldPathGrid(database, world, creature);
        return _pathfinder.TryFindNearest(
            grid,
            state.Tile,
            at => PortalRules.IsExitTile(world, database, at, state.OwnerPlayerId),
            _route)
            && _route.Count > 0;
    }

    /// <summary>The countdown of DK2's state 271, run every tick (rebellion.md §3).</summary>
    /// <remarks>At zero the creature changes owner to the hero side, is given
    /// <see cref="JobType.KillPlayer"/> naming the keeper it left, and stops leaving. The
    /// job is remembered and not performed: job 19 has no handler here, so the defector
    /// then fights as any hero creature does.</remarks>
    private static void Defections(SimulationContext context)
    {
        for (int i = context.World.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState state = context.World.Creatures[i];
            if (context.TremorRebellionStartedThisTick.Contains(state)) continue;
            if (state.RebelCountdownTicks <= 0) continue;

            if (CreatureDeathRules.IsDown(state))
            {
                state.RebelCountdownTicks = 0;
                continue;
            }

            if (--state.RebelCountdownTicks > 0) continue;

            // DK2 reads the hero id from a global that always names a player. A world
            // built without the hero side has nobody to defect to, so the creature
            // stays where it is - angry, and free to roll again.
            if (!context.World.HasPlayer(RebellionRules.DefectsTo))
            {
                context.Emit(SystemName, "creature_defect_blocked",
                    $"creature={state.Id} player={state.OwnerPlayerId} " +
                    $"reason=no_player_{RebellionRules.DefectsTo}");
                continue;
            }

            byte was = state.OwnerPlayerId;
            context.World.ChangeCreatureOwner(context.Database, state, RebellionRules.DefectsTo);
            state.RebelAgainstPlayerId = was;
            state.CurrentJob = JobType.KillPlayer;
            CreatureStateMachine.TrySetState(context.Hooks, state, CreatureStateMachine.Idle);
            state.RebelActive = true; // step 4 marks it again (rebellion.md §3)

            CreatureDef creature = context.Database.CreatureById(state.CreatureTypeId);
            context.Emit(SystemName, "creature_defected",
                $"creature={state.Id} type={creature.Name} " +
                $"player={was}->{state.OwnerPlayerId} " +
                $"at=({state.Tile.X},{state.Tile.Y}) job=KillPlayer target={was}");
        }
    }

    /// <summary>Walks the creature out to the nearest portal its keeper owns; it only
    /// actually goes when it gets there (Q116).</summary>
    /// <remarks>Job 10, and job 16's first branch: both enter DK2's state 194. Nearest
    /// is by path, not line - Dijkstra over this creature's own
    /// <see cref="WorldPathGrid"/>; a route is planned only when it has none, and a
    /// failure is retried silently each second but reported once. Its bed goes when it
    /// decides, not on arrival, so a lair cannot leak a tile per storm-off and a leaver
    /// cannot cure itself at -550 a second walking past its own lair. That commit is
    /// unconditional because DK2's job 10 sets the state without looking for a portal
    /// at all, where <see cref="Rebel"/> looks first.</remarks>
    /// <returns><c>true</c> when it is on its way out - on an exit, walking, or newly
    /// routed. <c>false</c> is no reachable portal, the branch <see cref="Rebel"/> needs.</returns>
    private bool Leave(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        bool decidingNow = state.Activity != CreatureActivity.Leaving;
        if (decidingNow)
        {
            if (!LeavingRules.Start(context, state, SystemName)) return false;
            LairRules.ReleaseBed(context.World, context.Database, state);
        }

        // The walk itself is DK2's states 194-196 — the nearest entrance of anyone —
        // shared with a fallen keeper's creatures (LeavingRules, creature-leaving.md).
        bool leaving = LeavingRules.Step(context, creature, state, SystemName, out TilePos exit);
        if (!decidingNow) return leaving;

        if (leaving)
        {
            if (world.CreatureById(state.Id) is not null)
                context.Emit(SystemName, "creature_leaving",
                    $"creature={state.Id} type={creature.Name} " +
                    $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
                    $"anger={state.Anger} portal=({exit.X},{exit.Y})");
        }
        else
        {
            context.Emit(SystemName, "creature_leave_blocked",
                $"creature={state.Id} type={creature.Name} " +
                $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
                $"anger={state.Anger} reason=no_reachable_portal");
        }

        return leaving;
    }
}
