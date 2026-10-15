// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>
/// The whole-map mesh of every shipped level, slot for slot, pinned as the scan-based build
/// produced it before the build was made faster (perf/navmesh-rebuilds). Every point, every
/// triangle's vertices, neighbours, byte and constraint bits are folded in, so the faster build
/// must make the same choice wherever the scans took the lowest slot first: the slot order is what
/// routing walks, so a different order is a different game even when the mesh looks the same.
/// <para>Each level is pinned as shipped and with seeded rewrites of 1, 25 and 400 tiles to
/// floor, water, lava or solid, which exercises far more rectangle shapes than the levels alone.</para>
/// </summary>
public class NavMeshBuildOnRealDataTests(ITestOutputHelper output)
{
    private static readonly byte[] Bytes = [NavBytes.Floor, NavBytes.Water, NavBytes.Lava, NavBytes.Solid];
    private static readonly int[] Rewrites = [0, 1, 25, 400];

    // Re-pinned 2026-10-15 (ADR-0486): the whole-map build now has the original's reset diagonal,
    // corner order, split slots, constraint insertion and cell-cache locate; on the Level 10
    // capture it matches DK2's own mesh at level start in 2,826 of 2,828 triangles and 2,823 slots
    // (navigation-partial-rebuild.md).
    private static readonly Dictionary<string, ulong> Pinned = new(StringComparer.Ordinal)
    {
         ["Alcatraz"] = 0x8D435A47C2153380UL,
         ["Bottleneck"] = 0xAA2E0EAA7FD05689UL,
         ["Caverns"] = 0x8A2F2F8301175018UL,
         ["Circlet"] = 0x9378C749E198F486UL,
         ["Clover"] = 0xAC2398A3063092F1UL,
         ["FrontEnd3DLevel"] = 0x98403C7BA5C7A923UL,
         ["Frosty's Castle"] = 0x3F338A14D7B42A6AUL,
         ["Frosty's Lair"] = 0x895025E4A5C19CF8UL,
         ["Frosty's Realm"] = 0xB02E4FCAB1A7A86BUL,
         ["Furnace"] = 0xC8603013EC4E6A22UL,
         ["Gonzalez"] = 0x63457FC285DB651BUL,
         ["HeroCove"] = 0x0968AE87AC542B8CUL,
         ["Hopping"] = 0xCE3DC630F86968E3UL,
         ["Islands"] = 0x76FBDB3BB95844C5UL,
         ["King Of The Hill"] = 0x97CE8766A8061EFDUL,
         ["Level10"] = 0x3F0ADE80F8E5F871UL,
         ["Level13"] = 0x266EF1B4E54FAE49UL,
         ["Level14"] = 0xF3F97A7559CBDAB7UL,
         ["Level15a"] = 0xBA27A109159236B9UL,
         ["Level16"] = 0x1568D23C8E83ABE9UL,
         ["Level17"] = 0x2312AE0FE9AD806EUL,
         ["Level19"] = 0xB20269A0EC771FB5UL,
         ["Level20"] = 0xA485898CF9770D9DUL,
         ["Level4"] = 0x9922FE3895053C24UL,
         ["Level6a"] = 0x204444B1AC408F42UL,
         ["Level6b"] = 0x225454F620AE7AEBUL,
         ["Level9"] = 0xE797D3ABD4B165E9UL,
         ["MPD6"] = 0x3137E17C0C80C3ACUL,
         ["No Mans Land"] = 0x1391674E9628D5D6UL,
         ["Olympia"] = 0xC6AC9DA251287F8EUL,
         ["Onslaught"] = 0x37DCE8BBEF0DFBCFUL,
         ["Patrol"] = 0xE3922DA68EC8FF0EUL,
         ["Platform"] = 0xDFAE25BD9A8AD070UL,
         ["Pressure"] = 0x4889439AB0F5A55AUL,
         ["Secret1"] = 0xED2CF305485118E6UL,
         ["Secret2"] = 0x7F17749D7CA31795UL,
         ["Secret3"] = 0x5BA255CBD87797EAUL,
         ["Secret4"] = 0x9A906588BF663746UL,
         ["Secret5"] = 0xFCFC095256679952UL,
         ["Siege"] = 0xA17C9C2E9C107C02UL,
         ["Small"] = 0x7AF020094E73645BUL,
         ["Spiral"] = 0x058B8F00073BD0BFUL,
         ["Swiss Cheese"] = 0xCD2D2D051E1727B7UL,
         ["The Deep End"] = 0x25030CE59ADAF9B9UL,
         ["The Pass"] = 0xE5DAF60CFC8F32D2UL,
         ["TrapTime"] = 0x5D7219A43962459DUL,
         ["U Turn"] = 0x9CD0B6C80FD3241CUL,
         ["Warrens"] = 0xDCCB9A2A6F8EB8D8UL,
         ["level1"] = 0xBEB9CC8EF017D178UL,
         ["level11A"] = 0x5203EBFF66327E22UL,
         ["level11B"] = 0x60F2C7F49BCFFC05UL,
         ["level11C"] = 0xC0019443964F17F9UL,
         ["level12"] = 0x347F40868DF2B11EUL,
         ["level15b"] = 0xEB73DA242AD96E90UL,
         ["level18"] = 0x4568592E504507B7UL,
         ["level2"] = 0x53A5628C458186F1UL,
         ["level3"] = 0x03C36764B47FB449UL,
         ["level5"] = 0x64BC05561CBFB42FUL,
         ["level7"] = 0x8954E7754A0E0385UL,
         ["level8"] = 0x021AE57FC3BE51C4UL,
         ["mpd1"] = 0x1DC258879B580CC1UL,
         ["mpd2"] = 0x80F4E4406BC0470DUL,
         ["mpd3"] = 0xF45051081F5981A2UL,
         ["mpd4"] = 0x5CF1AF12EEDF3621UL,
         ["mpd5"] = 0x2583EF7E5239C402UL,
         ["mpd7"] = 0xB8222438E201605BUL,
    };

