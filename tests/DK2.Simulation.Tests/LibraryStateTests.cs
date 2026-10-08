// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Library's states 53–59 (library-states.md, ADR-0397).</summary>
public class LibraryStateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Vampire = Dk2TerrainFixture.VampireCreatureId;

    /// <summary>The fixture with a wall-capable reinforced wall, a one-entry research list too dear to
    /// finish, and the Vampire given art for its read (slot 14) and its watch (slot 5).</summary>
    private static GameDatabase Db()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        TerrainDef wall = basis.TerrainById(Dk2TerrainFixture.ReinforcedWall);
        wall = wall with { Flags = wall.Flags | TerrainFlags.AllowRoomWalls };
        var clip = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 20, FramesPerSecond = 20 };
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        foreach (int slot in new[] { 1, 5, 14, 20 }) slots[slot] = clip;
        var level = new GlobalVariables();
        level.Research.Add(new ResearchEntry(ResearchTreeRules.KindSpell, Dk2TerrainFixture.CreateImpSpellId, 1_000_000));
        return new GameDatabase(basis.Terrain.Values.Select(t => t.TerrainId == wall.TerrainId ? wall : t), basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Vampire ? c with { ArtSlots = slots } : c),
            basis.Variables, basis.KeeperSpells.Values, objects: basis.Objects.Values, shots: basis.Shots.Values).ForLevel(level);
    }

    /// <summary>A square Library of the keeper's from (1,1) under a reinforced north wall, with
    /// something to research.</summary>
    private static WorldState World(GameDatabase db, int size)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int i = 1; i <= size; i++) world.Tile(i, 0).TerrainId = Dk2TerrainFixture.ReinforcedWall;
        for (int y = 1; y <= size; y++)
        for (int x = 1; x <= size; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Library), Keeper);
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, Dk2TerrainFixture.CreateImpSpellId, 3));
        world.Player(Keeper).ResearchTarget = 1;
        return world;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, params ISimulationSystem[] extra) =>
        new(db, world, seed: 42, [new AutoFurnitureSystem(), new CreatureMovementSystem(), new CreatureJobSystem(), .. extra]);

    private static CreatureState Researcher(GameDatabase db, WorldState world, TilePos at, int state = ResearchStep.ChoosingState)
    {
        CreatureState it = world.SpawnCreature(db, Vampire, Keeper, at);
        it.CurrentJob = JobType.Research;
        StatePlacement.As(it, CreatureActivity.AtWork);
        StatePlacement.Place(it, state);
        it.HeldPlace = new RoomPlace((at.Y * world.Width) + at.X, RoomCapacityRules.Units);   // the enter hook's claim
        return it;
    }

    private static RoomInstance Library(GameDatabase db, WorldState world) =>
        RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == RoomTypeId.Library);

    private static HashSet<int> Watch(GameSimulation sim, CreatureState creature, int ticks)
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < ticks; i++)
        {
            sim.Tick();
            seen.Add(CreatureStateTable.Get(creature.Dk2StateId).IsUtility ? creature.UtilityNewState : creature.Dk2StateId);
        }

        return seen;
    }

    [Fact]
    public void AResearcherWalksToAFreeBookOrBookcaseFacesItReadsLongAndChoosesAgain()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        Assert.Contains(world.Objects, o => o.ObjectTypeId == ResearchStep.BookCaseObjectId);
        CreatureState it = Researcher(db, world, new TilePos(3, 3));

        HashSet<int> seen = Watch(sim, it, 60 * SimulationTiming.TicksPerSecond);

        Assert.Contains(ResearchStep.AtTheShelfState, seen);
        Assert.Contains(ResearchStep.ReadingLongState, seen);
        Assert.Contains(ResearchStep.ChoosingState, seen);
        Assert.Equal(JobType.Research, it.CurrentJob);
    }

    [Fact]
    public void WithNoBookOrBookcaseAResearcherReadsShortOnABareTileAndChoosesAgain()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        new GameSimulation(db, world, seed: 1, [new AutoFurnitureSystem()]).Tick();
        foreach (ObjectState o in world.Objects.Where(o => o.ObjectTypeId is ResearchStep.BookObjectId or ResearchStep.BookCaseObjectId).ToList())
            world.RemoveObject(o.Id);
        CreatureState it = Researcher(db, world, new TilePos(2, 2));
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);

        HashSet<int> seen = Watch(sim, it, 40 * SimulationTiming.TicksPerSecond);

        Assert.Contains(ResearchStep.ReadingShortState, seen);
        Assert.DoesNotContain(ResearchStep.ReadingLongState, seen);
        Assert.Contains(ResearchStep.ChoosingState, seen);
    }

    [Fact]
    public void WithNothingToResearchTheChooserPlaysOneIdleActionAndThenGoesIdle()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        world.Player(Keeper).ResearchTarget = 0;
        CreatureState it = Researcher(db, world, new TilePos(2, 2));
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureJobSystem()]);

        sim.Run(2);
        Assert.Equal(ResearchStep.ChoosingState, it.Dk2StateId);
        sim.Run(8);

        Assert.Equal(CreatureActivity.Idle, it.Activity);
        Assert.Equal(JobType.None, it.CurrentJob);
    }

    [Fact]
    public void TheFinderCountsTheSeekersOwnLastTargetAsTaken()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        new GameSimulation(db, world, seed: 1, [new AutoFurnitureSystem()]).Tick();
        CreatureState it = Researcher(db, world, new TilePos(3, 3));
        ObjectState? first = null, second = null;
        var sim = new GameSimulation(db, world, seed: 1, [new Probe(context =>
        {
            ResearchStep.Find(context, Library(db, world), it, out first, out _);
            (it.StateTargetX, it.StateTargetY) = (CreatureState.CentreOf(first!.TileIndex % world.Width),
                CreatureState.CentreOf(first.TileIndex / world.Width));
            ResearchStep.Find(context, Library(db, world), it, out second, out _);
        })]);

        sim.Tick();

        Assert.NotNull(first);
        Assert.NotEqual(first!.Id, second?.Id);
    }

    [Fact]
    public void AnIntruderDisturbsAWorriedLongReaderInto59AndChargesACalmOneAngerNoWork()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState worried = Researcher(db, world, new TilePos(1, 1), ResearchStep.ReadingLongState);
        CreatureState calm = Researcher(db, world, new TilePos(3, 3), ResearchStep.ReadingLongState);
        CreatureDef vampire = db.CreatureById(Vampire);
        worried.Anger.Treatment = (CreatureAngerRules.UnhappyAt(vampire) / 10) + 1;
        calm.Anger.Treatment = CreatureAngerRules.UnhappyAt(vampire) / 10;
        CreatureState intruder = world.SpawnCreature(db, Vampire, Keeper, new TilePos(2, 2));
        var sim = new GameSimulation(db, world, seed: 1, [new Probe(context => ResearchStep.Disturb(context, "Test", Library(db, world), intruder))]);

        sim.Run(SimulationTiming.OriginalTicksPerGameSecond - 1);
        Assert.Equal(ResearchStep.ReadingLongState, worried.Dk2StateId);   // DK2's turn is our tick + 1
        sim.Tick();

        Assert.Equal(ResearchStep.DisturbedState, worried.Dk2StateId);
        Assert.Equal(ResearchStep.ReadingLongState, calm.Dk2StateId);
        Assert.Equal(vampire.AngerNoWork, calm.Anger.Work);
    }

    [Fact]
    public void TheContactTestRunsTheLibraryArmForACreatureNotResearching()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState worried = Researcher(db, world, new TilePos(1, 1), ResearchStep.ReadingLongState);
        worried.Anger.Treatment = CreatureAngerRules.UnhappyAt(db.CreatureById(Vampire));
        worried.ActionUntilTick = long.MaxValue;
        (worried.WaitPosX, worried.WaitPosY) = (worried.PosX, worried.PosY);
        var sim = new GameSimulation(db, world, seed: 1, [new RescueSystem()]);
        sim.Run(SimulationTiming.OriginalTicksPerGameSecond - 1);
        world.SpawnCreature(db, Vampire, Keeper, new TilePos(2, 2));   // its contact turn is DK2's second

        sim.Tick();

        Assert.Equal(ResearchStep.DisturbedState, worried.Dk2StateId);
    }

    [Fact]
    public void AnIntruderHoldingAPlaceDoesNotRunTheLibraryArm()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState worried = Researcher(db, world, new TilePos(1, 1), ResearchStep.ReadingLongState);
        worried.Anger.Treatment = CreatureAngerRules.UnhappyAt(db.CreatureById(Vampire));
        worried.ActionUntilTick = long.MaxValue;
        (worried.WaitPosX, worried.WaitPosY) = (worried.PosX, worried.PosY);
        var sim = new GameSimulation(db, world, seed: 1, [new RescueSystem()]);
        sim.Run(SimulationTiming.OriginalTicksPerGameSecond - 1);
        CreatureState intruder = world.SpawnCreature(db, Vampire, Keeper, new TilePos(2, 2));
        intruder.HeldPlace = new RoomPlace((2 * world.Width) + 2, RoomCapacityRules.Units);   // 0x0048A844's gate

        sim.Tick();

        Assert.Equal(ResearchStep.ReadingLongState, worried.Dk2StateId);
    }

    [Fact]
    public void TheWatchHurriesTheIntruderToTwiceItsCurrentSpeedForTwoV19Seconds()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        Researcher(db, world, new TilePos(1, 1), ResearchStep.DisturbedState);
        CreatureState intruder = world.SpawnCreature(db, Vampire, Keeper, new TilePos(1, 2));
        GameSimulation sim = Simulation(db, world);
        CreatureDef vampire = db.CreatureById(Vampire);
        int current = CreatureMovementSystem.MaxSpeedOf(db, vampire, intruder.Level, intruder.Running) >> 4;

        for (int i = 0; i < 3 && intruder.SpeedEffectTicks == 0; i++) sim.Tick();

        Assert.Equal(2 * current, intruder.SpeedPercent);
        Assert.InRange(intruder.SpeedEffectTicks, 1,
            2 * SimulationTiming.OriginalTicksPerGameSecond * SimulationTiming.OriginalTicksPerGameSecond);
    }

    [Fact]
    public void ADisturbedResearcherWatchesTheIntruderHurriesItOutAndWalksBackTo53()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState watcher = Researcher(db, world, new TilePos(1, 1), ResearchStep.DisturbedState);
        CreatureState intruder = world.SpawnCreature(db, Vampire, Keeper, new TilePos(1, 2));
        GameSimulation sim = Simulation(db, world);

        HashSet<int> seen = Watch(sim, watcher, 3);
        Assert.Equal(intruder.Id, watcher.WatchedCreatureId);
        Assert.True(intruder.SpeedEffectTicks > 0);

        seen.UnionWith(Watch(sim, watcher, 10 * SimulationTiming.TicksPerSecond));
        Assert.Contains(ResearchStep.ChoosingState, seen);
        Assert.True(watcher.Anger.Work > 0);
    }

    [Fact]
    public void AShortReaderGoesBackToTheChooserWhenABookcaseFrees()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        new GameSimulation(db, world, seed: 1, [new AutoFurnitureSystem()]).Tick();
        CreatureState it = Researcher(db, world, new TilePos(1, 3), ResearchStep.ReadingShortState);
        it.ActionUntilTick = long.MaxValue;
        (it.WaitPosX, it.WaitPosY) = (it.PosX, it.PosY);
        (it.StateTargetX, it.StateTargetY) = (it.PosX, it.PosY);
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureJobSystem()]);

        sim.Tick();

        Assert.Equal(ResearchStep.ChoosingState, it.Dk2StateId);
        Assert.Equal(0, it.ActionUntilTick);
    }

    private sealed class Probe(Action<SimulationContext> body) : ISimulationSystem
    {
        public string Name => "Probe";
        public void Execute(SimulationContext context) => body(context);
    }

    // ------------------------------------------------------------------- 60 and 62 (ADR-0402)

    /// <summary>A tile joins the room at (x, y), which stamps the rebuilt room (the type's slot +0x09).</summary>
    private static void Grow(GameDatabase db, WorldState world, RoomTypeId type, int x, int y)
    {
        RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
        SlabChain.Changed(world, db, (y * world.Width) + x, Dk2TerrainFixture.ClaimedPath, Keeper);
    }

    [Fact]
    public void ARebuiltLibrarySendsItsReaderTo62_WhichWaitsATurnAndGoesBackTo53()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState it = Researcher(db, world, new TilePos(2, 2), ResearchStep.ReadingShortState);
        it.ActionUntilTick = long.MaxValue;
        GameSimulation sim = Simulation(db, world);
        sim.Tick();

        Grow(db, world, RoomTypeId.Library, 4, 2);
        var order = new List<int>();
        long? endedIn = null;
        for (int i = 0; i < 6; i++)
        {
            sim.Tick();
            // The return state ends a running action on the turns it runs (0x00496D20).
            if (it.Dk2StateId == ResearchStep.ReturningState && it.ActionUntilTick == 0) endedIn = 0;
            order.Add(it.Dk2StateId);
        }

        int back = order.IndexOf(ResearchStep.ReturningState);
        Assert.True(back >= 0, string.Join(" ", order));
        Assert.Contains(ResearchStep.ChoosingState, order.Skip(back + 1));
        Assert.Equal(0, endedIn);   // 62 ends the read (0x00496D20)
    }

    /// <summary>The terrain at (x, y) becomes <paramref name="terrain"/> through the setter's own path.</summary>
    private static void SetTerrain(GameDatabase db, WorldState world, int x, int y, byte terrain)
    {
        ref TileState tile = ref world.Tile(x, y);
        (byte before, PlayerId owner) = (tile.TerrainId, tile.OwnerPlayerId);
        tile.TerrainId = terrain;
        SlabChain.Changed(world, db, (y * world.Width) + x, before, owner);
    }

    [Fact]
    public void ANeighbouringTilesChangeSendsTheLibrarysReaderTo62AndBackTo53_TheRoomUnchanged()
    {
        // 0x004E44E0 from Tile_SetTerrain (0x00450214): a wall reinforced beside the Library (ADR-0406).
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState it = Researcher(db, world, new TilePos(2, 2), ResearchStep.ReadingShortState);
        it.ActionUntilTick = long.MaxValue;
        GameSimulation sim = Simulation(db, world);
        sim.Tick();
        long size = SlabChain.SizeChangedTick(world, Library(db, world));

        SetTerrain(db, world, 4, 2, Dk2TerrainFixture.ReinforcedWall);
        var order = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            sim.Tick();
            order.Add(it.Dk2StateId);
        }

        Assert.Equal(size, SlabChain.SizeChangedTick(world, Library(db, world)));
        int back = order.IndexOf(ResearchStep.ReturningState);
        Assert.True(back >= 0, string.Join(" ", order));
        Assert.Contains(ResearchStep.ChoosingState, order.Skip(back + 1));
    }

    [Fact]
    public void AFreedLibrarySendsItsReaderTo62_WhichFindsNoLibraryAndIdles()
    {
        // The type's slot +0x05 (0x004E17E0): the room is gone, so 62's test fails (ADR-0406).
        GameDatabase db = Db();
        WorldState world = World(db, 1);
        CreatureState it = Researcher(db, world, new TilePos(1, 1), ResearchStep.ReadingShortState);
        it.ActionUntilTick = long.MaxValue;
        GameSimulation sim = Simulation(db, world);
        sim.Tick();

        SetTerrain(db, world, 1, 1, Dk2TerrainFixture.ClaimedPath);
        var order = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            sim.Tick();
            order.Add(it.Dk2StateId);
        }

        Assert.Contains(ResearchStep.ReturningState, order);
        Assert.Equal(JobType.None, it.CurrentJob);
        Assert.DoesNotContain(order[^1], new[] { ResearchStep.ReturningState, ResearchStep.ChoosingState });
    }

    /// <summary>The Vampire with art in slot 8 too, which 60's action 35 plays.</summary>
    private static GameDatabase WithCheerArt(GameDatabase basis)
    {
        var clip = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 20, FramesPerSecond = 20 };
        return new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c =>
            {
                if (c.CreatureId != Vampire) return c;
                CreatureAnimationTiming[] slots = [.. Enumerable.Range(0, CreatureDef.ArtSlotCount).Select(c.ArtSlot)];
                slots[8] = clip;
                return c with { ArtSlots = slots };
            }),
            basis.Variables, basis.KeeperSpells.Values, objects: basis.Objects.Values, shots: basis.Shots.Values);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]   // no art in slot 8: 60 returns 0 at once
    public void AShelvedItemSendsTheLibrarysReadersTo60_ACheerForTheLikelyCountedClipLength_ThenIdle(bool art)
    {
        GameDatabase db = art ? WithCheerArt(Db()) : Db();
        WorldState world = World(db, 3);
        CreatureState reader = Researcher(db, world, new TilePos(2, 2), ResearchStep.ReadingShortState);
        reader.ActionUntilTick = long.MaxValue;
        CreatureState elsewhere = world.SpawnCreature(db, Vampire, Keeper, new TilePos(10, 10));
        bool cheered = false;
        var shelve = new Probe(context =>
        {
            if (cheered) return;
            ResearchStep.Cheer(context, "probe", Library(db, world));
            cheered = true;
        });
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, shelve);
        sim.ObserveWith(sink);

        sim.Tick();
        Assert.Equal(ResearchStep.CelebratingState, reader.Dk2StateId);
        Assert.NotEqual(ResearchStep.CelebratingState, elsewhere.Dk2StateId);
        sim.Tick();

        // 60 at Age 1: the read ended, then the cheer (or nothing), then idle in the same call (61's
        // handler never runs), and the place is gone with the category.
        Assert.NotEqual(ResearchStep.CelebratingState, reader.Dk2StateId);
        Assert.False(reader.HeldPlace.IsHeld);
        SimEvent? cheer = sink.Events.Cast<SimEvent?>().FirstOrDefault(e => e!.Value.Kind == "research_cheer");
        if (!art)
        {
            Assert.Null(cheer);
            return;
        }
        int plays = int.Parse(cheer!.Value.Detail.Split(' ')[1]["plays=".Length..]);
        Assert.InRange(plays, 1, 2);
        Assert.Equal(sim.CurrentTick.Value - 1 + AnimationTimingRules.CountedHoldTicks(db.CreatureById(Vampire), 8, plays), reader.ActionUntilTick);
    }
}
