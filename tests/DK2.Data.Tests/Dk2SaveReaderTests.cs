// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Save;

namespace DK2.Data.Tests;

/// <summary>
/// Tests against the reference install's own saves. Skipped automatically when
/// there is no installation, and skipped when the install has no saves — a
/// fresh copy of DK2 has none, and that is not a failure.
///
/// <para>The load's correctness rests on one measurement: a played save differs
/// from its level's design map only where the player dug, claimed or built. A
/// grid read at the right offset and stride therefore agrees with the design
/// map on most tiles, and a wrong read agrees at chance. These tests pin both
/// sides of that.</para>
/// </summary>
public class Dk2SaveReaderTests
{
    private static string? FindInstall() => Dk2Install.Find();

    private static (string Install, string Save)? FirstSave()
    {
        string? install = FindInstall();
        if (install is null) return null;

        IReadOnlyList<string> saves = Dk2SaveReader.EnumerateSaves(install);
        return saves.Count == 0 ? null : (install, saves[0]);
    }

    [Fact]
    public void Header_NamesALevelTheInstallHas_AndTheSupportedBuild()
    {
        if (FirstSave() is not var (install, save)) return; // skipped: no install or no saves

        SavedGame? loaded = Dk2SaveReader.Load(install, save);
        Assert.NotNull(loaded);

        Assert.Equal(220u, loaded.Header.FormatWord);
        Assert.Equal(1u, loaded.Header.Version);
        Assert.Equal(Dk2SaveReader.SupportedBuildStamp, loaded.Header.BuildStamp);
        Assert.True(loaded.Header.IsSupportedBuild);

        // The tick the game was on. A save made after any play at all has one,
        // and it is the field that proves the header offsets are the writer's
        // rather than a lucky fit: it sits at 0x102, immediately after the
        // 64-byte level-name field.
        Assert.True(loaded.Header.GameTick > 0);

        // The level name has to be one the install can supply a grid for, or
        // the dimensions the grid was read at came from nowhere.
        Assert.NotNull(GameDatabaseLoader.LoadCampaignMap(install, loaded.Header.LevelName));
    }

    [Fact]
    public void Grid_MatchesTheLevelsDesignMap_OnMostTiles()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;

        LevelMap design = GameDatabaseLoader.LoadCampaignMap(install, loaded.Header.LevelName)!;
        Assert.Equal(design.Width, loaded.Map.Width);
        Assert.Equal(design.Height, loaded.Map.Height);

