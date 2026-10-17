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

        // Gate B (0x0048C9B0): a zero stamp swings at once, and the next swing waits the recharge
        // over the work rate — 14 ticks × 4096 / 0xCCC = 17 — strictly past the stamp.
        Assert.Equal(17, CombatRules.Scaled(recharge, demon, demonDef));
        GameSimulation sim = Combat(db, world);
        RunUntilSwing(sim, demon);
        long first = demon.TimeLastUsedMelee; // the tick the swing began; the world's counter is one past it
        Assert.InRange(first, 1, 3);
        Assert.Equal(1500, goblin.Health);

        sim.Run(delay - 1);
        Assert.Equal(1500, goblin.Health);
        sim.Run(1);
        Assert.False(demon.SwingInProgress);
        Assert.Equal(1500 + expected, goblin.Health);

        // Ticks up to first + 17 pass without a swing (1 + 17 < 18 is false); tick first + 18 swings.
        sim.Run((int)(first + 18 - sim.World.CurrentTick.Value));
        Assert.False(demon.SwingInProgress);
        sim.Run(1);
        Assert.True(demon.SwingInProgress);
        Assert.Equal(first + 18, demon.TimeLastUsedMelee);
    }

    /// <summary>Runs one tick at a time until the creature's swing begins, at most twenty ticks.</summary>
    private static long RunUntilSwing(GameSimulation sim, CreatureState creature)
    {
        for (int i = 0; i < 20 && !creature.SwingInProgress; i++) sim.Run(1);
        Assert.True(creature.SwingInProgress, "no swing within twenty ticks");
        return sim.World.CurrentTick.Value;
    }

    [Fact]
    public void TheBlowLandsOnTheTargetTheSwingWasStartedAt()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon);
        GameSimulation sim = Combat(db, world);
        RunUntilSwing(sim, demon);

        // The target steps out of reach during the wind-up; DK2 checks liveness at the blow, not range.
        goblin.PosX += 3 * Fp32.One;
        sim.Run(5);

        Assert.True(goblin.Health < 1500);
    }

    [Fact]
    public void TheKillerFightsInOneNinetyNine_CelebratesInTwoSixty_AndGoesIdle()
    {
        // ADR-0208: 199 from idle, 260 on the victory arm, then state 1; with nothing to do, 89 then
        // parks it in 3 (ADR-0228).
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon).DieInstantly = true;
        // With the systems that hand back a state id their creature has left beside it.
        GameSimulation sim = new(db, world, 1,
            [new CreatureJobSystem(), new CreatureSleepSystem(), new CombatSystem(), new CreatureHealthSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        Assert.Equal([199, 260, CreatureStateMachine.Idle, CreatureJobSystem.DoingNothingState], StateTrace.States(sim, demon, 120).Take(4));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    [Fact]
    public void ThreeBlowsKillAGoblin_AndTheKillerCelebratesThenLetsGo()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon);
        goblin.DieInstantly = true; // only an outright death gives the victory (Q511)
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureHealthSystem()]);

        for (int i = 0; i < 60 && !CreatureDeathRules.IsDown(goblin); i++) sim.Run(1);
        Assert.True(CreatureDeathRules.IsDown(goblin), $"health {goblin.Health}");

        // The death unlink, then the victory arm (combat-approach-and-victory.md §5): a pause, then idle.
        sim.Run(1);
        Assert.Equal(CreatureActivity.Celebrating, demon.Activity);
        Assert.Equal(CombatRules.NoTarget, demon.FightTargetId);
        sim.Run(VictoryRules.PauseTicks(db, demon));
        Assert.Equal(CreatureActivity.Idle, demon.Activity);
    }

    /// <summary>Q511: a victim that collapses gives no victory. Step 5 finds it not attackable
    /// (<c>0x0045F2DB</c>) and the attacker lets it go with no nibble; only an outright death sets
    /// it (<c>0x0045E4A0</c>), which ours reads as a death with no dying state (STRONGLY INFERRED).
    /// Ours celebrated over a Goblin that had only collapsed.</summary>
    [Fact]
    public void AVictimThatCollapsesGivesNoVictory()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, demon);
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureHealthSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        for (int i = 0; i < 60 && !CreatureDeathRules.IsDown(goblin); i++) sim.Run(1);
        sim.Run(8);

        Assert.Contains(sink.Events, e => e.Kind == "target_down" && e.Detail.Contains("victim=" + goblin.Id));
        Assert.DoesNotContain(sink.Events, e => e.Kind is "target_died" or "celebrates");
        Assert.NotEqual(CombatRules.AttackMode.Victorious, demon.AttackType);
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

        SeedFight(db, knight, goblin);
        SeedFight(db, warlock, goblin);
        sim.Run(1);
        Assert.Equal(goblin.Id, knight.FightTargetId);
        Assert.Equal(goblin.Id, warlock.FightTargetId);
        Assert.Equal(knight.Id, demon.FightTargetId);

        RunUntilSwing(sim, demon);
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

        SeedFight(db, knight, goblin);
        RunUntilSwing(sim, demon);
        Assert.Equal(goblin.Id, knight.FightTargetId);
    }

    /// <summary><c>0x0045AA20</c>'s walk toward the attacker runs only when the attacker is a trap
    /// (<c>0x0045AD68</c> compares its class with 3): struck by a creature, one not fighting stays put.</summary>
    [Fact]
    public void ACreatureNotFightingDoesNotWalkTowardACreatureThatStrikesIt()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        // Touching across a tile boundary, so a walk toward the attacker would be a route and not a standstill.
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 5, 5);
        demon.PosX = CreatureState.CentreOf(5) + 1800;
        CreatureState bystander = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Neutral, demon);
        GameSimulation sim = Combat(db, world);

        RunUntilSwing(sim, demon);
        Assert.False(bystander.IsRouted && bystander.Destination == demon.Tile);
    }

    [Fact]
    public void ABlitzerDivesAtTheBackline_AndAFlankerJoinsTheMelee()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState blitzer = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 5, 5);
        CreatureState frontliner = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, 7, 5);
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Rival, 5, 8);
        foreach (CreatureState candidate in new[] { frontliner, warlock })
        {
            candidate.LastFightScanTileIndex = candidate.Tile.Y * world.Width + candidate.Tile.X;
            candidate.FightProcessCounter = 0;
        }

        CreatureState nearer = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, 10, 3);
        CreatureState skeleton = Spawn(db, world, Dk2TerrainFixture.SkeletonCreatureId, Keeper, 10, 5);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 12, 5);
        // Newest first: Knight and Demon engage each other before the Flanker scans;
        // the nearer Knight has not updated yet, so its empty melee count loses.
        CreatureState engaged = Touching(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, demon);

        Combat(db, world).Run(1);

        Assert.Equal(warlock.Id, blitzer.FightTargetId);
        Assert.Equal(engaged.Id, demon.FightTargetId);
        Assert.True(CombatRules.DistanceSquared(skeleton.PosX, skeleton.PosY, nearer.PosX, nearer.PosY)
            < CombatRules.DistanceSquared(skeleton.PosX, skeleton.PosY, engaged.PosX, engaged.PosY));
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
        Assert.Equal((6 * world.Width) + 11, demon.FightTargetTileIndex);
        Assert.Equal(((6 * world.Width + 11) * 3) + 1, demon.FightTargetId);
        Assert.True(FightRules.TargetPosition(world, db, demon, out int targetX, out int targetY, out _));
        Assert.Equal((11 << 12) + 0x667, targetX);
        Assert.Equal((6 << 12) + 0x19A, targetY);
        Assert.Equal(CombatRules.AttackMode.MeleeAtThing, demon.AttackType);
        // The pillar is an object, class 2, and takes the commit's default half tile (0x0045E098), not a trap's quarter.
        int reach = db.CreatureById(demon.CreatureTypeId).MeleeRange.Raw + 0x800;
        Assert.Equal((reach * reach) >> 12, demon.AttackRangeSquared);

        for (int tick = 0; tick < 120; tick++)
        {
            sim.Tick();
            long dx = demon.PosX - CreatureState.CentreOf(12);
            long dy = demon.PosY - CreatureState.CentreOf(5);
            Assert.True(dx * dx + dy * dy >= (Fp32.One / 2L) * (Fp32.One / 2L));
        }
        Assert.True(world.Player(Rival).HeartHealth < before, $"heart {world.Player(Rival).HeartHealth}");
    }

    [Fact]
    public void EquidistantHeartPillarsAreScannedNewestFirst()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 3; y <= 7; y++)
            for (int x = 10; x <= 14; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), heart, Rival);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 12, 7);
        new GameSimulation(db, world, 1, [new CombatSystem()]).Tick();
        Assert.Equal(FightTargetKind.Heart, demon.FightTargetKind);
        Assert.Equal((6 * world.Width) + 13, demon.FightTargetTileIndex);
        Assert.Equal(((6 * world.Width + 13) * 3) + 2, demon.FightTargetId);
    }

    /// <summary>The commit's melee rule is blind to the target's class (<c>0x0045DE6E</c>): a caster that
    /// is not a Support closes to melee on the heart's pillar within a tile, and casts from further off.</summary>
    [Theory]
    [InlineData(0x0C00, CombatRules.AttackMode.MeleeAtThing)]
    [InlineData(0x2000, CombatRules.AttackMode.SpellAtThing)]
    public void ACasterThatIsNotASupportMeleesTheHeartWithinATile(int offset, CombatRules.AttackMode expected)
    {
        CreatureDef caster = Dk2TerrainFixture.Goblin() with
        {
            Name = "Caster", CreatureId = 61, FightStyle = FightStyle.Blitzer, Spells = Dk2TerrainFixture.Warlock().Spells,
        };
        GameDatabase db = Dk2TerrainFixture.Database(caster);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 3; y <= 7; y++)
            for (int x = 10; x <= 14; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), heart, Rival);
        CreatureState me = world.SpawnCreature(db, 61, Keeper, new TilePos(9, 5));
        me.PosX = (11 << 12) + 0x667 - offset;
        me.PosY = (6 << 12) + 0x19A;
        new GameSimulation(db, world, 1, [new CombatSystem()]).Run(1);

        Assert.Equal(FightTargetKind.Heart, me.FightTargetKind);
        Assert.Equal(expected, me.AttackType);
    }

    /// <summary>Q510: a forced creature target (<c>AttackingId</c>) leaves the commit's running best at
    /// <c>0x7FFFFFFF</c> (<c>0x0045CF05</c>), so a surveyed spell is cast even within a tile, where
    /// an ordinary target gets melee. Ours had the shortcut for a forced creature too.</summary>
    [Theory]
    [InlineData(false, CombatRules.AttackMode.MeleeAtCreature)]
    [InlineData(true, CombatRules.AttackMode.SpellAtCreature)]
    public void AForcedCreatureTargetWithinATileGetsTheSpell(bool forced, CombatRules.AttackMode expected)
    {
        CreatureDef caster = Dk2TerrainFixture.Goblin() with
        {
            Name = "Caster", CreatureId = 61, FightStyle = FightStyle.Blitzer, Spells = Dk2TerrainFixture.Warlock().Spells,
        };
        GameDatabase db = Dk2TerrainFixture.Database(caster);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState me = world.SpawnCreature(db, 61, Keeper, new TilePos(5, 5));
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 5));
        goblin.PosX = me.PosX + 0x0C00;
        if (forced)
        {
            me.AttackAbnormalTargets = true;
            me.AttackingId = goblin.Id;
            me.AttackingKind = FightTargetKind.Creature;
        }
        new GameSimulation(db, world, 1, [new CombatSystem()]).Run(1);

        Assert.Equal(goblin.Id, me.FightTargetId);
        Assert.Equal(expected, me.AttackType);
    }

    [Fact]
    public void TheBlowThatFellsAHeartConquersItsKeeper()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 3; y <= 7; y++)
            for (int x = 10; x <= 14; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), heart, Rival);
        world.Player(Rival).HeartHealth = 1;
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 10, 4);
        demon.PosX = (12 << 12) + 0x800 - 300;
        demon.PosY = (4 << 12) + 0x4CD;
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
        RunUntilSwing(original, demon);

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

    private static void SeedFight(GameDatabase db, CreatureState attacker, CreatureState target)
    {
        StatePlacement.Place(attacker, 199);
        attacker.FightTargetKind = FightTargetKind.Creature;
        attacker.FightTargetId = target.Id;
        attacker.AttackType = CombatRules.AttackMode.MeleeAtCreature;
        attacker.AttackRangeSquared = CombatRules.MeleeReachSquared(db.CreatureById(attacker.CreatureTypeId), db.CreatureById(target.CreatureTypeId));
        Assert.True(CombatRules.Register(target, attacker.Id, ranged: false));
    }

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
