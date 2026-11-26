// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A creature casting Raise Dead — docs/research/combat-acquisition.md §6.3, combat-spells.md §12,
/// shots.md §8 (Q886). A level-10 Vampire's body spell is chosen by the acquirer, not the attack survey: scan B
/// takes the nearest neutral body in sight, an enemy must beat its distance, the cast is once per acquisition,
/// and the shot (collide type 4) finds the first body in the finder's walk.</summary>
public class RaiseDeadCastingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void ALevelTenVampireWithANeutralBodyInSightRaisesASkeletonForItsKeeper()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 5, 10);
        CreatureState body = Corpse(db, world, 8, 10);
        int bodyId = body.Id;
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(120);

        // Nibble 3 and the third slot, with no attacker slot on the body; the engagement is on the body.
        SimEvent engaged = sink.Events.First(e => e.Kind == "engaged" && e.Detail.Contains("creature=" + vampire.Id));
        Assert.Contains($"body={bodyId} attack=3 slot=2", engaged.Detail);
        Assert.Contains("reach2=36864", engaged.Detail);
        Assert.Single(sink.Events, e => e.Kind == "casts_on_body" && e.Detail.Contains("creature=" + vampire.Id));
        Assert.DoesNotContain(world.Creatures, c => c.Id == bodyId);
        CreatureState raised = Assert.Single(world.Creatures, c => c.CreatureTypeId == Dk2TerrainFixture.SkeletonCreatureId);
        Assert.Equal(Keeper, raised.OwnerPlayerId);
        Assert.Equal(FightTargetKind.None, vampire.FightTargetKind);
    }

    [Fact]
    public void ABodyAGraveyardOwnsIsIgnored()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 5, 10);
        CreatureState body = Corpse(db, world, 7, 10);
        body.BodyOwnerPlayerId = Keeper;
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(60);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains("creature=" + vampire.Id));
        Assert.Contains(body, world.Creatures);
        Assert.DoesNotContain(world.Creatures, c => c.CreatureTypeId == Dk2TerrainFixture.SkeletonCreatureId);
    }

    [Fact]
    public void ABodyOutOfSightIsIgnored_AndOneBelowTheLevelIsNotCastAt()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 3, 10);
        Corpse(db, world, 8, 10);   // five tiles: past the four-tile sight
        GameSimulation sim = Sim(db, world);
        sim.Run(40);
        Assert.Equal(FightTargetKind.None, vampire.FightTargetKind);

        (db, world) = OpenWorld();
        vampire = Vampire(db, world, 5, 10);
        vampire.Level = 9;
        Corpse(db, world, 7, 10);
        Sim(db, world).Run(40);
        Assert.Equal(FightTargetKind.None, vampire.FightTargetKind);
        Assert.Equal(-1, SpellRules.SurveyBody(db, vampire, db.CreatureById(vampire.CreatureTypeId), 100, out _));
        vampire.Level = 10;
        Assert.Equal(2, SpellRules.SurveyBody(db, vampire, db.CreatureById(vampire.CreatureTypeId), 100, out CreatureSpellDef? spell));
        Assert.Equal(Dk2TerrainFixture.RaiseDeadSpellId, spell!.CreatureSpellId);
    }

    private const byte BlockerVampireId = 90;

    /// <summary>The body's distance seeds the best every enemy scan must beat strictly, so a Blocker's enemy
    /// (scored by distance alone) at two tiles beats a body at three, and one at three and a half loses to
    /// a body at two.</summary>
    [Fact]
    public void AnEnemyNearerThanTheBodyIsFoughtInstead_AFartherOneLosesToIt()
    {
        (GameDatabase db, WorldState world) = OpenWorld(blocker: true);
        CreatureState vampire = Vampire(db, world, 5, 10, BlockerVampireId);
        CreatureState body = Corpse(db, world, 8, 10);
        CreatureState enemy = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 7, 10);
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(14);

        SimEvent engaged = sink.Events.First(e => e.Kind == "engaged" && e.Detail.Contains("creature=" + vampire.Id));
        Assert.Contains($"target={enemy.Id} ", engaged.Detail);
        Assert.Equal(FightTargetKind.Creature, vampire.FightTargetKind);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains("body="));
        Assert.Contains(body, world.Creatures);

        (db, world) = OpenWorld(blocker: true);
        vampire = Vampire(db, world, 5, 10, BlockerVampireId);
        body = Corpse(db, world, 7, 10);
        Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 5, 13).PosX += Fp32.One / 2;
        sim = Sim(db, world);
        sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Run(14);
        Assert.Contains(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains($"creature={vampire.Id} ") && e.Detail.Contains($"body={body.Id} attack=3"));
    }

    /// <summary>The shipped Vampire is a Blitzer, whose score is the distance plus the squared sight range, so
    /// with the body's bare distance as the bound to beat an ordinary enemy in the arm never wins: the body is
    /// raised first. A Support candidate is scored by its distance alone (<c>0x0045B800</c>), so a nearer one does win.</summary>
    [Fact]
    public void ABlitzersEnemyScoresDistancePlusSight_SoItNeverBeatsTheBodyInTheArm()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 5, 10);
        CreatureState body = Corpse(db, world, 8, 10);
        Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 7, 10);
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(14);

        Assert.Contains(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains($"creature={vampire.Id} ") && e.Detail.Contains($"body={body.Id} attack=3"));
    }

    [Fact]
    public void ABlitzersNearerSupportEnemyBeatsTheBody()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 5, 10);
        Corpse(db, world, 8, 10);
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Rival, 7, 10);
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(14);

        SimEvent engaged = sink.Events.First(e => e.Kind == "engaged" && e.Detail.Contains("creature=" + vampire.Id + " "));
        Assert.Contains($"target={warlock.Id} ", engaged.Detail);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains("body="));
    }

    [Fact]
    public void ACarriedBodyIsDroppedAsATargetWithoutACast_AndTheRechargeIsNotSpent()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 5, 10);
        CreatureState body = Corpse(db, world, 6, 10);
        CreatureState carrier = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 6, 11);
        body.CarriedByCreatureId = carrier.Id;
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(40);

        // Scan B has no carried test, so the body is chosen; the fight state then refuses it every tick.
        Assert.Contains(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains($"body={body.Id} attack=3"));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "casts_on_body");
        Assert.Equal(CreatureState.NeverUsed, vampire.SpellStamps[2]);
        Assert.Contains(body, world.Creatures);
        Assert.DoesNotContain(world.Creatures, c => c.CreatureTypeId == Dk2TerrainFixture.SkeletonCreatureId);
        vampire.FightTargetKind = FightTargetKind.Body;
        vampire.FightTargetId = body.Id;
        Assert.False(FightRules.TargetIsValid(world, db, vampire));
        body.CarriedByCreatureId = -1;
        Assert.True(FightRules.TargetIsValid(world, db, vampire));
    }

    [Fact]
    public void TheRechargePreventsAnImmediateSecondCast_AndEndsAfterSeventeenSeconds()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 5, 10);
        CreatureState first = Corpse(db, world, 7, 9);
        CreatureState second = Corpse(db, world, 7, 12);
        GameSimulation sim = Sim(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        int recharge = SpellRules.RechargeTicks(db.CreatureSpells[Dk2TerrainFixture.RaiseDeadSpellId]);

        sim.Run(100);
        Assert.Single(sink.Events, e => e.Kind == "casts_on_body");
        Assert.Equal(1, world.Creatures.Count(c => c.CreatureTypeId == Dk2TerrainFixture.SkeletonCreatureId));
        Assert.Equal(1, new[] { first, second }.Count(b => world.Creatures.Contains(b)));
        Assert.True(vampire.SpellStamps[2] != CreatureState.NeverUsed);

        sim.Run(recharge + 100);
        Assert.Equal(2, sink.Events.Count(e => e.Kind == "casts_on_body"));
        Assert.Equal(2, world.Creatures.Count(c => c.CreatureTypeId == Dk2TerrainFixture.SkeletonCreatureId));
    }

    [Fact]
    public void ABodyThatRotsAwayEndsTheEngagement()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 5, 10);
        CreatureState body = Corpse(db, world, 8, 10);
        vampire.FightTargetKind = FightTargetKind.Body;
        vampire.FightTargetId = body.Id;
        vampire.AttackType = CombatRules.AttackMode.SpellAtAlly;

        Assert.True(FightRules.TargetIsValid(world, db, vampire));
        body.DyingTicks = CreatureDeathRules.BodyWindowTicks(db);
        Assert.False(FightRules.TargetIsValid(world, db, vampire));
        body.DyingTicks = 0;
        Assert.True(FightRules.TargetIsValid(world, db, vampire));
        world.RemoveCreature(body.Id);
        Assert.False(FightRules.TargetIsValid(world, db, vampire));
    }

    [Fact]
    public void ACollideFourShotFindsTheFirstBodyInTheWalk_NotTheNearest_AndNoLiveCreature()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        ShotDef def = db.Shots[Dk2TerrainFixture.RaiseDeadShotId];
        Assert.Equal(ShotRules.CollideBodies, def.CollideType);
        // Two bodies either side of a tile boundary, both under the overlap's half width of the shot; the
        // western tile is walked first although the eastern body is nearer.
        CreatureState west = Corpse(db, world, 9, 10);
        west.PosX = (9 * Fp32.One) + 4000;
        CreatureState east = Corpse(db, world, 10, 10);
        east.PosX = (10 * Fp32.One) + 100;
        west.PosY = east.PosY = (10 * Fp32.One) + 2048;
        ShotState shot = ShotRules.Spawn(world, def, new PlayerId(Keeper), (10 * Fp32.One) + 50, east.PosY,
            Dk2TerrainFixture.SkeletonCreatureId, 0, 0, FightTargetKind.None, 0, new PlayerId(0), -1, east.PosX, east.PosY);

        CreatureState? found = ShotRules.FindHit(world, db, shot, def);

        Assert.Same(west, found);
        Assert.True(Math.Abs(shot.PosX - east.PosX) < Math.Abs(shot.PosX - west.PosX));

        // A body a creature carries is off the map and not found; a standing creature is never a body.
        west.CarriedByCreatureId = 1;
        Assert.Same(east, ShotRules.FindHit(world, db, shot, def));
        east.CarriedByCreatureId = 1;
        CreatureState standing = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, 10, 10);
        standing.PosX = shot.PosX;
        standing.PosY = shot.PosY;
        Assert.Null(ShotRules.FindHit(world, db, shot, def));
    }

    [Fact]
    public void TheShotRaisesTheBodyItStrikesFirst_NotTheOneItWasAimedAt()
    {
        // 0x004AB830 copies the struck thing's position into the aim, and process 18 raises on that tile.
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 5, 10);
        CreatureState near = Corpse(db, world, 7, 10);
        CreatureState far = Corpse(db, world, 9, 10);
        ShotDef def = db.Shots[Dk2TerrainFixture.RaiseDeadShotId];
        ShotState shot = ShotRules.Spawn(world, def, new PlayerId(Keeper), vampire.PosX, vampire.PosY,
            Dk2TerrainFixture.SkeletonCreatureId, 0, vampire.Id, FightTargetKind.Body, far.Id, new PlayerId(0), -1, far.PosX, far.PosY);
        ShotRules.Launch(shot, def, far.PosX, far.PosY);
        GameSimulation sim = Sim(db, world);

        sim.Run(12);

        Assert.DoesNotContain(near, world.Creatures);
        Assert.Contains(far, world.Creatures);
        CreatureState raised = Assert.Single(world.Creatures, c => c.CreatureTypeId == Dk2TerrainFixture.SkeletonCreatureId);
        Assert.Equal(new TilePos(7, 10), raised.Tile);
        Assert.Equal(near.PosX, shot.TargetX);
    }

    [Fact]
    public void TheHitEventRunsTheProcessOnTheAimTile_AndTheShotDies()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState vampire = Vampire(db, world, 5, 10);
        CreatureState body = Corpse(db, world, 7, 10);
        ShotDef def = db.Shots[Dk2TerrainFixture.RaiseDeadShotId];
        ShotState shot = ShotRules.Spawn(world, def, new PlayerId(Keeper), body.PosX, body.PosY,
            Dk2TerrainFixture.SkeletonCreatureId, 0, vampire.Id, FightTargetKind.Body, body.Id, new PlayerId(0), -1, body.PosX, body.PosY);
        GameSimulation sim = Sim(db, world);

        sim.Run(1);

        Assert.DoesNotContain(body, world.Creatures);
        Assert.Contains(world.Creatures, c => c.CreatureTypeId == Dk2TerrainFixture.SkeletonCreatureId && c.OwnerPlayerId == Keeper);
        Assert.DoesNotContain(shot, world.Shots);
    }

    // ------------------------------------------------------------------ helpers

    private static (GameDatabase, WorldState) OpenWorld(bool blocker = false)
    {
        GameDatabase db = blocker
            ? Dk2TerrainFixture.Database(Dk2TerrainFixture.Vampire() with { CreatureId = BlockerVampireId, FightStyle = FightStyle.Blocker })
            : Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.TerrainId == Dk2TerrainFixture.Rock || tile.TerrainId == Dk2TerrainFixture.Gold
                    || tile.TerrainId == Dk2TerrainFixture.Gems)
                {
                    tile.TerrainId = Dk2TerrainFixture.DirtPath;
                }
            }
        }
        return (db, world);
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    private static CreatureState Vampire(GameDatabase db, WorldState world, int x, int y, byte type = Dk2TerrainFixture.VampireCreatureId)
    {
        CreatureState vampire = Spawn(db, world, type, Keeper, x, y);
        vampire.Level = 10;
        return vampire;
    }

    /// <summary>A Rival Goblin lying dead: the neutral player's body, in no room.</summary>
    private static CreatureState Corpse(GameDatabase db, WorldState world, int x, int y)
    {
        CreatureState dead = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, x, y);
        dead.Health = 0;
        CreatureDeathRules.BecomeCorpse(world, db, dead);
        return dead;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CreatureStatusSystem(), new CombatSystem(), new CreatureMovementSystem(), new ShotSystem()]);
}