        // The measured floor. The sample save sits at 73% and its earlier
        // quicksave at 85%; half is well below both and far above what a
        // misread grid could reach, so it separates the two cases without
        // pinning a number that legitimately moves as the player plays.
        Assert.True(
            loaded.TerrainAgreement > 0.5,
            $"terrain agreed with the design map on only {loaded.TerrainAgreement:P1} of tiles, " +
            "which suggests the grid was read at the wrong offset or stride rather than a busy player.");
    }

    [Fact]
    public void Grid_ReadAtTheWrongOffset_DoesNotAgree()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        LevelMap design = GameDatabaseLoader.LoadCampaignMap(install, loaded.Header.LevelName)!;

        byte[] bytes = File.ReadAllBytes(save);

        // The negative control the previous test needs to mean anything: shift
        // the grid by one tile record and the agreement has to collapse. If it
        // does not, the test above is passing on something other than a correct
        // read.
        var shifted = new byte[bytes.Length];
        Array.Copy(bytes, Dk2SaveReader.TileRecordSize, shifted, 0, bytes.Length - Dk2SaveReader.TileRecordSize);
        Array.Copy(bytes, 0, shifted, 0, Dk2SaveReader.GridOffset);

        LevelMap wrong = Dk2SaveReader.ReadGrid(shifted, design.Width, design.Height);

        Assert.True(
            Dk2SaveReader.AgreementWithDesign(wrong, design) < loaded.TerrainAgreement - 0.1,
            "shifting the grid by one tile did not reduce agreement, so agreement is not measuring alignment.");
    }

    [Fact]
    public void Owner_IsAlwaysAReservedPlayerId()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;

        // DK2 reserves 0 to 7: unowned, good, neutral, then five keepers
        // (PlayerIds). Ownership changes freely in play, so the test that means
        // something is that the lane never produces a value outside that range
        // — a misread lane would.
        foreach (MapTile tile in loaded.Map.Tiles)
            Assert.InRange(tile.PlayerId, PlayerIds.Unowned, (byte)7);
    }

    [Fact]
    public void Owner_MayIncludeIdsTheDesignMapDoesNot()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        LevelMap design = GameDatabaseLoader.LoadCampaignMap(install, loaded.Header.LevelName)!;

        // Pins a real oddity rather than asserting it away. mpd1's design map
        // has no unowned tile at all — every one of its 3,721 tiles belongs to
        // player 1, 2 or 3 — yet both saves of it carry exactly one tile owned
        // by nobody, and it is the last tile of the grid in both. The terrain
        // there reads correctly (impenetrable edge rock), and the alignment
        // tests rule out a shifted read, so this is DK2's own behaviour and not
        // ours. Left alone and recorded under Q181; it is invisible on screen
        // because the tile is unreachable map edge either way.
        var designPlayers = design.Tiles.Select(t => t.PlayerId).ToHashSet();
        var savedPlayers = loaded.Map.Tiles.Select(t => t.PlayerId).ToHashSet();

        foreach (byte extra in savedPlayers.Except(designPlayers))
            Assert.Equal(PlayerIds.Unowned, extra);
    }

    [Fact]
    public void EveryTerrainId_IsOneTheDatabaseDefines()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        GameDatabase database = GameDatabaseLoader.Load(install);

        // A wrong lane would produce ids the TERRAIN record has no row for.
        // This is a second, independent check on the same alignment claim.
        foreach (MapTile tile in loaded.Map.Tiles)
            Assert.NotNull(database.TerrainById(tile.TerrainId));
    }

    [Fact]
    public void EveryInstalledSave_Loads()
    {
        string? install = FindInstall();
        if (install is null) return; // skipped

        foreach (string save in Dk2SaveReader.EnumerateSaves(install))
        {
            SavedGame? loaded = Dk2SaveReader.Load(install, save);
            Assert.NotNull(loaded);
            Assert.True(
                loaded.TerrainAgreement > 0.5,
                $"{Path.GetFileName(save)} agreed on only {loaded.TerrainAgreement:P1} of tiles.");
        }
    }

    [Fact]
    public void Health_MatchesTheTerrainTablesStartingHealth_OnUndamagedTiles()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        GameDatabase database = GameDatabaseLoader.Load(install);

        // Lanes 6-7 are the tile's health, and this is what identified them: a
        // tile nobody has touched carries its terrain's StartingHealth from
        // Terrain.kwd exactly. Measured at 91.9% on a freshly started dungeon
        // and 82% on a long-played one — the remainder being tiles mid-dig,
        // mid-claim or mid-reinforce, which is what health is *for*.
        //
        // Two thirds is well under both and far above anything a wrong pair of
        // lanes could reach, since it would have to agree with a 41-row table
        // it knows nothing about.
        int matched = 0, total = 0;
        int scratch = Dk2SaveReader.ScratchSlotIndex(loaded.Map.Width, loaded.Map.Height);

        for (int i = 0; i < loaded.Tiles.Length; i++)
        {
            if (i == scratch) continue;

            SavedTile tile = loaded.Tiles[i];
            TerrainDef? terrain = database.TerrainById(tile.TerrainId);
            if (terrain is null) continue;

            total++;
            if (tile.Health == terrain.StartingHealth) matched++;
        }

        Assert.True(total > 3000, $"expected a full grid, counted {total} tiles.");
        Assert.True(
            (double)matched / total > 0.66,
            $"only {matched}/{total} tiles carried their terrain's starting health, " +
            "so lanes 6-7 are not the health field this reader takes them for.");
    }

    [Fact]
    public void RoomId_GroupsTilesIntoContiguousRoomsOfTheRightSize()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        int w = loaded.Map.Width;

        var byRoom = new Dictionary<byte, List<(int X, int Y)>>();
        int scratch = Dk2SaveReader.ScratchSlotIndex(w, loaded.Map.Height);

        for (int i = 0; i < loaded.Tiles.Length; i++)
        {
            // The last slot is not a tile and carries a non-zero lane 2 of its
            // own, which would otherwise read as a one-tile room. Finding it
            // here is how its lane 2 was shown to be scratch rather than a room
            // id at all.
            if (i == scratch) continue;

            byte room = loaded.Tiles[i].RoomId;
            if (room == 0) continue;

            if (!byRoom.TryGetValue(room, out List<(int, int)>? cells))
                byRoom[room] = cells = [];
            cells.Add((i % w, i / w));
        }

        Assert.NotEmpty(byRoom);

        // What identified lane 2: every distinct value is one solid rectangle.
        // A byte that merely correlated with rooms would not be — it would
        // scatter, or lump every room of a type together. In the purpose-built
        // save the ten instances come out as a 2x2 hero gate, four 3x3 portals
        // and five 5x5 rooms, each exactly filling its box.
        foreach ((byte room, List<(int X, int Y)> cells) in byRoom)
        {
            int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X);
            int minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
            int area = (maxX - minX + 1) * (maxY - minY + 1);

            Assert.True(
                cells.Count == area,
                $"room {room} covers {cells.Count} tiles but spans a {maxX - minX + 1}x{maxY - minY + 1} " +
                "box, so lane 2 does not group tiles into solid rooms.");

            // No room in DK2 is a single tile, and none of the shipped ones is
            // larger than its footprint allows.
            Assert.InRange(cells.Count, 4, 25 * 25);
        }
    }
}
