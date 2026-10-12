// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class RoomConstructionEffectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachSuccessfullyPlacedTileRequestsTheConstructionEffect(bool rectangle)
    {
        var (simulation, events) = Start();
        BuildRoomCommand command = rectangle
            ? BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 2, 2)
            : BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair, 1, 1, 1, 1);

        simulation.Tick(InputFrame.Of(command));
        if (rectangle) simulation.Run(2);

        SimEvent[] effects = events.Events.Where(e => e.Kind == "construction_effect").ToArray();
        Assert.Equal(command.Tiles.Count, effects.Length);
        Assert.Equal(command.Tiles.OrderBy(t => t.Y).ThenBy(t => t.X),
            effects.Select(e => e.ConstructionEffect!.Value.Tile).OrderBy(t => t.Y).ThenBy(t => t.X));
        Assert.All(effects, e =>
        {
            Assert.Equal(PlayerIds.FirstKeeper, e.ConstructionEffect!.Value.Owner.Value);
            Assert.Equal(6144, e.ConstructionEffect.Value.Height.Raw);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefusedOrAlreadyBuiltTilesDoNotRequestEffects(bool alreadyBuilt)
    {
        var (simulation, events) = Start();
        if (alreadyBuilt)
            RoomRules.Place(simulation.Database, ref simulation.World.Tile(new TilePos(1, 1)),
                simulation.Database.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);

        int coordinate = alreadyBuilt ? 1 : 0;
        simulation.Tick(InputFrame.Of(BuildRoomCommand.Rect(PlayerIds.FirstKeeper, RoomTypeId.Lair,
            coordinate, coordinate, coordinate, coordinate)));

        Assert.DoesNotContain(events.Events, e => e.Kind == "construction_effect");
    }

    private static (GameSimulation, ListEventSink) Start()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 3)), db.Room(RoomTypeId.DungeonHeart), PlayerIds.FirstKeeper);
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 1)), db.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);
        RoomRules.Place(db, ref world.Tile(new TilePos(3, 2)), db.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);
        GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 10000);
        var simulation = new GameSimulation(db, world, 7, [new RoomBuildSystem(), new RoomBuildWaveSystem()]);
        var events = new ListEventSink();
        simulation.ObserveWith(events);
        return (simulation, events);
    }
}
