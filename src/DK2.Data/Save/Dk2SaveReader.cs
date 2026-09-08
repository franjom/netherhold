// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Save;

/// <summary>
/// Reads the tile grid out of a Dungeon Keeper 2 saved game — <c>.SAV</c> or
/// <c>.QSAV</c> — into the same <see cref="LevelMap"/> the KWD loader produces.
///
/// <para><b>Terrain and ownership only.</b> What follows the grid is a run of
/// typed sections — an 8-byte {kind, count} head, then that many entities, each
/// of which serialises itself through its own virtual method — so there is no
/// flat record array to walk and entities are not read yet (Q182). A save loads
/// with the dungeon as the player left it and no occupants, which is the same
/// shape a campaign level currently loads in: THINGS parses, but M6 has not
/// projected those records into the simulation.</para>
///
/// <para>Layout CONFIRMED against the reference install's own two saves and
/// cross-validated against the level's design map — see
/// docs/research/sav-format.md. The load is deliberately self-checking:
/// <see cref="AgreementWithDesign"/> compares the terrain lane against the
/// level's <c>Map.kld</c>, and a correct read of a played save agrees on most
/// tiles while a wrong offset agrees on almost none.</para>
/// </summary>
public static class Dk2SaveReader
{
    /// <summary>
    /// The <c>__DATE__ __TIME__</c> of the executable that wrote the save, which
    /// DK2 stores as its own compatibility guard. Saves written by any other
    /// build have an unproven layout and are rejected rather than misread.
    /// </summary>
    public const string SupportedBuildStamp = "May 12 200012:28:22";

    /// <summary>
    /// The build stamp is a <b>64-byte</b> field, but DK2 fills only the 19
    /// characters of <c>__DATE__</c> and <c>__TIME__</c> plus a terminator. The
    /// writer builds it in a 64-byte stack buffer with a copy and a concatenate
    /// and never clears it, so <b>every save leaks about 44 bytes of DK2's
    /// stack</b> into the file. See docs/research/sav-format.md; it is why the
    /// addresses visible at 0x20 and 0x28 are garbage rather than a serialised
    /// object graph (Q180).
    /// </summary>
    private const int BuildStampOffset = 8;
    private const int BuildStampField = 64;
    private const int BuildStampTextLength = 19;

    /// <summary>The level name: a 64-byte NUL-padded field, read off the writer.</summary>
    private const int LevelNameOffset = 0xC2;
    private const int LevelNameField = 64;

    /// <summary>The tick the game was on when it saved.</summary>
    private const int GameTickOffset = 0x102;

    /// <summary>
    /// The word the save opens with. <b>Not a header length</b>, though it was
    /// read as one until DK2's save routine was decompiled: the writer emits it
    /// as a literal, and the header actually runs to 0x134.
    /// </summary>
    private const uint ExpectedFormatWord = 220;

    /// <summary>Where the header ends and the undecoded block begins (Q183).</summary>
    private const int HeaderLength = 0x134;

    /// <summary>
    /// How many tiles the coarse pass compares at each candidate offset. Spread
    /// across the whole grid rather than taken from the front, because a level's
    /// first rows are border rock and match almost anywhere.
    /// </summary>
    private const int SampleTiles = 512;

    /// <summary>
    /// How many of the best-sampling offsets are then scored against the whole
    /// grid. More than one because the sample can rank a near neighbour first —
    /// one record either side of the true offset still scores highly.
    /// </summary>
    private const int FinalistOffsets = 32;

    /// <summary>
    /// Bytes per tile in the *runtime* grid, wider than the 4-byte design tile
    /// in a <c>Map.kld</c>.
    /// </summary>
    public const int TileRecordSize = 10;

    /// <summary>Lane 0 of the tile record: terrain id, the enum MAP(100) uses.</summary>
    private const int LaneTerrain = 0;

    /// <summary>Lane 8: the owning player id, matching MAP(100)'s player byte.</summary>
    private const int LaneOwner = 8;

    /// <summary>Lane 2: the room instance this tile belongs to, 0 for none.</summary>
    private const int LaneRoom = 2;

    /// <summary>Lanes 6-7: the tile's current health, little-endian u16.</summary>
    private const int LaneHealth = 6;

    /// <summary>
    /// The last slot of the grid is <b>not a tile</b>. Its bytes change between
    /// any two saves of the same dungeon, including saves where nothing on the
    /// map changed at all, and it is the one slot whose owner byte falls outside
    /// the level's own player set. Treated as scratch and excluded from tile
    /// statistics; still returned, because dropping a cell would misalign every
    /// index into the grid.
    /// </summary>
    public static int ScratchSlotIndex(int width, int height) => (width * height) - 1;

    /// <summary>Where DK2 keeps saves, relative to the install.</summary>
    public static string SaveDirectory(string installDir) =>
        Path.Combine(installDir, "Data", "Save");

