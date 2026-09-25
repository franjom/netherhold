// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>The Combat Pit's states, once a turn: 137, 138, 139 and 141, 139's one-tile walks, and
/// idle's return of a fighter to 138 (<c>combat-pit.md</c> §2, ADR-0213). The fight is the combat
/// system's, through the forced target 139 hands out.</summary>
/// <remarks>Runs after <see cref="CombatSystem"/>, as DK2's state handler follows its fight
/// processing, and before the movement. A state entered this turn runs from the next, as
/// DK2's dispatcher does. 138 on the rim of the keeper's own pit hands the creature to the
/// spectators' 142 (<see cref="CombatPitSpectatorRules"/>, ADR-0227). Not built: 140, which
/// nothing enters; action <c>0x2C</c> at the level cap.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/combat-pit.md")]
public sealed class CombatPitSystem : ISimulationSystem
{
    private const string SystemName = "CombatPit";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        long now = context.Tick.Value;
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (HandRules.IsHeld(creature) || CreatureDeathRules.IsDown(creature)) continue;

            int state = creature.Dk2StateId;
            if (state == CreatureStateMachine.Idle)
            {
                if (creature.CombatPitFighter) BackToThePit(context, creature);
                continue;
            }

            if (StateShadow.IsTravelling(creature) && creature.UtilityNewState == CombatPitRules.WaitingState)
            {
                if (creature.HasRoute) continue;
                creature.StopMoving();
                StateShadow.Arrive(context, SystemName, creature);
                creature.PitStateEnteredTick = now;
                continue;
            }

            if (!CombatPitRules.IsPitState(state) || creature.PitStateEnteredTick == now) continue;

