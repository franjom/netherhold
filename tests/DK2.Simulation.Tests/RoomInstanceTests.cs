// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>
/// Rooms as instances rather than as loose tiles (ADR-0027).
///
/// <para>Every rule these support is one the owner measured in the original and
/// which could not be expressed at all while a room was terrain tile by tile: a
/// dungeon heart holding 16000 (Q52), a temple's capacity being its rim (Q60), a
/// room needing a minimum size to function (Q45).</para>
///
/// <para>The instances are <b>derived</b>, so the tests that matter most here are
/// the ones about splitting and merging: those are where stored membership would
/// have gone wrong, and where recomputing cannot.</para>
/// </summary>
public class RoomInstanceTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;

    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    /// <summary>A world with nothing but the fixture's claimed room, so painting is unambiguous.</summary>
    private static WorldState World() =>
        WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    /// <summary>Paints a solid rectangle of room terrain, the way a keeper building it would.</summary>
    private static void Paint(
        WorldState world, RoomTypeId type, int x0, int y0, int w, int h, byte owner = Keeper)
    {
        RoomDef room = Db.Room(type);
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                RoomRules.Place(Db, ref tile, room, owner);
            }
        }
    }

    private static RoomInstance RoomAt(WorldState world, int x, int y) =>
        RoomIndex.RoomAt(world, Db, (y * world.Width) + x)
        ?? throw new InvalidOperationException($"no room at ({x},{y})");

    // ---- what a room is ---------------------------------------------------

    [Fact]
    public void ARectangleOfRoomTerrain_IsOneInstance()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 4, 3);

        RoomInstance room = RoomAt(world, 5, 5);

        Assert.Equal(RoomTypeId.Treasury, room.Definition.TypeId);
        Assert.Equal(Keeper, room.OwnerPlayerId);
        Assert.Equal(12, room.TileCount);
        Assert.Equal(4, room.Width);
        Assert.Equal(3, room.Height);
        Assert.True(room.IsRectangular);

        // Every tile of it answers with the same room.
        Assert.Equal(room.TileCount, RoomAt(world, 8, 7).TileCount);
    }

    [Fact]
    public void TileIndicesAreAscending_SoIterationIsReproducible()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 3, 3);

        IReadOnlyList<int> tiles = RoomAt(world, 6, 6).TileIndices;

        Assert.Equal(9, tiles.Count);
        for (int i = 1; i < tiles.Count; i++)
            Assert.True(tiles[i] > tiles[i - 1], "tile indices are not ascending");
    }

    /// <summary>
    /// Two keepers' treasuries meeting along a line are two treasuries. It
    /// matters as soon as a rule asks how big "this" room is — a shared instance
    /// would let a rival's tiles inflate your capacity.
    /// </summary>
    [Fact]
    public void TwoOwnersMeeting_AreTwoRooms()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 3);
        Paint(world, RoomTypeId.Treasury, 7, 5, 2, 3, owner: Rival);

        Assert.Equal(6, RoomAt(world, 5, 5).TileCount);
        Assert.Equal(6, RoomAt(world, 8, 5).TileCount);
        Assert.Equal(Rival, RoomAt(world, 8, 5).OwnerPlayerId);
    }

    /// <summary>
    /// Connectivity is orthogonal, so an hourglass touching only at a corner is
    /// two rooms rather than one.
    /// </summary>
    [Fact]
    public void TilesJoinedOnlyAtACorner_AreTwoRooms()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 2);
        Paint(world, RoomTypeId.Treasury, 7, 7, 2, 2);

        Assert.Equal(4, RoomAt(world, 5, 5).TileCount);
        Assert.Equal(4, RoomAt(world, 7, 7).TileCount);
        Assert.Equal(2, RoomIndex.Build(world, Db).Count(r => r.Definition.TypeId == RoomTypeId.Treasury));
    }

    // ---- splitting and merging, which is why this is derived ---------------

    /// <summary>
    /// <b>Cut a room in half and it is two rooms, with no split logic anywhere.</b>
    /// This is the case that made storing room membership in the tile the wrong
    /// choice: every path that changes a tile would have had to maintain it.
    /// </summary>
    [Fact]
    public void CuttingARoomInHalf_MakesTwoRooms()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 5, 1);
        Assert.Equal(5, RoomAt(world, 5, 5).TileCount);

        // Dig the middle tile back out from under it.
        ref TileState middle = ref world.Tile(7, 5);
        middle.TerrainId = Dk2TerrainFixture.DirtPath;
        middle.Health = Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;

        Assert.Equal(2, RoomAt(world, 5, 5).TileCount);
        Assert.Equal(2, RoomAt(world, 8, 5).TileCount);
    }

    /// <summary>And the reverse, equally free: one tile joins two rooms into one.</summary>
    [Fact]
    public void FillingTheGap_MakesThemOneRoomAgain()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 2, 1);
        Paint(world, RoomTypeId.Treasury, 8, 5, 2, 1);
        Assert.Equal(2, RoomAt(world, 5, 5).TileCount);

        Paint(world, RoomTypeId.Treasury, 7, 5, 1, 1);

        Assert.Equal(5, RoomAt(world, 5, 5).TileCount);
        Assert.Equal(5, RoomAt(world, 9, 5).TileCount);
    }

    // ---- the rim, which is a temple's capacity (Q60) ------------------------

    /// <summary>
    /// <b>The owner's three temple tooltips, as geometry.</b> A temple's capacity
    /// is its rim — the tiles with a neighbour outside the room — and the owner
    /// read 8, 12 and 16 off 3x3, 4x4 and 5x5 temples in the original. For a
    /// solid rectangle the rim is <c>2(w+h) − 4</c>, which is exactly those.
    ///
    /// <para>Counted rather than computed from the formula, so an L-shaped room
    /// answers sensibly too — which the formula cannot.</para>
    /// </summary>
    [Theory]
    [InlineData(3, 3, 8)]
    [InlineData(4, 4, 12)]
    [InlineData(5, 5, 16)]
    [InlineData(3, 5, 12)]
    [InlineData(1, 1, 1)]
    [InlineData(6, 6, 20)]
    public void TheRimOfARectangle_IsTwoWidthPlusHeightLessFour(int w, int h, int expected)
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 4, 4, w, h);

        RoomInstance room = RoomAt(world, 4, 4);

        Assert.Equal(expected, room.EdgeTileCount);
        Assert.Equal((w * h) - expected, room.InteriorTileCount);
    }

    // ---- the heart's platform, which is Q52 --------------------------------

    /// <summary>
    /// <b>Sixteen of a 5x5 heart's tiles store gold and nine do not.</b> The nine
    /// are the raised platform the heart structure stands on, and the total is
    /// the 16000 the owner measured with no treasury built.
    /// </summary>
    [Fact]
    public void AFiveByFiveHeart_StoresSixteenThousand()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.DungeonHeart, 5, 5, 5, 5);

        int storing = 0;
        int perTile = Db[VariableTypeId.MaxGoldPerDungeonHeartTile];

        foreach (int index in RoomAt(world, 5, 5).TileIndices)
            if (GoldStore.Capacity(world, Db, index) > 0) storing++;

        Assert.Equal(16, storing);
        Assert.Equal(16_000, storing * perTile);
        Assert.Equal(16_000, GoldStore.SpaceFor(world, Db, Keeper));
    }

    /// <summary>
    /// The platform is the middle nine, identified from the bounding box rather
    /// than from a shape table — so it needs nothing from
    /// <c>TileConstruction</c>, which we do not read.
    /// </summary>
    [Fact]
    public void ThePlatformIsTheMiddleNine()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.DungeonHeart, 5, 5, 5, 5);
        RoomInstance heart = RoomAt(world, 5, 5);

        for (int y = 5; y <= 9; y++)
        {
            for (int x = 5; x <= 9; x++)
            {
                bool middle = x is >= 6 and <= 8 && y is >= 6 and <= 8;
                Assert.Equal(middle, heart.IsCentrePlatform(x, y));
            }
        }
    }

    /// <summary>
    /// A treasury has no platform, so all of it stores — which is what stops the
    /// heart rule leaking into every room with a capacity.
    /// </summary>
    [Fact]
    public void ATreasuryHasNoPlatform_AndAllOfItStores()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 5, 5);

        int perTile = Db[VariableTypeId.MaxGoldPerTreasuryTile];
        Assert.Equal(25 * perTile, GoldStore.SpaceFor(world, Db, Keeper));
    }

    /// <summary>
    /// A heart too small to have a middle keeps all of its tiles — so a
    /// half-destroyed heart does not swallow its own storage, and every existing
    /// test that lays down one or two heart tiles still means what it did.
    /// </summary>
    [Fact]
    public void AHeartTooSmallToHaveAMiddle_StoresEverywhere()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.DungeonHeart, 5, 5, 2, 2);

        int perTile = Db[VariableTypeId.MaxGoldPerDungeonHeartTile];
        Assert.Equal(4 * perTile, GoldStore.SpaceFor(world, Db, Keeper));
    }

    // ---- minimum size to function, which is Q45 ----------------------------

    /// <summary>
    /// <b>A room below its <c>RecommendedSize</c> does nothing.</b> The field is
    /// badly named — it does not stop a keeper building smaller (Q36) — and two
    /// owner observations match it: a graveyard needs 3x3 for one grave, and a
    /// temple 5x5. The fixture's heart is 5x5, so a 4x4 one is not a working
    /// heart.
    /// </summary>
    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(6, 6, true)]
    [InlineData(4, 5, false)]
    [InlineData(5, 4, false)]
    [InlineData(1, 1, false)]
    public void AHeartFunctionsOnlyAtItsRecommendedSize(int w, int h, bool functional)
    {
        WorldState world = World();
        Paint(world, RoomTypeId.DungeonHeart, 4, 4, w, h);

        Assert.Equal(functional, RoomAt(world, 4, 4).IsFunctional);
    }

    /// <summary>
    /// A treasury's recommended size is 1x1, so a single tile of one works —
    /// which is the other half of the claim and stops "functional" becoming a
    /// rule that quietly disables small rooms in general.
    /// </summary>
    [Fact]
    public void ASingleTreasuryTileFunctions()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 1, 1);

        Assert.True(RoomAt(world, 5, 5).IsFunctional);
    }

    // ---- the whole-map sweep -----------------------------------------------

    /// <summary>
    /// <see cref="RoomIndex.Build"/> and <see cref="RoomIndex.RoomAt"/> have to
    /// agree, because they are chosen between on cost and callers assume they
    /// are interchangeable.
    /// </summary>
    [Fact]
    public void TheSweepAndTheSingleTileFill_Agree()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 3, 2);
        Paint(world, RoomTypeId.Lair, 9, 5, 2, 2);
        Paint(world, RoomTypeId.DungeonHeart, 5, 8, 5, 2, owner: Rival);

        List<RoomInstance> all = RoomIndex.Build(world, Db);

        // The fixture's map already contains a claimed-path room, which is not
        // room terrain, so only the three painted here count.
        Assert.Equal(3, all.Count);

        foreach (RoomInstance room in all)
        {
            foreach (int index in room.TileIndices)
            {
                RoomInstance? single = RoomIndex.RoomAt(world, Db, index);
                Assert.NotNull(single);
                Assert.Equal(room.TileCount, single.TileCount);
                Assert.Equal(room.Definition.TypeId, single.Definition.TypeId);
                Assert.Equal(room.OwnerPlayerId, single.OwnerPlayerId);
                Assert.Equal(room.EdgeTileCount, single.EdgeTileCount);
            }
        }
    }

    [Fact]
    public void ATileThatIsNotARoom_HasNoInstance()
    {
        WorldState world = World();

        // The fixture's claimed path is owned floor and is not a room.
        Assert.Null(RoomIndex.RoomAt(world, Db, (2 * world.Width) + 2));
        Assert.Null(RoomIndex.RoomAt(world, Db, -1));
        Assert.Null(RoomIndex.RoomAt(world, Db, world.TileCount));
    }
}
