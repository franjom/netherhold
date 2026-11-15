// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text;
using DK2.AI.Players;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Players;

namespace DK2.Harness;

/// <summary>
/// <c>playbook-available &lt;scenario&gt;</c> (campaign bot stage 3 plan, Task 3b): what a level offers its
/// player, for a playbook's <c>available</c> section; an authoring tool, never run during play. It runs
/// the scenario as a player run does, skipping the intro, and at the first playable tick reads the
/// bottom panel (<see cref="BottomPanelView"/>, as the player's observation does, doors and traps
/// included) and the creatures a keeper's portal may bring: those in the level's creature pool
/// (<see cref="PortalRules.PoolLimit"/>) that come by portal at all (<see cref="PortalRules.CanComeFromPortal"/>).
/// The portal ignores the creature availability table (ADR-0316), so a creature MAKE is not listed. Every other trigger MAKE action
/// (type 6, <c>0x00518AD0</c>) for the player and every research-list entry still researchable is
/// listed under <c>may-change</c>. The start-of-level triggers have run by the tick read
/// (<c>read-at-tick</c>), so a MAKE they performed shows in <c>start</c> and again in <c>may-change</c>.
/// </summary>
public static class PlaybookAvailableCommand
{
    /// <summary>The ticks a probe may take to reach its first playable observation.</summary>
    public const int ProbeTicks = 4000;

    public sealed record Available(string Level, IReadOnlyList<PlaybookItem> Start, IReadOnlyList<PlaybookItem> Creatures,
        IReadOnlyList<PlaybookChange> MayChange, long Tick);

    /// <summary>Skips the intro, then waits: the run only has to reach a playable tick.</summary>
    private sealed class Probe : IPlayerController
    {
        public PlayerDecision Decide(PlayerObservation observation) =>
            (observation.Offers.Actions & PlayerActionKinds.SkipIntro) != 0
                ? new PlayerDecision([new PlayerAction.SkipIntro()], 1, "skip the intro")
                : PlayerDecision.Wait(1, "probe");
    }

