// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary><c>Room_Rebuild</c>'s Hatchery arm, <c>Hatchery_PlaceCoops</c> (<c>0x004DE220</c>): at the level's start
/// and on every change of the room's tiles the room's auto-placed coops go and are placed again in chain order, each
/// with one world draw of <c>random(4)</c> (ADR-0672).</summary>
public class HatcheryCoopRebuildTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    /// <summary>The fixture's claimed 3x3 at (1..3, 1..3), widened to (1..7, 1..3) for a room that can grow.</summary>
    private static (GameDatabase Db, WorldState World) Start()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 3; y++)
            for (int x = 4; x <= 7; x++)
                (world.Tile(x, y).TerrainId, world.Tile(x, y).OwnerPlayerId) = (Dk2TerrainFixture.ClaimedPath, Keeper);
        return (db, world);
    }

    private static void Paint(GameDatabase db, WorldState world, int x0, int x1)
    {
        for (int y = 1; y <= 3; y++)
            for (int x = x0; x <= x1; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Hatchery), Keeper);
    }

    private static void Change(GameDatabase db, WorldState world, int x, int y, byte terrain)
    {
        ref TileState tile = ref world.Tile(x, y);
        (byte before, PlayerId owner) = (tile.TerrainId, tile.OwnerPlayerId);
        if (terrain == Dk2TerrainFixture.HatcheryTerrain) RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Hatchery), Keeper);
        else tile.TerrainId = terrain;
        SlabChain.Changed(world, db, (y * world.Width) + x, before, owner);
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, uint seed) =>
        new(db, world, new SimRandom(new Dk2Lcg(seed)), []);

    private static uint Seed(GameSimulation sim) => ((Dk2Lcg)sim.Rng.Algorithm).Seed;

    private static uint After(uint seed, int draws)
    {
        var lcg = new Dk2Lcg(seed);
        for (int i = 0; i < draws; i++) lcg.NextInt(4);
        return lcg.Seed;
    }

    private static List<ObjectState> Coops(WorldState world) =>
        [.. world.Objects.Where(o => o.ObjectTypeId == HatcheryRules.CoopObjectId)];

    [Fact]
    public void TheLevelStartPlacesTheCoopsAndDrawsOnceForEach()
    {
        var (db, world) = Start();
        Paint(db, world, 1, 7); // 21 tiles, interior (2..6, 2): coops at x 2, 4 and 6
        GameSimulation sim = Simulation(db, world, 9);
        Assert.Equal([2, 4, 6], Coops(world).Select(c => c.TileIndex % world.Width).Order());
        Assert.All(Coops(world), c => Assert.True(c.AutoPlaced));
        Assert.Equal(After(9, 3), Seed(sim));
    }

    [Fact]
    public void GrowingTheRoomReplacesEveryCoopAndDrawsOnceForEach()
    {
        var (db, world) = Start();
        Paint(db, world, 1, 6);
        GameSimulation sim = Simulation(db, world, 9);
        int[] before = [.. Coops(world).Select(c => c.Id)];
        Assert.NotEmpty(before);
        uint seed = Seed(sim);

        Change(db, world, 7, 2, Dk2TerrainFixture.HatcheryTerrain);
        List<ObjectState> after = Coops(world);
        Assert.NotEmpty(after);
        Assert.DoesNotContain(after, c => before.Contains(c.Id));
        Assert.Equal(After(seed, after.Count), Seed(sim));
    }

    [Fact]
    public void ACutRemovesTheCoopsAndEachPiecePlacesItsOwn()
    {
        var (db, world) = Start();
        Paint(db, world, 1, 7); // 21 tiles: three coops
        GameSimulation sim = Simulation(db, world, 9);
        Assert.Equal(3, Coops(world).Count);
        uint seed = Seed(sim);

        // Selling the middle column leaves two 3x3 pieces, one coop each.
        for (int y = 1; y <= 3; y++) Change(db, world, 4, y, Dk2TerrainFixture.ClaimedPath);
        List<ObjectState> after = Coops(world);
        Assert.Equal(2, after.Count);
        Assert.DoesNotContain(after, c => c.TileIndex % world.Width == 4);
        // Each of the three sales rebuilds the pieces it leaves, one draw per coop placed.
        Assert.NotEqual(seed, Seed(sim));
    }

    [Fact]
    public void ACoopTheRoomDidNotPlaceStaysAndItsTileIsSkipped()
    {
        var (db, world) = Start();
        Paint(db, world, 1, 3);
        ObjectState placed = world.SpawnObject(db, HatcheryRules.CoopObjectId, Keeper, (2 * world.Width) + 2);
        GameSimulation sim = Simulation(db, world, 9);
        Assert.Contains(placed, world.Objects);
        Assert.False(placed.AutoPlaced);
        Assert.DoesNotContain(Coops(world), c => c.AutoPlaced && c.TileIndex == placed.TileIndex);
        // The 3x3's one interior tile holds it, so the rebuild places none and draws nothing.
        Assert.Equal(9u, Seed(sim));
    }

    /// <summary>Work that changes the terrain of a tile beside the room refreshes it (<c>0x004ECDB0</c>): a dug
    /// wall twice, once in the applier and once at the dig strike's tail (<c>0x0049716B</c>); a claimed floor
    /// once. The Hatchery's three coops are placed again each time.</summary>
    [Theory]
    [InlineData(TileWorkKind.Dig, 2)]
    [InlineData(TileWorkKind.Claim, 1)]
    public void WorkBesideTheRoomPlacesItsCoopsAgain(TileWorkKind kind, int rebuilds)
    {
        var (db, world) = Start();
        Paint(db, world, 1, 7);
        ref TileState beside = ref world.Tile(4, 4);
        (beside.TerrainId, beside.OwnerPlayerId) = (kind == TileWorkKind.Dig ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.DirtPath, PlayerIds.Unowned);
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(9)), [new TileWorkSystem()]);
        int[] before = [.. Coops(world).Select(c => c.Id)];
        uint seed = Seed(sim);

        world.QueueWork(new TileWorkOrder((4 * world.Width) + 4, kind, Keeper, Multiplier: 1000));
        sim.Tick();
        Assert.NotEqual(Dk2TerrainFixture.Rock, world.Tile(4, 4).TerrainId);
        Assert.Equal(3, Coops(world).Count);
        Assert.DoesNotContain(Coops(world), c => before.Contains(c.Id));
        Assert.Equal(After(seed, 3 * rebuilds), Seed(sim));
    }
}
