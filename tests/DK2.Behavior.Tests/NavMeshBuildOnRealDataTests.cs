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

    // Re-pinned 2026-10-13 (ADR-0461): the whole-map build now carries no constraint bits, as the
    // original's does, and coarsens with the original's bounded flood (`0x004CC650`) rather than a
    // slot sweep to a fixed point; the flood leaves 1-3 % of triangles differently from the sweep.
    private static readonly Dictionary<string, ulong> Pinned = new(StringComparer.Ordinal)
    {
        ["Alcatraz"] = 0xF9564D072254425BUL,
        ["Bottleneck"] = 0x8C95AD54A166DA3FUL,
        ["Caverns"] = 0xC45DF5E1855F9AE7UL,
        ["Circlet"] = 0xC14AC2B828ECE605UL,
        ["Clover"] = 0xE0BF866385305908UL,
        ["FrontEnd3DLevel"] = 0xDCEFD1C838CC014EUL,
        ["Frosty's Castle"] = 0xC4A6E8F1FEE25CD1UL,
        ["Frosty's Lair"] = 0x4066D5A8B501F788UL,
        ["Frosty's Realm"] = 0xF8729925A7C0380CUL,
        ["Furnace"] = 0x9CB7AF24856F0319UL,
        ["Gonzalez"] = 0xC65AB7C1710E2B8EUL,
        ["HeroCove"] = 0xA4684F458D50B26BUL,
        ["Hopping"] = 0x8E3B4AF63C328108UL,
        ["Islands"] = 0x9DADDE26B6B5B1E6UL,
        ["King Of The Hill"] = 0xE0A39A7426D0389AUL,
        ["Level10"] = 0x137B834A2A15FC92UL,
        ["Level13"] = 0x7E4553384AD3741FUL,
        ["Level14"] = 0x4BE232A313A3ADCFUL,
        ["Level15a"] = 0xB286F35AF5A607DCUL,
        ["Level16"] = 0x6D871B015163C0AAUL,
        ["Level17"] = 0x13314ED3A3459207UL,
        ["Level19"] = 0x6B9DE270854ACE78UL,
        ["Level20"] = 0xDEEDE7A4760E7738UL,
        ["Level4"] = 0x77365033475A40F9UL,
        ["Level6a"] = 0x3AABEB04D6BF90A8UL,
        ["Level6b"] = 0x6CB78A0D36FF2CCBUL,
        ["Level9"] = 0x3B3A9609CAD0EC4EUL,
        ["MPD6"] = 0x9DBB5311D308A1DFUL,
        ["No Mans Land"] = 0x97F64492CB4077DFUL,
        ["Olympia"] = 0x8C561C1BED15CF7BUL,
        ["Onslaught"] = 0xB5FB43BF98C2748DUL,
        ["Patrol"] = 0x4BDE0BF1AA99D758UL,
        ["Platform"] = 0x9F0B0C0FCB2D5E6EUL,
        ["Pressure"] = 0x3FA0E14D3942EB90UL,
        ["Secret1"] = 0xCE70E46A93375305UL,
        ["Secret2"] = 0xB7E0C46A80269A0DUL,
        ["Secret3"] = 0x2D4A3712B329D2FCUL,
        ["Secret4"] = 0xEDE3696AF82146ACUL,
        ["Secret5"] = 0xD3678C776977FE33UL,
        ["Siege"] = 0x28406C658FE51651UL,
        ["Small"] = 0x5E8F7E96A10DE1FEUL,
        ["Spiral"] = 0xCC194BEF25EB9D30UL,
        ["Swiss Cheese"] = 0x94380A01A7EB273EUL,
        ["The Deep End"] = 0xCFE3C7A77B8E9DFFUL,
        ["The Pass"] = 0xC76018516099E019UL,
        ["TrapTime"] = 0xD874D4FAEDDD1A37UL,
        ["U Turn"] = 0xDC53FB22709E2220UL,
        ["Warrens"] = 0xDD08A854A0CB2F82UL,
        ["level1"] = 0x78002CCAB26F71C7UL,
        ["level11A"] = 0x2AAC995805A32875UL,
        ["level11B"] = 0xCB5ED0AE340F8871UL,
        ["level11C"] = 0x1A083449AE3FBF07UL,
        ["level12"] = 0xB2A8273DEC2B210AUL,
        ["level15b"] = 0x6B8BD1D2592E376BUL,
        ["level18"] = 0x01C848B078237F15UL,
        ["level2"] = 0xAEF75343D29D62F2UL,
        ["level3"] = 0xA80CD0B6AC647CD9UL,
        ["level5"] = 0x8C9828DF024D7282UL,
        ["level7"] = 0x6238A9F53F732932UL,
        ["level8"] = 0xEED8C76510F70AEEUL,
        ["mpd1"] = 0xE6AD2A41A8B28DA3UL,
        ["mpd2"] = 0x47D1361B7F462A7CUL,
        ["mpd3"] = 0x3E36FABC61FE0481UL,
        ["mpd4"] = 0xBA487F8D8E086FB7UL,
        ["mpd5"] = 0xFA7DFEAABE39E697UL,
        ["mpd7"] = 0x75886B1A52C08585UL,
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
