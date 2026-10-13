// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

/// <summary>The heart's fall and the verdicts it brings — docs/research/win-and-defeat.md,
/// ADR-0162. The blow is dealt directly here (combat lands it in CombatTests), and what is
/// pinned is the heart's own routine and everything DK2 does after the fall.</summary>
public class DungeonHeartTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void HeartHealthObservationRetainsEveryAdvanceAndDoesNotChangeState()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState quiet = NewWorld(db), observed = NewWorld(db);
        var changes = new List<HeartHealthChange>();
        observed.HeartHealthChanged += changes.Add;
        int maximum = observed.Player(Keeper).HeartHealth;
        foreach (WorldState world in new[] { quiet, observed })
        {
            world.DamageHeart(Keeper, 10, Rival);
            world.DamageHeart(Keeper, 0, Rival);
            world.RegenerateHeart(Keeper, 5);
        }
        Assert.Equal(new[] { new HeartHealthChange(Keeper, maximum - 10),
            new HeartHealthChange(Keeper, maximum - 10), new HeartHealthChange(Keeper, maximum - 5) }, changes);
        Assert.Equal(Simulation(db, quiet).StateHash(), Simulation(db, observed).StateHash());
    }

    [Fact]
    public void RegenerationStampsTheOwnerAsTheLastSource_AndAFullHeartKeepsItsAttacker()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        GiveHeart(world, database, Keeper, new TilePos(5, 5));
        world.DamageHeart(Keeper, 10, Rival);
        Assert.Equal(Rival, world.Player(Keeper).HeartLastAttackerPlayerId);
        GameSimulation sim = Simulation(database, world);

        // The room frame passes the owner's own tag as the source of the regeneration (0x004D9F4B).
        sim.Run(SimulationTiming.TicksPerSecond);
        Assert.Equal(Keeper, sim.World.Player(Keeper).HeartLastAttackerPlayerId);

        // Full again: the capped amount is zero and DK2 never makes the call, so a blow's stamp stays.
        sim.World.DamageHeart(Keeper, 0, Rival);
        sim.Run(SimulationTiming.TicksPerSecond * 2);
        Assert.Equal(database[VariableTypeId.DungeonHeartObjectHealth], sim.World.Player(Keeper).HeartHealth);
        Assert.Equal(Rival, sim.World.Player(Keeper).HeartLastAttackerPlayerId);
    }

    [Fact]
    public void AKeeperStartsWithAFullHeartAndNobodyElseHasOne()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);

        Assert.Equal(database[VariableTypeId.DungeonHeartObjectHealth], world.Player(Keeper).HeartHealth);
        Assert.Equal(0, world.Player(PlayerIds.Good).HeartHealth);
        Assert.Equal(0, world.Player(PlayerIds.Neutral).HeartHealth);
    }

    [Fact]
    public void TheHeartRegeneratesOncePerSecondAndNeverPastItsMaximum()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        GiveHeart(world, database, Keeper, new TilePos(5, 5));
        int max = database[VariableTypeId.DungeonHeartObjectHealth];
        int regen = database[VariableTypeId.DungeonHeartHealthRegenerationPerSecond];
        world.Player(Keeper).HeartHealth = max - 2 * regen - 1;
        GameSimulation sim = Simulation(database, world);

        sim.Run(SimulationTiming.TicksPerSecond);
        Assert.Equal(max - regen - 1, sim.World.Player(Keeper).HeartHealth);

        sim.Run(SimulationTiming.TicksPerSecond * 3);
        Assert.Equal(max, sim.World.Player(Keeper).HeartHealth);
    }

    [Fact]
    public void ADeadHeartDoesNotComeBack()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        GiveHeart(world, database, Keeper, new TilePos(5, 5));
        world.DamageHeart(Keeper, world.Player(Keeper).HeartHealth, PlayerIds.Good);
        GameSimulation sim = Simulation(database, world);

        sim.Run(SimulationTiming.TicksPerSecond * 3);

        Assert.Equal(0, sim.World.Player(Keeper).HeartHealth);
    }

    [Fact]
    public void TheKillingBlowStampsTheKillerAndTheTick()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);

        Assert.False(world.DamageHeart(Keeper, 10, Rival));
        Assert.Equal(0, world.Player(Keeper).KilledByPlayerId);
        Assert.True(world.DamageHeart(Keeper, world.Player(Keeper).HeartHealth, PlayerIds.Good));

        ref PlayerState keeper = ref world.Player(Keeper);
        Assert.Equal(0, keeper.HeartHealth);
        Assert.Equal(PlayerIds.Good, keeper.KilledByPlayerId);
        Assert.Equal(world.CurrentTick, keeper.HeartDiedTick);
        Assert.False(keeper.HeartRuined);
        Assert.Equal(PlayerStatus.Playing, keeper.Status);
    }

    [Fact]
    public void AHeartDestroyedByHeroesLosesTheGameAndRuinsTheRoomNextTurn()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        GiveHeart(world, database, Keeper, new TilePos(5, 5));
        world.DamageHeart(Keeper, world.Player(Keeper).HeartHealth, PlayerIds.Good);
        GameSimulation sim = Simulation(database, world);

        sim.Run(1);

        ref PlayerState keeper = ref sim.World.Player(Keeper);
        Assert.Equal(PlayerStatus.Lost, keeper.Status);
        Assert.Equal(new GameTick(0), keeper.StatusTick);
        Assert.True(keeper.HeartRuined);
    }

    [Fact]
    public void AHeartDestroyedByAKeeperIsConquered_AndTheLastKeeperStandingWins()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        world.DamageHeart(Keeper, world.Player(Keeper).HeartHealth, Rival);
        GameSimulation sim = Simulation(database, world);

        sim.Run(1);

        Assert.Equal(PlayerStatus.Conquered, sim.World.Player(Keeper).Status);
        Assert.Equal(PlayerStatus.Won, sim.World.Player(Rival).Status);
        Assert.Equal(PlayerStatus.Playing, sim.World.Player(PlayerIds.Good).Status);
    }

    [Fact]
    public void WithNoRivalNobodyIsAwardedTheWin()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        world.DamageHeart(Keeper, world.Player(Keeper).HeartHealth, PlayerIds.Good);
        GameSimulation sim = Simulation(database, world);

        sim.Run(1);

        Assert.Equal(PlayerStatus.Lost, sim.World.Player(Keeper).Status);
        Assert.Equal(PlayerStatus.Playing, sim.World.Player(PlayerIds.Good).Status);
    }

    [Fact]
    public void TheFirstVerdictStands()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);

        Assert.True(world.EndGame(Keeper, PlayerStatus.Won));
        Assert.False(world.EndGame(Keeper, PlayerStatus.Lost));
        world.DamageHeart(Keeper, world.Player(Keeper).HeartHealth, Rival);
        Simulation(database, world).Run(2);

        Assert.Equal(PlayerStatus.Won, world.Player(Keeper).Status);
    }

    [Fact]
    public void ARuinedHeartStoresNoGoldAndCanBeClaimed()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        // A real heart is 5x5 and only its inner 3x3, the centre platform, stores nothing.
        var heart = new List<TilePos>();
        for (int y = 3; y <= 7; y++)
            for (int x = 3; x <= 7; x++)
                heart.Add(new TilePos(x, y));
        GiveHeart(world, database, Keeper, heart.ToArray());
        int corner = 3 * world.Width + 3;
        ref TileState tile = ref world.TileAtIndex(corner);

        Assert.True(GoldStore.Capacity(world, database, corner) > 0);
        Assert.False(TileWorkRules.Applies(world, database, tile, TileWorkKind.ConvertRoom, Rival));

        world.DamageHeart(Keeper, world.Player(Keeper).HeartHealth, Rival);
        Simulation(database, world).Run(1);

        Assert.True(world.Player(Keeper).HeartRuined);
        Assert.Equal(0, GoldStore.Capacity(world, database, corner));
        Assert.True(TileWorkRules.Applies(world, database, tile, TileWorkKind.ConvertRoom, Rival));
        Assert.False(TileWorkRules.Applies(world, database, tile, TileWorkKind.ConvertRoom, Keeper));
    }

    [Fact]
    public void TheVerdictAndTheHeartSurviveASnapshot()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(database);
        GiveHeart(world, database, Keeper, new TilePos(5, 5));
        world.DamageHeart(Keeper, world.Player(Keeper).HeartHealth, Rival);
        GameSimulation original = Simulation(database, world);
        original.Run(1);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), database);

        ref PlayerState before = ref original.World.Player(Keeper);
        ref PlayerState after = ref restored.World.Player(Keeper);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.StatusTick, after.StatusTick);
        Assert.Equal(before.HeartHealth, after.HeartHealth);
        Assert.Equal(before.HeartLastAttackerPlayerId, after.HeartLastAttackerPlayerId);
        Assert.Equal(before.HeartDiedTick, after.HeartDiedTick);
        Assert.Equal(before.HeartRuined, after.HeartRuined);
        Assert.Equal(PlayerStatus.Won, restored.World.Player(Rival).Status);
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    [Fact]
    public void AContactShotHurtsTheHeartAndRetainsItsAttackMessage()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(db);
        GiveHeart(world, db, Keeper, new TilePos(5, 5));
        int before = world.Player(Keeper).HeartHealth;
        var sim = new GameSimulation(db, world, 1, [new HeartShot()]);
        sim.Tick();
        Assert.Equal(before - 300, world.Player(Keeper).HeartHealth);
        PlayerMessage message = Assert.Single(world.Messages.Entries);
        Assert.Equal((Keeper, PlayerMessageKind.HeartAttacked, 5, 5),
            (message.PlayerId, message.Kind, message.Data1, message.Data2));
    }

    [Fact]
    public void MeleeHeartDamagePostsOnceWithoutRefreshingTheFirstPayload()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = NewWorld(db);
        for (int y = 3; y <= 7; y++)
            for (int x = 3; x <= 7; x++)
                GiveHeart(world, db, Keeper, new TilePos(x, y));
        CreatureState attacker = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 5));
        attacker.SwingTargetKind = FightTargetKind.Heart;
        attacker.SwingTargetId = HeartPillarId.For(world, HeartPillarRules.At(new TilePos(5, 4), HeartPillar.First)).Value;
        attacker.SwingTargetPlayerId = Keeper;
        attacker.FightTargetTileIndex = 5 + 4 * world.Width;
        int before = world.Player(Keeper).HeartHealth;
        var sim = new GameSimulation(db, world, 1, [new HeartMelee(attacker)]);
        sim.Tick();
        Assert.True(world.Player(Keeper).HeartHealth < before);
        PlayerMessage first = Assert.Single(world.Messages.Entries);
        Assert.Equal((Keeper, PlayerMessageKind.HeartAttacked, 3, 3),
            (first.PlayerId, first.Kind, first.Data1, first.Data2));
        sim.Tick();
        PlayerMessage second = Assert.Single(world.Messages.Entries);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1u, second.Age);
    }

    private sealed class HeartMelee(CreatureState attacker) : ISimulationSystem
    {
        public string Name => "test heart melee";
        public void Execute(SimulationContext context) => Combat.FightRules.LandBlow(
            context, attacker, context.Database.CreatureById(attacker.CreatureTypeId));
    }

    private sealed class HeartShot : ISimulationSystem
    {
        public string Name => "test heart shot";
        public void Execute(SimulationContext context) => Combat.ShotRules.ContactDamageHeart(context,
            new ShotState { TargetPlayerId = Keeper, OwnerPlayerId = Rival, Data1 = 300 },
            context.Database.Shots[Dk2TerrainFixture.FireballShotId]);
    }

    private static WorldState NewWorld(GameDatabase database) =>
        WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);

    private static GameSimulation Simulation(GameDatabase database, WorldState world) =>
        new(database, world, 1, [new DungeonHeartSystem()]);

    private static void GiveHeart(WorldState world, GameDatabase database, byte player, params TilePos[] tiles)
    {
        RoomDef heart = database.Room(RoomTypeId.DungeonHeart);
        foreach (TilePos p in tiles)
            RoomRules.Place(database, ref world.Tile(p), heart, player);
    }
}
