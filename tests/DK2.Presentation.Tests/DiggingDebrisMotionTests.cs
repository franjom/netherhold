// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class DiggingDebrisMotionTests
{
    [InstallFact]
    public void CrumblingRockStartsAcrossTheTileTopAndFallsWithSmallerFragments()
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        var native = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        var crumble = new RoomConstructionParticles(db, 7, () => new EffectTime(0),
            terrainDebrisStyle: TerrainDebrisStyle.Crumbling);
        var request = ViewEffectRequest.AtTileCentre(new EffectId(db.TerrainById(2).DestroyedEffectId),
            PlayerIds.FirstKeeper, new TilePos(3, 3), new Fp32(Fp32.One));
        var emission = new SimEvent(new GameTick(0), "test", "effect_request", "") { EffectRequest = request };
        native.Emit(emission);
        crumble.Emit(emission);
        native.AdvanceTo(new EffectTime(51));
        crumble.AdvanceTo(new EffectTime(51));
        ConstructionParticle[] original = native.Particles.Where(p => p.Definition.ArtResource.Type == 5).ToArray();
        ConstructionParticle[] rocks = crumble.Particles.Where(p => p.Definition.ArtResource.Type == 5).ToArray();
        Assert.Equal(48, rocks.Length);
        Assert.Equal(original[0].InitialScale * 2 / 3, rocks[0].InitialScale);
        Assert.All(rocks, p =>
        {
            Assert.InRange(p.HeightRaw, request.Position.Z.Raw + 3072, request.Position.Z.Raw + 4096);
            Assert.InRange(Math.Abs(p.XRaw - request.Position.X.Raw), 0, 1638);
            Assert.InRange(Math.Abs(p.YRaw - request.Position.Y.Raw), 0, 1638);
            Assert.True(p.VelocityZ < 0);
            Assert.InRange(Math.Abs(p.VelocityX), 0, 1024);
            Assert.InRange(Math.Abs(p.VelocityY), 0, 1024);
        });
        int[] heights = rocks.Select(p => p.HeightRaw).ToArray();
        crumble.AdvanceTo(new EffectTime(101));
        for (int i = 0; i < rocks.Length; i++) Assert.True(rocks[i].HeightRaw < heights[i]);
    }

    [InstallFact]
    public void RockMeshesTumbleOncePerViewUpdateAndStopSpinningOnFloorContact()
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        var model = new RoomConstructionParticles(db, 7, () => new EffectTime(0));
        var request = ViewEffectRequest.AtTileCentre(new EffectId(6), PlayerIds.FirstKeeper,
            new TilePos(3, 3), new Fp32(Fp32.One));
        model.Emit(new SimEvent(new GameTick(0), "test", "effect_request", "") { EffectRequest = request });
        model.AdvanceTo(new EffectTime(51));
        ConstructionParticle[] rocks = model.Particles.Where(p => p.Definition.ArtResource.Type == 5).ToArray();
        Assert.Equal(48, rocks.Length);
        Assert.All(rocks, p =>
        {
            Assert.InRange(p.HeadingRaw, 0, 2047);
            Assert.Equal(0, p.PitchRaw);
            Assert.Equal(0, p.RollRaw);
            foreach (int spin in new[] { p.HeadingSpinRaw, p.PitchSpinRaw, p.RollSpinRaw })
            {
                Assert.True(spin <= 255 || spin >= 1792);
            }
        });
        Assert.Contains(rocks, p => p.PitchSpinRaw != 0 && p.RollSpinRaw != 0);
        int[] headings = rocks.Select(p => p.HeadingRaw).ToArray();
        model.AdvanceTo(new EffectTime(151));
        for (int i = 0; i < rocks.Length; i++)
        {
            Assert.Equal(2, rocks[i].InitialHealth - rocks[i].Health);
            Assert.Equal((headings[i] + rocks[i].HeadingSpinRaw) & 2047, rocks[i].HeadingRaw);
            Assert.Equal(rocks[i].PitchSpinRaw, rocks[i].PitchRaw);
            Assert.Equal(rocks[i].RollSpinRaw, rocks[i].RollRaw);
        }
        bool bounced = false;
        for (int step = 4; step <= 28; step++)
        {
            var airborne = model.Particles.Where(p => p.Definition.ArtResource.Type == 5
                && p.VelocityZ < 0 && p.PitchSpinRaw != 0).ToArray();
            model.AdvanceTo(new EffectTime(step * 50 + 1));
            foreach (ConstructionParticle p in airborne.Where(p => model.Particles.Contains(p) && p.VelocityZ >= 0))
            {
                bounced = true;
                Assert.Equal(0, p.HeadingSpinRaw);
                Assert.Equal(0, p.PitchSpinRaw);
                Assert.Equal(0, p.RollSpinRaw);
            }
        }
        Assert.True(bounced);
    }

}
