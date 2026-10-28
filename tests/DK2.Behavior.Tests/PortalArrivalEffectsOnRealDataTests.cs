// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class PortalArrivalEffectsOnRealDataTests
{
    public static IEnumerable<object[]> CreatureEffects()
    {
        ushort[] effects =
        [
            0, 369, 164, 163, 165, 178, 181, 176, 177, 180, 172, 179,
            369, 369, 369, 369, 369, 369, 369, 369, 369, 184, 160, 185,
            369, 369, 0, 0, 369, 369, 369, 179, 181, 165, 180, 178,
            177, 163, 172, 185, 164, 176, 184, 160, 366, 0,
        ];
        for (int i = 0; i < effects.Length; i++) yield return [(byte)(i + 1), effects[i]];
    }

    [InstallTheory]
    [MemberData(nameof(CreatureEffects))]
    public void ScriptedArrivalsRequestEachCreaturesEffectOnFirstAndLaterArrival(byte creatureId, ushort effectId)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "level1");
        TriggerRecord[] actions =
        [
            Arrival(0xFFFF, 0xFFFE, creatureId),
            Arrival(0xFFFE, 0, creatureId),
        ];
        var (world, requests) = Run(db, "level1", actions);

        Assert.Equal(2, world.Creatures.Count);
        Assert.Equal(effectId == 0 ? 0 : 2, requests.Length);
        Assert.All(requests, request =>
        {
            Assert.Equal(new EffectId(effectId), request.EffectId);
            Assert.Equal(new PlayerId(3), request.Owner);
            Assert.Equal(default, request.Heading);
            Assert.Equal(new Fp32(0), request.Position.Z);
        });
    }

    [InstallTheory]
    [InlineData("level1", 65526, 181)]
    [InlineData("level2", 65531, 165)]
    [InlineData("level3", 65477, 180)]
    [InlineData("level3", 65504, 179)]
    [InlineData("level7", 65438, 181)]
    [InlineData("level7", 65439, 181)]
    [InlineData("level7", 65500, 181)]
    [InlineData("level8", 65392, 185)]
    [InlineData("level9", 65463, 164)]
    [InlineData("level12", 65489, 185)]
    public void EveryShippedScriptedArrivalRequestsItsEntranceBeforePromotion(string level, int triggerId, int effectId)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        TriggerRecord action = GameDatabaseLoader.LoadCampaignTriggers(install, level)!
            .Single(record => record.TriggerId == triggerId);
        var (world, requests) = Run(db, level, [action with { ChildTriggerId = 0, NextTriggerId = 0 }]);

        Assert.Single(world.Creatures);
        Assert.Equal((int)action.Parameters["Level"], requests.Length);
        Assert.Equal(new EffectId((ushort)effectId), requests[0].EffectId);
        Assert.All(requests.Skip(1), request => Assert.Equal(new EffectId(339), request.EffectId));
    }

    private static ActionTriggerRecord Arrival(ushort id, ushort next, byte creatureId) => new(new byte[16])
    {
        Body = new byte[8], TriggerId = id, NextTriggerId = next, Type = 28,
        Parameters = new Dictionary<string, long> { ["CreatureId"] = creatureId, ["Level"] = 1 },
    };

    private static (WorldState World, ViewEffectRequest[] Requests) Run(
        GameDatabase db, string level, TriggerRecord[] actions)
    {
        WorldState world = WorldState.FromMap(db,
            GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, level)!, [0, 1, 2, 3]);
        RoomInstance portal = RoomIndex.Build(world, db).First(room => room.Definition.TypeId == RoomTypeId.Portal);
        foreach (int tile in portal.TileIndices) world.TileAtIndex(tile).OwnerPlayerId = 3;
        world.Player(new PlayerId(3)).PortalsClosed = true;
        var tree = new TriggerTree(actions, 0, playerRoots: [new TriggerPlayerRoot(3, actions[0].TriggerId)]);
        var sim = new GameSimulation(db, world, 1, [], tree);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Tick();
        return (world, sink.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest!.Value).ToArray());
    }
}
