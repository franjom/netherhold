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
    [Fact]
    public void RuntimeTileUsesTheWritersTenBytesStartingAtRoomWord()
    {
        // A Level 10 portal tile: the original writer copies live tile +2..+B.
        // Its room and owner words carry flags above the low twelve-bit ids.
        byte[] record = [0x65, 0x24, 0, 0, 0x10, 0x27, 0x04, 0x20, 12, 0];
        SavedTile tile = Assert.Single(Dk2SaveReader.ReadTiles(record, 0, 1, 1));
        LevelMap map = Dk2SaveReader.ReadGrid(record, 0, 1, 1);

        Assert.Equal((ushort)1125, tile.RoomId);
        Assert.Equal((ushort)10000, tile.Health);
        Assert.Equal((byte)4, tile.OwnerPlayerId);
        Assert.Equal((byte)12, tile.TerrainId);
        Assert.Equal((byte)4, map.Tiles[0].PlayerId);
    }

    private static string FindInstall() => Dk2InstallGate.Path;

    private static (string Install, string Save)? FirstSave()
    {
        string install = FindInstall();
        if (install is null) return null;

        IReadOnlyList<string> saves = Dk2SaveReader.EnumerateSaves(install);
        return saves.Count == 0 ? null : (install, saves[0]);
    }

    [InstallFact]
    public void OwnersLevel10CaptureHasTheWritersGridAlignment()
    {
        string install = FindInstall();
        string save = Path.Combine(Dk2SaveReader.SaveDirectory(install), "woodsong_ai.SAV");
        if (!File.Exists(save)) return;

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        Assert.Equal("level10", loaded.Header.LevelName, ignoreCase: true);
        Assert.Equal(2408u, loaded.Header.GameTick);
        Assert.Equal(0xE7DA, loaded.GridOffset);
        Assert.Equal(1.0, loaded.TerrainAgreement);

        int portal = (4 * loaded.Map.Width) + 26;
        Assert.Equal((ushort)1125, loaded.Tiles[portal].RoomId);
        Assert.Equal((byte)4, loaded.Tiles[portal].OwnerPlayerId);
        Assert.Equal((byte)12, loaded.Tiles[portal].TerrainId);
    }

    [InstallFact]
    public void TrapTimeStoneBridgesCarryTheirOwnRoomIds()
    {
        string install = FindInstall();
        string save = Path.Combine(Dk2SaveReader.SaveDirectory(install), "fow_3.SAV");
        if (!File.Exists(save)) return;

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        Assert.Equal("TrapTime", loaded.Header.LevelName);
        Assert.Equal(0x14878, loaded.GridOffset);
        int row = 38 * loaded.Map.Width;
        Assert.Equal((ushort)1117, loaded.Tiles[row + 72].RoomId);
        Assert.Equal((ushort)1116, loaded.Tiles[row + 76].RoomId);
        Assert.Equal((ushort)0, loaded.Tiles[row + 73].RoomId);
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
            $"reading one byte on still agreed on {one:P1} of tiles, so terrain lane 8 and the " +
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
    public void Owner_WordsHaveHighFlagsButDecodeToPlayerIds()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        LevelMap design = GameDatabaseLoader.LoadCampaignMap(install, loaded.Header.LevelName)!;

        // The tile writer emits a u16 owner word, not a byte. High bits are
        // common in saves; the low twelve bits must agree with the map API.
        Assert.Equal(design.Width * design.Height, loaded.Tiles.Length);
        Assert.Contains(loaded.Tiles, tile =>
            (System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(tile.Raw.Span[6..]) & 0xF000) != 0);
        Assert.Equal(loaded.Tiles[^1].OwnerPlayerId, loaded.Map.Tiles[^1].PlayerId);
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

        // Most untouched tiles retain StartingHealth. The lower bound allows
        // damage and ongoing work while rejecting a wrong pair of record lanes.
        int matched = 0, total = 0;
        for (int i = 0; i < loaded.Tiles.Length; i++)
        {
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
            "so lanes 4-5 are not the health field this reader takes them for.");
    }

    [InstallFact]
    public void RoomId_GroupsTilesIntoContiguousRoomsOfTheRightSize()
    {
        if (FirstSave() is not var (install, save)) return; // skipped

        SavedGame loaded = Dk2SaveReader.Load(install, save)!;
        int w = loaded.Map.Width;

        var byRoom = new Dictionary<ushort, List<(int X, int Y)>>();

        for (int i = 0; i < loaded.Tiles.Length; i++)
        {
            ushort room = loaded.Tiles[i].RoomId;
            if (room == 0) continue;

            if (!byRoom.TryGetValue(room, out List<(int, int)>? cells))
                byRoom[room] = cells = [];
            cells.Add((i % w, i / w));
        }

        Assert.NotEmpty(byRoom);

        // The low twelve bits of lanes 0-1 identify an instance. Each distinct
        // value must connect by edge steps; room shapes need not be rectangles.
        foreach ((ushort room, List<(int X, int Y)> cells) in byRoom)
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
                + "reachable from the first by edge steps, so lanes 0-1 do not group tiles "
                + "into single rooms.");

            // Single-tile and irregular rooms are legal; size is only a bound.
            Assert.InRange(cells.Count, 1, 25 * 25);
        }
    }
}
