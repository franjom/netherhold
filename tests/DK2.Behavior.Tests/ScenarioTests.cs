// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
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
    public void Level17HarkanCastsTheCallAtItsFighterAndTakesItDownUnaided()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = Scenario.Load(Path.Combine(
            RepositoryRoot(), "scenarios", "keepers", "level17_harkan_call_to_arms.yaml"));
        ScenarioResult run = new ScenarioRunner(GameDatabaseLoader.Load(install), install).Run(scenario);

        // The battle step's own commands, not a scripted cast: action 54 on the first turn
        // divisible by 40 at the Black Knight's tile, and action 63 once the call is seen and
        // Harkan's removal gates fail (ADR-0283).
        Assert.Empty(run.ExpectationFailures);
        KeeperCommandEmission cast = Assert.Single(run.KeeperCommandLog, e => e.Command is CastSpellAtPlaceCommand);
        var call = (CastSpellAtPlaceCommand)cast.Command;
        Assert.Equal((39, (byte)4, (byte)3, 4, 7), (cast.InputTick, call.PlayerId, call.SpellId, call.X >> 12, call.Y >> 12));
        KeeperCommandEmission removal = Assert.Single(run.KeeperCommandLog, e => e.Command is RemoveCallToArmsCommand);
        Assert.InRange(removal.InputTick, cast.InputTick + 1, cast.InputTick + 1 + KeeperBattleStep.CastGraceTurns);
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }

    [InstallFact]
    public void Level17HarkanLiftsItsWoundedBlackKnightToALairUnaided()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = Scenario.Load(Path.Combine(
            RepositoryRoot(), "scenarios", "keepers", "level17_harkan_retreat.yaml"));
        GameDatabase database = GameDatabaseLoader.Load(install);
        ScenarioResult run = new ScenarioRunner(database, install).Run(scenario);

        // The retreat's own commands, not a scripted move: the wounded creature is passed over
        // while it seeks its bed and lifted once asleep, each pick-up followed at once by a drop
        // on one of Harkan's Lair tiles (ADR-0284, the scenario's comment for the turn).
        Assert.Empty(run.ExpectationFailures);
        List<KeeperCommandEmission> moves = run.KeeperCommandLog.Where(e => e.Command is PickUpCreatureCommand or DropCommand).ToList();
        Assert.NotEmpty(moves);
        Assert.All(moves, e => Assert.Equal((byte)4, e.Command.PlayerId));
        Assert.All(moves.Chunk(2), pair => Assert.True(pair[0].Command is PickUpCreatureCommand && pair[1].Command is DropCommand
            && pair[0].InputTick == pair[1].InputTick));
        Assert.True(moves[0].InputTick > 1, "the lift waits for the bed, it is not the first tick's");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level17")!;
        byte lair = database.RoomById((byte)RoomTypeId.Lair).TerrainId;
        foreach (DropCommand put in moves.Select(e => e.Command).OfType<DropCommand>())
        {
            MapTile landing = map[put.Tile.X, put.Tile.Y];
            Assert.Equal((lair, (byte)4), (landing.TerrainId, landing.PlayerId));
        }
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }

    [InstallFact]
    public void Level18NemesisHealsItsSleepingBlackKnightUnaidedAndTheSimulationAppliesTheHeal()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = Scenario.Load(Path.Combine(
            RepositoryRoot(), "scenarios", "keepers", "level18_nemesis_heal.yaml"));
        ScenarioResult run = new ScenarioRunner(GameDatabaseLoader.Load(install), install).Run(scenario);

        // The battle step's own cast, not a scripted one, and the level's own availability, not
        // the scenario's: no player lists a spell, and the VARIABLES rows give Nemesis Heal at 3.
        Assert.Empty(run.ExpectationFailures);
        Assert.All(scenario.Setup.Players, p => Assert.Empty(p.Spells));
        GlobalVariables variables = GameDatabaseLoader.LoadCampaignVariables(install, "level18")!;
        Assert.Equal(3, Assert.Single(variables.Availabilities, a => a.Kind == 5 && a.PlayerId == 4 && a.TypeId == 10).Value);
        KeeperCommandEmission emission = Assert.Single(run.KeeperCommandLog, e => e.Command is CastSpellOnCreatureCommand { SpellId: 10 });
        var cast = (CastSpellOnCreatureCommand)emission.Command;
        Assert.Equal((byte)4, cast.PlayerId);
        // The heal lands on the creature the keeper named, for the whole deficit (ADR-0287).
        SimEvent healed = Assert.Single(run.Events, e => e.Kind == "healed");
        Assert.Contains($"target={cast.CreatureId} amount=2500 landed=2500 health=4000", healed.Detail);
        Assert.Equal(healed.Tick.Value, emission.InputTick);
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }

    [InstallFact]
    public void Level18NemesisGrantsLevelsChoosesFabianAndCreepsTheCallTowardHerUnaided()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = Scenario.Load(Path.Combine(
            RepositoryRoot(), "scenarios", "keepers", "level18_nemesis_attack.yaml"));
        ScenarioResult run = new ScenarioRunner(GameDatabaseLoader.Load(install), install).Run(scenario);

        // The planner's own decisions (ADR-0298), not scripted ones: the level's availability
        // rows, the readiness grants as commands, Fabian as the target and the call at the
        // Library's position, the own room furthest from the heart, then crept.
        Assert.Empty(run.ExpectationFailures);
        Assert.All(scenario.Setup.Players, p => Assert.Empty(p.Spells));
        List<GrantCreatureLevelCommand> grants = run.KeeperCommandLog.Select(e => e.Command).OfType<GrantCreatureLevelCommand>().ToList();
        Assert.Equal((12, 4), (grants.Count, grants.Select(g => g.CreatureId).Distinct().Count()));
        Assert.Equal(12, run.Events.Count(e => e.Kind == "level_granted"));
        // Fabian's one breach is used first; with it spent the scan goes on to the others, so the
        // flags end holding more than her bit.
        KeeperAIState state = run.KeeperStates[4];
        Assert.Equal((1 << 5, 1 << 5, (byte)0), (state.PlayersAttackedFlags & (1 << 5), state.FirstAttackFlag & (1 << 5), state.Breaches[5].TunnelsLeft));
        List<CastSpellAtPlaceCommand> casts = run.KeeperCommandLog.Select(e => e.Command).OfType<CastSpellAtPlaceCommand>().ToList();
        Assert.NotEmpty(casts);
        Assert.All(casts, c => Assert.Equal(((byte)4, (byte)3), (c.PlayerId, c.SpellId)));
        Assert.Equal((60, 66), (casts[0].X >> 12, casts[0].Y >> 12));
        Assert.True(casts.Select(c => (c.X >> 12, c.Y >> 12)).Distinct().Count() > 1, "the call never crept");
        Assert.Contains(run.Events, e => e.Kind == "call_to_arms_slapped" && e.Detail.Contains("player=4"));
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }

    [InstallFact]
    public void Level11ADrakoChoosesMorganaStartsItsTunnelFromTheLairAndStopsWhereTheLavaStopsIt()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = Scenario.Load(Path.Combine(
            RepositoryRoot(), "scenarios", "keepers", "level11a_drako_attack.yaml"));
        ScenarioResult run = new ScenarioRunner(GameDatabaseLoader.Load(install), install).Run(scenario);

        // The planner picks Morgana (player 6) among four enemies, tags the corridor's first rock
        // north of the Lair, stores the tunnel at the first step and resumes it with the wait; the
        // lava two tiles on ends task 7 as 0x004FDC50 would without a Stone Bridge, and no call is cast.
        Assert.Empty(run.ExpectationFailures);
        KeeperAIState state = run.KeeperStates[5];
        Assert.Equal((1 << 6, 1 << 6, (byte)0, false), (state.PlayersAttackedFlags, state.FirstAttackFlag, state.Breaches[6].TunnelsLeft, state.Breaches[6].HasStoredTunnel));
        KeeperCommandEmission tag = Assert.Single(run.KeeperCommandLog, e => e.Command is TagTilesCommand t && t.Tiles.Contains(new TilePos(64, 41)));
        Assert.Equal((byte)5, tag.Command.PlayerId);
        Assert.DoesNotContain(run.KeeperCommandLog, e => e.Command is CastSpellAtPlaceCommand);
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }

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

    [InstallFact]
    public void KeeperCommandsAreCountedOnlyWhenThereIsATickToApplyThem()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = Scenario.Load(Path.Combine(
            RepositoryRoot(), "scenarios", "keepers", "level10_asmodeus_idle.yaml"));
        // Mana for the first tick's imp cast: the keeper predicts the cost test as DK2's cast does (ADR-0298).
        scenario.Setup.Players.Single(p => p.Id == 4).Mana = 25000;
        scenario.DurationTicks = 1;
        var runner = new ScenarioRunner(GameDatabaseLoader.Load(install), install);
        ScenarioResult one = runner.Run(scenario);
        Assert.Empty(one.EmittedKeeperCommands);
        Assert.Empty(one.KeeperCommandLog);
        scenario.DurationTicks = 2;
        ScenarioResult two = runner.Run(scenario);
        Assert.NotEmpty(two.KeeperCommandLog);
        Assert.All(two.KeeperCommandLog, e => Assert.InRange(e.InputTick, 1, scenario.DurationTicks - 1));
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

    [Theory]
    [InlineData("", typeof(Xoshiro256StarStar))]
    [InlineData("random: modern", typeof(Xoshiro256StarStar))]
    [InlineData("random: dk2", typeof(Dk2Lcg))]
    public void Load_RandomKeySelectsTheGenerator(string line, Type expected)
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, $"id: random-probe\nmap: flat_8x8\nduration_ticks: 1\n{line}\n");
        try
        {
            Scenario scenario = Scenario.Load(path);
            Assert.Empty(scenario.UnknownKeys);
            IRandomAlgorithm algorithm = scenario.CreateRandom().Algorithm;
            Assert.IsType(expected, algorithm);
            if (algorithm is Dk2Lcg dk2) Assert.Equal(Dk2Lcg.LevelStartSeed, dk2.Seed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsAnUnknownGenerator()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "id: random-probe\nmap: flat_8x8\nduration_ticks: 1\nrandom: dk3\n");
        try
        {
            Assert.Throws<InvalidDataException>(() => Scenario.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
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
        // have to exist. The warning threshold and interval are the shipped ones, so a
        // scenario keeper with more than four imps loses one as DK2's would (ADR-0277).
        vars.Misc[VariableTypeId.LowManaWarningThreshold] =
            new MiscVariable(VariableTypeId.LowManaWarningThreshold, 100, 0, 0);
        vars.Misc[VariableTypeId.InsufficientManaImpEvaluationPeriodSeconds] =
            new MiscVariable(VariableTypeId.InsufficientManaImpEvaluationPeriodSeconds, 10, 0, 0);
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
