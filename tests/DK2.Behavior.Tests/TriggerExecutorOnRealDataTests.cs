// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class TriggerExecutorOnRealDataTests
{
    [InstallFact]
    public void LevelOneTreeWalksAndItsBlockedTypeCensusIsPinned()
    {
        (GameSimulation simulation, _) = LevelOne(seed: 7);
        simulation.Tick();

        // Turn 1 across all three trees this executor walks. The world root, trigger 89,
        // is a type-34 level-time test that holds from the first turn, so both of its
        // type-14 children fire and arm their parties; party 1's root is the type 64. The
        // rest is the player tree, which is where level 1 keeps its script.

        // Blocked is conditions only, and it is the work queue: declining a condition
        // cannot be told apart from it being false, so the walk stops there.
        Assert.Equal(
            "213:19=1,213:25=4,213:30=3,213:31=3,213:46=1,213:53=2,213:56=1,213:58=1,"
            + "213:60=1,213:64=1",
            Census(simulation.LastTriggerRun!.Summary));

        // Actions, which descend whatever we do because their dispatcher succeeds.
        Assert.Equal(
            "214:14=2,214:17=2,214:24=2,214:31=2,214:38=2,214:44=1,214:45=1",
            Census(simulation.LastTriggerRun!.PartialSummary));

        // Nothing on level 1's opening turn reaches a type DK2 itself refuses.
        Assert.Empty(simulation.LastTriggerRun!.Unhandled);

        // Player-context type 72, taken as true rather than declined. If this ever reads
        // zero, the player tree stopped being walked.
        Assert.Equal("213:72=2", Census(simulation.LastTriggerRun!.AssumedSummary));
    }

    /// <summary>The world tree is <b>not</b> where campaign level 1 keeps its script, and a
    /// reader of the CSV this executor emits has to know that before concluding anything
    /// from an empty column.</summary>
    /// <remarks>
    /// The world root, trigger 89, spawns two parties and ends: <c>next = 0</c>. The chain
    /// that carries the level -- 1 to 10 to 15 to 31, the timers and the flags the live
    /// captures show moving -- hangs off trigger <b>1</b>, which is the single
    /// <b>player</b> root, walked by <c>FUN_004C79A0</c> and not by anything here. This
    /// test exists so that arrangement cannot quietly change under a claim that our world
    /// walker is complete.
    /// </remarks>
    [InstallFact]
    public void LevelOneKeepsItsScriptOnThePlayerRootNotTheWorldRoot()
    {
        string install = Dk2InstallGate.Path;
        LevelDef level = GameDatabaseLoader.LoadCampaignLevel(install, "level1")!;
        List<TriggerRecord> records = GameDatabaseLoader.LoadCampaignTriggers(install, "level1")!;
        ushort[] playerRoots = GameDatabaseLoader.LoadCampaignPlayers(install, "level1")!
            .Select(p => p.TriggerId).Where(id => id != 0).ToArray();

        Assert.Equal(89, level.TriggerId);
        Assert.Equal([(ushort)1], playerRoots);

        TriggerRecord world = records.Single(r => r.Tag == 213 && r.TriggerId == 89);
        TriggerRecord player = records.Single(r => r.Tag == 213 && r.TriggerId == 1);
        Assert.Equal(34, world.Type);
        Assert.Equal(34, player.Type);

        // The world root's chain is two records long and both are party spawns; the player
        // root's runs on. That length difference is the whole point.
        Assert.Equal(0, world.NextTriggerId);
        Assert.Equal(10, player.NextTriggerId);
        Assert.Equal(
            [(byte)14, (byte)14],
            Chain(records, world.ChildTriggerId).Select(r => r.Type).ToArray());
        Assert.True(Chain(records, player.NextTriggerId).Count > 10);
    }

    private static List<TriggerRecord> Chain(List<TriggerRecord> records, ushort from)
    {
        var chain = new List<TriggerRecord>();
        var seen = new HashSet<ushort>();
        for (ushort id = from; id != 0 && seen.Add(id);)
        {
            short signed = unchecked((short)id);
            TriggerRecord? record = records.FirstOrDefault(
                r => r.TriggerId == id && r.Tag == (signed > 0 ? 213 : 214));
            if (record is null) break;
            chain.Add(record);
            id = record.NextTriggerId;
        }
        return chain;
    }

    /// <summary>The whole reason an action's return value cannot buy this executor any
    /// reach: <b>every shipped action record is a leaf</b>. A walk descends into
    /// <c>ChildTriggerId</c> and only a condition ever has one, so the success or failure
    /// of an action decides nothing about where the walk goes next (ADR-0107). If this ever
    /// fails, un-blocking an action type starts to matter and the census above will move.
    /// </summary>
    [InstallFact]
    public void NoShippedActionRecordHasAChild()
    {
        string maps = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "maps");
        TriggerRecord[] actions = Directory.GetFiles(maps, "*Triggers.kld")
            .SelectMany(f => KwdTriggers.Parse(KwdReader.ReadFile(f)))
            .Where(r => r.Tag == 214)
            .ToArray();

        Assert.Equal(5408, actions.Length);
        Assert.All(actions, a => Assert.Equal(0, a.ChildTriggerId));

        // And conditions do branch, so the corpus is not simply flat.
        Assert.Contains(
            Directory.GetFiles(maps, "*Triggers.kld")
                .SelectMany(f => KwdTriggers.Parse(KwdReader.ReadFile(f))),
            r => r.Tag == 213 && r.ChildTriggerId != 0);
    }

    private static string Census(IEnumerable<(int Tag, byte Type, int Count)> summary) =>
        string.Join(",", summary.Select(s => $"{s.Tag}:{s.Type}={s.Count}"));

    [InstallFact]
    public void LevelOneTriggerRunIsDeterministicForTheSameSeed()
    {
        (GameSimulation a, _) = LevelOne(seed: 0x1234);
        (GameSimulation b, _) = LevelOne(seed: 0x1234);

        for (int i = 0; i < 200; i++)
        {
            a.Tick();
            b.Tick();
        }

        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [InstallFact]
    public void EveryShippedIndirectFlagActionExecutesItsDecodedOperand()
    {
        string install = Dk2InstallGate.Path;
        string maps = Path.Combine(install, "Data", "editor", "maps");
        TriggerRecord[] indirect = Directory.EnumerateFiles(maps, "*Triggers.kld")
            .Order(StringComparer.OrdinalIgnoreCase)
            .SelectMany(file => GameDatabaseLoader.LoadCampaignTriggers(
                install, Path.GetFileName(file)[..^"Triggers.kld".Length]) ?? [])
            .Where(r => r.Tag == 214 && r.Type == 7 && (r.Parameters["Flag"] & 0x02) == 0)
            .ToArray();

        Assert.Equal(4, indirect.Length);
        foreach (TriggerRecord action in indirect)
        {
            TriggerRecord isolated = new ActionTriggerRecord(new byte[16])
            {
                Body = action.Body, TriggerId = action.TriggerId, Type = action.Type,
                RepeatCount = action.RepeatCount, Parameters = action.Parameters,
            };
            var state = new TriggerState();
            byte target = (byte)action.Parameters["FlagId"];
            byte source = unchecked((byte)action.Parameters["Value"]);
            state.Write(target, FlagWriteMode.Assign, 5);
            if (source != target) state.Write(source, FlagWriteMode.Assign, 23);
            int operand = state.Read(source);
            byte mode = (byte)action.Parameters["Flag"];

            new TriggerExecutor(new TriggerTree([isolated], isolated.TriggerId), state).Run(1);

            int expected = (mode & 0x08) != 0 ? operand : unchecked((byte)(5 + operand));
            Assert.Equal(expected, state.Read(target));
        }
    }

    private static (GameSimulation Simulation, TriggerTree Tree) LevelOne(ulong seed)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "level1");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level1")!;
        LevelDef level = GameDatabaseLoader.LoadCampaignLevel(install, "level1")!;
        List<TriggerRecord> records = GameDatabaseLoader.LoadCampaignTriggers(install, "level1")!;
        byte[] players = GameDatabaseLoader.LoadCampaignPlayers(install, "level1")!
            .Select(p => p.PlayerId).ToArray();
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(install, "level1")!;
        WorldState world = WorldState.FromMap(database, map, players);
        var tree = new TriggerTree(
            records, level.TriggerId, things.OfType<HeroPartyThing>(),
            GameDatabaseLoader.LoadCampaignPlayers(install, "level1")!.Select(p => p.TriggerId));
        return (new GameSimulation(database, world, seed, triggers: tree), tree);
    }
}
