// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The shipped data under <see cref="RoomHealthRules"/> (tile-work.md "Room health", Q500).
/// Gated with [InstallFact], so it reports SKIPPED without an installation.</summary>
public class RoomHealthOnRealDataTests
{
    private static GameDatabase RealDatabase() => GameDatabaseLoader.Load(Dk2InstallGate.Path);

    /// <summary>The deficit kept on the tiles equals DK2's clamped sums for a tile added or a merge
    /// only while StartingHealth ≤ MaxHealth; every shipped room terrain has them equal (10000,
    /// 2000 for the bridges, 100 to 208 for the gates), which also makes the level-start pass for
    /// neutral rooms (<c>0x0050B966</c>) clamp to nothing.</summary>
    [InstallFact]
    public void EveryRoomTerrainStartsAtItsMaximum()
    {
        GameDatabase db = RealDatabase();
        foreach (RoomDef room in db.Rooms.Values)
        {
            TerrainDef terrain = db.TerrainById(room.TerrainId);
            Assert.True(terrain.StartingHealth == terrain.MaxHealth, $"{room.Name}: {terrain.StartingHealth} of {terrain.MaxHealth}");
        }
    }

    /// <summary>The room tick's regeneration is live and inert: no shipped room gains health.</summary>
    [InstallFact]
    public void NoShippedRoomHasAHealthGain()
    {
        Assert.All(RealDatabase().Rooms.Values, r => Assert.Equal(0, r.HealthGain));
    }

    /// <summary>Action 10's neutral arm reads variable 136 (<c>0x006F257C</c>), five times the rival
    /// rate: −5000 and −1000 a tick before the rate.</summary>
    [InstallFact]
    public void ANeutralRoomConvertsFiveTimesFasterThanARivals()
    {
        GameDatabase db = RealDatabase();
        Assert.Equal(-20000, db[VariableTypeId.ConvertNeutralRoomHealth]);
        Assert.Equal(-4000, db[VariableTypeId.ConvertRoomHealth]);
        Assert.Equal(-1500, db[VariableTypeId.AttackRoomHealth]);
        Assert.Equal(20000, db[VariableTypeId.RepairRoomHealth]);

        // DK2 divides each by variable 19 (4) at load with a truncating IDIV; ours shifts the product
        // down by 14 instead, which is the same only while each is a multiple of four.
        foreach (VariableTypeId v in new[] { VariableTypeId.ConvertNeutralRoomHealth, VariableTypeId.ConvertRoomHealth,
                                             VariableTypeId.AttackRoomHealth, VariableTypeId.RepairRoomHealth })
            Assert.Equal(0, db[v] % 4);
    }
}
