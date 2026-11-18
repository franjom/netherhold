// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// Mana accounting against shipped terrain definitions, including the bridge
/// bonus that is absent from their ManaGain fields. InstallFact skips without an install.
/// </summary>
public class ManaOnRealDataTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public ManaOnRealDataTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;
    private static GameDatabase RealDatabase() =>
        GameDatabaseLoader.Load(Dk2InstallGate.Path);
    private static WorldState OneTileWorld(GameDatabase db, byte terrainId, byte owner) =>
        WorldState.FromMap(db, new LevelMap(1, 1, [new MapTile(terrainId, owner, 0, 0)]),
            [(byte)0, PlayerIds.FirstKeeper, (byte)4, PlayerIds.Neutral]);

    private static int KeeperGain(WorldState world, GameDatabase db) =>
        ManaRules.GainPerSecondByPlayerIndex(world, db)[Enumerable.Range(0, world.PlayerCount)
            .Single(i => world.PlayerAtIndex(i).PlayerId == PlayerIds.FirstKeeper)];

    [InstallFact]
    public void BuiltBridgeEarnsOnePerSecond_AndRemovalRestoresWaterWithoutIncome()
    {
        GameDatabase db = RealDatabase();
        WorldState world = OneTileWorld(db, db.WaterTerrain!.TerrainId, PlayerIds.Neutral);
        ref TileState tile = ref world.Tile(0, 0);
        RoomRules.Place(world, db, ref tile, db.Room(RoomTypeId.WoodenBridge), PlayerIds.FirstKeeper);
        Assert.Equal(1, KeeperGain(world, db));
        Assert.Equal(0, world.Player((byte)0).TerrainManaGainPerSecond);

        RoomRules.Unmake(world, db, ref tile);

        Assert.Equal(db.WaterTerrain.TerrainId, tile.TerrainId);
        Assert.Equal(0, KeeperGain(world, db));
    }

    [InstallFact]
    public void BridgeTypeReplacementAddsAnotherBonus_ButSameTerrainDoesNot()
    {
        GameDatabase db = RealDatabase();
        WorldState world = OneTileWorld(db, db.WaterTerrain!.TerrainId, PlayerIds.Neutral);
        ref TileState tile = ref world.Tile(0, 0);
        RoomDef wooden = db.Room(RoomTypeId.WoodenBridge);
        RoomDef stone = db.Room(RoomTypeId.StoneBridge);
        RoomRules.Place(world, db, ref tile, wooden, PlayerIds.FirstKeeper);
        ManaRules.SetTerrain(world, db, ref tile, wooden.TerrainId);
        Assert.Equal(1, KeeperGain(world, db));

        ManaRules.SetTerrain(world, db, ref tile, stone.TerrainId);
        Assert.Equal(2, KeeperGain(world, db));
        ManaRules.SetTerrain(world, db, ref tile, stone.TerrainId);
        Assert.Equal(2, KeeperGain(world, db));
        RoomRules.Unmake(world, db, ref tile);
        Assert.Equal(1, KeeperGain(world, db));
    }

    [InstallFact]
    public void BridgeTerrainWrittenBeforeOwnershipCreditsTagZero()
    {
        GameDatabase db = RealDatabase();
        WorldState world = OneTileWorld(db, db.WaterTerrain!.TerrainId, PlayerIds.Neutral);
        ref TileState tile = ref world.Tile(0, 0);
        ManaRules.SetOwner(world, db, ref tile, (byte)0);
        ManaRules.SetTerrain(world, db, ref tile, db.Room(RoomTypeId.WoodenBridge).TerrainId);
        Assert.Equal(1, world.Player((byte)0).TerrainManaGainPerSecond);
        Assert.Equal(0, KeeperGain(world, db));

        ManaRules.SetOwner(world, db, ref tile, PlayerIds.FirstKeeper);

        Assert.Equal(1, world.Player((byte)0).TerrainManaGainPerSecond);
        Assert.Equal(0, KeeperGain(world, db));
    }

    [InstallFact]
    public void CapturedBridgeLeavesItsBonusBehind_AndRemovalDebitsTheCapturingKeeper()
    {
        GameDatabase db = RealDatabase();
        WorldState world = OneTileWorld(db, db.WaterTerrain!.TerrainId, PlayerIds.Neutral);
        ref TileState tile = ref world.Tile(0, 0);
        RoomRules.Place(world, db, ref tile, db.Room(RoomTypeId.WoodenBridge), PlayerIds.FirstKeeper);
        ManaRules.SetOwner(world, db, ref tile, (byte)4);
        Assert.Equal(1, KeeperGain(world, db));
        Assert.Equal(0, world.Player((byte)4).TerrainManaGainPerSecond);

        RoomRules.Unmake(world, db, ref tile);

        Assert.Equal(1, KeeperGain(world, db));
        Assert.Equal(-1, world.Player((byte)4).TerrainManaGainPerSecond);
    }

    [InstallFact]
    public void MapLaidOwnedBridgeCreditsItsKeeper()
    {
        GameDatabase db = RealDatabase();
        WorldState world = OneTileWorld(db, db.Room(RoomTypeId.WoodenBridge).TerrainId,
            PlayerIds.FirstKeeper);

        Assert.Equal(0, world.Player((byte)0).TerrainManaGainPerSecond);
        Assert.Equal(1, KeeperGain(world, db));
    }

    [InstallFact]
    public void TheManaVariablesFixture_MatchesTheInstall()
    {
        GameDatabase db = RealDatabase();

        Assert.Equal(200000, db[VariableTypeId.MaximumManaThreshold]);
        Assert.Equal(500, db[VariableTypeId.MaximumManaGainPerSecond]);
        Assert.Equal(5, db[VariableTypeId.ExcessManaDecreaseRatePerSecond]);
    }

    /// <summary>
    /// <b>Claimed Vault's 100 is CONFIRMED in the game</b> (owner, 2026-08-30):
    /// "each claimed mana vault raises mana production by +100". A vault a keeper
    /// has not taken pays nothing, which the pair of terrain types says in the
    /// data and the observation says on screen.
    ///
    /// <para>Nothing had to change for it — <see cref="ManaRules.GainPerSecond"/>
    /// reads <see cref="TerrainDef.ManaGain"/> off whatever terrain a tile is, so
    /// a vault has always paid its 100 here. Worth recording anyway, because it
    /// is the first time the <i>size</i> of a mana rate has been checked against
    /// the original rather than only its sign: a green figure that moves by
    /// exactly the tabled number is what makes "the unit is per second" more than
    /// an inference (Q39).</para>
    /// </summary>
    [InstallFact]
    public void TheVaultTerrainFixture_MatchesTheInstall()
    {
        GameDatabase db = RealDatabase();

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
    [InstallFact]
    public void OnlyHeldGroundGeneratesMana()
    {
        GameDatabase db = RealDatabase();

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

    /// <summary>
    /// <b>The data agrees that a keeper only ever has one dungeon heart.</b>
    /// A keeper has exactly one heart and loses the game when it falls
    /// (CONFIRMED — observed in the original), and the ROOMS record backs that
    /// up from its own side: the heart is the one room a keeper starts with that
    /// carries no <c>Buildable</c> flag, so there is no way to acquire a second.
    /// That is what licenses `ManaRules` to count the heart's 30 a second once
    /// per keeper rather than once per heart, without a room-instance concept.
    /// </summary>
    [InstallFact]
    public void ADungeonHeartCannotBeBuilt_WhichIsWhyAKeeperOnlyEverHasOne()
    {
        GameDatabase db = RealDatabase();

        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        Assert.False(heart.IsBuildable);
        Assert.Equal(30, db[VariableTypeId.DungeonHeartManaGenerationIncreasePerSecond]);

        // And the 30 is the keeper's, not the tile's: heart terrain generates
        // nothing of its own, and a heart is laid by the map, never built over
        // paid floor (ADR-0334).
        Assert.Equal(0, db.TerrainById(heart.TerrainId).ManaGain);
        Assert.Equal(heart.TerrainId, ManaRules.DungeonHeartTerrainId(db));
    }

    /// <summary>No room terrain generates mana of its own; a built room keeps the floor's gain
    /// on the rate word instead (ADR-0334), so building costs no income.</summary>
    [InstallFact]
    public void NoRoomGeneratesMana()
    {
        GameDatabase db = RealDatabase();

        foreach (RoomDef room in db.Rooms.Values)
            Assert.Equal(0, db.TerrainById(room.TerrainId).ManaGain);
    }

    /// <summary>
    /// Pins the negative result behind Q37a. <c>MaxManaGain</c> is non-zero on
    /// exactly one terrain in the whole table, where it merely repeats
    /// <c>ManaGain</c> — one data point, which was never enough to say what the
    /// field means. The one reading that would have given it a job, a
    /// contribution to a keeper's mana ceiling, is refuted: the ceiling is
    /// 200000 from the first second and never moves. So nothing reads it, and
    /// nothing should. If this test ever fails, a data set has given the field
    /// more to say than the shipped one does and the question is worth
    /// reopening — on that evidence, not on the field's name.
    /// </summary>
    [InstallFact]
    public void MaxManaGainIsSetOnExactlyOneTerrain_WhichIsWhyNothingReadsIt()
    {
        GameDatabase db = RealDatabase();

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
    [InstallFact]
    public void TheGainCapIsReachableOnAShippedMap()
    {
        GameDatabase db = RealDatabase();

        int cap = db[VariableTypeId.MaximumManaGainPerSecond];
        int perClaimedTile = db.TerrainById(8).ManaGain;
        int tilesToReachIt = cap / perClaimedTile;

        string install = Dk2InstallGate.Path;
        LevelMap? map = install is null ? null : GameDatabaseLoader.LoadCampaignMap(install, "level1");
        Assert.NotNull(map);

        Assert.True(
            map.Width * map.Height > tilesToReachIt,
            $"level1 has {map.Width * map.Height} tiles, fewer than the {tilesToReachIt} " +
            "a keeper would need to claim to reach the gain cap");
    }

    /// <summary>
    /// Checks imported terrain and bridge income against shipped maps.
    /// Logged gross and upkeep are clone diagnostics, not native observations.
    /// </summary>
    [InstallTheory]
    [InlineData("level1")]
    [InlineData("level8")]
    public void AKeepersInitialIncomeIncludesClaimedLandAndMapBridges(string level)
    {
        string install = Dk2InstallGate.Path;

        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
        Assert.NotNull(map);

        byte[] players = [.. map.Tiles.Select(t => t.PlayerId).Distinct().OrderBy(id => id)];
        WorldState world = WorldState.FromMap(db, map, players);

        ThingProjection.Place(world, db, GameDatabaseLoader.LoadCampaignThings(install, level)!);
        int[] upkeep = ManaRules.UpkeepPerSecondByPlayerIndex(world, db);
        int[] gain = ManaRules.GainPerSecondByPlayerIndex(world, db);
        byte heartTerrain = Assert.NotNull(ManaRules.DungeonHeartTerrainId(db));

        for (int i = 0; i < world.PlayerCount; i++)
        {
            byte player = world.PlayerAtIndex(i).PlayerId.Value;
            int expected = 0;
            int bridges = 0;
            int capBonus = 0;
            bool heart = false;
            for (int t = 0; t < world.TileCount; t++)
            {
                ref TileState tile = ref world.TileAtIndex(t);
                if (tile.OwnerPlayerId != player) continue;
                expected += db.TerrainById(tile.TerrainId).ManaGain;
                capBonus += db.TerrainById(tile.TerrainId).MaxManaGain;
                if (tile.TerrainId is 17 or 26) bridges++;
                if (tile.TerrainId == heartTerrain) heart = true;
            }

            // The heart is a flat per-keeper amount however many tiles it covers,
            // which on a shipped level is 25 of them.
            if (heart) expected += db[VariableTypeId.DungeonHeartManaGenerationIncreasePerSecond];

            Assert.Equal(Math.Min(expected + bridges,
                db[VariableTypeId.MaximumManaGainPerSecond] + capBonus), gain[i]);
            _output.WriteLine($"{level} player={player}: terrainAndHeart={expected}, bridges={bridges}, " +
                $"gross={gain[i]}, upkeep={upkeep[i]}, net={gain[i] - upkeep[i]}");
        }

        Assert.Contains(gain, g => g > 0);
    }
}
