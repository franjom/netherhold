// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Behavior.Tests;

/// <summary>
/// The test-controller hook (ADR-0552 Stage A): a controller's commands enter the simulation
/// through the same input frame as scripted commands, one tick after the view it decided on.
/// Runs on the synthetic database, so no DK2 installation is needed.
/// </summary>
public class ScenarioControllerTests
{
    private static readonly PlayerId Keeper = new(3);
    private static readonly TilePos Rock = new(6, 4);

    [Fact]
    public void AControllersCommand_IsTheSameInputAsTheScriptedCommandOneTickLater()
    {
        // The controller decides after tick 0, so its tag lands in tick 1's frame. A scenario
        // that scripts the same tag at tick 1 must produce the same hash on every tick.
        ScenarioResult controlled = Run(Yaml(), TagOnce());
        ScenarioResult scripted = Run(Yaml(commands: "  - { at_tick: 1, player: 3, tag: [[6, 4]] }"));
        ScenarioResult untouched = Run(Yaml());

        Assert.Equal(scripted.HashPerTick, controlled.HashPerTick);
        Assert.NotEqual(untouched.FinalStateHash, controlled.FinalStateHash);
        ControllerCommandEmission emission = Assert.Single(controlled.ControllerCommandLog);
        Assert.Equal(1, emission.InputTick);
        Assert.Equal("tag-once", emission.Controller);
    }

    [Fact]
    public void AControlledRun_IsReproducible()
    {
        Assert.Equal(Run(Yaml(), TagOnce()).HashPerTick, Run(Yaml(), TagOnce()).HashPerTick);
    }

    [Fact]
    public void AController_DecidesOnlyOnItsInterval()
    {
        var ticks = new List<long>();
        var controller = new ScenarioController("counter", Keeper, view =>
        {
            ticks.Add(view.Tick.Value);
            return [];
        })
        { IntervalTicks = 4 };

        Run(Yaml(), controller);

        // Views are captured after ticks 0, 4 and 8 of the 10 run; the world's tick has advanced past each.
        Assert.Equal([1L, 5L, 9L], ticks);
    }

    [Fact]
    public void AControllerCommandingAnotherPlayer_FailsTheRun()
    {
        var rogue = new ScenarioController("rogue", Keeper,
            _ => [TagTilesCommand.Tag(new PlayerId(1), Rock)]);

        var thrown = Assert.Throws<InvalidOperationException>(() => Run(Yaml(), rogue));
        Assert.Contains("rogue", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AControllerForAPlayerTheWorldLacks_IsRefusedBeforeTheRun()
    {
        var absent = new ScenarioController("absent", new PlayerId(5), _ => []);

        Assert.Throws<ArgumentException>(() => Run(Yaml(), absent));
    }

    [Fact]
    public void TwoControllersForOnePlayer_AreRefused()
    {
        var first = new ScenarioController("first", Keeper, _ => []);
        var second = new ScenarioController("second", Keeper, _ => []);

        Assert.Throws<ArgumentException>(() => Run(Yaml(), first, second));
    }

    [Fact]
    public void AControllerWithNoInterval_IsRefusedBeforeTheRun()
    {
        var never = new ScenarioController("never", Keeper, _ => []) { IntervalTicks = 0 };

        var thrown = Assert.Throws<ArgumentException>(() => Run(Yaml(), never));
        Assert.Contains("IntervalTicks", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AControllerReturningNull_FailsTheRunByName()
    {
        var broken = new ScenarioController("broken", Keeper, _ => null!);

        var thrown = Assert.Throws<InvalidOperationException>(() => Run(Yaml(), broken));
        Assert.Contains("broken", thrown.Message, StringComparison.Ordinal);
    }

    private static ScenarioController TagOnce()
    {
        bool done = false;
        return new ScenarioController("tag-once", Keeper, (KeeperView view) =>
        {
            if (done || !view.IsDiggable(Rock)) return [];
            done = true;
            return [TagTilesCommand.Tag(view.PlayerId, Rock)];
        });
    }

    private static ScenarioResult Run(string yaml, params ScenarioController[] controllers)
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            return new ScenarioRunner(ScenarioTests.SyntheticDatabase(), null).Run(Scenario.Load(path), controllers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Yaml(string? commands = null) => $"""
        id: controller-probe
        map: chamber_9x9
        seed: 7
        duration_ticks: 10
        setup:
          players:
            - {"{"} id: 3, gold: 0, mana: 0 {"}"}
        {(commands is null ? "" : "commands:\n" + commands)}
        """;
}
