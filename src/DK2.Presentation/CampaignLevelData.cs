// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A campaign level's records read from the install, as every engine adapter starts one: the level's database,
/// map, definition, players, things and triggers. A missing record is an <see cref="InvalidOperationException"/>.</summary>
public sealed class CampaignLevelData
{
    /// <summary>Ticks a new level runs before its first frame. The Heart's plug lid is asked for on game turn 1 and by
    /// nothing earlier, so DK2 draws no frame before its first turn (heart-intro.md §4, an inference from
    /// 0x004D9A60's guard); the first frame shows the lid, not the open pit beneath it. A restored checkpoint already
    /// includes its starting turn and runs none.</summary>
    public const int TicksBeforeFirstFrame = 1;

    /// <summary>Records already read; <see cref="Load"/> outside tests.</summary>
    internal CampaignLevelData(string name, GameDatabase database, LevelMap map, LevelDef level, List<PlayerDef> players,
        List<ThingRecord> things, List<TriggerRecord> triggers)
    {
        Name = name; Database = database; Map = map; Level = level; Players = players; Things = things; Triggers = triggers;
    }

    public string Name { get; }
    public GameDatabase Database { get; }
    public LevelMap Map { get; }
    public LevelDef Level { get; }
    public List<PlayerDef> Players { get; }
    public List<ThingRecord> Things { get; }
    public List<TriggerRecord> Triggers { get; }

    /// <summary>Reads level <paramref name="levelName"/> (e.g. <c>Level1</c>) from <paramref name="install"/>.</summary>
    public static CampaignLevelData Load(string install, string levelName)
    {
        return Load(install, levelName, database: null);
    }

    /// <summary>Reads the level records, reusing the database if supplied by the restored simulation of the same level.</summary>
    public static CampaignLevelData Load(string install, string levelName, GameDatabase? database)
    {
        if (install is null)
        {
            throw new ArgumentNullException(nameof(install));
        }

        if (levelName is null)
        {
            throw new ArgumentNullException(nameof(levelName));
        }

        database ??= GameDatabaseLoader.LoadForCampaignLevel(install, levelName);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, levelName) ?? throw Missing(levelName, "map");
        LevelDef level = GameDatabaseLoader.LoadCampaignLevel(install, levelName) ?? throw Missing(levelName, "level definition");
        List<PlayerDef> players = GameDatabaseLoader.LoadCampaignPlayers(install, levelName) ?? throw Missing(levelName, "players");
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(install, levelName) ?? throw Missing(levelName, "things");
        List<TriggerRecord> triggers = GameDatabaseLoader.LoadCampaignTriggers(install, levelName) ?? throw Missing(levelName, "triggers");
        return new CampaignLevelData(levelName, database, map, level, players, things, triggers);
    }

    /// <summary>A fresh simulation of the level with its things placed and its scripts armed
    /// (<see cref="CampaignSimulationFactory"/>); each call builds a new one.</summary>
    public GameSimulation CreateSimulation(ulong seed, bool fogOfWarEnabled)
    {
        return CampaignSimulationFactory.Create(Database, Map, Level, Players, Things, Triggers, seed, fogOfWarEnabled);
    }

    private static InvalidOperationException Missing(string level, string what)
    {
        return new InvalidOperationException($"the install has no '{level}' {what}.");
    }
}
