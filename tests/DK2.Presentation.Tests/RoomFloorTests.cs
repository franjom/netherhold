// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>DK2's room floors and liquid beds (floor-and-terrain-pieces.md §4 and §6), on rooms
/// carved into the shipped Level 1: quadrants NW, NE, SE, SW.</summary>
public class RoomFloorTests
{
    private const int NW = 0, NE = 1, SE = 2, SW = 3;
    private const int X0 = 10, Y0 = 10;

    private sealed record Room(GameDatabase Database, WorldState World, WorldView View);

    /// <summary>A room <paramref name="width"/> by <paramref name="height"/> inside a ring of
    /// Reinforced Wall, owned by player 3, its top-left tile at (X0 + 1, Y0 + 1).</summary>
    private static Room Carve(RoomTypeId type, int width, int height)
    {
        string dir = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")!;
        WorldState world = WorldState.FromMap(db, map, [0, 1]);
        byte wall = db.Terrain.Values.Single(t => t.Name == "Reinforced Wall").TerrainId;
        byte floor = db.Room(type).TerrainId;
        for (int y = Y0; y <= Y0 + height + 1; y++)
            for (int x = X0; x <= X0 + width + 1; x++)
            {
                bool ring = x == X0 || x == X0 + width + 1 || y == Y0 || y == Y0 + height + 1;
                world.Tile(x, y).TerrainId = ring ? wall : floor;
                world.Tile(x, y).OwnerPlayerId = 3;
            }
        return new Room(db, world, new WorldView(db));
    }

    private static PieceSet Floor(Room room, int x, int y) => room.View.TileAt(room.World, x, y).RoomFloor;

    /// <summary>The top-left corner: an outer corner NW (piece 1), the two edges beside it (piece
    /// 0) and an interior quarter SE (piece 3), each turned by its table rotation plus its quadrant.</summary>
    [InstallFact]
    public void ACornerTileDrawsAnOuterCornerTwoEdgesAndAnInteriorQuarter()
    {
        PieceSet floor = Floor(Carve(RoomTypeId.Treasury, 3, 4), X0 + 1, Y0 + 1);

        Assert.False(floor.HasWhole);
        Assert.Equal((1, 3), (floor.QuarterPiece(NW), floor.QuarterRotation(NW)));
        Assert.Equal((0, 0), (floor.QuarterPiece(NE), floor.QuarterRotation(NE)));
        Assert.Equal((3, 2), (floor.QuarterPiece(SE), floor.QuarterRotation(SE)));
        Assert.Equal((0, 3), (floor.QuarterPiece(SW), floor.QuarterRotation(SW)));
    }

    /// <summary>A tile inside a room's wall but beside another room tile's missing corner takes an
    /// inner corner (piece 2): here the room is an L, and the tile at its bend lacks only its SE.</summary>
    [InstallFact]
    public void TheBendOfAnLTakesAnInnerCorner()
    {
        Room room = Carve(RoomTypeId.Treasury, 3, 3);
        room.World.Tile(X0 + 3, Y0 + 3).TerrainId =
            room.Database.Terrain.Values.Single(t => t.Name == "Reinforced Wall").TerrainId;

        PieceSet floor = Floor(room, X0 + 2, Y0 + 2);
        Assert.Equal((2, 3), (floor.QuarterPiece(SE), floor.QuarterRotation(SE)));
        Assert.Equal((3, 0), (floor.QuarterPiece(NW), floor.QuarterRotation(NW)));
    }

    /// <summary>A Treasury's interior tile is a floor slot and draws the centre piece 9 whole.</summary>
    [InstallFact]
    public void AnInteriorTreasuryTileDrawsTheCentrePiece()
    {
        PieceSet floor = Floor(Carve(RoomTypeId.Treasury, 3, 4), X0 + 2, Y0 + 2);

        Assert.True(floor.HasWhole);
        Assert.False(floor.HasQuarters);
        Assert.Equal((9, 0), (floor.WholePiece, floor.WholeRotation));
    }

    /// <summary>A Guard Room's floor slots are one parity of a checkerboard: an interior tile of
    /// the other parity draws four interior quarters, each turned by its quadrant.</summary>
    [InstallFact]
    public void AGuardRoomsOffParityInteriorTileDrawsFourInteriorQuarters()
    {
        Room room = Carve(RoomTypeId.GuardRoom, 5, 5);
        int centres = 0;
        for (int y = Y0 + 2; y <= Y0 + 4; y++)
            for (int x = X0 + 2; x <= X0 + 4; x++)
            {
                PieceSet floor = Floor(room, x, y);
                if (floor.HasWhole) { centres++; continue; }
                for (int q = 0; q < 4; q++) Assert.Equal((3, q), (floor.QuarterPiece(q), floor.QuarterRotation(q)));
            }

        // The leftmost slot (X0 + 2, Y0 + 2) sets the parity: five of the nine.
        Assert.Equal(5, centres);
        Assert.True(Floor(room, X0 + 2, Y0 + 2).HasWhole);
    }

