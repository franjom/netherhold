// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The playbook format (campaign bot stage 3 plan, Task 3b): the plan's order, provenance,
/// integer ranges, and every problem reported at once.</summary>
public class PlaybookTests
{
    private const ulong Hash = 521;

    private static Playbook Level2 => new()
    {
        Level = "campaign:level2",
        Start = [PlaybookItem.Room(RoomTypeId.Lair), PlaybookItem.Room(RoomTypeId.Hatchery), new(PlaybookItemKind.Spell, "Create Imp")],
        Creatures = [new(PlaybookItemKind.Creature, "Goblin")],
        MayChange = [new(PlaybookItem.Room(RoomTypeId.TrainingRoom), true, "trigger 12 MAKE"), new(PlaybookItem.Room(RoomTypeId.Treasury), false, "trigger 13 MAKE")],
    };

    [Fact]
    public void ItemsReadAndWriteAsKindAndName()
    {
        Assert.True(PlaybookItem.TryParse("room:Library", out PlaybookItem library));
        Assert.Equal(PlaybookItem.Room(RoomTypeId.Library), library);
        Assert.Equal("spell:Call To Arms", new PlaybookItem(PlaybookItemKind.Spell, "Call To Arms").ToString());
        Assert.True(PlaybookItem.TryParse("spell:Call To Arms", out _));
        foreach (string bad in (string[])["Library", "Room:Library", "room:", ":Library", "room: Library", "chair:Throne"])
            Assert.False(PlaybookItem.TryParse(bad, out _), bad);
    }

    [Fact]
    public void ThePlansOrderAndValuesBecomeTheStrategy()
    {
        Playbook playbook = Level2 with
        {
            Rooms = [RoomTypeId.Hatchery, RoomTypeId.Lair],
            ExtraRooms = [RoomTypeId.TrainingRoom],
            Tunables = [new("strategy.room-side", 4, 3, 6, PlaybookProvenance.Researched, Source: "rooms.yaml")],
        };
        Assert.Empty(playbook.Review(Hash).Problems);

        PlayerStrategy strategy = playbook.Apply(new PlayerStrategy());
        Assert.Equal([RoomTypeId.Hatchery, RoomTypeId.Lair], strategy.Rooms);          // the plan's order, not the default's
        Assert.Equal([RoomTypeId.TrainingRoom], strategy.ExtraRooms);
        Assert.Equal(4, strategy.RoomSide);
        Assert.Empty(strategy.Validate());

        // Without a plan the defaults stay.
        Assert.Equal(new PlayerStrategy().Rooms, Level2.Apply(new PlayerStrategy()).Rooms);
        var options = new CampaignPlayerOptions(new PlayerRunId("t"), new PlayerId(3), []) { Playbook = playbook };
        Assert.Equal([RoomTypeId.Hatchery, RoomTypeId.Lair], options.Strategy.Rooms);
    }

    [Fact]
    public void ARoomThatOnlyMayComeWarnsAndOneThatNeverComesIsAProblem()
    {
        PlaybookReview review = (Level2 with { Rooms = [RoomTypeId.Lair, RoomTypeId.TrainingRoom], ExtraRooms = [RoomTypeId.Treasury] }).Review(Hash);
        Assert.Contains(review.Warnings, w => w.StartsWith("room:TrainingRoom is planned but only may become available (trigger 12 MAKE)", StringComparison.Ordinal));
        // The Treasury may only be taken away: neither offered nor on its way.
        Assert.Equal(["room:Treasury is planned but the level neither offers it at the start nor may make it available"], review.Problems);
    }

