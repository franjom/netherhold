// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Action 26's dig and claim arm and the work actions it starts
/// (possession-arms-and-inputs.md §3).</summary>
public sealed class PossessionDigTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const ushort East = 0x200, North = 0, Level = 0x200;

    private static (GameDatabase Db, WorldState World) Open(bool workerPossessor = false)
    {
        CreatureDef possessor = Dk2TerrainFixture.Possessor();
        if (workerPossessor) possessor = possessor with { Flags = possessor.Flags | CreatureFlags.IsWorker };
        GameDatabase db = Dk2TerrainFixture.Database(possessor);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static CreatureState Possess(GameDatabase db, WorldState world, byte type, int x, int y)
    {
        CreatureState me = world.SpawnCreature(db, type, Keeper, new TilePos(x, y));
        me.Possessed = true;
        me.Heading = East;
        world.Player(Keeper).PossessedCreatureId = me.Id;
        return me;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem(), new TileWorkSystem()]);
        sim.Tick();
        sim.Tick();
        return sim;
    }

    private static ActPossessedCreatureCommand Press(CreatureState me, ushort yaw = East) =>
        new(Keeper, new(new(new(me.PosX), new(me.PosY), new(me.PosZ)), new(yaw), new(Level)), true, new(0));

    private static void Set(WorldState world, int x, int y, byte terrain, byte owner = PlayerIds.Neutral)
    {
        ref TileState tile = ref world.Tile(x, y);
        tile.TerrainId = terrain;
        tile.OwnerPlayerId = owner;
    }

    private static void SetHealth(GameDatabase db, WorldState world, int x, int y) =>
        world.Tile(x, y).Health = db.TerrainById(world.Tile(x, y).TerrainId).MaxHealth;

    private static int Recharge(GameDatabase db, CreatureState me) =>
        CombatRules.RechargeTicks(db, me, CombatRules.TicksOf(db.CreatureById(me.CreatureTypeId).MeleeRecharge));

    private static int Rate(GameDatabase db, CreatureState me) =>
        EfficiencyRules.Modifier(me, db.CreatureById(me.CreatureTypeId));

    // ------------------------------------------------------------------ the facing tile

    [Fact]
    public void APossessedImpDigsTheFacedRockAtItsMeleeRechargeTimesTheStrike_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        Set(world, 11, 10, Dk2TerrainFixture.Rock);
        world.Tile(11, 10).Health = 30000;
        GameSimulation sim = Sim(db, world);
        int recharge = Recharge(db, me);
        Assert.True(recharge > 1, "the multiplier must be visible");
        sim.Tick(InputFrame.Of(Press(me)));
        int strike = TileWorkRules.PerApplication(db, TileWorkKind.Dig, Rate(db, me));
        Assert.Equal(30000 + (strike * recharge), world.Tile(11, 10).Health);
    }

    [Fact]
    public void TheFacingTileFollowsThePacketYaw_NorthIsUp_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        Set(world, 11, 10, Dk2TerrainFixture.Rock);
        Set(world, 10, 9, Dk2TerrainFixture.Rock);
        SetHealth(db, world, 11, 10);
        SetHealth(db, world, 10, 9);
        GameSimulation sim = Sim(db, world);
        int east = world.Tile(11, 10).Health, north = world.Tile(10, 9).Health;
        sim.Tick(InputFrame.Of(Press(me, North)));
        Assert.Equal(east, world.Tile(11, 10).Health);
        Assert.True(world.Tile(10, 9).Health < north);
    }

    [Fact]
    public void ACreatureThatIsNotAWorkerDigsNothing_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        Set(world, 11, 10, Dk2TerrainFixture.Rock);
        SetHealth(db, world, 11, 10);
        GameSimulation sim = Sim(db, world);
        int before = world.Tile(11, 10).Health;
        sim.Tick(InputFrame.Of(Press(me)));
        Assert.Equal(before, world.Tile(11, 10).Health);
    }

    [Fact]
    public void AWorkerThatIsNotAnImpDigsAtSixTimesDigOwnWallAndIgnoresTheRecharge_StronglyInferredFromBinary()
    {
        var (db, world) = Open(workerPossessor: true);
        CreatureState me = Possess(db, world, Dk2TerrainFixture.PossessorCreatureId, 10, 10);
        Set(world, 11, 10, Dk2TerrainFixture.Rock);
        world.Tile(11, 10).Health = 30000;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Press(me)));
        int stored = TileWorkRules.PerSecond(db, TileWorkKind.DigOwnWall) / SimulationTiming.OriginalTicksPerGameSecond;
        int strike = (int)(((long)stored * 6 * Rate(db, me)) >> 12);
        Assert.Equal(TileWorkRules.NonImpDigPerApplication(db, Rate(db, me)), strike);
        Assert.Equal(30000 + strike, world.Tile(11, 10).Health);
    }

    [Fact]
    public void APossessedImpMinesTheFacedSeamAtTheRechargeMultiple_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        Set(world, 11, 10, Dk2TerrainFixture.Gold);
        SetHealth(db, world, 11, 10);
        GameSimulation sim = Sim(db, world);
        int before = world.Tile(11, 10).Health;
        sim.Tick(InputFrame.Of(Press(me)));
        int strike = TileWorkRules.PerApplication(db, TileWorkKind.MineGold, Rate(db, me));
        Assert.True(before + (strike * Recharge(db, me)) > 0, "the seam must survive the strike");
        Assert.Equal(before + (strike * Recharge(db, me)), world.Tile(11, 10).Health);
        Assert.True(me.GoldHeld > 0);
    }

    // ------------------------------------------------------------------ the pose tile

    /// <summary>The pose tile (10,10) beside the keeper's own claimed floor at (9,10).</summary>
    private static void OwnFloorWest(WorldState world) => Set(world, 9, 10, Dk2TerrainFixture.ClaimedPath, Keeper);

    [Fact]
    public void AClaimStrikesEveryTickUntilTheTileTurns_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        OwnFloorWest(world);
        world.Tile(10, 10).Health = -6000;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Press(me)));
        int strike = TileWorkRules.PerApplication(db, TileWorkKind.Claim, Rate(db, me)) * Recharge(db, me);
        Assert.Equal(-6000 + strike, world.Tile(10, 10).Health);
        Assert.Equal((10 * world.Width) + 10, me.ClaimActionTile);
        int ticks = 1;
        while (world.Tile(10, 10).OwnerPlayerId != Keeper && ticks < 50)
        {
            sim.Tick();
            ticks++;
        }
        Assert.True(ticks > 1, "the claim must need more than the press's own strike");
        Assert.Equal(Keeper, world.Tile(10, 10).OwnerPlayerId);
        sim.Tick();
        Assert.Equal(-1, me.ClaimActionTile);
    }

    [Fact]
    public void AClaimEndsWhenTheCreatureLeavesPossession_Ours()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        OwnFloorWest(world);
        world.Tile(10, 10).Health = -30000;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Press(me)));
        int health = world.Tile(10, 10).Health;
        sim.Tick(InputFrame.Of(new LeavePossessionCommand(Keeper)));
        sim.Tick();
        Assert.Equal(-1, me.ClaimActionTile);
        Assert.Equal(health, world.Tile(10, 10).Health);
    }

    [Fact]
    public void AnEnemyFloorIsAttackedOnceAPressWithNoExperience_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        OwnFloorWest(world);
        Set(world, 10, 10, Dk2TerrainFixture.ClaimedPath, Rival);
        world.Tile(10, 10).Health = 30000;
        GameSimulation sim = Sim(db, world);
        int experience = me.ExperiencePoints;
        sim.Tick(InputFrame.Of(Press(me)));
        int strike = TileWorkRules.PerApplication(db, TileWorkKind.AttackTile, Rate(db, me));
        Assert.Equal(30000 + strike, world.Tile(10, 10).Health);
        sim.Tick();
        Assert.Equal(30000 + strike, world.Tile(10, 10).Health);
        Assert.Equal(experience, me.ExperiencePoints);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenTheEnemyFloorFallsTheTrapsOnTheWorkersOwnTileAreZeroed_StronglyInferredFromBinary(bool falls)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        OwnFloorWest(world);
        Set(world, 10, 10, Dk2TerrainFixture.ClaimedPath, Rival);
        world.Tile(10, 10).Health = falls ? 1 : 30000;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = 10, Y = 10, TrapId = Dk2TerrainFixture.SentryTrapId, PlayerId = Rival, NumberOfShots = 1 }]);
        TrapState trap = world.Traps[^1];
        int health = trap.Health;
        Assert.True(health > 0);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Press(me)));
        Assert.Equal(falls ? 0 : health, trap.Health);
    }

    [Fact]
    public void AnAlliedKeepersTileEndsThePress_TheFacedRockIsNotTried_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        OwnFloorWest(world);
        Set(world, 10, 10, Dk2TerrainFixture.ClaimedPath, Rival);
        Set(world, 11, 10, Dk2TerrainFixture.Rock);
        SetHealth(db, world, 11, 10);
        world.SetAllied(new PlayerId(Keeper), Rival, true);
        GameSimulation sim = Sim(db, world);
        int rock = world.Tile(11, 10).Health, floor = world.Tile(10, 10).Health;
        sim.Tick(InputFrame.Of(Press(me)));
        Assert.Equal((rock, floor), (world.Tile(11, 10).Health, world.Tile(10, 10).Health));
    }

    [Fact]
    public void APoseTileNotTouchingOwnFloorFallsThroughToTheFacedTile_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        Set(world, 11, 10, Dk2TerrainFixture.Rock);
        SetHealth(db, world, 11, 10);
        GameSimulation sim = Sim(db, world);
        int rock = world.Tile(11, 10).Health;
        sim.Tick(InputFrame.Of(Press(me)));
        Assert.Equal(-1, me.ClaimActionTile);
        Assert.NotEqual(Keeper, world.Tile(10, 10).OwnerPlayerId);
        Assert.True(world.Tile(11, 10).Health < rock);
    }

    [Fact]
    public void TheClaimTileAndTheWorkOrdersFieldsSurviveASnapshot()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        me.ClaimActionTile = 77;
        world.QueueWork(new TileWorkOrder(5, TileWorkKind.Dig, new PlayerId(Keeper), me.Id, 3,
            TileWorkStrike.NonImpDig | TileWorkStrike.ClearsWorkerTile));
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem()]);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new PossessionSystem()]);
        Assert.Equal(77, restored.World.CreatureById(me.Id)!.ClaimActionTile);
        Assert.Equal(world.PendingWork[0], restored.World.PendingWork[0]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }
    [Fact]
    public void AClaimEndsWhenTheWorkerIsDown_Ours()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        OwnFloorWest(world);
        world.Tile(10, 10).Health = -30000;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Press(me)));
        int health = world.Tile(10, 10).Health;
        me.Health = 0;
        sim.Tick();
        Assert.Equal(-1, me.ClaimActionTile);
        Assert.Equal(health, world.Tile(10, 10).Health);
    }

    [Fact]
    public void APathAnotherPlayerHoldsIsNotClaimed_TheFacedRockIsDugInstead_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        OwnFloorWest(world);
        world.Tile(10, 10).OwnerPlayerId = Rival;   // Dirt Path is not Ownable, so not an enemy floor either
        Set(world, 11, 10, Dk2TerrainFixture.Rock);
        world.Tile(11, 10).Health = 30000;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Press(me)));
        Assert.Equal(-1, me.ClaimActionTile);
        Assert.Equal(Rival, world.Tile(10, 10).OwnerPlayerId);
        Assert.True(world.Tile(11, 10).Health < 30000);
    }

    [Fact]
    public void GoldAnotherPlayerHoldsIsDugAsAWallNotMined_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ImpCreatureId, 10, 10);
        Set(world, 11, 10, Dk2TerrainFixture.Gold, Rival);
        world.Tile(11, 10).Health = 30000;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Press(me)));
        int strike = TileWorkRules.PerApplication(db, TileWorkKind.DigEnemyWall, Rate(db, me)) * Recharge(db, me);
        Assert.Equal(30000 + strike, world.Tile(11, 10).Health);
        Assert.Equal(0, me.GoldHeld);
    }

    [Fact]
    public void ANonImpDigIgnoresTheWallsOwner_TheHeroTunnellersStrike_StronglyInferredFromBinary()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var wall = new TileState { TerrainId = Dk2TerrainFixture.ReinforcedWall, OwnerPlayerId = Keeper, Health = 30000 };
        TileWorkResult result = TileWorkRules.Apply(db, ref wall, TileWorkKind.Dig, PlayerIds.Good,
            EfficiencyRules.BaseModifier, strike: TileWorkStrike.NonImpDig);
        Assert.Equal(TileWorkRules.NonImpDigPerApplication(db, EfficiencyRules.BaseModifier), result.HealthDelta);
        Assert.NotEqual(TileWorkRules.PerApplication(db, TileWorkKind.DigEnemyWall), result.HealthDelta);
    }
}