    private sealed class Grid(int width, int height, byte[] bytes) : INavByteGrid
    {
        public int Width => width;
        public int Height => height;
        public byte NavByte(int x, int y) => bytes[(y * width) + x];
    }

    [InstallFact]
    public void EveryShippedLevelBuildsThePinnedMesh()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        var actual = new SortedDictionary<string, ulong>(StringComparer.Ordinal);

        foreach (string level in LevelNames(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;
            WorldState world = WorldState.FromMap(database, map, map.Tiles.Select(t => t.PlayerId).Distinct());
            byte[] shipped = WorldSteering.CaptureNavigationBytes(database, world);

            Hash64 hash = Hash64.Start();
            foreach (int rewrites in Rewrites)
            {
                byte[] bytes = Rewrite(shipped, rewrites, seed: (uint)(level.GetStableHashCode() ^ rewrites));
                Fingerprint(NavMesh.Build(new Grid(world.Width, world.Height, bytes)), ref hash);
            }
            actual[level] = hash.Value;
        }

        foreach ((string level, ulong value) in actual)
            output.WriteLine($"        [\"{level}\"] = 0x{value:X16}UL,");
        Assert.NotEmpty(actual);
        Assert.Equal(Pinned.OrderBy(p => p.Key, StringComparer.Ordinal), actual);
    }

