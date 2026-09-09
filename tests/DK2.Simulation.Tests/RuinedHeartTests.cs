// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>A defeated keeper's dungeon heart (Q207): the rule lives nowhere
/// special. CONFIRMED (owner, 2026-09-07, Q93): "imp can then claim the enemy
/// 'dungeon heart' room which has turned to ruin, but it can't be sold and have
/// something else built in its place". All three clauses already hold, each
/// falling out of a general rule written for another reason — the note claiming
/// <c>RoomSellSystem</c> and <c>RoomBuildSystem</c> would allow the last two was
/// reasoning, not measurement. A claim nothing checks may already be true as
/// easily as already false, and both cost the same to find out.</summary>
public sealed class RuinedHeartTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    private static void Give(
        GameDatabase db, WorldState world, RoomTypeId type, byte owner, TilePos at)
    {
        ref TileState tile = ref world.Tile(at);
        RoomRules.Place(db, ref tile, db.Room(type), owner);
    }

    [Fact]
    public void ThereIsNoRuinTerrainAndNoRuinRoomInTheShippedTables()
    {
        // Which is what makes the question's own premise wrong. Q207 asked where
        // the ban lives on the assumption that a ruin is a THING; it is not. A
        // defeated heart stays a Dungeon Heart room, and every prohibition the
        // owner described follows from that one fact.
        GameDatabase db = Db;

        Assert.DoesNotContain(db.Rooms.Values, r =>
            r.Name.Contains("Ruin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(db.Terrain.Values, t =>
            t.Name.Contains("Ruin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ARivalsHeartIsREFUSEDToAnImpToday_AndThatIsRightForALiveOneOnly()
    {
        // Two rules keep an imp off a rival's heart and only one is about hearts:
        // Claim refuses all room terrain, a room being converted whole rather than
        // taken tile by tile (Q92); ConvertRoom refuses the dungeon heart
        // specifically, by terrain identity. That second exclusion is
        // unconditional and Q93 says it should not be — a heart that has FALLEN
        // can be claimed. Not implemented: defeat, so nothing to condition on.
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(2, 2);
        Give(db, world, RoomTypeId.DungeonHeart, Rival, at);

        Assert.False(TileWorkRules.Applies(
            db, world.Tile(at), TileWorkKind.Claim, Keeper));
        Assert.False(TileWorkRules.Applies(
            db, world.Tile(at), TileWorkKind.ConvertRoom, Keeper));

        // And the exclusion really is heart-specific rather than a room rule: a
        // rival's TREASURY converts, on the same map, for the same keeper.
        Give(db, world, RoomTypeId.Treasury, Rival, new TilePos(3, 3));
        Assert.True(TileWorkRules.Applies(
            db, world.Tile(new TilePos(3, 3)), TileWorkKind.ConvertRoom, Keeper));
    }

    [Fact]
    public void AndWhenItIsClaimableTheTerrainWillNotChangeUnderIt()
    {
        // What a conversion leaves behind, for the day a fallen heart becomes
        // convertible: the terrain stays and only the owner moves. So a claimed
        // ruin is still Dungeon Heart terrain, which is what makes the two
        // prohibitions below keep working on it without a single heart-specific
        // clause anywhere.
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(2, 2);
        Give(db, world, RoomTypeId.DungeonHeart, Rival, at);

        Assert.True(db.TerrainById(world.Tile(at).TerrainId).IsOwnable);
        Assert.Equal(
            Dk2TerrainFixture.DungeonHeartTerrain,
            TileWorkRules.OutcomeTerrainId(db, world.Tile(at), TileWorkKind.ConvertRoom));
    }

    [Fact]
    public void TheKeeperThatClaimsItStillCannotSellIt()
    {
        // OWNER: "it can't be sold". Already true, and the reason is one line in
        // RoomRules.CanSell: sellable means BUILDABLE, the same flag that decides
        // what a keeper may build. The shipped Dungeon Heart record carries no
        // `buildable` flag - nor does the Portal - so neither was ever sellable
        // and no heart-specific clause was needed.
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(2, 2);
        Give(db, world, RoomTypeId.DungeonHeart, Keeper, at);

        Assert.False(db.Room(RoomTypeId.DungeonHeart).IsBuildable);
        Assert.Equal(
            RoomSale.NotSellable,
            RoomRules.CanSell(db, world.Tile(at), Keeper, out _));

        var sim = new GameSimulation(db, world, 11, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Tick(InputFrame.Of(SellRoomCommand.At(Keeper, at)));

        Assert.Equal(Dk2TerrainFixture.DungeonHeartTerrain, world.Tile(at).TerrainId);
        Assert.Contains(sink.Events, e =>
            e.Kind == "sold" && e.Detail.Contains("NotSellable=1", StringComparison.Ordinal));
    }

    [Fact]
    public void AndCannotBuildAnythingElseOnTopOfIt()
    {
        // OWNER: "and have something else built in its place". Already true, and
        // again for a general reason: RoomRules.CanBuild refuses any tile whose
        // terrain is a room at all, so a heart is covered by the same clause that
        // stops a Lair going on a Treasury.
        GameDatabase db = Db;
        WorldState world = World(db);
        var at = new TilePos(2, 2);
        Give(db, world, RoomTypeId.DungeonHeart, Keeper, at);

        Assert.Equal(
            RoomPlacement.Occupied,
            RoomRules.CanBuild(db, world.Tile(at), db.Room(RoomTypeId.Treasury), Keeper));

        var sim = new GameSimulation(db, world, 11, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Tick(InputFrame.Of(
            BuildRoomCommand.At(Keeper, (byte)RoomTypeId.Treasury, at)));

        Assert.Equal(Dk2TerrainFixture.DungeonHeartTerrain, world.Tile(at).TerrainId);
        Assert.Contains(sink.Events, e =>
            e.Kind == "built" && e.Detail.Contains("Occupied=1", StringComparison.Ordinal));
    }

    [Fact]
    public void AndTheClaimedHeartStillBlocksItsOwnCentrePlatform()
    {
        // The half that IS new, from ADR-0084: a claimed heart is still a heart,
        // so its middle nine are still structure and still unwalkable. A keeper
        // who takes a rival's ruin does not get nine tiles of floor out of it.
        GameDatabase db = Db;
        WorldState world = World(db);

        for (int y = 4; y <= 8; y++)
        {
            for (int x = 4; x <= 8; x++)
            {
                Give(db, world, RoomTypeId.DungeonHeart, Keeper, new TilePos(x, y));
            }
        }

        Assert.Equal(9, StructureBlocks.For(world, db).Count);
    }
}
