// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

public sealed partial class ImpTaskSystem
{
    private const int TeleportDepartureTicks = 4;
    private const int TeleportArrivalTicks = 2;
    private const int SteeringUnitsPerPositionUnit = 16;
    private const byte TeleportArtSlot = 32;
    private const ushort TeleportDepartureEffect = 0x141;
    private const ushort TeleportArrivalEffect = 0x142;
    private const ThingSoundEvent TeleportDepartureSound = (ThingSoundEvent)0x343;
    private const ThingSoundEvent TeleportArrivalSound = (ThingSoundEvent)0x342;

    /// <summary>States 51 and 52 wait for actions 60 and 61, then resume the saved job
    /// (0x004653F0, 0x004654A0). The action handlers move after four turns and finish
    /// arrival after two (0x00499B80, 0x00499BE0).</summary>
    private static bool UpdateTeleport(SimulationContext context, CreatureState creature)
    {
        if (creature.Dk2StateId is not (ImpStates.TeleportingOut or ImpStates.TeleportingIn))
        {
            return false;
        }
        PollTeleportAction(context, creature);
        if (creature.TeleportAction != ImpTeleportAction.None || CreatureCurrentAction.IsRunning(creature, context.Tick))
        {
            return true;
        }
        if (creature.Dk2StateId == ImpStates.TeleportingOut)
        {
            creature.TeleportAction = ImpTeleportAction.Arrival;
            creature.TeleportActionUntilTick = context.Tick.Value + TeleportArrivalTicks;
            ActionAnimationClock.Begin(creature, context.Database.CreatureById(creature.CreatureTypeId),
                TeleportArtSlot, context.Rng);
            CreatureStateMachine.TrySetState(context.Hooks, creature, ImpStates.TeleportingIn, context.Tick.Value);
            TeleportPresentation(context, creature, arriving: true);
        }
        else
        {
            WorkerPickupRules.SetEnabled(creature, true);
            CreatureStateMachine.TrySetState(context.Hooks, creature, creature.UtilityNewState, context.Tick.Value);
        }
        return true;
    }

    /// <summary>Actions 60 and 61 are polled independently of the current state, like
    /// the other creature actions (0x00499B80, 0x00499BE0).</summary>
    internal static void PollTeleportAction(SimulationContext context, CreatureState creature)
    {
        if (creature.TeleportAction == ImpTeleportAction.None || context.Tick.Value < creature.TeleportActionUntilTick)
        {
            return;
        }
        if (creature.TeleportAction == ImpTeleportAction.Departure)
        {
            FpPosition3 target = creature.TeleportTarget;
            creature.PosX = creature.LastPosX = target.X.Raw;
            creature.PosY = creature.LastPosY = target.Y.Raw;
            creature.PosZ = creature.LastPosZ = target.Z.Raw;
            creature.ReaperHidden = true;
            creature.StopMoving();
            creature.ChainTileIndex = creature.Tile.Y * context.World.Width + creature.Tile.X;
            creature.ChainOrder = ++context.World.NextChainOrder;
        }
        else if (creature.TeleportAction == ImpTeleportAction.Arrival)
        {
            creature.ReaperHidden = false;
        }
        creature.TeleportAction = ImpTeleportAction.None;
        creature.TeleportActionUntilTick = 0;
    }

    private static void TeleportPresentation(SimulationContext context, CreatureState creature, bool arriving)
    {
        ushort effect = arriving ? TeleportArrivalEffect : TeleportDepartureEffect;
        context.RequestEffect(SystemName, new ViewEffectRequest(new EffectId(effect), creature.OwnerPlayerId,
            new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ)),
            new Dk2Angle((ushort)creature.Heading)));
        ThingSoundRules.Request(context, SystemName, creature,
            arriving ? TeleportArrivalSound : TeleportDepartureSound);
    }
}
