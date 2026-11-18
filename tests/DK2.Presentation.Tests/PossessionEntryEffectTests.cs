// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class PossessionEntryEffectTests
{
    [InstallFact]
    public void AuthoredChosenCreatureStarsRiseOnTheSuppliedViewClockWithoutFollowingTheCreature()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var target = new CreatureState { PosX = 50000, PosY = 60000 };
        ViewEffectRequest request = PossessionEntryEffect.ForCreature(database, target, new PlayerId(3))!.Value;
        Assert.Equal(new EffectId(102), request.EffectId);
        long milliseconds = 0;
        var draw = new EffectDrawList(database, 7, () => new(milliseconds), floorHeight: _ => default);
        draw.Model.Request(request);
        draw.Advance();
        Assert.Empty(draw.Model.Particles);

        target.PosX += Fp32.One;
        milliseconds = 51;
        draw.Advance();

        Assert.Equal(16, draw.Model.Particles.Count);
        ConstructionParticle first = draw.Model.Particles[0];
        Assert.All(draw.Model.Particles, star =>
        {
            Assert.Equal((ushort)243, star.Definition.EffectElementId);
            Assert.Equal("Gsparkle", star.Definition.ArtResource.Name);
            Assert.Equal((50000, 60000, Fp32.One - 2400), (star.XRaw, star.YRaw, star.HeightRaw));
            Assert.Equal(((byte)120, (byte)80, (byte)6), (star.Red, star.Green, star.Blue));
        });
        milliseconds = 101;
        draw.Advance();
        Assert.Equal(32, draw.Model.Particles.Count);
        Assert.True(first.HeightRaw > Fp32.One - 2400);
    }

    [Fact]
    public void StagingUsesTheAuthoredShotCreationAtFeetWithoutAttachmentOrColourKindSubstitution()
    {
        GameDatabase database = Database(73, 901);
        var target = new CreatureState { PosX = 10000, PosY = 12000, PosZ = 3000 };

        ViewEffectRequest? requested = PossessionEntryEffect.ForCreature(database, target, new PlayerId(3));

        Assert.NotNull(requested);
        ViewEffectRequest effect = requested.Value;
        Assert.Equal(new EffectId(901), effect.EffectId);
        Assert.Equal(new PlayerId(3), effect.Owner);
        Assert.Equal(new FpPosition3(new(10000), new(12000), new(Fp32.One + 3000)), effect.Position);
        Assert.Equal(effect.Position, effect.SecondPoint);
        Assert.Equal(EffectHeightSpace.Absolute, effect.HeightSpace);
        Assert.Equal(EffectVisibility.Gated, effect.Visibility);
        Assert.Equal(0, effect.AttachedCreatureId);
        Assert.Equal(ShotId.None, effect.AttachedShotId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAbsentSpellOrZeroCreationEffectDoesNotInventStars(bool hasSpell)
    {
        GameDatabase database = hasSpell ? Database(73, 0)
            : new GameDatabase([], [], [], new GlobalVariables());

        Assert.Null(PossessionEntryEffect.ForCreature(database, new CreatureState(), new PlayerId(3)));
    }

    private static GameDatabase Database(byte shotId, ushort effectId)
    {
        var spell = new KeeperSpellDef
        {
            Name = "renamed possession", KeeperSpellId = 2, ShotTypeId = shotId,
            ManaCost = 0, ManaUpkeepPerSecond = 0,
        };
        var shot = new ShotDef([])
        {
            Name = "authored shot", ShotId = shotId, CreationEffectId = effectId,
            MeshResource = new("", 0, new byte[12], 0, 0, 0, 0),
            Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), SoundCategory = "",
        };
        return new([], [], [], new GlobalVariables(), keeperSpells: [spell], shots: [shot]);
    }
}
