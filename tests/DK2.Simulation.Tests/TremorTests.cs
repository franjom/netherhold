// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class TremorTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos At = new(10, 10);

    private static (GameDatabase Db, WorldState World) Setup()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        basis.Variables.Misc[VariableTypeId.TremorDamagePerTerrainTilePerSecond] =
            new MiscVariable(VariableTypeId.TremorDamagePerTerrainTilePerSecond, 436, 0, 0);
        var spell = new KeeperSpellDef
        {
            Name = "Tremor", KeeperSpellId = 8, ManaCost = 30000, ManaUpkeepPerSecond = 0,
            ShotTypeId = 24, ShotData1 = 5, ShotData2 = 3,
            BonusShotTypeId = 24, BonusShotData1 = 7, BonusShotData2 = 3,
            LocationSelector = 5, TargetSelector = 4,
        };
        var shot = new ShotDef(new byte[239])
        {
            Name = "Tremor", ShotId = 24, Health = 10, Flags = 4,
            MeshResource = new KwdArtResource("", 0, default, 0, 0, 0, 0),
            Light = new KwdLight(0, 0, 0, 0, 0, 0, 0, 0, 0), SoundCategory = "",
            ShotProcessFlags = 0x40, ProcessType = 27, CollideType = 2,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, [spell], objects: basis.Objects.Values,
            shots: basis.Shots.Values.Where(s => s.ShotId != shot.ShotId).Append(shot));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper, (byte)(Keeper + 1)]);
        world.Player(Keeper).Mana = spell.ManaCost * 3;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, spell.KeeperSpellId, 3));
        world.Tile(At).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static CastSpellAtPlaceCommand Cast() =>
        new(Keeper, 8, CreatureState.CentreOf(At.X), CreatureState.CentreOf(At.Y));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CastPaysOnceAndKeepsTheResearchedDuration_StronglyInferredFromBinary(bool upgraded)
    {
        var (db, world) = Setup();
        world.SetUpgraded(Keeper, AvailabilityKind.Spell, 8, upgraded);
        ISimulationSystem[] systems = [new KeeperSpellSystem(), new ShotSystem(), new TremorSystem()];
        var sim = new GameSimulation(db, world, 1, systems);
        sim.Run(3);
        sim.Tick(InputFrame.Of(Cast()));
        var spell = db.KeeperSpellById(8)!;
        int durationTicks = spell.Shot(upgraded).Data1 * SimulationTiming.OriginalTicksPerGameSecond;
        TremorState active = Assert.Single(world.Tremors, t => t.Radius != 0);
        Assert.Equal((uint)3, active.StartedAt);
        Assert.Equal((ushort)durationTicks, active.DurationTicks);
        Assert.Equal((ushort)(spell.Shot(upgraded).Data2 << 12), active.Radius);
        Assert.Equal(spell.ManaCost * 2, world.Player(Keeper).Mana);
        sim.Run(durationTicks - 1);
        Assert.Single(world.Tremors, t => t.Radius != 0);
        sim.Tick();
        Assert.DoesNotContain(world.Tremors, t => t.Radius != 0);
        Assert.Equal(active.StartedAt, world.Tremors[0].StartedAt);
        Assert.Equal(active.DurationTicks, world.Tremors[0].DurationTicks);
        Assert.Equal((active.WorldX, active.WorldY), (world.Tremors[0].WorldX, world.Tremors[0].WorldY));
    }

    [Fact]
    public void NoFloorConsumesOnlyTheSquareSample_StronglyInferredFromBinary()
    {
        var (db, world) = Setup();
        world.Tile(At).TerrainId = Dk2TerrainFixture.ReinforcedWall;
        world.Tile(At).OwnerPlayerId = Keeper;
        world.Tile(At).Health = db.TerrainById(Dk2TerrainFixture.ReinforcedWall).StartingHealth;
        int before = world.Tile(At).Health;
        var draws = new Draws((49, 24));
        new GameSimulation(db, world, new SimRandom(draws),
            [new KeeperSpellSystem(), new ShotSystem(), new TremorSystem()]).Tick(InputFrame.Of(Cast()));
        Assert.Equal(before, world.Tile(At).Health);
        Assert.Equal(0, draws.Remaining);
    }

    [Theory]
    [InlineData(Keeper)]
    [InlineData(Keeper + 1)]
    public void FloorFallbackDamagesOwnedReinforcementWithoutAnOwnerFilter_StronglyInferredFromBinary(byte owner)
    {
        var (db, world) = Setup();
        TilePos wall = new(At.X, At.Y - 1);
        world.Tile(wall).TerrainId = Dk2TerrainFixture.ReinforcedWall;
        world.Tile(wall).OwnerPlayerId = owner;
        world.Tile(wall).Health = db.TerrainById(Dk2TerrainFixture.ReinforcedWall).StartingHealth;
        int before = world.Tile(wall).Health;
        int halfDamage = db[VariableTypeId.TremorDamagePerTerrainTilePerSecond] / 2;
        var draws = new Draws((49, 24), (halfDamage, 0));
        new GameSimulation(db, world, new SimRandom(draws),
            [new KeeperSpellSystem(), new ShotSystem(), new TremorSystem()]).Tick(InputFrame.Of(Cast()));
        Assert.Equal(before - halfDamage, world.Tile(wall).Health);
        Assert.Equal(owner, world.Tile(wall).OwnerPlayerId);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void WallDamageUsesTheTerrainConversionAtZeroHealth_StronglyInferredFromBinary()
    {
        var (db, world) = Setup();
        TilePos wall = new(At.X, At.Y - 1);
        world.Tile(wall).TerrainId = Dk2TerrainFixture.ReinforcedWall;
        world.Tile(wall).OwnerPlayerId = Keeper;
        int halfDamage = db[VariableTypeId.TremorDamagePerTerrainTilePerSecond] / 2;
        world.Tile(wall).Health = halfDamage - 1;
        var draws = new Draws((49, 24), (halfDamage, 0));
        new GameSimulation(db, world, new SimRandom(draws),
            [new KeeperSpellSystem(), new ShotSystem(), new TremorSystem()]).Tick(InputFrame.Of(Cast()));
        TerrainDef destroyed = db.TerrainById(db.TerrainById(Dk2TerrainFixture.ReinforcedWall).DestroyedTypeTerrainId);
        Assert.Equal(destroyed.TerrainId, world.Tile(wall).TerrainId);
        Assert.Equal((int)destroyed.StartingHealth, world.Tile(wall).Health);
        Assert.Equal(PlayerIds.Neutral, world.Tile(wall).OwnerPlayerId);
        Assert.Equal(0, draws.Remaining);
    }

    private sealed class Draws(params (int Bound, int Value)[] draws) : IRandomAlgorithm
    {
        private int _next;
        public int Remaining => draws.Length - _next;
        public int NextInt(int exclusiveMax)
        {
            Assert.True(_next < draws.Length, "Unexpected RNG draw.");
            var draw = draws[_next++];
            Assert.Equal(draw.Bound, exclusiveMax);
            return draw.Value;
        }
        public void HashInto(ref Hash64 hash) => hash.Add(_next);
    }

    [Fact]
    public void SquareSampleUsesRemainderForXAndQuotientForY_StronglyInferredFromBinary()
    {
        var (db, world) = Setup();
        world.Tile(At).TerrainId = Dk2TerrainFixture.Rock;
        var floor = new TilePos(12, 9);
        var wall = new TilePos(12, 8);
        world.Tile(floor).TerrainId = Dk2TerrainFixture.DirtPath;
        world.Tile(wall).TerrainId = Dk2TerrainFixture.ReinforcedWall;
        world.Tile(wall).OwnerPlayerId = Keeper;
        world.Tile(wall).Health = 1000;
        int half = db[VariableTypeId.TremorDamagePerTerrainTilePerSecond] / 2;
        var draws = new Draws((49, 19), (half, half - 1));
        new GameSimulation(db, world, new SimRandom(draws),
            [new KeeperSpellSystem(), new ShotSystem(), new TremorSystem()]).Tick(InputFrame.Of(Cast()));
        Assert.Equal(1000 - half * 2 + 1, world.Tile(wall).Health);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void TileChainReactsNewestFirstAndDoesNotDamageCreatureHealth_StronglyInferredFromBinary()
    {
        var (db, world) = Setup();
        world.Tile(At).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(At).OwnerPlayerId = Keeper;
        CreatureState older = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, At);
        TrapRules.RelinkCreatures(world);
        CreatureState newer = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, At);
        TrapRules.RelinkCreatures(world);
        older.Dk2StateId = newer.Dk2StateId = CreatureStateMachine.Idle;
        older.Fleeing = newer.Fleeing = true;
        int olderHealth = older.Health, newerHealth = newer.Health;
        var draws = new Draws((49, 24), (100, 4), (100, 5), (5, 2), (5, 2));
        new GameSimulation(db, world, new SimRandom(draws),
            [new KeeperSpellSystem(), new ShotSystem(), new TremorSystem()]).Tick(InputFrame.Of(Cast()));
        Assert.Equal(TremorRules.KnockdownState, newer.Dk2StateId);
        Assert.Equal(6, older.Dk2StateId);
        Assert.Equal((olderHealth, newerHealth), (older.Health, newer.Health));
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void FullSlotsEvictTheFirstOldestTieAndNarrowBeforeCapping_StronglyInferredFromBinary()
    {
        var (db, world) = Setup();
        var sim = new GameSimulation(db, world, 1, [new Probe(context =>
        {
            for (int i = 0; i < 9; i++)
                TremorRules.Start(context, new ShotState { PosX = i, PosY = i + 1, Data1 = 0x10005, Data2 = 31 });
        })]);
        sim.Tick();
        Assert.Equal(8, world.Tremors.Count);
        Assert.Equal(8, world.Tremors[0].WorldX);
        for (int i = 1; i < 8; i++) Assert.Equal(i, world.Tremors[i].WorldX);
        Assert.All(world.Tremors, t =>
        {
            Assert.Equal((ushort)20, t.DurationTicks);
            Assert.Equal((ushort)0x5000, t.Radius);
        });
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        Assert.Equal(world.Tremors.ToArray(), restored.World.Tremors.ToArray());
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }

    [Fact]
    public void UnavailableOrUnaffordableCastsHaveNoShotOrWorldEffect_StronglyInferredFromBinary()
    {
        var (db, world) = Setup();
        world.Player(Keeper).Mana = db.KeeperSpellById(8)!.ManaCost - 1;
        var sim = new GameSimulation(db, world, new SimRandom(new Draws()),
            [new KeeperSpellSystem(), new ShotSystem(), new TremorSystem()]);
        sim.Tick(InputFrame.Of(Cast()));
        Assert.Empty(world.Shots);
        Assert.DoesNotContain(world.Tremors, t => t.Radius != 0);
        world.Player(Keeper).Mana = 90000;
        world.Player(Keeper).Availabilities.Clear();
        sim.Tick(InputFrame.Of(Cast()));
        Assert.Empty(world.Shots);
        Assert.DoesNotContain(world.Tremors, t => t.Radius != 0);
        Assert.Equal(90000, world.Player(Keeper).Mana);
    }

    private sealed class Probe(Action<SimulationContext> action) : ISimulationSystem
    {
        public string Name => "tremor-world-test-probe";
        public void Execute(SimulationContext context) => action(context);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryWorldRecordFieldAffectsHashAndSurvivesSave_StronglyInferredFromBinary(int field)
    {
        var (db, world) = Setup();
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        TremorState record = field switch
        {
            0 => new TremorState { WorldX = 1 },
            1 => new TremorState { WorldY = 1 },
            2 => new TremorState { StartedAt = uint.MaxValue },
            3 => new TremorState { DurationTicks = ushort.MaxValue },
            _ => new TremorState { Radius = 0x5000 },
        };
        Assert.IsAssignableFrom<IList<TremorState>>(world.Tremors)[0] = record;
        Assert.NotEqual(before, sim.StateHash());
        var copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        Assert.Equal(record, copy.World.Tremors[0]);
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    [Fact]
    public void ActiveAndExpiredRecordsSurviveASnapshot_StronglyInferredFromBinary()
    {
        var (db, world) = Setup();
        ISimulationSystem[] systems = [new KeeperSpellSystem(), new ShotSystem(), new TremorSystem()];
        var sim = new GameSimulation(db, world, 7, systems);
        sim.Run(3);
        sim.Tick(InputFrame.Of(Cast()));
        var copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, systems);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        sim.Run(30);
        copy.Run(30);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal(world.Tremors.ToArray(), copy.World.Tremors.ToArray());
        var expired = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, systems);
        Assert.Equal(sim.StateHash(), expired.StateHash());
    }
}
