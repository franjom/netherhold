// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

public class CommandRoutingTests
{
    private sealed class Recorder : ISimulationSystem
    {
        public string Name => "recorder";

        public List<string> Seen { get; } = [];

        public void Execute(SimulationContext context)
        {
            foreach (TagTilesCommand tag in context.Commands<TagTilesCommand>())
                Seen.Add($"tag {tag.PlayerId} {tag.Tiles[0].X}");
            foreach (SellRoomCommand sell in context.Commands<SellRoomCommand>())
                Seen.Add($"sell {sell.PlayerId}");
            Seen.Add($"tags again {context.Commands<TagTilesCommand>().Count}");
            Seen.Add($"all {context.Input.All.Count}");
        }
    }

    [Fact]
    public void ASystemSeesOnlyItsOwnCommands_InTheOrderTheyWereGiven()
    {
        var recorder = new Recorder();
        GameDatabase database = TestWorld.Database();
        var simulation = new GameSimulation(database, TestWorld.World(database), 1, [recorder]);

        simulation.Tick(InputFrame.Of(
            TagTilesCommand.Tag(4, new TilePos(5, 1)),
            new SellRoomCommand(3, [new TilePos(1, 1)]),
            TagTilesCommand.Tag(3, new TilePos(2, 1)),
            new SellRoomCommand(4, [new TilePos(1, 1)])));

        Assert.Equal(
            ["tag 4 5", "tag 3 2", "sell 3", "sell 4", "tags again 2", "all 4"],
            recorder.Seen);
    }

    [Fact]
    public void ANewTickStartsFromItsOwnCommands()
    {
        var recorder = new Recorder();
        GameDatabase database = TestWorld.Database();
        var simulation = new GameSimulation(database, TestWorld.World(database), 1, [recorder]);

        simulation.Tick(InputFrame.Of(TagTilesCommand.Tag(3, new TilePos(2, 1))));
        recorder.Seen.Clear();
        simulation.Tick();

        Assert.Equal(["tags again 0", "all 0"], recorder.Seen);
    }
}
