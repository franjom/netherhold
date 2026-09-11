// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Save;
using DK2.TestSupport;

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
    private static string FindInstall() => Dk2InstallGate.Path;

    private static (string Install, string Save)? FirstSave()
    {
        string install = FindInstall();
        if (install is null) return null;

        IReadOnlyList<string> saves = Dk2SaveReader.EnumerateSaves(install);
        return saves.Count == 0 ? null : (install, saves[0]);
    }

    [InstallFact]
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

    [InstallFact]
    public void Grid_MatchesTheLevelsDesignMap_OnMostTiles()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;

        LevelMap design = GameDatabaseLoader.LoadCampaignMap(install, loaded.Header.LevelName)!;
        Assert.Equal(design.Width, loaded.Map.Width);
        Assert.Equal(design.Height, loaded.Map.Height);

        // The measured floor. Across the fourteen saves in the reference
        // install the located offset scores between 72.9% and 100%, and one
        // tile record either side scores 59% to 91% - so half is below every
        // real read and above most misreads, without pinning a number that
        // legitimately moves as the player plays. The neighbouring offsets
        // score as high as they do because a map has long runs of one terrain;
        // that is why the byte-level control below is the sharper one.
        Assert.True(
            loaded.TerrainAgreement > 0.5,
            $"terrain agreed with the design map on only {loaded.TerrainAgreement:P1} of tiles, " +
            "which suggests the grid was read at the wrong offset or stride rather than a busy player.");
    }

    [InstallFact]
    public void Grid_ReadAtTheWrongOffset_DoesNotAgree()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        LevelMap design = GameDatabaseLoader.LoadCampaignMap(install, loaded.Header.LevelName)!;

        byte[] bytes = File.ReadAllBytes(save);

        // The negative control the previous test needs to mean anything: read
        // the grid one tile record further on and the agreement has to collapse.
        // If it does not, the test above is passing on something other than a
        // correct read - and since the offset is now searched for rather than
        // fixed, this is also the check that the search found a peak and not a
        // plateau.
        LevelMap offByATile = Dk2SaveReader.ReadGrid(
            bytes,
            loaded.GridOffset + Dk2SaveReader.TileRecordSize,
            design.Width,
            design.Height);

        double tile = Dk2SaveReader.AgreementWithDesign(offByATile, design);
        Assert.True(
            tile < loaded.TerrainAgreement - 0.05,
            $"reading one tile record on agreed {tile:P1} against {loaded.TerrainAgreement:P1} at the " +
            "located offset, so agreement is not measuring alignment.");

        // And the sharper half of the control: one BYTE on, which breaks the
        // ten-byte stride rather than the tile index. Measured at 0.0% on every
        // save in the reference install - a shifted lane is reading a different
        // field of a different tile and cannot agree with anything.
        LevelMap offByAByte = Dk2SaveReader.ReadGrid(
            bytes, loaded.GridOffset + 1, design.Width, design.Height);

        double one = Dk2SaveReader.AgreementWithDesign(offByAByte, design);
        Assert.True(
            one < 0.05,
            $"reading one byte on still agreed on {one:P1} of tiles, so lane 0 and the " +
            $"{Dk2SaveReader.TileRecordSize}-byte stride are not what this reader takes them for.");
    }

    [InstallFact]
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

    [InstallFact]
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

    [InstallFact]
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

    [InstallFact]
    public void EveryInstalledSave_Loads()
    {
        string install = FindInstall();

        foreach (string save in Dk2SaveReader.EnumerateSaves(install))
        {
            SavedGame? loaded = Dk2SaveReader.Load(install, save);
            Assert.NotNull(loaded);
            Assert.True(
                loaded.TerrainAgreement > 0.5,
                $"{Path.GetFileName(save)} agreed on only {loaded.TerrainAgreement:P1} of tiles.");
        }
    }

    [InstallFact]
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

    [InstallFact]
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

        // What identifies lane 2: every distinct value is one CONNECTED region.
        // It asserted "one solid rectangle" until 2026-09-11, when a played My
        // Pet Dungeon save produced a room of 46 tiles in a 9x8 box. Rooms are
        // built tile by tile, so a box was never the property to assert -- and
        // this is the second time this test generalised from one purpose-built
        // save. A byte merely correlating with rooms would scatter instead.
        foreach ((byte room, List<(int X, int Y)> cells) in byRoom)
        {
            var members = new HashSet<(int X, int Y)>(cells);
            var reached = new HashSet<(int X, int Y)>();
            var pending = new Stack<(int X, int Y)>();
            pending.Push(cells[0]);
            while (pending.Count > 0)
            {
                (int x, int y) = pending.Pop();
                if (!reached.Add((x, y))) continue;
                if (members.Contains((x + 1, y))) pending.Push((x + 1, y));
                if (members.Contains((x - 1, y))) pending.Push((x - 1, y));
                if (members.Contains((x, y + 1))) pending.Push((x, y + 1));
                if (members.Contains((x, y - 1))) pending.Push((x, y - 1));
            }

            Assert.True(
                reached.Count == cells.Count,
                $"room {room} covers {cells.Count} tiles of which only {reached.Count} are "
                + "reachable from the first by edge steps, so lane 2 does not group tiles "
                + "into single rooms.");

            // A room may be ONE tile: terrain 37 is the shipped "Hero Gate
            // [ 3x1 ]", and the owner's save carries two 1x1 rooms on the row
            // where they bridged lava. Which tile of a bridge holds the id is
            // odd enough to be Q200 rather than asserted away here. The
            // connectivity check above is the real test of lane 2; size is a
            // sanity bound and is treated as one.
            Assert.InRange(cells.Count, 1, 25 * 25);
        }
    }
}
