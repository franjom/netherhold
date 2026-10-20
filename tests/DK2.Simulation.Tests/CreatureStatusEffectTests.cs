// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The creature side of the status shots — docs/research/creature-status-effects.md,
/// ADR-0168: a status shot's handler runs on the thing it strikes (or its target on the first tick)
/// and leaves a tick countdown on the creature; the health routine scales damage by 128ths while
/// the words run; freeze and stun stand the creature. Shots are spawned on their target and hit on
/// the next tick, with the shipped spells' data words unless the test says otherwise.</summary>
public class CreatureStatusEffectTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const byte ReaperCreatureId = 50;

    [Fact]
    public void AFreezeStandsItsTargetForFortyTicks_AtFiveQuartersDamage_AndNoFight()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        GameSimulation sim = Sim(db, world);
        Fire(db, world, Dk2TerrainFixture.FreezeShotId, Keeper, goblin, 10, 0);

        sim.Run(1);
        Assert.Equal(CreatureActivity.Frozen, goblin.Activity);
        Assert.Equal(40, goblin.FrozenTicks);
        Assert.Equal(160, StatusEffectRules.DamageMultiplier(world, db, goblin));
        Assert.Equal(-500, StatusEffectRules.Hurt(world, db, db.CreatureById(goblin.CreatureTypeId), goblin, 400));
        Assert.False(CombatRules.MayLookForAFight(world, db, goblin, force: false));
        // A frozen creature is still a target: the Warlock engages it.
        Assert.True(CombatRules.IsAttackable(goblin));

        sim.Run(20);
        Assert.Equal(CreatureActivity.Frozen, goblin.Activity);
        Assert.Equal(CreatureActivity.Fighting, warlock.Activity);
        Assert.Equal(goblin.Id, warlock.FightTargetId);

        sim.Run(20);
        Assert.Equal(0, goblin.FrozenTicks);
        Assert.NotEqual(CreatureActivity.Frozen, goblin.Activity);
        Assert.Equal(128, StatusEffectRules.DamageMultiplier(world, db, goblin));
    }

    [Fact]
    public void ArmourHalvesDamageForItsDataWordOfTicks()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 8, 10);
        CreatureDef def = db.CreatureById(goblin.CreatureTypeId);
        GameSimulation sim = Sim(db, world);
        Fire(db, world, Dk2TerrainFixture.ArmourShotId, Keeper, goblin, 80, 0);

        sim.Run(1);
        // A duration, not an amount: 80 ticks, not 80 points (creature-status-effects.md §2).
        Assert.Equal(80, goblin.ArmourTicks);
        Assert.Equal(-200, StatusEffectRules.Hurt(world, db, def, goblin, 400));

        sim.Run(80);
        Assert.Equal(0, goblin.ArmourTicks);
        Assert.Equal(-400, StatusEffectRules.Hurt(world, db, def, goblin, 400));
    }

    [Fact]
    public void AnInvulnerableCreatureRefusesAFireball_ForTwentyTicks()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 8, 10);
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        Fire(db, world, Dk2TerrainFixture.InvulnerableShotId, Keeper, goblin, 5, 0);
        sim.Run(1);
        Assert.Equal(20, goblin.InvulnerableTicks);

        Fire(db, world, Dk2TerrainFixture.FireballShotId, Rival, goblin, 300, 0);
        sim.Run(1);
        Assert.Equal(1500, goblin.Health);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "shot_hit");
        Assert.Empty(world.Shots);

        sim.Run(19);
        Assert.Equal(0, goblin.InvulnerableTicks);
        Fire(db, world, Dk2TerrainFixture.FireballShotId, Rival, goblin, 300, 0);
        sim.Run(1);
        Assert.Equal(1200, goblin.Health);
    }

    [Fact]
    public void AnInvisibleCreatureIsDroppedByItsAttackers_AndAcquiredByNobodyForSixtyTicks()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        GameSimulation sim = Sim(db, world);
        sim.Run(3);
        Assert.Equal(goblin.Id, warlock.FightTargetId);
        Assert.Contains(warlock.Id, goblin.EngagedRanged);

        Fire(db, world, Dk2TerrainFixture.InvisibleShotId, Rival, goblin, 15, 0);
        sim.Run(1);
        Assert.Equal(60, goblin.InvisibleTicks);
        Assert.NotEqual(goblin.Id, warlock.FightTargetId);
        Assert.All(goblin.EngagedRanged, slot => Assert.Equal(CombatRules.NoTarget, slot));
        Assert.False(CombatRules.IsAttackable(goblin));

        // A cast already begun still fires at its own target copy; the engagement is not renewed.
        sim.Run(30);
        Assert.NotEqual(goblin.Id, warlock.FightTargetId);

        sim.Run(30);
        Assert.Equal(0, goblin.InvisibleTicks);
        Assert.True(CombatRules.IsAttackable(goblin));
    }

    [Fact]
    public void ASpeedEffectStopsAtTheWordSetSpeedStores_0xFFF()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState quick = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 8, 10);
        CreatureDef def = db.CreatureById(quick.CreatureTypeId);
        quick.SpeedEffectTicks = 10;
        quick.SpeedPercent = 100_000;

        int cap = StatusEffectRules.SpeedCap(db, def, quick, CreatureMovementSystem.MaxSpeedOf(def));

        Assert.Equal(CreatureMovementSystem.MaxSpeedPerTick << 4, cap);
    }

    [Fact]
    public void HasteRaisesTheCapByHalfForSixtyTicks_AndSlowFloorsAtAQuarterOfTheWalk()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState quick = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 8, 10);
        CreatureState slow = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 10);
        CreatureDef def = db.CreatureById(quick.CreatureTypeId);
        int walk = CreatureMovementSystem.MaxSpeedOf(def);
        GameSimulation sim = Sim(db, world);
        Fire(db, world, Dk2TerrainFixture.HasteShotId, Keeper, quick, 150, 15);
        Fire(db, world, Dk2TerrainFixture.SlowShotId, Rival, slow, 20, 15);

        sim.Run(1);
        Assert.Equal((60, 150), (quick.SpeedEffectTicks, quick.SpeedPercent));
        // A second Haste is refused while one runs; the cap is set on the next movement tick.
        Fire(db, world, Dk2TerrainFixture.HasteShotId, Keeper, quick, 150, 15);
        sim.Run(1);
        Assert.Equal(59, quick.SpeedEffectTicks);
        Assert.Equal(((walk >> 4) * 150 / 100) << 4, quick.Body.MaxSpeed);
        // 20 % is under the quarter-of-walk floor.
        Assert.Equal(((walk >> 4) / 4) << 4, slow.Body.MaxSpeed);

        sim.Run(59);
        Assert.Equal((0, 100), (quick.SpeedEffectTicks, quick.SpeedPercent));
        sim.Run(1);
        Assert.Equal(walk, quick.Body.MaxSpeed);
    }

    [Fact]
    public void AFreezeIsTwoEighteen_EndingInIdle_AndAStunTwoFiftyFive_EndingIn250OffARoom()
    {
        // ADR-0208: 218 until the word runs out, then 1. 255, then 257, whose tail asks the room
        // underfoot (0x004E8DA0(0)): off a room a keeper's creature stands in 250 (ADR-0226).
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState frozen = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        CreatureState stunned = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 14);
        GameSimulation sim = Sim(db, world);
        Fire(db, world, Dk2TerrainFixture.FreezeShotId, Keeper, frozen, 10, 0);
        ShotRules.Spawn(world, db.Shots[Dk2TerrainFixture.LightningShotId], Keeper, CreatureState.CentreOf(5),
            CreatureState.CentreOf(14), 300, 0, 0, FightTargetKind.Creature, stunned.Id, 0, -1, stunned.PosX, stunned.PosY);

        sim.Run(1);
        Assert.Equal(218, frozen.Dk2StateId);
        Assert.Equal(255, stunned.Dk2StateId);

        sim.Run(41);
        Assert.Equal(CreatureActivity.Idle, frozen.Activity);
        Assert.Equal(CreatureStateMachine.Idle, frozen.Dk2StateId);
        Assert.Equal(DropLandingRules.StandingState, stunned.Dk2StateId);
    }

    [Fact]
    public void AFrozenCreatureIsNotStunned_TheRuleRefusesTwoFiftyFiveFromTwoEighteen()
    {
        // 0x00491830 returns at once when SetState(255) is refused: no stun, no action, no event 15.
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        Fire(db, world, Dk2TerrainFixture.FreezeShotId, Keeper, goblin, 10, 0);
        sim.Run(1);
        Assert.Equal(218, goblin.Dk2StateId);

        ShotRules.Spawn(world, db.Shots[Dk2TerrainFixture.LightningShotId], Keeper, CreatureState.CentreOf(5),
            CreatureState.CentreOf(10), 300, 0, 0, FightTargetKind.Creature, goblin.Id, 0, -1, goblin.PosX, goblin.PosY);
        sim.Run(1);

        Assert.Contains(sink.Events, e => e.Kind == "lightning" && e.Detail.Contains($"target={goblin.Id} "));
        Assert.Equal(CreatureActivity.Frozen, goblin.Activity);
        Assert.Equal(218, goblin.Dk2StateId);
        Assert.Equal(0, goblin.StunTicks);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "stunned");
    }

    [Fact]
    public void LightningStrikesItsTargetOnTheFirstTick_DoublesOnWater_StunsForStunDuration_AndRisesFreeToFight()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState dry = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        CreatureState wet = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 14);
        world.Tile(new TilePos(8, 14)).TerrainId = Dk2TerrainFixture.Water;
        CreatureDef def = db.CreatureById(dry.CreatureTypeId);
        GameSimulation sim = Sim(db, world);
        ShotDef lightning = db.Shots[Dk2TerrainFixture.LightningShotId];
        foreach (CreatureState target in new[] { dry, wet })
            ShotRules.Spawn(world, lightning, Keeper, CreatureState.CentreOf(5), CreatureState.CentreOf(10), 300, 0,
                0, FightTargetKind.Creature, target.Id, 0, -1, target.PosX, target.PosY);

        sim.Run(1);
        Assert.Equal(1200, dry.Health);
        Assert.Equal(900, wet.Health);
        Assert.Equal(CreatureActivity.Stunned, dry.Activity);
        // The fixture Goblin's StunDuration is five seconds: twenty ticks.
        Assert.Equal(20, dry.StunTicks);
        Assert.Equal(256, StatusEffectRules.DamageMultiplier(world, db, dry));
        Assert.Equal(-200, StatusEffectRules.Hurt(world, db, def, dry, 100));
        Assert.False(CombatRules.MayLookForAFight(world, db, dry, force: false));

        sim.Run(20);
        // Then 257, getting up, for the Goblin's eleven ticks: still the stunned family, still doubled.
        Assert.Equal(257, dry.Dk2StateId);
        Assert.Equal(CreatureActivity.Stunned, dry.Activity);
        Assert.Equal(256, StatusEffectRules.DamageMultiplier(world, db, dry));
        // 257's flag byte lacks bit 6 (0x0045A71F), so a creature getting up may look for a fight.
        Assert.True(CombatRules.MayLookForAFight(world, db, dry, force: false));
        sim.Run(DropLandingRules.GetUpTicks(def));
        Assert.Equal(CreatureActivity.Idle, dry.Activity);
        Assert.Equal(128, StatusEffectRules.DamageMultiplier(world, db, dry));
    }

    [Fact]
    public void PoisonBleedsItsAmountATickForItsTicks_AndPausesWhileFrozen()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState bleeding = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        CreatureState frozen = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 12, 10);
        GameSimulation sim = Sim(db, world);
        // Test words: 100 a tick for ten ticks (the Maiden's spit is 200 for ten, more than a Goblin has).
        Fire(db, world, Dk2TerrainFixture.PoisonSpitShotId, Keeper, bleeding, 100, 10);
        Fire(db, world, Dk2TerrainFixture.PoisonSpitShotId, Keeper, frozen, 100, 10);
        Fire(db, world, Dk2TerrainFixture.FreezeShotId, Keeper, frozen, 10, 0);

        sim.Run(1);
        Assert.Equal((10, 100, Keeper), (bleeding.PoisonTicks, bleeding.PoisonAmount, bleeding.PoisonOwner));
        Assert.Equal(1500, bleeding.Health);

        sim.Run(10);
        Assert.Equal(0, bleeding.PoisonTicks);
        Assert.Equal(500, bleeding.Health);
        Assert.Equal(CreatureActivity.Frozen, frozen.Activity);
        Assert.Equal(10, frozen.PoisonTicks);
        Assert.Equal(1500, frozen.Health);

        sim.Run(40);
        Assert.Equal(0, frozen.PoisonTicks);
        // Ten ticks at 100 once thawed — the poison's ticks do not run down while frozen.
        Assert.Equal(500, frozen.Health);
    }

    [Fact]
    public void DrainMovesAQuarterOfTheTargetsMaximumToTheCaster()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper, 5, 10);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        warlock.Health = 1000;
        GameSimulation sim = Sim(db, world);
        ShotDef drain = db.Shots[Dk2TerrainFixture.DrainShotId];
        ShotRules.Spawn(world, drain, Keeper, goblin.PosX, goblin.PosY, 25, 0, warlock.Id,
            FightTargetKind.Creature, goblin.Id, 0, -1, goblin.PosX, goblin.PosY);

        sim.Run(1);
        Assert.Equal(1125, goblin.Health);
        Assert.Equal(1375, warlock.Health);
    }

    [Fact]
    public void TheReaperFireballStunsBeforeItsContactDamageLands_SoItDoublesItself_Q399()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        GameSimulation sim = Sim(db, world);
        Fire(db, world, Dk2TerrainFixture.ReaperFireballShotId, Keeper, goblin, 300, 0);

        sim.Run(1);
        // shots.md §4a: the process event runs before the contact damage of the same hit, and the
        // health routine reads the stunned state — 300 lands as 600.
        Assert.Equal(CreatureActivity.Stunned, goblin.Activity);
        Assert.Equal(900, goblin.Health);
    }

    [Fact]
    public void AHornedReaperTakesNoDamage_IsNotFrozen_AndIsNotStunned()
    {
        CreatureDef reaper = Dk2TerrainFixture.Goblin() with
        {
            Name = "Reaper", CreatureId = ReaperCreatureId, Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.IsHorny,
        };
        GameDatabase db = Dk2TerrainFixture.Database(reaper);
        WorldState world = OpenWorld(db);
        CreatureState horny = Spawn(db, world, ReaperCreatureId, Rival, 8, 10);
        GameSimulation sim = Sim(db, world);
        Assert.Equal(0, StatusEffectRules.Hurt(world, db, reaper, horny, 400));

        Fire(db, world, Dk2TerrainFixture.FreezeShotId, Keeper, horny, 10, 0);
        Fire(db, world, Dk2TerrainFixture.ReaperFireballShotId, Keeper, horny, 300, 0);
        sim.Run(1);
        Assert.Equal(CreatureActivity.Idle, horny.Activity);
        Assert.Equal(0, horny.FrozenTicks);
        Assert.Equal(1500, horny.Health);
    }

    [Fact]
    public void TheStatusWordsSurviveASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 8, 10);
        goblin.ArmourTicks = 3;
        goblin.InvulnerableTicks = 4;
        goblin.FrozenTicks = 5;
        goblin.InvisibleTicks = 6;
        goblin.SpeedEffectTicks = 7;
        goblin.SpeedPercent = 150;
        goblin.PoisonTicks = 8;
        goblin.PoisonAmount = 9;
        goblin.PoisonOwner = Keeper;
        goblin.StunTicks = 11;
        StatePlacement.As(goblin, CreatureActivity.Stunned);
        GameSimulation original = Sim(db, world);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new CreatureStatusSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        CreatureState copy = restored.World.CreatureById(goblin.Id)!;
        Assert.Equal((3, 4, 5, 6, 7, 150), (copy.ArmourTicks, copy.InvulnerableTicks, copy.FrozenTicks, copy.InvisibleTicks, copy.SpeedEffectTicks, copy.SpeedPercent));
        Assert.Equal((8, 9, Keeper, 11, CreatureActivity.Stunned), (copy.PoisonTicks, copy.PoisonAmount, copy.PoisonOwner, copy.StunTicks, copy.Activity));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary><c>0x00481360</c> from the damage routine (ADR-0348, Q531): a stunned creature hit
    /// with a non-Imp among its attackers has a quarter of its stun left added, never past the full
    /// <c>StunDuration</c>; with only an Imp attacking, nothing changes.</summary>
    [Theory]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId, 8, 10)]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId, 18, 20)]
    [InlineData(Dk2TerrainFixture.ImpCreatureId, 8, 8)]
    public void AHitOnAStunnedCreatureLengthensItsStunOnlyWithANonImpAttacker(byte attackerType, int left, int after) =>
        HitWhileStunned(attackerType, 255, left, after);

    /// <summary>256 runs 255's handler (<c>0x004810D0</c> jumps to <c>0x004810E0</c>), so it lengthens
    /// too; 257's handler never reads the start turn, so its counter is untouched.</summary>
    [Theory]
    [InlineData(256, 8, 10)]
    [InlineData(257, 8, 8)]
    public void TheBoulderStunLengthensAndGettingUpDoesNot(int state, int left, int after) =>
        HitWhileStunned(Dk2TerrainFixture.GoblinCreatureId, state, left, after);

    private static void HitWhileStunned(byte attackerType, int state, int left, int after)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 8, 10);
        CreatureState attacker = Spawn(db, world, attackerType, Rival, 9, 10);
        CreatureDef def = db.CreatureById(goblin.CreatureTypeId);
        Assert.Equal(20, CombatRules.TicksOf(def.StunDuration));
        goblin.Dk2StateId = state;
        goblin.StunTicks = left;
        goblin.EngagedMelee[0] = attacker.Id;

        StatusEffectRules.Hurt(world, db, def, goblin, 10);

        Assert.Equal(after, goblin.StunTicks);
    }

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        return (db, OpenWorld(db));
    }

    private static WorldState OpenWorld(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
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
        return world;
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    /// <summary>A shot standing on its target with the given data words: it strikes on the next tick.</summary>
    private static ShotState Fire(GameDatabase db, WorldState world, byte shotId, byte owner, CreatureState target, int data1, int data2) =>
        ShotRules.Spawn(world, db.Shots[shotId], owner, target.PosX, target.PosY, data1, data2, 0,
            FightTargetKind.Creature, target.Id, 0, -1, target.PosX, target.PosY);

    private static GameSimulation Sim(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CreatureStatusSystem(), new CombatSystem(), new CreatureMovementSystem(), new ShotSystem()]);
}
