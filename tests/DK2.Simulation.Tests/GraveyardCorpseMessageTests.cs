// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>When DK2 asks for a Graveyard (Q544): the corpse selections (<c>0x0046D3C0</c>, the
/// search, and <c>0x0046CF40</c>) post 41 with no Graveyard and 42 with none that has a place, and
/// carry nothing then. The fixture map opens (1..3, 1..3); the imp starts at (2,3) beside a corpse
/// at (3,3).</summary>
public class GraveyardCorpseMessageTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = 7;
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static WorldState World(int availability = RoomNeedRules.Available)
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        List<AvailabilityState> rows = world.Player(Keeper).Availabilities;
        rows.RemoveAll(a => a.Kind == AvailabilityKind.Room && a.TypeId == (int)RoomTypeId.Graveyard);
        rows.Add(new AvailabilityState(AvailabilityKind.Room, (int)RoomTypeId.Graveyard, availability));
        return world;
    }

    private static CreatureState Corpse(WorldState world, TilePos at)
    {
        CreatureState goblin = world.SpawnCreature(Db, GoblinId, Keeper, at);
        CreatureDeathRules.BecomeCorpse(world, Db, goblin);
        return goblin;
    }

    private static CreatureState RunImp(WorldState world, int ticks = 2)
    {
        CreatureState imp = world.SpawnCreature(Db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2, 3));
        new GameSimulation(Db, world, 42, [new ImpTaskSystem()]).Run(ticks);
        return imp;
    }

    private static PlayerMessageKind[] Posted(WorldState world) =>
        [.. world.Messages.Entries.Where(m => m.Room == RoomTypeId.Graveyard).Select(m => m.Kind)];

    [Fact]
    public void NoGraveyardAtAvailabilityThreeAsksForOneOnceAndLeavesTheCorpse()
    {
        WorldState world = World();
        Corpse(world, new TilePos(3, 3));
        CreatureState imp = RunImp(world);
        Assert.Equal([PlayerMessageKind.RoomNeeded], Posted(world));
        Assert.Equal(-1, imp.TargetBodyId);
        Assert.True(world.Player(Keeper).GraveyardWantedPosted);
    }

    [Fact]
    public void NoGraveyardBelowAvailabilityThreeAsksNothing()
    {
        WorldState world = World(availability: 0);
        Corpse(world, new TilePos(3, 3));
        RunImp(world);
        Assert.Empty(Posted(world));
    }

    [Fact]
    public void AGraveyardWithNoFloorSlotPosts42AndTakesNoCorpse()
    {
        // One tile has no interior, so no floor slot (Room_IsFloorSlot) and no place for a corpse.
        WorldState world = World();
        RoomRules.Place(Db, ref world.Tile(1, 1), Db.Room(RoomTypeId.Graveyard), Keeper);
        Corpse(world, new TilePos(3, 3));
        CreatureState imp = RunImp(world);
        Assert.Equal([PlayerMessageKind.RoomFull], Posted(world));
        Assert.Equal(-1, imp.TargetBodyId);
    }

    [Fact]
    public void ACorpseOnATileTheKeeperDoesNotOwnIsNeitherReportedNorTaken()
    {
        WorldState world = World();
        world.Tile(3, 3).OwnerPlayerId = PlayerIds.Neutral;
        Corpse(world, new TilePos(3, 3));
        CreatureState imp = RunImp(world);
        Assert.Empty(Posted(world));
        Assert.Equal(-1, imp.TargetBodyId);
    }
}
