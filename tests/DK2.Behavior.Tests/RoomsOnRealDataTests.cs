// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Behavior.Tests;

/// <summary>
/// The room and gold rules against the real game's data.
///
/// Two jobs, both of them guards rather than demonstrations. The first is drift:
/// <c>DK2.Simulation.Tests.Dk2TerrainFixture</c> now transcribes four room
/// records and three gold limits, and a transcription can be wrong — the Imp's
/// hunger rate already was. The second is the structural claim underneath
/// <see cref="RoomRules"/>: that the rules can be written against the ROOMS
/// record's own flags without ever naming a room, which is only worth anything
/// if the shipped flags really do partition the rooms the way the code assumes.
///
/// Skips when no installation is configured (DK2_DIR).
/// </summary>
public class RoomsOnRealDataTests
{
    private static GameDatabase? RealDatabase()
    {
        string? install = Dk2Install.Find();
        return install is null ? null : GameDatabaseLoader.Load(install);
    }

    /// <summary>
    /// <b>`RecommendedSizeX/Y` is the size below which a room does nothing</b>,
    /// and the owner matched it twice without ever seeing the field (Q45).
    ///
    /// <list type="bullet">
    /// <item>"a graveyard needs a minimum 3x3 so one grave appears" — the record
    /// says <b>3x3</b>;</item>
    /// <item>a temple was flagged as needing <b>5x5</b> — the record says
    /// 5x5.</item>
    /// </list>
    ///
    /// <para>The field is badly named: it recommends nothing, and it does not
    /// stop a keeper building smaller — a 1x1 temple really is buildable (Q36),
    /// it just does not work. Read by
    /// <c>RoomInstance.IsFunctional</c>.</para>
    ///
    /// <para>Also asserted: the rooms whose size is 1x1. Without them the claim
    /// would be untestable in the direction that matters — a rule which said
    /// every room needs to be big would fit the two observations equally
    /// well.</para>
    /// </summary>
    [Fact]
    public void TheRecommendedSizesAreTheFunctionalMinimumsTheOwnerObserved()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        void Size(string name, int x, int y)
        {
            RoomDef room = db.Rooms.Values.Single(r => r.Name == name);
            Assert.Equal(x, room.RecommendedSizeX);
            Assert.Equal(y, room.RecommendedSizeY);
        }

        Size("Graveyard", 3, 3);   // owner: 3x3 before a single grave appears
        Size("Temple", 5, 5);      // owner: believed 5x5

        // The other end of the claim: rooms that work at a single tile.
        Size("Treasury", 1, 1);
        Size("Lair", 1, 1);
        Size("Guard Room", 1, 1);

