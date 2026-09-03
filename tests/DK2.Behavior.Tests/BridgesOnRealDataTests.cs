using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Behavior.Tests;

/// <summary>
/// The claim <see cref="BridgeRules"/> rests on, checked against every shipped
/// map (Q64).
///
/// <para>The owner confirmed that a sold bridge reverts to the water or lava it
/// spanned. The terrain table cannot supply that — every room terrain records
/// Claimed Path as its destroyed type — so the fact has to live somewhere else,
/// and the reading is that the MAP record's third byte per tile marks a bridge
/// over lava.</para>
///
/// <para>This is the guard on that reading. It is a sweep rather than a spot
/// check on purpose: the whole strength of the inference is that the partition
/// holds across all 66 maps and all three bridge types without a single
/// crossing, and a test on one level would not notice if that stopped being
/// true. Skips when no installation is configured (DK2_DIR).</para>
/// </summary>
public class BridgesOnRealDataTests
{
    private static readonly int[] NeighbourDx = [0, -1, 1, 0];
    private static readonly int[] NeighbourDy = [-1, 0, 0, 1];

    private static string? Install() => Dk2Install.Find();

    /// <summary>Every map file the install ships, by level name.</summary>
    private static IEnumerable<string> LevelNames(string install)
    {
        string maps = Path.Combine(install, "Data", "editor", "maps");
        if (!Directory.Exists(maps)) yield break;

        foreach (string file in Directory.EnumerateFiles(maps, "*Map.kld").OrderBy(f => f))
            yield return Path.GetFileName(file)[..^"Map.kld".Length];
    }

    /// <summary>
    /// <b>The partition, over the whole corpus.</b> A bridge tile marked 2 never
    /// touches water and a bridge tile marked 1 never touches lava — which is the
    /// entire basis for reading the byte as the hazard a bridge spans.
    ///
    /// <para>Neighbours rather than the level's water and lava counts, and that
    /// distinction is what nearly refuted the reading: a fully bridged pond
    /// leaves no water to count, so Furnace looks like a level with no water at
    /// all while carrying bridges over lava. Asking what each tile <i>borders</i>
    /// is what makes the split visible.</para>
    /// </summary>
    [Fact]
    public void TheBridgeMarkerPartitionsWaterFromLavaAcrossEveryShippedMap()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        byte water = db.WaterTerrain!.TerrainId;
        byte lava = db.LavaTerrain!.TerrainId;

        int bridgeTiles = 0, besideWater = 0, besideLava = 0;
        var crossings = new List<string>();

        foreach (string level in LevelNames(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    MapTile tile = map[x, y];
                    RoomDef? room = db.RoomByTerrain(tile.TerrainId);
                    if (room is null || !BridgeRules.IsBridge(room)) continue;

                    bridgeTiles++;
                    bool spansLava = tile.Flags == BridgeRules.SpansLava;

                    for (int i = 0; i < 4; i++)
                    {
                        int nx = x + NeighbourDx[i], ny = y + NeighbourDy[i];
                        if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height) continue;

                        byte beside = map[nx, ny].TerrainId;
                        if (beside == water)
                        {
                            besideWater++;
                            if (spansLava) crossings.Add($"{level} ({x},{y}) marked lava, beside water");
                        }
                        else if (beside == lava)
                        {
                            besideLava++;
                            if (!spansLava) crossings.Add($"{level} ({x},{y}) marked water, beside lava");
                        }
                    }
                }
            }
        }

        Assert.Empty(crossings);

        // And the sweep was not vacuous: the shipped campaign really does contain
        // bridges of both kinds.
        Assert.True(bridgeTiles > 100, $"only {bridgeTiles} bridge tiles found");
        Assert.True(besideWater > 0, "no bridge tile borders water");
        Assert.True(besideLava > 0, "no bridge tile borders lava");
    }

    /// <summary>
    /// The marker takes no value but 1 or 2 anywhere, and <b>2 occurs only on
    /// bridge terrain</b> — never on any of the other terrain types. That
    /// exclusivity is why the byte can be read as a bridge marker at all rather
    /// than as something general we happen to be seeing on bridges.
    /// </summary>
    [Fact]
    public void TheMarkerIsTwoOnlyOnBridges()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        var values = new SortedSet<byte>();
        var nonBridgeTwos = new List<string>();

        foreach (string level in LevelNames(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;

            foreach (MapTile tile in map.Tiles)
            {
                values.Add(tile.Flags);
                if (tile.Flags != BridgeRules.SpansLava) continue;

                RoomDef? room = db.RoomByTerrain(tile.TerrainId);
                if (room is null || !BridgeRules.IsBridge(room))
                    nonBridgeTwos.Add($"{level} terrain {tile.TerrainId}");
            }
        }

        Assert.Empty(nonBridgeTwos);
        Assert.Equal<byte[]>([BridgeRules.SpansWater, BridgeRules.SpansLava], [.. values]);
    }

    /// <summary>
    /// Read through the rules rather than the raw byte: every shipped bridge tile
    /// reverts to a hazard, and <b>none</b> of them reverts to the Claimed Path
    /// its terrain record claims. This is the assertion that would fail if
    /// <see cref="BridgeRules"/> were bypassed again.
    /// </summary>
    [Fact]
    public void EveryShippedBridgeTileRevertsToAHazardRatherThanToFloor()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        int checked_ = 0;

        foreach (string level in LevelNames(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;

            foreach (MapTile source in map.Tiles)
            {
                RoomDef? room = db.RoomByTerrain(source.TerrainId);
                if (room is null || !BridgeRules.IsBridge(room)) continue;

                var tile = new TileState { TerrainId = source.TerrainId, Flags = source.Flags };
                TerrainDef reverted = db.TerrainById(BridgeRules.RevertsTo(db, tile));

                Assert.True(
                    reverted.IsWater || reverted.IsLava,
                    $"{level}: bridge terrain {source.TerrainId} reverts to {reverted.Name}");
                checked_++;
            }
        }

        Assert.True(checked_ > 100, $"only {checked_} bridge tiles checked");
    }

    /// <summary>
    /// One water terrain and one lava terrain, which is what lets the rules find
    /// them by flag instead of by id. A second of either would need a rule nobody
    /// has evidence for.
    /// </summary>
    [Fact]
    public void TheInstallHasExactlyOneWaterAndOneLavaTerrain()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Single(db.Terrain.Values, t => t.IsWater);
        Assert.Single(db.Terrain.Values, t => t.IsLava);
        Assert.Equal("Water", db.WaterTerrain!.Name);
        Assert.Equal("Lava", db.LavaTerrain!.Name);
    }
}
