// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Heart's opening sequence (<c>0x004D9A60</c>, docs/research/heart-intro.md).</summary>
public class HeartIntroTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void WithTheShippedDelay_TheStartLevelEffectComesOnTurnOne_AndTheArchesRingSevenSecondsLater()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        GiveHeart(world, database, Keeper, 3, 3);
        GiveHeart(world, database, Rival, 10, 6);
        var sim = new GameSimulation(database, world, 1, [new DungeonHeartSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);

        sim.Run(HeartIntroRules.SequenceSeconds * SimulationTiming.TicksPerSecond + 4);

        var requests = events.Events.Where(e => e.EffectRequest.HasValue)
            .Select(e => (Turn: e.Tick.Value, e.EffectRequest!.Value.EffectId.Value, Owner: e.EffectRequest.Value.Owner.Value))
            .OrderBy(r => r.Turn).ThenBy(r => r.Owner).ToArray();
        Assert.Equal(new[]
        {
            // Game turn 1 is our tick 0; turn 29 (seven seconds in) is tick 28.
            (0L, HeartIntroRules.StartLevelEffectId, Keeper), (0L, HeartIntroRules.StartLevelEffectId, Rival),
            (28L, HeartIntroRules.ArchesFadeInEffectId, Keeper), (28L, HeartIntroRules.ArchesFadeInEffectId, Rival),
        }, requests);
        var first = events.Events.First(e => e.EffectRequest.HasValue).EffectRequest!.Value;
        // At the Heart's tile centre, one tile up: (x * 0x1000 + 0x800, y * 0x1000 + 0x800, 0x1000).
        Assert.Equal(new FpPosition3(new Fp32(5 * Fp32.One + Fp32.One / 2), new Fp32(5 * Fp32.One + Fp32.One / 2),
            new Fp32(Fp32.One)), first.Position);
        Assert.True(HeartIntroRules.IsViewerOnly(HeartIntroRules.StartLevelEffectId));
        Assert.True(HeartIntroRules.IsViewerOnly(HeartIntroRules.ArchesFadeInEffectId));
        Assert.False(HeartIntroRules.IsViewerOnly(HeartIntroRules.PlugStaticLitEffectId));
    }

    [Fact]
    public void TheArchesAreInvisibleUntilSevenSeconds_ThenGainThirtyTwoATurn_AndAreOpaqueFromNine()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        Assert.Equal(1u, HeartIntroRules.StartTurn(database));
        Assert.Equal(1u, HeartIntroRules.Turn(new GameTick(0)));
        Assert.Equal(0, HeartIntroRules.DecorationAlpha(0, database));
        Assert.Equal(0, HeartIntroRules.DecorationAlpha(28, database));
        Assert.Equal(0, HeartIntroRules.DecorationAlpha(29, database));
        Assert.Equal(32, HeartIntroRules.DecorationAlpha(30, database));
        Assert.Equal(224, HeartIntroRules.DecorationAlpha(36, database));
        Assert.Equal(255, HeartIntroRules.DecorationAlpha(37, database));
        Assert.Equal(255, HeartIntroRules.DecorationAlpha(1000, database));
        Assert.False(HeartIntroRules.SequenceOver(37, database));
        Assert.True(HeartIntroRules.SequenceOver(38, database));
    }

    private static WorldState NewWorld(GameDatabase database) =>
        WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);

    /// <summary>A 5x5 Heart whose top-left tile is (<paramref name="left"/>, <paramref name="top"/>).</summary>
    private static void GiveHeart(WorldState world, GameDatabase database, byte player, int left, int top)
    {
        RoomDef heart = database.Room(RoomTypeId.DungeonHeart);
        for (int y = top; y < top + 5; y++)
            for (int x = left; x < left + 5; x++)
                RoomRules.Place(database, ref world.Tile(new TilePos(x, y)), heart, player);
    }
}
