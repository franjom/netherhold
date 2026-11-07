// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class KeeperRefreshClockTests
{
    private sealed class Draws(params int[] delays) : IRandomAlgorithm
    {
        private int _nextDelay;
        public List<int> Bounds { get; } = [];
        public int NextInt(int exclusiveMax)
        {
            Bounds.Add(exclusiveMax);
            return exclusiveMax == 50 && delays.Length > 0 ? delays[_nextDelay++] : 12;
        }
        public void HashInto(ref Hash64 hash) { }
    }

    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Fact]
    public void TheFirstKeeperFrameDrawsARefreshDelay()
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [Keeper]);
        var draws = new Draws();
        var sim = new GameSimulation(Db, world, new SimRandom(draws), [new DungeonHeartSystem(frames: false)]);
        draws.Bounds.Clear();
        sim.Tick();
        Assert.Equal(new[] { 50 }, draws.Bounds);
    }

    [Fact]
    public void KeeperDelaysPrecedeTheNeutralFrameInActiveOrder()
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper, (byte)(Keeper + 1)]);
        var draws = new Draws(7, 23);
        var sim = new GameSimulation(Db, world, new SimRandom(draws), [new DungeonHeartSystem(frames: false), new NeutralAmbientEffectSystem()]);
        draws.Bounds.Clear();
        sim.Tick();
        Assert.Equal(new[] { 50, 50, 18, 18 }, draws.Bounds);
        Assert.Equal(new GameTick(108), world.Player((byte)(Keeper + 1)).KeeperRefresh.NextRefreshTick);
        Assert.Equal(new GameTick(124), world.Player(Keeper).KeeperRefresh.NextRefreshTick);
    }

    [Fact]
    public void ADefeatedKeeperDoesNotRunTheClock()
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [Keeper]);
        world.Player(Keeper).KilledByPlayerId = PlayerIds.Good;
        var draws = new Draws();
        var sim = new GameSimulation(Db, world, new SimRandom(draws), [new DungeonHeartSystem(frames: false)]);
        draws.Bounds.Clear();
        sim.Tick();
        Assert.Empty(draws.Bounds);
    }

    [Theory]
    [InlineData(1436, false, 1308, 1438, 0)]
    [InlineData(1437, false, 1438, 1550, 1)]
    [InlineData(1436, true, 1437, 1549, 1)]
    public void DeadlineEqualityOrRequestControlsTheDraw(long tick, bool request, long last, long next, int count)
    {
        var draws = new Draws();
        KeeperRefreshClock result = KeeperRefreshClockRules.Advance(
            new KeeperRefreshClock(new GameTick(1308), new GameTick(1438), request), new GameTick(tick), new SimRandom(draws));
        Assert.Equal(new KeeperRefreshClock(new GameTick(last), new GameTick(next)), result);
        Assert.Equal(count, draws.Bounds.Count);
        Assert.All(draws.Bounds, bound => Assert.Equal(50, bound));
    }

    [Fact]
    public void DeadlineAdditionWrapsAndTheFollowingComparisonRemainsUnsigned()
    {
        var draws = new Draws();
        var rng = new SimRandom(draws);
        KeeperRefreshClock clock = KeeperRefreshClockRules.Advance(default, new GameTick(uint.MaxValue - 2L), rng);
        Assert.Equal(new GameTick(110), clock.NextRefreshTick);
        clock = KeeperRefreshClockRules.Advance(clock, new GameTick(uint.MaxValue - 1L), rng);
        Assert.Equal(new GameTick(uint.MaxValue), clock.LastRefreshTick);
        Assert.Equal(new GameTick(111), clock.NextRefreshTick);
        Assert.Equal(clock, KeeperRefreshClockRules.Advance(clock, new GameTick(uint.MaxValue), rng));
        Assert.Equal(new[] { 50, 50 }, draws.Bounds);
    }

    [Theory]
    [InlineData(PlayerStatus.Won, false)]
    [InlineData(PlayerStatus.Playing, true)]
    public void StatusDoesNotGateTheClockAndBit22KeepsTheKeeperFrameAlive(byte status, bool afterDefeat)
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [Keeper]);
        ref PlayerState player = ref world.Player(Keeper);
        player.Status = status;
        player.KilledByPlayerId = afterDefeat ? PlayerIds.Good : (byte)0;
        player.KeeperFrameAfterDefeat = afterDefeat;
        var draws = new Draws();
        var sim = new GameSimulation(Db, world, new SimRandom(draws), [new DungeonHeartSystem(frames: false)]);
        draws.Bounds.Clear();
        sim.Tick();
        Assert.Equal(new[] { 50 }, draws.Bounds);
        Assert.Equal(status, world.Player(Keeper).Status);
    }

    [Fact]
    public void SaveAndCopyRetainThePendingRequestAndContinueTheSameStream()
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [Keeper]);
        var sim = new GameSimulation(Db, world, new SimRandom(new Dk2Lcg(7)), [new DungeonHeartSystem(frames: false)]);
        sim.Run(5);
        ref PlayerState player = ref world.Player(Keeper);
        player.KeeperRefresh = new KeeperRefreshClock(new GameTick(1308), new GameTick(1438), true);
        player.KeeperFrameAfterDefeat = true;
        player.KilledByPlayerId = PlayerIds.Good;
        GameSimulation loaded = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), Db, [new DungeonHeartSystem(frames: false)]);
        WorldState copy = WorldState.CopyOf(world);
        Assert.Equal(player.KeeperRefresh, loaded.World.Player(Keeper).KeeperRefresh);
        Assert.Equal(player.KeeperRefresh, copy.Player(Keeper).KeeperRefresh);
        Assert.True(loaded.World.Player(Keeper).KeeperFrameAfterDefeat);
        Assert.True(copy.Player(Keeper).KeeperFrameAfterDefeat);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        sim.Tick();
        loaded.Tick();
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(new GameTick(6), loaded.World.Player(Keeper).KeeperRefresh.LastRefreshTick);
        Assert.False(loaded.World.Player(Keeper).KeeperRefresh.Requested);
    }

    [Fact]
    public void EachRetainedClockInputChangesTheHash()
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [Keeper]);
        var sim = new GameSimulation(Db, world, 7, []);
        ulong original = sim.StateHash();
        foreach (KeeperRefreshClock changed in new[]
        {
            new KeeperRefreshClock(new GameTick(1), default),
            new KeeperRefreshClock(default, new GameTick(1)),
            new KeeperRefreshClock(default, default, true),
        })
        {
            world.Player(Keeper).KeeperRefresh = changed;
            Assert.NotEqual(original, sim.StateHash());
        }
        world.Player(Keeper).KeeperRefresh = default;
        world.Player(Keeper).KeeperFrameAfterDefeat = true;
        Assert.NotEqual(original, sim.StateHash());
    }
}
