using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

public class KwdTerrainRoomsTests
{
    private static string? EditorDir()
    {
        string? env = Environment.GetEnvironmentVariable("DK2_DIR");
        string dir = env ?? @"E:\GogGames\Dungeon Keeper 2";
        string editor = Path.Combine(dir, "Data", "editor");
        return Directory.Exists(editor) ? editor : null;
    }

    private static List<TerrainDef>? Terrain() =>
        EditorDir() is { } d
            ? KwdTerrainRooms.ParseTerrain(KwdReader.ReadFile(Path.Combine(d, "Terrain.kwd")))
            : null;

    private static List<RoomDef>? Rooms() =>
        EditorDir() is { } d
            ? KwdTerrainRooms.ParseRooms(KwdReader.ReadFile(Path.Combine(d, "Rooms.kwd")))
            : null;

    // ---------------- Terrain ----------------

    [Fact]
    public void Terrain_ParsesAllTypes_WithContiguousIds()
    {
        List<TerrainDef>? t = Terrain();
        if (t is null) return;

        Assert.Equal(41, t.Count);
        Assert.Equal(Enumerable.Range(1, 41).ToList(),
                     t.Select(x => (int)x.TerrainId).OrderBy(i => i).ToList());
        Assert.All(t, x => Assert.False(string.IsNullOrWhiteSpace(x.Name)));
    }

    [Fact]
    public void Terrain_CoreTypes_HaveExpectedProperties()
    {
        List<TerrainDef>? all = Terrain();
        if (all is null) return;
        TerrainDef T(byte id) => all.Single(x => x.TerrainId == id);

        TerrainDef rock = T(2);
        Assert.Equal("Rock", rock.Name);
        Assert.True(rock.IsSolid);
        Assert.True(rock.IsTaggable);          // diggable
        Assert.Equal(165, rock.StartingHealth);

        TerrainDef impenetrable = T(1);
        Assert.True(impenetrable.IsImpenetrable);
        Assert.False(impenetrable.IsTaggable); // cannot be dug

        TerrainDef gold = T(6);
        Assert.Equal(3000, gold.GoldValue);
        Assert.True(gold.IsTaggable);

        TerrainDef gems = T(7);
        Assert.Equal(3000, gems.GoldValue);
        // Gems are inexhaustible: destroying them yields gems again.
        Assert.Equal(7, gems.DestroyedTypeTerrainId);

        Assert.True(T(4).IsWater);
        Assert.True(T(5).IsLava);
    }

    [Fact]
    public void Terrain_TransitionGraph_EncodesDigAndClaimRules()
    {
        List<TerrainDef>? all = Terrain();
        if (all is null) return;
        TerrainDef T(byte id) => all.Single(x => x.TerrainId == id);

        // Rock, when dug out, becomes Dirt Path.
        Assert.Equal("Rock", T(2).Name);
        Assert.Equal("Dirt Path", T(T(2).DestroyedTypeTerrainId).Name);

        // Rock, reinforced to full health, becomes Reinforced Wall.
        Assert.Equal("Reinforced Wall", T(T(2).MaxHealthTypeTerrainId).Name);

        // Reinforced Wall, destroyed, falls back to Rock.
        Assert.Equal("Rock", T(T(9).DestroyedTypeTerrainId).Name);

        // Dirt Path, fully claimed, becomes Claimed Path.
        Assert.Equal("Dirt Path", T(3).Name);
        Assert.Equal("Claimed Path", T(T(3).MaxHealthTypeTerrainId).Name);
    }

    [Fact]
    public void Terrain_HeroLair_HealsRatherThanDamages()
    {
        List<TerrainDef>? all = Terrain();
        if (all is null) return;

        // Damage is signed: Hero Lair stores -67 (heals). Reading it unsigned
        // would give 65469, which is how the field's signedness was discovered.
        TerrainDef heroLair = all.Single(x => x.TerrainId == 35);
        Assert.Equal("Hero Lair", heroLair.Name);
        Assert.True(heroLair.Damage < 0, $"expected healing, got {heroLair.Damage}");

        // Lava and water damage.
        Assert.True(all.Single(x => x.TerrainId == 5).Damage > 0);
    }

    // ---------------- Rooms ----------------

    [Fact]
    public void Rooms_ParseAllTypes_WithContiguousIds()
    {
        List<RoomDef>? r = Rooms();
        if (r is null) return;

        Assert.Equal(26, r.Count);
        Assert.Equal(Enumerable.Range(1, 26).ToList(),
                     r.Select(x => (int)x.RoomId).OrderBy(i => i).ToList());
    }

