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
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

// The computer keepers' scenarios, one class per level. Each runs a whole campaign level under
// the keeper AI and costs tens of seconds, and xUnit runs the tests of one class in sequence, so
// a class per level lets them run side by side. Every scenario here that carries expectations
// is listed in ScenarioCorpus.RunByTheirOwnTest, and its test asserts there are no failures.

public class Level10KeeperScenarioTests(ITestOutputHelper output)
{
    [InstallFact]
    public void Level10ComputerKeeperRespectsLockedRoomAvailability()
    {
        Scenario scenario = ScenarioCorpus.Load("keepers/level10_asmodeus_idle.yaml");
        ScenarioResult run = ScenarioCorpus.Run(scenario, output);

        Assert.DoesNotContain(run.EmittedKeeperCommands, c => c is BuildRoomCommand);
        Assert.Contains(run.EmittedKeeperCommands, c => c is CreateImpCommand);
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }

    [InstallFact]
    public void Level10AsmodeusDropsItsCreaturesOnItsTrainingRoomsInteriorsUnaided()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = ScenarioCorpus.Load("keepers/level10_asmodeus_idle.yaml");
        scenario.DurationTicks = 200;
        ScenarioResult run = ScenarioCorpus.Run(scenario, output);

        // Skill level 10 and a minimum of 30: never ready, so step 9 moves its creatures (ADR-0317).
        List<KeeperCommandEmission> moves = run.KeeperCommandLog.Where(e => e.Command is PickUpCreatureCommand or DropCommand).ToList();
        Assert.NotEmpty(moves);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level10")!;
        byte training = ScenarioCorpus.Database.RoomById((byte)RoomTypeId.TrainingRoom).TerrainId;
        foreach (DropCommand put in moves.Select(e => e.Command).OfType<DropCommand>())
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    MapTile around = map[put.Tile.X + dx, put.Tile.Y + dy];
                    Assert.Equal((training, (byte)4), (around.TerrainId, around.PlayerId));
                }
    }

    [InstallFact]
    public void KeeperCommandsAreCountedOnlyWhenThereIsATickToApplyThem()
    {
        Scenario scenario = ScenarioCorpus.Load("keepers/level10_asmodeus_idle.yaml");
        // Mana for the first tick's imp cast: the keeper predicts the cost test as DK2's cast does (ADR-0298).
        scenario.Setup.Players.Single(p => p.Id == 4).Mana = 25000;
        scenario.DurationTicks = 1;
        ScenarioResult one = ScenarioCorpus.Run(scenario, output);
        Assert.Empty(one.EmittedKeeperCommands);
        Assert.Empty(one.KeeperCommandLog);
        scenario.DurationTicks = 2;
        ScenarioResult two = ScenarioCorpus.Run(scenario, output);
        Assert.NotEmpty(two.KeeperCommandLog);
        Assert.All(two.KeeperCommandLog, e => Assert.InRange(e.InputTick, 1, scenario.DurationTicks - 1));
    }
}

