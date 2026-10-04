// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-reaper.md")]
public static class ReaperRules
{
    public const byte SpellId = 5;
    public const byte ProcessType = 33;
    public const int ArrivingState = 235;
    public const int MaterializingState = 236;
    public const int GreetingState = 237;
    public const int DepartingState = 242;
    private const int CreaturePoolCapacity = 448;

    public static CreatureState? OwnedReaper(WorldState world, GameDatabase database, in PlayerState player)
    {
        foreach (CreatureState creature in world.CreatureOwnershipOrder)
            if (CreatureStateMachine.HasState(creature) && creature.OwnerPlayerId == player.PlayerId
                && database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsHorny))
                return creature;
        return null;
    }

    public static int UpkeepPerSecond(WorldState world, GameDatabase database, in PlayerState player)
    {
        CreatureState? reaper = OwnedReaper(world, database, player);
        uint turn = unchecked((uint)(world.CurrentTick.Value + 1));
        if (player.Status != PlayerStatus.Playing || reaper is null || reaper.IsOneShotReaper
            || reaper.Dk2StateId is 235 or 237 or 242 || reaper.HornyTick >= turn)
            return 0;
        return database.KeeperSpellById(SpellId)?.ManaUpkeepPerSecond ?? 0;
    }

    public static bool AfterPayment(SimulationContext context, in PlayerState player)
    {
        int upkeep = UpkeepPerSecond(context.World, context.Database, player);
        if (upkeep == 0 || player.Mana >= upkeep) return false;
        Depart(context, OwnedReaper(context.World, context.Database, player)!);
        context.Emit("reaper", "unaffordable", $"player={player.PlayerId} upkeep={upkeep} mana={player.Mana}");
        return true;
    }

    public static void Depart(SimulationContext context, CreatureState reaper)
    {
        EnterState(context, reaper, ReaperState.Departing);
        reaper.ReaperCanAcquireFight = reaper.ReaperIsAttackable = false;
        reaper.StopMoving();
        reaper.Body.Vel = default;
    }

    public static void RunLifecycle(SimulationContext context)
    {
        foreach (CreatureState reaper in context.World.Creatures)
        {
            if (reaper.Dk2StateId is not (ArrivingState or MaterializingState or GreetingState or DepartingState)) continue;
            uint age = ++reaper.ReaperStateAgeTicks;
            switch (reaper.Dk2StateId)
            {
                case ArrivingState:
                    reaper.ReaperComplete = false;
                    if (age == 1 || age >= 12)
                        EnterState(context, reaper, ReaperState.Materializing);
                    break;
                case MaterializingState:
                    if (age == 1) reaper.ReaperAlpha = 0;
                    if (reaper.ReaperAlpha == byte.MaxValue)
                        EnterState(context, reaper, ReaperState.Greeting);
                    else reaper.ReaperAlpha = (byte)Math.Min(byte.MaxValue, reaper.ReaperAlpha + 32);
                    break;
                case GreetingState:
                    if (age == 1) BeginAction(context, reaper);
                    else if (context.Tick.Value >= reaper.ActionUntilTick)
                    {
                        reaper.ReaperActionId = 0;
                        reaper.ReaperCanAcquireFight = reaper.ReaperIsAttackable = true;
                        EnterState(context, reaper, ReaperState.Idle);
                    }
                    break;
                case DepartingState:
                    if (age == 1 && reaper.ReaperActionId != 47) BeginAction(context, reaper);
                    if (age >= 6 * SimulationTiming.OriginalTicksPerGameSecond)
                    {
                        reaper.ReaperAlpha = (byte)Math.Max(0, reaper.ReaperAlpha - 32);
                        if (reaper.ReaperAlpha == 0) reaper.Health = 0;
                    }
                    break;
            }
        }
    }

    public static void RemoveDeparted(SimulationContext context)
    {
        for (int i = context.World.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState reaper = context.World.Creatures[i];
            if (reaper.Dk2StateId == DepartingState && reaper.ReaperAlpha == 0 && reaper.Health == 0)
            {
                context.World.RemoveCreature(reaper.Id);
                context.Emit("reaper", "departed", $"creature={reaper.Id} player={reaper.OwnerPlayerId}");
            }
        }
    }

    private static void BeginAction(SimulationContext context, CreatureState reaper)
    {
        CreatureDef definition = context.Database.CreatureById(reaper.CreatureTypeId);
        CreatureAnimationTiming animation = definition.ReaperSummonAnimation.Flags.HasFlag(CreatureAnimationFlags.Present)
            ? definition.ReaperSummonAnimation : definition.ReaperSummonFallbackAnimation;
        reaper.ReaperActionId = 46;
        reaper.ActionUntilTick = context.Tick.Value + AnimationTimingRules.FirstCycleHoldTicks(animation);
        if (animation.ResourceType is not (1 or 3)) reaper.ReaperAlpha = byte.MaxValue;
    }

    public static void Create(SimulationContext context, ShotState shot)
    {
        if (shot.Data1 != 0) return;
        WorldState world = context.World;
        var at = new TilePos(shot.PosX >> 12, shot.PosY >> 12);
        context.Navigation.Refresh();
        if (!world.Contains(at) || (context.Navigation.NavByte(at.X, at.Y) & 8) != 0) return;
        if (world.Creatures.Count(CreatureStateMachine.HasState) >= CreaturePoolCapacity) return;
        CreatureDef? definition = ReaperDefinition(context.Database);
        if (definition is null || !world.HasPlayer(shot.OwnerPlayerId)) return;
        CreatureState reaper = world.SpawnCreature(context.Database, definition.CreatureId, shot.OwnerPlayerId, at);
        reaper.PosX = reaper.LastPosX = shot.PosX;
        reaper.PosY = reaper.LastPosY = shot.PosY;
        EnterState(context, reaper, ReaperState.Arriving);
        for (int level = 1; level < 10; level++) CreatureLevelRules.LevelUp(context.Hooks, definition, reaper);
        reaper.HornyTick = unchecked((uint)(context.Tick.Value + 1 + 50 * SimulationTiming.OriginalTicksPerGameSecond));
        context.Emit("reaper", "created", $"creature={reaper.Id} player={reaper.OwnerPlayerId} level={reaper.Level}");
    }

    private static void EnterState(SimulationContext context, CreatureState reaper, ReaperState state)
    {
        if (CreatureStateMachine.TrySetState(context.Hooks, reaper, (int)state, context.Tick.Value))
            reaper.ReaperStateAgeTicks = 0;
    }

    private static CreatureDef? ReaperDefinition(GameDatabase database)
    {
        foreach (CreatureDef candidate in database.Creatures.Values)
        {
            byte id = candidate.Flags.HasFlag(CreatureFlags.IsUnique)
                ? checked((byte)candidate.UniqueBaseCreatureId) : candidate.CreatureId;
            if (database.Creatures.TryGetValue(id, out CreatureDef? definition)
                && definition.Flags.HasFlag(CreatureFlags.IsHorny))
                return definition;
        }
        return null;
    }

    private enum ReaperState : ushort
    {
        Arriving = ArrivingState, Materializing = MaterializingState, Greeting = GreetingState,
        Idle = Systems.CreatureJobSystem.HornyIdleState, Departing = DepartingState,
    }
}
