// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

/// <summary>
/// Covers the seam between the file parsers and the simulation: one immutable
/// database, loaded once, that everything downstream reads.
///
/// Skips when no Dungeon Keeper 2 installation is configured (DK2_DIR).
/// </summary>
public class GameDatabaseTests
{
    private static GameDatabase Database() =>
        GameDatabaseLoader.Load(Dk2InstallGate.Path);

    [InstallFact]
    public void Load_ReadsEveryDefinitionTable()
    {
        GameDatabase db = Database();

        Assert.Equal(41, db.Terrain.Count);
        Assert.Equal(26, db.Rooms.Count);
        Assert.Equal(46, db.Creatures.Count);
        Assert.Equal(13, db.KeeperSpells.Count);
        Assert.Equal(145, db.Objects.Count);
        Assert.Equal(8, db.Doors.Count);
        Assert.Equal(16, db.Traps.Count);
        Assert.Equal(41, db.CreatureSpells.Count);
        Assert.Equal(65, db.Shots.Count);
        Assert.Equal(370, db.Effects.Count);
        Assert.Equal(596, db.EffectElements.Count);
        Assert.NotEmpty(db.Variables.Misc);
    }

    [InstallFact]
    public void Definitions_CrossReferenceCorrectly()
    {
        GameDatabase db = Database();

        // Every room lays down a terrain type that exists, and the room that
        // spawns skeletons is the Prison. Both facts already hold at the parser
        // level; asserting them here proves the database preserved them.
        foreach (RoomDef room in db.Rooms.Values)
            Assert.True(db.Terrain.ContainsKey(room.TerrainId),
                $"{room.Name} lays unknown terrain id {room.TerrainId}");

        RoomDef prison = db.Room(RoomTypeId.Prison);
        Assert.Equal("Skeleton", db.CreatureById(prison.CreatedCreatureId).Name);
    }

    [InstallFact]
    public void BalanceNumbers_AreReachableByName()
    {
        GameDatabase db = Database();

        // The digging and claiming deltas the M3 mechanics will read. Values
        // CONFIRMED in docs/research/kwd-variables.md; the point of the test is
        // that simulation code can reach them without hardcoding anything.
        Assert.Equal(-300, db[VariableTypeId.MineGoldHealth]);
        Assert.Equal(825, db[VariableTypeId.ClaimTileHealth]);
        Assert.Equal(-5000, db[VariableTypeId.DigRockHealth]);
        Assert.Equal(50, db[VariableTypeId.ReinforceWallHealth]);
    }

    [InstallFact]
    public void MissingVariable_FailsLoudly()
    {
        GameDatabase db = Database();

        // Silently returning 0 for an absent balance number would be a very
        // quiet way to invent DK2 behaviour.
        Assert.Throws<KeyNotFoundException>(() => db[(VariableTypeId)9999]);
    }

    [InstallFact]
    public void LookupByName_FindsKnownDefinitions()
    {
        GameDatabase db = Database();

        Assert.NotNull(db.CreatureByName("Imp"));
        Assert.NotNull(db.RoomByName("Treasury"));
        Assert.Null(db.CreatureByName("Nonexistent Creature"));
    }

    [InstallFact]
    public void UnknownIds_ThrowRatherThanReturningNull()
    {
        GameDatabase db = Database();

        Assert.Throws<KeyNotFoundException>(() => db.TerrainById(200));
        Assert.Throws<KeyNotFoundException>(() => db.RoomById(200));
        Assert.Throws<KeyNotFoundException>(() => db.CreatureById(200));
    }

    [InstallFact]
    public void LoadCampaignMap_ReadsTheFirstLevel()
    {
        string install = Dk2InstallGate.Path;

        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, "level1");

        Assert.NotNull(map);
        Assert.NotNull(map);
        Assert.Equal(30, map.Width);
        Assert.Equal(38, map.Height);
        Assert.Equal(30 * 38, map.Tiles.Count);
    }

    [InstallFact]
    public void LoadCampaignMap_ReturnsNullForAnUnknownLevel()
    {
        string install = Dk2InstallGate.Path;

        Assert.Null(GameDatabaseLoader.LoadCampaignMap(install, "no_such_level"));
    }
}