public class Level11AKeeperScenarioTests(ITestOutputHelper output)
{
    [InstallFact]
    public void Level11ADrakoBridgesTheLavaWithStoneThenCallsItsArmyToMorganasHeart()
    {
        Scenario scenario = ScenarioCorpus.Load("keepers/level11a_drako_attack.yaml");
        ScenarioResult run = ScenarioCorpus.Run(scenario, output);

        // The planner picks Morgana (player 6) among four enemies and tags the corridor's first rock
        // north of the Lair; the lava two tiles on takes two Stone Bridge tiles, one a tick, each
        // touching Drako's own floor (0x004FDC50, 0x0044F630, ADR-0304); the tunnel goes on, and the
        // front casts the call two tiles off Morgana's heart. Lava never takes the Wooden Bridge.
        Assert.Empty(run.ExpectationFailures);
        KeeperAIState state = run.KeeperStates[5];
        // Morgana first; by tick 200 a later plan has also chosen Raksha (bit 7).
        Assert.Equal((1 << 6, 1 << 6), (state.PlayersAttackedFlags & (1 << 6), state.FirstAttackFlag & (1 << 6)));
        KeeperCommandEmission tag = Assert.Single(run.KeeperCommandLog, e => e.Command is TagTilesCommand t && t.Tiles.Contains(new TilePos(64, 41)));
        Assert.Equal((byte)5, tag.Command.PlayerId);

        List<(int Tick, BuildRoomCommand Build)> spans = run.KeeperCommandLog
            .Where(e => e.Command is BuildRoomCommand b && BridgeRules.IsBridge(ScenarioCorpus.Database.RoomById(b.RoomId)))
            .Select(e => (e.InputTick, (BuildRoomCommand)e.Command)).ToList();
        Assert.Equal([(47, new TilePos(64, 40)), (48, new TilePos(64, 39))], spans.Take(2).Select(s => (s.Tick, s.Build.Tiles[0])));
        Assert.All(spans, s => Assert.Equal((byte)RoomTypeId.StoneBridge, s.Build.RoomId));

        var call = (CastSpellAtPlaceCommand)Assert.Single(run.KeeperCommandLog, e => e.Command is CastSpellAtPlaceCommand).Command;
        Assert.Equal(((byte)CallToArmsRules.SpellId, 64, 34), (call.SpellId, call.X >> 12, call.Y >> 12));
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }
}

public class Level17KeeperScenarioTests(ITestOutputHelper output)
{
    [InstallFact]
    public void Level17HarkanCastsTheCallAtItsFighterAndTakesItDownUnaided()
    {
        Scenario scenario = ScenarioCorpus.Load("keepers/level17_harkan_call_to_arms.yaml");
        ScenarioResult run = ScenarioCorpus.Run(scenario, output);

        // The battle step's own commands, not a scripted cast: action 54 on the first turn
        // divisible by 40 at the Black Knight's tile, and action 63 once the call is seen and
        // Harkan's removal gates fail (ADR-0283).
        // A later fight still running on a later such turn draws another call: which fights last
        // that long depends on the run's draws, so every call is checked, not their number.
        Assert.Empty(run.ExpectationFailures);
        List<KeeperCommandEmission> casts = run.KeeperCommandLog.Where(e => e.Command is CastSpellAtPlaceCommand).ToList();
        List<KeeperCommandEmission> removals = run.KeeperCommandLog.Where(e => e.Command is RemoveCallToArmsCommand).ToList();
        Assert.NotEmpty(casts);
        var call = (CastSpellAtPlaceCommand)casts[0].Command;
        Assert.Equal((39, (byte)4, (byte)3, 4, 7), (casts[0].InputTick, call.PlayerId, call.SpellId, call.X >> 12, call.Y >> 12));
        Assert.Equal(casts.Count, removals.Count);
        for (int i = 0; i < casts.Count; i++)
        {
            Assert.Equal(39, casts[i].InputTick % 40);
            Assert.InRange(removals[i].InputTick, casts[i].InputTick + 1, casts[i].InputTick + 1 + KeeperBattleStep.CastGraceTurns);
        }
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }

    [InstallFact]
    public void Level17PlayerFiveLiftsItsWoundedBlackKnightToALairUnaided()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = ScenarioCorpus.Load("keepers/level17_player5_retreat.yaml");
        GameDatabase database = ScenarioCorpus.Database;
        ScenarioResult run = ScenarioCorpus.Run(scenario, output);