    /// <summary>A Work Shop slot whose position hash picks the empty object 46 draws no centre.</summary>
    [InstallFact]
    public void AWorkShopSlotOfTheEmptyObjectDrawsNoCentre()
    {
        Room room = Carve(RoomTypeId.Workshop, 7, 7);
        RoomDef shop = room.Database.Room(RoomTypeId.Workshop);
        int empty = 0, centres = 0;
        for (int y = Y0 + 2; y <= Y0 + 6; y++)
            for (int x = X0 + 2; x <= X0 + 6; x++)
            {
                if (((x + y) & 1) != ((X0 + 2 + Y0 + 2) & 1)) continue;
                bool isEmpty = shop.ObjectIds[AutoFurnitureRules.Choice(x, y, 3)] == 46;
                Assert.Equal(!isEmpty, Floor(room, x, y).HasWhole);
                if (isEmpty) empty++; else centres++;
            }

        Assert.True(empty > 0 && centres > 0, $"{empty} empty, {centres} centres: the hash picked one kind only");
    }

    [InstallFact]
    public void PlainTerrainAndTheHeroGatesHaveNoRoomFloor()
    {
        Room room = Carve(RoomTypeId.Treasury, 3, 4);
        Assert.True(Floor(room, X0, Y0).IsEmpty);
        Assert.Null(room.View.TileAt(room.World, X0, Y0).RoomKeys);

        room.World.Tile(X0 + 2, Y0 + 2).TerrainId = room.Database.Room(RoomTypeId.HeroGate2By2).TerrainId;
        Assert.True(Floor(room, X0 + 2, Y0 + 2).IsEmpty);
        Assert.Null(room.View.TileAt(room.World, X0 + 2, Y0 + 2).RoomKeys);
    }

    /// <summary>A 3×3 Portal (<c>0x004EF730</c>, <c>0x00446140</c>): each tile one whole piece,
    /// PORTAL dx + 3·dy from the north-west tile, unturned.</summary>
    [InstallFact]
    public void APortalTileDrawsThePieceOfItsPlaceInTheThreeByThree()
    {
        Room room = Carve(RoomTypeId.Portal, 3, 3);
        for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
            {
                PieceSet floor = Floor(room, X0 + 1 + dx, Y0 + 1 + dy);
                Assert.False(floor.HasQuarters);
                Assert.Equal((dx + (3 * dy), 0), (floor.WholePiece, floor.WholeRotation));
            }
    }

    /// <summary>The Heart's 5×5 table (<c>0x0066DBC8</c>) from its first slab: the outer ring's
    /// corners piece 3 and edges piece 2, the inner ring's corners piece 0 and edges piece 1, and
    /// nothing at the centre, which is still the room's to draw.</summary>
    [InstallFact]
    public void TheHeartLaysItsFiveByFiveTableAndLeavesItsCentreEmpty()
    {
        Room room = Carve(RoomTypeId.DungeonHeart, 5, 5);
        (int, int) At(int dx, int dy)
        {
            PieceSet floor = Floor(room, X0 + 1 + dx, Y0 + 1 + dy);
            return (floor.WholePiece, floor.WholeRotation);
        }

        Assert.Equal((3, 3), At(0, 0));
        Assert.Equal((2, 3), At(2, 0));
        Assert.Equal((3, 1), At(4, 4));
        Assert.Equal((0, 0), At(1, 1));
        Assert.Equal((1, 3), At(1, 2));
        Assert.Equal((2, 0), At(4, 2));
        Assert.True(Floor(room, X0 + 3, Y0 + 3).IsEmpty);
        Assert.NotNull(room.View.TileAt(room.World, X0 + 3, Y0 + 3).RoomKeys);
    }

    /// <summary>A Prison (DoubleQuad, §6.4): its door tile, north of the first interior tile, draws
    /// only piece 14 in its north-west quarter; an interior tile piece 13 four times.</summary>
    [InstallFact]
    public void APrisonsDoorTileDrawsPieceFourteenAndItsInteriorPieceThirteen()
    {
        Room room = Carve(RoomTypeId.Prison, 5, 5);
        PieceSet door = Floor(room, X0 + 2, Y0 + 1);
        Assert.True(door.HasQuarter(NW));
        Assert.False(door.HasQuarter(NE) || door.HasQuarter(SE) || door.HasQuarter(SW) || door.HasWhole);
        Assert.Equal((14, 0), (door.QuarterPiece(NW), door.QuarterRotation(NW)));

        PieceSet inside = Floor(room, X0 + 3, Y0 + 3);
        for (int q = 0; q < 4; q++) Assert.Equal((13, q), (inside.QuarterPiece(q), inside.QuarterRotation(q)));
    }

