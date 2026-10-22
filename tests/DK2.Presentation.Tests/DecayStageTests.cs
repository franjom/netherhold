// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>A Decay terrain's face texture follows its health (<c>0x004419D0</c>, Q617): a
/// Reinforced Wall steps from Brick (3) to Brick_DECAY3 (0) as Imps dig it down.</summary>
public sealed class DecayStageTests
{
    private static TerrainDef Wall(TerrainFlags flags, byte variants) => new()
    {
        Name = "Reinforced Wall", TerrainId = 9, Flags = flags, TextureVariants = variants,
        StartingHealth = 900, MaxHealth = 900, GoldValue = 0, ManaGain = 0, MaxManaGain = 0,
        Damage = 0, MaxHealthTypeTerrainId = 0, DestroyedTypeTerrainId = 0,
        Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
    };

    [Theory]
    [InlineData(900, 3)]
    [InlineData(677, 3)]   // 675 / 225 = 3
    [InlineData(676, 2)]
    [InlineData(452, 2)]
    [InlineData(451, 1)]
    [InlineData(227, 1)]
    [InlineData(226, 0)]   // health - 2 below one step
    [InlineData(1, 0)]
    public void AReinforcedWallsStageIsItsHealthLessTwoOverAQuarterOfItsMaximum(int health, int stage) =>
        Assert.Equal(stage, WorldView.DecayStage(health, Wall(TerrainFlags.Decay, 4)));

    [Fact]
    public void ATerrainWithoutDecayOrWithOneTextureHasNoStage()
    {
        Assert.Equal(-1, WorldView.DecayStage(100, Wall(0, 4)));
        Assert.Equal(-1, WorldView.DecayStage(100, Wall(TerrainFlags.Decay, 1)));
    }

    [InstallFact]
    public void TheShippedReinforcedWallIsTheOnlyDecayTerrainAndHasFourTextures()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        TerrainDef wall = Assert.Single(db.Terrain.Values, t => (t.Flags & TerrainFlags.Decay) != 0);
        Assert.Equal("Reinforced Wall", wall.Name);
        Assert.Equal(4, wall.TextureVariants);
        Assert.Equal(900, wall.MaxHealth);
    }
}
