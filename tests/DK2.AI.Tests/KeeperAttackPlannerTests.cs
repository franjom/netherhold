// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.AI.Tests;

/// <summary>The attack planner, the front and attack management against the binary's boundaries
/// (ADR-0298, computer-keeper-management.md §6 and §7): a second keeper's dungeon ten tiles north
/// of the fixture's, joined by an open column when a test says so.</summary>
public class KeeperAttackPlannerTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private const byte Enemy = 4, Other = 5;
    private static readonly TilePos EnemyHeart = new(15, 5);
    private static readonly TilePos OtherHeart = new(26, 15);

    /// <summary>DK2's room CentrePos for a five-by-five heart: its first slab plus three.</summary>
    private static readonly TilePos EnemyHeartPosition = new(16, 6), OwnHeartPosition = new(16, 16);

    /// <summary>The fixture with five-by-five hearts on claimed patches: the keeper's at (15,15),
    /// the enemy's ten tiles north with one tile of rock between the patches; a third keeper's to
    /// the east when asked; that one tile of rock opened to floor when asked.</summary>
    private static WorldState World(bool open = false, bool other = false)
    {
        WorldState world = KeeperWorld.World(Db, morePlayers: other ? [Enemy, Other] : [Enemy]);
        Dungeon(world, Keeper, KeeperWorld.HeartAt);
        Dungeon(world, Enemy, EnemyHeart);
        if (other) Dungeon(world, Other, OtherHeart);
        if (open) KeeperWorld.Set(world, Db, new TilePos(15, 10), KeeperWorld.DirtPath);
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, CallToArmsRules.SpellId, 3);
        world.Player(Keeper).Mana = 20000;
        return world;
    }

    private static void Dungeon(WorldState world, byte owner, TilePos heart)
    {
        for (int y = heart.Y - 4; y <= heart.Y + 4; y++)
            for (int x = heart.X - 4; x <= heart.X + 4; x++)
                if (world.Contains(x, y) && !Db.TerrainById(world.Tile(x, y).TerrainId).IsImpenetrable)
                    KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.ClaimedPath, owner);
        for (int y = heart.Y - 2; y <= heart.Y + 2; y++)
            for (int x = heart.X - 2; x <= heart.X + 2; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.HeartFloor, owner);
    }

    private static KeeperAIParameters Attacker(byte firstEnd = 0, byte firstStart = 2, byte usage = 1, byte wait = 0) =>
        KeeperWorld.Parameters() with
        {
            NeverAttack = false, IAMinimumCreatures = 0, IACreatureSkillLevel = 1, IAThreatSuperiority = 100,
            IAFirstEnd = firstEnd, IAFirstStart = firstStart, CallToArmsUsage = usage, WaitBeforeAttackingAgain = wait,
            BreachAtXPoints = 1, UseKeeperSpells = 4,
        };

    /// <summary>The systems a cast at a place needs to raise the call and the tags need to land.</summary>
    private static ISimulationSystem[] CallSystems() =>
        [new TileTagSystem(), new KeeperSpellSystem(), new CreatureMovementSystem(), new ShotSystem(), new CallToArmsSystem()];

    private static CreatureState Fighter(WorldState world, byte owner, int x, int y, int level = 1)
    {
        CreatureState creature = world.SpawnCreature(Db, KeeperWorld.FighterCreature, owner, new TilePos(x, y));
        while (creature.Level < level) CreatureLevelRules.LevelUp(new StateHookContext(world, Db), Db.CreatureById(KeeperWorld.FighterCreature), creature);
        return creature;
    }

    private static (KeeperAttackPlanner Planner, KeeperAIState State, List<PlayerCommand> Commands) Planner(
        WorldState world, KeeperAIParameters p, KeeperAIState? state = null, ulong seed = 7)
    {
        var view = new KeeperView(Db, world, Keeper);
        if (state is null)
        {
            state = new KeeperAIState();
            state.InitialiseBreaches(p.BreachAtXPoints);
        }
        var rng = new SimRandom(seed);
        var commands = new List<PlayerCommand>();
        var battle = new KeeperBattleStep(view, state, p, rng, commands);
        return (new KeeperAttackPlanner(view, state, p, rng, commands, new KeeperReadiness(view, p, commands), battle), state, commands);
    }

    // ----------------------------------------------------------------- gates and readiness

    [Theory]
    [InlineData(0)] // NeverAttack
    [InlineData(1)] // the attack wait
    [InlineData(2)] // a standing call
    public void NeverAttackTheAttackWaitOrAStandingCallRefuseBeforeReadinessCanGrantALevel(int gate)
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15); Fighter(world, Keeper, 16, 15); Fighter(world, Keeper, 14, 15, 2);
        KeeperAIParameters p = Attacker() with { IACreatureSkillLevel = 2, NeverAttack = gate == 0 };
        var state = new KeeperAIState();
        state.InitialiseBreaches(1);
        if (gate == 1) state.CannotAttackCounter = 5;
        if (gate == 2) state.CallToArmsInUse = true;
        (KeeperAttackPlanner planner, _, List<PlayerCommand> commands) = Planner(world, p, state);

        Assert.False(planner.Plan());
        Assert.Empty(commands);
        Assert.Equal(KeeperTask.Decide, state.Task);
    }

    [Theory]
    [InlineData(2, 2, false)]
    [InlineData(2, 3, true)]
    [InlineData(1, 2, true)]
    public void ReadinessWantsStrictlyMoreNonWorkersThanTheMinimumAndWorkersDoNotCount(int minimum, int fighters, bool ready)
    {
        WorldState world = World();
        for (int i = 0; i < fighters; i++) Fighter(world, Keeper, 13 + i, 15);
        world.SpawnCreature(Db, KeeperWorld.WorkerCreature, Keeper, new TilePos(15, 16));
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker() with { IAMinimumCreatures = (byte)minimum });

        Assert.Equal(ready, planner.Plan());
        Assert.Equal(ready ? KeeperTask.DigPath : KeeperTask.Decide, state.Task);
    }

    [Fact]
    public void FewUnderLevelledFightersAreGrantedALevelAndTheArmyIsReadyOnThePredictedLevels()
    {
        WorldState world = World();
        CreatureState a = Fighter(world, Keeper, 13, 15), b = Fighter(world, Keeper, 14, 15);
        Fighter(world, Keeper, 16, 15, 2);
        (KeeperAttackPlanner planner, KeeperAIState state, List<PlayerCommand> commands) = Planner(world, Attacker() with { IACreatureSkillLevel = 2 });

        // Two below level 2, at most the level and fewer than three: each is granted, and the
        // final walk reads the predicted level, so the attack starts this tick.
        Assert.True(planner.Plan());
        // DK2's owned list holds the newest creature at its head, so b is granted before a.
        Assert.Equal([b.Id, a.Id], commands.OfType<GrantCreatureLevelCommand>().Select(c => c.CreatureId));
        Assert.Equal(KeeperTask.DigPath, state.Task);

        new GameSimulation(Db, world, 1, [new KeeperSpellSystem()]).Tick(new InputFrame(commands));
        Assert.Equal((2, 2), (a.Level, b.Level));
    }

    [Fact]
    public void MoreUnderLevelledFightersThanTheLevelAreNotGrantedAndTheArmyIsNotReady()
    {
        WorldState world = World();
        for (int i = 0; i < 3; i++) Fighter(world, Keeper, 13 + i, 15);
        (KeeperAttackPlanner planner, KeeperAIState state, List<PlayerCommand> commands) = Planner(world, Attacker() with { IACreatureSkillLevel = 2 });

        Assert.False(planner.Plan());
        Assert.Empty(commands);
        Assert.Equal(KeeperTask.Decide, state.Task);
    }

    [Fact]
    public void ThinkGrantsEachCreatureOnceATickWhereTheTrainingStepAndThePlannerBothTest()
    {
        WorldState world = World();
        Fighter(world, Keeper, 13, 15); Fighter(world, Keeper, 14, 15); Fighter(world, Keeper, 16, 15, 2);
        var keeper = new KeeperAI(Keeper, Attacker() with { IACreatureSkillLevel = 2 }, new SimRandom(3));

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        List<GrantCreatureLevelCommand> grants = commands.OfType<GrantCreatureLevelCommand>().ToList();
        Assert.Equal(2, grants.Count);
        Assert.Equal(2, grants.Select(g => g.CreatureId).Distinct().Count());
    }

    [Fact]
    public void IAAllRoomsPlacedWaitsForAllRoomsBuilt()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15); Fighter(world, Keeper, 16, 15);
        KeeperAIParameters p = Attacker() with { IAAllRoomsPlaced = true };
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, p);
        Assert.False(planner.Plan());

        state.AllRoomsBuilt = true;
        (planner, _, _) = Planner(world, p, state);
        Assert.True(planner.Plan());
    }

    [Theory]
    [InlineData(1, 2, false)] // researchable spell, gate on
    [InlineData(1, 3, true)]  // researched
    [InlineData(2, 2, true)]  // the shipped 2 reads as 0
    [InlineData(0, 2, true)]
    public void IAAllSpellsResearchedDeclinesWhileASpellIsResearchable(byte gate, int availability, bool attacks)
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15); Fighter(world, Keeper, 16, 15);
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Thunderbolt, availability);
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker() with { IAAllSpellsResearched = gate });

        Assert.Equal(attacks, planner.Plan());
        Assert.Equal(0, state.UnbuiltResearchMoves); // no Library to free a slot at
    }

    [Fact]
    public void TheResearchGateDrawsARandomLibraryAndCountsTheMoveItWouldMake()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15); Fighter(world, Keeper, 16, 15);
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Thunderbolt, 2);
        for (int x = 13; x <= 14; x++) KeeperWorld.Set(world, Db, new TilePos(x, 20), KeeperWorld.LibraryFloor, Keeper);
        var rng = new SimRandom(5);
        var view = new KeeperView(Db, world, Keeper);
        var state = new KeeperAIState();
        state.InitialiseBreaches(1);
        var commands = new List<PlayerCommand>();
        KeeperAIParameters p = Attacker() with { IAAllSpellsResearched = 1 };
        var planner = new KeeperAttackPlanner(view, state, p, rng, commands, new KeeperReadiness(view, p, commands),
            new KeeperBattleStep(view, state, p, rng, commands));

        Assert.False(planner.Plan());

        // 0x004C6460(6) draws random(libraries) before the move: the stream moved on by one draw.
        Assert.Equal(1, state.UnbuiltResearchMoves);
        var expected = new SimRandom(5);
        expected.NextInt(1);
        Assert.Equal(expected.NextInt(1000), rng.NextInt(1000));
    }

    [Fact]
    public void ARuinedHeartIsNotDrawnAsTheEndRoomAndAnAttackedBitIsNeverCleared()
    {
        WorldState world = World(open: true);
        Fighter(world, Keeper, 15, 15);
        world.Player(Enemy).HeartRuined = true;
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker(firstEnd: 0, firstStart: 2));

        // The room draw skips the ruin bit, so end 0 finds no Heart room and refuses after the bits.
        Assert.False(planner.Plan());
        Assert.Equal(1 << Enemy, state.PlayersAttackedFlags);
        // The front finds no Heart room either and keeps the bit, as DK2's never-null slot test does.
        Assert.False(planner.FindFront());
        Assert.Equal(1 << Enemy, state.PlayersAttackedFlags);
    }

    [Fact]
    public void TheStrengthTestCountsAGrantedLevelAsDK2sTestSeesTheGrantApplied()
    {
        WorldState world = World();
        // One own fighter below the skill: granted to level 2, whose threat then meets the enemy's.
        Fighter(world, Keeper, 15, 15); Fighter(world, Keeper, 16, 15, 2);
        Fighter(world, Enemy, 15, 5, 2); Fighter(world, Enemy, 16, 5);
        KeeperAIParameters p = Attacker() with { IACreatureSkillLevel = 2 };
        (KeeperAttackPlanner planner, KeeperAIState state, List<PlayerCommand> commands) = Planner(world, p);

        Assert.True(planner.Plan());
        Assert.Single(commands.OfType<GrantCreatureLevelCommand>());
        Assert.Equal(Enemy, state.AttackTarget);
    }

    // ----------------------------------------------------------------- the target

    [Theory]
    [InlineData(2, 0, true)]  // 100 against 100: equality suffices
    [InlineData(2, 1, false)] // the enemy's worker adds 2: 102 against 100
    [InlineData(3, 0, false)]
    public void TheWholeListsStrengthMustReachTheEnemysTimesTheSuperiority(int enemyFighters, int enemyWorkers, bool attacks)
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15); Fighter(world, Keeper, 16, 15);
        for (int i = 0; i < enemyFighters; i++) Fighter(world, Enemy, 13 + i, 5);
        for (int i = 0; i < enemyWorkers; i++) world.SpawnCreature(Db, KeeperWorld.WorkerCreature, Enemy, new TilePos(15, 6));
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker());

        Assert.Equal(attacks, planner.Plan());
        Assert.Equal(attacks ? 1 << Enemy : 0, state.PlayersAttackedFlags);
    }

    [Theory]
    [InlineData(0, Enemy)]  // ten tiles beats eleven
    [InlineData(1, Other)]  // the nearer is an ally
    [InlineData(2, Other)]  // the nearer is conquered
    [InlineData(3, 0)]      // both are out of the game
    public void TheNearestEnemyHeartWinsAndAlliesAndFinishedPlayersAreSkipped(int change, byte target)
    {
        WorldState world = World(other: true);
        Fighter(world, Keeper, 15, 15);
        if (change == 1) world.SetAllied(Keeper, Enemy, true);
        if (change == 2) world.Player(Enemy).Status = PlayerStatus.Conquered;
        if (change == 3) { world.Player(Enemy).Status = PlayerStatus.Lost; world.Player(Other).Status = PlayerStatus.Conquered; }
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker());

        Assert.Equal(target != 0, planner.Plan());
        Assert.Equal(target, state.AttackTarget);
    }

    [Fact]
    public void TheFirstAttackTakesTheEnemyHeartAndTheOwnHeartForEndZeroAndStartTwo()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15);
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker(firstEnd: 0, firstStart: 2));

        Assert.True(planner.Plan());
        Assert.Equal((KeeperTask.DigPath, KeeperTask.Done), (state.Task, state.NextTask));
        Assert.Equal((EnemyHeartPosition, OwnHeartPosition), (state.DigRoomAt, state.DigRoomAtCurrent));
        Assert.Equal((1 << Enemy, 1 << Enemy, Enemy, (byte)0), (state.PlayersAttackedFlags, state.FirstAttackFlag, state.AttackTarget, state.Breaches[Enemy].TunnelsLeft));
    }

    [Theory]
    [InlineData(2, 16, 6)]  // the weakest: the heart, with nobody near it
    [InlineData(3, 6, 1)]   // the strongest: the room with the enemy fighter on it
    public void TheFirstAttackRanksTheEnemysRoomsByItsOwnStrengthWithinFiveTiles(byte firstEnd, int x, int y)
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15);
        for (int ex = 5; ex <= 7; ex++) KeeperWorld.Set(world, Db, new TilePos(ex, 1), KeeperWorld.TreasuryFloor, Enemy);
        Fighter(world, Enemy, 6, 1);
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker(firstEnd: firstEnd, firstStart: 2));

        Assert.True(planner.Plan());
        Assert.Equal(new TilePos(x, y), state.DigRoomAt);
    }

    [Fact]
    public void TheStrongestRoomChoiceRefusesWhenEveryStrengthIsZeroAfterTheFlagsAreWritten()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15);
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker(firstEnd: 3, firstStart: 2));

        // Strict greater than zero from zero: no end room, no task, and yet the bits are set.
        Assert.False(planner.Plan());
        Assert.Equal(KeeperTask.Decide, state.Task);
        Assert.Equal((1 << Enemy, 1 << Enemy, 1), (state.PlayersAttackedFlags, state.FirstAttackFlag, (int)state.Breaches[Enemy].TunnelsLeft));
    }

    [Fact]
    public void StartZeroTakesTheOwnRoomWhosePositionLiesFurthestFromTheHeartPosition()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15);
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker(firstEnd: 0, firstStart: 0));
        // The heart room's own CentrePos, first slab plus three, lies off the heart thing's tile,
        // strictly above zero, so the heart room itself is the furthest when it is the only room.
        Assert.True(planner.Plan());
        Assert.Equal(OwnHeartPosition, state.DigRoomAtCurrent);

        for (int x = 13; x <= 14; x++) KeeperWorld.Set(world, Db, new TilePos(x, 21), KeeperWorld.TreasuryFloor, Keeper);
        (planner, state, _) = Planner(world, Attacker(firstEnd: 0, firstStart: 0));
        Assert.True(planner.Plan());
        Assert.Equal(new TilePos(13, 21), state.DigRoomAtCurrent);
    }

    [Fact]
    public void StartThreeTakesTheOwnNonBridgeRoomNearestTheEndRoomsFirstSlab()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15);
        for (int x = 13; x <= 14; x++) KeeperWorld.Set(world, Db, new TilePos(x, 11), KeeperWorld.TreasuryFloor, Keeper);
        (KeeperAttackPlanner planner, KeeperAIState state, _) = Planner(world, Attacker(firstEnd: 0, firstStart: 3));

        // The Treasury's first slab (13,11) is nearer the end's first slab (13,3) than the heart's (13,13).
        Assert.True(planner.Plan());
        Assert.Equal(new TilePos(13, 11), state.DigRoomAtCurrent);
    }

    [Fact]
    public void ALaterAttackOnTheSamePlayerDrawsBothRoomsAtRandom()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15);
        var state = new KeeperAIState { FirstAttackFlag = 1 << Enemy };
        state.InitialiseBreaches(1);
        (KeeperAttackPlanner planner, _, _) = Planner(world, Attacker(firstEnd: 3, firstStart: 0), state);

        // The all-zero strongest choice is not consulted now: a random room each, one apiece.
        Assert.True(planner.Plan());
        Assert.Equal((EnemyHeartPosition, OwnHeartPosition), (state.DigRoomAt, state.DigRoomAtCurrent));
    }

    [Fact]
    public void AttackerOnlyModeAttacksTheRecordedAttackerOnceAndForgetsIt()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15);
        KeeperAIParameters p = Attacker() with { IAOnlyAttackAttackers = true };
        var state = new KeeperAIState { HaveBeenAttacked = true, AttackingPlayerId = Enemy };
        state.InitialiseBreaches(1);
        (KeeperAttackPlanner planner, _, _) = Planner(world, p, state);

        // The end is the attacker's nearest non-bridge room's first slab, the start the nearest own room's position.
        Assert.True(planner.Plan());
        Assert.Equal((new TilePos(13, 3), OwnHeartPosition, Enemy, false, (byte)0), (state.DigRoomAt, state.DigRoomAtCurrent, state.AttackTarget, state.HaveBeenAttacked, state.AttackingPlayerId));

        state.BeginTask(KeeperTask.Decide);
        (planner, _, _) = Planner(world, p, state);
        Assert.False(planner.Plan());
    }

    // ----------------------------------------------------------------- the tunnel

    [Fact]
    public void TheTunnelIsStoredWhereTheCursorMeetsUnownedLandAndResumedWithTheAttackWait()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15);
        var keeper = new KeeperAI(Keeper, Attacker(wait: 30), new SimRandom(3));
        var sim = new GameSimulation(Db, world, 1, [new TileTagSystem(), new KeeperSpellSystem()]);
        KeeperAIState state = keeper.State;

        sim.Tick(new InputFrame(keeper.Think(Db, world)));
        Assert.Equal((KeeperTask.DigPath, Enemy), (state.Task, state.AttackTarget));
        // The walker tags the one rock tile on the line north; the cursor stands on it.
        IReadOnlyList<PlayerCommand> second = keeper.Think(Db, world);
        Assert.Equal(new TilePos(16, 10), Assert.Single(Assert.Single(second.OfType<TagTilesCommand>()).Tiles));
        sim.Tick(new InputFrame(second));
        // Unowned rock in the cursor's 3x3: the rest of the tunnel is stored and the task ends.
        sim.Tick(new InputFrame(keeper.Think(Db, world)));
        Assert.Equal((KeeperTask.Done, (byte)0, new TilePos(16, 10), EnemyHeartPosition),
            (state.Task, state.AttackTarget, state.Breaches[Enemy].Start[0], state.Breaches[Enemy].End[0]));
        sim.Tick(new InputFrame(keeper.Think(Db, world)));
        Assert.Equal(KeeperTask.Decide, state.Task);
        // Task 0: the enemy's tunnels are used up and one is stored, so it is resumed with the wait.
        sim.Tick(new InputFrame(keeper.Think(Db, world)));
        Assert.Equal((KeeperTask.DigPath, new TilePos(16, 10), EnemyHeartPosition, 120, false),
            (state.Task, state.DigRoomAtCurrent, state.DigRoomAt, state.CannotAttackCounter, state.Breaches[Enemy].HasStoredTunnel));
    }

    [Fact]
    public void AnAttackTunnelWhoseTargetLiesOutsideTheBuildAreaDigsOn()
    {
        WorldState world = World();
        Fighter(world, Keeper, 15, 15);
        var keeper = new KeeperAI(Keeper, Attacker(), new SimRandom(3), buildArea: new BuildArea(0, 10, 29, 29));

        keeper.Think(Db, world);
        IReadOnlyList<PlayerCommand> second = keeper.Think(Db, world);

        Assert.Equal(KeeperTask.DigPath, keeper.State.Task);
        Assert.Single(second.OfType<TagTilesCommand>());
    }

    // ----------------------------------------------------------------- the front and the call

    [Fact]
    public void AnOpenWayEndsTheTunnelAtOnceAndTheCallIsCastBesideTheEnemyHeartThroughTheSimulation()
    {
        WorldState world = World(open: true);
        Fighter(world, Keeper, 15, 15); Fighter(world, Keeper, 16, 15);
        // Seed 2 draws the diagonal away from the keeper's own land, which this test needs (no drop
        // site within five of the call). The idle fighters' slap rolls share the stream, so a change
        // to who may be slapped moves the draw (seed 3 until 2026-10-06).
        var keeper = new KeeperAI(Keeper, Attacker(usage: 1), new SimRandom(2));
        var sim = new GameSimulation(Db, world, 1, CallSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Tick(new InputFrame(keeper.Think(Db, world)));
        Assert.Equal(KeeperTask.DigPath, keeper.State.Task);

        // Step 7: the front is two tiles off the enemy heart's position in a random diagonal, and
        // the cast goes there through action 54 the tick a walker routes to the drawn point; task 7
        // finds the route and ends the tunnel without a tag on a tick the front left it allowance.
        CastSpellAtPlaceCommand? call = null;
        for (int t = 0; t < 8 && call is null; t++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            call = commands.OfType<CastSpellAtPlaceCommand>().SingleOrDefault();
            sim.Tick(new InputFrame(commands));
        }
        Assert.NotNull(call);
        Assert.Equal(CallToArmsRules.SpellId, call!.SpellId);
        Assert.Contains(call.X >> 12, new[] { EnemyHeartPosition.X - 2, EnemyHeartPosition.X + 2 });
        Assert.Contains(call.Y >> 12, new[] { EnemyHeartPosition.Y - 2, EnemyHeartPosition.Y + 2 });
        Assert.True(keeper.State.CallToArmsInUse);
        Assert.Equal(250, keeper.State.CallToArmsCounter);
        Assert.Equal(0, keeper.State.Responses.Count); // no owned drop site within five of the call
        Assert.Contains(sink.Events, e => e.Kind == "call_to_arms_raised" && e.Detail.Contains($"player={Keeper}"));
        // With the call up, usage 1 leaves step 7 to the removal test, so the tunnel task runs.
        sim.Tick(new InputFrame(keeper.Think(Db, world)));
        Assert.Equal(KeeperTask.Done, keeper.State.Task);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "tagged");
    }

    [Fact]
    public void UsageOneQueuesHalfTheArmysThreatForReinforcementAtAnOwnedSiteNearTheCall()
    {
        WorldState world = World(open: true);
        // Own land within five tiles of every point the front can draw: a row at the enemy's edge.
        for (int x = 13; x <= 17; x++) KeeperWorld.Set(world, Db, new TilePos(x, 9), KeeperWorld.ClaimedPath, Keeper);
        Fighter(world, Keeper, 15, 15, 2); Fighter(world, Keeper, 16, 15);
        var keeper = new KeeperAI(Keeper, Attacker(usage: 1), new SimRandom(3));

        for (int t = 0; t < 8 && !keeper.State.CallToArmsInUse; t++) keeper.Think(Db, world);

        Assert.Equal(1, keeper.State.Responses.Count);
        KeeperThreatResponse queued = keeper.State.Responses.Current!.Value;
        int level2 = ThreatEvaluator.CreatureThreat(KeeperWorld.FighterThreat, 2, unchecked((short)Db.StatPercent(CreatureStatId.Threat, 2)));
        Assert.Equal((short)((level2 + KeeperWorld.FighterThreat) / 2), queued.RemainingThreat);
        Assert.Equal(Keeper, world.Tile(queued.Position).OwnerPlayerId);
    }

    [Theory]
    [InlineData(true, 0, 0)]      // no mana: the front is found, the cast refused, one allowance spent
    [InlineData(false, 20000, 1)] // no way: nothing is found and nothing is spent
    public void TheFrontSpendsOneAllowanceWhenFoundWhetherOrNotTheCastPasses(bool open, int mana, int left)
    {
        WorldState world = World(open);
        world.Player(Keeper).Mana = mana;
        Fighter(world, Keeper, 15, 15);
        var state = new KeeperAIState { PlayersAttackedFlags = 1 << Enemy };
        state.InitialiseBreaches(1);
        (KeeperAttackPlanner planner, _, List<PlayerCommand> commands) = Planner(world, Attacker(usage: 1), state);

        Assert.Equal(left, planner.Manage(1));
        Assert.Empty(commands);
        Assert.False(state.CallToArmsInUse);
    }

    [Fact]
    public void AStagingPointOnTheHeartRoomsOwnPositionIsRefusedByTheCastAsDK2sWouldBe()
    {
        WorldState world = World(open: true);
        Fighter(world, Keeper, 15, 15);
        var keeper = new KeeperAI(Keeper, Attacker(usage: 0, firstStart: 2), new SimRandom(3));

        keeper.Think(Db, world);
        // Two off the heart routes, so the start room's position is kept: the heart room's, first
        // slab plus three, which lies in the inner three by three that target 4 refuses.
        Assert.Equal((OwnHeartPosition.X, OwnHeartPosition.Y), (keeper.State.CallToArmsX >> 12, keeper.State.CallToArmsY >> 12));
        for (int t = 0; t < 6; t++) Assert.Empty(keeper.Think(Db, world).OfType<CastSpellAtPlaceCommand>());
        Assert.False(keeper.State.CallToArmsInUse);
    }

    [Fact]
    public void WithUsageZeroTheCallIsCastAtTheStagingPointAndCreepsOnlyOnceTheArmyHasGathered()
    {
        WorldState world = World(open: true);
        // The start room: a Treasury on the patch's south edge, the own room furthest from the heart.
        var treasury = new TilePos(13, 20);
        for (int x = 13; x <= 14; x++) KeeperWorld.Set(world, Db, new TilePos(x, 20), KeeperWorld.TreasuryFloor, Keeper);
        KeeperWorld.Set(world, Db, new TilePos(27, 27), KeeperWorld.DirtPath);
        CreatureState straggler = Fighter(world, Keeper, 27, 27); // sixteen tiles off, and not the list's head
        Fighter(world, Keeper, 15, 15); // the newest, which the route tests use
        // The removal test must not take the call down between the steps: no enemy or own minimum.
        KeeperAIParameters p = Attacker(usage: 0, firstStart: 0) with { RemoveCallToArmsEnemyCreatures = 0, RemoveCallToArmsOwnCreatures = 0 };
        var keeper = new KeeperAI(Keeper, p, new SimRandom(3));

        keeper.Think(Db, world);
        // The staging point: two off the heart routes, so the start room's own position is kept.
        Assert.Equal((treasury.X, treasury.Y), (keeper.State.CallToArmsX >> 12, keeper.State.CallToArmsY >> 12));
        CastSpellAtPlaceCommand? first = null;
        for (int t = 0; t < 8 && first is null; t++) first = keeper.Think(Db, world).OfType<CastSpellAtPlaceCommand>().SingleOrDefault();
        Assert.NotNull(first);
        Assert.Equal((treasury.X, treasury.Y), (first!.X >> 12, first.Y >> 12));
        keeper.State.CallToArmsCastTurn = -100; // ours: the call is treated as seen
        world.Player(Keeper).CallToArms = new CallToArmsState(true, 1, (ushort)treasury.X, (ushort)treasury.Y, 0);

        // Two non-workers, one within ten tiles: 2 <= floor(5/4) fails, the call stays put.
        for (int t = 0; t < 4; t++) Assert.Empty(keeper.Think(Db, world).OfType<CastSpellAtPlaceCommand>());
        straggler.PosX = CreatureState.CentreOf(16); straggler.PosY = CreatureState.CentreOf(16);
        // Both near: 2 <= 2, the call creeps to the route's first waypoint and is cast again.
        CastSpellAtPlaceCommand? crept = null;
        for (int t = 0; t < 8 && crept is null; t++) crept = keeper.Think(Db, world).OfType<CastSpellAtPlaceCommand>().SingleOrDefault();
        Assert.NotNull(crept);
        Assert.True(crept!.Y < first.Y, $"the call did not move north: {crept.X >> 12},{crept.Y >> 12}");
    }

    [Fact]
    public void TheCommandStreamIsRepeatableForASeed()
    {
        static List<(int, ulong)> Run(ulong seed)
        {
            WorldState world = World(open: true);
            Fighter(world, Keeper, 15, 15); Fighter(world, Keeper, 16, 15);
            var keeper = new KeeperAI(Keeper, Attacker(usage: 1), new SimRandom(seed));
            var sim = new GameSimulation(Db, world, 1, CallSystems());
            var stream = new List<(int, ulong)>();
            for (int t = 0; t < 12; t++)
            {
                IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
                var hash = Hash64.Start();
                new InputFrame(commands).HashInto(ref hash);
                stream.Add((t, hash.Value));
                sim.Tick(new InputFrame(commands));
            }
            return stream;
        }

        Assert.Equal(Run(11), Run(11));
    }

    // ----------------------------------------------------------------- the observation

    [Fact]
    public void TheObservationCarriesEveryStatusTheHeartMiddleAndTheCallsRefusedTilesAcrossARoundTrip()
    {
        WorldState world = World();
        world.Player(Enemy).Status = PlayerStatus.Won;
        world.Player(Enemy).HeartRuined = true;
        var captured = new KeeperView(Db, world, Keeper);
        KeeperView restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(captured), Db);

        foreach (KeeperView view in new[] { captured, restored })
        {
            Assert.Equal(KeeperWorld.HeartAt, view.Heart);
            Assert.Equal(EnemyHeart, view.HeartOf(Enemy));
            Assert.Equal((OwnHeartPosition, new TilePos(13, 13)), (view.PositionOf(view.OwnRooms[0]), view.FirstSlabOf(view.OwnRooms[0])));
            Assert.Equal((PlayerStatus.Won, PlayerStatus.Playing), (view.StatusOf(Enemy), view.StatusOf(Keeper)));
            Assert.Equal((true, false), (view.HeartRuined(Enemy), view.HeartRuined(Keeper)));
            // Target 4 refuses the heart's inner three by three and admits its corner.
            Assert.False(view.CallToArmsPlaceAllowed(new TilePos(16, 16)));
            Assert.True(view.CallToArmsPlaceAllowed(new TilePos(17, 17)));
            Assert.True(view.EnemyLandAround(new TilePos(11, 11)));
            Assert.False(view.EnemyLandAround(new TilePos(15, 15)));
        }
    }
}
