// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;

namespace DK2.Behavior.Tests;

public sealed class FogOfWarOnRealDataTests
{
    [Fact]
    public void ExactlySixShippedTerrainsRevealThroughFog()
    {
        string? install = Dk2Install.Find();
        if (install is null) return;

        string[] actual = GameDatabaseLoader.Load(install).Terrain.Values
            .Where(t => t.IsRevealedThroughFog)
            .OrderBy(t => t.TerrainId)
            .Select(t => t.Name.Trim())
            .ToArray();

        Assert.Equal(
            ["Impenetrable Rock", "Water", "Lava", "Gold", "Gems", "Edge of Map"],
            actual);
    }
}
