// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The trap update (<c>0x004A84E0</c>, traps.md) on an open map whose trap tiles the trap's
/// keeper owns: the sight, tile and Trigger selectors, the cost, the recharge, the threat and the
/// Trigger's reach. Only the shots and the traps run, so nobody walks.</summary>
public class TrapFiringTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    private static (GameDatabase db, WorldState world) Open(params CreatureDef[] extra)
    {
        GameDatabase db = Dk2TerrainFixture.Database(extra);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        world.Player(Keeper).Mana = 1000;
        return (db, world);
    }

    private static TrapState Place(GameDatabase db, WorldState world, byte trapId, int x, int y, byte owner = Keeper)
    {
        ref TileState tile = ref world.Tile(x, y);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = owner;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = x, Y = y, TrapId = trapId, PlayerId = owner, NumberOfShots = 1 }]);
        return world.Traps[^1];
    }

    private static CreatureState Enemy(GameDatabase db, WorldState world, int x, int y, byte type = Dk2TerrainFixture.GoblinCreatureId)
    {
        CreatureState creature = world.SpawnCreature(db, type, Rival, new TilePos(x, y));
        creature.Health = 100000;
        return creature;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem(), new ShotSystem(), new TrapSystem(), new TrapRemovalSystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    private static List<long> Fired(ListEventSink sink, TrapState trap) =>
        sink.Events.Where(e => e.Kind == "trap_fired" && e.Detail.StartsWith($"trap={trap.Id} ")).Select(e => e.Tick.Value).ToList();

    [Fact]
    public void ASentryTurnsToAnEnemyInRange_FiresACannonBallFromItsTurnedMuzzle_PaysItsMana_AndRechargesFor24Ticks()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 5);
        CreatureState goblin = Enemy(db, world, 8, 5);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        // Turn 0: the constructor's zeroed cache answers nobody, so the Sentry sweeps ten steps and holds fire.
        sim.Run(1);
        Assert.Equal(10, sentry.Heading);
        Assert.Empty(world.Shots);
        sim.Run(1);
        // Turn 1: turned first, at most 0x100 toward the goblin's bearing 0x200; then fired at a charge threshold of 0.
        Assert.Equal(10 + 0x100, sentry.Heading);
        ShotState ball = Assert.Single(world.Shots);
        Assert.Equal(Dk2TerrainFixture.CannonBallShotId, ball.ShotTypeId);
        // The offset (0, -2048) turned by the heading through the sine table, each product shifted down 16.
        Assert.Equal((sentry.PosX - ((-2048 * TrapRules.Sine(sentry.Heading)) >> 16), sentry.PosY + ((-2048 * TrapRules.Cosine(sentry.Heading)) >> 16)),
            (ball.PosX, ball.PosY));
        Assert.True(ball.VelX > 0);
        Assert.False(ball.ForcedCollision);
        Assert.Equal((250, 250), (ball.Data1, world.Player(Keeper).OneShotManaSpent));
        Assert.Equal(750, world.Player(Keeper).Mana);
        Assert.Equal(25, sentry.RechargeAt);

        sim.Run(30);
        Assert.Equal([1L, 25L], Fired(sink, sentry));
        Assert.Equal(500, world.Player(Keeper).Mana);
        Assert.Contains(sink.Events, e => e.Kind == "shot_hit" && e.Detail.Contains($"target={goblin.Id}"));
    }

    [Fact]
    public void AKeeperShortOfManaDoesNotFire_AndKeepsItsCharge_WhileTheGoodPlayersSentryPaysNothing()
    {
        (GameDatabase db, WorldState world) = Open();
        world.Player(Keeper).Mana = 249;
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 5);
        Enemy(db, world, 8, 5);
        TrapState hero = Place(db, world, Dk2TerrainFixture.HeroSentryTrapId, 5, 12, PlayerIds.Good);
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 12)).Health = 100000;
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Run(4);
        Assert.Empty(Fired(sink, sentry));
        Assert.Equal(3, sink.Events.Count(e => e.Kind == "trap_no_mana" && e.Detail.StartsWith($"trap={sentry.Id} ")));
        Assert.Equal(3, sentry.Charge);
        Assert.Equal(249, world.Player(Keeper).Mana);
        Assert.Equal([1L], Fired(sink, hero));
    }

    [Fact]
    public void AFreezeTrapFreezesTheLatestEnemyOnItsTile_ThroughAForcedFirstCollision_AndPassesOverAFlyer()
    {
        CreatureDef flyer = Dk2TerrainFixture.Goblin() with { CreatureId = 60, Name = "Flyer", Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.CanFly };
        (GameDatabase db, WorldState world) = Open(flyer);
        TrapState freeze = Place(db, world, Dk2TerrainFixture.FreezeTrapId, 5, 5);
        CreatureState first = Enemy(db, world, 5, 5);
        Enemy(db, world, 6, 5);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Run(1);
        ShotState shot = Assert.Single(world.Shots);
        Assert.True(shot.ForcedCollision);
        Assert.Equal(first.Id, shot.TargetId);
        sim.Run(1);
        Assert.Contains(sink.Events, e => e.Kind == "frozen" && e.Detail.StartsWith($"creature={first.Id} ticks={20 * 4}"));

        // A flyer that came onto the tile later heads the chain: selector 2 refuses it and does not look further.
        CreatureState bat = Enemy(db, world, 5, 5, type: 60);
        sim.Run(30);
        Assert.Equal(bat.Id, TrapRules.FirstOnTile(world, 5, 5, Keeper)!.Id);
        Assert.Single(Fired(sink, freeze));
    }

    [Fact]
    public void ALightningTrapStrikesItsTargetOnTheShotsFirstTick()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trap = Place(db, world, Dk2TerrainFixture.LightningTrapId, 5, 5);
        CreatureState goblin = Enemy(db, world, 8, 5);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Run(3);
        Assert.Equal([1L], Fired(sink, trap));
        Assert.Contains(sink.Events, e => e.Kind == "lightning" && e.Detail.StartsWith($"target={goblin.Id} ") && e.Detail.Contains("damage=400"));
        Assert.Equal(625, world.Player(Keeper).Mana);
    }

    [Fact]
    public void AFireburstStandsAtItsMuzzleAndBurnsTheEnemiesWithinItsTile()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trap = Place(db, world, Dk2TerrainFixture.FireburstTrapId, 5, 5);
        // Its range of one tile is strict: only a creature on its own tile is seen.
        CreatureState goblin = Enemy(db, world, 5, 5);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Run(3);
        ShotState burst = Assert.Single(world.Shots);
        Assert.Equal((0, 0), (burst.VelX, burst.VelY));
        Assert.Equal((trap.PosX, trap.PosY - 1761), (burst.PosX, burst.PosY));
        Assert.Contains(sink.Events, e => e.Kind == "shot_area_hit" && e.Detail.Contains($"target={goblin.Id}"));
    }

    [Fact]
    public void ATriggerFiresTheTrapBesideItAtItsCreature_ThreeTurnsLater_WhileThatTrapsOwnSightIsSuppressed()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trigger = Place(db, world, Dk2TerrainFixture.TriggerTrapId, 5, 5);
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 6, 5);
        TrapState far = Place(db, world, Dk2TerrainFixture.SentryTrapId, 7, 6);
        Assert.Equal((1, 0), (sentry.TriggerCount, far.TriggerCount));
        CreatureState goblin = Enemy(db, world, 5, 5);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Run(5);
        Assert.Equal([0L], Fired(sink, trigger));
        // Countdown 4, decremented the turn it is set: the reach is one tile once it reaches 0, on turn 3.
        Assert.Equal([3L], Fired(sink, sentry));
        Assert.Contains(sink.Events, e => e.Kind == "trap_fired" && e.Detail.StartsWith($"trap={sentry.Id} ") && e.Detail.Contains($"target={goblin.Id}"));
        // The diagonal Sentry, not beside the Trigger, saw the goblin itself on turn 1.
        Assert.Equal([1L], Fired(sink, far));
    }

    [Fact]
    public void TheThreatIsSharedAmongTheEnemiesInTheOuterRadius_AndLandsOnlyInsideTheInnerOne()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 2, 10);
        CreatureState inner = Enemy(db, world, 6, 10);   // 4 tiles: inside the inner radius of 5
        CreatureState outer = Enemy(db, world, 7, 10);   // 5 tiles: inside the outer 6, not the inner 5
        CreatureState beyond = Enemy(db, world, 9, 10);  // 7 tiles: outside both

        TrapRules.ShareThreat(world, new NoWalls(), sentry, 74, (5 + 1) << 12);
        Assert.Equal((37, 0, 0), (inner.TempFear, outer.TempFear, beyond.TempFear));
    }

    [Fact]
    public void AFiredSentryKeepsSharingItsThreatEveryTurn_AsNothingInTheTrapsCodeClearsIt_ReadNotObserved()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 5);
        CreatureState goblin = Enemy(db, world, 8, 5);
        GameSimulation sim = Sim(db, world, new ListEventSink());

        sim.Run(2);
        Assert.Equal((74, (byte)180), (sentry.Threat, sentry.ThreatDuration));
        Assert.Equal(74, goblin.TempFear);
        world.Player(Keeper).Mana = 0; // no second firing to restart the clock
        sim.Run(200);
        Assert.Equal((74, (byte)0), (sentry.Threat, sentry.ThreatDuration));
        Assert.Equal(74 * 201 % 65536, goblin.TempFear);
    }

    [Fact]
    public void ATrapOnATileItsKeeperHasLostIsRemovedThatTurn_AndARemovedTriggerLeavesItsNeighboursCount()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trigger = Place(db, world, Dk2TerrainFixture.TriggerTrapId, 5, 5);
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 6);
        Assert.Equal(1, sentry.TriggerCount);
        world.Tile(5, 5).OwnerPlayerId = Rival;
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Run(1);
        Assert.DoesNotContain(trigger, world.Traps);
        Assert.Equal(0, sentry.TriggerCount);
        Assert.Contains(sink.Events, e => e.Kind == "trap_owner_lost" && e.Detail.StartsWith($"trap={trigger.Id} "));
    }

    [Fact]
    public void ATrapRecoversItsGainEachGameSecond_UpToItsHealth()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 5);
        sentry.Health = 2990;
        GameSimulation sim = Sim(db, world, new ListEventSink());

        sim.Run(1);
        Assert.Equal((2990, 0L), (sentry.Health, sentry.RecoveryAt)); // strictly past the deadline
        sim.Run(1);
        Assert.Equal((2996, 4L), (sentry.Health, sentry.RecoveryAt));
        sim.Run(3);
        Assert.Equal(2996, sentry.Health);
        sim.Run(1);
        Assert.Equal((3000, 8L), (sentry.Health, sentry.RecoveryAt));
    }

    [Fact]
    public void ACreatureNotFightingThatATrapFiresAtWalksToTheTrap()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 5);
        CreatureState goblin = Enemy(db, world, 8, 5);
        var sink = new ListEventSink();
        Sim(db, world, sink).Run(2);

        Assert.Contains(sink.Events, e => e.Kind == "walks_to_trap" && e.Detail == $"creature={goblin.Id} trap={sentry.Id}");
        Assert.True(goblin.IsRouted);
        Assert.Equal(new TilePos(5, 5), goblin.Destination);
    }

    [Fact]
    public void TheSineTableIsTheBinarysOwn()
    {
        Assert.Equal([0, 201, 402, 603, 804, 1005], Enumerable.Range(0, 6).Select(TrapRules.Sine));
        Assert.Equal((46340, 65535, 65536, -65536), (TrapRules.Sine(256), TrapRules.Sine(511), TrapRules.Sine(512), TrapRules.Sine(1536)));
        Assert.Equal(TrapRules.Sine(512), TrapRules.Cosine(0));
        Assert.Equal(TrapRules.Sine(0), TrapRules.Cosine(1536));
    }

    [Fact]
    public void TrapRuntimeWordsAndTheTileChainSurviveASnapshot()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState sentry = Place(db, world, Dk2TerrainFixture.SentryTrapId, 5, 5);
        Enemy(db, world, 8, 5);
        GameSimulation before = Sim(db, world, new ListEventSink());
        before.Run(3);

        GameSimulation after = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(before), db, []);
        TrapState restored = Assert.Single(after.World.Traps);
        Assert.Equal((sentry.Heading, sentry.RechargeAt, sentry.Threat, sentry.SightTurn), (restored.Heading, restored.RechargeAt, restored.Threat, restored.SightTurn));
        Assert.Equal(before.StateHash(), after.StateHash());
    }

    private sealed class NoWalls : ISightBlocker
    {
        public bool Blocks(int x, int y) => false;
    }
}
