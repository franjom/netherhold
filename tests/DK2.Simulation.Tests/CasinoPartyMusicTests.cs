// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Casino party's music (<c>Casino_RoomUpdate</c> <c>0x004D6270</c>): for each creature of the room in state 153 the update asks
/// <c>0x316</c> with the room's handle in the category of the terrain at the room's anchor tile (<c>0x004D63D0</c>); once the party is
/// off and the music was on, the update stops the room's handle and asks <c>0x33E</c> with it (<c>0x004D66BA</c>, <c>0x004D6727</c>), which
/// is what ends the music. The anchor tile is read as the room's first tile (STRONGLY INFERRED), and the explored-tile test that
/// scales the volume or gates the end sound is the viewer's and is not asked here. Docs: sound-effects-continuous.md section 3.</summary>
public sealed class CasinoPartyMusicTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = Dk2TerrainFixture.GoblinCreatureId;

    private static GameDatabase Db()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        byte casino = basis.Room(RoomTypeId.Casino).TerrainId;
        return new GameDatabase(basis.Terrain.Values.Select(t => t.TerrainId == casino ? t with { SoundCategory = "ROOM_CASINO" } : t),
            basis.Rooms.Values, basis.Creatures.Values, basis.Variables, objects: basis.Objects.Values, shots: basis.Shots.Values);
    }

    private static (WorldState World, RoomInstance Room, CreatureState Dancer) Setup(GameDatabase db, bool dancer = true)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Casino), Keeper);
        RoomInstance room = RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == RoomTypeId.Casino);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1));
        goblin.CurrentJob = JobType.Drink;
        StatePlacement.As(goblin, CreatureActivity.AtWork);
        StatePlacement.Place(goblin, dancer ? CasinoStep.DancingState : CasinoStep.ChoosingState);
        goblin.HeldPlace = new RoomPlace((1 * world.Width) + 1, RoomCapacityRules.Units);
        goblin.ActionUntilTick = long.MaxValue;
        ref TileState first = ref world.TileAtIndex(room.TileIndices[0]);
        first.CasinoParty = CasinoJackpotRules.PartyBit;
        first.CasinoPartyStartTick = 0;
        world.Player(Keeper).CasinoParties = 1;
        return (world, room, goblin);
    }

    private static (GameSimulation Sim, ListEventSink Events) Simulate(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, seed: 1, [new CasinoSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, events);
    }

    private static ThingSoundRequest[] Heard(ListEventSink events, int from = 0) =>
        events.Events.Skip(from).Where(e => e.ThingSound is { Source.Kind: SoundSourceKind.Room }).Select(e => e.ThingSound!.Value).ToArray();

    [Fact]
    public void AnOngoingPartyWithADancerAsks316EveryUpdateWithTheRoomsHandleAtItsAnchorTile()
    {
        GameDatabase db = Db();
        (WorldState world, RoomInstance room, _) = Setup(db);
        var (sim, events) = Simulate(db, world);

        sim.Run(5);

        ThingSoundRequest[] heard = Heard(events);
        Assert.Equal(5, heard.Length);
        int tile = room.TileIndices[0];
        Assert.All(heard, s =>
        {
            Assert.Equal(ThingSoundEvent.PartyMusic, s.Event);
            Assert.Equal("ROOM_CASINO", s.Category);
            Assert.Equal(new SoundSourceId(SoundSourceKind.Room, tile), s.Source);
            Assert.Equal(new FpPosition3(new Fp32(CreatureState.CentreOf(tile % world.Width)), new Fp32(CreatureState.CentreOf(tile / world.Width)), new Fp32(0)), s.Position);
            Assert.True(s.Voiced);
        });
    }

    [Fact]
    public void APartyWithNoDancerHasNoMusic()
    {
        GameDatabase db = Db();
        (WorldState world, _, _) = Setup(db, dancer: false);
        var (sim, events) = Simulate(db, world);

        sim.Run(5);

        Assert.Empty(Heard(events));
    }

    [Fact]
    public void ThePartyEndingStopsTheRoomsHandleThenAsks33EWithIt()
    {
        GameDatabase db = Db();
        (WorldState world, RoomInstance room, CreatureState dancer) = Setup(db);
        var (sim, events) = Simulate(db, world);
        sim.Run(2);   // the music is on
        int before = events.Events.Count;
        ref TileState first = ref world.TileAtIndex(room.TileIndices[0]);
        first.CasinoParty &= unchecked((byte)~CasinoJackpotRules.PartyBit);   // the party is over; the music bit remains
        world.Player(Keeper).CasinoParties = 0;

        sim.Run(2);

        ThingSoundRequest[] heard = Heard(events, before);
        Assert.Equal(2, heard.Length);
        Assert.True(heard[0].Stop);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Room, room.TileIndices[0]), heard[0].Source);
        Assert.Equal(ThingSoundEvent.PartyEnded, heard[1].Event);
        Assert.Equal("ROOM_CASINO", heard[1].Category);
        Assert.Equal(heard[0].Source, heard[1].Source);
        Assert.Equal(CasinoStep.ChoosingState, dancer.Dk2StateId);
    }
}
