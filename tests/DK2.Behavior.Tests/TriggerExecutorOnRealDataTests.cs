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
        // cannot be told apart from it being false, so the walk stops there. Twelve types
        // have left it: 53/56/58/60 (ADR-0109), 30 gold (ADR-0110), 31 gold mined
        // (ADR-0112), 19 creature count (ADR-0113), 25 room count (ADR-0114), 29 spell
        // availability (ADR-0120), and 46/52/71/73 (ADR-0121) -- 46 is what left this
        // census, and it left it answered false rather than implemented.
        Assert.Equal(
            "213:64@World=1",
            Census(simulation.LastTriggerRun!.Summary));

        // Actions, which descend whatever we do because their dispatcher succeeds.
        Assert.Equal(
            "214:14@World=2,214:17@Player=2,214:24@Player=2,214:31@Player=2,214:38@Player=2",
            Census(simulation.LastTriggerRun!.PartialSummary));

        // Nothing on level 1's opening turn reaches a type DK2 itself refuses.
        Assert.Empty(simulation.LastTriggerRun!.Unhandled);

        // Player-context type 72, taken as true rather than declined. If this ever reads
        // zero, the player tree stopped being walked.
        Assert.Equal("213:72@Player=2", Census(simulation.LastTriggerRun!.AssumedSummary));
    }

    /// <summary>Corpus corroboration for the one thing about action type 1 that a wrong
    /// reading would hide: <b>its coordinates are zero-based</b>. The arm at
    /// <c>0x00518B12</c> makes a tile centre of each body word without decrementing,
    /// where type 22's arm decrements first (ADR-0136) — so the two neighbouring arms
    /// read the same pair of words under different conventions, and nothing in the
    /// parameter values themselves says which is which (type 1's smallest coordinate is
    /// 2, so no record is out of range either way).</summary>
    /// <remarks>What discriminates them is where the creature lands. Read one tile up
    /// and left, a scripted hero appears inside solid rock far more often than it should.
    /// The margin is the assertion; the exact counts are not, because they would break on
    /// any change to the terrain table rather than to this rule.</remarks>
    [InstallFact]
    public void CreateCreatureCoordinatesLandOnOpenGroundOnlyWhenReadZeroBased()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);

        int records = 0, solidAsWritten = 0, solidIfDecremented = 0;

        foreach (string level in TriggerLevels(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;

            foreach (TriggerRecord record in
                GameDatabaseLoader.LoadCampaignTriggers(install, level) ?? [])
            {
                if (record.Tag != 214 || record.Type != 1) continue;

                int x = (int)record.Parameters["PosX"], y = (int)record.Parameters["PosY"];
                records++;
                if (Solid(database, map, x, y)) solidAsWritten++;
                if (Solid(database, map, x - 1, y - 1)) solidIfDecremented++;
            }
        }

        Assert.Equal(268, records);

        // Not one of the 268 spawns a creature inside rock, and 67 would if the
        // coordinates were decremented first. Both halves matter: the zero says the
        // reading is right, the 67 says the corpus could have told us it was wrong.
        Assert.Equal(0, solidAsWritten);
        Assert.True(solidIfDecremented > 50,
            $"only {solidIfDecremented} decremented coordinates land in solid terrain, "
            + "so this no longer discriminates the two readings");
    }

    private static bool Solid(GameDatabase database, LevelMap map, int x, int y) =>
        map.Contains(x, y) && database.TerrainById(map.Tiles[(y * map.Width) + x].TerrainId).IsSolid;

    private static IEnumerable<string> TriggerLevels(string install)
    {
        string maps = Path.Combine(install, "Data", "editor", "maps");
        foreach (string file in Directory.EnumerateFiles(maps, "*Triggers.kld").OrderBy(f => f))
            yield return Path.GetFileName(file)[..^"Triggers.kld".Length];
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

    [InstallFact]
    public void EveryShippedType25RecordUsesTheRoomCountMode()
    {
        string maps = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "maps");
        TriggerRecord[] records = Directory.GetFiles(maps, "*Triggers.kld")
            .SelectMany(file => KwdTriggers.Parse(KwdReader.ReadFile(file)))
            .Where(record => record.Tag == 213 && record.Type == 25)
            .ToArray();

        Assert.Equal(85, records.Length);
        Assert.Equal(
            new Dictionary<long, int> { [1] = 1, [2] = 3, [3] = 30, [4] = 8, [5] = 43 },
            records.GroupBy(record => record.Parameters["Comparison"])
                .ToDictionary(group => group.Key, group => group.Count()));
        Assert.DoesNotContain(records, record => record.Parameters["Comparison"] == 0);
    }

    // The context is part of the pin, not commentary: the same type number is dispatched
    // by a different arm in each tree, so a census that omits it can stay green while the
    // walk that produced it has moved to a different rule.
    private static string Census(IEnumerable<TriggerCensusRow> summary) =>
        string.Join(",", summary.Select(s => $"{s.Tag}:{s.Type}@{s.Context}={s.Count}"));

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

    /// <summary>With level 1's THINGS in the world, its two type-14 records raise their parties on the
    /// opening turn (ADR-0197): hero-owned creatures exist and type 14 has left <c>Partial</c>. The
    /// census test above keeps a world without THINGS, where the action can only arm.</summary>
    [InstallFact]
    public void LevelOnesPartiesAreRaisedOnTheOpeningTurnWhenTheWorldHasItsThings()
    {
        (GameSimulation simulation, _) = LevelOne(seed: 7, withThings: true);
        Assert.All(simulation.World.HeroParties, p => Assert.Equal((byte)0, p.SpawnCount));

        simulation.Tick();

        int heroes = simulation.World.Creatures.Count(c => c.OwnerPlayerId == PlayerIds.Good && c.PartyId != HeroPartyRules.NoParty);
        Assert.True(heroes > 0, "no party member was raised");
        Assert.DoesNotContain("214:14@World", Census(simulation.LastTriggerRun!.PartialSummary));
        Assert.Equal(2, simulation.World.HeroParties.Count(p => p.SpawnCount == 1));
    }

    private static (GameSimulation Simulation, TriggerTree Tree) LevelOne(ulong seed, bool withThings = false)
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
        if (withThings) ThingProjection.Place(world, database, things);
        var tree = new TriggerTree(
            records, level.TriggerId, things.OfType<HeroPartyThing>(),
            GameDatabaseLoader.LoadCampaignPlayers(install, "level1")!
                .Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
        return (new GameSimulation(database, world, seed, triggers: tree), tree);
    }
}
