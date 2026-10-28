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
    public void AreaAndPlayerTimersSeeAClaimCompletedThisTurn_AndStampEachTurnsNumber()
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
        }) with { RepeatCount = 255 };
        var player = Condition(2, 1, 65533, new()
        {
            ["Comparison"] = 3, ["FlagId"] = 8, ["Flag"] = 1, ["Value"] = 1,
        }) with { RepeatCount = 255 };
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

        // Each walk stamps DK2's turn (our tick + 1), not a clock of its own.
        Assert.Equal(1, world.Triggers.ReadTimer(14));
        Assert.Equal(1, world.Triggers.ReadTimer(15));
        Assert.Equal(1, world.Triggers.Read(8));
        sim.Tick();
        Assert.Equal(2, world.Triggers.ReadTimer(14));
        Assert.Equal(2, world.Triggers.ReadTimer(15));
    }

    [Fact]
    public void RootsRunCreatureByCreature_ThenAreas_ThenRoomTicks_ThenPlayers()
    {
        // 0048A480, 0050A7D7/0050A7F5, 0x004B9EEC: creature roots after their own update, list-5
        // areas after the sweep, player roots at the player frame, after the room ticks.
        GameDatabase db = TestWorld.Database();
        WorldState world = TestWorld.World(db);
        world.Tile(5, 5).OwnerPlayerId = 1;
        CreatureState older = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 1, new TilePos(5, 5));
        CreatureState newer = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 1, new TilePos(5, 5));
        older.TriggerId = newer.TriggerId = 1;
        ThingProjection.Place(world, db, [new ActionPointThing(new byte[56])
        {
            ActionPointId = 1, TriggerId = 2, StartX = 5, EndX = 5, StartY = 5, EndY = 5, Name = "fixture",
        }]);
        var tree = new TriggerTree(
        [
            Condition(1, 34, 65535, new() { ["Comparison"] = 5, ["Value"] = 0 }) with { RepeatCount = 255 },
            Condition(2, 42, 65534, new() { ["Comparison"] = 5, ["PlayerId"] = 1, ["TerrainId"] = 0, ["Value"] = 1 }),
            Condition(3, 1, 65533, new() { ["Comparison"] = 3, ["FlagId"] = 9, ["Flag"] = 1, ["Value"] = 0 }),
            SetFlag(65535, 1), SetFlag(65534, 2), SetFlag(65533, 3),
        ], 0, playerRoots: [new TriggerPlayerRoot(1, 3)]);
        var log = new List<string>();
        var systems = new List<ISimulationSystem>();
        foreach (ISimulationSystem system in SimulationSystems.Default())
        {
            if (system is StoreSweepSystem) systems.Add(new FlagProbe("rooms begin", log));
            systems.Add(system is CreatureUpdateSystem update
                ? new CreatureUpdateSystem([.. update.Steps, new FlagProbe("update", log)])
                : system);
            if (system is CreatureUpdateSystem) systems.Add(new FlagProbe("creatures", log));
            if (system is ActionPointTriggerSystem) systems.Add(new FlagProbe("areas", log));
            if (system is RoomHealthSystem) systems.Add(new FlagProbe("rooms end", log));
            if (system is PlayerTriggerSystem) systems.Add(new FlagProbe("players", log));
        }
        var sim = new GameSimulation(db, world, 1, systems, tree);

        sim.Tick();

        Assert.Equal(
        [
            $"update {newer.Id}: 000", $"update {older.Id}: 100", "creatures: 100", "areas: 110",
            "rooms begin: 110", "rooms end: 110", "players: 111",
        ], log);
    }

    [Fact]
    public void AListThatSchedulesSomeRootsButNotAllIsRefused()
    {
        GameDatabase db = TestWorld.Database();
        ISimulationSystem areas = new ActionPointTriggerSystem(), players = new PlayerTriggerSystem();
        ISimulationSystem creatures = new CreatureUpdateSystem();
        foreach (ISimulationSystem[] systems in new[]
        {
            new[] { areas }, [players], [creatures, areas], [creatures, players], [areas, players],
        })
        {
            ArgumentException refused = Assert.Throws<ArgumentException>(
                () => new GameSimulation(db, TestWorld.World(db), 1, systems));
            Assert.Contains("missing", refused.Message, StringComparison.Ordinal);
        }
        _ = new GameSimulation(db, TestWorld.World(db), 1, [creatures]);
        _ = new GameSimulation(db, TestWorld.World(db), 1, [creatures, areas, players]);
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

    private sealed class FlagProbe(string label, List<string> log) : ICreatureUpdateStep
    {
        public string Name => "flag probe";
        public void Execute(SimulationContext context) => log.Add($"{label}: {Flags(context)}");
        public void Update(SimulationContext context, CreatureState creature) =>
            log.Add($"{label} {creature.Id}: {Flags(context)}");
        private static string Flags(SimulationContext context) =>
            $"{context.World.Triggers.Read(1)}{context.World.Triggers.Read(2)}{context.World.Triggers.Read(3)}";
    }

    private static TriggerRecord SetFlag(ushort id, byte flag) => new ActionTriggerRecord(new byte[16])
    {
        Body = new byte[8], TriggerId = id, Type = 7,
        Parameters = new Dictionary<string, long> { ["FlagId"] = flag, ["Flag"] = 10, ["Value"] = 1 },
    };

    private static TriggerRecord Timer(ushort id, byte timer) => new ActionTriggerRecord(new byte[16])
    {
        Body = new byte[8], TriggerId = id, Type = 8,
        Parameters = new Dictionary<string, long> { ["TimerId"] = timer },
    };
}
