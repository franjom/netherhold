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

    private static readonly Dictionary<string, ulong> Pinned = new(StringComparer.Ordinal)
    {
        ["Alcatraz"] = 0x94269177F0F18EA9UL,
        ["Bottleneck"] = 0x0EF4A276319C18F7UL,
        ["Caverns"] = 0xF464DDACE397361BUL,
        ["Circlet"] = 0x4010C841A53672BAUL,
        ["Clover"] = 0x51D7EC702D73C81CUL,
        ["FrontEnd3DLevel"] = 0x1B577A4EFBC77084UL,
        ["Frosty's Castle"] = 0xB810C86995993C86UL,
        ["Frosty's Lair"] = 0xF1E3F07F7C788CD1UL,
        ["Frosty's Realm"] = 0x9773FE79EF275A42UL,
        ["Furnace"] = 0xF9B8FE9D2D6D8D93UL,
        ["Gonzalez"] = 0xFDEDF58AD40801D3UL,
        ["HeroCove"] = 0xCC10A2A0588AE511UL,
        ["Hopping"] = 0xC8F2D420D97150CDUL,
        ["Islands"] = 0x41D553DD12DCD576UL,
        ["King Of The Hill"] = 0x9ED78AB9262EC9E8UL,
        ["Level10"] = 0x1A576C0A590CB522UL,
        ["Level13"] = 0x89B437444A58E04FUL,
        ["Level14"] = 0x209A0F19BF0DDB9BUL,
        ["Level15a"] = 0x1F2B0905E1A92683UL,
        ["Level16"] = 0x2EFF12B7B2F12707UL,
        ["Level17"] = 0xC34212665710549BUL,
        ["Level19"] = 0x326F54D7BFE06242UL,
        ["Level20"] = 0x97FC5CDCC428B0B5UL,
        ["Level4"] = 0xD10E96EF3B044C61UL,
        ["Level6a"] = 0xA4195198CEC07F33UL,
        ["Level6b"] = 0x8EDC1606BEA1F83FUL,
        ["Level9"] = 0x73F411BD8FEA60EFUL,
        ["MPD6"] = 0x6680DECF19D4F66DUL,
        ["No Mans Land"] = 0x00D0C370D75495CEUL,
        ["Olympia"] = 0xBDBCC6F14F766CC5UL,
        ["Onslaught"] = 0x8871FFFE45F37F54UL,
        ["Patrol"] = 0x09F5BCF5BC98AB3DUL,
        ["Platform"] = 0xAA48D3A6993CD9A4UL,
        ["Pressure"] = 0x8911090CC7982506UL,
        ["Secret1"] = 0x98AFEF2357653735UL,
        ["Secret2"] = 0x1E9224C86CCF45BAUL,
        ["Secret3"] = 0xD5AF964B9A47C3CFUL,
        ["Secret4"] = 0x5F904FE02F8AD07BUL,
        ["Secret5"] = 0x0BE0FB72A29FC648UL,
        ["Siege"] = 0x298C23A6DFCA8F51UL,
        ["Small"] = 0x9B9B4361DA5E289FUL,
        ["Spiral"] = 0x740AB01B68DBBFB4UL,
        ["Swiss Cheese"] = 0x8529C2F6365B4027UL,
        ["The Deep End"] = 0xA35130F1960BFF9CUL,
        ["The Pass"] = 0x8B05AA3AE30B09EAUL,
        ["TrapTime"] = 0x9B4BD4BFA6A60496UL,
        ["U Turn"] = 0x3F947426CCE657FDUL,
        ["Warrens"] = 0x3DB3E0460ECC91B4UL,
        ["level1"] = 0xD4C147CFFA8041E9UL,
        ["level11A"] = 0x6DAE435CF06E3E2DUL,
        ["level11B"] = 0x0C0F628127CA6798UL,
        ["level11C"] = 0x94CC4F280D17995FUL,
        ["level12"] = 0x356AD26593DC9F66UL,
        ["level15b"] = 0xC2299986B0827EE8UL,
        ["level18"] = 0xAB2FF4A27E78F0E5UL,
        ["level2"] = 0x91F11DEF91BDDA1DUL,
        ["level3"] = 0xE180488F50CD51C9UL,
        ["level5"] = 0xC0451F0BBF357A02UL,
        ["level7"] = 0x523FCED0F95C7764UL,
        ["level8"] = 0xCED7736D86AD4A57UL,
        ["mpd1"] = 0x6539AC6EF2E89601UL,
        ["mpd2"] = 0x0CD1937D9C50C54BUL,
        ["mpd3"] = 0xCA3C2FB05EE8A415UL,
        ["mpd4"] = 0x5609CB6D7395C34CUL,
        ["mpd5"] = 0xE599546FC10FA2F5UL,
        ["mpd7"] = 0x842B2D4FEAFA2CA9UL,
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
