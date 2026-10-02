// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A trap's upkeep (<c>0x004C1B60</c>, Q59), its no-mana message (<c>0x004C1A30</c>) and the
/// area damage that reaches it (<c>0x004AC6D0</c>), traps.md §9, §10 and shots.md §4a (ADR-0326).</summary>
public sealed class TrapUpkeepAndDamageTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    private static (GameDatabase db, WorldState world) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static TrapState Place(GameDatabase db, WorldState world, byte trapId, int x, int y, byte owner = Keeper, bool completed = true)
    {
        ref TileState tile = ref world.Tile(x, y);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = owner;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = x, Y = y, TrapId = trapId, PlayerId = owner, NumberOfShots = (byte)(completed ? 1 : 0) }]);
        return world.Traps[^1];
    }

    private static int Upkeep(WorldState world, GameDatabase db, byte player)
    {
        int[] all = ManaRules.UpkeepPerSecondByPlayerIndex(world, db);
        for (int i = 0; i < world.PlayerCount; i++)
            if (world.PlayerAtIndex(i).PlayerId == player) return all[i];
        throw new InvalidOperationException();
    }

    [Fact]
    public void ACompletedTrapDrawsItsDefinitionsUpkeepEverySecond_ABlueprintDrawsNothing()
    {
        (GameDatabase db, WorldState world) = Open();
        Place(db, world, Dk2TerrainFixture.SentryTrapId, 4, 4);
        Place(db, world, Dk2TerrainFixture.LightningTrapId, 6, 4);
        Place(db, world, Dk2TerrainFixture.FreezeTrapId, 8, 4, completed: false);

        Assert.Equal(5 + 25, Upkeep(world, db, Keeper));
    }

    [Fact]
    public void AHerosTrapDrawsNoUpkeep_BecauseDk2PaysKeepersAloneAndOursPaysEveryPlayer()
    {
        (GameDatabase db, WorldState world) = Open();
        Place(db, world, Dk2TerrainFixture.HeroSentryTrapId, 4, 4, owner: PlayerIds.Good);

        Assert.Equal(0, Upkeep(world, db, PlayerIds.Good));
    }

    [Fact]
    public void TheUpkeepComesOffTheKeepersManaWithTheSecondsPayment()
    {
        (GameDatabase db, WorldState world) = Open();
        Place(db, world, Dk2TerrainFixture.FireburstTrapId, 4, 4);
        world.Player(Keeper).Mana = 1000;
        var sim = new GameSimulation(db, world, 1, [new ManaSystem()]);

        sim.Run(SimulationTiming.TicksPerSecond);

        // Land: the one claimed tile, 1 a second; upkeep 10.
        Assert.Equal(1000 + 1 - 10, world.Player(Keeper).Mana);
    }

    [Fact]
    public void ATrapShortOfManaPostsMessage53Subtype1_ThenNotAgainForFifteenSeconds()
    {
        (GameDatabase db, WorldState world) = Open();
        Place(db, world, Dk2TerrainFixture.FreezeTrapId, 5, 5);
        world.Player(Keeper).Mana = 0;
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 5));
        goblin.Health = 100000;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new TrapSystem()]);
        sim.ObserveWith(sink);

        sim.Run(3);
        PlayerMessage message = Assert.Single(world.Messages.Entries, m => m.Kind == PlayerMessageKind.ManaShortfall);
        Assert.Equal((Keeper, TrapRules.TrapNoManaSubtype), (message.PlayerId, message.Data1));
        long first = world.Player(Keeper).TrapNoManaMessageAt;
        Assert.InRange(first, 15 * SimulationTiming.OriginalTicksPerGameSecond, 15 * SimulationTiming.OriginalTicksPerGameSecond + 3);

        sim.Run(40);
        Assert.True(sink.Events.Count(e => e.Kind == "trap_no_mana") > 10);
        Assert.Equal(first, world.Player(Keeper).TrapNoManaMessageAt);
        sim.Run(30);
        Assert.Equal(first + (15 * SimulationTiming.OriginalTicksPerGameSecond), world.Player(Keeper).TrapNoManaMessageAt);
    }

    [Fact]
    public void AFireburstHurtsARivalsTrapWithinItsRadius_NotItsOwnersTrap()
    {
        // The burst stands 1761 north of its trap's centre at (5,5): (5,4)'s centre is within a tile.
        (GameDatabase db, WorldState world) = Open();
        TrapState fireburst = Place(db, world, Dk2TerrainFixture.FireburstTrapId, 5, 5);
        TrapState rival = Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 4, owner: Rival);
        TrapState own = Place(db, world, Dk2TerrainFixture.SentryTrapId, 4, 5);
        world.Player(Keeper).Mana = 1000;
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 5));
        goblin.Health = 100000;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new ShotSystem(), new TrapSystem(), new TrapRemovalSystem()]);
        sim.ObserveWith(sink);

        sim.Run(4);

        Assert.Contains(sink.Events, e => e.Kind == "shot_area_trap_hit" && e.Detail.Contains($"trap={rival.Id} "));
        Assert.True(rival.Health < db.Traps[Dk2TerrainFixture.SentryTrapId].Health);
        Assert.Equal(db.Traps[Dk2TerrainFixture.SentryTrapId].Health, own.Health);
        Assert.Equal(db.Traps[Dk2TerrainFixture.FireburstTrapId].Health, fireburst.Health);
    }

    [Fact]
    public void AnAreaShotsSecondTargetTakesTheFirstHitsDamageScaledAgain_TheDamageWordIsOverwritten()
    {
        // 0x004AC8BF stores each hit's result back in the damage word, so two goblins on one spot
        // take d1 = data1 (R - s) / R, then d1 (R - s) / R.
        (GameDatabase db, WorldState world) = Open();
        Place(db, world, Dk2TerrainFixture.FireburstTrapId, 5, 5);
        world.Player(Keeper).Mana = 1000;
        CreatureState first = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 5));
        CreatureState second = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 5));
        first.Health = second.Health = 100000;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new ShotSystem(), new TrapSystem()]);
        sim.ObserveWith(sink);

        sim.Run(4);

        long tick = sink.Events.First(e => e.Kind == "shot_area_hit").Tick.Value;
        List<int> damages = sink.Events.Where(e => e.Kind == "shot_area_hit" && e.Tick.Value == tick)
            .Select(e => int.Parse(e.Detail.Split(' ').First(f => f.StartsWith("damage=", StringComparison.Ordinal))[7..])).ToList();
        Assert.Equal(2, damages.Count);
        ShotState burst = Assert.Single(world.Shots);
        int radius = burst.Data2 * Fp32.One;
        long s = ShotRules.Isqrt((long)TrapRules.DistanceSquared(burst.PosX, burst.PosY, first.PosX, first.PosY) << 12);
        int d1 = (int)((radius - s) * burst.Data1 / radius);
        Assert.Equal([d1, (int)((radius - s) * d1 / radius)], damages);
    }

    [Theory]
    // An enemy's trap counts 0x800 wide: the box's half is (2 x 819 + 2048) / 2 = 1843.
    [InlineData(Rival, 1800, true)]
    [InlineData(Rival, 1850, false)]
    // The owner's own trap counts 0 wide, half 819, and the predicate has no owner test.
    [InlineData(Keeper, 800, true)]
    [InlineData(Keeper, 1800, false)]
    public void ACannonBallStrikesACompletedTrapItOverlaps_AnEnemysCountedWiderThanItsOwn(byte trapOwner, int offset, bool struck)
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trap = Place(db, world, Dk2TerrainFixture.SentryTrapId, 8, 8, owner: trapOwner);
        ShotDef ball = db.Shots[Dk2TerrainFixture.CannonBallShotId];
        ShotRules.Spawn(world, ball, Keeper, trap.PosX + offset, trap.PosY, 250, 0, 0, FightTargetKind.None, CombatRules.NoTarget, 0, -1,
            trap.PosX + offset, trap.PosY + 0x4000);
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new ShotSystem()]);
        sim.ObserveWith(sink);

        sim.Run(1);

        int full = db.Traps[Dk2TerrainFixture.SentryTrapId].Health;
        Assert.Equal(struck ? full - 250 : full, trap.Health);
        Assert.Equal(struck, sink.Events.Any(e => e.Kind == "shot_trap_hit"));
    }

    [Fact]
    public void ABlueprintIsNoShotsTarget()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState blueprint = Place(db, world, Dk2TerrainFixture.SentryTrapId, 8, 8, owner: Rival, completed: false);
        ShotRules.Spawn(world, db.Shots[Dk2TerrainFixture.CannonBallShotId], Keeper, blueprint.PosX, blueprint.PosY, 250, 0, 0,
            FightTargetKind.None, CombatRules.NoTarget, 0, -1, blueprint.PosX, blueprint.PosY + 0x4000);
        var sim = new GameSimulation(db, world, 1, [new ShotSystem()]);

        sim.Run(1);

        Assert.Equal(db.Traps[Dk2TerrainFixture.SentryTrapId].Health, blueprint.Health);
    }

    [Fact]
    public void ThePlayersNoManaClockSurvivesASnapshotRoundTrip()
    {
        (GameDatabase db, WorldState world) = Open();
        world.Player(Keeper).TrapNoManaMessageAt = 1234;
        var sim = new GameSimulation(db, world, 1, [new TrapSystem()]);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new TrapSystem()]);

        Assert.Equal(1234, copy.World.Player(Keeper).TrapNoManaMessageAt);
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }
}
