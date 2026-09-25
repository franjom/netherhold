// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Angry job 16, read from <c>0x00475170</c> and <c>0x004750B0</c>
/// (docs/research/rebellion.md). The Portal comes first and turning Good is the
/// fallback, which is not what the name suggests and is why Q116's two accounts both
/// looked right.
/// <para>What is <b>not</b> tested, because it is not built: the <c>RebelActive</c> and
/// <c>RebelLeader</c> flags, creature action 43, and notification type 19. The three
/// <c>Rebel*Percentage</c> variables are not tested either — they are read by nothing in
/// DKII.EXE, which <see cref="NothingAboutRebellionCanDependOnTheThreeOutcomePercentages"/> pins
/// from this side.</para></summary>
public class RebellionTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const int Second = SimulationTiming.TicksPerSecond;
    private const byte VampireId = Dk2TerrainFixture.VampireCreatureId;

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    /// <summary>With the hero side present, because that is who a rebel defects to and
    /// a world without it has nobody to become.</summary>
    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new CreatureMovementSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
            new CreatureAngerSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static void PaintPortal(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(RoomTypeId.Portal), Keeper);
    }

    /// <summary>A rebel with a way out takes it, and never starts the countdown.</summary>
    private static CreatureState AngryVampire(GameDatabase db, WorldState world, TilePos at)
    {
        CreatureState vampire = world.SpawnCreature(db, VampireId, Keeper, at);
        vampire.Anger.Lair = CreatureAngerRules.Ceiling + 1;
        return vampire;
    }

    [Fact]
    public void ARebelWithAReachablePortalWalksOutThroughItInstead()
    {
        // The first branch of 0x00475170: find the nearest Portal of the creature's OWN
        // keeper and route to it in state 194, the same state job 10 Leave sets. Only
        // when that fails does anything else happen, so a keeper with a portal never
        // loses a creature to the heroes - it just loses the creature.
        GameDatabase db = Db;
        WorldState world = World(db);
        PaintPortal(db, world, new TilePos(3, 3));
        CreatureState vampire = AngryVampire(db, world, new TilePos(1, 1));

        var events = new ListEventSink();
        Simulation(db, world, events).Run(Second * 30);

        Assert.Empty(world.Creatures);
        Assert.Single(events.Events, e => e.Kind == "creature_left");
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_rebelling");
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_defected");
        Assert.Equal(0, vampire.RebelCountdownTicks);
    }

    [Fact]
    public void ARebelTakenFromItsJobIsIdleInStateOne()
    {
        // The countdown starts from idle; a creature at work leaves its job's state for 1.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState vampire = AngryVampire(db, world, new TilePos(2, 2));
        vampire.CurrentJob = JobType.Train;
        vampire.Activity = CreatureActivity.AtWork;
        StatePlacement.Place(vampire, JobRules.StateFor(JobType.Train)!.Value);

        Simulation(db, world).Run(Second);

        Assert.Equal(RebellionRules.DefectDelayTicks, vampire.RebelCountdownTicks);
        Assert.Equal(CreatureActivity.Idle, vampire.Activity);
        Assert.Equal(CreatureStateMachine.Idle, vampire.Dk2StateId);
    }

    [Fact]
    public void TheCountdownIsThirtyTicksAndTheThirtiethIsStillWaiting()
    {
        // 0x004750B0 compares the tick against its deadline with JBE, so the tick that
        // equals it still waits. Thirty ticks is 7.5 game seconds, which is why the
        // countdown runs per tick rather than with the rest of the anger ladder: a
        // per-second pass could only have produced 7 or 8.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState vampire = AngryVampire(db, world, new TilePos(2, 2));

        GameSimulation sim = Simulation(db, world);

        // The first roll lands on the last tick of a game second, so run until one
        // has happened and the countdown is loaded.
        sim.Run(Second);
        Assert.Equal(RebellionRules.DefectDelayTicks, vampire.RebelCountdownTicks);
        Assert.Equal(Keeper, vampire.OwnerPlayerId);

        sim.Run(RebellionRules.DefectDelayTicks - 1);
        Assert.Equal(Keeper, vampire.OwnerPlayerId);
        Assert.Equal(1, vampire.RebelCountdownTicks);

        sim.Run(1);
        Assert.Equal(RebellionRules.DefectsTo, vampire.OwnerPlayerId);
        Assert.Equal(0, vampire.RebelCountdownTicks);
    }

    [Fact]
    public void ADefectorRemembersWhichKeeperItRebelledAgainst()
    {
        // 0x0048BE20(19, oldOwner, 0) writes the remembered-job triple at creature
        // +0x409: job 19 is KillPlayer and the player is the keeper just left. The job
        // is remembered here and not performed - job 19 has no handler in this
        // simulation - but losing the target would make it unrecoverable later.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState vampire = AngryVampire(db, world, new TilePos(2, 2));

        Simulation(db, world).Run(Second * 30);

        Assert.Equal(JobType.KillPlayer, vampire.CurrentJob);
        Assert.Equal(Keeper, vampire.RebelAgainstPlayerId);
        Assert.Equal(RebellionRules.DefectsTo, vampire.OwnerPlayerId);

        // And it does not rebel against itself once it is on the hero side, which
        // would overwrite the keeper above (ours, not DK2's - see Rebel).
        Simulation(db, world).Run(Second * 30);
        Assert.Equal(Keeper, vampire.RebelAgainstPlayerId);
    }

    [Fact]
    public void APrisonerAndACreatureInTheHandCannotRebel()
    {
        // Two of job 16's three guards. The third, CombatPitFighter, is inert here:
        // nothing sets it, so it can only ever pass (RebellionRules.CanRebel).
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState vampire = AngryVampire(db, world, new TilePos(2, 2));

        vampire.PrisonOwnerPlayerId = PlayerIds.FirstKeeper + 1;
        Assert.False(RebellionRules.CanRebel(vampire));

        vampire.PrisonOwnerPlayerId = PrisonRules.NoPrison;
        Assert.True(RebellionRules.CanRebel(vampire));

        vampire.InHand = true;
        Assert.False(RebellionRules.CanRebel(vampire));
    }

    [Fact]
    public void NothingAboutRebellionCanDependOnTheThreeOutcomePercentages()
    {
        // The three percentages and RebelMaxFollowers are read by nothing in DKII.EXE:
        // each global's address occurs exactly once in the whole image, against two
        // positive controls that returned their documented reader counts
        // (rebellion.md 5). Pinned by shape, because a value test would have to invent
        // numbers the original never reads: wiring a percentage means reading the
        // VARIABLES table, which means taking a GameDatabase, which fails here.
        Type rules = typeof(RebellionRules);
        Assert.NotEmpty(rules.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.All(
            rules.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
            m => Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == typeof(GameDatabase)));

        // And the whole of src/, not just this class: RebelMaxFollowers is the fourth
        // dead one. Matched on the qualified form, because that is what a reader has to
        // write - the prose above names three of them and is not a reader.
        string[] sources = Directory.GetFiles(
            Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sources);
        foreach (VariableTypeId dead in new[]
        {
            VariableTypeId.RebelMaxFollowers,
            VariableTypeId.RebelLeavingPercentage,
            VariableTypeId.RebelBecomeGoodPercentage,
            VariableTypeId.RebelDefectPercentage,
        })
        {
            Assert.DoesNotContain(sources, f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && f.EndsWith(".cs", StringComparison.Ordinal)
                && !f.EndsWith("VariableTypeId.cs", StringComparison.Ordinal)
                && File.ReadAllText(f).Contains($"VariableTypeId.{dead}", StringComparison.Ordinal));
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DK2.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