        // And the ones nobody has looked at, pinned so a data change is visible.
        Size("Prison", 5, 5);
        Size("Combat Pit", 5, 5);
        Size("Library", 3, 3);
    }

    [Fact]
    public void TheRoomFixture_MatchesTheInstall()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return; // skipped: no DK2 install

        void Check(
            RoomTypeId id, string name, ushort cost, byte terrain, uint flags,
            byte sizeX, byte sizeY)
        {
            RoomDef r = db.Room(id);
            Assert.Equal(name, r.Name);
            Assert.Equal(cost, r.Cost);
            Assert.Equal(terrain, r.TerrainId);
            Assert.Equal(flags, (uint)r.Flags);
            Assert.Equal(100, r.ReturnPercentage);

            // Read by RoomInstance.IsFunctional (Q45), so the fixture has to
            // carry the real values and this is what stops it drifting.
            Assert.Equal(sizeX, r.RecommendedSizeX);
            Assert.Equal(sizeY, r.RecommendedSizeY);
        }

        Check(RoomTypeId.Treasury, "Treasury", 200, 10, 0x128Cu, 1, 1);
        Check(RoomTypeId.Lair, "Lair", 300, 11, 0x128Cu, 1, 1);
        Check(RoomTypeId.DungeonHeart, "Dungeon Heart", 0, 14, 0x204u, 5, 5);
        Check(RoomTypeId.WoodenBridge, "Wooden Bridge", 200, 17, 0x1083u, 1, 1);

        Assert.Equal(3000, db[VariableTypeId.MaxGoldPerTreasuryTile]);
        Assert.Equal(1000, db[VariableTypeId.MaxGoldPerDungeonHeartTile]);
        Assert.Equal(2000, db[VariableTypeId.MaxGoldPileOutsideTreasury]);

        // The sell rate the fixture copies (Q34). This is the assertion that
        // would have caught four sessions of recording it as 100.
        Assert.Equal(50, db[VariableTypeId.RoomSellValuePercentageOfCost]);
        Assert.Equal(50, db[VariableTypeId.DoorSellValuePercentageOfCost]);
        Assert.Equal(50, db[VariableTypeId.TrapSellValuePercentageOfCost]);
    }

    /// <summary>
    /// <b>`ReturnPercentage` is a per-room gate, not a rival refund rate</b> (Q34).
    /// It is 100 for every room a keeper can build and 0 for every room they
    /// cannot sell, and the fact that those two sets coincide is what makes the
    /// reading more than a guess. Asserted in both directions, so that "it is
    /// always 100" could not fit the same evidence.
    /// </summary>
    [Fact]
    public void ReturnPercentageIsZeroForExactlyTheRoomsAKeeperCannotSell()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        RoomDef[] zero = [.. db.Rooms.Values.Where(r => r.ReturnPercentage == 0)];
        RoomDef[] hundred = [.. db.Rooms.Values.Where(r => r.ReturnPercentage == 100)];

        // Nothing in between: the field is a gate, not a scale.
        Assert.Equal(db.Rooms.Count, zero.Length + hundred.Length);

        // Not one of the zero rows is buildable, and every zero row is a gate.
        Assert.All(zero, r => Assert.False(r.IsBuildable));
        Assert.All(zero, r => Assert.Contains("Gate", r.Name));
        Assert.NotEmpty(zero);

        // And every buildable room refunds exactly half its cost.
        RoomDef[] buildable = [.. db.Rooms.Values.Where(r => r.IsBuildable)];
        Assert.NotEmpty(buildable);
        Assert.All(buildable, r =>
            Assert.Equal(r.Cost / 2, RoomRules.RefundPerTile(db, r)));

        // A temple is the expensive end of it: 3000 a tile, 1500 back.
        Assert.Equal(1500, RoomRules.RefundPerTile(db, db.Room(RoomTypeId.Temple)));
    }

    [Fact]
    public void TheRoomTerrainFixture_MatchesTheInstall()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        void Check(byte id, string name, ushort start, byte toDead)
        {
            TerrainDef t = db.TerrainById(id);
            Assert.Equal(name, t.Name);
            Assert.Equal(start, t.StartingHealth);
            Assert.Equal(toDead, t.DestroyedTypeTerrainId);
            Assert.True(t.IsRoom);
            Assert.True(t.IsOwnable);
            Assert.False(t.IsSolid);
        }

        Check(10, "Treasury", 10000, 8);
        Check(11, "Lair", 10000, 8);
        Check(14, "Dungeon Heart", 10000, 8);
        Check(17, "Wooden Bridge", 2000, 8);

        TerrainDef water = db.TerrainById(4);
        Assert.Equal("Water", water.Name);
        Assert.True(water.IsWater);
    }

    [Fact]
    public void EveryRoomOwnsItsOwnTerrain()
    {
        // GameDatabase.RoomsByTerrain is a dictionary keyed on terrain id, and
        // the room rules read "which room is this tile part of" through it. That
        // only answers a question if the mapping is one-to-one.
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        List<byte> terrainIds = db.Rooms.Values.Select(r => r.TerrainId).ToList();

        Assert.Equal(terrainIds.Count, terrainIds.Distinct().Count());
        Assert.Equal(db.Rooms.Count, db.RoomsByTerrain.Count);
    }

    [Fact]
    public void EveryRoomsTerrainIsFlaggedAsARoom()
    {
        // The reverse link. If a room named terrain that was not flagged Room,
        // RoomRules.IsSuitableGround would let a keeper build on top of it.
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        foreach (RoomDef room in db.Rooms.Values)
            Assert.True(db.TerrainById(room.TerrainId).IsRoom, $"{room.Name} terrain is not flagged Room");
    }

    [Fact]
    public void OnlyBridgesGoOverWaterAndLava()
    {
        // The claim RoomRules rests on: the placeable-on flags carry the whole
        // rule, so no code has to know what a bridge is. If a future data set
        // gave a third room water placement, this fails rather than the rules
        // quietly doing something nobody chose.
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        string[] overWater = db.Rooms.Values
            .Where(r => r.PlaceableOnWater || r.PlaceableOnLava)
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Hero Stone Bridge", "Stone Bridge", "Wooden Bridge"], overWater);

        foreach (RoomDef bridge in db.Rooms.Values.Where(r => r.PlaceableOnWater))
            Assert.False(bridge.PlaceableOnLand, $"{bridge.Name} is placeable on land as well as water");
    }

    [Fact]
    public void NoRoomIsBuildableWithoutSomewhereToBuildIt()
    {
        // A Buildable room with no placeable-on flag at all would be one a
        // keeper could select and never place — a shape the rules do not handle
        // and the data does not contain.
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        foreach (RoomDef room in db.Rooms.Values.Where(r => r.IsBuildable))
        {
            Assert.True(
                room.PlaceableOnLand || room.PlaceableOnWater || room.PlaceableOnLava,
                $"{room.Name} is buildable but has no ground it can go on");
        }
    }

    [Fact]
    public void OnlyTheTreasuryAndTheHeartHoldGold()
    {
        // GoldStore keys capacity on the room type because the two variables are
        // named for those two rooms. This asserts the consequence against the
        // real table: every other room tile in the game holds nothing.
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        string[] holders = db.Rooms.Values
            .Where(r => GoldStore.Capacity(db, new TileState { TerrainId = r.TerrainId }) > 0)
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Dungeon Heart", "Treasury"], holders);
    }

    [Fact]
    public void ARoomTileFallsBackToClaimedFloor_SoItsGoldCannotStayWhereItWas()
    {
        // The spill rule, on the shipped numbers: a treasury beaten down becomes
        // terrain 8, which holds nothing, so the gold in it has to go somewhere
        // and there is nowhere yet (Q31).
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        RoomDef treasury = db.Room(RoomTypeId.Treasury);
        TerrainDef terrain = db.TerrainById(treasury.TerrainId);
        TerrainDef fallback = db.TerrainById(terrain.DestroyedTypeTerrainId);

        Assert.False(fallback.IsRoom);
        Assert.Equal(0, GoldStore.Capacity(db, new TileState { TerrainId = fallback.TerrainId }));
    }

    [Fact]
    public void ARoomCanBeBuiltOnClaimedFloorOnARealCampaignLevel()
    {
        // The whole rule against a shipped map: find a tile the level gives a
        // player as claimed floor, and check the ground rules let its owner
        // build there and let nobody else.
        string? install = Dk2Install.Find();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, "level1");
        if (map is null) return;

        WorldState world = WorldState.FromMap(db, map, map.Tiles.Select(t => t.PlayerId).Distinct());
        RoomDef lair = db.Room(RoomTypeId.Lair);

        (int index, byte owner) = FindClaimedFloor(db, world);
        Assert.True(index >= 0, "level1 has no claimed floor tile to build on");

        ref TileState tile = ref world.TileAtIndex(index);
        Assert.Equal(RoomPlacement.Allowed, RoomRules.CanBuild(db, tile, lair, owner));
        Assert.Equal(
            RoomPlacement.NotYourLand,
            RoomRules.CanBuild(db, tile, lair, (byte)(owner + 1)));
    }

    /// <summary>The first ownable, non-room, non-solid tile with an owner.</summary>
    /// <summary>
    /// <b>Room cost is per tile, and the ROOMS record's number is the one a
    /// keeper pays.</b> CONFIRMED (2026-08-30, owner observation of the
    /// original): five rooms were priced from the game and all five match the
    /// shipped record exactly.
    ///
    /// <para>That closes Q32, which had been STRONGLY INFERRED on the grounds
    /// that the original charges as a keeper paints. Five independent matches is
    /// a different kind of evidence from one plausible reading, and it is worth
    /// a test rather than a sentence: <c>Cost</c> could have been per room, or
    /// scaled, or a base a room's size modified, and none of those would have
    /// shown up until somebody built a big room.</para>
    /// </summary>
    [Theory]
    [InlineData(RoomTypeId.Treasury, 200)]
    [InlineData(RoomTypeId.Lair, 300)]
    [InlineData(RoomTypeId.Hatchery, 300)]
    [InlineData(RoomTypeId.Library, 600)]
    [InlineData(RoomTypeId.TrainingRoom, 500)]
    public void ARoomCostsWhatTheOriginalChargesForIt_PerTile(RoomTypeId room, int goldPerTile)
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return; // skipped: no DK2 install

        Assert.Equal(goldPerTile, db.Room(room).Cost);
        Assert.Equal(goldPerTile, RoomRules.CostPerTile(db.Room(room)));
    }

    private static (int Index, byte Owner) FindClaimedFloor(GameDatabase db, WorldState world)
    {
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            TerrainDef terrain = db.TerrainById(tile.TerrainId);
            if (terrain.IsOwnable && !terrain.IsSolid && !terrain.IsRoom && tile.OwnerPlayerId != 0)
                return (i, tile.OwnerPlayerId);
        }
        return (-1, 0);
    }
}
