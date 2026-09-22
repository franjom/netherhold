// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The fight itself — docs/research/combat-fight-state.md, ADR-0164: the two cadence gates,
/// the wind-up and the blow, the approach, retaliation, the four arms' scores and the heart as a
/// target. Creatures are placed touching where the swing is the subject, because a melee reach
/// is well under a tile.</summary>
public class CombatTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void ASwingBeginsWhenTheRechargeAllows_AndLandsAfterTheWindUp()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon);
        CreatureDef demonDef = db.CreatureById(demon.CreatureTypeId);
        int recharge = CombatRules.TicksOf(demonDef.MeleeRecharge);
        int delay = CombatRules.TicksOf(demonDef.MeleeHitDelay);
        Assert.Equal(14, recharge);
        Assert.Equal(5, delay);
        int expected = MeleeDamageRules.Blow(db, demonDef, 1, MeleeDamageRules.Stance.None,
            Bearing.Of(demon.PosX - goblin.PosX, demon.PosY - goblin.PosY), 0, 0);
        Assert.True(expected < 0);

        GameSimulation sim = Combat(db, world);
        sim.Run(recharge);
        Assert.False(demon.SwingInProgress);
        sim.Run(1);
        Assert.True(demon.SwingInProgress);
        Assert.Equal(recharge, demon.TimeLastUsedMelee);
        Assert.Equal(1500, goblin.Health);

        sim.Run(delay - 1);
        Assert.Equal(1500, goblin.Health);
        sim.Run(1);
        Assert.False(demon.SwingInProgress);
        Assert.Equal(1500 + expected, goblin.Health);

        sim.Run(recharge - delay);
        Assert.True(demon.SwingInProgress);
    }

    [Fact]
    public void TheBlowLandsOnTheTargetTheSwingWasStartedAt()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon);
        GameSimulation sim = Combat(db, world);
        sim.Run(15);
        Assert.True(demon.SwingInProgress);

        // The target steps out of reach during the wind-up; DK2 checks liveness at the blow, not range.
        goblin.PosX += 3 * Fp32.One;
        sim.Run(5);

        Assert.True(goblin.Health < 1500);
    }

    [Fact]
    public void ThreeBlowsKillAGoblin_AndTheKillerLetsGo()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon);
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureHealthSystem()]);

        sim.Run(14 + (2 * 14) + 5 + 2);

        Assert.True(CreatureDeathRules.IsDown(goblin), $"health {goblin.Health}");
        Assert.Equal(CreatureActivity.Idle, demon.Activity);
        Assert.Equal(CombatRules.NoTarget, demon.FightTargetId);
    }

    [Fact]
    public void AFighterWalksIntoReachBeforeItSwings()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, 8, 5);
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]);

        sim.Run(2);
        Assert.Equal(CreatureActivity.Fighting, demon.Activity);
        Assert.True(demon.IsRouted, "the approach requests a route toward the target");

        sim.Run(200);
        Assert.True(knight.Health < 1500 || demon.Health < 6000, "somebody has landed a blow");
    }

    [Fact]
    public void ACreatureAlreadyFightingTurnsOnItsAttacker_OnlyWhenItsOwnTargetIsCrowded()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        // The Rival's Knight and Warlock both fight the Keeper's Goblin; the Keeper's Bile Demon
        // takes the Knight. When the demon's swing begins, the Knight's target is double-teamed.
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 5, 5);
        CreatureState knight = Touching(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, goblin);
        CreatureState warlock = Touching(db, world, Dk2TerrainFixture.WarlockCreatureId, Rival, goblin, dy: 1);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        demon.PosX = knight.PosX + 1500;
        demon.PosY = knight.PosY;
        GameSimulation sim = Combat(db, world);

        sim.Run(1);
        Assert.Equal(goblin.Id, knight.FightTargetId);
        Assert.Equal(goblin.Id, warlock.FightTargetId);
        Assert.Equal(knight.Id, demon.FightTargetId);

        sim.Run(14);
        Assert.True(demon.SwingInProgress);
        Assert.Equal(demon.Id, knight.FightTargetId);
        Assert.Contains(knight.Id, demon.EngagedMelee);
        Assert.DoesNotContain(knight.Id, goblin.EngagedMelee);
    }

    [Fact]
    public void ACreatureFightingAloneKeepsItsTargetWhenStruck()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 5, 5);
        CreatureState knight = Touching(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, goblin);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        demon.PosX = knight.PosX + 1500;
        demon.PosY = knight.PosY;
        GameSimulation sim = Combat(db, world);

        sim.Run(15);

        Assert.True(demon.SwingInProgress);
        Assert.Equal(goblin.Id, knight.FightTargetId);
    }

    [Fact]
    public void ACreatureNotFightingWalksTowardWhoeverStrikesIt()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        // Touching across a tile boundary, so the walk toward the attacker is a route and not a standstill.
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        demon.PosX = CreatureState.CentreOf(5) + 1800;
        CreatureState bystander = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Neutral, demon);
        GameSimulation sim = Combat(db, world);

        sim.Run(15);

        Assert.True(demon.SwingInProgress);
        Assert.Equal(CreatureActivity.Idle, bystander.Activity);
        Assert.True(bystander.IsRouted);
        Assert.Equal(demon.Tile, bystander.Destination);
    }

    [Fact]
    public void ABlitzerDivesAtTheBackline_AndAFlankerJoinsTheMelee()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState blitzer = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 5, 5);
        Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, 7, 5);
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Rival, 5, 8);

        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 12, 5);
        CreatureState engaged = Touching(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, demon);
        CreatureState skeleton = Spawn(db, world, Dk2TerrainFixture.SkeletonCreatureId, Keeper, 10, 5);
        Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, 10, 3);

        Combat(db, world).Run(1);

        Assert.Equal(warlock.Id, blitzer.FightTargetId);
        Assert.Equal(engaged.Id, demon.FightTargetId);
        Assert.Equal(engaged.Id, skeleton.FightTargetId);
    }

    [Fact]
    public void AnEnemyHeartInSightIsAttacked_AndDamageHeartIsCalled()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 3; y <= 7; y++)
            for (int x = 10; x <= 14; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), heart, Rival);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 9, 5);
        int before = world.Player(Rival).HeartHealth;
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]);

        sim.Run(1);
        Assert.Equal(FightTargetKind.Heart, demon.FightTargetKind);
        Assert.Equal(Rival, demon.FightTargetPlayerId);
        Assert.Equal((5 * world.Width) + 10, demon.FightTargetTileIndex);
        Assert.Equal(CombatRules.AttackTypes.MeleeAtThing, demon.AttackType);

        sim.Run(120);
        Assert.True(world.Player(Rival).HeartHealth < before, $"heart {world.Player(Rival).HeartHealth}");
    }

    [Fact]
    public void TheBlowThatFellsAHeartConquersItsKeeper()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 4; y <= 6; y++)
            for (int x = 10; x <= 12; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), heart, Rival);
        world.Player(Rival).HeartHealth = 1;
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 10, 4);
        demon.PosX = CreatureState.CentreOf(10) - 300;
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new DungeonHeartSystem()]);

        sim.Run(14 + 5 + 2);

        Assert.Equal(0, world.Player(Rival).HeartHealth);
        Assert.Equal(Keeper, world.Player(Rival).KilledByPlayerId);
        Assert.Equal(PlayerStatus.Conquered, world.Player(Rival).Status);
        Assert.Equal(PlayerStatus.Won, world.Player(Keeper).Status);
        Assert.Equal(CreatureActivity.Idle, demon.Activity);
    }

    [Fact]
    public void AFightSurvivesASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon);
        GameSimulation original = Combat(db, world);
        original.Run(16);
        Assert.True(demon.SwingInProgress);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new CombatSystem()]);

        CreatureState after = restored.World.CreatureById(demon.Id)!;
        Assert.Equal(demon.TimeLastUsedMelee, after.TimeLastUsedMelee);
        Assert.Equal(demon.SwingStartTick, after.SwingStartTick);
        Assert.Equal(demon.SwingTargetId, after.SwingTargetId);
        Assert.Equal(original.StateHash(), restored.StateHash());
        original.Run(10);
        restored.Run(10);
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    // ------------------------------------------------------------------ helpers

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.TerrainId == Dk2TerrainFixture.Rock || tile.TerrainId == Dk2TerrainFixture.Gold
                    || tile.TerrainId == Dk2TerrainFixture.Gems)
                    tile.TerrainId = Dk2TerrainFixture.DirtPath;
            }
        }
        return (db, world);
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    /// <summary>A creature a third of a tile from <paramref name="other"/>: inside every melee reach.</summary>
    private static CreatureState Touching(GameDatabase db, WorldState world, byte type, byte owner, CreatureState other, int dy = 0)
    {
        CreatureState c = world.SpawnCreature(db, type, owner, other.Tile);
        c.PosX = other.PosX + (dy == 0 ? 1300 : 0);
        c.PosY = other.PosY + (dy * 1300);
        return c;
    }

    private static GameSimulation Combat(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CombatSystem()]);
}
