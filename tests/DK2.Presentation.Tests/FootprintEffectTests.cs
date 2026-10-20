// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class FootprintEffectTests
{
    [Theory]
    [InlineData((ushort)99, "bloodfoot", 0x40101u)]
    [InlineData((ushort)100, "waterfoot", 0x100u)]
    public void DirectFlatArtAppearsAtBirthAndExpiresAfterFiftyViewUpdates(ushort id, string art, uint flags)
    {
        var effect = Definition(id, art, flags);
        var db = new GameDatabase([], [], [], new GlobalVariables(), effects: [effect]);
        var model = new RoomConstructionParticles(db, 1, () => new EffectTime(0));
        var point = new FpPosition3(new(8192), new(12288), new(0));
        model.Request(new ViewEffectRequest(new EffectId(id), PlayerIds.FirstKeeper, point, new Dk2Angle(256)));
        ConstructionParticle sprite = Assert.Single(model.Particles);
        Assert.Equal(art, sprite.Definition.ArtResource.Name);
        Assert.Equal(flags, sprite.Definition.ArtResource.Flags);
        Assert.Equal(819, sprite.ScaleRaw);
        Assert.Equal(8192, sprite.XRaw);
        Assert.Equal(12288, sprite.YRaw);
        Assert.Equal(4096, sprite.HeightRaw);
        Assert.Equal(256, sprite.HeadingRaw);
        Assert.Equal(0, sprite.Red);
        Assert.Equal(0, sprite.Green);
        Assert.Equal(0, sprite.Blue);
        model.AdvanceTo(new EffectTime(2451));
        Assert.Single(model.Particles);
        Assert.Equal(8192, sprite.XRaw);
        Assert.Equal(12288, sprite.YRaw);
        model.AdvanceTo(new EffectTime(2501));
        Assert.Empty(model.Particles);
    }

    [InstallTheory]
    [InlineData((ushort)99, "bloodfoot", 0x40101u)]
    [InlineData((ushort)100, "waterfoot", 0x100u)]
    public void ShippedFootprintsUseTheResearchedDirectSpriteDefinitions(ushort id, string art, uint flags)
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var effect = db.Effects[id];
        Assert.Equal(art, effect.ArtResource.Name);
        Assert.Equal(flags, effect.ArtResource.Flags);
        Assert.Equal(0, effect.GenerationType);
        Assert.Equal(50, effect.MinHp);
        Assert.Equal(50, effect.MaxHp);
        Assert.Equal(819, effect.MinScale);
        Assert.Equal(819, effect.MaxScale);
        var model = new RoomConstructionParticles(db, 1, () => new EffectTime(0));
        model.Request(new ViewEffectRequest(new EffectId(id), PlayerIds.FirstKeeper,
            new FpPosition3(new(8192), new(8192), new(0))));
        Assert.Single(model.Particles);
    }

    private static EffectDef Definition(ushort id, string art, uint flags) => new([])
    {
        EffectId = id, Name = art, ArtResource = new KwdArtResource(art, flags, new byte[12], 2, 0, 0, 0),
        Light = new KwdLight(0, 0, 0, 0, 0, 0, 0, 0, 0), GenerateIds = [],
        GenerationType = 0, MinHp = 50, MaxHp = 50, MinScale = 819, MaxScale = 819,
        Flags = 0x480
    };
}
