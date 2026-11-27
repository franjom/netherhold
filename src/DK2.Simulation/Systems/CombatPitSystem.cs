// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

public enum CombatPitActionId : byte { None = 0, Victory = 35, AtLevelCap = 44, SpectatorCheer = 69, SpectatorOtherCheer = 70 }

/// <summary>The Combat Pit's states, once a turn: 137 through141, 139's one-tile walks, and
/// idle's return of a fighter to 138 (<c>combat-pit.md</c> §2, ADR-0213). The fight is the combat
/// system's, through the forced target 139 hands out.</summary>
/// <remarks>Runs in the creature's update after <see cref="CombatSystem"/>, as DK2's state
/// handler follows its fight processing, and before the movement. A state entered this turn runs from the next, as
/// DK2's dispatcher does. 138 on the rim of the keeper's own pit hands the creature to the
/// spectators' 142 (<see cref="CombatPitSpectatorRules"/>, ADR-0227). State140 can be restored
/// from a save; no ordinary gameplay entry has been found.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/combat-pit.md")]
public sealed class CombatPitSystem : ICreatureUpdateStep
{
    private const string SystemName = "CombatPit";

    public string Name
    {
        get
        {
            return SystemName;
        }
    }

    public void Execute(SimulationContext context)
    {
        CreatureUpdateSystem.RunAlone(context, this);
    }

