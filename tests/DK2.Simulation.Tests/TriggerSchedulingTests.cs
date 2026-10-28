// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class TriggerSchedulingTests
{
    [Fact]
    public void AreaAndPlayerTimersSeeAClaimCompletedThisTurn_AndReadTheSameClock()
    {
        // 0050A7D7/0050A7F5: the creature's work precedes list-5 areas and player roots.
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        world.Tile(5, 5).OwnerPlayerId = 2;
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 1, new TilePos(5, 5));
        ThingProjection.Place(world, db, [new ActionPointThing(new byte[56])
        {
            ActionPointId = 1, TriggerId = 1, StartX = 5, EndX = 5, StartY = 5, EndY = 5, Name = "fixture",
        }]);
        var area = Condition(1, 42, 65535, new()
        {
            ["Comparison"] = 5, ["PlayerId"] = 1, ["TerrainId"] = 0, ["Value"] = 1,
        });
        var player = Condition(2, 1, 65533, new()
        {
            ["Comparison"] = 3, ["FlagId"] = 8, ["Flag"] = 1, ["Value"] = 1,
        });
        var setFlag = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 65534, Type = 7,
            Parameters = new Dictionary<string, long> { ["FlagId"] = 8, ["Flag"] = 10, ["Value"] = 1 },
        };
        TriggerRecord stampArea = Timer(65535, 14) with { NextTriggerId = 65534 };
        var tree = new TriggerTree([area, player, stampArea, setFlag, Timer(65533, 15)], 0,
            playerRoots: [new TriggerPlayerRoot(1, 2)]);
        var sim = new GameSimulation(db, world, 1, Scheduled(new ClaimStep()), tree);

        sim.Tick();

        Assert.Equal(1, world.Triggers.ReadTimer(14));
        Assert.Equal(1, world.Triggers.ReadTimer(15));
        Assert.Equal(1, world.Triggers.Read(8));
    }

    [Fact]
    public void CreatureRootSeesItsOwnUpdateBeforeTheNextCreatureRuns()
    {
        // 0048A480: the root walk follows the handler and movement in each creature update.
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        CreatureState older = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 1, new TilePos(5, 5));
        CreatureState newer = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 1, new TilePos(5, 5));
        newer.TriggerId = 1;
        var condition = Condition(1, 36, 65535, new() { ["Comparison"] = 3, ["Value"] = 0 });
        var step = new HealthStep(newer.Id, older.Id);
        var tree = new TriggerTree([condition, Timer(65535, 3)], 0);
        var sim = new GameSimulation(db, world, 1, Scheduled(step), tree);

        sim.Tick();

        Assert.Equal(1, step.StampSeenByOlder);
        Assert.Equal(1, world.Triggers.ReadTimer(3));
    }

    private static IEnumerable<ISimulationSystem> Scheduled(ICreatureUpdateStep step) =>
        SimulationSystems.Default()
            .Where(s => s is CreatureUpdateSystem || s.GetType().Name.EndsWith("TriggerSystem", StringComparison.Ordinal))
            .Select(s => s is CreatureUpdateSystem ? new CreatureUpdateSystem(step) : s);

    [Fact]
    public void AnAdditionalStandaloneStepDoesNotWalkTheCreatureRootAgain()
    {
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 1, new TilePos(5, 5));
        creature.TriggerId = 1;
        var condition = Condition(1, 34, 65535, new() { ["Comparison"] = 5, ["Value"] = 0 })
            with { RepeatCount = 255 };
        var increment = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 65535, Type = 7,
            Parameters = new Dictionary<string, long> { ["FlagId"] = 0, ["Flag"] = 18, ["Value"] = 1 },
        };
        var step = new ClaimStep();
        var sim = new GameSimulation(db, world, 1, Scheduled(step).Append(step),
            new TriggerTree([condition, increment], 0));

        sim.Tick();

        Assert.Equal(1, world.Triggers.Read(0));
    }

    private sealed class ClaimStep : ICreatureUpdateStep
    {
        public string Name => "complete claim";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature) =>
            context.World.Tile(5, 5).OwnerPlayerId = 1;
    }

    private sealed class HealthStep(int newer, int older) : ICreatureUpdateStep
    {
        public string Name => "change health";
        public int StampSeenByOlder { get; private set; }
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature)
        {
            if (creature.Id == newer) creature.Health = 0;
            if (creature.Id == older) StampSeenByOlder = context.World.Triggers.ReadTimer(3);
        }
    }

    private static TriggerRecord Condition(ushort id, byte type, ushort child, Dictionary<string, long> parameters) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 1,
            ChildTriggerId = child, Parameters = parameters,
        };

    private static TriggerRecord Timer(ushort id, byte timer) => new ActionTriggerRecord(new byte[16])
    {
        Body = new byte[8], TriggerId = id, Type = 8,
        Parameters = new Dictionary<string, long> { ["TimerId"] = timer },
    };
}
