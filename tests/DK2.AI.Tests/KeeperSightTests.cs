// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using Xunit;

namespace DK2.AI.Tests;

/// <summary>Step 14's Sight of Evil (<c>0x005003A0</c>, docs/research/computer-keeper-spell-sites.md §3):
/// the position is drawn before the mode test, mode 1 casts on <c>random(100) &lt; 2</c>, mode 2 on
/// <c>random(100) &lt; 20</c> at an enemy's tile, and a cast whose permission passes spends one
/// allowance.</summary>
public class KeeperSightTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private const int Side = 30 << 12;

    /// <summary>A keeper with nothing else to draw for: no room is available and its gold is above its
    /// TendancyMoney, so the tick's only draws are step 9's training roll and step 14's.</summary>
    private static WorldState Idle(int sight = 3)
    {
        WorldState world = KeeperWorld.World(Db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        foreach (RoomTypeId room in new[] { RoomTypeId.Treasury, RoomTypeId.Lair, RoomTypeId.Hatchery })
            world.SetAvailability(Keeper, AvailabilityKind.Room, (byte)room, 1);
        world.SetAvailability(Keeper, AvailabilityKind.Spell, KeeperSpellRules.SightOfEvil, (byte)sight);
        return world;
    }

    private static KeeperAI Keeper1(byte mode, SimRandom rng) =>
        new(Keeper, KeeperWorld.Parameters() with { UseSightOfEvil = mode }, rng);

    /// <summary>DK2's draws for one tick, replayed: the training roll, then X, Y and the mode roll.</summary>
    private static (int X, int Y, int Roll) Draws(int seed)
    {
        var twin = new SimRandom((ulong)seed);
        twin.NextInt(100);
        return (twin.NextInt(Side), twin.NextInt(Side), twin.NextInt(100));
    }

    private static int SeedWhere(Func<(int X, int Y, int Roll), bool> wanted)
    {
        for (int seed = 1; ; seed++)
            if (wanted(Draws(seed))) return seed;
    }

    /// <summary>The next value after <paramref name="draws"/> draws from a fresh stream.</summary>
    private static int After(int seed, int draws)
    {
        var twin = new SimRandom((ulong)seed);
        for (int i = 0; i < draws; i++) twin.NextInt(100);
        return twin.NextInt(int.MaxValue);
    }

    [Fact]
    public void ModeOneCastsAtTheDrawnPositionOnARollBelowTwo()
    {
        int seed = SeedWhere(d => d.Roll < 2);
        var rng = new SimRandom((ulong)seed);
        (int x, int y, _) = Draws(seed);

        IReadOnlyList<PlayerCommand> commands = Keeper1(1, rng).Think(Db, Idle());

        Assert.Equal(new CastSpellAtPlaceCommand(Keeper, KeeperSpellRules.SightOfEvil, x, y),
            Assert.Single(commands.OfType<CastSpellAtPlaceCommand>()));
        Assert.Equal(After(seed, 4), rng.NextInt(int.MaxValue));
    }

    [Fact]
    public void ModeOneRollOfTwoCastsNothingAfterTheSameThreeDraws()
    {
        int seed = SeedWhere(d => d.Roll == 2);
        var rng = new SimRandom((ulong)seed);

        Assert.Empty(Keeper1(1, rng).Think(Db, Idle()).OfType<CastSpellAtPlaceCommand>());
        Assert.Equal(After(seed, 4), rng.NextInt(int.MaxValue));
    }

    [Fact]
    public void ModeTwoCastsOnAnUnownedTileAsAnEnemys_UnownedAsEnemyIsQ497()
    {
        int seed = SeedWhere(d => d.Roll < 20
            && (d.X >> 12) is > 0 and < 29 && (d.Y >> 12) is > 0 and < 29
            && Math.Max(Math.Abs((d.X >> 12) - 15), Math.Abs((d.Y >> 12) - 15)) > 2);

        Assert.Single(Keeper1(2, new SimRandom((ulong)seed)).Think(Db, Idle()).OfType<CastSpellAtPlaceCommand>());
    }

    [Fact]
    public void ModeTwoCastsNothingOnTheKeepersOwnTile()
    {
        int seed = SeedWhere(d => d.Roll < 20 && Math.Max(Math.Abs((d.X >> 12) - 15), Math.Abs((d.Y >> 12) - 15)) is > 0 and <= 2);
        var rng = new SimRandom((ulong)seed);

        Assert.Empty(Keeper1(2, rng).Think(Db, Idle()).OfType<CastSpellAtPlaceCommand>());
        Assert.Equal(After(seed, 4), rng.NextInt(int.MaxValue));
    }

    [Fact]
    public void ModeTwoRollOfTwentyCastsNothing()
    {
        int seed = SeedWhere(d => d.Roll == 20);

        Assert.Empty(Keeper1(2, new SimRandom((ulong)seed)).Think(Db, Idle()).OfType<CastSpellAtPlaceCommand>());
    }

    [Fact]
    public void ModeThreeDrawsThePositionAndNothingMore()
    {
        var rng = new SimRandom(5);

        Assert.Empty(Keeper1(3, rng).Think(Db, Idle()).OfType<CastSpellAtPlaceCommand>());
        Assert.Equal(After(5, 3), rng.NextInt(int.MaxValue));
    }

    [Fact]
    public void ModeZeroDrawsNothing()
    {
        var rng = new SimRandom(5);

        Assert.Empty(Keeper1(0, rng).Think(Db, Idle()).OfType<CastSpellAtPlaceCommand>());
        Assert.Equal(After(5, 1), rng.NextInt(int.MaxValue));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void OnlyAvailabilityThreeCastsAndTheRollIsStillDrawn(int availability)
    {
        int seed = SeedWhere(d => d.Roll < 2);
        var rng = new SimRandom((ulong)seed);

        Assert.Empty(Keeper1(1, rng).Think(Db, Idle(availability)).OfType<CastSpellAtPlaceCommand>());
        Assert.Equal(After(seed, 4), rng.NextInt(int.MaxValue));
    }

    /// <summary>With one allowance, a cast that spends it leaves task 0 unrun; Treasury is made
    /// available so that task 0, when it runs, starts the site search.</summary>
    [Theory]
    [InlineData(100000, KeeperTask.Decide)]
    [InlineData(4999, KeeperTask.FindSite)]
    public void APermittedCastSpendsTheAllowanceAndARefusedOneIsStillIssued(int mana, KeeperTask after)
    {
        int seed = SeedWhere(d => d.Roll < 2 && Math.Max(Math.Abs((d.X >> 12) - 15), Math.Abs((d.Y >> 12) - 15)) > 2);
        WorldState world = Idle();
        world.SetAvailability(Keeper, AvailabilityKind.Room, (byte)RoomTypeId.Treasury, 3);
        world.Player(Keeper).Mana = mana;
        KeeperAI keeper = Keeper1(1, new SimRandom((ulong)seed));

        Assert.Single(keeper.Think(Db, world).OfType<CastSpellAtPlaceCommand>());
        Assert.Equal(after, keeper.State.Task);
    }

    [Fact]
    public void TheCaptureRefusesAFiveByFiveHeartsMiddleAndAdmitsEverythingElse()
    {
        WorldState world = Idle();
        for (int y = 13; y <= 17; y++)
            for (int x = 13; x <= 17; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.HeartFloor, Keeper);
        var view = new KeeperView(Db, world, Keeper);
        KeeperView restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(view), Db);

        foreach (KeeperView v in new[] { view, restored })
        {
            Assert.False(v.SightPlaceAllowed(new TilePos(16, 16)));
            Assert.False(v.SightPlaceAllowed(new TilePos(14, 14)));
            Assert.True(v.SightPlaceAllowed(new TilePos(13, 15))); // the Heart's edge
            Assert.True(v.SightPlaceAllowed(new TilePos(5, 5)));   // rock: location 5 admits it
            Assert.False(v.SightPlaceAllowed(new TilePos(30, 5)));
        }
    }

    [Fact]
    public void TheRefusalsAreCapturedOnlyWhileTheSpellIsAvailable()
    {
        WorldState world = Idle(2);
        for (int y = 13; y <= 17; y++)
            for (int x = 13; x <= 17; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.HeartFloor, Keeper);

        Assert.True(new KeeperView(Db, world, Keeper).SightPlaceAllowed(new TilePos(16, 16)));
    }

    [Fact]
    public void ARunCastsThroughTheSimulationWhichCharges()
    {
        WorldState world = Idle();
        var sim = new GameSimulation(Db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        KeeperAI keeper = Keeper1(1, new SimRandom(11));
        int casts = 0;

        for (int tick = 0; tick < 400; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            casts += commands.OfType<CastSpellAtPlaceCommand>().Count();
            sim.Tick(new InputFrame(commands));
        }

        Assert.InRange(casts, 1, 40);
        Assert.Equal(100000 - (casts * 5000), world.Player(Keeper).Mana);
        SimEvent[] regions = events.Events.Where(e => e.SightRegion is not null).ToArray();
        Assert.Equal(casts, regions.Length);
        Assert.All(regions, e =>
        {
            Assert.Equal(Keeper, e.SightRegion!.Value.Viewer.Value);
            Assert.Equal(5, e.SightRegion.Value.RadiusTiles);
            Assert.Equal(64, e.SightRegion.Value.DurationTicks);
        });
        sim.Run(64);
        Assert.Empty(world.Shots);
    }
}
