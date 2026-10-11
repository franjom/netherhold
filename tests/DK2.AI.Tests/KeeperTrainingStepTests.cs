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

/// <summary>The training moves against the binary's branches (computer-keeper-management.md §2,
/// ADR-0317): Training against the Pit, the strict bounds, the rooms and their exclusion, the
/// predicates, the slot, the permissions, the allowance and the owned list's order.</summary>
public class KeeperTrainingStepTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private const int FightState = 199;

    /// <summary>A 3×3 Training Room and a 3×3 Pit, each with one interior tile: the only slot.</summary>
    private static readonly TilePos TrainingSlot = new(13, 18);
    private static readonly TilePos PitSlot = new(18, 13);

    /// <summary>Three quarters of the Fighter's level-1 <c>ExpForNextLevel</c> of 3000.</summary>
    private const int PitBar = 2250;

    private static WorldState World(bool training = true, bool pit = true, GameDatabase? db = null)
    {
        WorldState world = KeeperWorld.World(db ?? Db);
        if (training) Room(world, KeeperWorld.TrainingFloor, TrainingSlot.X - 1, TrainingSlot.Y - 1, 3, 3);
        if (pit) Room(world, KeeperWorld.CombatPitFloor, PitSlot.X - 1, PitSlot.Y - 1, 3, 3);
        return world;
    }

    private static void Room(WorldState world, byte floor, int x, int y, int w, int h)
    {
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                Lay(world, floor, new TilePos(x + dx, y + dy));
    }

    /// <summary>Lays one room tile through the slab chain, so a chain built tile by tile keeps its order.</summary>
    private static void Lay(WorldState world, byte floor, TilePos at)
    {
        int index = (at.Y * world.Width) + at.X;
        byte terrain = world.Tile(at).TerrainId, owner = world.Tile(at).OwnerPlayerId;
        KeeperWorld.Set(world, Db, at, floor, Keeper);
        SlabChain.Changed(world, Db, index, terrain, owner);
    }

    private static CreatureState Fighter(WorldState world, int experience = 0, int x = 15, int y = 13, int level = 1)
    {
        CreatureState creature = world.SpawnCreature(Db, KeeperWorld.FighterCreature, Keeper, new TilePos(x, y));
        creature.Level = level;
        creature.Health = CreatureLevelRules.MaxHealth(Db, Db.CreatureById(KeeperWorld.FighterCreature), level);
        creature.ExperiencePoints = experience;
        return creature;
    }

    private static KeeperAIParameters Parameters(byte skill = 1, byte speed = 4) =>
        KeeperWorld.Parameters(speed: speed) with { IACreatureSkillLevel = skill };

    /// <summary>Runs the step alone on a fresh capture, returning the allowance left and the commands.</summary>
    private static (int Left, List<PlayerCommand> Commands) Run(
        WorldState world, int allowance = 4, byte skill = 1, ulong seed = 7, byte building = 0,
        HashSet<int>? prepared = null, GameDatabase? db = null)
    {
        var view = new KeeperView(db ?? Db, world, Keeper);
        KeeperAIParameters p = Parameters(skill);
        var commands = new List<PlayerCommand>();
        var state = new KeeperAIState { CurrentRoomToBuild = building };
        var step = new KeeperTrainingStep(view, state, p, new SimRandom(seed), commands,
            new KeeperReadiness(view, p, commands), prepared ?? []);
        return (step.Run(allowance), commands);
    }

    private static int[] Lifted(IEnumerable<PlayerCommand> commands) =>
        commands.OfType<PickUpCreatureCommand>().Select(c => c.CreatureId).ToArray();

    private static TilePos[] Drops(IEnumerable<PlayerCommand> commands) =>
        commands.OfType<DropCommand>().Select(c => c.Tile).ToArray();

    [Theory]
    [InlineData(PitBar, false)]
    [InlineData(PitBar + 1, true)]
    public void ExperienceStrictlyAboveThreeQuartersOfTheThresholdGoesToThePit_AtItToTraining(int experience, bool pit)
    {
        WorldState world = World();
        CreatureState creature = Fighter(world, experience);

        (int left, List<PlayerCommand> commands) = Run(world);

        Assert.Equal([creature.Id], Lifted(commands));
        Assert.Equal([pit ? PitSlot : TrainingSlot], Drops(commands));
        Assert.Equal(3, left);
    }

    [Theory]
    [InlineData(4, 5, true)]   // at the Training Room's cap of 4 and below the skill level
    [InlineData(4, 4, false)]  // at the skill level: equality is Training
    [InlineData(3, 5, false)]  // below the cap
    public void ALevelAtTheTrainingCapAndBelowTheSkillLevelGoesToThePit(int level, byte skill, bool pit)
    {
        WorldState world = World();
        Fighter(world, level: level);

        (_, List<PlayerCommand> commands) = Run(world, skill: skill);

        Assert.Equal([pit ? PitSlot : TrainingSlot], Drops(commands));
    }

    [Theory]
    [InlineData(1125, false)]
    [InlineData(1126, true)]
    public void ThePitWantsHealthStrictlyAboveThreeQuartersAndPassesOverWithoutSpending(int health, bool moved)
    {
        WorldState world = World();
        CreatureState creature = Fighter(world, PitBar + 1);
        creature.Health = health;

        (int left, List<PlayerCommand> commands) = Run(world);

        // Three quarters of 1500 is 1125; a Pit candidate failing it does not fall to Training.
        Assert.Equal(moved ? [creature.Id] : Array.Empty<int>(), Lifted(commands));
        Assert.Equal(moved ? 3 : 4, left);
    }

    [Fact]
    public void TrainingHasNoHealthGate()
    {
        WorldState world = World();
        CreatureState creature = Fighter(world);
        creature.Health = 1;

        Assert.Equal([TrainingSlot], Drops(Run(world).Commands));
    }

    [Fact]
    public void APitCandidateWithoutAPitIsPassedOverAndTrainingStillRuns()
    {
        WorldState world = World(pit: false);
        CreatureState ready = Fighter(world, PitBar + 1, x: 14);
        CreatureState novice = Fighter(world, 0, x: 16);

        (int left, List<PlayerCommand> commands) = Run(world);

        Assert.Equal([novice.Id], Lifted(commands));
        Assert.DoesNotContain(ready.Id, Lifted(commands));
        Assert.Equal(3, left);
    }

    [Fact]
    public void WithNeitherRoomNothingIsWalkedOrSpent()
    {
        WorldState world = World(training: false, pit: false);
        Fighter(world);

        (int left, List<PlayerCommand> commands) = Run(world);

        Assert.Empty(commands);
        Assert.Equal(4, left);
    }

    [Theory]
    [InlineData(7, false, true)]     // building a Training Room: Training is off
    [InlineData(0x87, false, true)]  // expanding one: the low seven bits still name it
    [InlineData(16, true, false)]    // building a Pit: the Pit is off
    [InlineData(2, true, true)]
    public void TheTypeBeingBuiltTurnsItsBranchOff(byte building, bool training, bool pit)
    {
        WorldState world = World();
        CreatureState novice = Fighter(world, 0, x: 14);
        CreatureState ready = Fighter(world, PitBar + 1, x: 16);

        int[] lifted = Lifted(Run(world, building: building).Commands);

        Assert.Equal(training, lifted.Contains(novice.Id));
        Assert.Equal(pit, lifted.Contains(ready.Id));
    }

    [Theory]
    [InlineData(300, 301, false)]
    [InlineData(301, 301, true)]
    [InlineData(300, 300, true)]
    public void TheDroppedCounterGuardHoldsFromTurnThreeHundredAndOne(int counter, int turn, bool moved)
    {
        WorldState world = World();
        var clock = new GameSimulation(Db, world, 1, Array.Empty<ISimulationSystem>());
        while (world.CurrentTick.Value + 1 < turn) clock.Tick(new InputFrame(Array.Empty<PlayerCommand>()));
        CreatureState creature = Fighter(world);
        creature.DroppedTick = world.CurrentTick.Value - counter;

        (int left, List<PlayerCommand> commands) = Run(world);

        Assert.Equal(moved ? [creature.Id] : Array.Empty<int>(), Lifted(commands));
        Assert.Equal(moved ? 3 : 4, left);
    }

    [Fact]
    public void ACreatureWithADroppedToDoJobIsLeftAlone()
    {
        WorldState world = World();
        CreatureState creature = Fighter(world);
        creature.DroppedToDoJob = JobType.Research;

        Assert.Empty(Run(world).Commands);
    }

    [Theory]
    [InlineData(EatingRules.EatState, false)]   // eating: the needs group, 7
    [InlineData(76, false)]   // landing
    [InlineData(255, false)]  // stunned
    [InlineData(FightState, false)]
    [InlineData(1, true)]
    public void TheStateRestrictionsPassOverWithoutSpending(int stateId, bool moved)
    {
        WorldState world = World();
        CreatureState creature = Fighter(world);
        creature.Dk2StateId = stateId;

        (int left, List<PlayerCommand> commands) = Run(world);

        Assert.Equal(moved, Lifted(commands).Contains(creature.Id));
        Assert.Equal(moved ? 3 : 4, left);
    }

    [Fact]
    public void ACreatureAlreadyInItsDestinationsCategoryIsNotMovedThereButMayGoToTheOther()
    {
        WorldState world = World();
        CreatureState training = Fighter(world, 0, x: 14);
        training.Dk2StateId = Enumerable.Range(0, 281).First(id => CreatureStateTable.Get(id).Category == CreatureStateCategory.Training
            && CreatureStateTable.Get(id).Group != NeedRules.NeedGroup);
        CreatureState pitFighter = Fighter(world, 0, x: 16);
        pitFighter.Dk2StateId = Enumerable.Range(0, 281).First(id => CreatureStateTable.Get(id).Category == CreatureStateCategory.CombatPit
            && CreatureStateTable.Get(id).Group != NeedRules.NeedGroup && !KeeperThreatResponder.IsStunned(id) && id != 76);

        int[] lifted = Lifted(Run(world).Commands);

        // A trainee is not sent to Training; a Pit creature with no experience is.
        Assert.DoesNotContain(training.Id, lifted);
        Assert.Contains(pitFighter.Id, lifted);
    }

    [Fact]
    public void ACreatureInThisHandIsLeftAlone()
    {
        WorldState world = World();
        CreatureState creature = Fighter(world);
        var sim = new GameSimulation(Db, world, 1, [new HandSystem()]);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        Assert.True(creature.InHand);

        Assert.Empty(Run(world).Commands);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PitBar + 1)]
    public void ARefusedPermissionSpendsTheAllowanceAndIssuesNothing(int experience)
    {
        WorldState world = World();
        CreatureState creature = Fighter(world, experience);
        // The mana-pressure stamp refuses the pick-up predicate 0x0048E6A0 (ADR-0277).
        creature.ManaRemovalStamp = 1;

        (int left, List<PlayerCommand> commands) = Run(world, allowance: 1);

        Assert.Empty(commands);
        Assert.Equal(0, left);
    }

    [Fact]
    public void AFullHandIsAskedButNothingIsDroppedAndTheAllowanceIsSpent()
    {
        GameDatabase db = KeeperWorld.Database(handCapacity: 0);
        WorldState world = World(db: db);
        CreatureState creature = world.SpawnCreature(db, KeeperWorld.FighterCreature, Keeper, new TilePos(15, 13));

        (int left, List<PlayerCommand> commands) = Run(world, allowance: 1, db: db);

        // 0x004BC500 refuses a full hand and 0x004BCAE0 then finds nothing to drop.
        Assert.Equal([creature.Id], Lifted(commands));
        Assert.Empty(Drops(commands));
        Assert.Equal(0, left);
    }

    [Fact]
    public void ARoomWithNoFloorSlotSpendsTheAllowanceAndDrawsNothing()
    {
        WorldState world = World(training: false, pit: false);
        Room(world, KeeperWorld.TrainingFloor, 12, 17, 2, 2);
        Fighter(world);

        (int left, List<PlayerCommand> commands) = Run(world, allowance: 1);

        // A 2x2 room has no interior cell for 0x004E8210 to count: the attempt is spent, nothing moves.
        Assert.Empty(commands);
        Assert.Equal(0, left);
    }

    [Fact]
    public void TheSlotIsAnInteriorCellDrawnUniformlyAndNeverTheRim()
    {
        var drops = new HashSet<TilePos>();
        for (ulong seed = 1; seed <= 16; seed++)
        {
            WorldState world = World(training: false, pit: false);
            Room(world, KeeperWorld.TrainingFloor, 11, 17, 4, 3);
            Fighter(world);
            drops.UnionWith(Drops(Run(world, seed: seed).Commands));
        }
        // A 4x3 room's interior is (12,18) and (13,18); both are drawn and nothing else is.
        Assert.Equal(new HashSet<TilePos> { new(12, 18), new(13, 18) }, drops);
    }

    [Fact]
    public void TheSlotsAreCountedInSlabChainOrder()
    {
        WorldState world = World(training: false, pit: false);
        // Laid from the bottom-right corner, the chain reaches the right-hand interior cell first.
        for (int y = 19; y >= 17; y--)
            for (int x = 14; x >= 11; x--)
                Lay(world, KeeperWorld.TrainingFloor, new TilePos(x, y));

        KeeperRoomView room = Assert.Single(new KeeperView(Db, world, Keeper).OwnRooms,
            r => r.Definition.RoomId == (byte)RoomTypeId.TrainingRoom);

        Assert.Equal([(18 * world.Width) + 13, (18 * world.Width) + 12], room.FloorSlots);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheOwnedListIsWalkedNewestFirstWhileAllowanceLasts(int allowance)
    {
        WorldState world = World();
        CreatureState a = Fighter(world, x: 14);
        CreatureState b = Fighter(world, x: 15);
        CreatureState c = Fighter(world, x: 16);

        (int left, List<PlayerCommand> commands) = Run(world, allowance: allowance);

        Assert.Equal(new[] { c.Id, b.Id, a.Id }.Take(allowance), Lifted(commands));
        Assert.Equal(0, left);
        // Each pick-up is followed by its own drop, as DK2 drops before the next creature.
        Assert.All(Enumerable.Range(0, allowance), i => Assert.IsType<DropCommand>(commands[(2 * i) + 1]));
    }

    [Fact]
    public void ACreatureTheRetreatPreparedIsLandingOrStillPrepared()
    {
        WorldState world = World();
        CreatureState creature = Fighter(world);

        // Dropped by the retreat it is landing (76) and passed over; refused by the hand it sits in
        // 75, which no predicate excludes.
        Assert.Empty(Run(world, prepared: [creature.Id]).Commands);
        GameDatabase full = KeeperWorld.Database(handCapacity: 0);
        WorldState refused = World(db: full);
        CreatureState held = refused.SpawnCreature(full, KeeperWorld.FighterCreature, Keeper, new TilePos(15, 13));
        Assert.Equal([held.Id], Lifted(Run(refused, prepared: [held.Id], db: full).Commands));
    }

    [Fact]
    public void AWorkerIsSkipped()
    {
        WorldState world = World();
        world.SpawnCreature(Db, KeeperWorld.WorkerCreature, Keeper, new TilePos(15, 13));

        (int left, List<PlayerCommand> commands) = Run(world);

        Assert.Empty(commands);
        Assert.Equal(4, left);
    }

    /// <summary>Runs the keeper and the simulation together until a training move has been issued
    /// and <paramref name="settled"/> holds, returning the keeper's command log.</summary>
    private static List<(int Tick, PlayerCommand Command)> PlayUntil(WorldState world, Func<bool> settled, byte skill = 1)
    {
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 50000;
        KeeperWorld.FurnishRooms(Db, world);
        var sim = new GameSimulation(Db, world, 1,
        [
            new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem(), new CreatureJobSystem(),
            new CombatPitSystem(), new CreatureHealthSystem(),
        ]);
        var keeper = new KeeperAI(Keeper, Parameters(skill, speed: 1), new SimRandom(3));
        var log = new List<(int Tick, PlayerCommand Command)>();
        for (int tick = 0; tick < 600; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            foreach (PlayerCommand command in commands) log.Add((tick, command));
            sim.Tick(new InputFrame(commands));
            if (log.Any(e => e.Command is DropCommand) && settled()) break;
        }
        return log;
    }

    [Fact]
    public void ThinkSendsANoviceToTheTrainingRoomUnaided_WhereItTrains()
    {
        WorldState world = World(pit: false);
        CreatureState novice = Fighter(world);

        List<(int Tick, PlayerCommand Command)> log = PlayUntil(world, () => novice.CurrentJob == JobType.Train
            && novice.ExperiencePoints >= 40);

        // Lifted once and put down on the room's one interior cell by the real hand; the drop on a
        // job room starts the Train job and remembers it (0x004E8DA0), and it earns experience.
        Assert.Single(log, e => e.Command is PickUpCreatureCommand p && p.CreatureId == novice.Id);
        Assert.Equal(TrainingSlot, ((DropCommand)Assert.Single(log, e => e.Command is DropCommand).Command).Tile);
        Assert.False(novice.InHand);
        Assert.Equal(JobType.Train, novice.DroppedToDoJob);
        int workingState = CreatureStateTable.Get(novice.Dk2StateId).IsUtility ? novice.UtilityNewState : novice.Dk2StateId;
        Assert.Equal(CreatureStateCategory.Training, CreatureStateTable.Get(workingState).Category);
        Assert.True(novice.ExperiencePoints >= 40);
    }

    [Fact]
    public void ThinkSendsACreatureNearItsLevelToTheCombatPitUnaided_WhereItFights()
    {
        WorldState world = World(training: false);
        CreatureState veteran = Fighter(world, PitBar + 1);

        // Skill 5 keeps the retreat's fifth branch, a Pit fighter at the skill level, from lifting it
        // straight back out (computer-keeper-threat.md §4); at skill 1 the two steps alternate.
        List<(int Tick, PlayerCommand Command)> log = PlayUntil(world, () => veteran.Dk2StateId == CombatPitRules.WaitingState, skill: 5);

        Assert.Equal(PitSlot, ((DropCommand)Assert.Single(log, e => e.Command is DropCommand).Command).Tile);
        Assert.True(veteran.CombatPitFighter);
        Assert.Equal(CombatPitRules.WaitingState, veteran.Dk2StateId);
        // In the pit its category is the Pit's, so the step does not send it again.
        Assert.Single(log, e => e.Command is PickUpCreatureCommand);
    }

    [Fact]
    public void TheCommandStreamIsRepeatableForOneSeedAndTheObservationRoundTrips()
    {
        WorldState world = World();
        CreatureState veteran = Fighter(world, PitBar + 1, x: 14);
        veteran.DroppedToDoJob = JobType.None;
        Fighter(world, 0, x: 16).DroppedToDoJob = JobType.Train;
        Fighter(world, 10, x: 17);

        KeeperView view = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(new KeeperView(Db, world, Keeper)), Db);
        KeeperView direct = new(Db, world, Keeper);
        Assert.Equal(direct.Creatures.Select(c => (c.Experience, c.DroppedToDoJob)), view.Creatures.Select(c => (c.Experience, c.DroppedToDoJob)));
        Assert.Equal(direct.OwnRooms.Select(r => r.FloorSlots.ToArray()), view.OwnRooms.Select(r => r.FloorSlots.ToArray()));

        var first = new List<string>();
        var second = new List<string>();
        var one = new KeeperAI(Keeper, Parameters(speed: 4), new SimRandom(11));
        var two = new KeeperAI(Keeper, Parameters(speed: 4), new SimRandom(11));
        for (int tick = 0; tick < 8; tick++)
        {
            first.AddRange(one.Think(view).Select(c => c.ToString()!));
            second.AddRange(two.Think(view).Select(c => c.ToString()!));
        }
        Assert.Contains(first, c => c.Contains(nameof(DropCommand)));
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(3, true)]   // three non-workers are not more than IAMinimumCreatures 3: not ready
    [InlineData(4, false)]  // four at the skill level of 1: ready, so step 9 never moves anyone
    public void OnlyANotReadyArmyIsMoved(int fighters, bool moved)
    {
        WorldState world = World();
        for (int i = 0; i < fighters; i++) Fighter(world, x: 13 + i);
        var keeper = new KeeperAI(Keeper, Parameters(skill: 1, speed: 4), new SimRandom(5));

        var commands = new List<PlayerCommand>();
        for (int tick = 0; tick < 16; tick++) commands.AddRange(keeper.Think(Db, world));

        Assert.Equal(moved, commands.Any(c => c is PickUpCreatureCommand));
    }

    /// <summary>A 4x3 room's two interior cells, in chain order for a room laid row by row.</summary>
    private static TilePos[] TwoSlotRoom(WorldState world, byte floor, int x, int y)
    {
        Room(world, floor, x, y, 4, 3);
        return [new TilePos(x + 1, y + 1), new TilePos(x + 2, y + 1)];
    }

    /// <summary>A seed whose stream picks a different second-room slot with one extra slot draw than
    /// without it, after <paramref name="roomDraws"/>; and the two picks, by the step's own draws.</summary>
    private static (ulong Seed, int Without, int With) SeedWhereAnExtraDrawShows(int[] roomDraws, int slots)
    {
        for (ulong seed = 1; seed < 256; seed++)
        {
            var without = new SimRandom(seed);
            var with = new SimRandom(seed);
            foreach (int count in roomDraws) { without.NextInt(count); with.NextInt(count); }
            with.NextInt(slots);
            int a = without.NextInt(slots), b = with.NextInt(slots);
            if (a != b) return (seed, a, b);
        }
        throw new InvalidOperationException("no discriminating seed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARefusedPermissionDrawsASlotInThePitBranchOnly(bool pit)
    {
        WorldState world = World(training: false, pit: false);
        TilePos[] trainingSlots = TwoSlotRoom(world, KeeperWorld.TrainingFloor, 11, 17);
        TilePos[] pitSlots = TwoSlotRoom(world, KeeperWorld.CombatPitFloor, 18, 9);
        int experience = pit ? PitBar + 1 : 0;
        CreatureState permitted = Fighter(world, experience, x: 14);
        CreatureState refused = Fighter(world, experience, x: 16);
        refused.ManaRemovalStamp = 1;
        (ulong seed, int without, int with) = SeedWhereAnExtraDrawShows([1, 1], 2);

        (int left, List<PlayerCommand> commands) = Run(world, seed: seed);

        // The refused creature, walked first, spends an allowance either way; only the Pit asks for
        // its slot before the permission (0x004FF9A8 before 0x004FF9BF), so only there does it draw.
        Assert.Equal([permitted.Id], Lifted(commands));
        TilePos[] slots = pit ? pitSlots : trainingSlots;
        Assert.Equal([slots[pit ? with : without]], Drops(commands));
        Assert.Equal(2, left);
    }

    [Fact]
    public void TheRoomOfTheTypeBeingBuiltIsStillDrawn()
    {
        WorldState world = World(training: false, pit: false);
        TwoSlotRoom(world, KeeperWorld.TrainingFloor, 11, 17);
        TwoSlotRoom(world, KeeperWorld.TrainingFloor, 21, 17);
        TilePos[] pitSlots = TwoSlotRoom(world, KeeperWorld.CombatPitFloor, 18, 9);
        Fighter(world, PitBar + 1);
        // Two Training Rooms, then the one Pit, then the slot; a seed where skipping the Training
        // draw would pick the other slot.
        int Slot(ulong seed, bool drawTraining)
        {
            var replay = new SimRandom(seed);
            if (drawTraining) replay.NextInt(2);
            replay.NextInt(1);
            return replay.NextInt(2);
        }
        ulong seed = 1;
        while (Slot(seed, true) == Slot(seed, false)) seed++;

        // 0x004C6460 runs for both types before the +0x98 tests, so the Training draw is made
        // though building a Training Room turns that branch off.
        Assert.Equal([pitSlots[Slot(seed, true)]], Drops(Run(world, seed: seed, building: (byte)RoomTypeId.TrainingRoom).Commands));
    }

    [Theory]
    [InlineData(2925, false)]  // three quarters of 3000 x 130% at level 2: Training
    [InlineData(2926, true)]   // above it: the Pit, though far above the level-1 bar of 2250
    public void TheExperienceBarIsTheCurrentLevelsScaledThreshold(int experience, bool pit)
    {
        WorldState world = World();
        Fighter(world, experience, level: 2);

        Assert.Equal([pit ? PitSlot : TrainingSlot], Drops(Run(world).Commands));
    }

    [Theory]
    [InlineData(1406, false)]  // three quarters of 1500 x 125% at level 2 is 1406
    [InlineData(1407, true)]
    public void ThePitsHealthGateIsTheLevelScaledMaximum(int health, bool moved)
    {
        WorldState world = World();
        CreatureState creature = Fighter(world, 2926, level: 2);
        creature.Health = health;

        Assert.Equal(moved ? [PitSlot] : Array.Empty<TilePos>(), Drops(Run(world).Commands));
    }

    [Theory]
    [InlineData(1800, true)]   // carried to 2100 of 2625 at level 4: above the gate of 1968
    [InlineData(1680, false)]  // carried to 1960: below it
    public void ACreatureReadinessLevelsIsJudgedAtItsNewLevelWithItsHealthShare(int health, bool pit)
    {
        WorldState world = World();
        Fighter(world, level: 6, x: 13);
        CreatureState levelled = Fighter(world, level: 3, x: 17);
        levelled.Health = health;
        var view = new KeeperView(Db, world, Keeper);
        KeeperAIParameters p = Parameters(skill: 6) with { IAMinimumCreatures = 1 };
        var commands = new List<PlayerCommand>();
        var readiness = new KeeperReadiness(view, p, commands);

        // Two non-workers, more than one; one below level 6, at most 6 and fewer than two: it is
        // levelled to 4 (0x0048B120) and the army is still not ready.
        Assert.False(readiness.ArmyReady());
        Assert.Equal(4, readiness.Granted[levelled.Id]);
        new KeeperTrainingStep(view, new KeeperAIState(), p, new SimRandom(7), commands, readiness, []).Run(4);

        // At level 4, at the Training cap and below the skill level, it is a Pit candidate; at
        // its old level 3 it would be sent to Training.
        int at = commands.FindIndex(c => c is PickUpCreatureCommand pick && pick.CreatureId == levelled.Id);
        Assert.Equal(pit, at >= 0);
        if (pit) Assert.Equal(PitSlot, ((DropCommand)commands[at + 1]).Tile);
    }

    [Fact]
    public void TheDestinationCategoryIsReadThroughAWalk()
    {
        WorldState world = World();
        CreatureState walking = Fighter(world, 0, x: 14);
        int training = Enumerable.Range(0, 281).First(id => CreatureStateTable.Get(id).Category == CreatureStateCategory.Training
            && CreatureStateTable.Get(id).Group != NeedRules.NeedGroup);
        int walk = Enumerable.Range(0, 281).First(id => CreatureStateTable.Get(id).IsUtility
            && CreatureStateTable.Get(id).Group != NeedRules.NeedGroup && !KeeperThreatResponder.IsStunned(id));
        walking.Dk2StateId = walk;
        walking.UtilityNewState = training;

        // Walking into Training, its category through the walk is Training's (0x00478050): it is
        // not sent there again, though the walk's own state is neither Training nor the needs group.
        Assert.DoesNotContain(walking.Id, Lifted(Run(world).Commands));
        walking.UtilityNewState = CreatureStateMachine.Idle;
        Assert.Contains(walking.Id, Lifted(Run(world).Commands));
    }
}
