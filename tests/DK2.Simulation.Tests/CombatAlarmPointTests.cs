// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

public class CombatAlarmPointTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Fact]
    public void AnAlarmUsesTheDrawnPortalSlabInsteadOfItsBlockedCentre()
    {
        var (db, world, room, imp) = Fixture(Dk2TerrainFixture.PortalTerrain);
        var random = new FixedDraw(0);
        bool raised = Raise(db, world, imp, random);

        Assert.True(raised);
        Assert.Equal(new TilePos(10, 10), imp.Destination);
        Assert.Equal([room.TileCount], random.Bounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInteriorDrawAdvancesAlongTheSlabChainAndWrapsWithoutAnotherDraw(bool wrap)
    {
        var (db, world, room, imp) = Fixture(Dk2TerrainFixture.LairTerrain);
        int centre = 11 * world.Width + 11;
        if (wrap) world.TileAtIndex(centre).SlabOrder = 99;
        int[] chain = SlabChain.Order(world, room);
        int draw = Array.IndexOf(chain, centre);
        var random = new FixedDraw(draw);

        Assert.True(Raise(db, world, imp, random));

        int expected = chain[(draw + 1) % chain.Length];
        Assert.Equal(new TilePos(expected % world.Width, expected / world.Width), imp.Destination);
        Assert.Equal([room.TileCount], random.Bounds);
    }

    [Fact]
    public void ABlockedDrawDoesNotChooseAnotherPointOrAnotherRoom()
    {
        var (db, world, room, imp) = Fixture(Dk2TerrainFixture.PortalTerrain);
        PlaceRoom(world, Dk2TerrainFixture.LairTerrain, 15, 15);
        int[] chain = SlabChain.Order(world, room);
        var random = new FixedDraw(Array.IndexOf(chain, 11 * world.Width + 11));

        Assert.False(Raise(db, world, imp, random));
        Assert.False(imp.HasRoute);
        Assert.Equal([room.TileCount], random.Bounds);
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.PortalTerrain)]
    [InlineData(Dk2TerrainFixture.DungeonHeartTerrain)]
    [InlineData(Dk2TerrainFixture.WoodenBridgeTerrain)]
    public void SpecialRoomInteriorsAreNotFloorSlots(byte terrain)
    {
        var (_, world, room, _) = Fixture(terrain);
        Assert.False(AutoFurnitureRules.IsFloorSlot(world, room, 11 * world.Width + 11));
    }

    private static bool Raise(GameDatabase db, WorldState world, CreatureState imp, FixedDraw random)
    {
        bool raised = false;
        var simulation = new GameSimulation(db, world, new SimRandom(random),
            [new Probe(context => raised = FearRules.RaiseAlarm(context, new RoomCache(), imp,
                db.CreatureById(imp.CreatureTypeId), imp.PosX, imp.PosY, 50))]);
        simulation.Tick();
        return raised;
    }

    private static (GameDatabase Db, WorldState World, RoomInstance Room, CreatureState Imp) Fixture(byte terrain)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        PlaceRoom(world, terrain, 11, 11);
        RoomInstance room = RoomIndex.Build(world, db).Single();
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(8, 8));
        Dk2TerrainFixture.SetKnownIdentities(world);
        return (db, world, room, imp);
    }

    private static void PlaceRoom(WorldState world, byte terrain, int cx, int cy)
    {
        for (int y = cy - 1; y <= cy + 1; y++)
            for (int x = cx - 1; x <= cx + 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                tile.TerrainId = terrain;
                tile.OwnerPlayerId = Keeper;
            }
    }

    private sealed class FixedDraw(int value) : IRandomAlgorithm
    {
        public List<int> Bounds { get; } = [];
        public int NextInt(int exclusiveMax)
        {
            Bounds.Add(exclusiveMax);
            Assert.InRange(value, 0, exclusiveMax - 1);
            return value;
        }
        public void HashInto(ref Hash64 hash) { }
    }

    private sealed class Probe(Action<SimulationContext> body) : ISimulationSystem
    {
        public string Name => "alarm point probe";
        public void Execute(SimulationContext context) => body(context);
    }
}
