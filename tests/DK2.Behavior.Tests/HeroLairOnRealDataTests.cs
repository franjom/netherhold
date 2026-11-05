// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The shipped data behind the level start's Hero Lair sweep (docs/research/hero-lair-trestles.md, ADR-0673):
/// terrain 35 is plain ground no room uses, object 0x71 exists, and only a few maps have a tile whose eight neighbours
/// are terrain 35 too.</summary>
public class HeroLairOnRealDataTests
{
    [InstallFact]
    public void TerrainThirtyFiveIsNeitherSolidNorWaterNorARoomTerrain()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        TerrainDef lair = db.TerrainById(HeroLairTerrain);
        Assert.Equal("Hero Lair", lair.Name);
        Assert.Equal((TerrainFlags)0, lair.Flags & (TerrainFlags.Solid | TerrainFlags.ConstructionTypeWater));
        Assert.DoesNotContain(db.Rooms.Values, r => r.TerrainId == HeroLairTerrain);
        Assert.Equal("Hero Armoury Trestle", db.ObjectById(0x71)!.Name);
    }

    [InstallFact]
    public void OnlyTheseMapsHoldAnInteriorLairTile()
    {
        string install = Dk2InstallGate.Path;
        var found = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(Path.Combine(install, "Data", "editor", "maps"), "*Variables.kld"))
        {
            string level = Path.GetFileNameWithoutExtension(file)[..^"Variables".Length];
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;
            int interior = 0;
            for (int y = 1; y < map.Height - 1; y++)
                for (int x = 1; x < map.Width - 1; x++)
                    if (AllNineAre35(map, x, y)) interior++;
            if (interior > 0) found[level] = interior;
        }
        Assert.Equal(
            new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Level16"] = 1, ["Level17"] = 1, ["mpd1"] = 9, ["mpd2"] = 8, ["mpd3"] = 9, ["mpd4"] = 3, ["mpd5"] = 6,
                ["mpd7"] = 2, ["Patrol"] = 2, ["TrapTime"] = 2,
            },
            found);
    }

    private const byte HeroLairTerrain = 35;

    private static bool AllNineAre35(LevelMap map, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (map[x + dx, y + dy].TerrainId != HeroLairTerrain) return false;
        return true;
    }
}
