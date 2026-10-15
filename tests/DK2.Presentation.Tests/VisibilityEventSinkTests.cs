// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class VisibilityEventSinkTests
{
    [Fact]
    public void SightAdmitsAnEffectInTheNextTickWithoutAFrameDrain()
    {
        var (simulation, view) = Fixture();
        var received = new List<bool>();
        var sink = new VisibilityEventSink(view, simulation.World, new PlayerId(1),
            new VisibilityRecorder(view, simulation.World, new TilePos(10, 10), received));

        sink.Emit(Sight(simulation.CurrentTick, new TilePos(10, 10), 1));
        simulation.Run(1);
        sink.Emit(Effect(simulation.CurrentTick));

        Assert.Equal(new[] { false, true }, received);
    }

    [Fact]
    public void ReplacementRegionIsAppliedBeforeForwardingItsEvent()
    {
        var (simulation, view) = Fixture();
        var received = new List<bool>();
        var sink = new VisibilityEventSink(view, simulation.World, new PlayerId(1),
            new VisibilityRecorder(view, simulation.World, new TilePos(10, 10), received));
        sink.Emit(Sight(simulation.CurrentTick, new TilePos(10, 10), 1));
        simulation.Run(24);
        sink.Emit(Effect(simulation.CurrentTick));
        sink.Emit(Sight(simulation.CurrentTick, new TilePos(25, 25), 1));
        sink.Emit(Effect(simulation.CurrentTick));

        Assert.Equal(new[] { false, true, false, false }, received);
    }

    [Fact]
    public void PossessionChangesAreAppliedBeforeFilteringEachEffect()
    {
        var (simulation, view) = Fixture();
        var received = new List<bool>();
        var sink = new VisibilityEventSink(view, simulation.World, new PlayerId(1),
            new VisibilityRecorder(view, simulation.World, new TilePos(10, 10), received));
        sink.Emit(Effect(simulation.CurrentTick));
        simulation.World.Player(new PlayerId(1)).PossessedCreatureId = 9;
        sink.Emit(Effect(simulation.CurrentTick));
        simulation.World.Player(new PlayerId(1)).PossessedCreatureId = 0;
        sink.Emit(Effect(simulation.CurrentTick));

        Assert.Equal(new[] { false, true, false }, received);
    }

    [Fact]
    public void AnotherViewersSightCannotReplaceTheLocalRegion()
    {
        var (simulation, view) = Fixture();
        var received = new List<bool>();
        var sink = new VisibilityEventSink(view, simulation.World, new PlayerId(1),
            new VisibilityRecorder(view, simulation.World, new TilePos(10, 10), received));
        sink.Emit(Sight(simulation.CurrentTick, new TilePos(10, 10), 1));
        simulation.Run(24);
        sink.Emit(Sight(simulation.CurrentTick, new TilePos(25, 25), 2));
        sink.Emit(Effect(simulation.CurrentTick));

        Assert.Equal(new[] { false, true, true }, received);
    }

    private sealed class VisibilityRecorder(WorldView view, WorldState world, TilePos tile,
        List<bool> received) : ISimulationEventSink
    {
        public void Emit(in SimEvent simEvent) => received.Add(view.CanShowTile(world, tile));
    }

    private static SimEvent Sight(GameTick tick, TilePos tile, byte viewer) => new(tick, "test", "sight", "")
    {
        SightRegion = new SightRegionRequest(new PlayerId(viewer), tile, 64, 5),
    };

    private static SimEvent Effect(GameTick tick) => new(tick, "test", "effect", "");

    private static (GameSimulation Simulation, WorldView View) Fixture()
    {
        var terrain = new TerrainDef
        {
            Name = "Floor", TerrainId = 0, Flags = 0,
            StartingHealth = 1, MaxHealth = 1, GoldValue = 0, ManaGain = 0, MaxManaGain = 0,
            Damage = 0, MaxHealthTypeTerrainId = 0, DestroyedTypeTerrainId = 0,
            Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var database = new GameDatabase([terrain], [], [], new GlobalVariables());
        var tiles = Enumerable.Repeat(new MapTile(0, 0, 0, 0), 900).ToArray();
        WorldState world = WorldState.FromMap(database, new LevelMap(30, 30, tiles), [1, 2], true);
        return (new GameSimulation(database, world, 1, []), new WorldView(database, 1));
    }
}