    /// <summary>A DoubleQuad edge quarter with no interior neighbour takes the outer ring's piece
    /// from the inverted mask, half a turn on: on the north edge, the same edge piece 0 at rotation
    /// 0 the Normal floor gives. Its southern quarters touch the interior and take the inner ring's
    /// piece 10 at the table rotation plus the quadrant (0x00444553, 0x00444734).</summary>
    [InstallFact]
    public void ATempleNorthEdgeTileTakesTheOuterRingAboveAndTheInnerRingBelow()
    {
        Room room = Carve(RoomTypeId.Temple, 5, 5);
        PieceSet edge = Floor(room, X0 + 3, Y0 + 1);
        Assert.Equal((0, 0), (edge.QuarterPiece(NW), edge.QuarterRotation(NW)));
        Assert.Equal((0, 0), (edge.QuarterPiece(NE), edge.QuarterRotation(NE)));
        Assert.Equal((10, 0), (edge.QuarterPiece(SE), edge.QuarterRotation(SE)));
        Assert.Equal((10, 0), (edge.QuarterPiece(SW), edge.QuarterRotation(SW)));
    }

    /// <summary>The bed (<c>0x0043E440</c>): open water draws none, a pool's corner its outer corner
    /// BED1, and lava with every neighbour lava the whole BED3; both keep the liquid's keys.</summary>
    [InstallFact]
    public void ALiquidPoolsBedFollowsItsNeighbours()
    {
        Room room = Carve(RoomTypeId.Treasury, 3, 3);
        TerrainDef water = room.Database.WaterTerrain!, lava = room.Database.LavaTerrain!;
        for (int y = Y0 + 1; y <= Y0 + 3; y++)
            for (int x = X0 + 1; x <= X0 + 3; x++)
                room.World.Tile(x, y).TerrainId = water.TerrainId;

        TileView centre = room.View.TileAt(room.World, X0 + 2, Y0 + 2);
        Assert.True(centre.Bed.IsEmpty);
        Assert.NotNull(centre.BedKeys);
        Assert.Equal(LiquidSurface.Water, centre.Surface);

        PieceSet corner = room.View.TileAt(room.World, X0 + 1, Y0 + 1).Bed;
        Assert.Equal((1, 1), (corner.WholePiece, corner.WholeRotation));

        for (int y = Y0 + 1; y <= Y0 + 3; y++)
            for (int x = X0 + 1; x <= X0 + 3; x++)
                room.World.Tile(x, y).TerrainId = lava.TerrainId;
        PieceSet open = room.View.TileAt(room.World, X0 + 2, Y0 + 2).Bed;
        Assert.Equal((3, 0), (open.WholePiece, open.WholeRotation));
        Assert.Equal(LiquidSurface.Lava, room.View.TileAt(room.World, X0 + 2, Y0 + 2).Surface);
    }

    /// <summary>A bridge over water (Quad, <c>0x00443910</c>) draws the Normal floor's quarters by
    /// its own mask, and over open water, as here, no bed but the water's surface.</summary>
    [InstallFact]
    public void ABridgeOverOpenWaterDrawsQuartersAndNoBed()
    {
        Room room = Carve(RoomTypeId.Treasury, 3, 3);
        TerrainDef water = room.Database.WaterTerrain!;
        byte bridge = room.Database.Room(RoomTypeId.WoodenBridge).TerrainId;
        for (int y = Y0 + 1; y <= Y0 + 3; y++)
            for (int x = X0 + 1; x <= X0 + 3; x++)
                room.World.Tile(x, y).TerrainId = water.TerrainId;
        room.World.Tile(X0 + 2, Y0 + 2).TerrainId = bridge;
        room.World.Tile(X0 + 2, Y0 + 2).Flags = BridgeRules.SpansWater;

        TileView tile = room.View.TileAt(room.World, X0 + 2, Y0 + 2);
        Assert.Equal((1, 3), (tile.RoomFloor.QuarterPiece(NW), tile.RoomFloor.QuarterRotation(NW)));
        Assert.True(tile.Bed.IsEmpty);
        Assert.Same(room.View.TileAt(room.World, X0 + 1, Y0 + 1).BedKeys, tile.BedKeys);
        Assert.Equal(LiquidSurface.Water, tile.Surface);

        // Over lava a bridge draws the bed but no surface (0x0043D1C5..0x0043D394).
        room.World.Tile(X0 + 2, Y0 + 2).Flags = BridgeRules.SpansLava;
        Assert.Equal(LiquidSurface.None, room.View.TileAt(room.World, X0 + 2, Y0 + 2).Surface);
    }
}
