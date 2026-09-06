// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// The Prison: capture, withering, skeletons and the jail break (ADR-0064).
///
/// <para>The other branch of DK2's imp task kind 13, and the half of carrying
/// ADR-0063 had to stop short of. The owner drew the line between the two verbs
/// on 2026-09-05, and it is sharper than this project had it:</para>
///
/// <para><i>"a corpse can not be picked up by hand of evil and dropped to a
/// graveyard, only an imp can carry it to its grave, also an injured creature,
/// lying on the floor bleeding out, can't be picked up by hand of evil and
/// dropped to its lair, only an imp can carry it to its bed to recover, likewise
/// only an imp can drag an enemy creature to prison, a hand of evil can then
/// pick up that creature from prison and drop it around the dungeon, back in
/// prison, or onto a torture rack in the torture chamber."</i></para>
///
/// <para>So: <b>the hand never touches a body</b>, and <b>a prisoner is the one
/// creature a keeper may lift that it does not own</b>.</para>
/// </summary>
public class PrisonTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 1;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte GoblinId = 7;
    private const byte SkeletonId = Dk2TerrainFixture.SkeletonCreatureId;
    private const int Second = SimulationTiming.TicksPerSecond;

    // The fixture map's walkable ground is a 3x3 claimed room at (1..3, 1..3).
    // The prison is its right-hand column, which leaves the left two columns to
    // stand imps and bodies on.
    private static readonly TilePos CellA = new(3, 1);
    private static readonly TilePos CellB = new(3, 2);
    private static readonly TilePos CellC = new(3, 3);
    private static readonly TilePos Floor = new(1, 3);

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(),
            new RoomSellSystem(),
            new CreatureMovementSystem(),
            new ImpTaskSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
            new PrisonSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>Lays a room tile down without going through the build rules.</summary>
    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, TilePos at)
    {
        ref TileState tile = ref world.Tile(at.X, at.Y);
        RoomRules.Place(db, ref tile, db.Room(type), Keeper);
    }

    private static void Prison(GameDatabase db, WorldState world, params TilePos[] cells)
    {
        foreach (TilePos cell in cells) Paint(db, world, RoomTypeId.Prison, cell);
    }

    /// <summary>
    /// Puts a creature on the floor before tick 0 — the same helper
    /// <c>CarryingTests</c> uses, and for the same reason: a collapse is booked
    /// once a game second, so setting health to zero alone leaves nineteen ticks
    /// in which an imp takes some other job.
    /// </summary>
    private static void Down(CreatureState creature)
    {
        creature.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(creature));
    }

    private static CreatureState Prisoner(
        GameDatabase db, WorldState world, TilePos cell, byte owner = Rival)
    {
        CreatureState captive = world.SpawnCreature(db, GoblinId, owner, cell);
        int index = (cell.Y * world.Width) + cell.X;
        Assert.True(PrisonRules.Imprison(world, db, captive, Keeper, index));
        return captive;
    }

    // ---- capture ----------------------------------------------------------

    [Fact]
    public void AnImpDragsAnEnemyToAPrisonRatherThanToABed()
    {
        // Kind 13's other branch. Same task, same priority, different room -
        // the original's handler carries the pair of room ids (2 and 11) and
        // picks between them on ownership.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);

        CreatureState enemy = world.SpawnCreature(db, GoblinId, Rival, new TilePos(1, 1));
        Down(enemy);
        world.SpawnCreature(db, ImpId, Keeper, Floor);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 30 * Second; i++) sim.Tick();

        Assert.Equal(CreatureActivity.Imprisoned, enemy.Activity);
        Assert.Equal(Keeper, enemy.PrisonOwnerPlayerId);

        // Still the rival's creature. A prisoner changes jailers, not keepers.
        Assert.Equal(Rival, enemy.OwnerPlayerId);
        Assert.Contains(sink.Events, e => e.Kind == "creature_imprisoned");
    }

    [Fact]
    public void AnImpLeavesAnEnemyLyingWhenItsKeeperHasNoPrison()
    {
        // What ADR-0063 shipped as a deliberate stop, kept as a rule: without
        // somewhere to put a body, nobody sets off for it.
        GameDatabase db = Db;
        WorldState world = World(db);

        CreatureState enemy = world.SpawnCreature(db, GoblinId, Rival, new TilePos(1, 1));
        Down(enemy);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        Assert.False(PrisonRules.IsImprisoned(enemy));
        Assert.NotEqual(CreatureActivity.FetchingBody, imp.Activity);
        Assert.NotEqual(CreatureActivity.CarryingBody, imp.Activity);
    }

    [Fact]
    public void AnImpWillNotDeliverIntoAPrisonThatIsFull()
    {
        // The manual: imps try the next nearest prison with space, and when
        // there is none they stop collecting prisoners altogether.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA);
        Prisoner(db, world, CellA);

        CreatureState enemy = world.SpawnCreature(db, GoblinId, Rival, new TilePos(1, 1));
        Down(enemy);
        world.SpawnCreature(db, ImpId, Keeper, Floor);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        Assert.False(PrisonRules.IsImprisoned(enemy));
    }

    [Fact]
    public void APrisonHoldsOnePrisonerPerTile()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);

        RoomInstance prison = RoomIndex.Build(world, db)
            .Single(r => r.Definition.TypeId == RoomTypeId.Prison);
        Assert.Equal(3, PrisonRules.Capacity(prison));

        Prisoner(db, world, CellA);
        Prisoner(db, world, CellB);
        Assert.Equal(2, PrisonRules.PrisonerCount(world, prison));

        // The third cell is the only one a carry can still be aimed at.
        int free = PrisonRules.FreeTileIn(world, db, Keeper, Floor);
        Assert.Equal((CellC.Y * world.Width) + CellC.X, free);

        Prisoner(db, world, CellC);
        Assert.Equal(-1, PrisonRules.FreeTileIn(world, db, Keeper, Floor));
    }

    [Fact]
    public void AnImprisonedCreatureIsNotPickedUpAndCarriedOffAgain()
    {
        // A prisoner is not a body. CarryRules refuses it outright, which is
        // what stops the jailer's own imps ferrying prisoners back and forth.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);

        Assert.False(CarryRules.IsCarryable(captive));
    }

    // ---- withering --------------------------------------------------------

    [Fact]
    public void APrisonerWakesWithTwoPetalsAndLosesThreeHealthASecond()
    {
        // CONFIRMED by the owner (2026-09-05, Q167): "its flower gets two
        // petals, and then that creature starts loosing health at some standard
        // rate". Two petals of ten is CreatureCriticalHealthPercentageOfMax,
        // which ships at 20 - a Goblin's 1500 becomes 300.
        //
        // A first draft gave a prisoner FULL health on nothing better than the
        // shape of the mechanic, which made a Goblin take 500 game seconds to
        // wither where it should take a hundred.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);

        Assert.Equal(300, captive.Health);
        Assert.Equal(db.CreatureById(GoblinId).Hp / 5, captive.Health);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 10 * Second; i++) sim.Tick();

        Assert.Equal(300 - 30, captive.Health);
    }

    [Fact]
    public void ACapturedGoblinIsASkeletonInAHundredGameSeconds()
    {
        // The whole clock end to end, and the number the owner's answer moved:
        // 300 health at -3 a second is 100 seconds, not the 500 a full-health
        // prisoner would have taken. That is the difference between a prison
        // being an urgent decision and a forgettable one, and it is why a
        // keeper reaches for a chicken.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);
        int captiveId = captive.Id;

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 99 * Second; i++) sim.Tick();
        Assert.NotNull(world.CreatureById(captiveId));

        for (int i = 0; i < 2 * Second; i++) sim.Tick();
        Assert.Null(world.CreatureById(captiveId));

        CreatureState skeleton = Assert.Single(world.Creatures);
        Assert.Equal(SkeletonId, skeleton.CreatureTypeId);
    }

    [Fact]
    public void APrisonerThatWithersAwayRisesAsASkeletonForItsJailer()
    {
        // The manual, in its own words: "any creatures that perish in Prison
        // will rise as Skeletons under your control". The Prison's
        // CreatedCreatureId is 9 and this is the only thing in the project that
        // reads that column.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);
        captive.Health = 3;
        int captiveId = captive.Id;

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 2 * Second; i++) sim.Tick();

        Assert.Null(world.CreatureById(captiveId));

        CreatureState skeleton = Assert.Single(world.Creatures);
        Assert.Equal(SkeletonId, skeleton.CreatureTypeId);

        // The jailer's, not the rival's. That is the whole point of the room.
        Assert.Equal(Keeper, skeleton.OwnerPlayerId);
        Assert.Equal(CellA, skeleton.Tile);
        Assert.Equal(db.CreatureById(SkeletonId).Hp, skeleton.Health);
        Assert.Contains(sink.Events, e => e.Kind == "prisoner_perished");
    }

    [Fact]
    public void ASkeletonInACellDoesNotWitherAtAllBecauseItNeverEats()
    {
        // Q173, read from the binary on 2026-09-06 once the prison state
        // category's own update hook was identified: the routine that applies
        // PrisonModifyCreatureHealthPerSecond takes the health off ONLY when the
        // creature's HungerFill is non-zero. The wither is HUNGER, which the
        // owner had guessed and said he did not know.
        //
        // Which makes a Skeleton stable in a cell for ever - and a Skeleton is
        // what a prison MAKES, so the room's own product is immune to the room.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);

        CreatureState bones = world.SpawnCreature(db, SkeletonId, Rival, CellA);
        Assert.True(PrisonRules.Imprison(
            world, db, bones, Keeper, (CellA.Y * world.Width) + CellA.X));

        // Whatever the arrival health is - two petals, like anybody else - it
        // stays there.
        int arrived = bones.Health;

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 60 * Second; i++) sim.Tick();

        Assert.Equal(arrived, bones.Health);
        Assert.True(PrisonRules.IsImprisoned(bones));

        // And the control, on the same map and the same clock: a Goblin does.
        CreatureState goblin = Prisoner(db, world, CellB);
        int before = goblin.Health;
        for (int i = 0; i < 10 * Second; i++) sim.Tick();
        Assert.True(goblin.Health < before);
    }

    [Fact]
    public void APrisonerLeavesNoBodyAndNoCorpseBehindIt()
    {
        // Dying in a prison is not the ordinary death: there is no collapse, no
        // rescue window and nothing for another imp to carry to a graveyard.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);
        captive.Health = 1;

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 2 * Second; i++) sim.Tick();

        Assert.DoesNotContain(world.Creatures, CreatureDeathRules.IsCorpse);
        Assert.DoesNotContain(world.Creatures, CreatureDeathRules.IsDying);
    }

    [Fact]
    public void APrisonerDoesNotWalkWorkSleepOrGrowHungry()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);
        int hunger = captive.HungerTicks;

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        Assert.Equal(CreatureActivity.Imprisoned, captive.Activity);
        Assert.Equal(CellA, captive.Tile);
        Assert.Empty(captive.Path);

        // Its whole second belongs to PrisonSystem - the health system does not
        // get a look at it, so nothing else moves its hunger clock either.
        Assert.Equal(hunger, captive.HungerTicks);
    }

    // ---- the hand ---------------------------------------------------------

    [Fact]
    public void TheHandCannotPickUpACreatureBleedingOutOnTheFloor()
    {
        // CONFIRMED by the owner, 2026-09-05, and it corrects ADR-0062: that
        // decision recorded the refusal as a narrowing of ours, invented to
        // excuse a missing mechanic. It is DK2's own rule. Its pick-up handler
        // refuses the state flag at bit 0x200000 and DK2's creature dump names
        // that bit CreatureDying (Q161).
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState mine = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        Down(mine);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, mine.Id)));

        Assert.False(mine.InHand);
        Assert.Empty(world.Player(Keeper).Hand);
        Assert.Contains(sink.Events, e => e.Kind == "pickup_refused");
    }

    [Fact]
    public void TheHandCannotPickUpACorpseEither()
    {
        // "only an imp can carry it to its grave".
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState mine = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        Down(mine);
        CreatureDeathRules.BecomeCorpse(world, db, mine);
        Assert.True(CreatureDeathRules.IsCorpse(mine));

        GameSimulation sim = Simulation(db, world);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, mine.Id)));

        Assert.False(mine.InHand);
    }

    [Fact]
    public void TheHandMayLiftAPrisonerOutOfItsOwnPrison()
    {
        // The one ownership exception in the game: "a hand of evil can then
        // pick up that creature from prison".
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);

        GameSimulation sim = Simulation(db, world);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, captive.Id)));

        Assert.True(captive.InHand);
        Assert.Equal([captive.Id], world.Player(Keeper).Hand);

        // Lifting one out is not freeing one. It is still the keeper's prisoner
        // while it is in the hand, which is what lets the drop put it back.
        Assert.Equal(Keeper, captive.PrisonOwnerPlayerId);
    }

    [Fact]
    public void TheHandStillCannotTouchAnEnemyCreatureWalkingAround()
    {
        // The exception is exactly as wide as the prison. A rival's creature at
        // liberty is not the keeper's to pick up, prison or no prison.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState theirs = world.SpawnCreature(db, GoblinId, Rival, Floor);

        GameSimulation sim = Simulation(db, world);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, theirs.Id)));

        Assert.False(theirs.InHand);
    }

    [Fact]
    public void APrisonerIsNotSlappable()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);
        int health = captive.Health;

        GameSimulation sim = Simulation(db, world);
        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, captive.Id)));

        Assert.Equal(health, captive.Health);
        Assert.Equal(0, captive.WorkFasterTicks);
    }

    [Fact]
    public void DroppingAPrisonerBackOnAPrisonTilePutsItStraightBack()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, captive.Id)));
        sim.Tick(InputFrame.Of(new DropCreatureCommand(Keeper, CellC)));

        Assert.False(captive.InHand);
        Assert.Equal(CreatureActivity.Imprisoned, captive.Activity);
        Assert.Equal(Keeper, captive.PrisonOwnerPlayerId);
        Assert.Equal(CellC, captive.Tile);
        Assert.Contains(sink.Events, e => e.Kind == "prisoner_returned");
    }

    [Fact]
    public void DroppingAPrisonerAnywhereElseSetsItFree()
    {
        // "drop it around the dungeon" - and a creature dropped around the
        // dungeon is loose in it, which is the keeper's problem now.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, captive.Id)));
        sim.Tick(InputFrame.Of(new DropCreatureCommand(Keeper, Floor)));

        Assert.False(PrisonRules.IsImprisoned(captive));
        Assert.Equal(Rival, captive.OwnerPlayerId);
        Assert.NotEqual(CreatureActivity.Imprisoned, captive.Activity);
        Assert.Contains(sink.Events,
            e => e.Kind == "prisoner_freed" && e.Detail.Contains("dropped_outside"));
    }

    [Fact]
    public void APrisonerWhoseCellIsSoldOutFromUnderItGoesFree()
    {
        // The manual's "losing a prison frees every enemy in it", and the same
        // rule covers selling part of one in use. Checked by asking where the
        // prisoner is standing rather than by hooking the sell path, so digging
        // the tile away or a rival claiming it works the same way.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Tick(InputFrame.Of(SellRoomCommand.Rect(Keeper, CellA.X, CellA.Y, CellA.X, CellA.Y)));
        for (int i = 0; i < 2 * Second; i++) sim.Tick();

        Assert.False(PrisonRules.IsImprisoned(captive));
        Assert.Contains(sink.Events,
            e => e.Kind == "prisoner_freed" && e.Detail.Contains("cell_gone"));
    }

    [Fact]
    public void AnOvercrowdedPrisonBreaksOutAndEmptiesItself()
    {
        // Fifty percent per extra creature, once a game second, so two extra
        // prisoners are a certainty on the first roll and one is a coin toss
        // that will not survive twenty of them.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA);

        CreatureState first = Prisoner(db, world, CellA);

        // Only a keeper can do this. An imp refuses to deliver into a full
        // prison, so overcrowding is always somebody's own doing - which is
        // exactly what the manual calls it.
        CreatureState second = world.SpawnCreature(db, GoblinId, Rival, CellA);
        Assert.True(PrisonRules.Imprison(
            world, db, second, Keeper, (CellA.Y * world.Width) + CellA.X));

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        Assert.False(PrisonRules.IsImprisoned(first));
        Assert.False(PrisonRules.IsImprisoned(second));
        Assert.Contains(sink.Events, e => e.Kind == "jail_break");
    }

    [Fact]
    public void APrisonThatIsMerelyFullDoesNotBreakOut()
    {
        // Full is not overcrowded. A prison at exactly its capacity holds.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB);
        CreatureState first = Prisoner(db, world, CellA);
        CreatureState second = Prisoner(db, world, CellB);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 30 * Second; i++) sim.Tick();

        Assert.True(PrisonRules.IsImprisoned(first));
        Assert.True(PrisonRules.IsImprisoned(second));
    }

    // ---- bookkeeping ------------------------------------------------------

    [Fact]
    public void APrisonerSurvivesASnapshot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world, CellA, CellB, CellC);
        CreatureState captive = Prisoner(db, world, CellA);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 3 * Second; i++) sim.Tick();

        byte[] bytes = WorldSnapshot.ToBytes(sim);
        GameSimulation restored = WorldSnapshot.FromBytes(
            bytes, db, SimulationSystems.Default());

        Assert.Equal(sim.StateHash(), restored.StateHash());

        CreatureState? back = restored.World.CreatureById(captive.Id);
        Assert.NotNull(back);
        Assert.Equal(Keeper, back!.PrisonOwnerPlayerId);
        Assert.Equal(CreatureActivity.Imprisoned, back.Activity);
    }
}
