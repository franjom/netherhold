// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The Casino party's pop, <c>0x344</c> in the global effects category (<c>0x004D6610</c>), which DK2 requests only
/// when it created the party's effect (<c>0x004D65E9</c>), and creation (<c>View_CreateVisibleEffect</c>,
/// <c>0x00440420</c>) fails off the map, on a solid tile, out of the camera unless the effect has flag
/// <c>0x8000</c>, and on an unexplored tile; the view's gates stand for those.</summary>
public sealed class PartyPopSoundTests
{
    private static readonly KwdArtResource EmptyArt = new("", 0, new byte[12], 0, 0, 0, 0);

    private static EffectDef Effect(uint flags) => new([])
    {
        Name = "Fireworks", EffectId = 320, ArtResource = EmptyArt, Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0),
        GenerateIds = [], GenerationType = 4, ElementsPerTurn = 1, MinHp = 1, MaxHp = 1, Flags = flags,
    };

    private static ViewEffectRequest Request() =>
        new(new(320), PlayerIds.FirstKeeper, new FpPosition3(new(0x3000), new(0x5000), new(0x1000)));

    private static RoomConstructionParticles Model(uint flags, Func<bool> explored, Func<bool> inCamera) =>
        new(new GameDatabase([], [], [], new GlobalVariables(), effects: [Effect(flags)]), 7, () => default,
            effectVisible: _ => explored(), cameraVisible: _ => inCamera(), floorHeight: _ => default);

    [Fact]
    public void TheEffectIsReportedCreatedOnlyWhereDk2sCreationGatesPassAndTheCameraGateIsBypassedByFlag8000()
    {
        bool explored = true, inCamera = true;
        var created = new List<ViewEffectRequest>();
        RoomConstructionParticles model = Model(0x4440, () => explored, () => inCamera);
        model.EffectCreated = created.Add;

        model.Request(Request());
        Assert.Single(created);

        inCamera = false;
        model.Request(Request());
        Assert.Single(created);   // out of the camera: no effect, so no pop

        inCamera = true;
        explored = false;
        model.Request(Request());
        Assert.Single(created);   // a tile the player has not seen

        var bypass = new List<ViewEffectRequest>();
        RoomConstructionParticles flagged = Model(0x4440 | 0x8000, () => true, () => false);
        flagged.EffectCreated = bypass.Add;
        flagged.Request(Request());
        Assert.Single(bypass);    // flag 0x8000 skips the camera test
    }

    [Fact]
    public void ThePopIs0x344InTheEffectsCategoryAtTheEffectsPositionWithoutAVoiceAndOnlyForTheFireworks()
    {
        SoundRequest pop = PartyPopSound.For(Request())!.Value;

        Assert.Equal("effects", pop.Category.Name);
        Assert.Equal(0x344u, pop.Id.Value);
        Assert.Equal(new Vector3(3f, 1f, -5f), pop.World);   // (x, height, -y) in tiles
        Assert.Equal(SoundVoiceId.None, pop.Voice);
        Assert.Null(PartyPopSound.For(Request() with { EffectId = new(321) }));
    }
}
