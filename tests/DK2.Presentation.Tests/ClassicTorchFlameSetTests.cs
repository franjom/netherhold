// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The wall flames' phase offsets and point, moved out of the Unity adapter's <c>ClassicTorchFlames</c> (plan
/// step 3f), against a copy of what it did.</summary>
public sealed class ClassicTorchFlameSetTests
{
    /// <summary>The old Set: one Dk2Lcg seeded 1, a draw per torch the first time it is seen, kept while it stays.</summary>
    [Fact]
    public void OffsetsComeFromOneSeededGeneratorInFirstSeenOrder()
    {
        var phases = new ClassicTorchFlamePhases();
        var legacy = new Dk2Lcg(1);
        int a = legacy.NextInt(4096), b = legacy.NextInt(4096), c = legacy.NextInt(4096);
        Assert.Equal(a, phases.OffsetOf(70));
        Assert.Equal(b, phases.OffsetOf(3));
        Assert.Equal(a, phases.OffsetOf(70));
        Assert.Equal(b, phases.OffsetOf(3));
        Assert.True(phases.Remove(70));
        Assert.False(phases.Remove(70));
        // Lit again, a torch draws the generator's next value.
        Assert.Equal(c, phases.OffsetOf(70));
        Assert.Equal(b, phases.OffsetOf(3));
        Assert.NotEqual(a, b);
        // A clear forgets every torch and keeps the generator's place.
        phases.Clear();
        int d = legacy.NextInt(4096);
        Assert.Equal(d, phases.OffsetOf(3));
        Assert.Equal(d, phases.OffsetOf(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(1536)]
    public void TheUnbentPointIsTheSpritePositionInTheRenderersFrame(int yaw)
    {
        var torch = new TorchView(0, new TilePos(0, 0), 4096 * 3 + 100, 8192 + 7, 6144, yaw, 255, 255, 255, 128, 6144, default);
        FpPosition3 raw = ClassicTorchFlame.Position(torch);
        Assert.Equal(new Vector3(raw.X.Raw / 4096f, raw.Z.Raw / 4096f, -raw.Y.Raw / 4096f), ClassicTorchFlame.RenderPoint(torch, null));
        Assert.Equal(Legacy(torch, null), ClassicTorchFlame.RenderPoint(torch, null));
    }

    [InstallFact]
    public void TheBentPointIsTheOldOnesToTheBit()
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")!;
        var world = WorldState.FromMap(db, map, [0, 1]);
        var layout = new TorchLayout(db);
        layout.Update(world);
        var lattice = new TerrainLattice(db, world, PlayerIds.FirstKeeper) { HighWalls = true };
        int bent = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            if (layout.At(i) is not TorchView torch)
            {
                continue;
            }

            Vector3 point = ClassicTorchFlame.RenderPoint(torch, lattice);
            Assert.Equal(Legacy(torch, lattice), point);
            if (Vector3.Distance(point, ClassicTorchFlame.RenderPoint(torch, null)) > 0.001f)
            {
                bent++;
            }
        }
        Assert.True(bent > 0);
    }

    // The old ClassicTorchFlames.Draw: the raw point over 4096 with z down, bent, then (x, -z, -y).
    private static Vector3 Legacy(in TorchView torch, TerrainLattice? lattice)
    {
        FpPosition3 raw = ClassicTorchFlame.Position(torch);
        var point = new Vector3(raw.X.Raw / 4096f, raw.Y.Raw / 4096f, -raw.Z.Raw / 4096f);
        if (lattice != null)
        {
            point = lattice.Deform(point, torch.Wall.X, torch.Wall.Y);
        }

        return new Vector3(point.X, -point.Z, -point.Y);
    }
}
