using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Behavior.Tests;

/// <summary>
/// The mana rules against the real game's data.
///
/// Two jobs. The first is drift: <c>DK2.Simulation.Tests.Dk2TerrainFixture</c>
/// transcribes the two vault terrains and three mana rates, and a transcription
/// can be wrong. The second is the claim <see cref="ManaRules"/> rests on — that
/// mana income <i>is</i> territory, because the only terrains carrying a
/// non-zero <c>ManaGain</c> are exactly the ones a keeper holds and has not
/// built on. That claim is worth nothing unless the shipped table really does
/// partition that way, so it is asserted here by exhaustion rather than assumed.
///
/// Skips when no installation is configured (DK2_DIR).
/// </summary>
public class ManaOnRealDataTests
{
    private static GameDatabase? RealDatabase()
    {
        string? install = Dk2Install.Find();
        return install is null ? null : GameDatabaseLoader.Load(install);
    }

    [Fact]
    public void TheManaVariablesFixture_MatchesTheInstall()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return; // skipped: no DK2 install

        Assert.Equal(200000, db[VariableTypeId.MaximumManaThreshold]);
        Assert.Equal(500, db[VariableTypeId.MaximumManaGainPerSecond]);
        Assert.Equal(5, db[VariableTypeId.ExcessManaDecreaseRatePerSecond]);
    }

    [Fact]
    public void TheVaultTerrainFixture_MatchesTheInstall()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        TerrainDef vault = db.TerrainById(31);
        Assert.Equal("Mana Vault", vault.Name);
        Assert.Equal(0, vault.ManaGain);
        Assert.Equal(32, vault.MaxHealthTypeTerrainId);
        Assert.False(vault.IsOwnable);

        TerrainDef claimed = db.TerrainById(32);
        Assert.Equal("Claimed Vault", claimed.Name);
        Assert.Equal(100, claimed.ManaGain);
        Assert.Equal(2000, claimed.StartingHealth);
        Assert.Equal(31, claimed.DestroyedTypeTerrainId);
        Assert.True(claimed.IsOwnable);

        Assert.Equal(1, db.TerrainById(8).ManaGain);  // Claimed Path
        Assert.Equal(1, db.TerrainById(9).ManaGain);  // Reinforced Wall
    }

    /// <summary>
    /// <b>Mana income is territory, and the data says so by exhaustion.</b>
    /// Three terrain types out of 41 pay anything, and all three are ownable,
    /// none of them is a room, and between them they are every kind of ground a
    /// keeper can hold without building on it — floor, wall and vault.
    /// </summary>
    [Fact]
    public void OnlyHeldGroundGeneratesMana()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        string[] paying = [.. db.Terrain.Values
            .Where(t => t.ManaGain > 0)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => t.Name)];

        Assert.Equal(["Claimed Path", "Claimed Vault", "Reinforced Wall"], paying);

        foreach (TerrainDef t in db.Terrain.Values.Where(t => t.ManaGain > 0))
        {
            Assert.True(t.IsOwnable, $"{t.Name} pays mana but cannot be owned");
            Assert.False(t.IsRoom, $"{t.Name} pays mana and is a room");
        }
    }

    /// <summary>No room in the game generates mana, which is why building costs income.</summary>
    [Fact]
    public void NoRoomGeneratesMana()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        foreach (RoomDef room in db.Rooms.Values)
            Assert.Equal(0, db.TerrainById(room.TerrainId).ManaGain);
    }

    /// <summary>
    /// Pins the negative result behind Q37: <c>MaxManaGain</c> is non-zero on
    /// exactly one terrain in the whole table, where it merely repeats
    /// <c>ManaGain</c>. One data point is not enough to tell whether it is a
    /// contribution to a keeper's mana ceiling, a cap on the tile's own output,
    /// or something else, so nothing reads it. If this test ever fails, the
    /// field has more to say than it does today and Q37 is worth reopening.
    /// </summary>
    [Fact]
    public void MaxManaGainIsSetOnExactlyOneTerrain_WhichIsWhyNothingReadsIt()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        TerrainDef[] set = [.. db.Terrain.Values.Where(t => t.MaxManaGain > 0)];

        TerrainDef only = Assert.Single(set);
        Assert.Equal("Claimed Vault", only.Name);
        Assert.Equal(only.ManaGain, only.MaxManaGain);
    }

    /// <summary>
    /// <b>The cap is not decorative.</b> <c>MaximumManaGainPerSecond</c> is 500
    /// against one mana per claimed tile, so it bites at 500 claimed tiles —
    /// which is a large dungeon but not an impossible one on a shipped map.
    /// That is the check that the per-second reading of the terrain field is the
    /// right order of magnitude: a per-tick reading would put the cap somewhere
    /// nobody could ever reach.
    /// </summary>
    [Fact]
    public void TheGainCapIsReachableOnAShippedMap()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        int cap = db[VariableTypeId.MaximumManaGainPerSecond];
        int perClaimedTile = db.TerrainById(8).ManaGain;
        int tilesToReachIt = cap / perClaimedTile;

        string? install = Dk2Install.Find();
        LevelMap? map = install is null ? null : GameDatabaseLoader.LoadCampaignMap(install, "level1");
        if (map is null) return;

        Assert.True(
            map.Width * map.Height > tilesToReachIt,
            $"level1 has {map.Width * map.Height} tiles, fewer than the {tilesToReachIt} " +
            "a keeper would need to claim to reach the gain cap");
    }

    /// <summary>
    /// A keeper's income on a shipped level is the sum over the tiles they hold,
    /// computed by the same code the simulation runs. Nothing here asserts a
    /// number DK2 would print — it asserts the rule reaches the real map at all,
    /// and that a level which starts a keeper with land starts them with income.
    /// </summary>
    [Fact]
    public void AKeepersIncomeOnARealCampaignLevelIsTheirClaimedLand()
    {
        string? install = Dk2Install.Find();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, "level1");
        if (map is null) return;

        byte[] players = [.. map.Tiles.Select(t => t.PlayerId).Distinct().OrderBy(id => id)];
        WorldState world = WorldState.FromMap(db, map, players);

        int[] gain = ManaRules.GainPerSecondByPlayerIndex(world, db);

        for (int i = 0; i < world.PlayerCount; i++)
        {
            byte player = world.PlayerAtIndex(i).PlayerId;
            int expected = 0;
            for (int t = 0; t < world.TileCount; t++)
            {
                ref TileState tile = ref world.TileAtIndex(t);
                if (tile.OwnerPlayerId == player)
                    expected += db.TerrainById(tile.TerrainId).ManaGain;
            }

            Assert.Equal(Math.Min(expected, db[VariableTypeId.MaximumManaGainPerSecond]), gain[i]);
        }

        Assert.Contains(gain, g => g > 0);
    }
}
