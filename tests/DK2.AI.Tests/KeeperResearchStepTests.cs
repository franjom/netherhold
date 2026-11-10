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

/// <summary>Step 12 against <c>0x00500C20</c>'s research block, the researcher move <c>0x004FA8D0</c>
/// and job placement <c>0x00503690</c> (computer-keeper-management.md §11): the roll and its gates,
/// the candidate, the cap and the guards, the hand's drops and the pick-up scan.</summary>
public class KeeperResearchStepTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private const byte Research = 2; // availability 2: a spell awaiting research

    private static WorldState World(bool portal = true, bool library = false, bool training = false, bool heroPortal = false)
    {
        WorldState world = KeeperWorld.World(Db);
        if (portal) Room(world, KeeperWorld.PortalFloor, 4, 4);
        if (heroPortal) Room(world, KeeperWorld.HeroPortalFloor, 4, 22);
        if (library) Room(world, KeeperWorld.LibraryFloor, 22, 4);
        if (training) Room(world, KeeperWorld.TrainingFloor, 22, 22);
        return world;
    }

    private static void Room(WorldState world, byte floor, int x, int y)
    {
        for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
            {
                var at = new TilePos(x + dx, y + dy);
                int index = (at.Y * world.Width) + at.X;
                byte terrain = world.Tile(at).TerrainId, owner = world.Tile(at).OwnerPlayerId;
                KeeperWorld.Set(world, Db, at, floor, Keeper);
                SlabChain.Changed(world, Db, index, terrain, owner);
            }
    }

    private static CreatureState Spawn(WorldState world, byte type, int x = 14, int y = 15) =>
        world.SpawnCreature(Db, type, Keeper, new TilePos(x, y));

    private static KeeperAIParameters Parameters(byte probability = 0) =>
        KeeperWorld.Parameters() with { ProbabilityOfMovingCreatureForResearch = probability };

    /// <summary>The researcher move alone on a fresh capture.</summary>
    private static List<PlayerCommand> Move(WorldState world, GameDatabase? db = null)
    {
        var view = new KeeperView(db ?? Db, world, Keeper);
        var commands = new List<PlayerCommand>();
        new KeeperResearcherMove(view, new SimRandom(7), commands, []).Run();
        return commands;
    }

    /// <summary>Step 12 alone on a fresh capture.</summary>
    private static (int Left, List<PlayerCommand> Commands) Step(
        WorldState world, int allowance = 4, byte probability = 0, ulong seed = 7, SimRandom? rng = null,
        KeeperAIState? state = null, GameDatabase? db = null)
    {
        var view = new KeeperView(db ?? Db, world, Keeper);
        KeeperAIParameters p = Parameters(probability);
        var commands = new List<PlayerCommand>();
        var step = new KeeperResearchStep(view, state ?? new KeeperAIState(), p, rng ?? new SimRandom(seed), commands,
            new KeeperReadiness(view, p, commands), []);
        return (step.Run(allowance), commands);
    }

    private static TilePos PositionOf(WorldState world, RoomTypeId type)
    {
        var view = new KeeperView(Db, world, Keeper);
        return view.PositionOf(view.OwnRooms.Single(r => r.Definition.RoomId == (byte)type));
    }

    private static ulong SeedWhere(Func<SimRandom, bool> wanted)
    {
        for (ulong seed = 1; ; seed++)
            if (wanted(new SimRandom(seed))) return seed;
    }

    private static int NextAfter(ulong seed, params int[] draws)
    {
        var rng = new SimRandom(seed);
        foreach (int range in draws) rng.NextInt(range);
        return rng.NextInt(1 << 30);
    }

    private static int[] Lifted(IEnumerable<PlayerCommand> commands) =>
        commands.OfType<TakeCreatureCommand>().Select(c => c.CreatureId).ToArray();

    private static TilePos[] Drops(IEnumerable<PlayerCommand> commands) =>
        commands.OfType<DropCommand>().Select(c => c.Tile).ToArray();

    private static void Hold(WorldState world, CreatureState creature) =>
        new GameSimulation(Db, world, 1, [new HandSystem()]).Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, creature.Id)));

    // ----------------------------------------------------------------- 0x004FA8D0

    [Fact]
    public void TheLeastDescriptorByteGoesToAPortal_ATieToTheLastListed()
    {
        WorldState world = World();
        Spawn(world, KeeperWorld.FighterCreature);
        Spawn(world, KeeperWorld.SleeperCreature, 13);
        Spawn(world, KeeperWorld.SleeperCreature, 16);
        var view = new KeeperView(Db, world, Keeper);
        int last = view.Creatures.Where(c => c.CreatureTypeId == KeeperWorld.SleeperCreature)
            .OrderBy(c => c.OwnershipRank).Last().Id;

        List<PlayerCommand> commands = Move(world);

        Assert.Equal([last], Lifted(commands));
        Assert.Equal([PositionOf(world, RoomTypeId.Portal)], Drops(commands));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void FewerThanTwoResearchersAreNeeded(int researchers, bool moved)
    {
        WorldState world = World();
        for (int i = 0; i < researchers; i++) Spawn(world, KeeperWorld.MoodyCreature, 13 + i);
        CreatureState fighter = Spawn(world, KeeperWorld.FighterCreature, 16);

        Assert.Equal(moved ? [fighter.Id] : Array.Empty<int>(), Lifted(Move(world)));
    }

    [Fact]
    public void AWorkerIsNeverTheCandidate()
    {
        WorldState world = World();
        Spawn(world, KeeperWorld.WorkerCreature);

        Assert.Empty(Move(world));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void OnlyAKeeperAtItsPortalCapMakesRoom(int fromPortal, bool moved)
    {
        // One portal supports two; Fighters that came through it count, the Sleeper (the least byte)
        // is the candidate, and nobody researches.
        GameDatabase db = KeeperWorld.Database(firstPortalCreatures: 2);
        WorldState world = World();
        CreatureState candidate = Spawn(world, KeeperWorld.SleeperCreature);
        for (int i = 0; i < fromPortal; i++) Spawn(world, KeeperWorld.FighterCreature, 13 + i).CameThroughPortal = true;

        Assert.Equal(moved ? [candidate.Id] : Array.Empty<int>(), Lifted(Move(world, db)));
    }

    [Fact]
    public void AHeroPortalStandsInForAMissingPortal()
    {
        WorldState world = World(portal: false, heroPortal: true);
        Spawn(world, KeeperWorld.FighterCreature);

        Assert.Equal([PositionOf(world, RoomTypeId.HeroPortal)], Drops(Move(world)));
        Assert.Empty(Move(World(portal: false)));
    }

    [Theory]
    [InlineData(197)]
    [InlineData(196)]
    [InlineData(DropLandingRules.LandingState)]
    public void ALeavingOrLandingCandidateIsNotMoved(int state)
    {
        WorldState world = World();
        Spawn(world, KeeperWorld.FighterCreature).Dk2StateId = state;

        Assert.Empty(Move(world));
    }

    [Fact]
    public void TheHandAndThePortalMarkSurviveTheSnapshot()
    {
        WorldState world = World();
        CreatureState held = Spawn(world, KeeperWorld.MoodyCreature);
        Spawn(world, KeeperWorld.FighterCreature, 16).CameThroughPortal = true;
        Hold(world, held);
        var view = new KeeperView(Db, world, Keeper);

        KeeperView after = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(view), Db);

        Assert.Equal([HandItem.Of(held)], after.HandItems);
        Assert.Equal(view.Creatures.Select(c => c.CameThroughPortal), after.Creatures.Select(c => c.CameThroughPortal));
        Assert.Contains(after.Creatures, c => c.CameThroughPortal);
    }

    // ----------------------------------------------------------------- step 12

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheRollAtLeastTheProbabilityOpensTheStep(bool open)
    {
        WorldState world = World(portal: false);
        ulong seed = SeedWhere(r => r.NextInt(3) >= 1 == open);
        var rng = new SimRandom(seed);

        Step(world, probability: 1, rng: rng);

        // Open, job placement draws its pick-up roll; shut, only the research roll is drawn.
        Assert.Equal(open ? NextAfter(seed, 3, 100) : NextAfter(seed, 3), rng.NextInt(1 << 30));
    }

    [Fact]
    public void WithoutAllowanceOnlyTheRollIsDrawn()
    {
        var rng = new SimRandom(7);

        Assert.Equal(0, Step(World(), allowance: 0, rng: rng).Left);
        Assert.Equal(NextAfter(7, 3), rng.NextInt(1 << 30));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASpellAwaitingResearchMakesRoomForAResearcherOnlyWithALibrary(bool library)
    {
        WorldState world = World(library: library);
        world.SetAvailability(Keeper, AvailabilityKind.Spell, KeeperSpellRules.Thunderbolt, Research);
        CreatureState fighter = Spawn(world, KeeperWorld.FighterCreature);
        ulong seed = SeedWhere(r => { r.NextInt(3); r.NextInt(1); r.NextInt(1); return r.NextInt(100) >= KeeperResearchStep.PickUpPercent; });

        (int left, List<PlayerCommand> commands) = Step(world, seed: seed);

        Assert.Equal(library ? [fighter.Id] : Array.Empty<int>(), Lifted(commands));
        Assert.Equal(library ? [PositionOf(world, RoomTypeId.Portal)] : Array.Empty<TilePos>(), Drops(commands));
        Assert.Equal(4, left); // the move spends nothing
    }

    [Fact]
    public void AnObjectOnTopOfTheHandSpendsTheWholeAllowance()
    {
        WorldState world = World(portal: false, library: true);
        world.Player(Keeper).Hand.Add(new HandItem(HandItemKind.Object, 999));

        (int left, List<PlayerCommand> commands) = Step(world);

        Assert.Equal((0, 0), (left, commands.Count));
    }

    [Fact]
    public void AHeldCreatureIsDroppedOnlyAtARoomItsJobsName()
    {
        WorldState world = World(); // a Portal, which no job names
        Hold(world, Spawn(world, KeeperWorld.MoodyCreature));

        (int left, List<PlayerCommand> commands) = Step(world);

        Assert.Equal((0, 0), (left, commands.Count));
    }

    [Fact]
    public void ResearchWithNothingToResearchAdmitsNobody()
    {
        // A Library exists but no spell awaits research, and Moody's Train has no room.
        WorldState world = World(portal: false, library: true);
        Spawn(world, KeeperWorld.MoodyCreature);
        ulong seed = SeedWhere(r => { r.NextInt(3); return r.NextInt(100) < KeeperResearchStep.PickUpPercent; });

        Assert.Empty(Lifted(Step(world, seed: seed).Commands));
    }

    [Fact]
    public void AQueuedAttackResponseHoldsJobPlacementBack()
    {
        var state = new KeeperAIState();
        Assert.True(state.Responses.TryReceive(() => new KeeperThreatResponse(10, KeeperWorld.HeartAt)));
        var rng = new SimRandom(7);

        Step(World(portal: false), state: state, rng: rng);

        Assert.Equal(NextAfter(7, 3), rng.NextInt(1 << 30));
    }

    [Fact]
    public void TheHandsLastCreatureIsDroppedAtItsJobsRoom()
    {
        WorldState world = World(portal: false, library: true);
        CreatureState moody = Spawn(world, KeeperWorld.MoodyCreature);
        Hold(world, moody);

        (int left, List<PlayerCommand> commands) = Step(world, seed: SeedWhere(r =>
        {
            r.NextInt(3); r.NextInt(1);
            return r.NextInt(100) >= KeeperResearchStep.PickUpPercent;
        }));

        // Research is its first job and a Library exists; no placement test is asked for a drop.
        Assert.Equal([PositionOf(world, RoomTypeId.Library)], Drops(commands));
        Assert.Empty(Lifted(commands));
        Assert.Equal(3, left);
    }

    [Fact]
    public void AHeldCreatureWithNoJobRoomSpendsTheWholeAllowanceRetrying()
    {
        WorldState world = World(portal: false);
        Hold(world, Spawn(world, KeeperWorld.FighterCreature));
        var rng = new SimRandom(7);

        (int left, List<PlayerCommand> commands) = Step(world, rng: rng);

        Assert.Equal((0, 0), (left, commands.Count));
        // The pick-up roll is drawn though the hand's retries left no allowance.
        Assert.Equal(NextAfter(7, 3, 100), rng.NextInt(1 << 30));
    }

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(9, 10, true)]
    [InlineData(10, 11, false)]
    [InlineData(10, 50, false)]
    public void ThePickUpScanLiftsAnIdleCreatureItsJobAdmits_WithoutADrop(int low, int high, bool lifted)
    {
        WorldState world = World(portal: false, training: true);
        CreatureState moody = Spawn(world, KeeperWorld.MoodyCreature);
        // Research has no Library; Train's room is drawn and its test asks readiness, which a
        // lone creature fails. The scan runs on a roll below ten only.
        ulong seed = SeedWhere(r => { r.NextInt(3); int roll = r.NextInt(100); return roll >= low && roll < high; });

        (int left, List<PlayerCommand> commands) = Step(world, seed: seed);

        Assert.Equal(lifted ? [moody.Id] : Array.Empty<int>(), Lifted(commands));
        Assert.Empty(Drops(commands));
        Assert.Equal(lifted ? 3 : 4, left);
    }

    [Fact]
    public void ThePickUpScanPassesOverADroppedToDoJob()
    {
        ulong seed = SeedWhere(r => { r.NextInt(3); return r.NextInt(100) < KeeperResearchStep.PickUpPercent; });
        WorldState world = World(portal: false, training: true);
        Spawn(world, KeeperWorld.MoodyCreature).DroppedToDoJob = JobType.Train;

        Assert.Empty(Step(world, seed: seed).Commands);
    }

    private static ulong ScanSeed() =>
        SeedWhere(r => { r.NextInt(3); return r.NextInt(100) < KeeperResearchStep.PickUpPercent; });

    /// <summary>The scan's group-7 test has no state of its own to refuse: no needs-group state
    /// carries activity code 5, so the activity test, asked first, always refuses them.</summary>
    [Fact]
    public void ThePickUpScanTakesOnlyIdleCreatures()
    {
        WorldState world = World(portal: false, training: true);
        CreatureState moody = Spawn(world, KeeperWorld.MoodyCreature);
        // A Training state, activity code 6, outside the needs group.
        moody.Dk2StateId = Enumerable.Range(0, 281).First(id => CreatureStateTable.Get(id).Category == CreatureStateCategory.Training
            && CreatureStateTable.Get(id).Group != NeedRules.NeedGroup);

        Assert.Empty(Lifted(Step(world, seed: ScanSeed()).Commands));
    }

    [Fact]
    public void ResearchAdmitsAnIdleCreatureWhileASpellAwaitsResearch()
    {
        WorldState world = World(portal: false, library: true);
        world.SetAvailability(Keeper, AvailabilityKind.Spell, KeeperSpellRules.Thunderbolt, Research);
        CreatureState moody = Spawn(world, KeeperWorld.MoodyCreature);
        // The roll, the gate's Library draw, then the scan's roll, its Library and the test's own.
        ulong seed = SeedWhere(r => { r.NextInt(3); r.NextInt(1); return r.NextInt(100) < KeeperResearchStep.PickUpPercent; });

        Assert.Equal([moody.Id], Lifted(Step(world, seed: seed).Commands));
    }

    [Fact]
    public void AFullHandHoldsTheScanBack()
    {
        // The hand loop ends only on an empty hand or no allowance, so a hand with no room at all is
        // the case the scan's own capacity test decides: nothing is lifted and nothing spent.
        GameDatabase none = KeeperWorld.Database(handCapacity: 0);
        WorldState world = World(portal: false, training: true);
        Spawn(world, KeeperWorld.MoodyCreature, 13);

        (int left, List<PlayerCommand> commands) = Step(world, allowance: 4, seed: ScanSeed(), db: none);

        Assert.Equal((4, 0), (left, commands.Count));
    }

    [Fact]
    public void TheScanStopsOnceTheHandIsFull()
    {
        GameDatabase two = KeeperWorld.Database(handCapacity: 1);
        WorldState world = World(portal: false, training: true);
        Spawn(world, KeeperWorld.MoodyCreature, 13);
        Spawn(world, KeeperWorld.MoodyCreature, 16);

        (int left, List<PlayerCommand> commands) = Step(world, seed: ScanSeed(), db: two);

        // The first lift fills the one-item hand; the second Moody is never tried.
        Assert.Single(Lifted(commands));
        Assert.Equal(3, left);
    }

    [Fact]
    public void ThePlacementDropsTheLastItemNotTheFirst()
    {
        WorldState world = World(portal: false, library: true);
        Hold(world, Spawn(world, KeeperWorld.MoodyCreature));
        world.Player(Keeper).Hand.Add(new HandItem(HandItemKind.Object, 999));

        // An object on top: every pass retries it, and the Moody beneath is never dropped.
        Assert.Empty(Drops(Step(world).Commands));
    }

    public static TheoryData<string> MoveGuards => ["stunned", "unpermitted", "dropped"];

    [Theory]
    [MemberData(nameof(MoveGuards))]
    public void TheResearcherMoveKeepsTheGuards(string guard)
    {
        WorldState world = World();
        CreatureState fighter = Spawn(world, KeeperWorld.FighterCreature);
        switch (guard)
        {
            case "stunned": fighter.Dk2StateId = BoulderRules.StunnedState; break;
            case "unpermitted": fighter.ManaRemovalStamp = 1; break;
            case "dropped":
                var clock = new GameSimulation(Db, world, 1, Array.Empty<ISimulationSystem>());
                while (world.CurrentTick.Value + 1 < 301) clock.Tick(new InputFrame(Array.Empty<PlayerCommand>()));
                fighter.DroppedTick = world.CurrentTick.Value - 300;
                break;
        }

        Assert.Empty(Move(world));
    }
}