    [Fact]
    public void ATunedValueNamesItsCommitAndGoesStaleWithTheHashVersion()
    {
        PlaybookTunable tuned = new("strategy.lair-tiles-per-creature", 2, 1, 3, PlaybookProvenance.Tuned, ProvenOn: "5118067a", HashVersion: 519);
        PlaybookReview stale = (Level2 with { Tunables = [tuned] }).Review(Hash);
        Assert.Empty(stale.Problems);
        Assert.Equal(["tunable strategy.lair-tiles-per-creature is stale: proven on 5118067a at hash 519, the simulation is at 521"], stale.Warnings);
        Assert.Empty((Level2 with { Tunables = [tuned with { HashVersion = Hash }] }).Review(Hash).Warnings);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        Playbook bad = Level2 with
        {
            Level = "level2",
            Start = [.. Level2.Start, PlaybookItem.Room(RoomTypeId.Lair), new(PlaybookItemKind.Creature, "Imp")],
            MayChange = [new(PlaybookItem.Room(RoomTypeId.Library), true, " ")],
            Rooms = [RoomTypeId.Lair, RoomTypeId.Lair],
            Rules = ["R4", "R4", ""],
            Tunables =
            [
                new("strategy.room-side", 7, 3, 6, PlaybookProvenance.Researched, Source: "x"),
                new("strategy.treasury-full-percent", 80, 50, 120, PlaybookProvenance.Researched, Source: "x"),
                new("strategy.lair-tiles-per-creature", 1, 1, 2, PlaybookProvenance.Researched),
                new("strategy.home-guard-tiles", 8, 4, 9, PlaybookProvenance.Tuned),
                new("strategy.colour", 1, 1, 1, PlaybookProvenance.Researched, Source: "x"),
            ],
            Notes = [new("gold", "a seam at (40, 12)", "")],
            Events = [new("first-wave", "heroes at tick 6000", " ")],
            Caps = new PlaybookCaps(MaxTicks: 0, WallSeconds: -1, OutputMiB: 1),
        };
        IReadOnlyList<string> problems = bad.Review(Hash).Problems;
        string[] expected =
        [
            "level 'level2' is not a campaign level (campaign:<name>)",
            "available start lists 'room:Lair' more than once",
            "available start lists a creature; creatures go under available creatures",
            "may-change room:Library says nothing of what changes it",
            "planned rooms lists 'Lair' more than once",
            "rules lists 'R4' more than once",
            "a rule id is empty",
            "tunable strategy.room-side: value 7 is not within min 3 and max 6",
            "tunable strategy.treasury-full-percent: range 50-120 leaves 1-100",
            "tunable strategy.lair-tiles-per-creature is researched but cites no source",
            "tunable strategy.home-guard-tiles is tuned but names no proven-on commit and hash version",
            "note 'gold' cites no source",
            "event 'first-wave' cites no source",
            "cap max-ticks 0 is not positive",
            "cap wall-seconds -1 is not positive",
            "cap output-mib 1 is not between 2 and 1048576",
        ];
        foreach (string problem in expected) Assert.Contains(problem, problems);
        Assert.Contains(problems, p => p.StartsWith("tunable 'strategy.colour' is not one of strategy.assault-party, ", StringComparison.Ordinal));
        Assert.Equal(expected.Length + 1, problems.Count);
    }

    [Fact]
    public void EveryTunablePathSetsAStrategyThatValidatesAtItsBounds()
    {
        foreach (string path in Playbook.TunablePaths)
        {
            // 5 is within every path's bounds but the on/off switches' (0-1), and is no default; there the one not the default.
            int value = path switch
            {
                "strategy.scouts" => 1,
                "strategy.create-imps" or "strategy.avoid-traps" or "strategy.gold-budget" or "strategy.second-rooms" => 0,
                "strategy.assault-party" => 3,
                _ => 5,
            };
            Playbook set = Level2 with { Tunables = [new(path, value, value, value, PlaybookProvenance.Researched, Source: "test")] };
            Assert.Empty(set.Review(Hash).Problems);
            PlayerStrategy changed = set.Apply(new PlayerStrategy());
            Assert.Empty(changed.Validate());
            Assert.Contains(typeof(PlayerStrategy).GetProperties(), p => p.PropertyType == typeof(int)
                && (int)p.GetValue(changed)! == value && (int)p.GetValue(new PlayerStrategy())! != value);
        }
    }
}
