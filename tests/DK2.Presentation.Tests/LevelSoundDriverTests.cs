// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;
using static DK2.Presentation.Tests.ClassicSoundMixerTests;

namespace DK2.Presentation.Tests;

/// <summary>The level's sound driver (<see cref="LevelSoundDriver"/>, moved from Unity's <c>DungeonRunner.Sound</c>): the thing sounds the
/// simulation emits, the terrain ambience around the listener's tile (<c>0x0044E2A0</c>, sound-effects-continuous.md section 4), the
/// footsteps and the party pop, without allocating once the level runs.</summary>
public sealed class LevelSoundDriverTests
{
    private static readonly PlayerId Viewer = new(PlayerIds.FirstKeeper);
    private const uint Bed = 0x2EA, Step = CreatureFootsteps.Walk, Thing = 0x120;

    private static async Task<(LevelSoundDriver Driver, FakeOutput Output, WorldState World)> Driver()
    {
        TerrainDef water = new()
        {
            Name = "water", TerrainId = 2, Flags = 0, StartingHealth = 1, MaxHealth = 1, GoldValue = 0, ManaGain = 0, MaxManaGain = 0,
            Damage = 0, MaxHealthTypeTerrainId = 2, DestroyedTypeTerrainId = 2, Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
            SoundCategory = "WATER_TEST",
        };
        var variables = new GlobalVariables();
        foreach (VariableTypeId id in Enum.GetValues<VariableTypeId>()) variables.Misc[id] = new MiscVariable(id, 0, 0, 0);
        var database = new GameDatabase([water], [], [], variables);
        var map = new LevelMap(4, 4, Enumerable.Repeat(new MapTile(2, PlayerIds.Unowned, 0, 0), 16).ToArray());
        WorldState world = WorldState.FromMap(database, map, [PlayerIds.Good, PlayerIds.FirstKeeper]);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++) world.Tile(x, y).SetExploredBy(Viewer);

        var item = new SoundMapBytes.Item(20f, [(1, 1, 0)]);
        FakeAssets assets = new FakeAssets()
            .With(new SoundCategory("water_test"), SoundMapBytes.Maps("Global\\Water", new SoundMapBytes.Group(Bed, 1, 0, item)))
            .With(new SoundCategory("imp"), SoundMapBytes.Maps("Global\\Imp", new SoundMapBytes.Group(Step, 1, 0, item),
                new SoundMapBytes.Group(Thing, 1, 0, item)))
            .With(new SoundCategory("effects"), SoundMapBytes.Maps("Global\\Effects", new SoundMapBytes.Group(PartyPopSound.EventId, 1, 0, item)));
        var output = new FakeOutput();
        var mixer = new ClassicSoundMixer(output, assets, 0);
        await mixer.Prefetch([new SoundCategory("water_test"), new SoundCategory("imp"), new SoundCategory("effects")], 1);
        return (new LevelSoundDriver(mixer, database, Viewer), output, world);
    }

    [Fact]
    public async Task TheAmbienceBedPlaysAtItsCellsCentreFromTheListenersTile()
    {
        (LevelSoundDriver driver, FakeOutput output, WorldState world) = await Driver();

        driver.Update(world, new TilePos(1, 1), 33);

        Assert.Equal(new Vector3(1.5f, 0f, -1.5f), driver.Mixer.ListenerFocus);
        FakeSource bed = Assert.Single(output.Effects);
        Assert.True(bed.IsPlaying);
        // The sixteen explored tiles' mean is (2, -2): half a tile from the listener's point on each axis.
        Assert.Equal(new Vector2(.5f, -.5f), bed.Offset);
    }

    [Fact]
    public async Task AThingSoundTheSimulationEmitsPlaysOnTheNextUpdate()
    {
        (LevelSoundDriver driver, FakeOutput output, WorldState world) = await Driver();
        WorldSoundDirector sounds = driver.Observe(null);
        sounds.Emit(new SimEvent(new GameTick(1), "imp", "thing_sound", "")
        {
            ThingSound = new ThingSoundRequest(SoundSourceId.Of(new CreatureId(7)), "IMP", (ThingSoundEvent)Thing,
                new FpPosition3(new Fp32(4096), new Fp32(4096), new Fp32(0))),
        });

        driver.Update(world, null, 33);

        Assert.Single(output.Effects, s => s.IsPlaying);
    }

    [Fact]
    public async Task AStepAndAPartyEffectAreRequestsInTheirCategories()
    {
        (LevelSoundDriver driver, FakeOutput output, WorldState _) = await Driver();

        driver.Footstep(7, "IMP", wading: false, crossing: 1, new Vector3(1f, 0f, -1f));
        driver.Footstep(7, "IMP", wading: false, crossing: 0, new Vector3(1f, 0f, -1f));
        driver.EffectCreated(new ViewEffectRequest(new EffectId(CasinoJackpotRules.PartyEffectId), Viewer,
            new FpPosition3(new Fp32(4096), new Fp32(4096), new Fp32(0))));

        Assert.Equal(2, output.Effects.Count);
    }

    [Fact]
    public async Task AFrameWhoseTurnAndListenerAreUnchangedAllocatesNothing()
    {
        (LevelSoundDriver driver, FakeOutput _, WorldState world) = await Driver();
        driver.Observe(null);
        for (int frame = 0; frame < 3; frame++)
        {
            driver.Update(world, new TilePos(1, 1), frame * 33);
            driver.Footstep(7, "IMP", wading: false, crossing: 1 + (frame & 1), new Vector3(1f, 0f, -1f));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 3; frame < 60; frame++)
        {
            driver.Mixer.PlayDecoded(frame * 33);
            driver.Mixer.UpdateSpeech(frame * 33);
            driver.Update(world, new TilePos(1, 1), frame * 33);
            driver.Footstep(7, "IMP", wading: false, crossing: 1 + (frame & 1), new Vector3(1f, 0f, -1f));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
