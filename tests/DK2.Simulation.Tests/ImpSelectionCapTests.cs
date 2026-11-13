// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>State 10's cap (<c>0x00463E2C</c>..<c>0x00463E9C</c>, the counter at player <c>+0x7A</c>):
/// four of a keeper's workers may choose a job in one tick; the fifth and later rest in 12. A
/// just-dropped worker skips the cap and does not use a place (OpenKeeper audit 1000-1110, item 17;
/// openkeeper-1021-review.md).</summary>
public sealed class ImpSelectionCapTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Fact]
    public void FourOfAKeepersWorkersChooseInOneTick_TheRestRest()
    {
        (GameSimulation sim, ListEventSink sink, _) = Six(dropped: -1);

        sim.Run(2);   // state 1 hands each to 10 on the first tick; 10 chooses on the second

        Assert.Equal(4, ImpTaskSystem.SelectionsPerTick);
        Assert.Equal(2, Throttled(sink).Length);
    }

    [Fact]
    public void AJustDroppedWorkerSkipsTheCapAndTakesNoPlace()
    {
        (GameSimulation sim, ListEventSink sink, CreatureState[] imps) = Six(dropped: 0);

        sim.Run(2);   // state 1 hands each to 10 on the first tick; 10 chooses on the second

        string[] throttled = Throttled(sink);
        Assert.Single(throttled);   // five count against the cap, not six
        Assert.DoesNotContain(throttled, d => d.Contains($"imp={imps[0].Id} "));
    }

    [Fact]
    public void TheCapIsPerKeeper()
    {
        (GameSimulation sim, ListEventSink sink, _) = Six(dropped: -1, rivals: 4);

        sim.Run(2);   // state 1 hands each to 10 on the first tick; 10 chooses on the second

        Assert.Equal(2, Throttled(sink).Length);   // the rival's four all choose
    }

    private static string[] Throttled(ListEventSink sink) =>
        sink.Events.Where(e => e.Kind == "throttled").Select(e => e.Detail + " ").ToArray();

    /// <summary>Six idle Imps of the keeper on its claimed floor (and optionally a rival's), all
    /// in state 10 after the first tick.</summary>
    private static (GameSimulation, ListEventSink, CreatureState[]) Six(int dropped, int rivals = 0)
    {
        const byte Rival = Keeper + 1;
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = x < 8 ? Keeper : Rival;
            }
        var imps = new CreatureState[6];
        for (int i = 0; i < imps.Length; i++)
            imps[i] = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(2 + i % 3, 2 + i / 3));
        if (dropped >= 0) imps[dropped].JustDropped = true;
        for (int i = 0; i < rivals; i++)
            world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Rival, new TilePos(10 + i % 2, 2 + i / 2));
        var sim = new GameSimulation(db, world, 11, [new ImpTaskSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink, imps);
    }
}
