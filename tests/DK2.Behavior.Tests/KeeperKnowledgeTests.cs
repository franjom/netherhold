// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation.Players;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The campaign player's knowledge (stage 3 plan, Task 3a): room facts generated from the
/// simulation's own rules on the shipped data, and the loader that turns them and the rules into
/// the player's options.</summary>
public class KeeperKnowledgeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly Lazy<IReadOnlyList<KeeperKnowledgeCommand.RoomFacts>> Generated = new(() =>
        KeeperKnowledgeCommand.Measure(ScenarioCorpus.Database,
            GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "level1")!));

    private static KeeperKnowledgeCommand.SizeFacts At(RoomTypeId room, int side) =>
        Generated.Value.Single(r => r.Room == room).Sizes.Single(s => s.Side == side);

    private static string KnowledgeDirectory => Path.Combine(ScenarioCorpus.RepositoryRoot(), KeeperKnowledgeFile.DirectoryName);

    [InstallFact]
    public void ALibrarysShelvesAndATemplesHandFollowTheirSquare()
    {
        Assert.Equal([1, 4, 9], [At(RoomTypeId.Library, 3).FloorSlots, At(RoomTypeId.Library, 4).FloorSlots, At(RoomTypeId.Library, 5).FloorSlots]);
        Assert.True(At(RoomTypeId.Library, 4).Attraction > At(RoomTypeId.Library, 3).Attraction);
        Assert.Equal([0, 0, 2], [At(RoomTypeId.Temple, 3).Attraction, At(RoomTypeId.Temple, 4).Attraction, At(RoomTypeId.Temple, 5).Attraction]);
        Assert.Equal(9 * 3000, At(RoomTypeId.Treasury, 3).Gold);
        Assert.DoesNotContain(Generated.Value, r => r.Room is RoomTypeId.DungeonHeart or RoomTypeId.Portal);
    }

    [InstallFact]
    public void TheCommittedFactsAreWhatTheRulesGenerateNow()
    {
        string committed = File.ReadAllText(Path.Combine(KnowledgeDirectory, KeeperKnowledgeFile.RoomsFile)).Replace("\r\n", "\n");
        Assert.Equal(KeeperKnowledgeCommand.Yaml(Generated.Value, ScenarioCorpus.Database), committed);
    }

    [InstallFact]
    public void EachLevelsResearchSetsTheLibrarysShelves()
    {
        var rows = new List<string>();
        foreach (string level in (string[])["level1", "level2", "level3", "level4", "level5", "level6a", "level6b", "level7", "level8", "level9", "level10"])
        {
            int researchable = KeeperKnowledgeFile.ResearchableItems(Dk2InstallGate.Path, "campaign:" + level, new PlayerId(3))!.Value;
            int slots = KeeperKnowledgeFile.ResearchSlots(researchable);
            KeeperKnowledgeCommand.SizeFacts? fits = Generated.Value.Single(r => r.Room == RoomTypeId.Library).Sizes.FirstOrDefault(s => s.FloorSlots >= slots);
            rows.Add($"{level}: {researchable} researchable, {slots} slots, Library {(fits is null ? "over 7x7" : $"{fits.Side}x{fits.Side}")}");
        }
        output.WriteLine(string.Join("\n", rows));
        // Recorded 2026-11-14 at hash 519, research-list entries only: Levels 2 and 7 leave two items (a 4x4 Library).
        Assert.Equal(
        [
            "level1: 0 researchable, 1 slots, Library 3x3", "level2: 2 researchable, 2 slots, Library 4x4",
            "level3: 0 researchable, 1 slots, Library 3x3", "level4: 1 researchable, 1 slots, Library 3x3",
            "level5: 0 researchable, 1 slots, Library 3x3", "level6a: 1 researchable, 1 slots, Library 3x3",
            "level6b: 1 researchable, 1 slots, Library 3x3", "level7: 2 researchable, 2 slots, Library 4x4",
            "level8: 0 researchable, 1 slots, Library 3x3", "level9: 0 researchable, 1 slots, Library 3x3",
            "level10: 1 researchable, 1 slots, Library 3x3",
        ], rows);
        Assert.Null(KeeperKnowledgeFile.ResearchableItems(Dk2InstallGate.Path, "fixture:empty", new PlayerId(3)));
    }

    [InstallFact]
    public void TheRepositorysKnowledgeLoadsWithTheLibraryRuleActing()
    {
        KeeperKnowledgeFile.Loaded withoutLevel = KeeperKnowledgeFile.Load(KnowledgeDirectory);
        Assert.DoesNotContain("R2", withoutLevel.ActingRules);                // no level, no research count
        KeeperKnowledgeFile.Loaded loaded = KeeperKnowledgeFile.Load(KnowledgeDirectory, researchable: 7);
        Assert.Equal(4, loaded.SlotTargets[RoomTypeId.Library]);            // 7 books: 3 full slots, one with room
        Assert.Contains("R2", loaded.ActingRules);
        Assert.DoesNotContain("R4", loaded.ActingRules);                    // applies: playbook
        Assert.Equal(5, loaded.Knowledge.Room(RoomTypeId.Temple)!.SmallestUsefulSide);
        Assert.Equal(KnowledgeDirectory, KeeperKnowledgeFile.DefaultDirectory(ScenarioCorpus.PathOf("campaign-player/level1.yaml")));
    }

    private static string Folder(string rooms, string rules)
    {
        string dir = Path.Combine(Path.GetTempPath(), "dk2-knowledge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, KeeperKnowledgeFile.RoomsFile), rooms);
        File.WriteAllText(Path.Combine(dir, KeeperKnowledgeFile.RulesFile), rules);
        return dir;
    }

    private static readonly string OneLibrary = $$"""
        hash_version: {{DK2.Simulation.GameSimulation.CurrentHashVersion}}
        rooms:
          - room: Library
            cost_per_tile: 600
            sizes: [ { side: 3, floor_slots: 1, attraction: 2, gold: 0 } ]
            attracts: [ { capped_by_this_room: true, portal: true }, { capped_by_this_room: true, portal: false } ]
          - room: Graveyard
            cost_per_tile: 2000
            sizes: [ { side: 3, floor_slots: 1, attraction: 0, gold: 0 } ]
            attracts: [ { capped_by_this_room: true, portal: false } ]
        """;

    [Fact]
    public void OnlyRulesInForceActAndOnlyPortalCreaturesMakeARoomCapOne()
    {
        string dir = Folder(OneLibrary, """
            rules:
              - { id: A, rule: "a", source: owner, status: refuted, applies: always, form: { kind: attraction-target, room: Library, value: 9 } }
              - { id: B, rule: "b", source: owner, status: proposed, applies: playbook, form: { kind: tile-target, room: Hatchery, tiles: 25 } }
              - { id: C, rule: "c", source: owner, status: proposed, applies: always, form: { kind: attraction-target, room: Library, value: 2 } }
            """);
        try
        {
            KeeperKnowledgeFile.Loaded loaded = KeeperKnowledgeFile.Load(dir);
            Assert.Equal(["C"], loaded.ActingRules);
            Assert.Equal(2, loaded.AttractionTargets[RoomTypeId.Library]);
            Assert.Empty(loaded.TileTargets);
            Assert.True(loaded.Knowledge.Room(RoomTypeId.Library)!.CapsCreatures);
            Assert.False(loaded.Knowledge.Room(RoomTypeId.Graveyard)!.CapsCreatures);      // its creature never comes by portal
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ANumberIsNoRoomNameAndATargetNeedsTheRoomsFacts()
    {
        string dir = Folder(OneLibrary, """
            rules:
              - { id: A, rule: "a", source: owner, status: proposed, applies: always, form: { kind: tile-target, room: "99", tiles: 3 } }
              - { id: B, rule: "b", source: owner, status: proposed, applies: always, form: { kind: attraction-target, room: Temple, value: 2 } }
              - { id: C, rule: "c", source: owner, status: proposed, applies: always, form: { kind: tile-target, room: "Hatchery,Library", tiles: 3 } }
              - { id: D, rule: "d", source: owner, status: proposed, applies: always, form: { kind: tile-target, room: " Library", tiles: 3 } }
              - { id: E, rule: "e", source: owner, status: proposed, applies: always, form: { kind: research-shelves, room: Library } }
              - { id: F, rule: "f", source: owner, status: proposed, applies: always, form: { kind: research-shelves, room: Library } }
            """);
        try
        {
            // No research count is given, yet the second shelves rule for one room is still refused.
            var refused = Assert.Throws<InvalidDataException>(() => KeeperKnowledgeFile.Load(dir));
            Assert.Contains("A names no known room", refused.Message, StringComparison.Ordinal);
            Assert.Contains("B targets Temple", refused.Message, StringComparison.Ordinal);
            Assert.Contains("C names no known room", refused.Message, StringComparison.Ordinal);   // a comma list ORs to a defined value
            Assert.Contains("D names no known room", refused.Message, StringComparison.Ordinal);
            Assert.Contains("F sets Library a second time", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("E sets", refused.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void BadRulesAreRefusedWithEveryProblemNamed()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dk2-knowledge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, KeeperKnowledgeFile.RoomsFile), """
                hash_version: 1
                rooms:
                  - { room: Library, cost_per_tile: -1, sizes: [ { side: 3, floor_slots: 1, attraction: 2, gold: 0 } ], attracts: [] }
                  - { room: Casino, cost_per_tile: 1, sizes: [ { side: 3, floor_slots: 1, attraction: -2, gold: 0 } ], attracts: [] }
                """);
            File.WriteAllText(Path.Combine(dir, KeeperKnowledgeFile.RulesFile), """
                rules:
                  - { id: A, rule: "x", source: owner, status: maybe, applies: always, form: { kind: attraction-target, room: Library, value: 5 } }
                  - { id: A, rule: "y", source: owner, status: proposed, applies: always, form: { kind: tile-target, room: Nowhere, tiles: 3 } }
                """);
            var refused = Assert.Throws<InvalidDataException>(() => KeeperKnowledgeFile.Load(dir));
            Assert.Contains("hash 1", refused.Message, StringComparison.Ordinal);
            Assert.Contains("status 'maybe'", refused.Message, StringComparison.Ordinal);
            Assert.Contains("'A' is missing or repeated", refused.Message, StringComparison.Ordinal);
            Assert.Contains("names no known room", refused.Message, StringComparison.Ordinal);
            Assert.Contains("Library has a negative cost_per_tile", refused.Message, StringComparison.Ordinal);
            Assert.Contains("Casino has a size with a bad or repeated side or a negative value", refused.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