    /// <summary>The per-tile rebuild (ADR-0461) on every shipped level: 40 single-tile edits applied
    /// one at a time, each leaving a valid mesh whose every triangle carries the byte of the tile
    /// under it and no constraint bit, with the map corners still vertices. What the edit leaves is
    /// the original's business and is not pinned; that it leaves a usable mesh is ours.</summary>
    [InstallFact]
    public void EveryShippedLevelSurvivesSequentialTileEdits()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        int levels = 0, edits = 0;
        foreach (string level in LevelNames(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;
            WorldState world = WorldState.FromMap(database, map, map.Tiles.Select(t => t.PlayerId).Distinct());
            byte[] bytes = WorldSteering.CaptureNavigationBytes(database, world);
            var grid = new Grid(world.Width, world.Height, bytes);
            NavMesh mesh = NavMesh.Build(grid);
            uint state = (uint)level.GetStableHashCode() | 1;
            for (int i = 0; i < 40; i++)
            {
                state = (state * 1664525) + 1013904223;
                int x = 1 + (int)((state >> 8) % (uint)(world.Width - 2));
                state = (state * 1664525) + 1013904223;
                int y = 1 + (int)((state >> 8) % (uint)(world.Height - 2));
                state = (state * 1664525) + 1013904223;
                byte nav = Bytes[(state >> 16) % (uint)Bytes.Length];
                if (bytes[(y * world.Width) + x] == nav) continue;
                bytes[(y * world.Width) + x] = nav;
                try
                {
                    mesh.Retriangulate(grid, x, y, x + 1, y + 1);
                    AssertEdited(mesh, grid);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"{level}: edit {i} at ({x},{y}) to {nav}: {ex.Message}", ex);
                }
                edits++;
            }
            levels++;
        }
        output.WriteLine($"{edits} edits over {levels} levels");
        Assert.True(levels > 0);
    }

    private static void AssertEdited(NavMesh mesh, Grid grid)
    {
        for (int t = 0; t < mesh.TriangleSlots; t++)
        {
            if (!mesh.IsTriangleLive(t)) continue;
            int a = mesh.Vertex(t, 0), b = mesh.Vertex(t, 1), c = mesh.Vertex(t, 2);
            Assert.True(NavMesh.Orient(mesh.PointX(a), mesh.PointY(a), mesh.PointX(b), mesh.PointY(b), mesh.PointX(c), mesh.PointY(c)) > 0,
                $"triangle {t} is not wound positively");
            long cx = 0, cy = 0;
            for (int i = 0; i < 3; i++)
            {
                Assert.False(mesh.IsConstraint(t, i), $"triangle {t} edge {i} keeps a constraint bit");
                int u = mesh.Neighbour(t, i);
                if (u != NavMesh.NoNeighbour)
                {
                    Assert.True(mesh.IsTriangleLive(u), $"triangle {t} edge {i} points at dead triangle {u}");
                    int j = mesh.EdgeIndexOf(u, mesh.Vertex(t, (i + 1) % 3), mesh.Vertex(t, i));
                    Assert.Equal(t, mesh.Neighbour(u, j));
                }
                int p = mesh.Vertex(t, i);
                cx += mesh.PointX(p) * 3L * NavMesh.UnitsPerTile;
                cy += mesh.PointY(p) * 3L * NavMesh.UnitsPerTile;
            }
            int tx = (int)Math.Floor(cx / 9 / (double)NavMesh.UnitsPerTile), ty = (int)Math.Floor(cy / 9 / (double)NavMesh.UnitsPerTile);
            if (tx < 0 || ty < 0 || tx >= grid.Width || ty >= grid.Height) continue;
            Assert.True(grid.NavByte(tx, ty) == mesh.NavByte(t),
                $"triangle {t} carries byte {mesh.NavByte(t)} over tile ({tx},{ty}) whose byte is {grid.NavByte(tx, ty)}");
        }
        foreach ((int x, int y) in new[] { (0, 0), (grid.Width, 0), (grid.Width, grid.Height), (0, grid.Height) })
            Assert.True(mesh.TryLocateVertex(x, y, out _, out _), $"map corner ({x},{y}) is no longer a vertex");
    }

    /// <summary>Rewrites <paramref name="count"/> tiles, drawn by a fixed generator so the pin
    /// does not move with the simulation's random number generator.</summary>
    private static byte[] Rewrite(byte[] shipped, int count, uint seed)
    {
        byte[] bytes = (byte[])shipped.Clone();
        uint state = seed | 1;
        for (int i = 0; i < count; i++)
        {
            state = (state * 1664525) + 1013904223;
            int tile = (int)(state % (uint)bytes.Length);
            state = (state * 1664525) + 1013904223;
            bytes[tile] = Bytes[(state >> 16) % (uint)Bytes.Length];
        }
        return bytes;
    }

    private static void Fingerprint(NavMesh mesh, ref Hash64 hash)
    {
        hash.Add(mesh.PointSlots);
        for (int p = 0; p < mesh.PointSlots; p++)
        {
            hash.Add(mesh.IsPointLive(p));
            hash.Add(mesh.PointX(p));
            hash.Add(mesh.PointY(p));
        }
        hash.Add(mesh.TriangleSlots);
        for (int t = 0; t < mesh.TriangleSlots; t++)
        {
            hash.Add(mesh.NavByte(t));
            for (int i = 0; i < 3; i++)
            {
                hash.Add(mesh.Vertex(t, i));
                hash.Add(mesh.Neighbour(t, i));
                hash.Add(mesh.IsConstraint(t, i));
            }
        }
    }

    private static IEnumerable<string> LevelNames(string install)
    {
        string maps = Path.Combine(install, "Data", "editor", "maps");
        Assert.True(Directory.Exists(maps), $"'{maps}' is missing from the installation.");
        foreach (string file in Directory.EnumerateFiles(maps, "*Map.kld").OrderBy(f => f, StringComparer.Ordinal))
            yield return Path.GetFileName(file)[..^"Map.kld".Length];
    }
}

internal static class StableHash
{
    /// <summary>FNV-1a over the UTF-16 code units: <see cref="string.GetHashCode()"/> is randomised per process.</summary>
    public static int GetStableHashCode(this string text)
    {
        uint h = 2166136261;
        foreach (char c in text) h = (h ^ c) * 16777619;
        return unchecked((int)h);
    }
}
