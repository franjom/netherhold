// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Dungeon Heart's sounds, in <c>ROOM_DUNGEON_HEART</c> (the room definition's terrain's
/// category): <c>0xF1</c> when the local player's heart opens (<c>0x004D9E00</c>, handle 0, centre, height
/// 0x1000); <c>0x329</c> for every blow before the invulnerability test (<c>0x004DA2DB</c>) and <c>0xDA</c>
/// when it dies (<c>0x004DA5B2</c>), both with the room's handle at the heart; <c>0xF4</c> on the death turn
/// (<c>0x004DAC38</c>) at the heart tile, height 0. The heart object's own position is the tile centre
/// at the floor height ours reads for a room (0x1000), a deviation: ours keeps no heart object.</summary>
public sealed class HeartSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const string Heart = "ROOM_DUNGEON_HEART";

    private static (GameDatabase Db, WorldState World) NewWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 3; y < 8; y++)
            for (int x = 3; x < 8; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), heart, Keeper);
        for (int y = 10; y < 15; y++)
            for (int x = 10; x < 15; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), heart, Rival);
        world.Player(Keeper).HeartHealth = 5000;
        world.Player(Rival).HeartHealth = 100;
        return (db, world);
    }

    private static ThingSoundRequest[] Sounds(ListEventSink events) =>
        events.Events.Where(e => e.ThingSound.HasValue).Select(e => e.ThingSound!.Value).ToArray();

    private static FpPosition3 At(int x, int y, int height) =>
        new(new Fp32(CreatureState.CentreOf(x)), new Fp32(CreatureState.CentreOf(y)), new Fp32(height));

    private sealed class Act(Action<SimulationContext> action) : ISimulationSystem
    {
        public string Name => "Act";
        public void Execute(SimulationContext context) => action(context);
    }

    [Fact]
    public void EveryHeartRequestsF1WhenItOpensMarkedForItsOwnerAtItsCentreAtHeightOneWithoutAVoice()
    {
        var (db, world) = NewWorld();
        var sim = new GameSimulation(db, world, 1, [new DungeonHeartSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Tick();

        ThingSoundRequest[] heard = Sounds(events).OrderBy(s => s.ViewerOnlyFor!.Value.Value).ToArray();
        Assert.Equal(2, heard.Length);
        Assert.All(heard, s =>
        {
            Assert.Equal(ThingSoundEvent.HeartOpened, s.Event);
            Assert.Equal((ushort)0xF1, (ushort)s.Event);
            Assert.Equal(Heart, s.Category);
            Assert.False(s.Voiced);
        });
        Assert.Equal([new PlayerId(Keeper), new PlayerId(Rival)], heard.Select(s => s.ViewerOnlyFor!.Value));
        Assert.Equal(At(5, 5, 0x1000), heard[0].Position);
        Assert.Equal(At(12, 12, 0x1000), heard[1].Position);

        sim.Run(40);
        Assert.Equal(2, Sounds(events).Length);   // once, on the opening turn only
    }

    [Fact]
    public void ABlowRequests329WithTheRoomsVoiceAtTheHeartAndAKillingBlowAlsoRequestsDA()
    {
        var (db, world) = NewWorld();
        var events = new ListEventSink();
        bool killed = false;
        var sim = new GameSimulation(db, world, 1, [new Act(c =>
            killed = HeartSoundRules.Damage(c, "Act", Rival, 40, Keeper))]);
        sim.ObserveWith(events);
        sim.Tick();

        ThingSoundRequest hit = Assert.Single(Sounds(events));
        Assert.False(killed);
        Assert.Equal(ThingSoundEvent.HeartDamaged, hit.Event);
        Assert.Equal((ushort)0x329, (ushort)hit.Event);
        Assert.Equal(Heart, hit.Category);
        // The Heart's centre piece has floor 0 (0x004545C0's +0xC == 0x40 arm), where its object stands (Q795).
        Assert.Equal(At(12, 12, 0), hit.Position);
        Assert.True(hit.Voiced);
        Assert.Equal(SoundSourceKind.Room, hit.Source.Kind);
        Assert.Null(hit.ViewerOnlyFor);
        Assert.Equal(60, world.Player(Rival).HeartHealth);

        world.Player(Rival).HeartHealth = 30;
        sim.Tick();
        ThingSoundRequest[] heard = Sounds(events).Skip(1).ToArray();
        Assert.True(killed);
        Assert.Equal([ThingSoundEvent.HeartDamaged, ThingSoundEvent.HeartDestroyed], heard.Select(s => s.Event));
        Assert.Equal((ushort)0xDA, (ushort)heard[1].Event);
        Assert.Equal(Heart, heard[1].Category);
        Assert.Equal(hit.Source, heard[1].Source);   // both pass the room's one handle
        Assert.True(heard[1].Voiced);
    }

    [Fact]
    public void ADeadHeartAndAHealingMakeNoHeartSound()
    {
        var (db, world) = NewWorld();
        world.Player(Rival).HeartHealth = 0;
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new Act(c =>
        {
            HeartSoundRules.Damage(c, "Act", Rival, 40, Keeper);   // the room has no heart object left
            world.RegenerateHeart(Keeper, 10);
        })]);
        sim.ObserveWith(events);
        sim.Tick();
        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void TheDeathTurnOfAFallenKeepersDungeonRequestsF4AtTheHeartTileHeightZero_TheTileIsStronglyInferred()
    {
        var (db, world) = NewWorld();
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new Act(c => DefeatRules.OnDeathTurn(c, Rival))]);
        sim.ObserveWith(events);
        sim.Tick();

        ThingSoundRequest sound = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.HeartCollapsed, sound.Event);
        Assert.Equal((ushort)0xF4, (ushort)sound.Event);
        Assert.Equal(Heart, sound.Category);
        Assert.Equal(At(12, 12, 0), sound.Position);
        Assert.True(sound.Voiced);   // the player's own handle, +0x10A2, not the room's (+0x14) that 0x329 and 0xDA pass
        Assert.Equal(new SoundSourceId(SoundSourceKind.Player, Rival), sound.Source);
    }

    [Fact]
    public void ABlowOnAHeartWithNoTilesIsStillTakenButMakesNoHeartSound()
    {
        var (db, world) = NewWorld();
        for (int y = 10; y < 15; y++)
            for (int x = 10; x < 15; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new Act(c => HeartSoundRules.Damage(c, "Act", Rival, 40, Keeper))]);
        sim.ObserveWith(events);
        sim.Tick();

        Assert.Equal(60, world.Player(Rival).HeartHealth);
        Assert.Empty(Sounds(events));   // as the collapse is guarded by the heart tile: no tile, no position to sound at
    }

    /// <summary>Shows the sink does not mutate state, not that a sound could not.</summary>
    [Fact]
    public void AttachingASoundSinkDoesNotChangeTheStateHashOfAHeartBlowAndDefeatRun()
    {
        static GameSimulation Run(ListEventSink? sink)
        {
            var (db, world) = NewWorld();
            var sim = new GameSimulation(db, world, 1, [new DungeonHeartSystem(), new Act(c =>
            {
                if (c.Tick.Value == 40) HeartSoundRules.Damage(c, "Act", Rival, 1_000_000, Keeper);
                if (c.Tick.Value == 41) DefeatRules.OnDeathTurn(c, Rival);
            })]);
            if (sink is not null) sim.ObserveWith(sink);
            sim.Run(45);
            return sim;
        }
        var heard = new ListEventSink();
        GameSimulation with = Run(heard);
        Assert.Equal(5, Sounds(heard).Length);   // two openings, a blow, the death, the collapse
        Assert.Equal(Run(null).StateHash(), with.StateHash());
    }
}