    [Fact]
    public void RoomIds_MatchAndExtendFlameNaming()
    {
        List<RoomDef>? all = Rooms();
        if (all is null) return;
        RoomDef R(RoomTypeId id) => all.Single(x => x.RoomId == (byte)id);

        // Ids Flame already knew.
        Assert.Equal("Treasury", R(RoomTypeId.Treasury).Name);
        Assert.Equal("Library", R(RoomTypeId.Library).Name);
        Assert.Equal("Work Shop", R(RoomTypeId.Workshop).Name);
        Assert.Equal("Temple", R(RoomTypeId.Temple).Name);
        Assert.Equal("Crypt", R(RoomTypeId.Crypt).Name);

        // Ids Flame left as unk_N, resolved from this data.
        Assert.Equal("Lair", R(RoomTypeId.Lair).Name);
        Assert.Equal("Training Room", R(RoomTypeId.TrainingRoom).Name);
        Assert.Equal("Hero Stone Bridge", R(RoomTypeId.HeroStoneBridge).Name);
    }

    [Fact]
    public void Rooms_TerrainIds_ResolveAgainstTerrainTable()
    {
        List<RoomDef>? rooms = Rooms();
        List<TerrainDef>? terrain = Terrain();
        if (rooms is null || terrain is null) return;

        // Every room lays down a real terrain type, and that terrain is flagged
        // as a room tile. Cross-parser consistency check.
        foreach (RoomDef r in rooms)
        {
            TerrainDef t = terrain.Single(x => x.TerrainId == r.TerrainId);
            Assert.True(t.IsRoom || t.IsOwnable,
                $"{r.Name} lays terrain '{t.Name}' which is neither room nor ownable");
        }
    }

    [Fact]
    public void Rooms_SpawningRooms_ReferenceCorrectCreatures()
    {
        List<RoomDef>? rooms = Rooms();
        if (rooms is null) return;

        string? env = Environment.GetEnvironmentVariable("DK2_DIR");
        string dir = env ?? @"E:\GogGames\Dungeon Keeper 2";
        List<CreatureDef> creatures = KwdCreatures.Parse(
            KwdReader.ReadFile(Path.Combine(dir, "Data", "editor", "Creatures.kwd")));

        // Prison produces Skeletons; Graveyard produces Vampires.
        // Confirms CreatedCreatureId offset against the creature table.
        RoomDef prison = rooms.Single(r => r.RoomId == (byte)RoomTypeId.Prison);
        RoomDef graveyard = rooms.Single(r => r.RoomId == (byte)RoomTypeId.Graveyard);

        Assert.Equal("Skeleton", creatures.Single(c => c.CreatureId == prison.CreatedCreatureId).Name);
        Assert.Equal("Vampire", creatures.Single(c => c.CreatureId == graveyard.CreatedCreatureId).Name);
    }

    [Fact]
    public void Rooms_Costs_AreSane()
    {
        List<RoomDef>? all = Rooms();
        if (all is null) return;

        RoomDef R(RoomTypeId id) => all.Single(x => x.RoomId == (byte)id);
        Assert.Equal(200, R(RoomTypeId.Treasury).Cost);
        Assert.Equal(300, R(RoomTypeId.Lair).Cost);
        Assert.Equal(600, R(RoomTypeId.Library).Cost);
        Assert.Equal(3000, R(RoomTypeId.Temple).Cost);

        // Portal and Dungeon Heart cannot be bought.
        Assert.Equal(0, R(RoomTypeId.Portal).Cost);
        Assert.Equal(0, R(RoomTypeId.DungeonHeart).Cost);

        Assert.All(all, r => Assert.InRange(r.ReturnPercentage, (byte)0, (byte)100));
    }

    // ---------------- Map grid ----------------

    [Fact]
    public void EveryCampaignMap_HasParsableGrid_WithValidTerrainIds()
    {
        string? editor = EditorDir();
        if (editor is null) return;

        List<TerrainDef> terrain = KwdTerrainRooms.ParseTerrain(
            KwdReader.ReadFile(Path.Combine(editor, "Terrain.kwd")));
        var validTerrainIds = terrain.Select(t => t.TerrainId).ToHashSet();

        int gridsFound = 0;
        foreach (string file in Directory.GetFiles(Path.Combine(editor, "maps"), "*.kld")
                                         .Where(f => f.EndsWith("Map.kld", StringComparison.OrdinalIgnoreCase)))
        {
            MapGrid? grid = MapGrid.Parse(KwdReader.ReadFile(file));
            if (grid is null) continue;
            gridsFound++;

            Assert.InRange(grid.Width, 1, 512);
            Assert.InRange(grid.Height, 1, 512);
            Assert.Equal(grid.Width * grid.Height, grid.Tiles.Count);

            // Every tile must reference a terrain type that actually exists.
            foreach (MapTile t in grid.Tiles)
                Assert.Contains(t.TerrainId, validTerrainIds);
        }

        Assert.True(gridsFound > 50, $"expected many map grids, found {gridsFound}");
    }
}
