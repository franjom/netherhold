// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>The Workshop's three sounds (<c>Workshop_Tick</c> <c>0x004F4B20</c>): <c>0x2E9</c> after the steam effect (<c>0x004F4C27</c>) and the lava
/// spill's (<c>0x004F4DC7</c>), <c>0x2E8</c> after the smoke's (<c>0x004F4F03</c>), each with the room's handle, in the category of the terrain at the effect's
/// position and at that position; the lava bubbling's effect asks none. docs/research/sound-effects.md section 9.</summary>
public sealed class WorkshopSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const ushort Steam = 50, Smoke = 51, LavaBubbling = 93, AnvilSparks = 42, BucketSpill = 69;
    private const byte Anvil = 15, Bucket = 16;

    private static GameDatabase Db(ushort steam = Steam)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        TerrainDef rock = basis.TerrainById(Dk2TerrainFixture.Rock);
        byte workshopTerrain = basis.Room(RoomTypeId.Workshop).TerrainId;
        return new GameDatabase(
            basis.Terrain.Values.Select(t => t.TerrainId == rock.TerrainId ? rock with { Flags = rock.Flags | TerrainFlags.AllowRoomWalls }
                : t.TerrainId == workshopTerrain ? t with { SoundCategory = "ROOM_WORKSHOP" } : t),
            basis.Rooms.Values.Select(r => r.TypeId == RoomTypeId.Workshop
                ? r with { RoomEffect425Id = steam, RoomEffect427Id = Smoke, RoomEffect429Id = LavaBubbling } : r),
            basis.Creatures.Values, basis.Variables, basis.KeeperSpells.Values,
            objects: basis.Objects.Values.Select(o => o.ObjectId switch
            {
                Anvil => o with { IdleEffectId = AnvilSparks },
                Bucket => o with { IdleEffectId = BucketSpill },
                _ => o,
            }),
            shots: basis.Shots.Values);
    }

    private static (GameSimulation Sim, WorldState World, RoomInstance Room, ListEventSink Events) Setup(ushort steam = Steam)
    {
        GameDatabase db = Db(steam);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(24, 24), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 3; y < 8; y++)
            for (int x = 3; x < 8; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Workshop), Keeper);
        Dk2TerrainFixture.FurnishRooms(db, world);
        RoomInstance room = RoomIndex.RoomAt(world, db, (3 * world.Width) + 3)!;
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(1)), [new WorkshopTimerSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, world, room, events);
    }

    private static ViewEffectRequest[] Effects(ListEventSink events, ushort id) =>
        [.. events.Events.Where(e => e.EffectRequest?.EffectId == new EffectId(id)).Select(e => e.EffectRequest!.Value)];

    /// <summary>The lava-spill timer plays the idle effect of whichever furniture its pick walks to: the Steel Bucket's or the Anvil's.</summary>
    private static ViewEffectRequest[] Spills(ListEventSink events) => [.. Effects(events, BucketSpill), .. Effects(events, AnvilSparks)];

    private static ThingSoundRequest[] Heard(ListEventSink events, int word) =>
        [.. Sounds(events).Where(s => (int)s.Event == word && s.Source.Kind == SoundSourceKind.Room)];

    [Fact]
    public void TheSteamEffectIsFollowedBy2e9WithTheRoomsHandleInTheTerrainsCategoryAtTheEffectsPosition()
    {
        (GameSimulation sim, _, RoomInstance room, ListEventSink events) = Setup();

        sim.Tick();

        ViewEffectRequest steam = Assert.Single(Effects(events, Steam));
        Assert.Contains(Heard(events, 0x2E9), s => s.Position == steam.Position);
        ThingSoundRequest sound = Heard(events, 0x2E9).First(s => s.Position == steam.Position);
        Assert.Equal("ROOM_WORKSHOP", sound.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Room, room.TileIndices[0]), sound.Source);
        Assert.True(sound.Voiced);
    }

    [Fact]
    public void TheSmokeEffectIsFollowedBy2e8AtItsOwnPosition()
    {
        (GameSimulation sim, _, RoomInstance room, ListEventSink events) = Setup();

        sim.Tick();

        ViewEffectRequest smoke = Assert.Single(Effects(events, Smoke));
        ThingSoundRequest sound = Assert.Single(Heard(events, 0x2E8));
        Assert.Equal(smoke.Position, sound.Position);
        Assert.Equal("ROOM_WORKSHOP", sound.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Room, room.TileIndices[0]), sound.Source);
    }

    [Fact]
    public void EveryWorkshopSoundFollowsOneOfItsThreeEffectsAndTheBubblingEffectAsksNone()
    {
        (GameSimulation sim, _, _, ListEventSink events) = Setup();

        sim.Run(200);

        int steam = Effects(events, Steam).Length, spill = Spills(events).Length, smoke = Effects(events, Smoke).Length;
        Assert.True(steam > 1 && smoke > 1 && Effects(events, LavaBubbling).Length > 1);
        Assert.Equal(steam + spill, Heard(events, 0x2E9).Length);
        Assert.Equal(smoke, Heard(events, 0x2E8).Length);
        Assert.All(Spills(events), spillEffect => Assert.Contains(Heard(events, 0x2E9), s => s.Position == spillEffect.Position));
    }

    [Fact]
    public void ARoomWithoutTheSteamEffectAsksNoSteamSound()
    {
        (GameSimulation sim, _, _, ListEventSink events) = Setup(steam: 0);

        sim.Run(200);

        Assert.Empty(Effects(events, Steam));
        Assert.Equal(Spills(events).Length, Heard(events, 0x2E9).Length);
    }
}
