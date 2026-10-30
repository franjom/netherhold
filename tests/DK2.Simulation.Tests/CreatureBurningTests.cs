// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class CreatureBurningTests
{
    [Theory]
    [InlineData(true, 1, 314, Dk2TerrainFixture.ClaimedPath, 10)]
    [InlineData(true, 1, 315, Dk2TerrainFixture.ClaimedPath, 0)]
    [InlineData(true, 2, 393, Dk2TerrainFixture.ClaimedPath, 10)]
    [InlineData(true, 2, 394, Dk2TerrainFixture.ClaimedPath, 0)]
    [InlineData(true, 1, 300, Dk2TerrainFixture.Water, 0)]
    [InlineData(true, 1, 300, Dk2TerrainFixture.Lava, 10)]
    [InlineData(false, 1, 314, Dk2TerrainFixture.ClaimedPath, 5)]
    [InlineData(false, 1, 315, Dk2TerrainFixture.ClaimedPath, 0)]
    [InlineData(false, 2, 393, Dk2TerrainFixture.ClaimedPath, 5)]
    [InlineData(false, 2, 394, Dk2TerrainFixture.ClaimedPath, 0)]
    [InlineData(false, 1, 300, Dk2TerrainFixture.Water, 0)]
    [InlineData(false, 1, 300, Dk2TerrainFixture.Lava, 5)]
    public void IgnitionUsesTheTruncatedPreHitPercentAndOnlyWaterSuppressesIt(
        bool contact, int level, int health, byte terrain, int expectedTicks)
    {
        var (db, world, target) = Setup();
        target.Level = level;
        target.Health = health;
        world.Tile(target.Tile).TerrainId = terrain;
        new GameSimulation(db, world, 1, [new Hit(contact)]).Tick();
        Assert.Equal(expectedTicks, target.BurningTicks);
    }

    [Fact]
    public void AHitCrossingTheThresholdDoesNotIgniteUntilTheNextHit()
    {
        var (db, world, target) = Setup();
        target.Health = 315;
        var sim = new GameSimulation(db, world, 1, [new Hit(true)]);
        sim.Tick();
        Assert.Equal(0, target.BurningTicks);
        sim.Tick();
        Assert.Equal(10, target.BurningTicks);
    }

    [Fact]
    public void EachBurningPassDrawsZXYThreeTimesAndEmitsUnattachedFlamesEvenOnItsLastTick()
    {
        var (db, world, target) = Setup();
        CreatureDef def = db.CreatureById(target.CreatureTypeId);
        int height = def.Height.Raw, width = def.FootprintRadius.Raw;
        var draws = new Draws((height, 11), (width, 12), (width, 13),
            (height, 21), (width, 22), (width, 23), (height, 31), (width, 32), (width, 33));
        target.BurningTicks = 1;
        target.BurningOwnerTag = 4;
        target.PosZ = 90;
        var sim = new GameSimulation(db, world, new SimRandom(draws), [new CreatureStatusSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        int health = target.Health;
        sim.Tick();
        Assert.Equal(0, draws.Remaining);
        ViewEffectRequest[] flames = events.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest!.Value).ToArray();
        Assert.Equal(3, flames.Length);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(new EffectId(88), flames[i].EffectId);
            Assert.Equal(new PlayerId(3), flames[i].Owner);
            Assert.Equal(new FpPosition3(new Fp32(target.PosX - width / 2 + 12 + 10 * i),
                new Fp32(target.PosY - width / 2 + 13 + 10 * i), new Fp32(101 + 10 * i)), flames[i].Position);
            Assert.Equal(0, flames[i].AttachedCreatureId);
            Assert.Equal(default, flames[i].Heading);
        }
        Assert.False(target.IsBurning);
        Assert.Equal((ushort)0, target.BurningOwnerTag);
        Assert.Equal(health, target.Health);
        sim.Tick();
        Assert.Equal(3, events.Events.Count(e => e.EffectRequest.HasValue));
    }

    [Fact]
    public void HornySkipsTheCountdownAndEveryDraw()
    {
        CreatureDef horny = Dk2TerrainFixture.Goblin() with { CreatureId = 100, Flags = CreatureFlags.IsHorny };
        GameDatabase db = Dk2TerrainFixture.Database([horny]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, (byte)2, (byte)3]);
        CreatureState target = world.SpawnCreature(db, 100, 3, new TilePos(4, 4));
        Dk2TerrainFixture.SetKnownIdentities(world);
        target.BurningTicks = 2;
        target.BurningOwnerTag = 4;
        new GameSimulation(db, world, new SimRandom(new Draws()), [new CreatureStatusSystem()]).Tick();
        Assert.Equal(2, target.BurningTicks);
        Assert.Equal((ushort)4, target.BurningOwnerTag);
    }

    [Fact]
    public void ACorpseNoLongerRunsTheCreatureBurningPass()
    {
        var (db, world, target) = Setup();
        target.Health = 0;
        StatePlacement.As(target, CreatureActivity.Dead);
        target.BurningTicks = 2;
        var sim = new GameSimulation(db, world, new SimRandom(new Draws()), [new CreatureStatusSystem()]);
        sim.Tick();
        Assert.Equal(2, target.BurningTicks);
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 10)]
    public void AFireHitIgnitesADyingCreatureBeforeItsHealthRefusal(bool contact, int expectedTicks)
    {
        var (db, world, target) = Setup();
        StatePlacement.Place(target, CreatureDeathRules.DyingState);
        target.Health = 200;
        new GameSimulation(db, world, 1, [new Hit(contact)]).Tick();
        Assert.Equal(expectedTicks, target.BurningTicks);
        Assert.Equal((ushort)4, target.BurningOwnerTag);
        Assert.Equal(200, target.Health);
    }

    [Fact]
    public void BurningWordsAreIndependentlyHashedAndSurviveASnapshot()
    {
        var (db, world, target) = Setup();
        var sim = new GameSimulation(db, world, 1);
        ulong before = sim.StateHash();
        target.BurningTicks = 7;
        ulong afterTicks = sim.StateHash();
        Assert.NotEqual(before, afterTicks);
        target.BurningOwnerTag = 4;
        Assert.NotEqual(afterTicks, sim.StateHash());
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal((ushort)7, restored.World.Creatures[0].BurningTicks);
        Assert.Equal((ushort)4, restored.World.Creatures[0].BurningOwnerTag);
    }

    [Fact]
    public void EveryCreaturesBurningDrawsPrecedeThatCreaturesOtherDrawsInNewestFirstOrder()
    {
        var (db, world, first) = Setup();
        CreatureState second = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 3, new TilePos(5, 4));
        Dk2TerrainFixture.SetKnownIdentities(world);
        first.BurningTicks = second.BurningTicks = 1;
        CreatureDef def = db.CreatureById(first.CreatureTypeId);
        int h = def.Height.Raw, w = def.FootprintRadius.Raw;
        var draws = new Draws((h, 1), (w, 2), (w, 3), (h, 4), (w, 5), (w, 6), (h, 7), (w, 8), (w, 9), (77, 0),
            (h, 11), (w, 12), (w, 13), (h, 14), (w, 15), (w, 16), (h, 17), (w, 18), (w, 19), (77, 0));
        var after = new OtherDraw();
        var events = new ListEventSink();
        var sim = new GameSimulation(db, world, new SimRandom(draws), [new CreatureUpdateSystem(new CreatureStatusSystem(), after)]);
        sim.ObserveWith(events);
        sim.Tick();
        Assert.Equal(0, draws.Remaining);
        Assert.Equal([second.Id, first.Id], after.Order);
        Assert.Equal(second.PosX - w / 2 + 2, events.Events.First(e => e.EffectRequest.HasValue).EffectRequest!.Value.Position.X.Raw);
        Assert.IsType<CreatureStatusSystem>(SimulationSystems.Default().OfType<CreatureUpdateSystem>().Single().Steps[0]);
        Assert.DoesNotContain(SimulationSystems.Default(), system => system is CreatureStatusSystem);
    }

    private sealed class OtherDraw : ICreatureUpdateStep
    {
        public List<int> Order { get; } = [];
        public string Name => "test_other_draw";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature)
        {
            Order.Add(creature.Id);
            context.Rng.NextInt(77);
        }
    }

    [Fact]
    public void AWeakCreatureIgnitesBeforeTheContactDamageLands()
    {
        var (db, world, target) = Setup();
        target.Health = 300;
        var sim = new GameSimulation(db, world, 1, [new Hit(contact: true)]);
        sim.Tick();
        Assert.Equal(10, target.BurningTicks);
        Assert.Equal((ushort)4, target.BurningOwnerTag);
        Assert.Equal(299, target.Health);
    }

    private sealed class Hit(bool contact) : ISimulationSystem
    {
        public string Name => "test_hit";
        public void Execute(SimulationContext context)
        {
            CreatureState target = context.World.Creatures[0];
            ShotDef definition = context.Database.Shots[Dk2TerrainFixture.FireballShotId] with { AttackCategory = 4 };
            var shot = new ShotState { OwnerPlayerId = 4, Data1 = 1 };
            if (contact) ShotRules.ContactDamage(context, shot, definition, target);
            else ShotRules.ModifyHealth(context, shot, definition, target, context.Database.CreatureById(target.CreatureTypeId));
        }
    }

    private static (GameDatabase, WorldState, CreatureState) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, (byte)1, (byte)2, (byte)3, (byte)4]);
        world.Tile(4, 4).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(4, 4).OwnerPlayerId = 3;
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 3, new TilePos(4, 4));
        Dk2TerrainFixture.SetKnownIdentities(world);
        return (db, world, creature);
    }

    private sealed class Draws(params (int Bound, int Value)[] draws) : IRandomAlgorithm
    {
        private int _index;
        public int Remaining => draws.Length - _index;
        public int NextInt(int exclusiveMax)
        {
            var draw = draws[_index++];
            Assert.Equal(draw.Bound, exclusiveMax);
            return draw.Value;
        }
        public void HashInto(ref Hash64 hash) => hash.Add(_index);
    }
}
