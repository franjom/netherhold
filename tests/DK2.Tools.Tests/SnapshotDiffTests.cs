// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.Json;
using DK2.Core;
using DK2.Data.Model;
using DK2.Data.Save;
using DK2.Simulation;
using DK2.Tools.Commands;

namespace DK2.Tools.Tests;

[Collection("Console")]
public class SnapshotDiffTests
{
    [Fact]
    public void ReportsCreaturePlayerAndTileChangesWithoutChangingInputs()
    {
        var f = new Fixture();
        f.Before.Creature.Health = 100;
        f.After.Creature.Health = 85;
        f.After.Creature.PosX += 17;
        f.After.Creature.Body.PathLastProgress = 2;
        f.After.Simulation.World.Player(3).Mana = 41;
        f.After.Simulation.World.Tile(new TilePos(2, 1)).Health = 77;
        f.Write();
        byte[] before = File.ReadAllBytes(f.BeforePath), after = File.ReadAllBytes(f.AfterPath);

        Assert.Equal(1, f.Run());
        using JsonDocument report = f.Report();
        AssertChange(report, "creatures[1].Health", "100", "85");
        AssertChange(report, "creatures[1].PosX", "6144", "6161");
        AssertChange(report, "players[3].Mana", "0", "41");
        AssertChange(report, "tiles[2,1].Health", "100", "77");
        AssertChange(report, "creatures[1].Body.PathLastProgress", "0", "2");
        Assert.Equal(before, File.ReadAllBytes(f.BeforePath));
        Assert.Equal(after, File.ReadAllBytes(f.AfterPath));
    }

    [Fact]
    public void IdenticalSnapshotsHaveNoChangesAndAStableReport()
    {
        var f = new Fixture();
        f.Write();
        Assert.Equal(0, f.Run());
        using JsonDocument report = f.Report();
        Assert.True(report.RootElement.GetProperty("snapshotBytesEqual").GetBoolean());
        Assert.Empty(Changes(report));
        Assert.Equal(0, f.Run(output: f.Output + ".second.json"));
        Assert.Equal(File.ReadAllText(f.Output), File.ReadAllText(f.Output + ".second.json"));
    }

    [Fact]
    public void CreatureIdentityRatherThanListIndexNamesAddedAndRemovedEntities()
    {
        var f = new Fixture();
        f.After.Simulation.World.RemoveCreature(1);
        CreatureState replacement = f.After.Simulation.World.SpawnCreature(f.Database, 1, new PlayerId(3), new TilePos(1, 1));
        replacement.NameIndex = 0;
        f.Write();

        Assert.Equal(1, f.Run());
        using JsonDocument report = f.Report();
        Assert.Contains(Changes(report), c => c.GetProperty("path").GetString() == "creatures[1].$exists"
            && c.GetProperty("kind").GetString() == "removed");
        Assert.Contains(Changes(report), c => c.GetProperty("path").GetString() == "creatures[2].$exists"
            && c.GetProperty("kind").GetString() == "added");
    }

    [Fact]
    public void CrossedMapDimensionsDoNotInventTilesMissingFromBothSnapshots()
    {
        var f = new Fixture(beforeWidth: 2, beforeHeight: 3, afterWidth: 3, afterHeight: 2);
        f.Write();

        Assert.Equal(1, f.Run());
        using JsonDocument report = f.Report();
        Assert.DoesNotContain(Changes(report), c => c.GetProperty("path").GetString()!.StartsWith("tiles[2,2]", StringComparison.Ordinal));
        Assert.Contains(Changes(report), c => c.GetProperty("path").GetString() == "tiles[2,0].$exists"
            && c.GetProperty("kind").GetString() == "added");
        Assert.Contains(Changes(report), c => c.GetProperty("path").GetString() == "tiles[0,2].$exists"
            && c.GetProperty("kind").GetString() == "removed");
    }

    [Fact]
    public void NestedCollectionsAndFullWidthIntegersRemainExact()
    {
        var f = new Fixture();
        f.After.Simulation.World.Player(3).IntroducedCreatureTypes.Add(9);
        f.After.Creature.Body.Segment[2] = 123;
        f.After.Creature.LastAteTick = long.MaxValue - 2;
        f.Write();

        Assert.Equal(1, f.Run());
        using JsonDocument report = f.Report();
        AssertChange(report, "creatures[1].Body.Segment[2]", "0", "123");
        AssertChange(report, "creatures[1].LastAteTick", "0", "9223372036854775805");
        Assert.Contains(Changes(report), c => c.GetProperty("path").GetString() == "players[3].IntroducedCreatureTypes[0]"
            && c.GetProperty("after").GetString() == "9");
    }

