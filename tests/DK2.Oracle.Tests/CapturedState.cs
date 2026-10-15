// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.Json;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Oracle.Tests;

/// <summary>
/// One tick of a DK2 full-state capture, as plain values, with the memory image the original
/// runs on: our world is rebuilt from the level and then given the capture's tiles, so our C#
/// and DKII.EXE answer the same question on the same state (ADR-0481).
///
/// <para>The values come from <c>tools/oracle/capture_state.py</c>, which reads the capture
/// with the local decoder; what each field is, and where its offset comes from, is written
/// there. Captures and the decoder live in the gitignored <c>reverse-engineering/</c>, so
/// every test that reads one skips where they are absent (<see cref="CapturedFactAttribute"/>).</para>
/// </summary>
internal sealed record CapturedState(
    int Tick, int Width, int Height, CapturedPlayer[] Players, int[] Tiles, CapturedRoom[] Rooms,
    CapturedCreature[] Creatures)
{
    /// <summary>The memory image for <see cref="OriginalCpu.Run"/>, beside the JSON.</summary>
    public string MemoryImage { get; private init; } = "";

    public int Terrain(int x, int y) => Tiles[3 * (y * Width + x)];

    public int Owner(int x, int y) => Tiles[3 * (y * Width + x) + 1];

    public int RoomSlot(int x, int y) => Tiles[3 * (y * Width + x) + 2];

    /// <summary>The ticks of <paramref name="capture"/> (a path under the repository) that it
    /// holds, exported once per test run into the temp folder.</summary>
    public static IReadOnlyList<CapturedState> Load(string capture, IEnumerable<int> ticks)
    {
        string source = Dk2CaptureGate.PathFor(capture);
        string folder = Path.Combine(Path.GetTempPath(), "dk2-captured",
            Path.GetFileNameWithoutExtension(capture) + "-" + File.GetLastWriteTimeUtc(source).Ticks);
        int[] wanted = [.. ticks];
        int[] missing = [.. wanted.Where(t => !File.Exists(Path.Combine(folder, $"{t}.json")))];
        if (missing.Length > 0)
            OriginalCpu.Python("capture_state.py", [source, folder, .. missing.Select(t => t.ToString())]);

        var states = new List<CapturedState>();
        foreach (int tick in wanted)
        {
            string json = Path.Combine(folder, $"{tick}.json");
            if (!File.Exists(json)) continue;   // past the capture's end
            CapturedState state = JsonSerializer.Deserialize<CapturedState>(File.ReadAllText(json), Json)!;
            states.Add(state with { MemoryImage = Path.ChangeExtension(json, ".mem") });
        }
        return states;
    }

    /// <summary>Our world for <paramref name="levelName"/> as the level file sets it up.</summary>
    public static (GameDatabase Database, WorldState World) Level(string levelName)
    {
        string install = Dk2InstallGate.Path;
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, levelName)!;
        List<PlayerDef> players = GameDatabaseLoader.LoadCampaignPlayers(install, levelName)!;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, levelName);
        WorldState world = WorldState.FromMap(database, map, [.. players.Select(p => p.PlayerId)]);
        ThingProjection.Place(world, database, GameDatabaseLoader.LoadCampaignThings(install, levelName)!);
        return (database, world);
    }

    /// <summary>Gives <paramref name="world"/> this tick's terrain and owners, tile by tile.</summary>
    public void ApplyTiles(WorldState world)
    {
        Assert.True(world.Width == Width && world.Height == Height,
            $"the capture's map is {Width}x{Height}, ours {world.Width}x{world.Height}");
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = (byte)Terrain(x, y);
            tile.OwnerPlayerId = (byte)Owner(x, y);
        }
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}

internal sealed record CapturedPlayer(int Id, int Tag, uint Ptr);

internal sealed record CapturedRoom(int? Slot, uint Ptr, bool Active, int Type, int Owner, int MinX, int MaxX, int MinY, int MaxY);

internal sealed record CapturedCreature(int Slot, uint Ptr, int Owner, int X, int Y, int Type, uint State, uint Health,
    int Target, uint? TargetClass);

/// <summary><c>[Fact]</c> for a test on a capture: skips without the install, Unicorn, the
/// capture or the local decoder.</summary>
public sealed class CapturedFactAttribute : FactAttribute
{
    public CapturedFactAttribute(string capture)
    {
        string? decoder = Environment.GetEnvironmentVariable("DK2_CAPTURE_DECODER");
        string decoderFile = Path.Combine(decoder ?? Dk2CaptureGate.PathFor("reverse-engineering"), "dk2capture.py");
        Skip = OriginalCpu.SkipReason
            ?? Dk2CaptureGate.SkipReasonFor(capture)
            ?? (File.Exists(decoderFile) ? null : $"No capture decoder at {decoderFile} (reverse-engineering/dk2capture.py).");
    }
}
