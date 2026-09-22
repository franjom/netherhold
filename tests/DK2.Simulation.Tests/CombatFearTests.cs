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
/// of the origin a heartless player's heart position is taken to be (Q372), so fear counts in full.</summary>
public class CombatFearTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

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
    public void TheWithdrawalRunsForTheHeart_TwoTilesOffOnEachAxis()
    {
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
        Assert.Equal(2, Math.Abs(to.X - 3));
        Assert.Equal(2, Math.Abs(to.Y - 3));
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
