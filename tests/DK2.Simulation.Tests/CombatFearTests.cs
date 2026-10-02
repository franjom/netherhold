// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Fear and flight — docs/research/combat-fear.md, ADR-0165: the threat a fighter radiates,
/// the heart-distance factor, the counter and the flee mark, the back-off and the run, the
/// withdrawal of a hurt creature and where it goes. Duellists stand at (12,12), beyond ten tiles
/// of (1,1), a heartless player's heart position (Q372), so fear counts in full.</summary>
public class CombatFearTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void WithdrawalThroughTheCombatGroupDoesNotDispatchTheBattleHandler()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState creature = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        var sim = new GameSimulation(db, world, 1, [new WithdrawWithoutBattle(creature)]);
        sim.Tick();
        Assert.Equal(CreatureActivity.Withdrawing, creature.Activity);
        Assert.Empty(world.Messages.Entries);
    }

    private sealed class WithdrawWithoutBattle(CreatureState creature) : ISimulationSystem
    {
        public string Name => "withdrawal entry test";
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            var hearts = new HeartSites();
            hearts.Refresh(context.World, context.Database);
            Assert.True(FearRules.Withdraw(context, new WorldSight(context.Navigation, context.World, context.Database),
                hearts, new RoomCache(), creature, context.Database.CreatureById(creature.CreatureTypeId), []));
        }
    }

    /// <summary>A player with no heart keeps the initialiser's (1,1), not the origin (<c>0x004B8640</c>).
    /// At (5,5) that is four tiles, where the origin would be five.</summary>
    [Fact]
    public void AHeartlessPlayersHeartIsTileOneOne()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        var none = new HeartSites();

        Assert.Equal(4, FearRules.HeartDistance(world, none, Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 5, 5)));
        Assert.Equal(0, FearRules.HeartDistance(world, none, Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 1, 1)));
    }

    [Fact]
    public void AFighterRadiatesItsThreat_SplitAmongItsEngagements_IntoThoseItCanSee()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);

        // Tick 1, in id order: the Goblin engages and radiates its whole Threat (37) at its one
        // engagement; the Demon engages, takes that 37 as its CurrentFear, and splits its 120
        // between the Goblin as target and the Goblin as attacker — 60 each, 120 in all.
        sim.Run(1);
        Assert.Equal(37, demon.CurrentFear);
        Assert.Equal(0, demon.TempFear);
        Assert.Equal(120, goblin.TempFear);

        // Tick 2: the intake becomes the Goblin's CurrentFear, and the Demon fills it again.
        sim.Run(1);
        Assert.Equal(120, goblin.CurrentFear);
        Assert.Equal(120, goblin.TempFear);
    }

    [Fact]
    public void FearIsScaledByTheDistanceFromTheOwnHeart_AndIsNothingBesideIt()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 12, 12);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 13, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);

        sim.Run(2);
        // One tile from the heart: a tenth of the 120 taken, under the Goblin's Fear of 50.
        Assert.Equal(12, goblin.CurrentFear);

        sim.Run(12);
        Assert.Equal(CreatureActivity.Fighting, goblin.Activity);
        Assert.Equal(0, goblin.OverThreatCounter);
    }

    [Fact]
    public void ThreeTicksOverTheThreshold_AndTheCreatureBacksOff_ForTheBackOffTimer()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        // CurrentFear reaches 120 in tick 2; ticks 3, 4 and 5 count it over 50; tick 5 flees.
        sim.Run(4);
        Assert.Equal(2, goblin.OverThreatCounter);
        Assert.Equal(CreatureActivity.Fighting, goblin.Activity);

        sim.Run(1);
        Assert.Equal(CreatureActivity.Fleeing, goblin.Activity);
        Assert.Contains(sink.Events, e => e.Kind == "flees" && e.Detail.Contains("creature=" + goblin.Id));

        // 120 / 50 = 2 → (4 − 2) × (30 − 0) = 60 ticks, unless the demon runs faster than the Goblin.
        CreatureDef goblinDef = db.CreatureById(goblin.CreatureTypeId), demonDef = db.CreatureById(demon.CreatureTypeId);
        int expected = FearRules.RunPerTick(demonDef) > FearRules.RunPerTick(goblinDef) ? 0 : 60;
        Assert.Equal(expected, goblin.BackOffTimer);
        Assert.True(goblin.Running || expected == 0);
    }

    [Fact]
    public void AFrightenedCreatureFleesInTwoHundredThree_AndWithdrawsInSixTowardsTwoHundredTwo()
    {
        // ADR-0208: 199, 203, then the withdraw's state 6 saving 203 and resuming 202, which
        // ends at once in state 1.
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        var seen = StateTrace.Of(sim, goblin, 200, () => sink.Events.Any(e => e.Kind == "withdrawn"));

        Assert.Equal([199, 203, 6], seen.Select(s => s.State).Take(3));
        Assert.Contains((6, 203, 202), seen);
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    /// <summary>The withdraw state stamps <c>OldFightPos</c> for a non-Imp, and DK2 writes the x
    /// coordinate into both words (<c>0x004617FB</c>–<c>0x00461811</c>): the return point is (x, x).</summary>
    [Fact]
    public void AWithdrawalStampsWhereTheFightWas_WithDk2sXInBothWords()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 9);
        Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        for (int t = 0; t < 60 && !sink.Events.Any(e => e.Kind == "withdraws"); t++) sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "withdraws" && e.Detail.Contains("creature=" + goblin.Id));
        Assert.InRange(goblin.OldFightTileX, 11, 13);
        Assert.Equal(goblin.OldFightTileX, goblin.OldFightTileY);
        Assert.True(goblin.OldFightTick > 0);
    }

    [Fact]
    public void ABackedOffCreatureKeepsItsTarget_AndRunsWhenTheTimerIsOut()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(5);
        Assert.Equal(CreatureActivity.Fleeing, goblin.Activity);
        int timer = goblin.BackOffTimer;
        if (timer > 0)
        {
            sim.Run(1);
            Assert.Equal(timer - 1, goblin.BackOffTimer);
            Assert.Equal(CreatureActivity.Fleeing, goblin.Activity);
            Assert.Equal(demon.Id, goblin.FightTargetId);
        }

        // With no lair, heart or room of its own, the Goblin withdraws to a tile beside it and
        // stands idle there; it never swings at the Demon on the way.
        sim.Run(timer + 3);
        Assert.Contains(sink.Events, e => e.Kind == "withdraws" && e.Detail.Contains("creature=" + goblin.Id));
        Assert.True(goblin.Activity is CreatureActivity.Withdrawing or CreatureActivity.Idle, goblin.Activity.ToString());
        Assert.Equal(CombatRules.NoTarget, goblin.FightTargetId);
    }

    [Fact]
    public void AFearlessCreatureNeverCounts()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, 12, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, knight);
        GameSimulation sim = Combat(db, world);

        sim.Run(12);
        Assert.True(knight.CurrentFear > 0);
        Assert.Equal(0, knight.OverThreatCounter);
        Assert.Equal(CreatureActivity.Fighting, knight.Activity);
    }

    [Fact]
    public void NoEscapeTile_NoFearChecks()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        // The Goblin in a one-tile pocket whose only open neighbour the Demon stands on.
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && !(dx == 1 && dy == 0))
                    world.Tile(new TilePos(12 + dx, 12 + dy)).TerrainId = Dk2TerrainFixture.Rock;
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, 13, 12);
        demon.PosX = goblin.PosX + 2200;
        demon.PosY = goblin.PosY;
        GameSimulation sim = Combat(db, world);

        sim.Run(12);
        Assert.True(goblin.CurrentFear > 50);
        Assert.Equal(0, goblin.OverThreatCounter);
        Assert.Equal(CreatureActivity.Fighting, goblin.Activity);
    }

    [Fact]
    public void AHurtCreatureFarFromItsHeart_WithdrawsInsteadOfFighting()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        goblin.Health = 400; // under a third of 1500
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(1);
        Assert.Contains(sink.Events, e => e.Kind == "withdraws" && e.Detail.Contains("creature=" + goblin.Id));
        Assert.NotEqual(CreatureActivity.Fighting, goblin.Activity);
        Assert.Equal(CombatRules.NoTarget, goblin.FightTargetId);

        sim.Run(10);
        Assert.Equal(0, goblin.TimeLastUsedMelee);
        Assert.True(demon.TimeLastUsedMelee > 0 || demon.Activity == CreatureActivity.Fighting);
    }

    [Fact]
    public void AHurtCreatureNearItsHeartStandsAndFights()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 8, 12);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12); // four tiles off
        goblin.Health = 400;
        CreatureState knight = Touching(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(3);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "withdraws");
        Assert.Equal(CreatureActivity.Fighting, goblin.Activity);
        Assert.Equal(knight.Id, goblin.FightTargetId);
    }

    [Fact]
    public void TheWithdrawalRunsForTheHeartsPositionWord_TwoTilesOffOnEachAxis_IfASmallHeartsWordIsOffsetLikeACapturedOnes()
    {
        // The heart's +0x27/+0x29 word sits one tile past its centre on the three captured 5×5
        // hearts (RoomInstance.CentreFieldX, Q515); this fixture's 3×3 heart centred on (3,3) is
        // aimed at from (4,4) on the assumption that the offset does not depend on the size.
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 3, 3);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        goblin.Health = 400;
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);

        sim.Run(1);
        Assert.Equal(CreatureActivity.Withdrawing, goblin.Activity);
        Assert.True(goblin.Running);
        TilePos to = goblin.Destination;
        Assert.Equal(2, Math.Abs(to.X - 4));
        Assert.Equal(2, Math.Abs(to.Y - 4));
    }

    [Fact]
    public void AHeartPointInTheWallSendsTheWithdrawalToARandomRoomsWordInstead()
    {
        // Level 3's imps: three of the heart word's four points are reinforced wall round a 5×5
        // heart, and the withdraw fell through to a room 15 times in 21 (ADR-0323). Here all four
        // are rock, so the run ends at a room's own word: the Treasury's (13,14), or the heart's
        // (4,4) when step 4's draw lands on the heart itself, which is a room of the keeper's too.
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 3, 3);
        foreach ((int x, int y) in new[] { (2, 2), (6, 2), (2, 6), (6, 6) })
        {
            ref TileState wall = ref world.Tile(new TilePos(x, y));
            wall.TerrainId = Dk2TerrainFixture.Rock;
            wall.OwnerPlayerId = 0;
        }
        for (int y = 14; y <= 15; y++)
        {
            for (int x = 12; x <= 15; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                tile.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
                tile.OwnerPlayerId = Keeper;
            }
        }
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 9, 9);
        goblin.Health = 400;
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);

        sim.Run(1);
        Assert.Equal(CreatureActivity.Withdrawing, goblin.Activity);
        Assert.Contains(goblin.Destination, new[] { new TilePos(13, 14), new TilePos(4, 4) });
        Assert.DoesNotContain(goblin.Destination, new[] { new TilePos(2, 2), new TilePos(6, 2), new TilePos(2, 6), new TilePos(6, 6) });
    }

    [Fact]
    public void ARoomsPositionWordIsItsFloorCentre_AndAHeartsOneTileFurther_AsThreeCapturedHeartsRead()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 3, 3);
        for (int y = 14; y <= 15; y++)
        {
            for (int x = 12; x <= 15; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                tile.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
                tile.OwnerPlayerId = Keeper;
            }
        }

        List<RoomInstance> rooms = RoomIndex.Build(world, db);
        RoomInstance heart = rooms.Single(r => r.Definition.TypeId == RoomTypeId.DungeonHeart);
        RoomInstance treasury = rooms.Single(r => r.Definition.TypeId == RoomTypeId.Treasury);
        Assert.Equal((4, 4), (heart.CentreFieldX, heart.CentreFieldY));
        Assert.Equal((13, 14), (treasury.CentreFieldX, treasury.CentreFieldY));
    }

    [Fact]
    public void AHeroNeverWithdraws()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.Health = 400;
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, knight);
        GameSimulation sim = Combat(db, world);

        sim.Run(3);
        Assert.Equal(CreatureActivity.Fighting, knight.Activity);
        Assert.Equal(demon.Id, knight.FightTargetId);
    }

    [Fact]
    public void FearSurvivesASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState goblin = world.Creatures[0];
        Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation original = Combat(db, world);
        original.Run(4);
        Assert.True(goblin.CurrentFear > 0 && goblin.OverThreatCounter > 0);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new CombatSystem()]);
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
        return (db, world);
    }

    /// <summary>A three-by-three heart centred on the tile, owned by the keeper.</summary>
    private static void PlaceHeart(WorldState world, byte owner, int cx, int cy)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                ref TileState tile = ref world.Tile(new TilePos(cx + dx, cy + dy));
                tile.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
                tile.OwnerPlayerId = owner;
            }
        }
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

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
