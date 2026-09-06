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

    // A real 3x3 prison at (4..6, 3..5) - DK2's smallest working one, and the
    // size the owner read a capacity of TWO off the status bar (Q166). The three
    // named cells are three of its nine tiles; the floor around it is paved so
    // there is somewhere to stand imps and lay bodies down.
    //
    // It used to be a 1x3 column, which under the corrected capacity rule is not
    // a prison at all: no inner tile, no capacity, no room.
    private static readonly TilePos CellA = new(4, 3);
    private static readonly TilePos CellB = new(5, 4);
    private static readonly TilePos CellC = new(6, 5);
    private static readonly TilePos Floor = new(2, 3);

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
            new PrisonerEatSystem(),
            new PrisonerWanderSystem(),
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

    /// <summary>
    /// Paves the ground and paints DK2's smallest working prison on it: 3x3 at
    /// (4..6, 3..5), which holds two.
    /// </summary>
    private static void Prison(GameDatabase db, WorldState world)
    {
        for (int y = 1; y <= 8; y++)
        {
            for (int x = 1; x <= 8; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }
        }

        for (int y = 3; y <= 5; y++)
            for (int x = 4; x <= 6; x++)
                Paint(db, world, RoomTypeId.Prison, new TilePos(x, y));
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
        Prison(db, world);

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
        Prison(db, world);

        // Full is TWO on a 3x3, not one per tile (Q166).
        Prisoner(db, world, CellA);
        Prisoner(db, world, CellB);

        CreatureState enemy = world.SpawnCreature(db, GoblinId, Rival, new TilePos(1, 1));
        Down(enemy);
        world.SpawnCreature(db, ImpId, Keeper, Floor);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        Assert.False(PrisonRules.IsImprisoned(enemy));
    }

    [Fact]
    public void ThreeByThreeIsDk2sSmallestPrisonAndItHoldsTwo()
    {
        // CONFIRMED off DK2's own status bar (owner, 2026-09-06):
        // "Captives held: 0 / 2 ... Maximum Skeletons: 2" on a 3x3.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);

        RoomInstance prison = RoomIndex.Build(world, db)
            .Single(r => r.Definition.TypeId == RoomTypeId.Prison);
        Assert.Equal(9, prison.TileCount);
        Assert.Equal(1, prison.InteriorTileCount);
        Assert.Equal(2, PrisonRules.Capacity(prison));

        Prisoner(db, world, CellA);

        // One in, one to go - and the tile it goes on is any free one, because
        // the rim is where a body is laid down and the capacity is not per tile.
        Assert.NotEqual(-1, PrisonRules.FreeTileIn(world, db, Keeper, Floor));

        Prisoner(db, world, CellB);
        Assert.Equal(2, PrisonRules.PrisonerCount(world, prison));

        // Full at two, with seven empty tiles still in the room.
        Assert.Equal(-1, PrisonRules.FreeTileIn(world, db, Keeper, Floor));
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(1, 5, 0)]
    [InlineData(3, 3, 2)]
    [InlineData(3, 4, 3)]
    [InlineData(4, 4, 5)]
    [InlineData(5, 5, 10)]
    public void APrisonHoldsItsInnerTilesPlusOne(int w, int h, int expected)
    {
        // The owner's other half: "any smaller size and it doesn't render like a
        // prison, it only occupies those tiles." A room with no inner tile has
        // no capacity, and DK2's "+1" never fires because the count is zero.
        GameDatabase db = Db;
        WorldState world = World(db);

        for (int y = 3; y < 3 + h; y++)
            for (int x = 4; x < 4 + w; x++)
                Paint(db, world, RoomTypeId.Prison, new TilePos(x, y));

        RoomInstance prison = RoomIndex.Build(world, db)
            .Single(r => r.Definition.TypeId == RoomTypeId.Prison);
        Assert.Equal(w * h, prison.TileCount);
        Assert.Equal(expected, PrisonRules.Capacity(prison));
    }

    [Fact]
    public void AnImprisonedCreatureIsNotPickedUpAndCarriedOffAgain()
    {
        // A prisoner is not a body. CarryRules refuses it outright, which is
        // what stops the jailer's own imps ferrying prisoners back and forth.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellA);

        Assert.False(CarryRules.IsCarryable(captive));
    }

    // ---- withering --------------------------------------------------------

    [Fact]
    public void APrisonerWakesAtOneSixthAndLosesThreeHealthASecond()
    {
        // CONFIRMED by the owner (2026-09-05, Q167): "its flower gets two
        // petals, and then that creature starts loosing health at some standard
        // rate".
        //
        // The NUMBER is now read rather than fitted (2026-09-06). DK2's
        // ModifyHealth, on taking a creature below zero, sets its health to
        // maxHealth / 6 and flags it dying - computed the long way, as
        // 100 * max / 6 / 100, which is the arithmetic of "a sixth expressed as
        // a percentage". A flower has six petals. Those are the same decision.
        //
        // What this replaced was 20%, which was ours twice over: it came from
        // CreatureCriticalHealthPercentageOfMax read as "two petals of ten", and
        // the flower has six petals AND a raw scan of .text finds exactly one
        // reference to that variable's global - the loader writing it. Nothing
        // in the game reads it.
        //
        // A Goblin's 1500 becomes 250, not 300. An earlier draft gave a
        // prisoner FULL health on nothing better than the shape of the
        // mechanic, which made it take 500 game seconds to wither.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellA);

        Assert.Equal(250, captive.Health);
        Assert.Equal(
            db.CreatureById(GoblinId).Hp / CreatureHealthRules.KnockoutFraction,
            captive.Health);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 10 * Second; i++) sim.Tick();

        Assert.Equal(250 - 30, captive.Health);
    }

    [Fact]
    public void ACapturedGoblinIsASkeletonInEightyThreeGameSeconds()
    {
        // The whole clock end to end. A Goblin knocked out sits at 1500/6 = 250,
        // and PrisonModifyCreatureHealthPerSecond is -3, so it has 83 seconds -
        // not the 100 this test asserted while the arrival health was a guessed
        // 20%, and not the 500 a full-health prisoner would have had. That is
        // the difference between a prison being an urgent decision and a
        // forgettable one, and it is why a keeper reaches for a chicken.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellA);
        int captiveId = captive.Id;

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 82 * Second; i++) sim.Tick();
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
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellA);
        captive.Health = 3;
        int captiveId = captive.Id;

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        // Where it STOOD, not where it went in. Since ADR-0073 a prisoner
        // shuffles about its cell, so the tile it dies on is whichever one it
        // had wandered to - and "replaced where it stands" is the actual rule,
        // which this used to assert only by accident of the creature never
        // moving.
        TilePos died = captive.Tile;
        for (int i = 0; i < 2 * Second; i++)
        {
            sim.Tick();
            if (world.CreatureById(captiveId) is null) break;
            died = captive.Tile;
        }

        Assert.Null(world.CreatureById(captiveId));

        CreatureState skeleton = Assert.Single(world.Creatures);
        Assert.Equal(SkeletonId, skeleton.CreatureTypeId);

        // The jailer's, not the rival's. That is the whole point of the room.
        Assert.Equal(Keeper, skeleton.OwnerPlayerId);
        Assert.Equal(died, skeleton.Tile);
        Assert.InRange(skeleton.Tile.X, 4, 6);
        Assert.InRange(skeleton.Tile.Y, 3, 5);
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
        Prison(db, world);

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
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellA);
        captive.Health = 1;

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 2 * Second; i++) sim.Tick();

        Assert.DoesNotContain(world.Creatures, CreatureDeathRules.IsCorpse);
        Assert.DoesNotContain(world.Creatures, CreatureDeathRules.IsDying);
    }

    /// <summary>
    /// A prisoner does not work, sleep or grow hungry. <b>It used to not walk
    /// either, and that half is gone</b> (ADR-0073): the two assertions this
    /// test lost - that the creature never left its arrival tile and never held
    /// a path - were the stated narrowing Q172 was raised against, and DK2's
    /// prisoners shuffle about their cell. What survives is everything that was
    /// a rule rather than a gap.
    /// </summary>
    [Fact]
    public void APrisonerDoesNotWorkOrSleep()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellA);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 20 * Second; i++) sim.Tick();

        // Wandering is not a change of activity: the original stays in its
        // prison states throughout, and so does this. Nor is walking to a
        // chicken - the same three labels cover all six prison states.
        Assert.Equal(CreatureActivity.Imprisoned, captive.Activity);

        // It moved, but only ever inside the cell.
        Assert.InRange(captive.Tile.X, 4, 6);
        Assert.InRange(captive.Tile.Y, 3, 5);
    }

    [Fact]
    public void APrisonerDoesGrowHungry()
    {
        // This test used to say the opposite, and the opposite was a side
        // effect: the health system skipped prisoners entirely so that exactly
        // one system settled their second (ADR-0064), and their hunger clock
        // went with their health. DK2 suppresses the hunger clock on a bit of
        // the state record's flag byte, and the fifteen states that set it are
        // drinking, gambling and dancing - the Casino, not the Prison
        // (ADR-0074).
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellA);
        CreatureDef goblin = db.CreatureById(GoblinId);

        Assert.Equal(0, captive.HungerTicks);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 10 * Second; i++) sim.Tick();

        Assert.Equal(10 * Second, captive.HungerTicks);

        // Ten seconds is as far as this can be taken: a Goblin's HungerRate is
        // 180 game seconds and PrisonModifyCreatureHealthPerSecond turns it to
        // bone in 83, so no prisoner ever gets hungry IN a cell.
        //
        // That is not an argument that the mechanic is unreachable, and the
        // reason is the clock's shape rather than its rate. HungerNeed
        // accumulates over a creature's whole life and is paid off only by
        // eating (EatingRules.Tick), so a creature that has been running about
        // the map for three minutes is ALREADY hungry when the imps take it -
        // and looks for food the moment it is put down. That is the case the
        // owner watched, and ARaceForOneChickenInACell sets it up the same way.
        Assert.True(captive.Health > 0);
        Assert.Equal(CreatureActivity.Imprisoned, captive.Activity);
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
        Prison(db, world);
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
        Prison(db, world);
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
        Prison(db, world);
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
        Prison(db, world);
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
        Prison(db, world);
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
        Prison(db, world);
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
        Prison(db, world);

        CreatureState first = Prisoner(db, world, CellA);
        CreatureState filler = Prisoner(db, world, CellB);

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
        Assert.False(PrisonRules.IsImprisoned(filler));
        Assert.False(PrisonRules.IsImprisoned(second));
        Assert.Contains(sink.Events, e => e.Kind == "jail_break");
    }

    [Fact]
    public void APrisonThatIsMerelyFullDoesNotBreakOut()
    {
        // Full is not overcrowded. A prison at exactly its capacity holds.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
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
        Prison(db, world);
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

    // ---- wandering the cell (Q172, ADR-0073) ------------------------------

    /// <summary>
    /// <b>A prisoner moves</b>, which it did not before ADR-0073. The mover
    /// used to refuse to walk one at all, and that refusal was written down as
    /// a stated narrowing against this very question.
    /// </summary>
    [Fact]
    public void APrisonerShufflesAboutItsCell()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellB);

        TilePos start = captive.Tile;
        GameSimulation sim = Simulation(db, world);

        bool moved = false;
        for (int i = 0; i < 5 * Second && !moved; i++)
        {
            sim.Tick();
            moved = captive.Tile != start;
        }

        Assert.True(moved, "a prisoner stood still for five game seconds");
    }

    /// <summary>
    /// <b>And never leaves.</b> The guarantee is not a rule in the mover — it
    /// is that a tile outside the cell is never offered as a candidate, because
    /// the original's scan tests each one against the room instance the
    /// creature is standing in. Run long enough that a random walk would have
    /// escaped a 3x3 many times over if anything let it.
    /// </summary>
    [Fact]
    public void APrisonerNeverWandersOutOfItsCell()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellB);

        GameSimulation sim = Simulation(db, world);
        var visited = new HashSet<TilePos>();

        // Eighty seconds is nearly the whole of a Goblin's clock, so this also
        // says it wanders right up until the moment it does not.
        for (int i = 0; i < 80 * Second; i++)
        {
            sim.Tick();
            if (world.CreatureById(captive.Id) is null) break;

            visited.Add(captive.Tile);
            Assert.InRange(captive.Tile.X, 4, 6);
            Assert.InRange(captive.Tile.Y, 3, 5);
        }

        // It should have got about, not jittered between two tiles.
        Assert.True(visited.Count >= 4,
            $"a prisoner only ever stood on {visited.Count} tiles of nine");
    }

    /// <summary>
    /// <b>The scan order is the original's, and it is not cosmetic.</b> DK2
    /// draws a starting index and then walks its table of offset pairs
    /// forwards, wrapping — north, east, south, west, as stored at
    /// <c>0x006BC350</c>. So the direction a prisoner takes when its first
    /// choice is a wall is decided by that order, and getting it backwards
    /// would be invisible except in the walk it produces.
    ///
    /// <para>The prisoner stands in the cell's north-west corner (4,3): north
    /// and west are outside the room, so a start of 0 (north) has to wrap all
    /// the way to east, and a start of 3 (west) wraps to north, then east.</para>
    /// </summary>
    [Theory]
    [InlineData(0, 5, 3)]  // north is outside -> east
    [InlineData(1, 5, 3)]  // east
    [InlineData(2, 4, 4)]  // south
    [InlineData(3, 5, 3)]  // west is outside -> wraps past north -> east
    public void ThePrisonersStepFollowsDk2sOwnDirectionOrder(int start, int x, int y)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, new TilePos(4, 3));

        Assert.True(PrisonRules.TryWanderStep(world, db, captive, start, out TilePos step));
        Assert.Equal(new TilePos(x, y), step);
    }

    /// <summary>
    /// A prisoner with nowhere to go stands still rather than walking out.
    /// There is no such prison in DK2 — a room this small is not a prison at
    /// all (Q166) — but the scan returning nothing is a case the original
    /// handles and so does this.
    /// </summary>
    [Fact]
    public void APrisonerWithNoNeighbouringCellTileStandsStill()
    {
        GameDatabase db = Db;
        WorldState world = World(db);

        for (int y = 1; y <= 8; y++)
            for (int x = 1; x <= 8; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }

        var only = new TilePos(4, 4);
        Paint(db, world, RoomTypeId.Prison, only);

        CreatureState captive = world.SpawnCreature(db, GoblinId, Rival, only);
        captive.PrisonOwnerPlayerId = Keeper;
        captive.Activity = CreatureActivity.Imprisoned;

        for (int start = 0; start < PrisonRules.WanderDirections; start++)
            Assert.False(PrisonRules.TryWanderStep(world, db, captive, start, out _));
    }

    /// <summary>
    /// <b>Wandering does not buy a prisoner time.</b> The clock is
    /// PrisonSystem's and runs on the second whatever the creature is doing, so
    /// a Goblin that spends its whole sentence walking still turns to bone at
    /// eighty-three seconds. Worth pinning because the two systems now touch
    /// the same creature on the same tick.
    /// </summary>
    [Fact]
    public void AWanderingPrisonerKeepsTheSameEightyThreeSecondClock()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = Prisoner(db, world, CellB);
        int captiveId = captive.Id;

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 82 * Second; i++) sim.Tick();
        Assert.NotNull(world.CreatureById(captiveId));

        for (int i = 0; i < 2 * Second; i++) sim.Tick();
        Assert.Null(world.CreatureById(captiveId));
    }

    // ---- the bird (ADR-0074, Q195) ----------------------------------------

    /// <summary>
    /// Makes a prisoner that arrives already hungry, which is the ordinary case
    /// and not a contrivance: <c>HungerNeed</c> accumulates over a creature's
    /// whole life and is paid off only by eating, so anything the imps drag in
    /// after three minutes on the map is hungry before it lands. It has to be
    /// set up rather than waited for, because a Goblin's HungerRate is 180 game
    /// seconds and a cell turns it to bone in 83.
    /// </summary>
    private static CreatureState HungryPrisoner(
        GameDatabase db, WorldState world, TilePos cell)
    {
        CreatureState captive = Prisoner(db, world, cell);
        captive.HungerNeed = EatingRules.HungerFill(db, db.CreatureById(GoblinId), captive.Level);
        return captive;
    }

    private static ObjectState Chicken(GameDatabase db, WorldState world, TilePos at) =>
        world.SpawnObject(
            db, HatcheryRules.ChickenObjectId, Keeper, (at.Y * world.Width) + at.X);

    [Fact]
    public void AHungryPrisonerWalksToAChickenAndEatsIt()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = HungryPrisoner(db, world, CellA);
        ObjectState bird = Chicken(db, world, CellC);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 10 * Second; i++) sim.Tick();

        // The bird is gone, the hunger is paid off, and the Goblin is
        // HpFromChicken better for it - capped at the missing health, and a
        // captive starts at a sixth of its maximum so there is room for all.
        //
        // The gain is read off the meal rather than off the clock, because the
        // prison keeps draining the whole time: eating buys health, it does not
        // pause the countdown (ADR-0064).
        Assert.Null(world.ObjectById(bird.Id));
        Assert.Equal(0, captive.HungerNeed);
        Assert.Contains(sink.Events, e =>
            e.Kind == "prisoner_ate"
            && e.Detail.Contains("gained=" + db.CreatureById(GoblinId).HpFromChicken));

        // It walked to the bird rather than eating from where it stood - read
        // off the meal again, because a fed prisoner goes straight back to
        // shuffling and will not be standing there when the run ends.
        Assert.Contains(sink.Events, e =>
            e.Kind == "prisoner_ate" && e.Detail.Contains("at=(6,5)"));
        Assert.Contains(sink.Events, e => e.Kind == "prisoner_food_chosen");

        // And it is still a prisoner in a prison state throughout: 117, 118 and
        // 119 carry the same label, group tag and category as 114, 115 and 116.
        Assert.Equal(CreatureActivity.Imprisoned, captive.Activity);
        Assert.True(PrisonRules.IsImprisoned(captive));
        Assert.InRange(captive.Tile.X, 4, 6);
        Assert.InRange(captive.Tile.Y, 3, 5);
    }

    [Fact]
    public void OneBirdPaysOffTheWholeOfHungerFillInACell()
    {
        // The prison's own number, written down two sittings before anything
        // could call it: DK2's state 118 passes the level-scaled HungerFill to
        // the routine its hatchery state passes a literal 1 to. A prisoner that
        // has gone four helpings without eating is filled by one chicken.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureDef goblin = db.CreatureById(GoblinId);
        int fill = EatingRules.HungerFill(db, goblin, 1);

        CreatureState captive = Prisoner(db, world, CellA);
        captive.HungerNeed = 4 * fill;
        Chicken(db, world, CellA);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 4 * Second; i++) sim.Tick();

        Assert.Equal(3 * fill, captive.HungerNeed);
    }

    [Fact]
    public void ARaceForOneChickenAndOnlyOnePrisonerEats()
    {
        // The owner watched exactly this (Q172): "they rush toward that chicken,
        // the first one to reach it eats it". Nothing reserves the bird in the
        // original and nothing reserves it here - the loser arrives, finds its
        // tile empty, and goes back to looking.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState near = HungryPrisoner(db, world, CellB);
        CreatureState far = HungryPrisoner(db, world, CellA);
        ObjectState bird = Chicken(db, world, CellC);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 10 * Second; i++) sim.Tick();

        Assert.Null(world.ObjectById(bird.Id));

        // Exactly one meal was served.
        Assert.Single(sink.Events, e => e.Kind == "prisoner_ate");

        // And it was the nearer of the two: (5,4) is one step from (6,5), (4,3)
        // is two. Straight-line squared distance, which is the original's
        // ranking and not a path length.
        Assert.Equal(0, near.HungerNeed);
        Assert.True(far.HungerNeed > 0);

        // The loser noticed, rather than standing over an empty tile for ever.
        Assert.Contains(sink.Events, e =>
            e.Kind == "prisoner_food_lost" && e.Detail.Contains("creature=" + far.Id));
    }

    [Fact]
    public void AHungryPrisonerWithNothingToEatGrowsAngry()
    {
        // AngerNoFood is 100 for a Goblin and what lands is 25. The creature
        // loader divides every angerNo* field by DK2's four ticks a game second
        // as it loads it, so what the engine holds is anger per TICK - and the
        // prison adds one of those per failed search rather than one per tick.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = HungryPrisoner(db, world, CellA);

        Assert.Equal(25, PrisonRules.AngerPerFailedFoodSearch(db.CreatureById(GoblinId)));
        Assert.Equal(0, captive.Anger);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 5 * Second; i++) sim.Tick();

        // One attempt a game second, five seconds, five helpings of 25.
        Assert.Equal(5, sink.Events.Count(e => e.Kind == "prisoner_no_food"));
        Assert.Equal(5 * 25, captive.Anger);
    }

    [Fact]
    public void AChickenOutsideTheCellIsNotFood()
    {
        // The containment ADR-0073 found in the wander applies to the search as
        // well, and for the same reason: the original walks the room's own tile
        // chain, so a tile outside the room is never a candidate in the first
        // place. A bird on the corridor is not a bird a prisoner can see.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = HungryPrisoner(db, world, CellA);
        ObjectState bird = Chicken(db, world, Floor);

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 10 * Second; i++) sim.Tick();

        Assert.NotNull(world.ObjectById(bird.Id));
        Assert.True(captive.HungerNeed > 0);
        Assert.InRange(captive.Tile.X, 4, 6);
        Assert.InRange(captive.Tile.Y, 3, 5);
    }

    [Fact]
    public void APrisonerOnItsWayToAChickenDoesNotAlsoShuffle()
    {
        // The original asks about hunger FIRST and wanders only when the answer
        // is no - one handler, in that order - so the walk to the bird is never
        // interrupted by a random sideways step.
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        HungryPrisoner(db, world, CellA);
        Chicken(db, world, CellC);

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 10 * Second; i++) sim.Tick();

        List<SimEvent> events = [.. sink.Events];
        int chosen = events.FindIndex(e => e.Kind == "prisoner_food_chosen");
        int ate = events.FindIndex(e => e.Kind == "prisoner_ate");
        Assert.True(chosen >= 0 && ate > chosen);

        Assert.DoesNotContain(
            events.GetRange(chosen, ate - chosen),
            e => e.Kind == "prisoner_stepped");
    }

    [Fact]
    public void AFedPrisonerStillWithersAndStillTurnsToBone()
    {
        // A chicken buys time and nothing else. The prison's drain is untouched
        // by eating, so the extra health is extra seconds and then the same
        // Skeleton (ADR-0064, ADR-0071).
        GameDatabase db = Db;
        WorldState world = World(db);
        Prison(db, world);
        CreatureState captive = HungryPrisoner(db, world, CellA);
        Chicken(db, world, CellA);
        int captiveId = captive.Id;

        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 5 * Second; i++) sim.Tick();
        Assert.Equal(0, captive.HungerNeed);

        for (int i = 0; i < 400 * Second; i++) sim.Tick();

        Assert.Null(world.CreatureById(captiveId));
        Assert.Contains(world.Creatures, c =>
            c.CreatureTypeId == SkeletonId && c.OwnerPlayerId == Keeper);
    }
}
