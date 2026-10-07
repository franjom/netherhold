// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A captive on a torture device (ADR-0193): strapped on through 120, 130 and 131, hurt
/// every fifth turn by the category 15 hook, and turned to its jailer's side once it has served
/// its level-scaled <c>TortureTimeToConvert</c>.</summary>
public class TortureTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 1;
    private const byte GoblinId = 7;

    private static readonly TilePos Cell = new(3, 3);
    private static readonly TilePos Wheel = new(8, 3);
    private static readonly TilePos ChamberFloor = new(7, 2);

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    /// <summary>A 3x3 prison at (2..4, 2..4) and a 3x3 Torture Chamber at (7..9, 2..4) with a
    /// wheel in its middle, on paved ground the keeper owns.</summary>
    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        for (int y = 2; y <= 4; y++)
        {
            for (int x = 2; x <= 4; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), Keeper);
            for (int x = 7; x <= 9; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.TortureChamber), Keeper);
        }

        world.SpawnObject(db, TortureRules.WheelObjectId, Keeper, Index(world, Wheel)).AutoPlaced = true;
        return world;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(),
            new CreatureMovementSystem(),
            new PrisonSystem(),
            new TortureSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static int Index(WorldState world, TilePos at) => (at.Y * world.Width) + at.X;

    private static CreatureState Captive(GameDatabase db, WorldState world)
    {
        CreatureState captive = world.SpawnCreature(db, GoblinId, Rival, Cell);
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), captive, Keeper, Index(world, Cell)));
        return captive;
    }

    private static void Carry(GameSimulation sim, CreatureState creature, TilePos to)
    {
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, to)));
    }

    [Fact]
    public void ACaptiveDroppedOnTheWheelIsStrappedToItAndStaysItsJailersCaptive()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        Carry(sim, captive, Wheel);

        Assert.True(TortureRules.IsBeingTortured(captive));
        Assert.Equal(TortureRules.TorturedState, captive.Dk2StateId);
        Assert.Equal(Keeper, captive.PrisonOwnerPlayerId);
        Assert.Equal(Rival, captive.OwnerPlayerId);
        Assert.Equal(Wheel, captive.Tile);
        Assert.Contains(sink.Events, e => e.Kind == "captive_strapped");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void ACaptiveDroppedOnTheChamberFloorOffTheDeviceGoesFree()
    {
        // 130 wants the device on the creature's own tile; elsewhere it fails to idle.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        GameSimulation sim = Simulation(db, world);

        Carry(sim, captive, ChamberFloor);

        Assert.False(TortureRules.IsBeingTortured(captive));
        Assert.False(PrisonRules.IsImprisoned(captive));
    }

    [Fact]
    public void TheWheelHurtsItsCaptiveTenEveryFifthTurnAndItsMoodIsLeftAlone()
    {
        // TortureHpChange -10 through the health routine, on turns 4, 9, 14 ... from the strap
        // (Q424). The mood applies only to a creature in its own keeper's chamber.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        captive.Health = 1000;
        GameSimulation sim = Simulation(db, world);

        sim.Run(4);
        Assert.Equal(1000, captive.Health);
        sim.Run(1);
        Assert.Equal(990, captive.Health);
        Assert.Equal(TortureRules.DurationPerAct, captive.TorturedDuration);
        sim.Run(10);
        Assert.Equal(970, captive.Health);
        Assert.Equal(0, captive.Anger.Treatment);
    }

    [Fact]
    public void AGoblinConvertsOnTheEightyFirstActAndJoinsItsJailer()
    {
        // 80 seconds is 320 turns of duration at level 1; four an act, so the 81st act passes
        // it, on turn 4 + 5 x 80 = 404. A full-health Goblin has lost 810 by then.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        captive.Health = 1500;
        captive.RescuePriorityMask = 0x8044;
        CreatureState resident = world.SpawnCreature(db, GoblinId, Keeper, Cell);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        sim.Run(404);
        Assert.Equal(Rival, captive.OwnerPlayerId);
        Assert.Equal((ushort)0x8044, captive.RescuePriorityMask);
        sim.Run(1);

        Assert.Equal(Keeper, captive.OwnerPlayerId);
        Assert.Equal(1500 - 810, captive.Health);
        Assert.Equal((ushort)0x8040, captive.RescuePriorityMask);
        Assert.Equal(new[] { captive.Id, resident.Id }, world.OwnedCreatures(Keeper).Select(c => c.Id));
        Assert.DoesNotContain(captive, world.OwnedCreatures(Rival));
        Assert.Equal(0, captive.TorturedDuration);
        Assert.False(PrisonRules.IsImprisoned(captive));
        Assert.False(TortureRules.IsBeingTortured(captive));
        Assert.Contains(sink.Events, e => e.Kind == "converted" && e.Detail.Contains($"to={Keeper}"));
    }

    [Fact]
    public void ACaptiveAtASixthOfItsHealthDiesOnTheWheelBeforeItCanConvert()
    {
        // A prisoner wakes at 250 of 1500: 25 acts of 10, well short of the 81 a conversion needs.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        Assert.Equal(250, captive.Health);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        sim.Run(4 + (5 * 24));
        Assert.Equal(10, captive.Health);
        sim.Run(1);

        Assert.Equal(0, captive.Health);
        Assert.Equal(Rival, captive.OwnerPlayerId);
        Assert.False(TortureRules.IsBeingTortured(captive));
        Assert.Contains(sink.Events, e => e.Kind == "tortured_to_death");

        // An enemy's death tells the keeper something (0x004F2370, ADR-0230): 3–10 are kept.
        string told = Assert.Single(sink.Events, e => e.Kind == "information").Detail;
        int reward = int.Parse(told[(told.IndexOf("reward=", StringComparison.Ordinal) + 7)..].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(reward, TortureInformationRules.Candidates(World(db), db.CreatureById(captive.CreatureTypeId), Rival, Keeper));   // before any was given
        Assert.Equal(reward != TortureInformationRules.Reveal, TortureInformationRules.Given(world, reward, Keeper, Rival));
    }

    [Fact]
    public void TheTimeServedCarriesOverFromOneStrappingToTheNext()
    {
        // TorturedDuration (creature+0x4F6) is zeroed only by a conversion.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        captive.Health = 1500;
        GameSimulation sim = Simulation(db, world);
        sim.Run(10);
        Assert.Equal(2 * TortureRules.DurationPerAct, captive.TorturedDuration);

        Carry(sim, captive, Cell);
        Assert.Equal(PrisonRules.DecidingState, captive.Dk2StateId);
        Carry(sim, captive, Wheel);
        Assert.Equal(2 * TortureRules.DurationPerAct, captive.TorturedDuration);
        Assert.True(TortureRules.IsBeingTortured(captive));
    }

    [Fact]
    public void AWheelWithSomeoneOnItTakesNobodyElse()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState first = Captive(db, world);
        CreatureState second = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), first, Keeper, Index(world, Wheel)));

        Assert.False(TortureRules.TryStrap(new StateHookContext(world, db), second, Keeper, Index(world, Wheel)));
        Assert.Null(TortureRules.FreeDeviceAt(world, db, Keeper, Index(world, Wheel), second.Id));
    }

    [Fact]
    public void AWheelThatIsNotTheRoomsFurnitureGivesNoPlaceSoTheStrapIsRefused()
    {
        // The chamber's places are its auto-placed devices (0x004E3ED0); a loose wheel is none,
        // so 130's claim is refused and the captive is left in 1 (ADR-0309).
        GameDatabase db = Db;
        WorldState world = World(db);
        world.Objects.Single(o => o.ObjectTypeId == TortureRules.WheelObjectId).AutoPlaced = false;
        CreatureState captive = Captive(db, world);

        Assert.False(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        Assert.Equal(CreatureStateMachine.Idle, captive.Dk2StateId);
        Assert.False(captive.HeldPlace.IsHeld);
    }

    [Fact]
    public void AVictimWhoseWheelIsGoneIsLetGo()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        GameSimulation sim = Simulation(db, world);

        world.RemoveObject(world.Objects.Single(o => o.ObjectTypeId == TortureRules.WheelObjectId).Id);
        sim.Run(1);

        Assert.False(TortureRules.IsBeingTortured(captive));
        Assert.False(PrisonRules.IsImprisoned(captive));
        Assert.Equal(CreatureStateMachine.Idle, captive.Dk2StateId);
    }

    [Fact]
    public void AVictimOutOfTheTortureStatesIsNotHurt()
    {
        // The hurt is the category 15 hook: it reads the state, not the device the victim is on.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        captive.Health = 1000;
        StatePlacement.Place(captive, CreatureStateMachine.Idle);

        Simulation(db, world).Run(20);

        Assert.Equal(1000, captive.Health);
    }

    [Fact]
    public void AnOwnGoblinDroppedOnTheWheelIsStrappedStaysItsOwnAndTakesTheMood()
    {
        // 77 goes to 130 in a Torture Chamber for any creature (0x0046F1F0); in its own keeper's
        // chamber the hook also applies TortureMoodChange, 1092 for a Goblin.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, ChamberFloor);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        Carry(sim, goblin, Wheel);
        Assert.True(TortureRules.IsBeingTortured(goblin));
        Assert.Equal(TortureRules.TorturedState, goblin.Dk2StateId);
        Assert.False(PrisonRules.IsImprisoned(goblin));
        Assert.Contains(sink.Events, e => e.Kind == "own_strapped");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");

        int health = goblin.Health;
        sim.Run(5);
        Assert.Equal(health - 10, goblin.Health);
        Assert.NotEqual(0, goblin.Anger.Treatment);
        Assert.Equal(Keeper, goblin.OwnerPlayerId);
    }

    [Fact]
    public void AWillingTortureeAnswersACallToArmsThroughState135_LeavingTheWheel()
    {
        // 0x004BDDA0: TortureVoluntary (+0x3EA bit 14) lifts the recruit's refusals and kicks it to 135
        // first (0x004BDF0E), whose leave from category 15 frees the device, then 190 (ADR-0400).
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, ChamberFloor);
        Carry(Simulation(db, world), goblin, Wheel);
        Assert.Equal(TortureRules.TorturedState, goblin.Dk2StateId);
        goblin.TortureVoluntary = true;
        world.Player(Keeper).Mana = 20000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, CallToArmsRules.SpellId, 3));
        var cta = new GameSimulation(db, world, 1,
            [new KeeperSpellSystem(), new CreatureMovementSystem(), new ShotSystem(), new CallToArmsSystem()]);

        cta.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, CallToArmsRules.SpellId, CreatureState.CentreOf(11), CreatureState.CentreOf(7))));

        Assert.True(goblin.HeedingCallToArms);
        Assert.False(TortureRules.IsBeingTortured(goblin));
    }

    [Fact]
    public void AnOwnGoblinWhoseWheelIsGoneGetsOffIdle()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, ChamberFloor);
        GameSimulation sim = Simulation(db, world);
        Carry(sim, goblin, Wheel);
        Assert.Equal(TortureRules.TorturedState, goblin.Dk2StateId);

        world.RemoveObject(world.Objects.Single(o => o.ObjectTypeId == TortureRules.WheelObjectId).Id);
        sim.Run(1);

        Assert.False(TortureRules.IsBeingTortured(goblin));
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
    }

    [Fact]
    public void AnOwnHornedReaperDroppedOnTheWheelIsNotStrapped()
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Goblin() with
        {
            CreatureId = 60, Name = "Horny", Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.IsHorny,
        });
        WorldState world = World(db);
        CreatureState reaper = world.SpawnCreature(db, 60, Keeper, ChamberFloor);
        GameSimulation sim = Simulation(db, world);

        Carry(sim, reaper, Wheel);

        Assert.False(TortureRules.IsBeingTortured(reaper));
    }

    [Fact]
    public void AVictimWhoseWheelIsGoneGetsOntoAFreeOneBesideItThroughOneThirtySix()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        var beside = new TilePos(Wheel.X, Wheel.Y - 1);
        world.SpawnObject(db, TortureRules.WheelObjectId, Keeper, Index(world, beside)).AutoPlaced = true;
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        world.RemoveObject(world.Objects.Single(o => o.TileIndex == Index(world, Wheel)).Id);
        sim.Run(1);

        Assert.True(TortureRules.IsBeingTortured(captive));
        Assert.Equal(beside, captive.Tile);
        Assert.Equal(TortureRules.TorturedState, captive.Dk2StateId);
        Assert.Equal(Keeper, captive.PrisonOwnerPlayerId);
        Assert.Contains(sink.Events, e => e.Kind == "restrapped");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void AFreeWheelOnlyDiagonallyBesideIsOutOfOneThirtySixsReach()
    {
        // 0x1800 against the squared distance: a side neighbour is 1, a diagonal 2.
        GameDatabase db = Db;
        WorldState world = World(db);
        world.SpawnObject(db, TortureRules.WheelObjectId, Keeper, Index(world, new TilePos(Wheel.X + 1, Wheel.Y - 1))).AutoPlaced = true;
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        GameSimulation sim = Simulation(db, world);

        world.RemoveObject(world.Objects.Single(o => o.TileIndex == Index(world, Wheel)).Id);
        sim.Run(1);

        Assert.False(TortureRules.IsBeingTortured(captive));
    }
}