    [Fact]
    public void UnknownActivityDoesNotInvokeComputedGetters()
    {
        var f = new Fixture();
        f.Before.Creature.Dk2StateId = f.After.Creature.Dk2StateId = 9999;
        f.After.Creature.Health = 12;
        f.Write();

        Assert.Equal(1, f.Run());
        using JsonDocument report = f.Report();
        AssertChange(report, "creatures[1].Health", "100", "12");
    }

    [Fact]
    public void SnapshotDifferencesOutsideTheProjectionCannotReportEquality()
    {
        var f = new Fixture();
        f.After.Simulation.World.SpawnShot(1, new PlayerId(3), 2, 3);
        f.Write();

        Assert.Equal(3, f.Run());
        using JsonDocument report = f.Report();
        Assert.False(report.RootElement.GetProperty("snapshotBytesEqual").GetBoolean());
        Assert.Empty(Changes(report));
        Assert.Equal("different-outside-scope", report.RootElement.GetProperty("verdict").GetString());
    }

    [Fact]
    public void RandomStateIsComparedWithoutAdvancingEitherSimulation()
    {
        var f = new Fixture();
        f.After.Simulation.Rng.NextInt(100);
        f.Write();

        Assert.Equal(1, f.Run());
        using JsonDocument report = f.Report();
        Assert.Contains(Changes(report), c => c.GetProperty("path").GetString()!.StartsWith("snapshot.Rng.", StringComparison.Ordinal));
    }

