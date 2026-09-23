// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The NonFighter arm and the alarm — docs/research/combat-alarm.md, ADR-0166: a worker
/// fights anything within a tile of its heart, ignores enemies that are not heroes elsewhere,
/// withdraws from a hero away from its rooms, and near them counts heads and either keeps
/// scanning, raises the alarm at its nearest room, or withdraws. The room's creatures answer
/// the alarm until their Threat covers the deficit, then stand down after thirty ticks.</summary>
public class CombatNonFighterTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void AnImpNearItsHeartFightsAnythingWithinATile()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceRoom(world, Keeper, Dk2TerrainFixture.DungeonHeartTerrain, 12, 12);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 14, 12);
        CreatureState goblin = Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, imp);
        GameSimulation sim = Combat(db, world);

        sim.Run(5);
        Assert.Equal(CreatureActivity.Fighting, imp.Activity);
        Assert.Equal(goblin.Id, imp.FightTargetId);
    }

    [Fact]
    public void AnImpAwayFromItsHeartIgnoresAnEnemyThatIsNotAHero()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        Touching(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival, imp);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(8);
        Assert.NotEqual(CreatureActivity.Fighting, imp.Activity);
        Assert.DoesNotContain(sink.Events, e => e.Detail.Contains("creature=" + imp.Id) && e.Kind is "engaged" or "withdraws" or "raises_alarm");
    }

    [Fact]
    public void AnImpThatSeesAHeroAwayFromItsRoomsWithdraws()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.PosX = imp.PosX + 3700; // under a tile: inside an Imp's forced sight
        knight.PosY = imp.PosY;
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(5);
        Assert.Contains(sink.Events, e => e.Kind == "withdraws" && e.Detail.Contains("creature=" + imp.Id));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "engaged" && e.Detail.Contains("creature=" + imp.Id));
    }

    [Fact]
    public void AnImpNearItsRoomsThatOutnumbersTheHeroKeepsScanning()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceRoom(world, Keeper, Dk2TerrainFixture.LairTerrain, 13, 9);
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.PosX = imp.PosX + 3700;
        knight.PosY = imp.PosY;
        Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, 15, 12); // Threat 120 on our side
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(5);
        Assert.DoesNotContain(sink.Events, e => e.Detail.Contains("creature=" + imp.Id) && e.Kind is "engaged" or "withdraws" or "raises_alarm");
        Assert.Equal(CreatureActivity.Idle, imp.Activity);
    }

    [Fact]
    public void AnOutnumberedImpNearItsRoomsRaisesTheAlarm_AndTheRoomAnswersUntilTheDeficitIsCovered()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        // The room six tiles from the hero: within the Imp's reporting distance, beyond the
        // Goblins' own sight and hearing, so none of them acquires the Knight before the alarm.
        PlaceRoom(world, Keeper, Dk2TerrainFixture.LairTerrain, 13, 5);
        int roomTile = (5 * world.Width) + 13;
        CreatureState first = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 5);
        CreatureState second = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 14, 5);
        CreatureState third = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 13, 4);
        foreach (CreatureState g in new[] { first, second, third })
        {
            g.Activity = CreatureActivity.AtWork;
            g.JobTileIndex = roomTile;
        }
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.PosX = imp.PosX + 3700;
        knight.PosY = imp.PosY;
        GameSimulation sim = new(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        // The Knight's 37 against the Imp's 2 at three quarters: a deficit of (37 − 1) × 3 / 2 = 54.
        sim.Run(5);
        var alarm = Assert.Single(sink.Events, e => e.Kind == "raises_alarm");
        Assert.Contains("deficit=54", alarm.Detail);
        Assert.Equal(CreatureActivity.RaisingAlarm, imp.Activity);
        Assert.True(imp.Running);

        // Two Goblins of 37 cover 54; the third stays at work.
        sim.Run(40);
        var raised = Assert.Single(sink.Events, e => e.Kind == "alarm_raised");
        Assert.Contains("sent=2", raised.Detail);
        int reporting = new[] { first, second, third }.Count(g => g.Activity is CreatureActivity.Reporting or CreatureActivity.Fighting);
        Assert.Equal(2, reporting);
        Assert.Equal(CreatureActivity.AtWork, third.Activity);
        Assert.NotEqual(CreatureActivity.RaisingAlarm, imp.Activity);
    }

    [Fact]
    public void AResponderStandsDownThirtyTicksAfterArriving()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        goblin.Activity = CreatureActivity.Reporting;
        goblin.Running = true;
        GameSimulation sim = Combat(db, world);

        sim.Run(FearRules.ReportTicks);
        Assert.Equal(CreatureActivity.Reporting, goblin.Activity);
        sim.Run(1);
        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.False(goblin.Running);
    }

    [Fact]
    public void TheAlarmSurvivesASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState imp = Spawn(db, world, Dk2TerrainFixture.ImpCreatureId, Keeper, 12, 12);
        imp.AlarmX = 5000;
        imp.AlarmY = 6000;
        imp.AlarmDeficit = 54;
        imp.AlarmRoomTile = 7;
        imp.ReportTicks = 3;
        GameSimulation original = Combat(db, world);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new CombatSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        CreatureState copy = restored.World.CreatureById(imp.Id)!;
        Assert.Equal((5000, 6000, 54, 7, 3), (copy.AlarmX, copy.AlarmY, copy.AlarmDeficit, copy.AlarmRoomTile, copy.ReportTicks));
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

    /// <summary>A three-by-three room of the terrain centred on the tile, owned by the keeper.</summary>
    private static void PlaceRoom(WorldState world, byte owner, byte terrain, int cx, int cy)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                ref TileState tile = ref world.Tile(new TilePos(cx + dx, cy + dy));
                tile.TerrainId = terrain;
                tile.OwnerPlayerId = owner;
            }
        }
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    private static CreatureState Touching(GameDatabase db, WorldState world, byte type, byte owner, CreatureState other)
    {
        CreatureState c = world.SpawnCreature(db, type, owner, other.Tile);
        c.PosX = other.PosX + 1300;
        c.PosY = other.PosY;
        return c;
    }

    private static GameSimulation Combat(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CombatSystem()]);
}
