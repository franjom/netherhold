// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>The sounds of docs/research/sound-effects.md section 14. <c>Object_Init</c> (<c>0x0049B1C0</c>)
/// requests <c>0xF1</c> with the object's handle <c>+0x2E</c> and <c>0xF2</c> with <c>+0x32</c>, and
/// <c>Object_OnDelete</c> (<c>0x0049B5E0</c>) <c>0xF4</c>, each in the object's category at <c>+0x1A</c>; the
/// struck Training Dummy asks <c>0x311</c> on its first update, and three specials a sound when activated.</summary>
public sealed class ObjectSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly int Tile = 2 + (2 * Dk2TerrainFixture.Map().Width);

    private static (GameDatabase Db, WorldState World, GameSimulation Sim, ListEventSink Events) Setup(
        params ISimulationSystem[] systems)
    {
        GameDatabase db = SoundFixture.Database(objects: new Dictionary<byte, string>
        {
            [HatcheryRules.ChickenObjectId] = "OBJECT_CHICKEN", [Dk2TerrainFixture.GoblinBedObjectId] = "OBJECT_BED",
            [RatObjectId] = "OBJECT_RAT", [BatObjectId] = "OBJECT_BAT",
            [Dk2TerrainFixture.TrainingDummyObjectId] = "OBJECT_TRAINING_DUMMY",
            [Dk2TerrainFixture.IncreaseLevelSpecialObjectId] = "OBJECT_SPECIAL",
            [Dk2TerrainFixture.RevealMapSpecialObjectId] = "OBJECT_SPECIAL",
            [Dk2TerrainFixture.ReceiveImpsSpecialObjectId] = "OBJECT_SPECIAL",
            [Dk2TerrainFixture.LocateHiddenLandSpecialObjectId] = "OBJECT_SPECIAL",
            [Dk2TerrainFixture.TransferCreatureSpecialObjectId] = "OBJECT_SPECIAL",
            [BoulderRules.ObjectId] = "TRAP_BOULDER", [FloorGold.FloorGoldObjectId] = "OBJECT_GOLD",
        });
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        var sim = new GameSimulation(db, world, seed: 42, systems);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (db, world, sim, events);
    }

    [Fact]
    public void AnObjectMadeInPlayRequestsF1ThenF2AtItsPositionWithItsVoice()
    {
        var (db, world, _, events) = Setup();
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, Tile);

        ThingSoundRequest[] heard = Sounds(events);   // Object_Init asks both, whatever the category holds
        Assert.Equal([ThingSoundEvent.ObjectMade, ThingSoundEvent.ObjectMadeSecond], heard.Select(s => s.Event));
        Assert.Equal((ushort)0xF1, (ushort)heard[0].Event);
        Assert.Equal((ushort)0xF2, (ushort)heard[1].Event);
        Assert.All(heard, sound =>
        {
            Assert.Equal("OBJECT_CHICKEN", sound.Category);
            Assert.Equal(new SoundSourceId(SoundSourceKind.Object, chicken.Id), sound.Source);
            Assert.Equal(Centre(world, Tile), sound.Position);
            Assert.True(sound.Voiced);
        });
    }

    [Fact]
    public void ABedARatAndABatEachAskBothInTheirOwnCategories()
    {
        var (db, world, _, events) = Setup();
        ObjectState bed = world.SpawnObject(db, Dk2TerrainFixture.GoblinBedObjectId, Keeper, Tile);
        ObjectState rat = world.SpawnObject(db, RatObjectId, Keeper, Tile + 1);
        ObjectState bat = world.SpawnObject(db, BatObjectId, Keeper, Tile + 2);

        ThingSoundRequest[] heard = Sounds(events);
        Assert.Equal(6, heard.Length);
        foreach ((int i, string category, int id) in new[] { (0, "OBJECT_BED", bed.Id), (2, "OBJECT_RAT", rat.Id), (4, "OBJECT_BAT", bat.Id) })
        {
            Assert.Equal((ThingSoundEvent.ObjectMade, category, id), (heard[i].Event, heard[i].Category, heard[i].Source.Value));
            Assert.Equal((ThingSoundEvent.ObjectMadeSecond, category, id), (heard[i + 1].Event, heard[i + 1].Category, heard[i + 1].Source.Value));
        }
        Assert.All(heard, s => Assert.Equal(SoundSourceKind.Object, s.Source.Kind));
        Assert.Equal(Centre(world, Tile, 0x1000), heard[0].Position);
    }

    [Fact]
    public void AnObjectWithNoSoundCategoryRequestsNothing()
    {
        var (db, world, _, events) = Setup();
        ObjectState bag = world.SpawnObject(db, 2, PlayerIds.Neutral, Tile);   // Gold Bag: no category
        world.RemoveObject(bag.Id);

        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void ARemovedChickenRequestsF4AtWhereItStoodWithItsVoice()
    {
        var (db, world, _, events) = Setup();
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, Tile);
        chicken.PosX = (3 << 12) + 321;
        chicken.PosY = (2 << 12) + 654;
        events.Clear();

        world.RemoveObject(chicken.Id);

        ThingSoundRequest sound = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.ObjectRemoved, sound.Event);
        Assert.Equal((ushort)0xF4, (ushort)sound.Event);
        Assert.Equal("OBJECT_CHICKEN", sound.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, chicken.Id), sound.Source);
        Assert.Equal(new FpPosition3(new Fp32((3 << 12) + 321), new Fp32((2 << 12) + 654), new Fp32(0x1000)), sound.Position);
        Assert.True(sound.Voiced);
    }

    [Fact]
    public void ARemovedBoulderRequestsF4InTheBoulderCategoryAndAnIdThatIsNotThereIsSilent()
    {
        var (db, world, _, events) = Setup();
        ObjectState boulder = world.SpawnObject(db, BoulderRules.ObjectId, Keeper, Tile);
        events.Clear();

        Assert.False(world.RemoveObject(9999));
        Assert.True(world.RemoveObject(boulder.Id));

        ThingSoundRequest sound = Assert.Single(Sounds(events));
        Assert.Equal((ThingSoundEvent.ObjectRemoved, "TRAP_BOULDER"), (sound.Event, sound.Category));
    }

    [Fact]
    public void AnObjectMadeBeforeAnyoneListensIsSilentAndAttachingASinkDoesNotChangeTheStateHash()
    {
        static GameSimulation Run(ListEventSink? sink)
        {
            GameDatabase db = SoundFixture.Database(objects: new Dictionary<byte, string> { [HatcheryRules.ChickenObjectId] = "OBJECT_CHICKEN" });
            WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
            var sim = new GameSimulation(db, world, seed: 42, []);
            if (sink is not null) sim.ObserveWith(sink);
            ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, Tile);
            sim.Tick(InputFrame.Empty);
            world.RemoveObject(chicken.Id);
            return sim;
        }
        var events = new ListEventSink();
        GameSimulation heard = Run(events);
        Assert.Equal([ThingSoundEvent.ObjectMade, ThingSoundEvent.ObjectMadeSecond, ThingSoundEvent.ObjectRemoved],
            Sounds(events).Select(s => s.Event));
        Assert.Equal(Run(null).StateHash(), heard.StateHash());
    }

    // ------------------------------------------------------------------------------ the chicken's step

    private static (ObjectState Chicken, GameSimulation Sim, ListEventSink Events) Stepping(ushort ticks, ObjectMoveFlags flags = ObjectMoveFlags.None)
    {
        var (db, world, sim, events) = Setup(new ObjectMovementSystem());
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, Tile);
        chicken.ProcessState = (byte)ChickenProcess.Moving;
        chicken.ProcessAgeTicks = 1;
        chicken.MoveResumeState = (byte)ChickenProcess.None;
        chicken.MoveFlags = flags;
        chicken.MoveTicksRemaining = ticks;
        chicken.MoveDestinationX = chicken.PosX + 0x1000;
        chicken.MoveDestinationY = chicken.PosY;
        chicken.MoveVelocityX = 0x100;
        events.Clear();
        return (chicken, sim, events);
    }

    /// <summary><c>Object_Process9MoveTo</c> (<c>0x0049FA50</c>) tests its step count first thing: on the call where it is 1 a chicken
    /// offers <c>0x110</c> (<c>0x0049FA95</c>) with its handle at where it stands, marked for the listener's alternator; on
    /// the calls before, and for any other object, nothing.</summary>
    [Fact]
    public void AChickenOffers110OnTheCallWhereItsStepCountIsOneAtItsPositionBeforeItMoves()
    {
        var (chicken, sim, events) = Stepping(3);
        var before = new FpPosition3(new Fp32(chicken.PosX), new Fp32(chicken.PosY), new Fp32(0x1000));

        sim.Run(2);
        Assert.Empty(Sounds(events));   // counts 3 and 2

        sim.Run(1);   // count 1
        ThingSoundRequest step = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.ChickenStep, step.Event);
        Assert.Equal((ushort)0x110, (ushort)step.Event);
        Assert.Equal("OBJECT_CHICKEN", step.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, chicken.Id), step.Source);
        Assert.Equal(before.Y, step.Position.Y);
        Assert.True(step.Voiced);
        Assert.True(step.Alternating);
    }

    [Fact]
    public void ATurningChickenWhoseCountStaysOneOffersItOnEveryCallItTurns()
    {
        var (chicken, sim, events) = Stepping(1, ObjectMoveFlags.Turning);
        chicken.Heading = 0x600;   // the destination lies east, 0x200: a turn of more than one call at 0x100 a call

        sim.Run(3);

        Assert.True(Sounds(events).Length >= 2);
        Assert.All(Sounds(events), s => Assert.Equal(ThingSoundEvent.ChickenStep, s.Event));
    }

    // ------------------------------------------------------------------------------ the struck dummy

    /// <summary><c>ObjectState18_Struck</c> (<c>0x004A13F0</c>) at <c>0x004A14F2</c>: on the first update of the spin
    /// (age 1), for an object whose definition id is 14 only, <c>0x311</c> with the object's handle at its position.</summary>
    [Fact]
    public void AStruckTrainingDummyRequests311OnceOnTheFirstUpdateOfItsSpinAndNoOtherObjectDoes()
    {
        var (db, world, sim, events) = Setup(new ObjectReactionSystem());
        ObjectState dummy = world.SpawnObject(db, Dk2TerrainFixture.TrainingDummyObjectId, Keeper, Tile);
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, Keeper, Tile + 1);
        events.Clear();
        dummy.ProcessState = chicken.ProcessState = ObjectReactionRules.StruckProcess;
        Assert.Empty(Sounds(events));

        sim.Tick(InputFrame.Empty);

        ThingSoundRequest sound = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.StruckDummy, sound.Event);
        Assert.Equal((ushort)0x311, (ushort)sound.Event);
        Assert.Equal("OBJECT_TRAINING_DUMMY", sound.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, dummy.Id), sound.Source);
        Assert.Equal(Centre(world, Tile), sound.Position);
        Assert.True(sound.Voiced);

        sim.Run(10);
        Assert.Single(Sounds(events));   // the later updates of the spin ask nothing
    }

    // ------------------------------------------------------------------------------------ the specials

    private static ThingSoundRequest Activate(byte objectTypeId, out ObjectState special)
    {
        var (db, world, sim, events) = Setup(new SpecialSystem());
        special = world.SpawnObject(db, objectTypeId, PlayerIds.Neutral, Tile);
        events.Clear();
        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(Keeper, special.Id)));
        ThingSoundRequest[] heard = Sounds(events).Where(s => s.Event is ThingSoundEvent.SpecialIncreasedLevel
            or ThingSoundEvent.SpecialReceivedImps or ThingSoundEvent.SpecialLocatedHiddenLand).ToArray();
        return heard.Length == 1 ? heard[0] : default;
    }

    /// <summary><c>Special_IncreaseLevel</c> (<c>0x004A42C0</c>, <c>0x80</c>), <c>Special_ReceiveImps</c> (<c>0x004A4980</c>,
    /// <c>0x8B</c>) and <c>Special_LocateHiddenLand</c> (<c>0x004A4F70</c>, <c>0x8F</c>) end with the sound, with the
    /// object's handle, in its category, at its position, whatever the arm did; the transfer arm asks none.</summary>
    [Theory]
    [InlineData(Dk2TerrainFixture.IncreaseLevelSpecialObjectId, 0x80)]
    [InlineData(Dk2TerrainFixture.ReceiveImpsSpecialObjectId, 0x8B)]
    [InlineData(Dk2TerrainFixture.LocateHiddenLandSpecialObjectId, 0x8F)]
    public void AnActivatedSpecialRequestsItsOwnSoundAtItsPositionWithItsVoice(byte objectTypeId, int expected)
    {
        ThingSoundRequest sound = Activate(objectTypeId, out ObjectState special);

        Assert.Equal((ushort)expected, (ushort)sound.Event);
        Assert.Equal("OBJECT_SPECIAL", sound.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, special.Id), sound.Source);
        Assert.Equal(sound.Position.X.Raw >> 12, Tile % Dk2TerrainFixture.Map().Width);
        Assert.True(sound.Voiced);
    }

    [Fact]
    public void TheTransferSpecialAndAnUnbuiltOneRequestNoSpecialSound()
    {
        Assert.Equal(default, Activate(Dk2TerrainFixture.TransferCreatureSpecialObjectId, out _));
        Assert.Equal(default, Activate(Dk2TerrainFixture.RevealMapSpecialObjectId, out _));
    }
}
