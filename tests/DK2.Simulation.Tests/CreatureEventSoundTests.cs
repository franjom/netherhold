// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The sounds of docs/research/sound-effects.md section 13 that need a story of their own: the Horned
/// Reaper's arrival (<c>0x32D</c>, state 235) and a burning creature (<c>0x340</c>, every tick it burns).</summary>
public sealed class CreatureEventSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte SummonSpellId = 5;   // SummonHornyTests.Setup's Summon Horny

    private static ThingSoundRequest[] Sounds(ListEventSink sink) =>
        sink.Events.Where(e => e.ThingSound.HasValue).Select(e => e.ThingSound!.Value).ToArray();

    private static FpPosition3 At(CreatureState creature) =>
        new(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ));

    /// <summary>State 235 (<c>0x00474740</c>) requests <c>0x32D</c> with the Reaper's handle at its position on its first
    /// update, in the tick it enters 236 (age 1 and age 12 or more both end it).</summary>
    [Fact]
    public void ASummonedReaperRequests32dOnceOnItsFirstUpdateAtItsPositionWithItsVoice()
    {
        var (db, world, at) = SummonHornyTests.Setup();
        var cast = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        cast.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, SummonSpellId,
            CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y))));
        CreatureState reaper = Assert.Single(world.Creatures);
        Assert.Equal(235, reaper.Dk2StateId);
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new ReaperSystem()]);
        sim.ObserveWith(sink);

        sim.Tick();

        Assert.Equal(236, reaper.Dk2StateId);
        ThingSoundRequest sound = Assert.Single(Sounds(sink));
        Assert.Equal(ThingSoundEvent.ReaperArrived, sound.Event);
        Assert.Equal((ushort)0x32D, (ushort)sound.Event);
        Assert.Equal("CREATURE_GOBLIN", sound.Category);   // the fixture's Reaper is a Goblin's definition
        Assert.Equal(SoundSourceId.Of(new CreatureId(reaper.Id)), sound.Source);
        Assert.Equal(At(reaper), sound.Position);
        Assert.True(sound.Voiced);

        sim.Run(40);
        Assert.Single(Sounds(sink));   // 236, 237 and the greeting ask nothing: 0x12C has no group, so it is not requested here
    }

    /// <summary><c>Creature_UpdateTimedEffects</c> (<c>0x0048E160</c>) requests <c>0x340</c> with the creature's handle at
    /// its position on every tick its burning counter is nonzero, the tick it runs out included. DK2 then stops
    /// the sound (slot +0x38) as the fire ends; ours has no stop request (the loop owner, plan W7.3), so the
    /// last sample plays out.</summary>
    [Fact]
    public void ABurningCreatureRequests340OnEveryTickItBurns_ThePlayedSampleIsNotCutShort()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(2, 2));
        goblin.BurningTicks = 3;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem()]);
        sim.ObserveWith(sink);

        sim.Run(5);

        ThingSoundRequest[] heard = Sounds(sink).Where(s => s.Event == ThingSoundEvent.Burning).ToArray();
        Assert.Equal(3, heard.Length);
        Assert.Equal((ushort)0x340, (ushort)ThingSoundEvent.Burning);
        Assert.All(heard, s =>
        {
            Assert.Equal("CREATURE_GOBLIN", s.Category);
            Assert.Equal(SoundSourceId.Of(new CreatureId(goblin.Id)), s.Source);
            Assert.Equal(At(goblin), s.Position);
            Assert.True(s.Voiced);
        });
        Assert.Equal(0, goblin.BurningTicks);
    }
}
