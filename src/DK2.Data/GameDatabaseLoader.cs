// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
        List<KeeperSpellDef> keeperSpells = KwdKeeperSpells.Parse(
            KwdReader.ReadFile(Path.Combine(editor, "KeeperSpells.kwd")));
        List<ObjectDef> objects = KwdObjects.Parse(
            KwdReader.ReadFile(Path.Combine(editor, "Objects.kwd")));
        List<DoorDef> doors = KwdDoors.Parse(
            KwdReader.ReadFile(Path.Combine(editor, "Doors.kwd")));
        List<TrapDef> traps = KwdTraps.Parse(
            KwdReader.ReadFile(Path.Combine(editor, "Traps.kwd")));

        return new GameDatabase(
            terrain, rooms, creatures, variables, keeperSpells, objects, doors, traps);
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

    /// <summary>
    /// Reads one level's own VARIABLES table — <c>&lt;name&gt;Variables.kld</c> —
    /// or null when the level has no such file.
    ///
    /// <para><b>Every shipped level has one, and they are not decoration.</b>
    /// Twenty-six variable ids are overridden by at least one level, including
    /// <c>MinimumImpThreshold</c> (0 on four secret levels and the front end,
    /// against a global 4) and both payday variables (1200 on level 1, against a
    /// global 600). Loading only <c>GlobalVariables.kwd</c> — which is all this
    /// loader did until 2026-09-04 — plays every level with the wrong numbers
    /// wherever it disagrees. Apply it with
    /// <see cref="GameDatabase.ForLevel"/>.</para>
    ///
    /// <para>It also carries the two lists the global table leaves empty: the
    /// level's <c>CreaturePool</c> (level 1: fifteen Goblins) and its
    /// <c>Availability</c> set, which is what decides the rooms, traps, doors and
    /// spells that level offers.</para>
    /// </summary>
    public static GlobalVariables? LoadCampaignVariables(string installDir, string levelName)
    {
        string file = Path.Combine(
            installDir, "Data", EditorDir, "maps", levelName + "Variables.kld");

        return File.Exists(file) ? KwdVariables.Parse(KwdReader.ReadFile(file)) : null;
    }

    /// <summary>
    /// Reads one level's player slots — <c>&lt;name&gt;Players.kld</c> — or null
    /// when the level has no such file. Every shipped level has one, holding the
    /// good and neutral rows plus the human and any computer keepers; the
    /// keepers' tuning blocks are what <c>docs/KEEPER_AI.md</c> documents.
    /// </summary>
    public static List<PlayerDef>? LoadCampaignPlayers(string installDir, string levelName)
    {
        string file = Path.Combine(
            installDir, "Data", EditorDir, "maps", levelName + "Players.kld");

        return File.Exists(file) ? KwdPlayers.Parse(KwdReader.ReadFile(file)) : null;
    }

    /// <summary>
    /// The database as one campaign level plays it: the install's definitions
    /// with that level's VARIABLES applied over the global table.
    ///
    /// The shape a caller wants in one call, and the one to prefer over
    /// <see cref="Load"/> whenever a level is named.
    /// </summary>
    public static GameDatabase LoadForCampaignLevel(string installDir, string levelName) =>
        Load(installDir).ForLevel(LoadCampaignVariables(installDir, levelName));
}
