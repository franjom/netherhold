using DK2.Data;
using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>
/// Covers the seam between the file parsers and the simulation: one immutable
/// database, loaded once, that everything downstream reads.
///
/// Skips when no Dungeon Keeper 2 installation is configured (DK2_DIR).
/// </summary>
public class GameDatabaseTests
{
    private static GameDatabase? Database() => GameDatabaseLoader.LoadFromInstall();

    [Fact]
    public void Load_ReadsEveryDefinitionTable()
    {
        GameDatabase? db = Database();
        if (db is null) return; // skipped: no DK2 install

        Assert.Equal(41, db.Terrain.Count);
        Assert.Equal(26, db.Rooms.Count);
        Assert.Equal(46, db.Creatures.Count);
        Assert.NotEmpty(db.Variables.Misc);
    }

    [Fact]
    public void Definitions_CrossReferenceCorrectly()
    {
        GameDatabase? db = Database();
        if (db is null) return;

        // Every room lays down a terrain type that exists, and the room that
        // spawns skeletons is the Prison. Both facts already hold at the parser
        // level; asserting them here proves the database preserved them.
        foreach (RoomDef room in db.Rooms.Values)
            Assert.True(db.Terrain.ContainsKey(room.TerrainId),
                $"{room.Name} lays unknown terrain id {room.TerrainId}");

        RoomDef prison = db.Room(RoomTypeId.Prison);
        Assert.Equal("Skeleton", db.CreatureById(prison.CreatedCreatureId).Name);
    }

    [Fact]
    public void BalanceNumbers_AreReachableByName()
    {
        GameDatabase? db = Database();
        if (db is null) return;

        // The digging and claiming deltas the M3 mechanics will read. Values
        // CONFIRMED in docs/research/kwd-variables.md; the point of the test is
        // that simulation code can reach them without hardcoding anything.
        Assert.Equal(-300, db[VariableTypeId.MineGoldHealth]);
        Assert.Equal(825, db[VariableTypeId.ClaimTileHealth]);
        Assert.Equal(-5000, db[VariableTypeId.DigRockHealth]);
        Assert.Equal(50, db[VariableTypeId.ReinforceWallHealth]);
    }

    [Fact]
    public void MissingVariable_FailsLoudly()
    {
        GameDatabase? db = Database();
        if (db is null) return;

        // Silently returning 0 for an absent balance number would be a very
        // quiet way to invent DK2 behaviour.
        Assert.Throws<KeyNotFoundException>(() => db[(VariableTypeId)9999]);
    }

    [Fact]
    public void LookupByName_FindsKnownDefinitions()
    {
        GameDatabase? db = Database();
        if (db is null) return;

        Assert.NotNull(db.CreatureByName("Imp"));
        Assert.NotNull(db.RoomByName("Treasury"));
        Assert.Null(db.CreatureByName("Nonexistent Creature"));
    }

    [Fact]
    public void UnknownIds_ThrowRatherThanReturningNull()
    {
        GameDatabase? db = Database();
        if (db is null) return;

        Assert.Throws<KeyNotFoundException>(() => db.TerrainById(200));
        Assert.Throws<KeyNotFoundException>(() => db.RoomById(200));
        Assert.Throws<KeyNotFoundException>(() => db.CreatureById(200));
    }

    [Fact]
    public void LoadCampaignMap_ReadsTheFirstLevel()
    {
        string? install = Dk2Install.Find();
        if (install is null) return;

        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, "level1");
        Assert.NotNull(map);
        Assert.Equal(30, map.Width);
        Assert.Equal(38, map.Height);
        Assert.Equal(30 * 38, map.Tiles.Count);
    }

    [Fact]
    public void LoadCampaignMap_ReturnsNullForAnUnknownLevel()
    {
        string? install = Dk2Install.Find();
        if (install is null) return;

        Assert.Null(GameDatabaseLoader.LoadCampaignMap(install, "no_such_level"));
    }
}
