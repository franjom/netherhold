// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>DK2's creature list in the thing sweep (<c>0x004B7830</c>, functions/004B7830.md): newest
/// first, one creature's whole update (<c>0x00488930</c>) before the next one's, and inside it the
/// state handler before the steering step (ADR-0554).</summary>
public class CreatureUpdateSystemTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte GoblinId = 7;
    private const int TravelToPoint = 6;

    private static (GameDatabase Db, WorldState World) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        return (db, WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]));
    }

    private sealed class Step(string name, List<string> log, Action<SimulationContext, CreatureState>? act = null)
        : ICreatureUpdateStep
    {
        public string Name => name;
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void BeginTurn(SimulationContext context) => log.Add($"{name} begin");
        public void EndTurn(SimulationContext context) => log.Add($"{name} end");

        public void Update(SimulationContext context, CreatureState creature)
        {
            log.Add($"{name} {creature.Id}");
            act?.Invoke(context, creature);
        }
    }

    [Fact]
    public void EachCreatureTakesEveryStepBeforeTheNextOlderOneBegins()
    {
        // The sweep calls slot 0x14 on a list element and reads the next element only after it
        // returns; the linker 0x004B4A10 puts a new thing at the head.
        (GameDatabase db, WorldState world) = Setup();
        int a = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1)).Id;
        int b = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 1)).Id;
        int c = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 1)).Id;
        var log = new List<string>();

        new GameSimulation(db, world, 1, [new CreatureUpdateSystem(new Step("fight", log), new Step("move", log))]).Tick();

        Assert.Equal(
        [
            "fight begin", "move begin",
            $"fight {c}", $"move {c}", $"fight {b}", $"move {b}", $"fight {a}", $"move {a}",
            "fight end", "move end",
        ], log);
    }

    [Fact]
    public void ACreatureRemovedInItsOwnUpdateTakesNoMoreStepsAndOneMadeDuringTheSweepWaitsForTheNextTick()
    {
        // A thing linked at the list's head during the walk is behind the walk's cursor.
        (GameDatabase db, WorldState world) = Setup();
        int a = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 1)).Id;
        int b = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 1)).Id;
        var log = new List<string>();
        int made = -1;
        var fight = new Step("fight", log, (context, creature) =>
        {
            if (creature.Id != b || made >= 0) return;
            made = context.World.SpawnCreature(context.Database, GoblinId, Keeper, new TilePos(3, 1)).Id;
            context.World.RemoveCreature(b);
        });
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(fight, new Step("move", log))]);

        sim.Tick();
        Assert.Equal(["fight begin", "move begin", $"fight {b}", $"fight {a}", $"move {a}", "fight end", "move end"], log);

        log.Clear();
        sim.Tick();
        Assert.Equal(["fight begin", "move begin", $"fight {made}", $"move {made}", $"fight {a}", $"move {a}", "fight end", "move end"], log);
    }

    [Fact]
    public void ACreatureSentWalkingByItsHandlerStepsThatTickButAnImpWaitsForItsWalkStatesHandler()
    {
        // 0x00488930: the state handler (0x0048A030) precedes the steering step (0x0048A266), whose
        // gate skips the Imp (+0x36F == 1 at 0x0048A210-0x0048A248); an imp steps inside state 6's
        // handler (0x00470C80), which the dispatcher runs from the next update. Native Level 2 and
        // Level 4: non-Imps stepped on the tick they entered 6 in 28-40% of starts, Imps in 22 of
        // 3,891 (D:/DK2Evidence/runs/walk-start-timing-2026-10-27).
        (GameDatabase db, WorldState world) = Setup();
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(1, 1));
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 3));
        var send = new Step("send", [], (context, creature) =>
        {
            if (context.Tick.Value != 0) return;
            CreatureStateMachine.BeginTravel(context.Hooks, creature, TravelToPoint, CreatureStateMachine.Idle);
            Assert.True(context.Navigation.TryRoute(creature, new TilePos(3, creature.Tile.Y), MoveOptions.Plain));
        });
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(send, new CreatureMovementSystem())]);
        (int impX, int goblinX) = (imp.PosX, goblin.PosX);

        sim.Tick();
        Assert.Equal(TravelToPoint, imp.Dk2StateId);
        Assert.Equal(impX, imp.PosX);
        Assert.NotEqual(goblinX, goblin.PosX);

        sim.Tick();
        Assert.NotEqual(impX, imp.PosX);
    }
}
