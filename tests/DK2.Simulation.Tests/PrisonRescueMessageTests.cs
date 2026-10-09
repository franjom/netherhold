// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The rescue selector's room messages (<c>0x0046D810</c>) and the Prison's
/// <c>Activation</c> (<c>+0x59</c>), which its routed search needs (Q542). The fixture map opens
/// (1..3, 1..3) in rock; the imp starts at (2,3) beside a fallen enemy at (3,3).</summary>
public class PrisonRescueMessageTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 1;
    private const byte GoblinId = 7;
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static WorldState World() =>
        WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]);

    private static void Availability(WorldState world, RoomTypeId room, int value)
    {
        List<AvailabilityState> rows = world.Player(Keeper).Availabilities;
        rows.RemoveAll(a => a.Kind == AvailabilityKind.Room && a.TypeId == (int)room);
        rows.Add(new AvailabilityState(AvailabilityKind.Room, (int)room, value));
    }

    private static void Floor(WorldState world, int x, int y)
    {
        world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(x, y).OwnerPlayerId = Keeper;
    }

    /// <summary>A Prison at (x0..x1, y0..y1), laid tile by tile through the slab chain as a build is.</summary>
    private static void Prison(WorldState world, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) Build(world, x, y, RoomTypeId.Prison);
    }

    private static void Build(WorldState world, int x, int y, RoomTypeId type)
    {
        ref TileState tile = ref world.Tile(x, y);
        byte before = tile.TerrainId;
        PlayerId owner = tile.OwnerPlayerId;
        RoomRules.Place(Db, ref tile, Db.Room(type), Keeper);
        SlabChain.Changed(world, Db, world.TileIndex(new TilePos(x, y)), before, owner);
    }

    private static RoomInstance PrisonAt(WorldState world, int x, int y) =>
        RoomIndex.Build(world, Db).Single(r => r.Definition.TypeId == RoomTypeId.Prison && r.Contains(world.TileIndex(new TilePos(x, y))));

    private static CreatureState Fallen(WorldState world, byte owner)
    {
        CreatureState body = world.SpawnCreature(Db, GoblinId, owner, new TilePos(3, 3));
        body.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, Db), body));
        return body;
    }

    private static CreatureState RunImp(WorldState world)
    {
        CreatureState imp = world.SpawnCreature(Db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 3));
        var sim = new GameSimulation(Db, world, 42, [new ImpTaskSystem()]);
        sim.Tick(InputFrame.Empty); // 1 hands the imp to 10, which chooses on the next tick
        sim.Tick(InputFrame.Empty);
        return imp;
    }

    private static PlayerMessageKind[] Posted(WorldState world, RoomTypeId room) =>
        [.. world.Messages.Entries.Where(m => m.Room == room).Select(m => m.Kind)];

    // ---- Activation ---------------------------------------------------------------------------

    [Fact]
    public void APrisonTurnsActivationOnAtItsFirstTileAndKeepsItsOwnByteAsItGrows()
    {
        WorldState world = World();
        Floor(world, 5, 2);
        Floor(world, 4, 2);
        Build(world, 5, 2, RoomTypeId.Prison);
        Assert.True(PrisonRules.IsActivated(world, PrisonAt(world, 5, 2)));

        // Turned off, the room keeps it off when a lower tile joins: a two-tile room is not slot
        // +0x09's one-tile case, and the joined room's byte moves to the new lowest tile.
        JailBreakRules.DoorOf(world, PrisonAt(world, 5, 2)) = 0;
        Build(world, 4, 2, RoomTypeId.Prison);
        Assert.False(PrisonRules.IsActivated(world, PrisonAt(world, 4, 2)));
    }

    [Fact]
    public void ASalePiecesTakeTheOldRoomsByteAndAOneTilePieceTurnsActivationOn()
    {
        WorldState world = World();
        for (int x = 4; x <= 6; x++) Floor(world, x, 2);
        for (int x = 4; x <= 6; x++) Build(world, x, 2, RoomTypeId.Prison);
        JailBreakRules.DoorOf(world, PrisonAt(world, 4, 2)) = JailBreakRules.Open;

        // Sold from the middle: two one-tile pieces, each the old byte and Activation.
        ref TileState middle = ref world.Tile(5, 2);
        byte before = middle.TerrainId;
        middle.TerrainId = Dk2TerrainFixture.ClaimedPath;
        SlabChain.Changed(world, Db, world.TileIndex(new TilePos(5, 2)), before, middle.OwnerPlayerId);

        Assert.Equal(JailBreakRules.Open | JailBreakRules.Activated, JailBreakRules.DoorOf(world, PrisonAt(world, 4, 2)));
        Assert.Equal(JailBreakRules.Open | JailBreakRules.Activated, JailBreakRules.DoorOf(world, PrisonAt(world, 6, 2)));
        Assert.Equal(0, world.Tile(5, 2).PrisonDoor);
    }

    [Fact]
    public void AMapPrisonIsLoadedWithActivationOn()
    {
        LevelMap fixture = Dk2TerrainFixture.Map();
        var tiles = new MapTile[fixture.Width * fixture.Height];
        for (int y = 0; y < fixture.Height; y++)
            for (int x = 0; x < fixture.Width; x++)
                tiles[(y * fixture.Width) + x] = x is >= 4 and <= 6 && y is >= 4 and <= 6
                    ? new MapTile(Db.Room(RoomTypeId.Prison).TerrainId, Keeper, 0, 0)
                    : fixture[x, y];

        WorldState world = WorldState.FromMap(Db, new LevelMap(fixture.Width, fixture.Height, tiles),
            [(byte)0, Rival, PlayerIds.Neutral, Keeper]);
        Assert.Equal(JailBreakRules.Activated, JailBreakRules.DoorOf(world, PrisonAt(world, 5, 5)));
    }

    [Fact]
    public void APrisonWithActivationOffTakesNoPrisoner()
    {
        WorldState world = World();
        Floor(world, 4, 3);
        Prison(world, 4, 4, 6, 6);
        Assert.NotEqual(-1, PrisonRules.FreeTileIn(world, Db, Keeper, new TilePos(2, 3)));

        JailBreakRules.DoorOf(world, PrisonAt(world, 5, 5)) = 0;
        Assert.Equal(-1, PrisonRules.FreeTileIn(world, Db, Keeper, new TilePos(2, 3)));
    }

    // ---- the selector's messages ----------------------------------------------------------

    [Fact]
    public void NoPrisonAtAvailabilityThreePosts41AndTheBodyStays()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Prison, RoomNeedRules.Available);
        Fallen(world, Rival);
        CreatureState imp = RunImp(world);
        Assert.Equal([PlayerMessageKind.RoomNeeded], Posted(world, RoomTypeId.Prison));
        Assert.Equal(-1, imp.TargetBodyId);
    }

    [Fact]
    public void NoPrisonBelowAvailabilityThreePostsNothing()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Prison, 0);
        Fallen(world, Rival);
        RunImp(world);
        Assert.Empty(Posted(world, RoomTypeId.Prison));
    }

    [Fact]
    public void APrisonWithNoPlacePosts42()
    {
        // A 2×2 has no fenced tile, so it holds nobody (PrisonRules.Capacity).
        WorldState world = World();
        Floor(world, 4, 3);
        Prison(world, 4, 4, 5, 5);
        Fallen(world, Rival);
        RunImp(world);
        Assert.Equal([PlayerMessageKind.RoomFull], Posted(world, RoomTypeId.Prison));
    }

    [Theory]
    [InlineData(false, true)]  // sealed in rock: no route reaches it
    [InlineData(true, false)]  // reachable, its door's Activation off
    public void APrisonWithAPlaceThatTheRoutedSearchRejectsPosts43(bool wayIn, bool activated)
    {
        WorldState world = World();
        if (wayIn) Floor(world, 4, 3);
        Prison(world, 4, 4, 6, 6);
        if (!activated) JailBreakRules.DoorOf(world, PrisonAt(world, 5, 5)) = 0;
        Fallen(world, Rival);
        CreatureState imp = RunImp(world);
        Assert.Equal([PlayerMessageKind.RoomUnreachable], Posted(world, RoomTypeId.Prison));
        Assert.Equal(-1, imp.TargetBodyId);
    }

    [Fact]
    public void AReachablePrisonWithAPlacePostsNothingAndTakesTheBody()
    {
        WorldState world = World();
        Floor(world, 4, 3);
        Prison(world, 4, 4, 6, 6);
        CreatureState body = Fallen(world, Rival);
        CreatureState imp = RunImp(world);
        Assert.Empty(Posted(world, RoomTypeId.Prison));
        Assert.Equal(body.Id, imp.TargetBodyId);
    }

    [Fact]
    public void ABodyOnATileTheKeeperDoesNotOwnIsNeitherReportedNorTaken()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Prison, RoomNeedRules.Available);
        world.Tile(3, 3).OwnerPlayerId = PlayerIds.Neutral;
        Fallen(world, Rival);
        CreatureState imp = RunImp(world);
        Assert.Empty(Posted(world, RoomTypeId.Prison));
        Assert.Equal(-1, imp.TargetBodyId);
    }

    [Fact]
    public void AnOwnCreatureWithNoBedAndAFullLairPosts42ForALair()
    {
        WorldState world = World();
        Build(world, 1, 1, RoomTypeId.Lair);
        CreatureState sleeper = world.SpawnCreature(Db, GoblinId, Keeper, new TilePos(1, 1));
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, Db, sleeper));
        Fallen(world, Keeper);
        RunImp(world);
        Assert.Equal([PlayerMessageKind.RoomFull], Posted(world, RoomTypeId.Lair));
    }

    [Fact]
    public void AnOwnCreatureWithNoBedAndOnlyASealedLairPosts43ForALair()
    {
        WorldState world = World();
        Build(world, 6, 6, RoomTypeId.Lair);
        Fallen(world, Keeper);
        RunImp(world);
        Assert.Equal([PlayerMessageKind.RoomUnreachable], Posted(world, RoomTypeId.Lair));
    }

    [Fact]
    public void ABedlessOwnCreatureIsLeftBesideALairWithAPlace_WhereDk2WouldCarryItThere()
    {
        // Result 4 for the Lair: DK2 raises the carry (0x0046D810's own-creature loop); ours has no
        // bed to take it to and leaves it. A known deviation (ADR-0412), pinned so it is not mistaken
        // for DK2's.
        WorldState world = World();
        Build(world, 1, 1, RoomTypeId.Lair);
        Fallen(world, Keeper);
        CreatureState imp = RunImp(world);
        Assert.Empty(Posted(world, RoomTypeId.Lair));
        Assert.Equal(-1, imp.TargetBodyId);
    }

    [Fact]
    public void AnOwnCreatureWithNoBedAndNoLairPosts41ForALair()
    {
        WorldState world = World();
        Availability(world, RoomTypeId.Lair, RoomNeedRules.Available);
        Fallen(world, Keeper);
        CreatureState imp = RunImp(world);
        Assert.Equal([PlayerMessageKind.RoomNeeded], Posted(world, RoomTypeId.Lair));
        Assert.Equal(-1, imp.TargetBodyId);
    }
}
