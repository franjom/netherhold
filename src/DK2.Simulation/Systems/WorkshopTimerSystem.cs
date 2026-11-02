// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>The Workshop handler's four effect timers (<c>Workshop_Tick</c> <c>0x004F4B20</c>), run after
/// the hated-company step, which the same handler calls first. Rules and evidence:
/// <see cref="WorkshopTimerRules"/>.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/workshop-timers.md")]
public sealed class WorkshopTimerSystem : ISimulationSystem
{
    public string Name => "workshop timers";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        List<RoomInstance>? workshops = null;
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
            if (WorkshopTimerRules.IsWorkshop(room.Definition)) (workshops ??= []).Add(room);
        if (workshops is null) return;

        // DK2's room manager links each room at the head of its list as it is made, so it ticks the
        // newest first (0x004EC490, 0x004EC450); a room's list position is its construction order.
        workshops.Sort((a, b) =>
        {
            int byAge = world.TileAtIndex(b.TileIndices[0]).RoomListPosition.Value
                .CompareTo(world.TileAtIndex(a.TileIndices[0]).RoomListPosition.Value);
            return byAge != 0 ? byAge : a.TileIndices[0].CompareTo(b.TileIndices[0]);
        });
        foreach (RoomInstance room in workshops) WorkshopTimerRules.Tick(context, room);
    }
}
