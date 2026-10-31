// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

public class CombatAlarmPointTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    // 0x004752B0 walks only rooms of type 9 (0x00475336): with no Guard Room nothing is raised
    // and no point is drawn, whatever other rooms the owner has.
    [Theory]
    [InlineData(Dk2TerrainFixture.PortalTerrain)]
    [InlineData(Dk2TerrainFixture.LairTerrain)]
    public void OnlyAGuardRoomReceivesTheAlarm(byte terrain)
    {
        var (db, world, _, imp) = Fixture(terrain);
        var random = new FixedDraw(0);

        Assert.False(Raise(db, world, imp, random));
        Assert.False(imp.HasRoute);
        Assert.Empty(random.Bounds);
    }

    [Fact]
    public void ANearerPortalIsSkippedForTheGuardRoom()
    {
        var (db, world, _, imp) = Fixture(Dk2TerrainFixture.PortalTerrain);
        PlaceRoom(world, Dk2TerrainFixture.GuardRoomTerrain, 15, 15);
        var random = new FixedDraw(0);

        Assert.True(Raise(db, world, imp, random));
        Assert.InRange(imp.Destination.X, 14, 16);
        Assert.InRange(imp.Destination.Y, 14, 16);
        Assert.Equal([9], random.Bounds);
    }

    // A failed route ends the alarm: no second draw and no other Guard Room (0x004752B0 routes once).
    [Fact]
    public void ABlockedRouteDoesNotChooseAnotherPointOrAnotherRoom()
    {
        var (db, world, room, imp) = Fixture(Dk2TerrainFixture.GuardRoomTerrain);
        PlaceRoom(world, Dk2TerrainFixture.GuardRoomTerrain, 15, 15);
        for (int y = 7; y <= 9; y++)
            for (int x = 7; x <= 9; x++)
                if (x != 8 || y != 8) world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
        var random = new FixedDraw(0);

        Assert.False(Raise(db, world, imp, random));
        Assert.False(imp.HasRoute);
        Assert.Single(random.Bounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInteriorDrawAdvancesAlongTheSlabChainAndWrapsWithoutAnotherDraw(bool wrap)
    {
        var (db, world, room, imp) = Fixture(Dk2TerrainFixture.GuardRoomTerrain);
        int centre = 11 * world.Width + 11;
        if (wrap) world.TileAtIndex(centre).SlabOrder = 99;
        int[] chain = SlabChain.Order(world, room);
        int draw = Array.IndexOf(chain, centre);
        var random = new FixedDraw(draw);

        Assert.True(Raise(db, world, imp, random));

        Assert.True(AutoFurnitureRules.IsFloorSlot(world, room, centre));
        int expected = chain[(draw + 1) % chain.Length];
        Assert.Equal(new TilePos(expected % world.Width, expected / world.Width), imp.Destination);
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

    /// <summary><c>0x004754D0</c> counts a creature of the owner whose state, or a state-6 walk's arrival
    /// (<c>+0x32D</c>), is 251 or 254, within five tiles. A creature standing down in 252 after raising
    /// one does not count, nor a walk to 252 or a state-7 walk (ADR-0606).</summary>
    [Theory]
    [InlineData(251, 0, true)]
    [InlineData(254, 0, true)]
    [InlineData(251, 6, true)]
    [InlineData(254, 6, true)]
    [InlineData(252, 0, false)]
    [InlineData(252, 6, false)]
    [InlineData(202, 6, false)]
    [InlineData(251, 7, false)]
    public void OnlyARaiserOrReporterOnItsWayOrThereRefusesAnotherAlarm(int state, int walk, bool refuses)
    {
        var (db, world, _, imp) = Fixture(Dk2TerrainFixture.GuardRoomTerrain);
        CreatureState other = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(9, 8));
        other.AlarmX = imp.PosX;
        other.AlarmY = imp.PosY;
        if (walk != 0)
        {
            StatePlacement.Place(other, walk);
            other.UtilityNewState = state;
        }
        else StatePlacement.Place(other, state);

        Assert.Equal(refuses, FearRules.AlreadyResponding(world, Keeper, imp.PosX, imp.PosY));
    }

    /// <summary>The point each is judged by is <c>0x004755C0</c>'s: a raiser standing in 251 by the
    /// threat it raised (state <c>+0xA7/+0xAB</c>), wherever it stands; anyone else, a walker included,
    /// by its own position and not its route's end (ADR-0606).</summary>
    [Fact]
    public void ARaiserIsJudgedByItsThreatAndAWalkerByWhereItIs()
    {
        var (db, world, _, imp) = Fixture(Dk2TerrainFixture.GuardRoomTerrain);
        CreatureState raiser = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(17, 17));
        StatePlacement.Place(raiser, 251);
        raiser.AlarmX = imp.PosX;
        raiser.AlarmY = imp.PosY;
        Assert.True(FearRules.AlreadyResponding(world, Keeper, imp.PosX, imp.PosY));

        raiser.AlarmX = CreatureState.CentreOf(17);
        raiser.AlarmY = CreatureState.CentreOf(17);
        Assert.False(FearRules.AlreadyResponding(world, Keeper, imp.PosX, imp.PosY));

        StatePlacement.Place(raiser, 6);
        raiser.UtilityNewState = 254;
        Assert.True(new WorldSteering(db, world).TryRouteTo(raiser, imp.PosX, imp.PosY, MoveOptions.Plain));
        Assert.True(raiser.HasRoute);
        Assert.False(FearRules.AlreadyResponding(world, Keeper, imp.PosX, imp.PosY));
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
