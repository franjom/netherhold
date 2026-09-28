// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The research tree: what banked research points buy. See
/// docs/research/research-tree.md and ADR-0247.</summary>
public sealed class ResearchTreeTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const ushort SpellKind = 5;
    private const ushort DoorKind = 4;
    private const int Researchable = 2;
    private const int Available = 3;
    private const int CreateImp = Dk2TerrainFixture.CreateImpSpellId;
    private const int WoodenDoor = 1;

    /// <summary>A 3×3 Library's only floor slot, its interior tile.</summary>
    private static readonly TilePos Shelf = new(2, 2);

    private static int Second => SimulationTiming.TicksPerSecond;

    /// <summary>The fixture, with a two-entry research list: Create Imp, then the wooden door.
    /// Each costs <paramref name="cost"/>.</summary>
    private static GameDatabase Db(int cost = 40)
    {
        var level = new GlobalVariables();
        level.Research.Add(new ResearchEntry(ResearchTreeRules.KindSpell, CreateImp, cost));
        level.Research.Add(new ResearchEntry(ResearchTreeRules.KindDoor, WoodenDoor, cost));
        return Dk2TerrainFixture.Database().ForLevel(level);
    }

    /// <summary>The keeper holds rows for both items, in the given states, and a 3×3 Library on
    /// the fixture's claimed square.</summary>
    private static WorldState World(GameDatabase db, int createImp = Researchable, int door = Researchable,
        bool library = true)
    {
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(SpellKind, CreateImp, createImp));
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(DoorKind, WoodenDoor, door));

        if (library)
        {
            for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Library), Keeper);
        }

        return world;
    }

    /// <summary>The fixture's Vampire at work in the Library: one research point a tick.</summary>
    private static CreatureState Researcher(GameDatabase db, WorldState world)
    {
        var at = new TilePos(1, 1);
        CreatureState it = world.SpawnCreature(db, Dk2TerrainFixture.VampireCreatureId, Keeper, at);
        it.CurrentJob = JobType.Research;
        StatePlacement.As(it, CreatureActivity.AtWork);
        StatePlacement.Place(it, JobRules.StateFor(it.CurrentJob)!.Value);
        it.JobTileIndex = (at.Y * world.Width) + at.X;
        return it;
    }

    private static (GameSimulation Sim, ListEventSink Events) Simulation(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, seed: 42, [new ResearchSystem(), new CreatureJobSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    private static List<ObjectState> Books(WorldState world) =>
        world.Objects.Where(o => o.ObjectTypeId == ResearchTreeRules.BookObjectTypeId).ToList();

    // ---- choosing ---------------------------------------------------------

    [Fact]
    public void TheTargetIsTheFirstEntryStillResearchable_InListOrder()
    {
        GameDatabase db = Db();
        WorldState world = World(db, createImp: Available);

        Simulation(db, world).Sim.Run(1);

        // Create Imp is first on the list but already available, and it can be upgraded, but
        // the upgrade pass only runs when nothing at all is researchable: the door is next.
        Assert.Equal(2, world.Player(Keeper).ResearchTarget);
    }

    [Fact]
    public void WithNothingResearchable_TheTargetIsTheFirstSpellToUpgrade()
    {
        GameDatabase db = Db();
        WorldState world = World(db, createImp: Available, door: Available);

        Simulation(db, world).Sim.Run(1);

        Assert.Equal(1, world.Player(Keeper).ResearchTarget);
    }

    [Fact]
    public void AnUpgradedSpellAndAnAvailableDoorLeaveNothingToResearch()
    {
        // Doors are never upgraded by research: the chooser's second pass takes spells only.
        GameDatabase db = Db();
        WorldState world = World(db, createImp: Available, door: Available);
        world.SetUpgraded(Keeper, SpellKind, CreateImp, true);

        Simulation(db, world).Sim.Run(1);

        Assert.Equal(0, world.Player(Keeper).ResearchTarget);
    }

    // ---- paying for it ----------------------------------------------------

    [Fact]
    public void AFinishedSpellIsABookOnTheShelf_AndTheBookMakesItAvailable()
    {
        GameDatabase db = Db(cost: 40);
        WorldState world = World(db);
        Researcher(db, world);
        (GameSimulation sim, ListEventSink events) = Simulation(db, world);

        sim.Run(Second * 12);   // 48 points at one a tick: the first 40 buy Create Imp

        Assert.Equal(Available, world.Availability(Keeper, SpellKind, CreateImp));
        ObjectState book = Assert.Single(Books(world));
        Assert.Equal((Shelf.Y * world.Width) + Shelf.X, book.TileIndex);
        Assert.Equal(Keeper, book.OwnerPlayerId);
        Assert.Equal((byte)ResearchTreeRules.KindSpell, book.ResearchKind);
        Assert.Equal((byte)CreateImp, book.ResearchTypeId);
        Assert.Contains(events.Events, e => e.Kind == "research_completed");

        // The points reset on delivery and the door is the next target.
        Assert.Equal(2, world.Player(Keeper).ResearchTarget);
        Assert.True(world.Player(Keeper).ResearchPoints < 40);
    }

    [Fact]
    public void AnUpgradeMakesNoBook_ItSetsTheSpellsFlag()
    {
        GameDatabase db = Db(cost: 40);
        WorldState world = World(db, createImp: Available, door: Available);
        Researcher(db, world);
        (GameSimulation sim, ListEventSink events) = Simulation(db, world);

        sim.Run(Second * 12);

        Assert.True(world.IsUpgraded(Keeper, SpellKind, CreateImp));
        Assert.Empty(Books(world));
        Assert.Contains(events.Events, e => e.Kind == "research_upgraded");
        Assert.Contains(events.Events, e => e.Kind == "research_finished");
        Assert.Equal(0, world.Player(Keeper).ResearchTarget);
    }

    [Fact]
    public void AFullShelfSpendsNothing_AndThePointsKeepBanking()
    {
        // Two books on the only floor slot fill it (0x004E1AC0: fewer than two).
        GameDatabase db = Db(cost: 40);
        WorldState world = World(db);
        int shelf = (Shelf.Y * world.Width) + Shelf.X;
        world.SpawnObject(db, ResearchTreeRules.BookObjectTypeId, Keeper, shelf);
        world.SpawnObject(db, ResearchTreeRules.BookObjectTypeId, Keeper, shelf);
        Researcher(db, world);
        (GameSimulation sim, ListEventSink events) = Simulation(db, world);

        sim.Run(Second * 12);

        Assert.Equal(Researchable, world.Availability(Keeper, SpellKind, CreateImp));
        Assert.Equal(1, world.Player(Keeper).ResearchTarget);
        Assert.True(world.Player(Keeper).ResearchPoints >= 40);
        Assert.Contains(events.Events, e => e.Kind == "research_no_shelf");
    }

    // ---- books ------------------------------------------------------------

    [Fact]
    public void ABookForASpellAlreadyHeldIsDestroyedWhenShelved()
    {
        GameDatabase db = Db();
        WorldState world = World(db, createImp: Available);
        ObjectState book = world.SpawnObject(db, ResearchTreeRules.BookObjectTypeId, PlayerIds.Unowned, 0);
        book.ResearchKind = ResearchTreeRules.KindSpell;
        book.ResearchTypeId = CreateImp;

        Assert.False(ResearchTreeRules.Shelve(world, db, Keeper, book));
        Assert.Empty(Books(world));
    }

    [Fact]
    public void LosingTheLastBookMakesTheItemResearchableAgain_ButKeepsTheUpgrade()
    {
        GameDatabase db = Db();
        WorldState world = World(db);
        ObjectState first = world.SpawnObject(db, ResearchTreeRules.BookObjectTypeId, PlayerIds.Unowned, 0);
        first.ResearchKind = ResearchTreeRules.KindDoor;
        first.ResearchTypeId = WoodenDoor;
        ObjectState second = world.SpawnObject(db, ResearchTreeRules.BookObjectTypeId, PlayerIds.Unowned, 0);
        second.ResearchKind = ResearchTreeRules.KindDoor;
        second.ResearchTypeId = WoodenDoor;
        Assert.True(ResearchTreeRules.Shelve(world, db, Keeper, first));
        Assert.True(ResearchTreeRules.Shelve(world, db, Keeper, second));
        world.SetUpgraded(Keeper, DoorKind, WoodenDoor, true);

        world.RemoveObject(first.Id);
        ResearchTreeRules.Unshelved(world, Keeper, first);
        Assert.Equal(Available, world.Availability(Keeper, DoorKind, WoodenDoor));

        world.RemoveObject(second.Id);
        ResearchTreeRules.Unshelved(world, Keeper, second);
        Assert.Equal(Researchable, world.Availability(Keeper, DoorKind, WoodenDoor));
        Assert.True(world.IsUpgraded(Keeper, DoorKind, WoodenDoor));
    }

    // ---- what an upgrade changes -----------------------------------------

    [Fact]
    public void AnUpgradedCreateImpMakesALevelFourImp()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        ref TileState heart = ref world.Tile(2, 2);
        heart.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
        heart.Health = db.TerrainById(heart.TerrainId).StartingHealth;
        heart.OwnerPlayerId = Keeper;
        world.Player(Keeper).Mana = 100_000;
        world.Player(Keeper).Availabilities.Add(
            new AvailabilityState(SpellKind, CreateImp, Available, Upgraded: true));
        var sim = new GameSimulation(db, world, 5, [new ImpSupplySystem()]);

        sim.Tick(InputFrame.Of(new CreateImpCommand(Keeper)));

        CreatureDef imp = db.CreatureById(Dk2TerrainFixture.ImpCreatureId);
        Assert.NotEmpty(world.Creatures);
        Assert.All(world.Creatures, c =>
        {
            Assert.Equal(4, c.Level);
            Assert.Equal(CreatureLevelRules.MaxHealth(db, imp, 4), c.Health);
        });
    }

    [Fact]
    public void WithoutTheUpgradeCreateImpMakesALevelOneImp()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        ref TileState heart = ref world.Tile(2, 2);
        heart.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
        heart.Health = db.TerrainById(heart.TerrainId).StartingHealth;
        heart.OwnerPlayerId = Keeper;
        world.Player(Keeper).Mana = 100_000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(SpellKind, CreateImp, Available));
        var sim = new GameSimulation(db, world, 5, [new ImpSupplySystem()]);

        sim.Tick(InputFrame.Of(new CreateImpCommand(Keeper)));

        Assert.NotEmpty(world.Creatures);
        Assert.All(world.Creatures, c => Assert.Equal(CreatureLevelRules.FirstLevel, c.Level));
    }

    // ---- the save ---------------------------------------------------------

    [Fact]
    public void TheTargetTheUpgradeAndTheBookSurviveASave()
    {
        GameDatabase db = Db(cost: 40);
        WorldState world = World(db);
        Researcher(db, world);
        (GameSimulation sim, _) = Simulation(db, world);
        sim.Run(Second * 12);
        world.SetUpgraded(Keeper, DoorKind, WoodenDoor, true);

        GameSimulation loaded = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new ResearchSystem(), new CreatureJobSystem()]);

        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(2, loaded.World.Player(Keeper).ResearchTarget);
        Assert.True(loaded.World.IsUpgraded(Keeper, DoorKind, WoodenDoor));
        ObjectState book = Assert.Single(Books(loaded.World));
        Assert.Equal((byte)CreateImp, book.ResearchTypeId);
    }
}
