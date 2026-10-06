// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

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
/// Gated with [InstallFact], so it reports SKIPPED without an installation rather than passing.
/// </summary>
public class ManaOnRealDataTests
{
    private static GameDatabase RealDatabase() =>
        GameDatabaseLoader.Load(Dk2InstallGate.Path);

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
    /// A keeper's income on a shipped level is the sum over the tiles they hold,
    /// computed by the same code the simulation runs. Nothing here asserts a
    /// number DK2 would print — it asserts the rule reaches the real map at all,
    /// and that a level which starts a keeper with land starts them with income.
    /// </summary>
    [InstallFact]
    public void AKeepersIncomeOnARealCampaignLevelIsTheirClaimedLand()
    {
        string install = Dk2InstallGate.Path;

        GameDatabase db = GameDatabaseLoader.Load(install);
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, "level1");
        Assert.NotNull(map);

        byte[] players = [.. map.Tiles.Select(t => t.PlayerId).Distinct().OrderBy(id => id)];
        WorldState world = WorldState.FromMap(db, map, players);

        int[] gain = ManaRules.GainPerSecondByPlayerIndex(world, db);
        byte heartTerrain = Assert.NotNull(ManaRules.DungeonHeartTerrainId(db));

        for (int i = 0; i < world.PlayerCount; i++)
        {
            byte player = world.PlayerAtIndex(i).PlayerId.Value;
            int expected = 0;
            bool heart = false;
            for (int t = 0; t < world.TileCount; t++)
            {
                ref TileState tile = ref world.TileAtIndex(t);
                if (tile.OwnerPlayerId != player) continue;
                expected += db.TerrainById(tile.TerrainId).ManaGain;
                if (tile.TerrainId == heartTerrain) heart = true;
            }

            // The heart is a flat per-keeper amount however many tiles it covers,
            // which on a shipped level is 25 of them.
            if (heart) expected += db[VariableTypeId.DungeonHeartManaGenerationIncreasePerSecond];

            Assert.Equal(Math.Min(expected, db[VariableTypeId.MaximumManaGainPerSecond]), gain[i]);
        }

        Assert.Contains(gain, g => g > 0);
    }
}
