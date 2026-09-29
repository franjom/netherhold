// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Pays every keeper the mana their land generated, once a second.
/// <para><b>Once a second, not once a tick</b>: the rate the data gives is per
/// second and the original's tick rate is unmeasured (Q1). One application every
/// <see cref="SimulationTiming.TicksPerSecond"/> ticks keeps the arithmetic
/// exact in integers and the state hash independent of an unmeasured constant
/// (ADR-0017). Whether the original accrues smoothly or in whole seconds is
/// UNKNOWN, and shows only in the shape of the counter, never in a total.</para>
/// <para>The first payment lands at the end of the first second rather than on
/// tick zero, so a level does not begin by handing out a second that has not
/// elapsed.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/mana.md")]
public sealed class ManaSystem : ISimulationSystem
{
    private const string SystemName = "Mana";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond != 0) return;

        WorldState world = context.World;
        int[] gain = ManaRules.GainPerSecondByPlayerIndex(world, context.Database);
        int[] upkeep = ManaRules.UpkeepPerSecondByPlayerIndex(world, context.Database);

        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            int changed = ManaRules.ApplyOneSecond(context.Database, ref player, gain[i], upkeep[i]);

            // Both halves are reported, not just the net, because that is what
            // the original puts on screen: a green figure and a red one.
            if (changed != 0)
                context.Emit(SystemName, changed > 0 ? "mana_gained" : "mana_drained",
                    $"player={player.PlayerId} amount={Math.Abs(changed)} " +
                    $"rate={gain[i]}/s upkeep={upkeep[i]}/s total={player.Mana}");

            // The warning block and the removal scan follow the payment in 0x004C10E0, which
            // runs for keepers alone: the hero and neutral tags return before it (0x004B9EC0).
            if (PlayerIds.IsKeeper(player.PlayerId))
                Pressure(context, ref player, gain[i] - upkeep[i]);
        }
    }

    /// <summary>The net rate is the unclamped one: a keeper floored at zero still reads a
    /// negative income, which is what keeps its warnings moving.</summary>
    private static void Pressure(SimulationContext context, ref PlayerState player, int netRate)
    {
        GameDatabase database = context.Database;
        byte playerId = player.PlayerId;
        uint now = ManaRemovalLifecycle.Turn(context.Tick);
        uint minimum = (uint)database[VariableTypeId.MinimumImpThreshold];
        ManaPressureResult result = ManaPressureRules.Advance(
            player.ManaPressure, now, player.Mana, netRate,
            ManaRemovalLifecycle.EvilWorkers(context.World, database, playerId), minimum,
            (ushort)database[VariableTypeId.LowManaWarningThreshold],
            (byte)database[VariableTypeId.InsufficientManaImpEvaluationPeriodSeconds],
            (uint)SimulationTiming.TicksPerSecond);
        player.ManaPressure = result.State;

        if (result.Warning != 0)
        {
            context.World.Messages.Post(context.World, playerId, PlayerMessageKind.LowManaWarning, result.Warning, 0);
            context.Emit(SystemName, "mana_warning",
                $"player={playerId} stage={result.Warning} mana={player.Mana} net={netRate}/s turn={now}");
        }

        if (!result.ScanForRemoval) return;
        CreatureState? chosen = ManaRemovalLifecycle.Select(context, playerId, now, minimum);
        context.Emit(SystemName, "mana_removal_scan",
            $"player={playerId} chosen={(chosen is null ? -1 : chosen.Id)} " +
            $"type={(chosen is null ? 0 : chosen.CreatureTypeId)} mana={player.Mana} turn={now}");
    }
}