    /// <summary>
    /// Every save in an install, newest first. Both extensions, because a
    /// quicksave is the same format under a different name.
    /// </summary>
    public static IReadOnlyList<string> EnumerateSaves(string installDir)
    {
        string dir = SaveDirectory(installDir);
        if (!Directory.Exists(dir)) return [];

        return Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(".SAV", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".QSAV", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    /// <summary>
    /// Reads the header. Every offset here is taken from the write sequence in
    /// DK2's own save routine rather than inferred from sample files — see
    /// docs/research/sav-format.md.
    /// </summary>
    public static Dk2SaveHeader ReadHeader(ReadOnlySpan<byte> save)
    {
        if (save.Length < HeaderLength)
            throw new InvalidDataException(
                $"a save's header is {HeaderLength} bytes; this file is {save.Length}.");

        uint formatWord = BinaryPrimitives.ReadUInt32LittleEndian(save);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(save[4..]);

        // 220 is a constant the writer emits, NOT a length: the header runs to
        // 0x134. This reader treated it as a size until the save routine was
        // read, which happened to work only because the level name sits inside
        // the first 220 bytes.
        if (formatWord != ExpectedFormatWord)
            throw new InvalidDataException(
                $"the save opens with {formatWord}, not the expected {ExpectedFormatWord}.");

        string stamp = Encoding.ASCII.GetString(
            save.Slice(BuildStampOffset, BuildStampTextLength));

        return new Dk2SaveHeader(
            formatWord,
            version,
            stamp,
            ReadNulPaddedText(save.Slice(LevelNameOffset, LevelNameField), "level name"),
            BinaryPrimitives.ReadUInt32LittleEndian(save[GameTickOffset..]));
    }

    /// <summary>Reads one of the header's fixed-width NUL-padded text fields.</summary>
    private static string ReadNulPaddedText(ReadOnlySpan<byte> field, string what)
    {
        int end = field.IndexOf((byte)0);
        if (end < 0) end = field.Length;
        if (end == 0)
            throw new InvalidDataException($"the save's {what} field is empty.");

        return Encoding.ASCII.GetString(field[..end]);
    }

    /// <summary>
    /// Finds where the tile grid starts, by correlating the save's terrain lane
    /// against the level's own design map.
    ///
    /// <para><b>Why a search rather than a constant.</b> The offset was recorded
    /// as fixed at <see cref="FirstSampleGridOffset"/> from two saves of one
    /// level; a save of a different level puts the grid somewhere else entirely,
    /// because the undecoded block between the header and the grid (Q183) is
    /// sized by something about the level. See
    /// docs/research/sav-format.md and fog-of-war.md §11.5.</para>
    ///
    /// <para><b>Why correlation works.</b> A played save differs from its design
    /// map only where somebody dug, claimed or built, so the true offset scores
    /// very high — <b>99.7%</b> on a save taken at the start of a game — while a
    /// misaligned read agrees at chance against a 41-value enum. The peak is
    /// sharp: one record either side scores 91% or less.</para>
    ///
    /// <para>Two passes. The first scores a spread sample of
    /// <see cref="SampleTiles"/> tiles at every possible offset; the second
    /// scores the <see cref="FinalistOffsets"/> best of those against the whole
    /// grid and returns the winner. The sample alone is not trusted to rank a
    /// near neighbour below the real thing.</para>
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file is too small to hold the grid, or nothing in it correlates with
    /// the design map — which means the save is not of this level, or the record
    /// stride is not what this reader believes.
    /// </exception>
    public static int FindGridOffset(ReadOnlySpan<byte> save, LevelMap design)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));

        int tiles = design.Width * design.Height;
        int span = tiles * TileRecordSize;
        int lastOffset = save.Length - span;
        if (lastOffset < HeaderLength)
            throw new InvalidDataException(
                $"a {design.Width}x{design.Height} grid needs {span} bytes after the header; " +
                $"the save is {save.Length}.");

        // The sample. Every stride'th tile, so it crosses the whole map instead
        // of sitting in the border rock the first rows are made of.
        int stride = Math.Max(1, tiles / SampleTiles);
        int sampled = 0;
        for (int i = 0; i < tiles; i += stride) sampled++;

        // The finalists, kept sorted best-first. A plain array rather than a
        // heap: this target is netstandard2.1, the list is 32 long, and an
        // insertion only happens when an offset beats the worst of them.
        var offsets = new int[FinalistOffsets];
        var scores = new int[FinalistOffsets];
        int held = 0;

        for (int offset = HeaderLength; offset <= lastOffset; offset++)
        {
            int hits = 0;
            for (int i = 0; i < tiles; i += stride)
                if (save[offset + (i * TileRecordSize)] == design.Tiles[i].TerrainId) hits++;

            if (held == FinalistOffsets && hits <= scores[held - 1]) continue;

            int at = held < FinalistOffsets ? held++ : FinalistOffsets - 1;
            while (at > 0 && scores[at - 1] < hits)
            {
                scores[at] = scores[at - 1];
                offsets[at] = offsets[at - 1];
                at--;
            }
            scores[at] = hits;
            offsets[at] = offset;
        }

