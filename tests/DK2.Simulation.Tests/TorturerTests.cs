// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Mistress's Torture job (ADR-0196): 7 into 127, a walk in 6 to a victim resuming
/// 128, and the whip, 128 and 129 through idle; with no victim, a free device and 130, 131;
/// with neither, idle.</summary>
public class TorturerTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 1;
    private const byte GoblinId = 7;
    private const byte MistressId = Dk2TerrainFixture.MistressCreatureId;
    private const int Walk = 6;

    private static readonly TilePos Cell = new(3, 3);
    private static readonly TilePos Wheel = new(8, 3);
    private static readonly TilePos Chair = new(9, 4);
    private static readonly TilePos Outside = new(11, 7);

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    /// <summary>A 3x3 prison at (2..4, 2..4) and a 3x3 Torture Chamber at (7..9, 2..4) with a
    /// wheel in its middle, on paved ground the keeper owns.</summary>
    private static WorldState World(GameDatabase db, bool chair = false, bool coals = true)
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

        // Auto-placed, as the chamber's own would be: they are its places (ADR-0205). The
        // coals stand in for the wall furniture a walled chamber gets, the one place more a
        // Mistress with no victim asks for (0x004F2B80).
        world.SpawnObject(db, TortureRules.WheelObjectId, Keeper, Index(world, Wheel)).AutoPlaced = true;
        if (coals)
            world.SpawnObject(db, Dk2TerrainFixture.TortureCoalsObjectId, Keeper, Index(world, new TilePos(7, 2))).AutoPlaced = true;
        if (chair)
            world.SpawnObject(db, TortureRules.ElectricChairObjectId, Keeper, Index(world, Chair)).AutoPlaced = true;
        return world;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(),
            new CreatureMovementSystem(),
            new CreatureJobSystem(),
            new PrisonSystem(),
            new TortureSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static int Index(WorldState world, TilePos at) => (at.Y * world.Width) + at.X;

    private static CreatureState Victim(GameDatabase db, WorldState world)
    {
        CreatureState captive = world.SpawnCreature(db, GoblinId, Rival, Cell);
        Assert.True(PrisonRules.Imprison(world, db, captive, Keeper, Index(world, Cell)));
        Assert.True(TortureRules.TryStrap(world, db, captive, Keeper, Index(world, Wheel)));
        captive.Health = 1500;
        return captive;
    }

    [Fact]
    public void AMistressWalksToTheVictimAndWhipsItThroughIdleWithoutHurtingIt()
    {
        // Train is refused for want of a room, so her table falls through to Torture: 7 into
        // 127, 6 to the victim resuming 128, then 128 -> 1 -> 129 and back, the whip hurting
        // nothing. The victim's health moves by the wheel alone.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState victim = Victim(db, world);
        CreatureState mistress = world.SpawnCreature(db, MistressId, Keeper, Outside);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        var seen = ImpStateTrace.Seen(sim, mistress, 30 * SimulationTiming.TicksPerSecond,
            () => sink.Events.Count(e => e.Kind == "whipped") >= 3);

        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("job=Torture"));
        Assert.Contains((Walk, TortureRules.TorturerChoosingState, TortureRules.TorturerFacingState), seen);
        Assert.Contains(seen, s => s.State == TortureRules.TorturerWhippingState);
        Assert.Contains(sink.Events, e => e.Kind == "torturer_to_victim" && e.Detail.Contains($"victim={victim.Id}"));
        Assert.All(sink.Events.Where(e => e.Kind == "whipped"), e => Assert.Contains($"victim={victim.Id}", e.Detail));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");

        int acts = sink.Events.Count(e => e.Kind == "tortured");
        Assert.Equal(1500 - (10 * acts), victim.Health);
    }

    [Fact]
    public void TheWhipLastsSixTurnsAndComesRoundEverySeventh()
    {
        // Action 63 ends on the sixth turn after it began, with its effect, and 129 goes to 128
        // on that turn (ours: the action runs before the state); 128 starts the next whip on
        // the turn after.
        GameDatabase db = Db;
        WorldState world = World(db);
        Victim(db, world);
        world.SpawnCreature(db, MistressId, Keeper, Outside);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        for (int i = 0; i < 60 * SimulationTiming.TicksPerSecond
                        && sink.Events.Count(e => e.Kind == "whip_started") < 3; i++) sim.Tick();

        long[] starts = sink.Events.Where(e => e.Kind == "whip_started").Select(e => e.Tick.Value).ToArray();
        long[] ends = sink.Events.Where(e => e.Kind == "whipped").Select(e => e.Tick.Value).ToArray();
        long turn = SimulationTiming.TicksPerSecond / SimulationTiming.OriginalTicksPerGameSecond;
        Assert.Equal(3, starts.Length);
        Assert.Equal(TortureRules.WhipTurns * turn, ends[0] - starts[0]);
        Assert.Equal(7 * turn, starts[1] - starts[0]);
        Assert.Equal(7 * turn, starts[2] - starts[1]);
    }

    [Fact]
    public void AVictimIsTakenOnlyWhileItsWhipperIsInOneTwentyNine()
    {
        // 127 skips a victim that a torturer of the keeper's is whipping: in 129 with it as the
        // target. Facing it in 128 does not count, so a second Mistress may join then.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState victim = Victim(db, world);
        CreatureState first = world.SpawnCreature(db, MistressId, Keeper, new TilePos(7, 3));
        CreatureState second = world.SpawnCreature(db, MistressId, Keeper, new TilePos(9, 3));
        CreatureStateMachine.Begin(first, TortureRules.TorturerWhippingState);
        first.TortureTargetTile = Index(world, Wheel);

        Assert.Null(TorturerStep.UnattendedVictim(world, db, second));

        CreatureStateMachine.Release(first);
        CreatureStateMachine.Begin(first, TortureRules.TorturerFacingState);
        Assert.Same(victim, TorturerStep.UnattendedVictim(world, db, second));

        first.OwnerPlayerId = Rival;
        CreatureStateMachine.Release(first);
        CreatureStateMachine.Begin(first, TortureRules.TorturerWhippingState);
        Assert.Same(victim, TorturerStep.UnattendedVictim(world, db, second));
    }

    [Fact]
    public void WithNoVictimAMistressStrapsHerselfOnAndIsNeitherHurtNorConverted()
    {
        // 127 finds no victim and walks onto the free wheel resuming 130; 130 straps her on,
        // 131. Her TortureHpChange and TortureMoodChange are 0, and she is her keeper's own.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState mistress = world.SpawnCreature(db, MistressId, Keeper, Outside);
        int health = mistress.Health;
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        var seen = ImpStateTrace.Seen(sim, mistress, 30 * SimulationTiming.TicksPerSecond,
            () => TortureRules.IsBeingTortured(mistress));
        Assert.Contains((Walk, TortureRules.TorturerChoosingState, TortureRules.StrappingState), seen);
        Assert.Equal(TortureRules.TorturedState, mistress.Dk2StateId);
        Assert.Equal(Wheel, mistress.Tile);
        Assert.False(PrisonRules.IsImprisoned(mistress));

        sim.Run(400 * SimulationTiming.TicksPerSecond / SimulationTiming.OriginalTicksPerGameSecond);

        Assert.Equal(health, mistress.Health);
        Assert.Equal(Keeper, mistress.OwnerPlayerId);
        Assert.True(TortureRules.IsBeingTortured(mistress));
        Assert.Contains(sink.Events, e => e.Kind == "tortured" && e.Detail.Contains($"creature={mistress.Id} "));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "converted");
    }

    [Fact]
    public void WithNoVictimAMistressPassesOverAChamberWithOnePlace()
    {
        // 0x004F2B80, the finder with no victim, asks the capacity test for two places: a
        // chamber whose only furniture is one wheel has one (ADR-0205).
        GameDatabase db = Db;
        WorldState world = World(db, coals: false);
        CreatureState mistress = world.SpawnCreature(db, MistressId, Keeper, Outside);
        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(30 * SimulationTiming.TicksPerSecond);

        Assert.False(TortureRules.IsBeingTortured(mistress));
        Assert.Contains(sink.Events, e => e.Kind == "job_refused" && e.Detail.Contains($"creature={mistress.Id} ")
            && e.Detail.Contains("job=Torture") && e.Detail.Contains("reason=no_room"));
    }

    [Fact]
    public void WithAVictimTheChamberIsChosenWhateverItsPlaces()
    {
        // 0x004F29A0, the finder while the keeper has a victim, asks no capacity: the victim
        // fills the wheel's one place and the Mistress still comes to whip it.
        GameDatabase db = Db;
        WorldState world = World(db, coals: false);
        CreatureState victim = Victim(db, world);
        CreatureState mistress = world.SpawnCreature(db, MistressId, Keeper, Outside);
        var sink = new ListEventSink();
        Simulation(db, world, sink).Run(30 * SimulationTiming.TicksPerSecond);

        Assert.Contains(sink.Events, e => e.Kind == "torturer_to_victim" && e.Detail.Contains($"victim={victim.Id}"));
        Assert.Contains(sink.Events, e => e.Kind == "whipped");

        // Her states, 127 to 129, are no claiming category: the victim holds the one place.
        RoomInstance chamber = RoomIndex.RoomAt(world, db, Index(world, Wheel))!;
        Assert.True(EatingRules.Contains(chamber, (mistress.Tile.Y * world.Width) + mistress.Tile.X));
        Assert.Equal(1, RoomCapacityRules.Occupants(world, chamber));
    }

    [Fact]
    public void WithNoVictimAndNoFreeDeviceTheJobEndsInIdle()
    {
        // The only wheel is taken; 127 returns 0, which is idle.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState victim = Victim(db, world);
        CreatureState whipper = world.SpawnCreature(db, MistressId, Keeper, Outside);
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        for (int i = 0; i < 60 * SimulationTiming.TicksPerSecond && whipper.Dk2StateId != TortureRules.TorturerWhippingState; i++)
            sim.Tick();

        CreatureState second = world.SpawnCreature(db, MistressId, Keeper, Outside);
        for (int i = 0; i < 20 * SimulationTiming.TicksPerSecond
                        && !sink.Events.Any(e => e.Kind == "job_lost" && e.Detail.Contains($"creature={second.Id} ")); i++)
            sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "job_started" && e.Detail.Contains($"creature={second.Id} "));
        Assert.DoesNotContain(sink.Events, e => e.Kind.StartsWith("torturer_to") && e.Detail.Contains($"creature={second.Id} "));
        Assert.Equal(CreatureActivity.Idle, second.Activity);
        Assert.True(TortureRules.IsBeingTortured(victim));
    }

    [Fact]
    public void AMistressWhoseVictimIsTakenAwayStopsWhippingAndTakesTheFreedWheel()
    {
        // 129 finds no victim and goes to 128, which fails to idle; her next choice takes the
        // job again, and with no victim 127 walks her onto the wheel.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState victim = Victim(db, world);
        CreatureState mistress = world.SpawnCreature(db, MistressId, Keeper, Outside);
        GameSimulation sim = Simulation(db, world);
        for (int i = 0; i < 60 * SimulationTiming.TicksPerSecond && mistress.Dk2StateId != TortureRules.TorturerWhippingState; i++)
            sim.Tick();

        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, victim.Id)));
        Assert.True(HandRules.IsHeld(victim));
        sim.Run(15 * SimulationTiming.TicksPerSecond);

        Assert.True(sink.Events.Count(e => e.Kind == "whip_started") == 0);
        Assert.Contains(sink.Events, e => e.Kind == "torturer_to_device" && e.Detail.Contains($"creature={mistress.Id} "));
        Assert.True(TortureRules.IsBeingTortured(mistress));
        Assert.Equal(Wheel, mistress.Tile);
    }

    [Fact]
    public void AnElectricChairTurnsAwayACreatureThatCannotBeElectrocuted()
    {
        // 131 on a chair wants CanBeElectrocuted (definition +0x686 bit 19); the wheel does not.
        GameDatabase db = Db;
        CreatureDef goblin = db.CreatureById(GoblinId);
        WorldState world = World(db, chair: true);
        ObjectState chair = world.Objects.Single(o => o.ObjectTypeId == TortureRules.ElectricChairObjectId);
        ObjectState wheel = world.Objects.Single(o => o.ObjectTypeId == TortureRules.WheelObjectId);

        CreatureDef firefly = goblin with { Flags = goblin.Flags & ~CreatureFlags.CanBeElectrocuted };
        Assert.False(TortureRules.Holds(chair, firefly));
        Assert.True(TortureRules.Holds(wheel, firefly));
        Assert.True(TortureRules.Holds(chair, goblin with { Flags = goblin.Flags | CreatureFlags.CanBeElectrocuted }));
    }

    [Fact]
    public void WhileTheKeeperHasAVictimTheMistressGoesToTheChamberHoldingIt()
    {
        // 0x00483AE0 asks 0x004F29A0 first, which takes only a chamber with occupants.
        GameDatabase db = Db;
        WorldState world = World(db);
        for (int y = 6; y <= 8; y++)
        for (int x = 1; x <= 3; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.TortureChamber), Keeper);
        Victim(db, world);
        CreatureState mistress = world.SpawnCreature(db, MistressId, Keeper, new TilePos(2, 5));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        sim.Run(2 * SimulationTiming.TicksPerSecond);

        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("job=Torture"));
        Assert.Equal(Index(world, new TilePos(8, 3)), mistress.JobTileIndex);
    }
}
