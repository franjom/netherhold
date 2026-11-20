// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>Random wall deterioration, <c>0x004BB6D0</c>, from the keeper frame <c>0x004BB2A0</c> every turn
/// (ADR-0085). Switched off in shipped data: variables 145 and 146 are 0 and no level overrides them
/// (variables-corpus-sweep.md), so the routine returns before drawing.</summary>
/// <remarks>Each of variable-145 picks (an unsigned byte) draws x then y from the world's random,
/// <c>random(width + 1) + 1</c> and <c>random(height + 1) + 1</c>, and skips a point off the map. A tile
/// qualifies when its terrain is solid and not impenetrable, holds no gold, is owned by the keeper and
/// touches the keeper's own floor (<c>0x00453B30</c>): the repair predicate <c>0x00453E30</c> without its
/// health test. It then takes variable 146 as a signed byte through the tile-work routine with its own
/// keeper as the worker, so a negative value damages and a positive one heals.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/balance-variables-in-the-binary.md")]
public static class WallDeteriorationRules
{
    private const string SystemName = "wall deterioration";

    public static void Run(SimulationContext context, PlayerId keeper)
    {
        GameDatabase database = context.Database;
        int picks = (byte)database[VariableTypeId.NumberOfRandomWallDeteriorationsPerSecond];
        if (picks == 0) return;
        int delta = (sbyte)database[VariableTypeId.WallDeteriorationDamage];
        WorldState world = context.World;

        for (int i = 0; i < picks; i++)
        {
            int x = context.Rng.NextInt(world.Width + 1) + 1;   // 0x004BB70F
            int y = context.Rng.NextInt(world.Height + 1) + 1;  // 0x004BB73B
            if (x >= world.Width || y >= world.Height) continue;

            var at = new TilePos(x, y);
            ref TileState tile = ref world.Tile(at);
            TerrainDef terrain = database.TerrainById(tile.TerrainId);
            if (!terrain.IsSolid || terrain.IsImpenetrable || terrain.GoldValue != 0) continue;
            if (tile.OwnerPlayerId != keeper || !OwnFloorRules.Touches(database, world, at, keeper)) continue;

            context.Emit(SystemName, "wall_deteriorated", $"player={keeper} at=({x},{y}) delta={delta}");
            SpecialSystem.Work(context, (y * world.Width) + x, delta, keeper);
        }
    }
}