        int winner = -1;
        int winningScore = -1;
        for (int n = 0; n < held; n++)
        {
            // Nothing that failed the sample outright can win the full grid, and
            // scoring it would only cost time.
            if (scores[n] * 2 < sampled) continue;

            int hits = 0;
            for (int i = 0; i < tiles; i++)
                if (save[offsets[n] + (i * TileRecordSize)] == design.Tiles[i].TerrainId) hits++;

            if (hits <= winningScore) continue;
            winningScore = hits;
            winner = offsets[n];
        }

        if (winner < 0)
            throw new InvalidDataException(
                $"no offset in the save correlates with {design.Width}x{design.Height} of " +
                "the level's design map; the save may not be of this level.");

        return winner;
    }

    /// <summary>
    /// Reads the grid at the given dimensions, which come from the level's own
    /// design map — the save does not restate them anywhere yet found — and at
    /// the offset <see cref="FindGridOffset"/> located.
    /// </summary>
    public static LevelMap ReadGrid(
        ReadOnlySpan<byte> save, int gridOffset, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "grid dimensions must be positive.");

        int needed = gridOffset + (width * height * TileRecordSize);
        if (save.Length < needed)
            throw new InvalidDataException(
                $"a {width}x{height} grid needs {needed} bytes; the save is {save.Length}.");

        var tiles = new MapTile[width * height];
        for (int i = 0; i < tiles.Length; i++)
        {
            int o = gridOffset + (i * TileRecordSize);

            // Flags is left at zero rather than guessed. Eight of the ten lanes
            // are undecoded (Q181) and none of them matched the design map's
            // flag byte on better than chance, so there is nothing to copy here
            // that beats an honest zero. The one consequence to know about:
            // BridgeRules reads TileState.Flags to tell a bridge over lava from
            // one over water, so a bridge in a loaded save reads as spanning
            // water until Q181 is settled.
            tiles[i] = new MapTile(
                TerrainId: save[o + LaneTerrain],
                PlayerId: save[o + LaneOwner],
                Flags: 0,
                Unknown: 0);
        }

        return new LevelMap(width, height, tiles);
    }

    /// <summary>
    /// Reads the grid as the fuller <see cref="SavedTile"/>, keeping the lanes
    /// that <see cref="LevelMap"/> has nowhere to put.
    /// </summary>
    public static SavedTile[] ReadTiles(byte[] save, int gridOffset, int width, int height)
    {
        int needed = gridOffset + (width * height * TileRecordSize);
        if (save.Length < needed)
            throw new InvalidDataException(
                $"a {width}x{height} grid needs {needed} bytes; the save is {save.Length}.");

        var tiles = new SavedTile[width * height];
        for (int i = 0; i < tiles.Length; i++)
        {
            int o = gridOffset + (i * TileRecordSize);
            tiles[i] = new SavedTile(
                TerrainId: save[o + LaneTerrain],
                OwnerPlayerId: save[o + LaneOwner],
                RoomId: save[o + LaneRoom],
                Health: BinaryPrimitives.ReadUInt16LittleEndian(save.AsSpan(o + LaneHealth)),
                Raw: new ReadOnlyMemory<byte>(save, o, TileRecordSize));
        }
        return tiles;
    }

    /// <summary>
    /// Loads a save, taking the grid dimensions from the level's design map in
    /// the same installation.
    ///
    /// <para>Returns null when the install has no map for the level the save
    /// names — the same "skip cleanly" shape the rest of the loader uses.</para>
    /// </summary>
    public static SavedGame? Load(string installDir, string savePath)
    {
        byte[] bytes = File.ReadAllBytes(savePath);

        Dk2SaveHeader header = ReadHeader(bytes);

        if (!header.IsSupportedBuild)
            throw new InvalidDataException(
                $"the save was written by a DK2 build stamped '{header.BuildStamp}'; " +
                $"only '{SupportedBuildStamp}' has a known layout.");

        LevelMap? design = GameDatabaseLoader.LoadCampaignMap(installDir, header.LevelName);
        if (design is null) return null;

        int gridOffset = FindGridOffset(bytes, design);
        LevelMap map = ReadGrid(bytes, gridOffset, design.Width, design.Height);

        return new SavedGame(
            header,
            map,
            ReadTiles(bytes, gridOffset, design.Width, design.Height),
            AgreementWithDesign(map, design),
            gridOffset);
    }

    /// <summary>
    /// The share of tiles whose terrain still matches the level's design map.
    ///
    /// <para><b>This is the load's own correctness check, not a statistic.</b> A
    /// played save differs from its design map only where the player dug,
    /// claimed or built — 73% agreement on the sample save and 85% on an earlier
    /// quicksave of the same level. A grid read at the wrong offset or the wrong
    /// stride agrees at chance, which for a 41-value enum is nowhere near
    /// either. Treat a low number as a failed read rather than as a busy
    /// player.</para>
    /// </summary>
    public static double AgreementWithDesign(LevelMap saved, LevelMap design)
    {
        if (saved.Width != design.Width || saved.Height != design.Height) return 0;

        int same = 0;
        for (int i = 0; i < saved.Tiles.Count; i++)
            if (saved.Tiles[i].TerrainId == design.Tiles[i].TerrainId) same++;

        return (double)same / saved.Tiles.Count;
    }
}