            bool stays = state switch
            {
                CombatPitRules.ArrivingState => Arriving(context, creature),
                CombatPitRules.PlacingState => Placing(context, creature),
                CombatPitRules.WaitingState => Waiting(context, creature),
                _ => ThrownOut(context, creature),
            };
            if (!stays) StateShadow.ToIdle(context, SystemName, creature);
        }
    }

    /// <summary>Idle (<c>0x004773D0</c>): 1 and then 138; refused, no longer a fighter.</summary>
    private static void BackToThePit(SimulationContext context, CreatureState creature)
    {
        if (!Move(context, creature, CombatPitRules.PlacingState)) creature.CombatPitFighter = false;
    }

    /// <summary>137: on the floor, every fighter there is placed again; then 138.</summary>
    private static bool Arriving(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        int here = TileIndex(world, creature);
        RoomInstance? pit = CombatPitRules.PitAt(world, context.Database, here);
        if (CombatPitRules.IsFloor(pit, here))
        {
            IReadOnlyList<CreatureState> creatures = world.Creatures;
            for (int i = 0; i < creatures.Count; i++)
            {
                CreatureState other = creatures[i];
                if (!other.CombatPitFighter || !CombatPitRules.IsOnTheMap(other)) continue;
                if (!CombatPitRules.IsFloor(pit, TileIndex(world, other))) continue;
                CreatureStateMachine.TrySetState(other, CreatureStateMachine.Idle);
                Move(context, other, CombatPitRules.PlacingState);
            }
        }

        Move(context, creature, CombatPitRules.PlacingState);
        return true;
    }

    /// <summary>138: on the floor a fighter and 139; on the rim of the keeper's own pit, 142.</summary>
    private static bool Placing(SimulationContext context, CreatureState creature)
    {
        if (context.Tick.Value - creature.PitStateEnteredTick == 1) creature.StopMoving();
        int here = TileIndex(context.World, creature);
        RoomInstance? pit = CombatPitRules.PitAt(context.World, context.Database, here);
        if (pit is null) return false;
        if (!CombatPitRules.IsFloor(pit, here)) return CombatPitSpectatorRules.FromPlacing(context, SystemName, creature, pit);

        creature.CombatPitFighter = true;
        if (!Move(context, creature, CombatPitRules.WaitingState)) return false;
        context.Emit(SystemName, "pit_fighter", $"creature={creature.Id} player={creature.OwnerPlayerId} tile={here}");
        return true;
    }

    /// <summary>139: below the cap and not engaged, pair with the nearest fighter; else the anger,
    /// and a step to a neighbouring tile of the same kind.</summary>
    private static bool Waiting(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int here = TileIndex(world, creature);
        RoomInstance? pit = CombatPitRules.PitAt(world, database, here);
        if (creature.Level < database[VariableTypeId.CombatPitMaxExperienceLevel]
            && creature.FightTargetKind == FightTargetKind.None && pit != null
            && CombatPitRules.Opponent(world, database, pit, creature) is { } opponent)
        {
            CombatPitRules.Pair(world, creature, opponent);
            context.Emit(SystemName, "pit_paired", $"creature={creature.Id} opponent={opponent.Id}");
            return true;
        }

        // def+0x6C7 as loaded: the file's per-second figure over four, every turn (ADR-0228).
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        int anger = SimulationTiming.LoaderRatePerTick(definition.AngerNoWork);
        CreatureAngerRules.Apply(world, definition, creature, AngerBucket.Company, anger);
        CreatureAngerRules.Apply(world, definition, creature, AngerBucket.Work, anger);

        RoomInstance? room = pit ?? RoomIndex.RoomAt(world, database, here);
        if (room is null) return true;
        int step = CombatPitRules.NeighbourStep(context, room, creature);
        if (step < 0) return true;
        int x = CreatureState.CentreOf(step % world.Width), y = CreatureState.CentreOf(step / world.Width);
        if (!context.Navigation.TryRouteTo(creature, x, y, Navigation.MoveOptions.ExactSpot)) return true;
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, CombatPitRules.WaitingState);
        return true;
    }

    /// <summary>141: a turn's wait, then 138 on any pit.</summary>
    private static bool ThrownOut(SimulationContext context, CreatureState creature)
    {
        if (context.Tick.Value - creature.PitStateEnteredTick <= 1) return true;
        return CombatPitRules.PitAt(context.World, context.Database, TileIndex(context.World, creature)) != null
            && Move(context, creature, CombatPitRules.PlacingState);
    }

    /// <summary>The room reset <c>0x004D7E00</c> and the freed room's <c>0x004D8000</c>: every
    /// fighter on the pit's tiles stops being one, loses its forced target and goes to 141.</summary>
    /// <remarks>Ours is called with the pit as it was before a sale, or as it is after a build,
    /// and covers the attached list with the same walk.</remarks>
    public static void Reset(SimulationContext context, RoomInstance pit)
    {
        WorldState world = context.World;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (!creature.CombatPitFighter || !CombatPitRules.IsOnTheMap(creature)) continue;
            if (!pit.Contains(TileIndex(world, creature))) continue;
            CombatPitRules.Unmark(world, creature);
            if (!CreatureStateMachine.TrySetState(creature, CombatPitRules.ThrownOutState)) continue;
            creature.PitStateEnteredTick = context.Tick.Value;
            context.Emit(SystemName, "pit_thrown_out", $"creature={creature.Id} player={creature.OwnerPlayerId}");
        }
    }

    /// <summary>A gated move into a pit state, stamped for its <c>Age</c>; entering category 16 on
    /// the floor makes a fighter, as its enter hook <c>0x0047AAE0</c> does.</summary>
    internal static bool Move(SimulationContext context, CreatureState creature, int stateId)
    {
        bool entering = !CreatureStateMachine.HasState(creature)
            || CreatureStateTable.Get(creature.Dk2StateId).Category != CreatureStateCategory.CombatPit;
        if (!CreatureStateMachine.TrySetState(creature, stateId)) return false;
        creature.PitStateEnteredTick = context.Tick.Value;
        if (entering && CreatureStateTable.Get(stateId).Category == CreatureStateCategory.CombatPit)
        {
            int here = TileIndex(context.World, creature);
            if (CombatPitRules.IsFloor(CombatPitRules.PitAt(context.World, context.Database, here), here))
                creature.CombatPitFighter = true;
        }

        return true;
    }

    private static int TileIndex(WorldState world, CreatureState creature)
    {
        TilePos tile = creature.Tile;
        return (tile.Y * world.Width) + tile.X;
    }
}