    public void Update(SimulationContext context, CreatureState creature)
    {
        long now = context.Tick.Value;
        if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Sidelined))
        {
            return;
        }

        int state = creature.Dk2StateId;
        if (state == CreatureStateMachine.Idle)
        {
            // 1's handler sends a fighter back; not in the turn a combat handler set 1 (0x00476FC0).
            if (context.IdleHandlerWaits(creature))
            {
                return;
            }

            if (PossessionRules.FollowerTurn(context, SystemName, creature))
            {
                return;
            }

            if (creature.CombatPitFighter)
            {
                BackToThePit(context, creature);
            }
            else
            {
                creature.PrisonOwnerPlayerId = PrisonRules.NoPrison;
                creature.CurrentJob = JobType.None;
            }
            return;
        }

        if (StateShadow.IsTravelling(creature) && creature.UtilityNewState == CombatPitRules.WaitingState)
        {
            if (creature.HasRoute)
            {
                return;
            }

            creature.StopMoving();
            StateShadow.Arrive(context, SystemName, creature);
            creature.PitStateEnteredTick = now;
            return;
        }

        if (!CombatPitRules.IsPitState(state) || creature.PitStateEnteredTick == now)
        {
            return;
        }

        bool stays = state switch
        {
            CombatPitRules.ArrivingState => Arriving(context, creature),
            CombatPitRules.PlacingState => Placing(context, creature),
            CombatPitRules.WaitingState => Waiting(context, creature),
            CombatPitRules.VictoryState => Victory(context, creature),
            _ => ThrownOut(context, creature),
        };
        if (!stays)
        {
            StateShadow.ToIdle(context, SystemName, creature);
        }
    }

    /// <summary>Idle (<c>0x004773D0</c>): 1 and then 138; refused, no longer a fighter.</summary>
    private static void BackToThePit(SimulationContext context, CreatureState creature)
    {
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle);
        if (!Move(context, creature, CombatPitRules.PlacingState))
        {
            creature.CombatPitFighter = false;
        }
    }

    /// <summary>137: on the floor, every fighter there is placed again; then 138.</summary>
    private static bool Arriving(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        int here = TileIndex(world, creature);
        RoomInstance? pit = CombatPitRules.PitAt(world, context.Database, here);
        if (CombatPitRules.IsFloor(pit, here))
        {
            EntityList<CreatureState> creatures = world.Creatures;
            for (int i = 0; i < creatures.Count; i++)
            {
                CreatureState other = creatures[i];
                if (!other.CombatPitFighter || !CombatPitRules.IsOnTheMap(other))
                {
                    continue;
                }

                if (!CombatPitRules.IsFloor(pit, TileIndex(world, other)))
                {
                    continue;
                }

                CreatureStateMachine.TrySetState(context.Hooks, other, CreatureStateMachine.Idle);
                Move(context, other, CombatPitRules.PlacingState);
            }
        }

        Move(context, creature, CombatPitRules.PlacingState);
        return true;
    }

    /// <summary>138: on the floor a fighter and 139; on the rim of the keeper's own pit, 142.</summary>
    private static bool Placing(SimulationContext context, CreatureState creature)
    {
        if (context.Tick.Value - creature.PitStateEnteredTick == 1)
        {
            creature.StopMoving();
        }

        int here = TileIndex(context.World, creature);
        RoomInstance? pit = CombatPitRules.PitAt(context.World, context.Database, here);
        if (pit is null)
        {
            return false;
        }

        if (!CombatPitRules.IsFloor(pit, here))
        {
            return CombatPitSpectatorRules.FromPlacing(context, SystemName, creature, pit);
        }

        creature.CombatPitFighter = true;
        if (!Move(context, creature, CombatPitRules.WaitingState))
        {
            return false;
        }

        creature.PrisonOwnerPlayerId = pit.OwnerPlayerId;
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
        if (creature.Level >= database[VariableTypeId.CombatPitMaxExperienceLevel])
        {
            if (context.Tick.Value - creature.PitStateEnteredTick == 1)
            {
                BeginAction(context, creature, CombatPitActionId.AtLevelCap);
                return true;
            }
            if (CreatureCurrentAction.IsRunning(creature, context.Tick))
            {
                return true;
            }
        }
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
        CreatureAngerRules.Apply(world, definition, creature, AngerBucket.Company, anger, AngerComplaint.General);
        CreatureAngerRules.Apply(world, definition, creature, AngerBucket.Work, anger, AngerComplaint.NoWork);

        RoomInstance? room = pit ?? RoomIndex.RoomAt(world, database, here);
        if (room is null)
        {
            return true;
        }

        int step = CombatPitRules.NeighbourStep(context, room, creature);
        if (step < 0)
        {
            return true;
        }

        int x = CreatureState.CentreOf(step % world.Width), y = CreatureState.CentreOf(step / world.Width);
        if (!context.Navigation.TryRouteTo(creature, x, y, Navigation.MoveOptions.ExactSpot))
        {
            return true;
        }

        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, CombatPitRules.WaitingState);
        return true;
    }

    private static bool Victory(SimulationContext context, CreatureState creature)
    {
        uint age = unchecked((uint)(context.Tick.Value - creature.PitStateEnteredTick));
        uint limitTicks = unchecked((uint)(CombatPitRules.NativeTicksPerGameSecond(context.Database) * 4));
        if (age <= limitTicks)
        {
            if (!CreatureCurrentAction.IsRunning(creature, context.Tick))
            {
                BeginAction(context, creature, CombatPitActionId.Victory);
            }

            return true;
        }
        if (CreatureCurrentAction.IsRunning(creature, context.Tick))
        {
            return true;
        }

        Move(context, creature, CombatPitRules.PlacingState);
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        int anger = unchecked((short)context.Database[VariableTypeId.ModifyAngerOfCombatPitVictor]);
        CreatureAngerRules.Apply(context.World, definition, creature, AngerBucket.Treatment, anger, AngerComplaint.General);
        return true;
    }

    private static void BeginAction(SimulationContext context, CreatureState creature, CombatPitActionId action)
    {
        CreatureStateMachine.EndAction(creature);
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        byte slot = action == CombatPitActionId.Victory ? (byte)8 : (byte)31;
        ActionAnimationClock.Begin(creature, definition, slot, context.Rng);
        creature.CombatPitAction = action;
        creature.ActionHandlerDone = false;
        context.CombatPitActionsPolledThisTick.Add(creature);
        CreatureActionSounds.Begin(context, SystemName, creature, (int)action);
    }

    internal static void BeginCheerAction(SimulationContext context, string system, CreatureState creature,
        CombatPitActionId action, byte cycles)
    {
        CreatureStateMachine.EndAction(creature);
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        byte slot = action == CombatPitActionId.SpectatorCheer ? (byte)8 : (byte)20;
        ActionAnimationClock.BeginCounted(creature, definition, slot, cycles, context.Rng);
        creature.CombatPitAction = action;
        creature.ActionHandlerDone = false;
        context.CombatPitActionsPolledThisTick.Add(creature);
        CreatureActionSounds.Begin(context, system, creature, (int)action);
    }

    public static void PollAction(SimulationContext context, CreatureState creature)
    {
        if (creature.CombatPitAction == CombatPitActionId.None
            || !context.CombatPitActionsPolledThisTick.Add(creature))
        {
            return;
        }

        creature.ActionHandlerDone = creature.CombatPitAction == CombatPitActionId.AtLevelCap;
        if (creature.InHand)
        {
            CancelAction(creature);
        }
        else if (ActionAnimationClock.CountedActionEnded(creature))
        {
            creature.CombatPitAction = CombatPitActionId.None;
            creature.ActionHandlerDone = false;
            creature.ActionCount = 0;
        }
    }

    public static void AdvanceAction(SimulationContext context, CreatureState creature)
    {
        if (creature.CombatPitAction == CombatPitActionId.None
            || !context.CombatPitActionsAdvancedThisTick.Add(creature))
        {
            return;
        }

        ActionAnimationClock.Advance(creature, context.Database.CreatureById(creature.CreatureTypeId));
    }

    public static void CancelAction(CreatureState creature)
    {
        if (creature.CombatPitAction == CombatPitActionId.None)
        {
            return;
        }

        creature.CombatPitAction = CombatPitActionId.None;
        ActionAnimationClock.Clear(creature);
    }

    /// <summary>141: a turn's wait, then 138 on any pit.</summary>
    private static bool ThrownOut(SimulationContext context, CreatureState creature)
    {
        if (context.Tick.Value - creature.PitStateEnteredTick <= 1)
        {
            return true;
        }

        return CombatPitRules.PitAt(context.World, context.Database, TileIndex(context.World, creature)) != null
            && Move(context, creature, CombatPitRules.PlacingState);
    }

    /// <summary>0x004D7E00: clear on-map fighters on the rebuilt room's tiles, then release
    /// all remaining attached creatures to 141. The free/owner callback releases only holders.</summary>
    public static void Reset(SimulationContext context, RoomInstance pit)
    {
        WorldState world = context.World;
        EntityList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (!creature.CombatPitFighter || !CombatPitRules.IsOnTheMap(creature))
            {
                continue;
            }

            if (!pit.Contains(TileIndex(world, creature)))
            {
                continue;
            }

            CombatPitRules.Unmark(world, creature);
            if (!CreatureStateMachine.TrySetState(context.Hooks, creature, CombatPitRules.ThrownOutState))
            {
                continue;
            }

            creature.PitStateEnteredTick = context.Tick.Value;
            context.Emit(SystemName, "pit_thrown_out", $"creature={creature.Id} player={creature.OwnerPlayerId}");
        }
        ReleaseAttached(context, place => place.In(pit));
    }

    /// <summary>0x004D8000, also the tail of 0x004D7E00: release the room's attached list.
    /// The predicate retains the old room's membership while its tiles are rebuilt.</summary>
    internal static void ReleaseAttached(SimulationContext context, Predicate<RoomPlace> belongs)
    {
        while (true)
        {
            CreatureState? holder = null;
            foreach (CreatureState candidate in context.World.Creatures)
            {
                if (candidate.HeldPlace.IsHeld && belongs(candidate.HeldPlace)
                    && (holder is null || candidate.HeldPlaceSerial > holder.HeldPlaceSerial))
                {
                    holder = candidate;
                }
            }

            if (holder is null)
            {
                return;
            }

            if (!CreatureStateMachine.TrySetState(context.Hooks, holder, CombatPitRules.ThrownOutState))
            {
                continue;
            }

            holder.PitStateEnteredTick = context.Tick.Value;
            context.Emit(SystemName, "pit_thrown_out", $"creature={holder.Id} player={holder.OwnerPlayerId}");
        }
    }

    /// <summary>A gated move into a pit state, stamped for its <c>Age</c>; entering category 16 on
    /// the floor makes a fighter, as its enter hook <c>0x0047AAE0</c> does.</summary>
    internal static bool Move(SimulationContext context, CreatureState creature, int stateId)
    {
        bool entering = !CreatureStateMachine.HasState(creature)
            || CreatureStateTable.Get(creature.Dk2StateId).Category != CreatureStateCategory.CombatPit;
        if (!CreatureStateMachine.TrySetState(context.Hooks, creature, stateId))
        {
            return false;
        }

        creature.PitStateEnteredTick = context.Tick.Value;
        if (entering && CreatureStateTable.Get(stateId).Category == CreatureStateCategory.CombatPit)
        {
            int here = TileIndex(context.World, creature);
            if (CombatPitRules.IsFloor(CombatPitRules.PitAt(context.World, context.Database, here), here))
            {
                creature.CombatPitFighter = true;
            }
        }

        return true;
    }

    private static int TileIndex(WorldState world, CreatureState creature)
    {
        TilePos tile = creature.Tile;
        return (tile.Y * world.Width) + tile.X;
    }
}
