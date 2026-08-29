using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data;

/// <summary>
/// Builds a <see cref="GameDatabase"/> from an original Dungeon Keeper 2
/// installation. This is the only place that turns files into definitions;
/// everything downstream works from the immutable database.
/// </summary>
public static class GameDatabaseLoader
{
    /// <summary>Editor KWDs, relative to the install root.</summary>
    private const string EditorDir = "editor";

    public static GameDatabase Load(string installDir)
    {
        string editor = Path.Combine(installDir, "Data", EditorDir);
        if (!Directory.Exists(editor))
            throw new DirectoryNotFoundException(
                $"'{editor}' does not exist; '{installDir}' is not a DK2 installation.");

        List<TerrainDef> terrain = KwdTerrainRooms.ParseTerrain(
            KwdReader.ReadFile(Path.Combine(editor, "Terrain.kwd")));
        List<RoomDef> rooms = KwdTerrainRooms.ParseRooms(
            KwdReader.ReadFile(Path.Combine(editor, "Rooms.kwd")));
        List<CreatureDef> creatures = KwdCreatures.Parse(
            KwdReader.ReadFile(Path.Combine(editor, "Creatures.kwd")));
        GlobalVariables variables = KwdVariables.Parse(
            KwdReader.ReadFile(Path.Combine(editor, "GlobalVariables.kwd")));

        return new GameDatabase(terrain, rooms, creatures, variables);
    }

    /// <summary>
    /// Loads from the locally configured install, or returns null when there is
    /// none — the shape callers need so tests and CI can skip cleanly.
    /// </summary>
    public static GameDatabase? LoadFromInstall() =>
        Dk2Install.Find() is { } dir ? Load(dir) : null;

    /// <summary>Reads one campaign level's tile grid, e.g. "level1".</summary>
    public static LevelMap? LoadCampaignMap(string installDir, string levelName)
    {
        string maps = Path.Combine(installDir, "Data", EditorDir, "maps");
        string file = Path.Combine(maps, levelName + "Map.kld");
        if (!File.Exists(file))
        {
            // The campaign uses both "level1Map.kld" and bare "<name>.kld" forms.
            file = Path.Combine(maps, levelName + ".kld");
            if (!File.Exists(file)) return null;
        }
        return MapGrid.Parse(KwdReader.ReadFile(file));
    }
}