        // The retreat's own commands, not a scripted move: the wounded creature is passed over
        // while it seeks its bed and lifted once asleep, each pick-up followed at once by a drop
        // on one of player 5's Lair tiles (ADR-0284, the scenario's comment for the turn).
        Assert.Empty(run.ExpectationFailures);
        List<KeeperCommandEmission> moves = run.KeeperCommandLog.Where(e => e.Command is PickUpCreatureCommand or DropCommand).ToList();
        Assert.NotEmpty(moves);
        Assert.All(moves, e => Assert.Equal((byte)5, e.Command.PlayerId));
        Assert.All(moves.Chunk(2), pair => Assert.True(pair[0].Command is PickUpCreatureCommand && pair[1].Command is DropCommand
            && pair[0].InputTick == pair[1].InputTick));
        Assert.True(moves[0].InputTick > 1, "the lift waits for the bed, it is not the first tick's");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level17")!;
        byte lair = database.RoomById((byte)RoomTypeId.Lair).TerrainId;
        foreach (DropCommand put in moves.Select(e => e.Command).OfType<DropCommand>())
        {
            MapTile landing = map[put.Tile.X, put.Tile.Y];
            Assert.Equal((lair, (byte)5), (landing.TerrainId, landing.PlayerId));
        }
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }

    [InstallFact]
    public void Level17HarkanPutsItsOwnBlackKnightsInItsTrainingRoomUnaided()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = ScenarioCorpus.Load("keepers/level17_harkan_training.yaml");
        GameDatabase database = ScenarioCorpus.Database;
        ScenarioResult run = ScenarioCorpus.Run(scenario, output);

        // The training moves' own commands on the stock map, nothing added: each pick-up followed
        // at once by a drop on an interior cell of Harkan's Training Room (ADR-0317).
        Assert.Empty(run.ExpectationFailures);
        List<KeeperCommandEmission> moves = run.KeeperCommandLog.Where(e => e.Command is PickUpCreatureCommand or DropCommand).ToList();
        Assert.NotEmpty(moves);
        Assert.All(moves.Chunk(2), pair => Assert.True(pair[0].Command is PickUpCreatureCommand && pair[1].Command is DropCommand
            && pair[0].InputTick == pair[1].InputTick));
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level17")!;
        byte training = database.RoomById((byte)RoomTypeId.TrainingRoom).TerrainId;
        foreach (DropCommand put in moves.Select(e => e.Command).OfType<DropCommand>())
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    MapTile around = map[put.Tile.X + dx, put.Tile.Y + dy];
                    Assert.Equal((training, (byte)4), (around.TerrainId, around.PlayerId));
                }
        Assert.DoesNotContain(run.Unsupported, s => s.StartsWith("computer_keepers", StringComparison.Ordinal));
    }
}

/// <summary>Its own class rather than <see cref="Level17KeeperScenarioTests"/>'s: it runs the
/// longest of Harkan's scenarios twice.</summary>
public class Level17UnassistedKeeperScenarioTests(ITestOutputHelper output)
{
    [InstallFact]
    public void UnaidedKeeperHasARepeatableCommandStream()
    {
        Scenario scenario = ScenarioCorpus.Load("keepers/level17_harkan_unassisted.yaml");
        ScenarioResult first = ScenarioCorpus.Run(scenario, output);
        ScenarioResult second = ScenarioCorpus.Run(scenario, output);

        // The corpus theory leaves this scenario to this test (ScenarioCorpus.RunByTheirOwnTest).
        Assert.Empty(first.ExpectationFailures);
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
}

public class Level18KeeperScenarioTests(ITestOutputHelper output)
{
    [InstallFact]
    public void Level18NemesisHealsItsSleepingBlackKnightUnaidedAndTheSimulationAppliesTheHeal()
    {
        string install = Dk2InstallGate.Path;
        Scenario scenario = ScenarioCorpus.Load("keepers/level18_nemesis_heal.yaml");
        ScenarioResult run = ScenarioCorpus.Run(scenario, output);

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
}

/// <summary>Its own class rather than <see cref="Level18KeeperScenarioTests"/>'s: the attack
/// planner's run is the slowest scenario in the corpus.</summary>
public class Level18AttackKeeperScenarioTests(ITestOutputHelper output)
{
    [InstallFact]
    public void Level18NemesisGrantsLevelsChoosesFabianAndCreepsTheCallTowardHerUnaided()
    {
        Scenario scenario = ScenarioCorpus.Load("keepers/level18_nemesis_attack.yaml");
        ScenarioResult run = ScenarioCorpus.Run(scenario, output);

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
}
