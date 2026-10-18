// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public sealed class HudFlashTriggerTests
{
    [Theory]
    [InlineData(0, 242, 1, 3)]
    [InlineData(0, 243, 0, 0)]
    public void ActionNineEmitsTheGlobalFlashDescriptorWithoutChangingGameplay(byte category, byte button,
        byte available, uint duration)
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [PlayerIds.FirstKeeper]);
        TriggerRecord action = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFF, Type = 9,
            Parameters = new Dictionary<string, long>
            {
                ["TargetCategory"] = category, ["TargetId"] = button,
                ["Available"] = available, ["DurationSeconds"] = duration,
            },
        };
        var tree = new TriggerTree([action], action.TriggerId);
        var simulation = new GameSimulation(database, world, seed: 42, [], tree);
        var events = new ListEventSink();
        simulation.ObserveWith(events);
        simulation.Tick();
        var request = Assert.Single(events.Events, e => e.ScriptedView is not null).ScriptedView!.Value;
        Assert.Equal(ScriptedViewKind.FlashButton, request.Kind);
        Assert.Equal(new HudFlashRequest(new HudFlashTarget((ushort)(category | button << 8)), duration, available != 0),
            request.FlashButton);
        Assert.DoesNotContain(simulation.LastTriggerRun!.Partial, x => x.Type == 9);
    }
}
