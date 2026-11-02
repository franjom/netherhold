// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>The possessed creature's meal of a chicken, <c>GameAction_PossessedCreatureAct</c> (<c>0x00514830</c>), asks
/// <c>0xCD</c> with its own handle in its category at its position, then <c>0x111</c> with the chicken's in the
/// chicken's at the chicken's, after the heal and before the bird is deleted; a creature owing no food kills the bird
/// with no meal sound (the deletion's own <c>0xF4</c> remains).</summary>
public sealed class PossessionSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const ushort East = 0x200, Level = 0x200;

    private static (GameDatabase Db, WorldState World, CreatureState Me, ObjectState Chicken, GameSimulation Sim, ListEventSink Events) Setup(int hunger)
    {
        GameDatabase db = SoundFixture.Database(new Dictionary<byte, string> { [HatcheryRules.ChickenObjectId] = "OBJECT_CHICKEN" },
            Dk2TerrainFixture.Possessor() with { SoundCategory = "CREATURE_GOBLIN" });
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Keeper + 1]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        me.Possessed = true;
        me.Heading = East;
        world.Player(Keeper).PossessedCreatureId = me.Id;
        ObjectState chicken = world.SpawnObject(db, HatcheryRules.ChickenObjectId, PlayerIds.Neutral, (me.Tile.Y * world.Width) + me.Tile.X);
        chicken.PosX = me.PosX + 200;
        chicken.PosY = me.PosY;
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem()]);
        sim.Tick();
        sim.Tick();
        me.HungerNeed = hunger;
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Tick(InputFrame.Of(new ActPossessedCreatureCommand(Keeper,
            new(new(new(me.PosX), new(me.PosY), new(me.PosZ)), new(East), new(Level)), true, new(0))));
        return (db, world, me, chicken, sim, events);
    }

    [Fact]
    public void AHungryPossessedCreatureEatingAChickenAsksItsOwnCdThenTheChickensAndThenTheDeletionsF4()
    {
        var (_, world, me, chicken, _, events) = Setup(hunger: 3);

        Assert.Null(world.ObjectById(chicken.Id));
        ThingSoundRequest[] heard = Sounds(events);
        Assert.Equal([ThingSoundEvent.Ate, ThingSoundEvent.ChickenEaten, ThingSoundEvent.ObjectRemoved], heard.Select(s => s.Event));
        Assert.Equal((ushort)0xCD, (ushort)heard[0].Event);
        Assert.Equal("CREATURE_GOBLIN", heard[0].Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(me.Id)), heard[0].Source);
        Assert.Equal(new FpPosition3(new Fp32(me.PosX), new Fp32(me.PosY), new Fp32(me.PosZ)), heard[0].Position);
        Assert.True(heard[0].Voiced);
        Assert.Equal((ushort)0x111, (ushort)heard[1].Event);
        Assert.Equal("OBJECT_CHICKEN", heard[1].Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Object, chicken.Id), heard[1].Source);
        Assert.Equal(new FpPosition3(new Fp32(chicken.PosX), new Fp32(chicken.PosY), new Fp32(0x1000)), heard[1].Position);
        Assert.True(heard[1].Voiced);
    }

    [Fact]
    public void ACreatureOwingNoFoodKillsTheChickenWithoutAMealSound()
    {
        var (_, world, _, chicken, _, events) = Setup(hunger: 0);

        Assert.Null(world.ObjectById(chicken.Id));
        Assert.Equal([ThingSoundEvent.ObjectRemoved], Sounds(events).Select(s => s.Event));
    }
}