    /// <summary>The command: writes the section to stdout, or into the playbook at <paramref name="output"/>
    /// (replacing its <c>available</c> section, or starting a new playbook). 0, or 2 for a refusal.</summary>
    public static int Run(string install, string scenarioPath, PlayerId player, string? output, TextWriter log, TextWriter error)
    {
        try
        {
            Scenario scenario = Scenario.Load(scenarioPath);
            Available available = Generate(new ScenarioRunner(GameDatabaseLoader.Load(install), install), GameDatabaseLoader.Load(install), install, scenario, player);
            string section = Yaml(available);
            if (output is null) { log.Write(section); return 0; }
            string? existing = File.Exists(output) ? File.ReadAllText(output) : null;
            File.WriteAllText(output, Replace(existing, available.Level, section));
            log.WriteLine($"wrote the available section of {output}: {available.Start.Count} offered, {available.Creatures.Count} creatures, {available.MayChange.Count} may change");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"refused: {ex.Message}");
            return 2;
        }
    }

    public static Available Generate(ScenarioRunner runner, GameDatabase database, string installDirectory, Scenario scenario, PlayerId player)
    {
        if (!scenario.Map.StartsWith(ScenarioRunner.CampaignPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{scenario.Id}: '{scenario.Map}' is not a campaign level");
        string level = scenario.Map[ScenarioRunner.CampaignPrefix.Length..].Trim();
        Available? found = null;
        var options = new PlayerRunOptions(new Probe(), player, new PlayerRunId("playbook-available"))
        {
            MaxTicks = ProbeTicks,
            FinaleTicks = 0,
            Observed = (world, observation) =>
            {
                if (found is null && observation.Phase == PlayerPhase.Playing)
                    found = Read(database, installDirectory, level, world, player);
            },
            StopRequested = () => found is null ? null : "the level's offers are read",
        };
        runner.Run(scenario, new RunOptions { PlayerRun = options });
        return found ?? throw new InvalidDataException($"{scenario.Id}: no playable tick within {ProbeTicks} ticks");
    }

    private static Available Read(GameDatabase database, string installDirectory, string level, WorldState world, PlayerId player)
    {
        var panel = new BottomPanelView(database, player, (_, name) => name);
        panel.Capture(world);
        var start = new List<PlaybookItem>();
        foreach (BottomPanelChoice choice in panel.Choices)
            if (choice.Visible && choice.Selectable && ItemOf(database, choice.Kind, choice.Key.Value) is PlaybookItem item && !start.Contains(item))
                start.Add(item);

        // The pool is the level's: the base database merged with the level's own variables.
        GameDatabase merged = database.ForLevel(GameDatabaseLoader.LoadCampaignVariables(installDirectory, level),
            GameDatabaseLoader.LoadCampaignLevel(installDirectory, level));
        RoomDef portal = merged.Room(RoomTypeId.Portal);
        List<PlaybookItem> creatures = [.. merged.Creatures.Values
            .Where(c => PortalRules.PoolLimit(merged, player, c.CreatureId) > 0 && PortalRules.CanComeFromPortal(c, portal))
            .OrderBy(c => c.CreatureId).Select(c => new PlaybookItem(PlaybookItemKind.Creature, c.Name))];

        var changes = new List<PlaybookChange>();
        List<TriggerRecord> triggers = GameDatabaseLoader.LoadCampaignTriggers(installDirectory, level)
            ?? throw new InvalidDataException($"campaign:{level} has no TRIGGERS file");
        foreach (ActionTriggerRecord make in triggers.OfType<ActionTriggerRecord>().Where(r => r.Type == 6).OrderBy(r => r.TriggerId))
        {
            IReadOnlyDictionary<string, long> p = make.Parameters;
            if (p["PlayerId"] != player.Value || p["AvailabilityKind"] is < 1 or > 5) continue;
            if ((AvailabilityKind)p["AvailabilityKind"] == AvailabilityKind.Creature) continue;   // the portal reads the pool
            if (ItemOf(database, (AvailabilityKind)p["AvailabilityKind"], (int)p["TypeId"]) is not PlaybookItem item) continue;
            changes.Add(new PlaybookChange(item, p["Available"] != 0, string.Create(CultureInfo.InvariantCulture, $"trigger {make.TriggerId} MAKE")));
        }
        foreach (ResearchEntry entry in database.Variables.Research)
        {
            AvailabilityKind kind = ResearchTreeRules.AvailabilityKindOf(entry.Kind);
            if (kind == AvailabilityKind.None || !ResearchTreeRules.IsResearchable(world, player, entry)) continue;
            if (ItemOf(database, kind, entry.TypeId) is PlaybookItem item && !changes.Any(c => c.Item == item && c.By == "research"))
                changes.Add(new PlaybookChange(item, true, "research"));
        }
        return new Available(ScenarioRunner.CampaignPrefix + level, start, creatures, changes, world.CurrentTick.Value);
    }

    private static PlaybookItem? ItemOf(GameDatabase database, BottomPanelChoiceKind kind, byte id) => kind switch
    {
        BottomPanelChoiceKind.Room => ItemOf(database, AvailabilityKind.Room, id),
        BottomPanelChoiceKind.Spell => ItemOf(database, AvailabilityKind.Spell, id),
        BottomPanelChoiceKind.Door => ItemOf(database, AvailabilityKind.Door, id),
        BottomPanelChoiceKind.Trap => ItemOf(database, AvailabilityKind.Trap, id),
        _ => null,
    };

    /// <summary>An item by its availability kind and type id, named as the game data names it.</summary>
    public static PlaybookItem? ItemOf(GameDatabase database, AvailabilityKind kind, int id) => kind switch
    {
        AvailabilityKind.Room when Enum.IsDefined(typeof(RoomTypeId), (byte)id) => PlaybookItem.Room((RoomTypeId)id),
        AvailabilityKind.Spell when database.KeeperSpellById(id) is KeeperSpellDef spell => new PlaybookItem(PlaybookItemKind.Spell, spell.Name),
        AvailabilityKind.Door when database.Doors.TryGetValue((byte)id, out DoorDef? door) => new PlaybookItem(PlaybookItemKind.Door, door.Name),
        AvailabilityKind.Trap when database.Traps.TryGetValue((byte)id, out TrapDef? trap) => new PlaybookItem(PlaybookItemKind.Trap, trap.Name),
        AvailabilityKind.Creature when database.Creatures.TryGetValue((byte)id, out CreatureDef? creature) => new PlaybookItem(PlaybookItemKind.Creature, creature.Name),
        _ => null,
    };

    /// <summary>The <c>available</c> section as YAML, deterministic: the header comment, then start in the
    /// panel's order, creatures by id, may-change by trigger id then research list order.</summary>
    public static string Yaml(Available available)
    {
        var text = new StringBuilder();
        text.Append("available:          # generated by `DK2.Harness playbook-available`; never hand-written\n");
        text.Append(CultureInfo.InvariantCulture, $"  read-at-tick: {available.Tick}\n");
        text.Append("  start: [").Append(string.Join(", ", available.Start.Select(Quote))).Append("]\n");
        text.Append("  creatures: [").Append(string.Join(", ", available.Creatures.Select(Quote))).Append("]\n");
        if (available.MayChange.Count == 0) text.Append("  may-change: []\n");
        else
        {
            text.Append("  may-change:\n");
            foreach (PlaybookChange change in available.MayChange)
                text.Append("    - { item: ").Append(Quote(change.Item)).Append(", to: ")
                    .Append(change.ToAvailable ? "available" : "unavailable").Append(", by: \"").Append(change.By).Append("\" }\n");
        }
        return text.ToString();
    }

    private static string Quote(PlaybookItem item) => "\"" + item.ToString().Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary><paramref name="playbook"/>'s text with its <c>available</c> section (the key and its indented
    /// lines) replaced by <paramref name="section"/>, everything else kept, line endings included; or a new
    /// playbook holding the level and the section when it is empty. Refused for another level's playbook.</summary>
    public static string Replace(string? playbook, string level, string section)
    {
        if (string.IsNullOrWhiteSpace(playbook)) return $"level: {level}\n" + section;
        string newline = playbook.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = playbook.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? written = lines.FirstOrDefault(l => l.StartsWith("level:", StringComparison.Ordinal))?["level:".Length..].Split('#')[0].Trim();
        if (!string.Equals(written, level, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"the playbook is for {written ?? "no level"}, the section is {level}'s");
        int from = Array.FindIndex(lines, l => l.StartsWith("available:", StringComparison.Ordinal));
        if (from < 0) throw new InvalidDataException("the playbook has no top-level available: section to replace");
        int to = from + 1;
        while (to < lines.Length && lines[to].StartsWith(' ')) to++;
        string text = string.Join("\n", lines[..from]) + (from > 0 ? "\n" : "") + section + string.Join("\n", lines[to..]);
        return newline == "\n" ? text : text.Replace("\n", newline, StringComparison.Ordinal);
    }
}
