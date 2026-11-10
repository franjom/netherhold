// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The hand's lift and drop sounds: <c>0xC3</c> at the end of <c>Hand_PrepareCreature</c>
/// (<c>0x004BFBD0</c>, handle 0) and of <c>HandState2_LiftObject</c> (<c>0x004C0CB0</c>, the object's
/// handle), <c>0xC4</c> at the end of <c>Hand_Drop</c> (<c>0x004BC710</c>), each in the thing's own
/// definition's category at its position.</summary>
public sealed class HandSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private static readonly TilePos Floor = new(2, 2), Elsewhere = new(1, 3);

    private static (GameDatabase Db, WorldState World, GameSimulation Sim, ListEventSink Events) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        var sim = new GameSimulation(db, world, seed: 42, [new HandSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (db, world, sim, events);
    }

    /// <summary>The hand's requests; an object's own creation and deletion sounds (<c>0xF1</c>, <c>0xF2</c>, <c>0xF4</c>,
    /// ObjectSoundTests) are not what these tests are about.</summary>
    private static ThingSoundRequest[] Sounds(ListEventSink events) =>
        events.Events.Where(e => e.ThingSound.HasValue).Select(e => e.ThingSound!.Value)
            .Where(s => !(s.Source.Kind == SoundSourceKind.Object
                && s.Event is ThingSoundEvent.ObjectMade or ThingSoundEvent.ObjectMadeSecond or ThingSoundEvent.ObjectRemoved)).ToArray();

    private static FpPosition3 Centre(TilePos tile, int height = 0x1000) =>
        new(new Fp32(CreatureState.CentreOf(tile.X)), new Fp32(CreatureState.CentreOf(tile.Y)), new Fp32(height));

    [Fact]
    public void LiftingACreatureRequestsC3InItsCategoryAtItsPositionWithoutAVoice()
    {
        var (db, world, sim, events) = Setup();
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));

        ThingSoundRequest sound = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.PickedUp, sound.Event);
        Assert.Equal((ushort)0xC3, (ushort)sound.Event);
        Assert.Equal("CREATURE_IMP", sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(imp.Id)), sound.Source);
        Assert.Equal(new FpPosition3(new Fp32(imp.PosX), new Fp32(imp.PosY), new Fp32(imp.PosZ)), sound.Position);
        Assert.False(sound.Voiced);
    }

    [Fact]
    public void ARefusedLiftIsSilentButAFullHandStillSoundsBecausePreparationComesFirst()
    {
        var (db, world, sim, events) = Setup();
        CreatureState theirs = world.SpawnCreature(db, ImpId, PlayerIds.Neutral, Floor);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, theirs.Id)));
        Assert.Empty(Sounds(events));

        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        List<HandItem> hand = world.Player(Keeper).Hand;
        while (hand.Count < HandRules.Capacity(db)) hand.Add(HandItem.Of(imp));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        Assert.False(imp.InHand);
        Assert.Equal(ThingSoundEvent.PickedUp, Assert.Single(Sounds(events)).Event);
    }

    [Fact]
    public void DroppingACreatureRequestsC4AtThePointerXYAndTheDropsHeightLikeTheHumanDropPathAndARefusedDropDoesNot()
    {
        var (db, world, sim, events) = Setup();
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        int lifted = Sounds(events).Length;

        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(4, 4))));   // rock: refused
        Assert.Equal(lifted, Sounds(events).Length);

        var at = new FpPosition2(new Fp32((Elsewhere.X << 12) + 1000), new Fp32((Elsewhere.Y << 12) + 3000));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Elsewhere, 0, at)));
        ThingSoundRequest sound = Assert.Single(Sounds(events).Skip(lifted));
        Assert.Equal(ThingSoundEvent.Dropped, sound.Event);
        Assert.Equal("CREATURE_IMP", sound.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(imp.Id)), sound.Source);
        Assert.Equal(new FpPosition3(at.X, at.Y, new Fp32(0x2000)), sound.Position);   // 0x004C0F68 writes the height
        Assert.False(sound.Voiced);
    }

    [Fact]
    public void LiftingAndDroppingLooseGoldRequestC3AndC4InObjectGoldWithTheObjectsVoice()
    {
        var (db, world, sim, events) = Setup();
        ObjectState gold = FloorGold.Drop(world, db, (Floor.Y * world.Width) + Floor.X, 100)!;
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, gold.Id)));
        Assert.True(gold.InHand);

        ThingSoundRequest lift = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.PickedUp, lift.Event);
        Assert.Equal("OBJECT_GOLD", lift.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, gold.Id), lift.Source);
        Assert.Equal(Centre(Floor), lift.Position);
        Assert.True(lift.Voiced);

        var at = new FpPosition2(new Fp32((Elsewhere.X << 12) + 700), new Fp32((Elsewhere.Y << 12) + 900));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Elsewhere, 0, at)));
        ThingSoundRequest drop = Assert.Single(Sounds(events).Skip(1));
        Assert.Equal(ThingSoundEvent.Dropped, drop.Event);
        Assert.Equal("OBJECT_GOLD", drop.Category);
        Assert.Equal(lift.Source, drop.Source);
        Assert.Equal(new FpPosition3(at.X, at.Y, new Fp32(0x2000)), drop.Position);
        Assert.True(drop.Voiced);
    }

    [Fact]
    public void ADropOntoALiveCreatureSoundsAtThatCreaturesPointAtTheDropsHeight()
    {
        var (db, world, sim, events) = Setup();
        ObjectState gold = FloorGold.Drop(world, db, (Floor.Y * world.Width) + Floor.X, 100)!;
        CreatureState under = world.SpawnCreature(db, ImpId, Keeper, Elsewhere);
        under.PosZ = 777;
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, gold.Id)));
        var at = new FpPosition2(new Fp32((Elsewhere.X << 12) + 700), new Fp32((Elsewhere.Y << 12) + 900));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Elsewhere, under.Id, at)));
        ThingSoundRequest drop = Sounds(events).Single(s => s.Event == ThingSoundEvent.Dropped);
        Assert.Equal(new FpPosition3(new Fp32(under.PosX), new Fp32(under.PosY), new Fp32(0x2000)), drop.Position);
    }

    [Fact]
    public void AnObjectWithNoSoundCategoryRequestsNothing()
    {
        var (db, world, sim, events) = Setup();
        ObjectState bag = world.SpawnObject(db, 2, PlayerIds.Neutral, (Floor.Y * world.Width) + Floor.X);   // Gold Bag
        bag.GoldHeld = 50;
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, bag.Id)));
        Assert.True(bag.InHand);
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Elsewhere)));
        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void AttachingASoundSinkDoesNotChangeTheStateHashOfALiftAndDropRun()
    {
        static GameSimulation Run(ListEventSink? sink)
        {
            GameDatabase db = Dk2TerrainFixture.Database();
            WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
            CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
            ObjectState gold = FloorGold.Drop(world, db, (Floor.Y * world.Width) + Floor.X, 100)!;
            var sim = new GameSimulation(db, world, seed: 42, [new HandSystem()]);
            if (sink is not null) sim.ObserveWith(sink);
            sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id), new PickUpObjectCommand(Keeper, gold.Id)));
            sim.Tick(InputFrame.Of(new DropCommand(Keeper, Elsewhere), new DropCommand(Keeper, Elsewhere)));
            return sim;
        }
        var events = new ListEventSink();
        GameSimulation heard = Run(events);
        Assert.Equal(4, Sounds(events).Length);
        Assert.Equal(Run(null).StateHash(), heard.StateHash());
    }
}
