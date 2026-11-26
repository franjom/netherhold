// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.Simulation.Players;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Playbooks on shipped levels (campaign bot stage 3 plan, Task 3b): <c>playbook-available</c>
/// reads what a level offers, the loader refuses a plan the level cannot carry, and Level 1's playbook
/// plays exactly as the player does without one.</summary>
public class PlaybookFileTests
{
    private static readonly PlayerId Keeper = new(3);
    private const ulong Hash = 521;

    private static string PlaybookDirectory => Path.Combine(ScenarioCorpus.RepositoryRoot(), KeeperKnowledgeFile.DirectoryName, PlaybookFile.DirectoryName);

    private static PlaybookAvailableCommand.Available Generate(string scenario) =>
        PlaybookAvailableCommand.Generate(new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path), ScenarioCorpus.Database,
            Dk2InstallGate.Path, ScenarioCorpus.Load(scenario), Keeper);

    private static readonly Lazy<PlaybookAvailableCommand.Available> Level2 = new(() => Generate("campaign-player/level2/level2-seed1.yaml"));

    private static string Temp(string text)
    {
        string path = Path.Combine(Path.GetTempPath(), "dk2-playbook-" + Guid.NewGuid().ToString("N") + ".yaml");
        File.WriteAllText(path, text);
        return path;
    }

    [InstallFact]
    public void Level2OffersItsLairHatcheryAndTrainingRoomButNoTreasury()
    {
        PlaybookAvailableCommand.Available level2 = Level2.Value;
        Assert.Equal("campaign:level2", level2.Level);
        foreach (RoomTypeId room in (RoomTypeId[])[RoomTypeId.Lair, RoomTypeId.Hatchery, RoomTypeId.TrainingRoom])
            Assert.Contains(PlaybookItem.Room(room), level2.Start);
        Assert.DoesNotContain(PlaybookItem.Room(RoomTypeId.Treasury), level2.Start);
        Assert.DoesNotContain(level2.MayChange, c => c.Item == PlaybookItem.Room(RoomTypeId.Treasury));
        // Level 2's two research items (research-tree.md section 7).
        Assert.Equal(["spell:Create Imp", "spell:Thunderbolt"], level2.MayChange.Where(c => c.By == "research").Select(c => c.Item.ToString()));
    }

    [InstallFact]
    public void APlaybookPlanningATreasuryOnLevel2IsRefused()
    {
        string section = PlaybookAvailableCommand.Yaml(Level2.Value);
        string path = Temp("level: campaign:level2\n" + section + "plan:\n  rooms: [Lair, Treasury]\n");
        try
        {
            InvalidDataException refused = Assert.Throws<InvalidDataException>(() => PlaybookFile.Load(path, Hash));
            Assert.Contains("room:Treasury is planned but the level neither offers it at the start nor may make it available", refused.Message, StringComparison.Ordinal);
            File.WriteAllText(path, "level: campaign:level2\n" + section + "plan:\n  rooms: [Lair, Hatchery]\n");
            Assert.Equal([RoomTypeId.Lair, RoomTypeId.Hatchery], PlaybookFile.Load(path, Hash).Playbook.Rooms);
        }
        finally { File.Delete(path); }
    }

    [InstallFact]
    public void TheCommittedAvailableSectionsAreWhatTheLevelsGenerateNow()
    {
        string committed = File.ReadAllText(Path.Combine(PlaybookDirectory, "level1.yaml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        string fresh = PlaybookAvailableCommand.Yaml(Generate("campaign-player/level1.yaml"));
        Assert.Equal(committed, PlaybookAvailableCommand.Replace(committed, "campaign:level1", fresh));
        Assert.Empty(PlaybookFile.Load(Path.Combine(PlaybookDirectory, "level1.yaml"), GameSimulation.CurrentHashVersion).Warnings);
    }

    /// <summary>One Level 1 seed-1 attempt as <c>run --campaign-player 3</c> plays it, with or without a playbook.</summary>
    private static (ScenarioResult Result, IReadOnlyList<(long, string)> Decisions) Play(PlaybookFile.Loaded? playbook)
    {
        const string scenario = "campaign-player/level1.yaml";
        var spec = new CampaignPlayerSpec(Keeper, Knowledge: KeeperKnowledgeFile.DefaultDirectory(ScenarioCorpus.PathOf(scenario)))
        {
            Playbook = playbook is null ? null : "level1.yaml",
            LoadedPlaybook = playbook,
        };
        ScenarioResult result = new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path).Run(ScenarioCorpus.Load(scenario), new RunOptions
        {
            PlayerRun = PlayerRunCommand.PlayerRunFor(ScenarioCorpus.Database, spec, new PlayerRunId("level1-seed1"), null),
        });
        return (result, [.. result.PlayerRun!.Decisions.Select(d => (d.Tick.Value, d.Reason))]);
    }

    [InstallFact]
    public void Level1sPlaybookPlaysAsThePlayerDoesWithoutOne()
    {
        PlaybookFile.Loaded playbook = PlaybookFile.Load(Path.Combine(PlaybookDirectory, "level1.yaml"), GameSimulation.CurrentHashVersion);
        (ScenarioResult plain, IReadOnlyList<(long, string)> plainDecisions) = Play(null);
        (ScenarioResult played, IReadOnlyList<(long, string)> playedDecisions) = Play(playbook);
        Assert.Equal(PlayerRunStop.Won, played.PlayerRun!.Stop);
        Assert.Equal(plain.FinalStateHash, played.FinalStateHash);
        Assert.Equal(plainDecisions, playedDecisions);
    }

    [Fact]
    public void AnUnknownKeyABadItemAndABadProvenanceAreAllNamed()
    {
        string path = Temp("""
            level: campaign:level1
            available:
              read-at-tick: 3
              start: ["room:Lair", "chair:Throne"]
              creatures: []
              may-change: [ { item: "room:Library", to: sometimes, by: "x" } ]
            tunable:
              - { path: strategy.room-side, value: 4, min: 3, max: 6, provenance: guessed }
            """);
        try
        {
            string message = Assert.Throws<InvalidDataException>(() => PlaybookFile.Load(path, Hash)).Message;
            Assert.Contains("available start: 'chair:Throne' is not kind:Name", message, StringComparison.Ordinal);
            Assert.Contains("may-change room:Library: to 'sometimes' is not available or unavailable", message, StringComparison.Ordinal);
            Assert.Contains("tunable strategy.room-side: provenance 'guessed' is not researched or tuned", message, StringComparison.Ordinal);
            File.WriteAllText(path, "level: campaign:level1\navailable: { start: [], creatures: [], may-change: [] }\nplans: {}\n");
            Assert.Throws<InvalidDataException>(() => PlaybookFile.Load(path, Hash));   // a misspelt section is refused
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CapsStandInOnlyForLimitsTheCommandLineDidNotGive()
    {
        var caps = new PlaybookCaps(MaxTicks: 57600, MaxActions: 8192, WallSeconds: 2400, OutputMiB: 256);
        var given = new CampaignPlayerSpec(Keeper, MaxTicks: 1000) { GivenLimits = PlayerLimitFlags.MaxTicks };
        CampaignPlayerSpec capped = PlayerRunCommand.WithCaps(given, caps);
        Assert.Equal((1000, 8192, 2400, 256), (capped.MaxTicks, capped.MaxActions, capped.WallSeconds, capped.OutputMiB));
        Assert.Equal(new CampaignPlayerSpec(Keeper).MaxTicks, PlayerRunCommand.WithCaps(new CampaignPlayerSpec(Keeper), new PlaybookCaps()).MaxTicks);

        string path = Temp("level: campaign:level1\navailable: { start: [], creatures: [], may-change: [] }\ncaps: { wall-seconds: 700 }\n");
        try
        {
            string[] args = ["--campaign-player", "3", "--playbook", path];
            Assert.Equal(Path.GetFullPath(path), HarnessCommandLine.RunOptions(args)!.Player!.Playbook);
            Assert.Equal(PlayerLimitFlags.None, HarnessCommandLine.RunOptions(args)!.Player!.GivenLimits);
            Assert.Equal(PlayerLimitFlags.MaxTicks | PlayerLimitFlags.OutputMiB,
                HarnessCommandLine.RunOptions([.. args, "--player-max-ticks", "9", "--player-output-mib", "8"])!.Player!.GivenLimits);
            // Under watch the playbook's wall cap is each run's wall limit, and must stay below the watchdog.
            List<WatchJob> jobs = HarnessCommandLine.WatchJobs(["x.yaml"], args, timeoutSeconds: 900)!;
            Assert.Contains("700", jobs[0].Args);
            Assert.Null(HarnessCommandLine.WatchJobs(["x.yaml"], args, timeoutSeconds: 700));
            Assert.Null(HarnessCommandLine.RunOptions(["--playbook", path]));                  // no player to give it to
        }
        finally { File.Delete(path); }
    }

    [InstallFact]
    public void ARunRefusesAnotherLevelsPlaybookAndACapBeyondTheScenario()
    {
        Scenario level1 = ScenarioCorpus.Load("campaign-player/level1.yaml");
        string other = Temp("level: campaign:level2\navailable: { start: [], creatures: [], may-change: [] }\n");
        string longer = Temp("level: campaign:level1\navailable: { start: [], creatures: [], may-change: [] }\ncaps: { max-ticks: " + (level1.DurationTicks + 1) + " }\n");
        string root = Path.Combine(Path.GetTempPath(), "dk2-playbook-runs-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach ((string path, string why) in (ValueTuple<string, string>[])[(other, "the playbook is for campaign:level2"), (longer, "exceeds the scenario's duration_ticks")])
            {
                var error = new StringWriter();
                int exit = PlayerRunCommand.Run(new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path), ScenarioCorpus.Database, Dk2InstallGate.Path,
                    level1, HarnessCommandLine.RunOptions(["--campaign-player", "3", "--playbook", path])!, root, "r", TextWriter.Null, error);
                Assert.Equal(2, exit);
                Assert.Contains(why, error.ToString(), StringComparison.Ordinal);
            }
            Assert.False(Directory.Exists(root));                                                  // refused before any evidence folder
        }
        finally
        {
            File.Delete(other);
            File.Delete(longer);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [InstallFact]
    public void PortalCreaturesComeFromTheLevelsPoolNotTheAvailabilityTable()
    {
        // Level 3's script MAKEs Warlocks, Goblins and Fireflies unavailable at its start, yet the portal reads
        // the pool (ADR-0316: DK2's own portal still delivered all three); a creature MAKE is not listed.
        PlaybookAvailableCommand.Available level3 = Generate("campaign-player/level3/level3-seed1.yaml");
        foreach (string name in (string[])["Warlock", "Goblin", "Firefly"])
            Assert.Contains(new PlaybookItem(PlaybookItemKind.Creature, name), level3.Creatures);
        Assert.DoesNotContain(level3.MayChange, c => c.Item.Kind == PlaybookItemKind.Creature);
    }

    [Fact]
    public void ReplacingTheAvailableSectionKeepsEverythingElseAndRefusesAnotherLevel()
    {
        const string playbook = "# header\r\nlevel: campaign:level3   # three\r\navailable:\r\n  start: []\r\n  creatures: []\r\n\r\n# the plan\r\nplan:\r\n  rooms: [Lair]\r\n";
        string replaced = PlaybookAvailableCommand.Replace(playbook, "campaign:level3", "available:\n  start: [\"room:Lair\"]\n");
        Assert.Equal("# header\r\nlevel: campaign:level3   # three\r\navailable:\r\n  start: [\"room:Lair\"]\r\n\r\n# the plan\r\nplan:\r\n  rooms: [Lair]\r\n", replaced);
        string refused = Assert.Throws<InvalidDataException>(() => PlaybookAvailableCommand.Replace(playbook, "campaign:level1", "available:\n")).Message;
        Assert.Contains("the playbook is for campaign:level3, the section is campaign:level1's", refused, StringComparison.Ordinal);
        Assert.Equal("level: campaign:level1\navailable:\n", PlaybookAvailableCommand.Replace(null, "campaign:level1", "available:\n"));
    }

    [InstallFact]
    public void APlaybooksRulesNeedKnowledgeToActOn()
    {
        Scenario level1 = ScenarioCorpus.Load("campaign-player/level1.yaml");
        string path = Temp("level: campaign:level1\navailable: { start: [], creatures: [], may-change: [] }\nplan: { rules: [R4] }\n");
        try
        {
            var error = new StringWriter();
            int exit = PlayerRunCommand.Run(new ScenarioRunner(ScenarioCorpus.Database, Dk2InstallGate.Path), ScenarioCorpus.Database, Dk2InstallGate.Path,
                level1, HarnessCommandLine.RunOptions(["--campaign-player", "3", "--playbook", path, "--player-knowledge", "none"])!,
                Path.GetTempPath(), "never-written", TextWriter.Null, error);
            Assert.Equal(2, exit);
            Assert.Contains("the playbook asks for rules R4, but the player reads no knowledge", error.ToString(), StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnEventNeedsItsIdWhenAndExpectAndANoteMayBePlaced()
    {
        string path = Temp("level: campaign:level1\navailable: { start: [], creatures: [], may-change: [] }\n"
            + "notes: [ { kind: gold, text: a seam, at: [40, 12], provenance: researched, source: map dump } ]\n");
        try
        {
            Assert.Equal("a seam at (40, 12) (researched)", PlaybookFile.Load(path, Hash).Playbook.Notes[0].Text);
            File.AppendAllText(path, "events: [ { id: first-wave, source: trigger 7 } ]\n");
            Assert.Contains("event 1: id, when and expect are all required",
                Assert.Throws<InvalidDataException>(() => PlaybookFile.Load(path, Hash)).Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void APlaybookMaySwitchOnOnlyARuleThatWaitsForOne()
    {
        string knowledge = Path.Combine(ScenarioCorpus.RepositoryRoot(), KeeperKnowledgeFile.DirectoryName);
        Assert.DoesNotContain("R4", KeeperKnowledgeFile.Load(knowledge).ActingRules);
        KeeperKnowledgeFile.Loaded r4 = KeeperKnowledgeFile.Load(knowledge, playbookRules: ["R4"]);
        Assert.Contains("R4", r4.ActingRules);
        Assert.Equal(25, r4.TileTargets[RoomTypeId.Hatchery]);
        string refused = Assert.Throws<InvalidDataException>(() => KeeperKnowledgeFile.Load(knowledge, playbookRules: ["R3", "R99"])).Message;
        Assert.Contains("the playbook asks for rule R3", refused, StringComparison.Ordinal);
        Assert.Contains("the playbook asks for rule R99", refused, StringComparison.Ordinal);
    }
}