    [Fact]
    public void PackagedSaveHeadersSupplyTheLevelAndDoNotCountAsWorldChanges()
    {
        var f = new Fixture();
        f.Write(packaged: true);

        Assert.Equal(0, f.Run(level: null));
        using JsonDocument report = f.Report();
        Assert.Equal("fixture", report.RootElement.GetProperty("level").GetString());
        Assert.True(report.RootElement.GetProperty("snapshotBytesEqual").GetBoolean());
        Assert.NotEqual(report.RootElement.GetProperty("before").GetProperty("sha256").GetString(),
            report.RootElement.GetProperty("after").GetProperty("sha256").GetString());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "another-level")]
    public void MissingOrConflictingLevelContextIsRefused(bool packaged, string? level)
    {
        var f = new Fixture();
        f.Write(packaged);

        Assert.Equal(2, f.Run(level));
        Assert.False(File.Exists(f.Output));
    }

    [Theory]
    [InlineData("bad magic")]
    [InlineData("old version")]
    [InlineData("truncated")]
    [InlineData("trailing bytes")]
    [InlineData("duplicate creature")]
    public void InvalidSnapshotsAreRefusedWithoutWritingAReport(string fault)
    {
        var f = new Fixture();
        if (fault == "duplicate creature")
        {
            CreatureState another = f.After.Simulation.World.SpawnCreature(f.Database, 1, new PlayerId(3), new TilePos(2, 1));
            another.NameIndex = 1;
            another.Id = f.After.Creature.Id;
        }
        f.Write();
        byte[] bytes = File.ReadAllBytes(f.AfterPath);
        if (fault == "bad magic") bytes[0] = 0;
        if (fault == "old version") bytes[4] ^= 127;
        if (fault == "truncated") bytes = bytes[..^1];
        if (fault == "trailing bytes") bytes = [.. bytes, 0];
        File.WriteAllBytes(f.AfterPath, bytes);

        Assert.Equal(2, f.Run());
        Assert.False(File.Exists(f.Output));
    }

    [Fact]
    public void OutputCannotOverwriteAnyExistingFile()
    {
        var f = new Fixture();
        f.Write();
        File.WriteAllText(f.Output, "keep");
        byte[] before = File.ReadAllBytes(f.BeforePath);

        Assert.Equal(2, f.Run());
        Assert.Equal("keep", File.ReadAllText(f.Output));
        Assert.Equal(2, f.Run(output: f.BeforePath));
        Assert.Equal(before, File.ReadAllBytes(f.BeforePath));
    }

    private static JsonElement[] Changes(JsonDocument report) => report.RootElement.GetProperty("changes").EnumerateArray().ToArray();

    private static void AssertChange(JsonDocument report, string path, string before, string after)
    {
        JsonElement change = Assert.Single(Changes(report), c => c.GetProperty("path").GetString() == path);
        Assert.Equal(before, change.GetProperty("before").GetString());
        Assert.Equal(after, change.GetProperty("after").GetString());
        Assert.Equal("changed", change.GetProperty("kind").GetString());
    }

    private sealed class Fixture
    {
        private readonly string _directory = Path.Combine(Directory.GetCurrentDirectory(), "build", "snapshot-diff-tests", Guid.NewGuid().ToString("N"));
        public GameDatabase Database { get; }
        public (GameSimulation Simulation, CreatureState Creature) Before { get; }
        public (GameSimulation Simulation, CreatureState Creature) After { get; }
        public string BeforePath => Path.Combine(_directory, "before.bin");
        public string AfterPath => Path.Combine(_directory, "after.bin");
        public string Output => Path.Combine(_directory, "report.json");

        public Fixture(int beforeWidth = 3, int beforeHeight = 2, int afterWidth = 3, int afterHeight = 2)
        {
            Directory.CreateDirectory(_directory);
            // Synthetic decoding fixture, not a claim about DK2 balance or gameplay.
            var variables = new GlobalVariables();
            variables.Misc[VariableTypeId.DungeonHeartObjectHealth] = new(VariableTypeId.DungeonHeartObjectHealth, 100, 0, 0);
            Database = new GameDatabase(
                [new TerrainDef { TerrainId = 1, Name = "fixture", Flags = TerrainFlags.Ownable, StartingHealth = 100, MaxHealth = 100, GoldValue = 0, ManaGain = 0, MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = 1, DestroyedTypeTerrainId = 1, Depth = default, NameStringId = 0, TooltipStringId = 0 }], [],
                [new CreatureDef
                {
                    Name = "fixture", CreatureId = 1, Flags = CreatureFlags.IsWorker,
                    Speed = default, RunSpeed = default, ShuffleSpeed = default,
                    Height = default, EyeHeight = default, DistanceCanSee = default,
                    DistanceCanHear = default, PerceptionRange = default, FootprintRadius = default,
                    SizeClass = default, TurnRate = default, Hp = 100,
                    HpFromChicken = default, HungerRate = default, HungerFill = default,
                    TimeAwake = default, TimeSleep = default, OwnLandHealthIncreasePerSecond = default,
                    DecomposeValue = default, Fear = default, Threat = default,
                    MeleeDamage = default, SlapDamage = default, MeleeRange = default,
                    MeleeRecharge = default, MeleeHitDelay = default, MeleeAttackType = default,
                    StunDuration = default, FightStyle = default, Resistances = [],
                    Spells = [], Pay = default, MaxGoldHeld = default,
                    InitialGoldHeld = default, ExpForNextLevel = default, ExpPerSecond = default,
                    ExpPerSecondTraining = default, ResearchPerSecond = default, ManufacturePerSecond = default,
                    ManaGeneratedByPrayerPerSecond = default, JobClass = default, UnhappyThreshold = default,
                    AngerNoLair = default, AngerNoFood = default, AngerNoPay = default,
                    AngerNoWork = default, AngerSlap = default, AngerInHand = default,
                    TortureHpChange = default, TortureMoodChange = default, TortureTimeToConvert = default,
                    HappyJobs = [], UnhappyJobs = [], AngryJobs = [],
                    HateJobs = [], AlternativeJobs = [], Attractions = [],
                    GuardDuration = default, IdleDuration = default, SlapFearlessDuration = default,
                    PossessionManaCost = default, LairObjectId = default, CloneCreatureId = default,
                    NameStringId = default, TooltipStringId = default, IntroductionStringId = default,
                }], variables);
            Before = Make(beforeWidth, beforeHeight);
            After = Make(afterWidth, afterHeight);
        }

        private (GameSimulation, CreatureState) Make(int width, int height)
        {
            var map = new LevelMap(width, height, Enumerable.Repeat(new MapTile(1, 3, 0, 0), width * height).ToArray());
            WorldState world = WorldState.FromMap(Database, map, [3]);
            CreatureState creature = world.SpawnCreature(Database, 1, new PlayerId(3), new TilePos(1, 1));
            creature.NameIndex = 0;
            creature.LastAteTick = 0;
            return (GameSimulation.Resume(Database, world, new SimRandom(42)), creature);
        }

        public void Write(bool packaged = false)
        {
            byte[] before = WorldSnapshot.ToBytes(Before.Simulation), after = WorldSnapshot.ToBytes(After.Simulation);
            if (!packaged)
            {
                File.WriteAllBytes(BeforePath, before);
                File.WriteAllBytes(AfterPath, after);
                return;
            }
            SavedSessionFile.Save(BeforePath, new(new("fixture", 1, 0, DateTime.UnixEpoch, "before"), before, new CampaignProfile()));
            SavedSessionFile.Save(AfterPath, new(new("fixture", 1, 0, DateTime.UnixEpoch.AddDays(1), "after"), after, new CampaignProfile()));
        }

        public int Run(string? level = "fixture", string? output = null) => SnapshotDiffCommand.Run(
            ["snapshot-diff", BeforePath, AfterPath, output ?? Output, .. level is null ? Array.Empty<string>() : new[] { "--level", level }],
            _ => Database);

        public JsonDocument Report() => JsonDocument.Parse(File.ReadAllText(Output));
    }
}
