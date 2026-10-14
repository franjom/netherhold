// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Action 26's object arms and the swing on things that are not creatures
/// (possession-arms-and-inputs.md §1–2).</summary>
public sealed class PossessionObjectTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const ushort East = 0x200, Level = 0x200;

    private static (GameDatabase Db, WorldState World) Open(bool heart = false)
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Possessor());
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        if (heart)
            for (int y = 3; y <= 7; y++)
                for (int x = 10; x <= 14; x++)
                    RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), db.Room(RoomTypeId.DungeonHeart), Rival);
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

    private static GameSimulation Sim(GameDatabase db, WorldState world, params ISimulationSystem[] more)
    {
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem(), .. more]);
        sim.Tick();
        sim.Tick();
        return sim;
    }

    private static ActPossessedCreatureCommand Act(CreatureState me, bool melee = true, int slot = 0) =>
        new(Keeper, new(new(new(me.PosX), new(me.PosY), new(me.PosZ)), new(East), new(Level)), melee, new(slot));

    /// <summary>An object a little east of the creature, inside its reach.</summary>
    private static ObjectState InFront(GameDatabase db, WorldState world, CreatureState me, byte type, byte owner = PlayerIds.Neutral)
    {
        ObjectState o = world.SpawnObject(db, type, owner, (me.Tile.Y * world.Width) + me.Tile.X);
        o.PosX = me.PosX + 200;
        o.PosY = me.PosY;
        return o;
    }

    private static int Blow(GameDatabase db, CreatureState me) =>
        MeleeDamageRules.ApplyPlayerModifiers(MeleeDamageRules.Swing(db, db.CreatureById(me.CreatureTypeId), me.Level,
            MeleeDamageRules.StanceOf(me)), 0, 0);

    // ------------------------------------------------------------------ the chicken

    [Fact]
    public void AChickenEatenPaysOffTheFillHealsAndAddsHungerAngerWithoutRestartingTheClock_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        ObjectState chicken = InFront(db, world, me, HatcheryRules.ChickenObjectId);
        GameSimulation sim = Sim(db, world);
        me.HungerNeed = 3;
        me.HungerTicks = 77;
        me.Anger[AngerBucket.Hunger] = 100;
        int max = CreatureHealthRules.MaxHealth(db, db.CreatureById(me.CreatureTypeId), me);
        me.Health = max - 100;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Null(world.ObjectById(chicken.Id));
        Assert.Equal(2, me.HungerNeed);          // the Goblin's HungerFill is 1
        Assert.Equal(77, me.HungerTicks);
        Assert.Equal(200, me.Anger[AngerBucket.Hunger]);   // + bucket / fill: an add, not a forget
        Assert.Equal(max, me.Health);            // the missing 100 is under HpFromChicken's 375
    }

    [Fact]
    public void ACreatureOwingNoFoodKillsTheChickenAndGainsNothing_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        ObjectState chicken = InFront(db, world, me, HatcheryRules.ChickenObjectId);
        GameSimulation sim = Sim(db, world);
        me.HungerNeed = 0;
        me.Health -= 100;
        int health = me.Health;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Null(world.ObjectById(chicken.Id));
        Assert.Equal(health, me.Health);
        Assert.Equal(0, me.HungerNeed);
    }

    [Fact]
    public void TheChickenHealIsCappedAtHpFromChicken_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        InFront(db, world, me, HatcheryRules.ChickenObjectId);
        GameSimulation sim = Sim(db, world);
        me.HungerNeed = 1;
        me.Health = 100;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Equal(100 + 375, me.Health);
    }

    // ------------------------------------------------------------------ furniture

    [Fact]
    public void TheTrainingDummyIsSentIntoProcessState18AndTakesNoSwing_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        ObjectState dummy = InFront(db, world, me, Dk2TerrainFixture.TrainingDummyObjectId);
        GameSimulation sim = Sim(db, world);
        dummy.ProcessState = ObjectReactionRules.StruckProcess;
        dummy.ProcessAgeTicks = 5;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Equal(ObjectReactionRules.StruckProcess, dummy.ProcessState);
        Assert.Equal(0, dummy.ProcessAgeTicks);   // a running spin restarts
        Assert.False(me.SwingInProgress);
    }

    [Fact]
    public void ABookCaseTakesTheSwingAndRefusesItsBlow_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        ObjectState shelf = InFront(db, world, me, Dk2TerrainFixture.BookCaseObjectId);
        GameSimulation sim = Sim(db, world);
        long now = sim.CurrentTick.Value;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Equal(FightTargetKind.Object, me.SwingTargetKind);
        Assert.Equal(shelf.Id, me.SwingTargetId);
        Assert.Equal(now, me.PossessedTargetTick);
        Assert.Equal(FightTargetKind.Object, me.PossessedTargetKind);
        Assert.NotNull(world.ObjectById(shelf.Id));
    }

    // ------------------------------------------------------------------ the Arch Pillar

    private static HeartPillarTarget FirstPillar(GameDatabase db, WorldState world)
    {
        var sites = new HeartSites();
        sites.Refresh(world, db);
        return sites.SiteOf(new PlayerId(Rival))!.Value.Pillars[0];
    }

    [Fact]
    public void ASwingAtAnEnemyArchPillarDamagesItsHeart_StronglyInferredFromBinary()
    {
        var (db, world) = Open(heart: true);
        HeartPillarTarget pillar = FirstPillar(db, world);
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, pillar.Tile.X - 1, pillar.Tile.Y);
        me.PosX = pillar.X.Raw - 200;
        me.PosY = pillar.Y.Raw;
        GameSimulation sim = Sim(db, world, new CombatSystem());
        int before = world.Player(Rival).HeartHealth;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Equal(FightTargetKind.Heart, me.SwingTargetKind);
        Assert.Equal(HeartPillarId.For(world, pillar).Value, me.SwingTargetId);
        Assert.Equal(Rival, me.SwingTargetPlayerId);
        Assert.True(world.Player(Rival).HeartHealth < before);
    }

    [Fact]
    public void AnOwnArchPillarIsNotAMeleeTarget_StronglyInferredFromBinary()
    {
        var (db, world) = Open(heart: true);
        HeartPillarTarget pillar = FirstPillar(db, world);
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(pillar.Tile.X - 1, pillar.Tile.Y));
        me.Possessed = true;
        me.Heading = East;
        world.Player(Rival).PossessedCreatureId = me.Id;
        me.PosX = pillar.X.Raw - 200;
        me.PosY = pillar.Y.Raw;
        GameSimulation sim = Sim(db, world, new CombatSystem());
        int before = world.Player(Rival).HeartHealth;
        sim.Tick(InputFrame.Of(new ActPossessedCreatureCommand(Rival,
            new(new(new(me.PosX), new(me.PosY), new(me.PosZ)), new(East), new(Level)), true, new(0))));
        Assert.Equal(before, world.Player(Rival).HeartHealth);
        Assert.NotEqual(FightTargetKind.Heart, me.SwingTargetKind);
    }

    [Fact]
    public void ASpellScanAimsAtAnEnemyArchPillarAndTheCastTargetsItsHeart_StronglyInferredFromBinary()
    {
        var (db, world) = Open(heart: true);
        HeartPillarTarget pillar = FirstPillar(db, world);
        CreatureState me = Possess(db, world, Dk2TerrainFixture.PossessorCreatureId, pillar.Tile.X - 4, pillar.Tile.Y);
        me.PosY = pillar.Y.Raw;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 2)));
        Assert.Equal(SpellRules.SpellCast, me.SwingKind);
        Assert.Equal(FightTargetKind.Heart, me.SwingTargetKind);
        Assert.Equal(HeartPillarId.For(world, pillar).Value, me.SwingTargetId);
        Assert.Equal(Rival, me.SwingTargetPlayerId);
    }

    // ------------------------------------------------------------------ shots and bodies

    [Fact]
    public void ASwingAtAShotTakesTheBlowOffItsHealthFlat_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        GameSimulation sim = Sim(db, world, new CombatSystem());
        ShotState shot = world.SpawnShot(Dk2TerrainFixture.FireballShotId, new PlayerId(Rival), me.PosX + 200, me.PosY);
        shot.Health = 10000;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Equal(FightTargetKind.Shot, me.SwingTargetKind);
        Assert.Equal(10000 - Blow(db, me), shot.Health);
    }

    [Fact]
    public void ASwingAtABodyShortensItsRotWindow_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        CreatureState body = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        GameSimulation sim = Sim(db, world, new CombatSystem());
        CreatureDeathRules.BecomeCorpse(world, db, body);
        body.PosX = me.PosX + 200;
        body.PosY = me.PosY;
        body.DyingTicks = 8;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Equal(FightTargetKind.Body, me.SwingTargetKind);
        Assert.Equal(8 + Blow(db, me), body.DyingTicks);
    }
    [Fact]
    public void TheChickensAngerShareAndPayOffUseTheLevelScaledFill_StronglyInferredFromBinary()
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Possessor() with { HungerFill = 2 });
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState me = Possess(db, world, Dk2TerrainFixture.PossessorCreatureId, 10, 10);
        InFront(db, world, me, HatcheryRules.ChickenObjectId);
        GameSimulation sim = Sim(db, world);
        int fill = EatingRules.HungerFill(db, db.CreatureById(me.CreatureTypeId), me.Level);
        Assert.Equal(2, fill);
        me.HungerNeed = 5;
        me.Anger[AngerBucket.Hunger] = 101;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Equal(5 - fill, me.HungerNeed);
        Assert.Equal(101 + (101 / fill), me.Anger[AngerBucket.Hunger]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ABileDemonAlwaysEatsAndNeitherPaysOffNorAngers_StronglyInferredFromBinary(int need)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.BileDemonCreatureId, 10, 10);
        ObjectState chicken = InFront(db, world, me, HatcheryRules.ChickenObjectId);
        GameSimulation sim = Sim(db, world);
        me.HungerNeed = need;
        me.Anger[AngerBucket.Hunger] = 100;
        me.Health -= 50;
        int health = me.Health;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Null(world.ObjectById(chicken.Id));
        Assert.Equal(need, me.HungerNeed);
        Assert.Equal(100, me.Anger[AngerBucket.Hunger]);
        Assert.Equal(Math.Min(health + db.CreatureById(me.CreatureTypeId).HpFromChicken,
            CreatureHealthRules.MaxHealth(db, db.CreatureById(me.CreatureTypeId), me)), me.Health);
    }

    [Fact]
    public void APillarBlowIsTheFlatSwingWithNoBearingBonus_StronglyInferredFromBinary()
    {
        var (db, world) = Open(heart: true);
        HeartPillarTarget pillar = FirstPillar(db, world);
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, pillar.Tile.X - 1, pillar.Tile.Y);
        me.PosX = pillar.X.Raw - 200;
        me.PosY = pillar.Y.Raw;
        GameSimulation sim = Sim(db, world, new CombatSystem());
        int before = world.Player(Rival).HeartHealth;
        sim.Tick(InputFrame.Of(Act(me)));
        Assert.Equal(before - Blow(db, me), world.Player(Rival).HeartHealth);
    }
}
