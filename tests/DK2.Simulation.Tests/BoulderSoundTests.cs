// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>The sounds of docs/research/sound-effects-objects.md section 3, all in the boulder's category (object 74's
/// <c>TRAP_BOULDER</c>) with its one handle at its position: <c>Boulder_Roll</c> (<c>0x004A0E30</c>) asks <c>0x14F</c> at
/// the start of every roll and <c>0x149</c> after a turn that leaves it whole, <c>Boulder_Strike</c> (<c>0x004A0930</c>)
/// <c>0x14D</c> or <c>0x14E</c> for a creature, <c>0x14A</c> or <c>0x15A</c> for a door, <c>Boulder_TerrainWear</c>
/// (<c>0x004A10E0</c>) <c>0x14B</c> on water and <c>0x14C</c> on lava, and <c>Boulder_Update</c> (<c>0x004A1390</c>)
/// <c>0xF4</c> when it breaks.</summary>
public sealed class BoulderSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const int Row = 5;

    private static (GameDatabase Db, WorldState World) Open(GameDatabase? db = null)
    {
        db ??= SoundFixture.Database(new Dictionary<byte, string> { [BoulderRules.ObjectId] = "TRAP_BOULDER" });
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 12), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = y != Row ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static ObjectState Rolling(GameDatabase db, WorldState world, int x, int y, int rollX = 0x1000, int offset = 0)
    {
        ObjectState boulder = world.SpawnObject(db, BoulderRules.ObjectId, Keeper, (y * world.Width) + x);
        boulder.ProcessState = BoulderRules.ProcessState;
        boulder.PosX = CreatureState.CentreOf(x) + offset;
        boulder.PosY = CreatureState.CentreOf(y);
        boulder.Health = db[VariableTypeId.BoulderInitialHealth];
        boulder.RollSpeed = db[VariableTypeId.BoulderSpeedTilesPerSecond] / SimulationTiming.OriginalTicksPerGameSecond;
        boulder.RollX = rollX;
        boulder.RollSignX = (sbyte)Math.Sign(rollX);
        return boulder;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, out ListEventSink events)
    {
        var sim = new GameSimulation(db, world, 1,
            [new CreatureStatusSystem(), new ShotSystem(), new BoulderSystem(), new TrapSystem(), new TrapRemovalSystem(), new DoorRemovalSystem()]);
        events = new ListEventSink();
        sim.ObserveWith(events);
        return sim;
    }

    private static ThingSoundRequest[] Heard(ListEventSink events, ThingSoundEvent sound) => Sounds(events).Where(s => s.Event == sound).ToArray();

    private static void AssertBoulder(ThingSoundRequest sound, ObjectState boulder, FpPosition3? at = null)
    {
        Assert.Equal("TRAP_BOULDER", sound.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, boulder.Id), sound.Source);
        Assert.Equal(at ?? new FpPosition3(new Fp32(boulder.PosX), new Fp32(boulder.PosY), new Fp32(0x1000)), sound.Position);
        Assert.True(sound.Voiced);
    }

    [Fact]
    public void ARollingBoulderAsks14fAtItsPositionAtTheStartOfEveryUpdateBeforeItMoves()
    {
        (GameDatabase db, WorldState world) = Open();
        ObjectState boulder = Rolling(db, world, 5, Row);
        var start = new FpPosition3(new Fp32(boulder.PosX), new Fp32(boulder.PosY), new Fp32(0x1000));
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Run(1);

        ThingSoundRequest roll = Assert.Single(Heard(events, ThingSoundEvent.BoulderRolling));
        Assert.Equal((ushort)0x14F, (ushort)roll.Event);
        AssertBoulder(roll, boulder, start);
        Assert.NotEqual(start.X.Raw, boulder.PosX);   // it has moved since

        sim.Run(2);
        Assert.Equal(3, Heard(events, ThingSoundEvent.BoulderRolling).Length);
    }

    [Fact]
    public void ATurnThatLeavesTheBoulderWholeAsksTheCrashAtWhereItTurnedAndOneThatBreaksItDoesNot()
    {
        (GameDatabase db, WorldState world) = Open();
        ObjectState boulder = Rolling(db, world, 17, Row);
        GameSimulation sim = Sim(db, world, out ListEventSink events);
        for (int i = 0; i < 12 && !events.Events.Any(e => e.Kind == "boulder_turned"); i++) sim.Run(1);

        Assert.Contains(events.Events, e => e.Kind == "boulder_turned");
        ThingSoundRequest crash = Assert.Single(Heard(events, ThingSoundEvent.BoulderTurned));
        Assert.Equal((ushort)0x149, (ushort)crash.Event);
        AssertBoulder(crash, boulder);   // after the move that turned it

        (GameDatabase db2, WorldState world2) = Open();
        ObjectState worn = Rolling(db2, world2, 17, Row);
        worn.Wear = worn.Health - (worn.Health / 10);   // the wall's tenth takes it to its health
        GameSimulation sim2 = Sim(db2, world2, out ListEventSink events2);
        for (int i = 0; i < 12 && !events2.Events.Any(e => e.Kind == "boulder_broken"); i++) sim2.Run(1);
        Assert.Contains(events2.Events, e => e.Kind == "boulder_turned");
        Assert.Empty(Heard(events2, ThingSoundEvent.BoulderTurned));
    }

    [Fact]
    public void ABoulderThatBreaksAsksF4AtWhereItBrokeAfterTheWaterSoundAndBeforeItIsDeleted()
    {
        (GameDatabase db, WorldState world) = Open();
        ObjectState boulder = Rolling(db, world, 5, Row, rollX: 0);
        boulder.Wear = boulder.Health - 1;
        world.Tile(5, Row).TerrainId = Dk2TerrainFixture.Water;   // its tenth of health is more than it has left
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Run(1);

        Assert.Null(world.ObjectById(boulder.Id));
        ThingSoundRequest[] heard = Sounds(events);
        ThingSoundRequest[] broke = Heard(events, ThingSoundEvent.BoulderBroke);
        Assert.Equal(2, broke.Length);   // Boulder_Update's own, then the deletion's (a live handle retains the second)
        Assert.Equal((ushort)0xF4, (ushort)broke[0].Event);
        Assert.Equal(CreatureState.CentreOf(5), (int)broke[0].Position.X.Raw);
        Assert.All(broke, s => Assert.Equal(new SoundSourceId(SoundSourceKind.Object, boulder.Id), s.Source));
        Assert.Equal([ThingSoundEvent.BoulderRolling, ThingSoundEvent.BoulderInWater, ThingSoundEvent.BoulderBroke, ThingSoundEvent.BoulderBroke],
            heard.Select(s => s.Event));
    }

    /// <summary><c>Boulder_Update</c> runs the strike, the roll and the wear whatever the roll did, and a boulder with no health
    /// reaches the destruction block (<c>0x004A13AF</c>) after the roll destroyed it: <c>0xF4</c>, and no roll sound. No shipped
    /// <c>BoulderInitialHealth</c> is 0, and a slap (unbuilt) is what sets it, so this is not reached in play today.</summary>
    [Fact]
    public void ABoulderWithNoHealthStillAsksF4AfterItsRollDestroyedItAndNoRollSound()
    {
        (GameDatabase db, WorldState world) = Open();
        ObjectState boulder = Rolling(db, world, 5, Row);
        boulder.Health = 0;
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Run(1);

        Assert.Null(world.ObjectById(boulder.Id));
        Assert.Empty(Heard(events, ThingSoundEvent.BoulderRolling));
        Assert.Equal([ThingSoundEvent.BoulderBroke, ThingSoundEvent.BoulderBroke], Sounds(events).Select(s => s.Event));   // its own, then the deletion's
    }

    [Fact]
    public void ABoulderOnWaterAsks14bAndOnLavaAsks14cAtItsPositionAfterTheRoll()
    {
        foreach ((byte terrain, ThingSoundEvent expected, int id) in new[] { (Dk2TerrainFixture.Water, ThingSoundEvent.BoulderInWater, 0x14B),
                     (Dk2TerrainFixture.Lava, ThingSoundEvent.BoulderInLava, 0x14C) })
        {
            (GameDatabase db, WorldState world) = Open();
            world.Tile(8, Row).TerrainId = terrain;
            ObjectState boulder = Rolling(db, world, 8, Row, rollX: 0);
            GameSimulation sim = Sim(db, world, out ListEventSink events);

            sim.Run(1);

            ThingSoundRequest sound = Assert.Single(Heard(events, expected));
            Assert.Equal(id, (int)sound.Event);
            Assert.Equal("TRAP_BOULDER", sound.Category);
            Assert.Equal(new SoundSourceId(SoundSourceKind.Object, boulder.Id), sound.Source);
            Assert.Equal((boulder.PosX, boulder.PosY), ((int)sound.Position.X.Raw, (int)sound.Position.Y.Raw));
            Assert.True(sound.Voiced);
        }
    }

    [Fact]
    public void ABoulderOnPlainFloorAsksNeitherTheWaterNorTheLavaSound()
    {
        (GameDatabase db, WorldState world) = Open();
        Rolling(db, world, 8, Row, rollX: 0);
        GameSimulation sim = Sim(db, world, out ListEventSink events);
        sim.Run(3);
        Assert.Empty(Heard(events, ThingSoundEvent.BoulderInWater));
        Assert.Empty(Heard(events, ThingSoundEvent.BoulderInLava));
    }

    // ---------------------------------------------------------------------------------------- creatures

    /// <summary>A creature the strike leaves standing is hit with <c>0x14D</c>; one it leaves dying with <c>0x14E</c>
    /// (<c>+0x3E6</c> bit 0x200000 after the damage), each at the boulder, after the stun.</summary>
    [Fact]
    public void AStruckCreatureAsks14dWhenItSurvivesAnd14eWhenItDiesAtTheBoulder()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState lives = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, Row));
        ObjectState weak = Rolling(db, world, 5, Row);
        weak.Wear = 3900;   // strikes for the 100 it has left
        GameSimulation sim = Sim(db, world, out ListEventSink events);
        for (int i = 0; i < 10 && !events.Events.Any(e => e.Kind == "boulder_struck_creature"); i++) sim.Run(1);
        Assert.False(CreatureDeathRules.IsDown(lives));
        ThingSoundRequest hit = Assert.Single(Heard(events, ThingSoundEvent.BoulderHitCreature));
        Assert.Equal((ushort)0x14D, (ushort)hit.Event);
        Assert.Equal("TRAP_BOULDER", hit.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, weak.Id), hit.Source);
        Assert.Empty(Heard(events, ThingSoundEvent.BoulderHitDownedCreature));

        (GameDatabase db2, WorldState world2) = Open();
        CreatureState dies = world2.SpawnCreature(db2, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, Row));
        ObjectState strong = Rolling(db2, world2, 5, Row);
        GameSimulation sim2 = Sim(db2, world2, out ListEventSink events2);
        for (int i = 0; i < 10 && !events2.Events.Any(e => e.Kind == "boulder_struck_creature"); i++) sim2.Run(1);
        Assert.True(CreatureDeathRules.IsDown(dies));
        ThingSoundRequest killed = Assert.Single(Heard(events2, ThingSoundEvent.BoulderHitDownedCreature));
        Assert.Equal((ushort)0x14E, (ushort)killed.Event);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, strong.Id), killed.Source);
        Assert.Empty(Heard(events2, ThingSoundEvent.BoulderHitCreature));
    }

    // ---------------------------------------------------------------------------------------------- doors

    private static ThingSoundRequest[] DoorHit(byte impactKind, out ObjectState boulder)
    {
        GameDatabase basis = SoundFixture.Database(new Dictionary<byte, string> { [BoulderRules.ObjectId] = "TRAP_BOULDER" });
        (GameDatabase db, WorldState world) = Open(SoundFixture.WithDoorImpact(basis, impactKind));
        world.Tile(7, Row).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(7, Row).OwnerPlayerId = Rival;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 7, Y = Row, PlayerId = Rival, Flags = 0, DoorId = 1, Unknown_0011 = new byte[3] }]);
        boulder = Rolling(db, world, 7, Row, rollX: 0, offset: -1200);   // within half its width of the door's middle
        GameSimulation sim = Sim(db, world, out ListEventSink events);
        sim.Run(1);
        Assert.Contains(events.Events, e => e.Kind == "boulder_struck_door");
        return Sounds(events).Where(s => s.Event is ThingSoundEvent.BoulderHitDoor or ThingSoundEvent.BoulderHitMetalDoor).ToArray();
    }

    /// <summary>The door's impact byte (<c>def+0xB3</c>): 4 and 5 are <c>0x15A</c>, every other <c>0x14A</c>.</summary>
    [Theory]
    [InlineData(3, 0x14A)]
    [InlineData(2, 0x14A)]
    [InlineData(4, 0x15A)]
    [InlineData(5, 0x15A)]
    public void AStruckDoorAsks14aOr15aByItsImpactByteAtTheBoulder(int impactKind, int expected)
    {
        ThingSoundRequest[] heard = DoorHit((byte)impactKind, out ObjectState boulder);

        ThingSoundRequest hit = Assert.Single(heard);
        Assert.Equal(expected, (int)hit.Event);
        Assert.Equal("TRAP_BOULDER", hit.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, boulder.Id), hit.Source);
        Assert.True(hit.Voiced);
    }

    // ---------------------------------------------------------------------------------------- the hash

    [Fact]
    public void AttachingASoundSinkDoesNotChangeTheStateHashOfABoulderRun()
    {
        static GameSimulation Run(ListEventSink? sink)
        {
            (GameDatabase db, WorldState world) = Open();
            world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(9, Row));
            Rolling(db, world, 5, Row);
            var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem(), new BoulderSystem()]);
            if (sink is not null) sim.ObserveWith(sink);
            sim.Run(30);
            return sim;
        }
        var events = new ListEventSink();
        GameSimulation heard = Run(events);
        Assert.NotEmpty(Heard(events, ThingSoundEvent.BoulderRolling));
        Assert.Equal(Run(null).StateHash(), heard.StateHash());
    }
}
