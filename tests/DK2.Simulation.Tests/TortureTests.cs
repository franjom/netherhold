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

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null, TriggerTree? triggers = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(),
            new CreatureMovementSystem(),
            new PrisonSystem(),
            new TortureSystem(),
        ], triggers);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static int Index(WorldState world, TilePos at) => (at.Y * world.Width) + at.X;

    private static CreatureState Captive(GameDatabase db, WorldState world)
    {
        CreatureState captive = world.SpawnCreature(db, GoblinId, Rival, Cell);
        // Freshly knocked out, as an imp's prisoner is (Q726).
        Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, db), captive));
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), captive, Keeper, Index(world, Cell)));
        return captive;
    }

    private static void Carry(GameSimulation sim, CreatureState creature, TilePos to)
    {
        sim.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, to)));
        sim.Tick();
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
        // Stunned with the jailer's word kept; 255's end clears it (0x004811E5, ADR-0616).
        Assert.Equal(255, captive.Dk2StateId);
        Assert.True(PrisonRules.IsImprisoned(captive));
        var status = new GameSimulation(db, world, 1, [new CreatureStatusSystem()]);
        while (captive.Dk2StateId == 255) status.Tick();
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

    // A wheel over a coals trough on the chamber's rim: (7, 3) is not a floor slot, so a wall wheel.
    // x + y = 10 is even, so the victim turns back 6 a turn.
    private static readonly TilePos RimWheel = new(7, 3);

    private static ViewEffectRequest[] TroughEffects(ListEventSink sink) =>
        sink.Events.Where(e => e.EffectRequest is { } r && r.EffectId.Value is TortureRules.CoalsEffectId or TortureRules.WaterEffectId)
            .Select(e => e.EffectRequest!.Value).ToArray();

    private static (GameDatabase Db, WorldState World, CreatureState Captive) OnTheRimWheel(byte trough)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        world.SpawnObject(db, trough, Keeper, Index(world, RimWheel)).AutoPlaced = true;
        world.SpawnObject(db, TortureRules.WheelObjectId, Keeper, Index(world, RimWheel)).AutoPlaced = true;
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, RimWheel)));
        captive.Health = 1500;
        return (db, world, captive);
    }

    [Fact]
    public void TheStrapTurnsAWallWheelsVictimOneStep_AndEachTurnAnother_PhaseInferred()
    {
        (GameDatabase db, WorldState world, CreatureState captive) = OnTheRimWheel(TortureRules.CoalsTroughObjectId);
        Assert.Equal(0x800 - 6, captive.TortureRoll);
        GameSimulation sim = Simulation(db, world);

        sim.Run(3);

        Assert.Equal(0x800 - 24, captive.TortureRoll);
    }

    /// <summary><c>0x00499300</c>: in the roll band, once a game second, the coals raise 274 at the
    /// trough's point, owned by the victim's own keeper, with its heading.</summary>
    [Fact]
    public void TheCoalsUnderAWallWheelGlowOnceASecondWhileTheVictimIsInTheBand()
    {
        (GameDatabase db, WorldState world, CreatureState captive) = OnTheRimWheel(TortureRules.CoalsTroughObjectId);
        captive.TortureRoll = TortureRules.TroughEffectEndRoll - 1;
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        // 0x45F down to 0x3A0 at 6 a turn is 33 turns in the band: 8 or 9 whole seconds.
        sim.Run(40);

        ViewEffectRequest[] effects = TroughEffects(sink);
        Assert.InRange(effects.Length, 8, 9);
        RoomInstance room = RoomIndex.RoomAt(world, db, Index(world, RimWheel))!;
        ObjectState coals = world.Objects.Single(o => o.ObjectTypeId == TortureRules.CoalsTroughObjectId);
        (int x, int y) = AutoFurnitureRules.FurniturePosition(world, db, room, coals);
        Assert.All(effects, e =>
        {
            Assert.Equal(TortureRules.CoalsEffectId, e.EffectId.Value);
            Assert.Equal(Rival, e.Owner.Value);
            Assert.Equal((x, y), (e.Position.X.Raw, e.Position.Y.Raw));
            Assert.Equal((ushort)captive.Heading, e.Heading.Value);
        });
        Assert.All(sink.Events.Where(e => e.EffectRequest?.EffectId.Value == TortureRules.CoalsEffectId),
            e => Assert.Equal(0, (e.Tick.Value + 1) % SimulationTiming.OriginalTicksPerGameSecond));
    }

    [Fact]
    public void AWaterTroughRaisesTheWaterEffect_AndOutsideTheBandNothing()
    {
        (GameDatabase db, WorldState world, CreatureState captive) = OnTheRimWheel(TortureRules.WaterTroughObjectId);
        captive.TortureRoll = TortureRules.TroughEffectFirstRoll - 1;
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        sim.Run(40);
        Assert.Empty(TroughEffects(sink));

        captive.TortureRoll = TortureRules.TroughEffectFirstRoll + 30;
        sim.Run(8);
        Assert.NotEmpty(TroughEffects(sink));
        Assert.All(TroughEffects(sink), e => Assert.Equal(TortureRules.WaterEffectId, e.EffectId.Value));
    }

    [Fact]
    public void WithBothTroughsUnderTheWheelTheWaterIsFound()
    {
        (GameDatabase db, WorldState world, CreatureState captive) = OnTheRimWheel(TortureRules.CoalsTroughObjectId);
        world.SpawnObject(db, TortureRules.WaterTroughObjectId, Keeper, Index(world, RimWheel)).AutoPlaced = true;
        captive.TortureRoll = TortureRules.TroughEffectFirstRoll + 30;
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        sim.Run(8);

        Assert.NotEmpty(TroughEffects(sink));
        Assert.All(TroughEffects(sink), e => Assert.Equal(TortureRules.WaterEffectId, e.EffectId.Value));
    }

    /// <summary>The roll, and reward 11's tag, are creature state: hashed, saved and copied.</summary>
    /// <summary>A victim strapped to the wheel survives a save: the device's occupancy is derived
    /// from the creature's state, and the torture fields (duration, target tile, roll) are saved
    /// (OpenKeeper #1042; audit 1000-1110 item 17), so the restored run tortures on exactly as the
    /// original does, hurt for hurt.</summary>
    [Fact]
    public void AStrappedVictimSurvivesASaveAndTheTortureGoesOnIdentically()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        GameSimulation original = Simulation(db, world);
        Carry(original, captive, Wheel);
        original.Run(7);
        Assert.True(TortureRules.IsBeingTortured(captive));

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db,
            [new HandSystem(), new CreatureMovementSystem(), new PrisonSystem(), new TortureSystem()]);
        CreatureState back = restored.World.CreatureById(captive.Id)!;
        Assert.True(TortureRules.IsBeingTortured(back));
        Assert.Equal((captive.TorturedDuration, captive.TortureTargetTile, captive.TortureRoll, captive.PrisonOwnerPlayerId),
            (back.TorturedDuration, back.TortureTargetTile, back.TortureRoll, back.PrisonOwnerPlayerId));

        int durationAtSave = captive.TorturedDuration;
        for (int t = 0; t < 200; t++)
        {
            original.Tick();
            restored.Tick();
            Assert.True(original.StateHash() == restored.StateHash(), $"restored torture diverged {t} ticks after loading");
        }
        Assert.Equal(captive.Health, back.Health);
        Assert.True(back.TorturedDuration > durationAtSave);   // the device went on working after the load
    }

    [Fact]
    public void TheRollAndTheSightTagAreHashedSavedAndCopied()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Rival, Cell);
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        goblin.TortureRoll = 0x3A5;
        ulong rolled = sim.StateHash();
        Assert.NotEqual(before, rolled);
        goblin.TortureSightPasses = 9;
        ulong passes = sim.StateHash();
        Assert.NotEqual(rolled, passes);
        goblin.TortureSightPlayerId = Keeper;
        ulong player = sim.StateHash();
        Assert.NotEqual(passes, player);
        goblin.TortureSightTileIndex = 77;
        Assert.NotEqual(player, sim.StateHash());

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        CreatureState back = restored.World.CreatureById(goblin.Id)!;
        Assert.Equal((0x3A5, 9, Keeper, 77), (back.TortureRoll, back.TortureSightPasses, back.TortureSightPlayerId, back.TortureSightTileIndex));
        Assert.Equal(sim.StateHash(), restored.StateHash());

        WorldState copy = World(db);
        copy.SpawnCreature(db, GoblinId, Rival, Cell);
        copy.CopyFrom(world);
        CreatureState copied = copy.CreatureById(goblin.Id)!;
        Assert.Equal((0x3A5, 9, Keeper, 77), (copied.TortureRoll, copied.TortureSightPasses, copied.TortureSightPlayerId, copied.TortureSightTileIndex));
    }

    [Fact]
    public void AWheelOnAFloorSlotRaisesNoTroughEffect()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        world.SpawnObject(db, TortureRules.CoalsTroughObjectId, Keeper, Index(world, Wheel)).AutoPlaced = true;
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        captive.Health = 1500;
        captive.TortureRoll = 0x400;
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        sim.Run(20);

        Assert.Empty(TroughEffects(sink));
        Assert.Equal(0x400, captive.TortureRoll);
    }

    /// <summary><c>0x004F2800</c>: message 57 to the old owner, the owner change, every grievance wiped,
    /// message 58 to the new owner, then creature event 9 on the convert.</summary>
    [Fact]
    public void AConversionTellsBothKeepers_WipesItsGrievances_AndFiresConverted()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        captive.Health = 1500;
        captive.Anger.Hunger = 9000;
        captive.Anger.Treatment = 4000;
        captive.TriggerId = EventCounterTree.Root;
        GameSimulation sim = Simulation(db, world, triggers: EventCounterTree.For(CreatureTriggerEvent.Converted));

        sim.Run(404);
        Assert.DoesNotContain(world.Messages.Entries, m => m.Kind is PlayerMessageKind.CreatureConvertedAway or PlayerMessageKind.CreatureConverted);
        Assert.Equal(0, world.Triggers.Read(EventCounterTree.CountFlag));
        sim.Run(1);

        Assert.Equal(Keeper, captive.OwnerPlayerId);
        Assert.Equal(
            new[] { (new PlayerId(Rival), PlayerMessageKind.CreatureConvertedAway), (new PlayerId(Keeper), PlayerMessageKind.CreatureConverted) },
            world.Messages.Entries
                .Where(m => m.Kind is PlayerMessageKind.CreatureConvertedAway or PlayerMessageKind.CreatureConverted)
                .Select(m => (m.PlayerId, m.Kind)));
        Assert.All(world.Messages.Entries.Where(m => m.Kind is PlayerMessageKind.CreatureConvertedAway or PlayerMessageKind.CreatureConverted),
            m => Assert.Equal(captive.Id, m.Data1));
        Assert.True(captive.Anger.IsCalm);
        Assert.Equal(1, world.Triggers.Read(EventCounterTree.CountFlag));
    }

    [Fact]
    public void AConvertedMessageRepeatsForEachCreature_TheLostOneDoesNot()
    {
        GameDatabase db = Db;
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]);
        world.Messages.Post(world, Keeper, PlayerMessageKind.CreatureConverted, 10, 0);
        world.Messages.Post(world, Keeper, PlayerMessageKind.CreatureConverted, 11, 0);
        world.Messages.Post(world, Rival, PlayerMessageKind.CreatureConvertedAway, 10, 0);
        world.Messages.Post(world, Rival, PlayerMessageKind.CreatureConvertedAway, 11, 0);

        Assert.Equal(2, world.Messages.Entries.Count(m => m.Kind == PlayerMessageKind.CreatureConverted));
        Assert.Single(world.Messages.Entries, m => m.Kind == PlayerMessageKind.CreatureConvertedAway);
        Assert.Equal((uint)SimulationTiming.TicksFromSeconds(30),
            world.Messages.Entries.First(m => m.Kind == PlayerMessageKind.CreatureConverted).Timeout);
        Assert.Equal((uint)SimulationTiming.TicksFromSeconds(240),
            world.Messages.Entries.Single(m => m.Kind == PlayerMessageKind.CreatureConvertedAway).Timeout);
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
    public void AFreshCaptiveDiesOnTheWheelBeforeItCanConvert()
    {
        // A prisoner wakes at 390 of 1500 (240 + 150, Q726): 39 acts of 10, well short of the 81 a conversion needs.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        Assert.Equal(390, captive.Health);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        sim.Run(4 + (5 * 38));
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

    /// <summary>State 130 fires creature event 8, Tortured, after it moves to 131 (<c>0x004842BE</c>):
    /// once per strapping, then not again while the victim stays on.</summary>
    [Fact]
    public void ACaptiveStrappedOnTheWheelFiresTorturedOnce()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        captive.TriggerId = EventCounterTree.Root;
        GameSimulation sim = Simulation(db, world, triggers: EventCounterTree.For(CreatureTriggerEvent.Tortured));

        Carry(sim, captive, Wheel);
        Assert.True(TortureRules.IsBeingTortured(captive));
        Assert.Equal(1, world.Triggers.Read(EventCounterTree.CountFlag));

        sim.Run(5 * SimulationTiming.TicksPerSecond);
        Assert.Equal(1, world.Triggers.Read(EventCounterTree.CountFlag));
    }

    [Fact]
    public void AnOwnGoblinStrappedOnTheWheelFiresTorturedToo()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, ChamberFloor);
        goblin.TriggerId = EventCounterTree.Root;
        GameSimulation sim = Simulation(db, world, triggers: EventCounterTree.For(CreatureTriggerEvent.Tortured));

        Carry(sim, goblin, Wheel);

        Assert.True(TortureRules.IsBeingTortured(goblin));
        Assert.Equal(1, world.Triggers.Read(EventCounterTree.CountFlag));
    }

    [Fact]
    public void ACaptiveDroppedOffTheDeviceFiresNoTortured()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        captive.TriggerId = EventCounterTree.Root;
        GameSimulation sim = Simulation(db, world, triggers: EventCounterTree.For(CreatureTriggerEvent.Tortured));

        Carry(sim, captive, ChamberFloor);

        Assert.False(TortureRules.IsBeingTortured(captive));
        Assert.Equal(0, world.Triggers.Read(EventCounterTree.CountFlag));
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
    public void AnOwnImpDroppedOnTheWheelIsNotStrapped()
    {
        // 76's keeper-worker switch, Torture Chamber case 0x0046F034: no 130 for type id 1.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, db.CreatureByName("Imp")!.CreatureId, Keeper, ChamberFloor);

        Assert.False(TortureRules.TryStrapOwn(new StateHookContext(world, db), imp, Index(world, Wheel)));
        Assert.False(TortureRules.IsBeingTortured(imp));
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
        captive.TriggerId = EventCounterTree.Root;
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink, EventCounterTree.For(CreatureTriggerEvent.Tortured));

        world.RemoveObject(world.Objects.Single(o => o.TileIndex == Index(world, Wheel)).Id);
        sim.Run(1);

        // 136 -> 130 -> 131 straps it on again, and 130 fires Tortured again (0x004842BE).
        Assert.Equal(1, world.Triggers.Read(EventCounterTree.CountFlag));
        Assert.True(TortureRules.IsBeingTortured(captive));
        Assert.Equal(beside, captive.Tile);
        Assert.Equal(TortureRules.TorturedState, captive.Dk2StateId);
        Assert.Equal(Keeper, captive.PrisonOwnerPlayerId);
        Assert.Contains(sink.Events, e => e.Kind == "restrapped");
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void ANeighbouringTilesChangeTakesTheVictimOffAndBackOnItsWheelOnce()
    {
        // 0x004E44E0's Torture Chamber arm: the creature stopped and kicked to 136, which gets back
        // onto the free wheel under it (ADR-0406). Its claim is renewed, so it does not repeat.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        Assert.True(TortureRules.TryStrap(new StateHookContext(world, db), captive, Keeper, Index(world, Wheel)));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Run(1);

        ref TileState east = ref world.Tile(10, 3);
        (byte before, PlayerId owner) = (east.TerrainId, east.OwnerPlayerId);
        east.TerrainId = Dk2TerrainFixture.ReinforcedWall;
        SlabChain.Changed(world, db, Index(world, new TilePos(10, 3)), before, owner);
        sim.Run(6);

        Assert.Single(sink.Events, e => e.Kind == "restrapped");
        Assert.True(TortureRules.IsBeingTortured(captive));
        Assert.Equal(Wheel, captive.Tile);
        Assert.Equal(TortureRules.TorturedState, captive.Dk2StateId);
    }

    [Fact]
    public void ANeighbouringTilesChangeLeavesThePrisonsCaptivesAlone()
    {
        // The Prison has SpecialWalls and no arm in 0x004E44E0's table.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState captive = Captive(db, world);
        long stamp = captive.HeldPlaceStamp;

        ref TileState west = ref world.Tile(1, 3);
        (byte before, PlayerId owner) = (west.TerrainId, west.OwnerPlayerId);
        west.TerrainId = Dk2TerrainFixture.ReinforcedWall;
        SlabChain.Changed(world, db, Index(world, new TilePos(1, 3)), before, owner);

        Assert.True(captive.HeldPlace.IsHeld);
        Assert.Equal(stamp, captive.HeldPlaceStamp);
    }

    /// <summary>One neighbour, one kick: 0x004E44E0 steps through the kicked holder's own zeroed link,
    /// so only the room's head, its latest claimant, goes (0x004E5E00).</summary>
    [Fact]
    public void ANeighbouringTilesChangeKicksOnlyTheLatestClaimant()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomRules.Place(db, ref world.Tile(11, 7), db.Room(RoomTypeId.Library), Keeper);
        int at = Index(world, new TilePos(11, 7));
        CreatureState first = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(11, 7));
        CreatureState second = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(11, 7));
        (first.HeldPlace, first.HeldPlaceSerial) = (new RoomPlace(at, RoomCapacityRules.Units), 1);
        (second.HeldPlace, second.HeldPlaceSerial) = (new RoomPlace(at, RoomCapacityRules.Units), 2);

        ref TileState east = ref world.Tile(12, 7);
        (byte before, PlayerId owner) = (east.TerrainId, east.OwnerPlayerId);
        east.TerrainId = Dk2TerrainFixture.ReinforcedWall;
        SlabChain.Changed(world, db, Index(world, new TilePos(12, 7)), before, owner);

        Assert.Equal(SlabChain.KickedMark, second.HeldPlaceStamp);
        Assert.NotEqual(SlabChain.KickedMark, first.HeldPlaceStamp);
    }

    [Theory]
    [InlineData(RoomTypeId.Library, true)]
    [InlineData(RoomTypeId.TrainingRoom, true)]
    [InlineData(RoomTypeId.Workshop, true)]
    [InlineData(RoomTypeId.TortureChamber, true)]
    [InlineData(RoomTypeId.Casino, true)]
    [InlineData(RoomTypeId.Prison, false)]     // SpecialWalls, no arm
    [InlineData(RoomTypeId.Temple, false)]
    [InlineData(RoomTypeId.Treasury, false)]
    public void ANeighbouringTilesChangeKicksOnlyTheFiveArmsOf0x004E44E0(RoomTypeId type, bool kicked)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomRules.Place(db, ref world.Tile(11, 7), db.Room(type), Keeper);
        CreatureState holder = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(11, 7));
        holder.HeldPlace = new RoomPlace(Index(world, new TilePos(11, 7)), RoomCapacityRules.Units);

        ref TileState east = ref world.Tile(12, 7);
        (byte before, PlayerId owner) = (east.TerrainId, east.OwnerPlayerId);
        east.TerrainId = Dk2TerrainFixture.ReinforcedWall;
        SlabChain.Changed(world, db, Index(world, new TilePos(12, 7)), before, owner);

        Assert.Equal(kicked, holder.HeldPlaceStamp == SlabChain.KickedMark);
    }

    [Fact]
    public void ARoomTakenByARivalKicksItsHolders()
    {
        // Room_TransferOwnership 0x004E5650 runs the type's slot +0x0D (ADR-0406); a Treasury, whose
        // fixture terrain is Attackable, stands in for any room.
        GameDatabase db = Db;
        WorldState world = World(db);
        RoomRules.Place(db, ref world.Tile(11, 7), db.Room(RoomTypeId.Treasury), Keeper);
        Simulation(db, world).Run(50);
        int tile = Index(world, new TilePos(11, 7));
        for (int i = 0; i < 5000 && world.TileAtIndex(tile).OwnerPlayerId == Keeper; i++)
            RoomHealthRules.Convert(world, db, new TilePos(11, 7), Rival);
        RoomInstance taken = RoomIndex.RoomAt(world, db, tile)!;

        Assert.Equal(Rival, taken.OwnerPlayerId);
        Assert.Equal(world.CurrentTick.Value, SlabChain.HoldersStamp(world, taken));
        Assert.NotEqual(0, world.CurrentTick.Value);
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
