// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>Applies this tick's <see cref="PlaceDoorCommand"/>s and <see cref="PlaceTrapCommand"/>s
/// through <see cref="BlueprintPlacementRules"/> (ADR-0231).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/blueprint-placement.md")]
public sealed class BlueprintSystem : ISimulationSystem
{
    private const string SystemName = "Blueprint";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
        {
            (string kind, TilePos at, byte type, BlueprintPlacementRules.Verdict verdict) = command switch
            {
                PlaceDoorCommand door => ("door", door.Tile, door.DoorId,
                    BlueprintPlacementRules.PlaceDoor(context.World, context.Database, door.Tile, door.PlayerId, door.DoorId)),
                PlaceTrapCommand trap => ("trap", trap.Tile, trap.TrapId,
                    BlueprintPlacementRules.PlaceTrap(context.World, context.Database, trap.Tile, trap.PlayerId, trap.TrapId)),
                _ => ("", default, (byte)0, BlueprintPlacementRules.Verdict.NoSuchType),
            };
            if (kind.Length == 0) continue;
            context.Emit(SystemName, verdict == BlueprintPlacementRules.Verdict.Allowed ? "blueprint_placed" : "blueprint_refused",
                $"player={command.PlayerId} kind={kind} type={type} at=({at.X},{at.Y}) reason={verdict}");
        }
    }
}
