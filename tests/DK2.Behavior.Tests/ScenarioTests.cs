// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// Covers the scenario harness itself: that every scenario in the repository
/// loads, that a run is reproducible, and - most importantly - that the
/// harness reports what it could not do rather than quietly skipping it.
///
/// These run without a Dungeon Keeper 2 installation, so they are part of the
/// CI gate. Scenarios that need shipped maps are exercised separately and skip
/// when the install is absent.
/// </summary>
public class ScenarioTests
{
    [InstallFact]
    public void Level10ComputerKeeperRespectsLockedRoomAvailability()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = Scenario.Load(Path.Combine(
            RepositoryRoot(), "scenarios", "keepers", "level10_asmodeus_idle.yaml"));
        ScenarioResult run = new ScenarioRunner(GameDatabaseLoader.Load(install), install).Run(scenario);

        Assert.DoesNotContain(run.EmittedKeeperCommands, c => c is BuildRoomCommand);
        Assert.Contains(run.EmittedKeeperCommands, c => c is CreateImpCommand);
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }

    [InstallFact]
    public void UnaidedKeeperHasARepeatableCommandStream()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = Scenario.Load(Path.Combine(
            RepositoryRoot(), "scenarios", "keepers", "level17_harkan_unassisted.yaml"));
        var runner = new ScenarioRunner(GameDatabaseLoader.Load(install), install);
        ScenarioResult first = runner.Run(scenario);
        ScenarioResult second = runner.Run(scenario);

        Assert.Equal(first.HashPerTick, second.HashPerTick);
        Assert.NotEmpty(first.KeeperCommandLog);
        Assert.Equal(first.KeeperCommandLog.Select(Fingerprint),
            second.KeeperCommandLog.Select(Fingerprint));

        static (int Tick, ulong Hash) Fingerprint(KeeperCommandEmission emission)
        {
            var hash = Hash64.Start();
            emission.Command.HashInto(ref hash);
            return (emission.InputTick, hash.Value);
        }
    }

    [Fact]
    public void EveryScenarioInTheRepository_Loads()
    {
        List<string> files = ScenarioFiles();
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            Scenario scenario = Scenario.Load(file);
            Assert.False(string.IsNullOrWhiteSpace(scenario.Id), $"{file}: no id");
            Assert.False(string.IsNullOrWhiteSpace(scenario.Map), $"{file}: no map");
            Assert.True(scenario.DurationTicks > 0, $"{file}: duration_ticks must be positive");
            Assert.True(scenario.UnknownKeys.Count == 0,
                $"{file}: unrecognised keys {string.Join(", ", scenario.UnknownKeys)}");
        }
    }

    [Fact]
    public void EveryExpectation_SaysWhereItCameFrom()
    {
        // An expectation with no source is a guess pinned by a green check, which
        // buys silence rather than confidence. The field is mandatory by test
        // because nothing in YAML can make it so.
        foreach (string file in ScenarioFiles())
        {
            Scenario scenario = Scenario.Load(file);
            foreach (ScenarioExpectation expectation in scenario.Expect)
            {
                Assert.False(string.IsNullOrWhiteSpace(expectation.Because),
                    $"{file}: expectation '{expectation}' has no 'because'");
                Assert.False(string.IsNullOrWhiteSpace(expectation.Event),
                    $"{file}: an expectation has no 'event'");
            }
        }
    }

    [InstallFact]
    public void EveryScenario_MeetsItsExpectations()
    {
        // The gate that turns a scenario from a description into a regression
        // test. Install-gated on purpose: an expectation worth writing is a claim
        // about DK2's own numbers, and SyntheticDatabase has four terrain types
        // and one creature -- a goblin scenario cannot even run against it.
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);

        foreach (string file in ScenarioFiles())
        {
            Scenario scenario = Scenario.Load(file);
            if (scenario.Expect.Count == 0) continue;

            ScenarioResult result = new ScenarioRunner(database, install).Run(scenario);
            Assert.True(result.ExpectationFailures.Count == 0,
                $"{file}:{Environment.NewLine}  " +
                string.Join(Environment.NewLine + "  ", result.ExpectationFailures));
        }
    }

    [Fact]
    public void ScenarioIds_AreUnique()
    {
        // Ids name the output directory, so a duplicate silently overwrites
        // another scenario's evidence.
        List<string> ids = ScenarioFiles().Select(f => Scenario.Load(f).Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void RunningTheSameScenarioTwice_ProducesTheSameHashSeries()
    {
        Scenario scenario = LoadFlatScenario();
        var runner = new ScenarioRunner(SyntheticDatabase(), installDir: null);

        ScenarioResult first = runner.Run(scenario);
        ScenarioResult second = runner.Run(scenario);

        Assert.Equal(first.HashPerTick, second.HashPerTick);
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
    }

    [Fact]
    public void Run_ReportsThatInjectedWorkHasNoWorkerAndNoKnownRate()
    {
        // The honesty check, in its M3 form. Tile mechanics now run for real,
        // but nothing decides who does the work or how fast, and the queue is
        // filled by the scenario file rather than by a creature. A report that
        // looked complete would corrupt the regression corpus.
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: injected-work-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 3
            work:
              - { at_tick: 0, player: 1, kind: Claim, at: [3, 3] }
            """);

        try
        {
            ScenarioResult result = new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(path));

            Assert.Contains(result.Unsupported,
                u => u.Contains("injected by the harness", StringComparison.Ordinal));
            // This caveat has narrowed three times, and the third time it ran
            // out of unknowns. Q47 turned "the rate is unknown" into "the swing
            // count is known, the wall clock is not"; Q1 (ADR-0089) settled the
            // wall clock; and Q219 removed the last named unknown by
            // establishing that DK2 HAS NO FILL-IN AT ALL - FillInHealth has no
            // reader anywhere in the executable, and TileWorkKind.FillIn was
            // deleted. So the caveat must NOT still disclaim it, and this
            // asserts the absence: a stale disclaimer is a false statement
            // printed on every run, which is worse than a missing one.
            Assert.DoesNotContain(result.Unsupported,
                u => u.Contains("fill-in", StringComparison.OrdinalIgnoreCase));

            // Still non-vacuous about what the caveat does say.
            Assert.Contains(result.Unsupported,
                u => u.Contains("250 ms a tick", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Run_ReportsWhatItsWorkersStillCannotDo()
    {
        // Workers exist now, which makes a run look much more like a game than
        // it is. Every run that places one has to say what order it takes jobs
        // in and what it does not do afterwards, that its routing is ours rather
        // than DK2's, that gold it cannot hold is lost where DK2 would leave it
        // on the floor, and that nothing decided how many workers a keeper would
        // have.
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: worker-honesty-probe
            map: chamber_11x11
            seed: 1
            duration_ticks: 5
            setup:
              players:
                - { id: 1, gold: 0, mana: 0 }
              creatures:
                - { type: Imp, at: [5, 5], player: 1 }
            """);

        try
        {
            ScenarioResult result = new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(path));

            Assert.Contains(result.Unsupported,
                u => u.Contains("places creatures directly", StringComparison.Ordinal));
            Assert.Contains(result.Unsupported,
                u => u.Contains("marked excavation first, then claim floor", StringComparison.Ordinal));
            Assert.Contains(result.Unsupported,
                u => u.Contains("routing is ours, not DK2's", StringComparison.Ordinal));
            Assert.Contains(result.Unsupported,
                u => u.Contains("gold it cannot hold is LOST", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Run_ReportsWorkItCannotPlace()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: bad-work-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 3
            work:
              - { at_tick: 0, player: 1, kind: Yodel, at: [3, 3] }
              - { at_tick: 0, player: 1, kind: Dig, at: [99, 99] }
            commands:
              - { at_tick: 99, player: 1, tag: [[1, 1]] }
            """);

        try
        {
            ScenarioResult result = new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(path));

            Assert.Contains(result.Unsupported, u => u.Contains("'Yodel'", StringComparison.Ordinal));
            Assert.Contains(result.Unsupported, u => u.Contains("[99,99]", StringComparison.Ordinal));
            Assert.Contains(result.Unsupported, u => u.Contains("at_tick=99", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Run_ReportsUnknownKeysRatherThanIgnoringThem()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: unknown-key-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 5
            wibble: 42
            """);

        try
        {
            Scenario scenario = Scenario.Load(path);
            Assert.Contains("wibble", scenario.UnknownKeys);

            ScenarioResult result = new ScenarioRunner(SyntheticDatabase(), null).Run(scenario);
            Assert.Contains(result.Unsupported, u => u.Contains("wibble", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Run_ReportsSetupItWillNotApply()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: unapplied-setup-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 5
            setup:
              players:
                - { id: 1, gold: 500, mana: 100 }
              rooms:
                - { type: Lair, at: [2, 2], size: [3, 3] }
              creatures:
                - { type: Goblin, at: [4, 4] }
            observe:
              - first_destination
            """);

        try
        {
            ScenarioResult result = new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(path));

            Assert.Contains(result.Unsupported, u => u.StartsWith("setup.rooms", StringComparison.Ordinal));
            Assert.Contains(result.Unsupported, u => u.StartsWith("setup.creatures", StringComparison.Ordinal));
            Assert.Contains(result.Unsupported, u => u.StartsWith("observe.first_destination", StringComparison.Ordinal));

            // The gold has nowhere to go: the scenario asks for a lair, which
            // this database does not have, so no treasury is ever laid down.
            Assert.Contains(result.Unsupported,
                u => u.Contains("had nowhere to go", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Setup_AppliesStartingMana()
    {
        // A scenario whose setup is a no-op is worse than no scenario at all, so
        // mana has to reach the world and move the hash on its own.
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: starting-mana-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 1
            setup:
              players:
                - { id: 1, gold: 0, mana: 5678 }
            """);

        try
        {
            Scenario scenario = Scenario.Load(path);
            var withMana = new ScenarioRunner(SyntheticDatabase(), null).Run(scenario);

            scenario.Setup.Players[0].Mana = 0;
            ScenarioResult without = new ScenarioRunner(SyntheticDatabase(), null).Run(scenario);

            Assert.NotEqual(withMana.FinalStateHash, without.FinalStateHash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Setup_StoresStartingGoldInATreasuryAndSaysSoWhenItCannot()
    {
        // Gold is not a number on a player: it goes into treasury tiles. A
        // scenario that gives a keeper a treasury gets the gold; the same
        // scenario without one gets a report saying where it went instead.
        string with = WriteScenario("""
            id: starting-gold-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 1
            setup:
              players:
                - { id: 1, gold: 1234, mana: 0 }
              rooms:
                - { type: Treasury, at: [2, 2], size: [2, 2], player: 1 }
            """);

        string without = WriteScenario("""
            id: starting-gold-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 1
            setup:
              players:
                - { id: 1, gold: 1234, mana: 0 }
            """);

        try
        {
            ScenarioResult stored = new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(with));
            ScenarioResult homeless = new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(without));

            Assert.DoesNotContain(stored.Unsupported,
                u => u.Contains("had nowhere to go", StringComparison.Ordinal));
            Assert.Contains(homeless.Unsupported,
                u => u.Contains("1234 of 1234 had nowhere to go", StringComparison.Ordinal));

            // And the treasury is real state, not a report: the two runs differ.
            Assert.NotEqual(stored.FinalStateHash, homeless.FinalStateHash);
        }
        finally
        {
            File.Delete(with);
            File.Delete(without);
        }
    }

    [Fact]
    public void ABuildCommand_LaysTheRoomDownAndIsReportedAsApplied()
    {
        // The build command is a real player command: it goes through the input
        // frame, the ground rules and the treasury, and nothing about it is
        // listed as unsupported.
        string path = WriteScenario("""
            id: build-command-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 3
            setup:
              players:
                - { id: 0, gold: 1000, mana: 0 }
              rooms:
                - { type: Treasury, at: [1, 1], player: 0 }
            commands:
              - at_tick: 1
                player: 0
                build: { type: Treasury, tiles: [[3, 3], [4, 3]] }
            """);

        try
        {
            ScenarioResult result = new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(path));

            Assert.Contains(result.Events, e => e.Kind == "built" && e.Detail.Contains("tiles=2", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Unsupported,
                u => u.Contains("names nothing this build can do", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteScenario(string yaml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    [Fact]
    public void UnparseableMapSpec_IsRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: bad-map-probe
            map: somewhere_nice
            seed: 1
            duration_ticks: 5
            """);

        try
        {
            Assert.Throws<InvalidDataException>(
                () => new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Scenario LoadFlatScenario() =>
        Scenario.Load(Path.Combine(RepositoryRoot(), "scenarios", "smoke", "determinism_flat_map.yaml"));

    private static List<string> ScenarioFiles() =>
        [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "scenarios"), "*.yaml", SearchOption.AllDirectories)
             .OrderBy(f => f, StringComparer.Ordinal)];

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DK2.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root (no DK2.slnx above the test binaries).");
    }

    /// <summary>
    /// A minimal stand-in database so harness tests run without the original
    /// game. The numbers are invented and are not claims about DK2; only the
    /// two terrain roles the flat-map builder looks for matter here.
    /// </summary>
    private static GameDatabase SyntheticDatabase() => new(
        [
            Terrain(1, "Impenetrable Rock", TerrainFlags.Solid | TerrainFlags.Impenetrable, 0),
            Terrain(2, "Rock", TerrainFlags.Solid | TerrainFlags.Taggable, 165),
            Terrain(3, "Dirt Path", TerrainFlags.Ownable, 100),
            Terrain(4, "Treasury",
                TerrainFlags.Ownable | TerrainFlags.Room | TerrainFlags.Attackable, 10000),
        ],
        rooms: [SyntheticTreasury()],
        creatures: [Worker("Imp", 1)],
        variables: Variables(),
        keeperSpells: KeeperSpells());

    /// <summary>
    /// The keeper spells a registered system reads. Only Create Imp, because
    /// only Create Imp is read: <see cref="ImpSupplyRules"/> takes its cast cost
    /// and <see cref="ManaRules"/> takes the per-second upkeep every imp then
    /// charges its keeper.
    ///
    /// <para>Both numbers are DK2's own — 1500 to cast, 7 a second to keep — for
    /// the same reason the variables below are: a made-up figure here would be a
    /// made-up figure inside a determinism hash. `KeeperSpellsOnRealDataTests`
    /// reads both back out of the shipped file so these cannot drift from it.</para>
    /// </summary>
    private static KeeperSpellDef[] KeeperSpells() =>
    [
        new() { Name = KeeperSpellName.CreateImp, ManaCost = 1500, ManaUpkeepPerSecond = 7 },
    ];

    /// <summary>
    /// A stand-in treasury. It carries the real room id, because
    /// <see cref="DK2.Simulation.GoldStore"/> keys gold capacity on the room
    /// type; everything else about it is invented like the terrain above.
    /// </summary>
    private static RoomDef SyntheticTreasury() => new()
    {
        Name = "Treasury",
        RoomId = (byte)RoomTypeId.Treasury,
        Flags = RoomFlags.PlaceableOnLand | RoomFlags.Buildable,
        Cost = 200,
        ReturnPercentage = 100,
        TerrainId = 4,
        Construction = TileConstruction.Normal,
        CreatedCreatureId = 0,
        ObjectIds = [],
        HealthGain = 0,
        CeilingHeight = new Fp32(0),
        RecommendedSizeX = 1,
        RecommendedSizeY = 1,
        OrderInEditor = 0,
        NameStringId = 0,
        TooltipStringId = 0,
    };

    private static GlobalVariables Variables()
    {
        var vars = new GlobalVariables();
        foreach (VariableTypeId id in new[]
                 {
                     VariableTypeId.ImpIdleDelayBeforeReevaluationSeconds,
                     VariableTypeId.DigRockHealth,
                     VariableTypeId.MinimumImpThreshold,
                     VariableTypeId.TimeBeforeFreeImpGeneratedSeconds,

                     // Needed since Q68: claiming now applies to ground that is
                     // not already yours rather than only to unowned terrain, so
                     // a claim on this map's neutral floor reaches the rate
                     // lookup where it used to be rejected before getting there.
                     VariableTypeId.ClaimTileHealth,

                     // Needed since ADR-0078: PaydaySystem runs on every tick
                     // of every scenario and schedules the first payday from
                     // this. The value here is 1 like the rest of this block,
                     // so payday fires almost immediately - which is fine,
                     // because no scenario in this file owns a paid creature.
                     VariableTypeId.PayDayFrequencySeconds,
                     VariableTypeId.TimeBeforeFirstPayDaySeconds,
                 })
        {
            vars.Misc[id] = new MiscVariable(id, 1, 0, 0);
        }

        vars.Misc[VariableTypeId.MaxGoldPerTreasuryTile] =
            new MiscVariable(VariableTypeId.MaxGoldPerTreasuryTile, 3000, 0, 0);
        vars.Misc[VariableTypeId.RoomSellValuePercentageOfCost] =
            new MiscVariable(VariableTypeId.RoomSellValuePercentageOfCost, 50, 0, 0);
        vars.Misc[VariableTypeId.WoodBridgeLifeOnLavaSeconds] =
            new MiscVariable(VariableTypeId.WoodBridgeLifeOnLavaSeconds, 30, 0, 0);
        vars.Misc[VariableTypeId.MaxGoldPerDungeonHeartTile] =
            new MiscVariable(VariableTypeId.MaxGoldPerDungeonHeartTile, 1000, 0, 0);

        // ManaSystem reads these every second and GameDatabase throws rather
        // than defaulting. No terrain here generates mana, so the values only
        // have to exist.
        vars.Misc[VariableTypeId.MaximumManaThreshold] =
            new MiscVariable(VariableTypeId.MaximumManaThreshold, 200000, 0, 0);
        vars.Misc[VariableTypeId.MaximumManaGainPerSecond] =
            new MiscVariable(VariableTypeId.MaximumManaGainPerSecond, 500, 0, 0);
        vars.Misc[VariableTypeId.ExcessManaDecreaseRatePerSecond] =
            new MiscVariable(VariableTypeId.ExcessManaDecreaseRatePerSecond, 5, 0, 0);

        // The heart's health and regeneration, as the shipped global VARIABLES table
        // carries them (rows 174 and 131); a keeper's world needs both (ADR-0162).
        vars.Misc[VariableTypeId.DungeonHeartObjectHealth] = new MiscVariable(VariableTypeId.DungeonHeartObjectHealth, 100000, 0, 0);
        vars.Misc[VariableTypeId.DungeonHeartHealthRegenerationPerSecond] = new MiscVariable(VariableTypeId.DungeonHeartHealthRegenerationPerSecond, 25, 0, 0);
        return vars;
    }

    /// <summary>
    /// A worker with a name and a speed and nothing else. Invented, like the
    /// terrain above — these tests are about the harness reporting honestly, not
    /// about DK2. Real creature numbers live in
    /// <see cref="ImpsOnRealDataTests"/>.
    /// </summary>
    private static CreatureDef Worker(string name, byte id) => new()
    {
        Name = name,
        CreatureId = id,
        Flags = CreatureFlags.IsWorker,
        Speed = new Fp32(4096),
        RunSpeed = new Fp32(4096),
        ShuffleSpeed = new Fp32(4096),
        Height = new Fp32(0),
        EyeHeight = new Fp32(0),
        DistanceCanSee = new Fp32(0),
        DistanceCanHear = new Fp32(0),
        PerceptionRange = new Fp32(0),
        FootprintRadius = new Fp32(573),
        SizeClass = 1,
        TurnRate = 4096,
        Hp = 1,
        HpFromChicken = 0,
        HungerRate = new Fp32(0),
        HungerFill = 0,
        TimeAwake = 0,
        TimeSleep = 0,
        OwnLandHealthIncreasePerSecond = 0,
        DecomposeValue = 0,
        Fear = 0,
        Threat = 0,
        MeleeDamage = 0,
        SlapDamage = 0,
        MeleeRange = new Fp32(0),
        MeleeRecharge = new Fp32(0),
        MeleeHitDelay = new Fp32(0),
        MeleeAttackType = AttackType.None,
        StunDuration = new Fp32(0),
        FightStyle = FightStyle.NonFighter,
        Resistances = [],
        Spells = [],
        Pay = 0,
        MaxGoldHeld = 0,
        InitialGoldHeld = 0,
        ExpForNextLevel = 0,
        ExpPerSecond = 0,
        ExpPerSecondTraining = 0,
        ResearchPerSecond = 0,
        ManufacturePerSecond = 0,
        ManaGeneratedByPrayerPerSecond = 0,
        JobClass = JobClass.Scout,
        UnhappyThreshold = 0,
        AngerNoLair = 0,
        AngerNoFood = 0,
        AngerNoPay = 0,
        AngerNoWork = 0,
        AngerSlap = 0,
        AngerInHand = 0,
        TortureHpChange = 0,
        TortureMoodChange = 0,
        TortureTimeToConvert = new Fp32(0),
        HappyJobs = [],
        UnhappyJobs = [],
        AngryJobs = [],
        HateJobs = [],
        AlternativeJobs = [],
        Attractions = [],
        GuardDuration = new Fp32(0),
        IdleDuration = new Fp32(0),
        SlapFearlessDuration = new Fp32(0),
        PossessionManaCost = 0,
        LairObjectId = 0,
        CloneCreatureId = 0,
        NameStringId = 0,
        TooltipStringId = 0,
        IntroductionStringId = 0,
    };

    private static TerrainDef Terrain(byte id, string name, TerrainFlags flags, ushort health) => new()
    {
        Name = name,
        TerrainId = id,
        Flags = flags,
        StartingHealth = health,
        MaxHealth = health,
        GoldValue = 0,
        ManaGain = 0,
        MaxManaGain = 0,
        Damage = 0,
        MaxHealthTypeTerrainId = 0,
        DestroyedTypeTerrainId = 0,
        Depth = new Fp32(0),
        NameStringId = 0,
        TooltipStringId = 0,
    };
}
