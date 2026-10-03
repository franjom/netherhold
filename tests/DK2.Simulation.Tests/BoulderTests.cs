// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Boulder (boulder.md): process 22 makes it, state 17 strikes, rolls, turns and wears
/// it. Most tests roll it east along a corridor one tile wide, rock to the north and south.</summary>
public class BoulderTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const int Row = 5;

    private static (GameDatabase db, WorldState world) Open(bool corridor = true)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 12), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = corridor && y != Row ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    /// <summary>A boulder as process 22 leaves it, rolling east from a tile's centre.</summary>
    private static ObjectState Rolling(GameDatabase db, WorldState world, int x, int y, byte owner = Keeper)
    {
        ObjectState boulder = world.SpawnObject(db, BoulderRules.ObjectId, owner, (y * world.Width) + x);
        boulder.ProcessState = BoulderRules.ProcessState;
        boulder.PosX = CreatureState.CentreOf(x);
        boulder.PosY = CreatureState.CentreOf(y);
        boulder.Health = db[VariableTypeId.BoulderInitialHealth];
        boulder.RollSpeed = db[VariableTypeId.BoulderSpeedTilesPerSecond] / SimulationTiming.OriginalTicksPerGameSecond;
        boulder.RollX = 0x1000;
        boulder.RollSignX = 1;
        return boulder;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, 1,
            [new CreatureStatusSystem(), new ShotSystem(), new BoulderSystem(), new TrapSystem(), new TrapRemovalSystem(), new DoorRemovalSystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>Q520's corner, a binary prediction not yet watched: the handlers test only the tile ahead on
    /// a centre line and the position setter tests no terrain (boulder.md §3), so a 45° roll past two
    /// free side tiles enters the solid diagonal tile, turns at its centre toward the free side and rolls on.</summary>
    [Fact]
    public void Predicted_ADiagonalRollPastAFreeCornerEntersTheSolidTile_TurnsAtItsCentre_AndDoesNotBreak()
    {
        (GameDatabase db, WorldState world) = Open(corridor: false);
        world.Tile(6, Row - 1).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(6, Row - 2).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(7, Row - 1).TerrainId = Dk2TerrainFixture.Rock;
        ObjectState boulder = Rolling(db, world, 5, Row);
        boulder.RollX = 2896;
        boulder.RollY = -2896;
        boulder.RollSignY = -1;
        int health = boulder.Health;
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Run(1);
        Assert.Equal((6, Row - 1), (boulder.PosX >> 12, boulder.PosY >> 12));
        for (int i = 0; i < 3 && !sink.Events.Any(e => e.Kind == "boulder_turned"); i++) sim.Run(1);

        SimEvent turned = Assert.Single(sink.Events, e => e.Kind == "boulder_turned");
        Assert.Contains($"at=6,{Row - 1} roll=-4096,0", turned.Detail);
        Assert.Equal(health / 10, boulder.Wear);
        Assert.Contains(boulder, world.Objects);
        sim.Run(2);
        Assert.Equal(Row - 1, boulder.PosY >> 12);
        Assert.True(boulder.PosX >> 12 < 6);
    }

    [Fact]
    public void ABoulderTrapFiresAtAnEnemy_ItsShotMakesABoulderThatDestroysTheTrap_RollsToTheEnemy_AndKnocksItIntoTwoFiftySix()
    {
        (GameDatabase db, WorldState world) = Open();
        ref TileState trapTile = ref world.Tile(5, Row);
        trapTile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        trapTile.OwnerPlayerId = Keeper;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = 5, Y = Row, TrapId = Dk2TerrainFixture.BoulderTrapId, PlayerId = Keeper, NumberOfShots = 1 }]);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, Row));
        CreatureDef goblinDef = db.CreatureById(goblin.CreatureTypeId);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        // Turn 0's empty sight cache, turn 1's firing, turn 2's shot makes the boulder and the trap dies.
        sim.Run(3);
        ObjectState boulder = Assert.Single(world.Objects, o => o.ObjectTypeId == BoulderRules.ObjectId);
        Assert.Empty(world.Traps);
        Assert.Equal((0x1000, 0, (sbyte)1, (sbyte)0), (boulder.RollX, boulder.RollY, boulder.RollSignX, boulder.RollSignY));
        Assert.Equal(4915 / 4, boulder.RollSpeed);

        for (int i = 0; i < 10 && !sink.Events.Any(e => e.Kind == "boulder_struck_creature"); i++) sim.Run(1);
        // The goblin's level-scaled Hp halved, over BoulderToCreatureDamagePercentage 50: its whole Hp.
        int damage = (CreatureLevelRules.MaxHealth(db, goblinDef, goblin.Level) >> 1) * 100 / 50;
        var struck = Assert.Single(sink.Events, e => e.Kind == "boulder_struck_creature");
        Assert.Contains($"creature={goblin.Id} damage={damage}", struck.Detail);
        Assert.True(CreatureDeathRules.IsDown(goblin) || goblin.Dk2StateId == BoulderRules.StunnedState);
    }

    [Fact]
    public void ACreatureThatSurvivesTheStrikeLiesInTwoFiftySixForItsStunDuration_AndTheStrikeIsCappedByTheHealthLeft()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, Row));
        CreatureDef goblinDef = db.CreatureById(goblin.CreatureTypeId);
        int full = goblin.Health;
        // Its whole Hp would kill it; a boulder with 100 left strikes for 100.
        Rolling(db, world, 5, Row).Wear = 3900;
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        for (int i = 0; i < 10 && !sink.Events.Any(e => e.Kind == "boulder_struck_creature"); i++) sim.Run(1);
        Assert.Equal(BoulderRules.StunnedState, goblin.Dk2StateId);
        Assert.Equal(CombatRules.TicksOf(goblinDef.StunDuration), goblin.StunTicks);
        Assert.Contains($"damage=100 landed=100 health={full - 100} wear=4000 knocked",
            Assert.Single(sink.Events, e => e.Kind == "boulder_struck_creature").Detail);
        Assert.Contains(sink.Events, e => e.Kind == "boulder_broken");
    }

    [Fact]
    public void ABoulderReachingAHeartsCentreHurtsTheHeart_TheHeartObjectsHpCappedByTheHealthItHasLeft()
    {
        (GameDatabase db, WorldState world) = Open(corridor: false);
        for (int y = Row - 2; y <= Row + 2; y++)
            for (int x = 10; x <= 14; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.DungeonHeart), Rival);
        world.Player(Rival).HeartHealth = 50000;
        ObjectState boulder = Rolling(db, world, 5, Row);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        for (int i = 0; i < 20 && !sink.Events.Any(e => e.Kind == "boulder_struck_heart"); i++) sim.Run(1);
        // def 13's Hp 34464, capped by the 4000 the boulder has left: it breaks, the heart keeps 46000.
        Assert.Equal(46000, world.Player(Rival).HeartHealth);
        Assert.Equal(Keeper, world.Player(Rival).HeartLastAttackerPlayerId);
        Assert.DoesNotContain(boulder, world.Objects);
    }

    [Fact]
    public void ABoulderRollsThreeTimesItsBaseSpeedWhole_AndSlowsToTwoAndAHalfAsItWears()
    {
        (GameDatabase db, WorldState world) = Open();
        ObjectState boulder = Rolling(db, world, 5, Row);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);
        int start = boulder.PosX;

        sim.Run(1);
        int s = boulder.RollSpeed;
        Assert.Equal((s * 50 / 100) + (50 * s * 5 / 100), boulder.PosX - start);

        int at = boulder.PosX;
        boulder.Wear = 2000; // half worn: the first term halves
        sim.Run(1);
        Assert.Equal((s * 50 / 100 * 2000 / 4000) + (50 * s * 5 / 100), boulder.PosX - at);
    }

    [Fact]
    public void ADeadEndTurnsItBack_EachTurnWearsATenth_AndTheTenthTurnBreaksIt()
    {
        (GameDatabase db, WorldState world) = Open();
        ObjectState boulder = Rolling(db, world, 5, Row);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        for (int i = 0; i < 400 && world.Objects.Contains(boulder); i++) sim.Run(1);

        var turns = sink.Events.Where(e => e.Kind == "boulder_turned").ToList();
        Assert.Equal(10, turns.Count);
        // The impenetrable border at x 0 and 19: each end's last tile turns it back.
        Assert.Contains("at=18,5 roll=-4096,0 wear=400", turns[0].Detail);
        Assert.Contains("at=1,5 roll=4096,0 wear=800", turns[1].Detail);
        Assert.DoesNotContain(boulder, world.Objects);
        Assert.Contains(sink.Events, e => e.Kind == "boulder_broken");
    }

    [Fact]
    public void AtABlockedTileWithBothSidesOpenItTakesTheSideOfItsLaunchSign_AndOneSideOpenTakesThatSide()
    {
        (GameDatabase db, WorldState world) = Open(corridor: false);
        for (int y = 1; y < world.Height - 1; y++) world.Tile(10, y).TerrainId = Dk2TerrainFixture.Rock;
        ObjectState boulder = Rolling(db, world, 5, Row);
        boulder.RollSignY = 1;
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        for (int i = 0; i < 20 && !sink.Events.Any(e => e.Kind == "boulder_turned"); i++) sim.Run(1);
        Assert.Contains("at=9,5 roll=0,4096 wear=400", Assert.Single(sink.Events, e => e.Kind == "boulder_turned").Detail);

        // Going south along x 9, the bottom border blocks it; west is open, east (x 10) rock: west.
        for (int i = 0; i < 40 && sink.Events.Count(e => e.Kind == "boulder_turned") < 2; i++) sim.Run(1);
        Assert.Contains("at=9,10 roll=-4096,0 wear=800", sink.Events.Where(e => e.Kind == "boulder_turned").ElementAt(1).Detail);
    }

    [Fact]
    public void SolidTerrain_APrisonInterior_AndAnObjectThatCannotBeRolledThroughBlockIt_ButNotAPrisonEdgeOrGold()
    {
        (GameDatabase db, WorldState world) = Open(corridor: false);
        Assert.True(BoulderRules.Blocked(world, db, 0, Row));
        Assert.True(BoulderRules.Blocked(world, db, -1, Row));
        for (int y = 2; y <= 4; y++)
            for (int x = 12; x <= 14; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), Keeper);
        Assert.True(BoulderRules.Blocked(world, db, 13, 3));
        Assert.False(BoulderRules.Blocked(world, db, 12, 3));

        world.SpawnObject(db, Dk2TerrainFixture.TrainingDummyObjectId, Keeper, (Row * world.Width) + 7);
        Assert.True(BoulderRules.Blocked(world, db, 7, Row));
        ObjectState gold = world.SpawnObject(db, FloorGold.FloorGoldObjectId, Keeper, (Row * world.Width) + 8);
        gold.GoldHeld = 100;
        Assert.False(BoulderRules.Blocked(world, db, 8, Row));
    }

    [Fact]
    public void WaterWearsATenthOfItsHealthEachTurnItIsOver_LavaASixth()
    {
        (GameDatabase db, WorldState world) = Open();
        world.Tile(7, Row).TerrainId = Dk2TerrainFixture.Water;
        world.Tile(12, Row).TerrainId = Dk2TerrainFixture.Lava;
        ObjectState boulder = Rolling(db, world, 5, Row);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        for (int i = 0; i < 20 && world.Objects.Contains(boulder); i++) sim.Run(1);
        var worn = sink.Events.Where(e => e.Kind == "boulder_worn").Select(e => e.Detail).ToList();
        Assert.NotEmpty(worn);
        Assert.All(worn.Where(d => d.Contains("at=7,5")), d => Assert.Matches(@"wear=(400|800|1200)\b", d));
        Assert.Contains(worn, d => d.Contains("at=12,")); // a sixth: 666 each turn on the lava
    }

    [Fact]
    public void ItSmashesADoorItRollsThrough_ByTheDoorsWholeHealth_AndWearsByIt()
    {
        (GameDatabase db, WorldState world) = Open();
        ref TileState doorTile = ref world.Tile(8, Row);
        doorTile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        doorTile.OwnerPlayerId = Rival;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = 8, Y = Row, PlayerId = Rival, DoorId = 1, Unknown_0011 = new byte[3] }]);
        DoorState door = Assert.Single(world.Doors);
        door.UnderConstruction = false;
        ObjectState boulder = Rolling(db, world, 5, Row);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        for (int i = 0; i < 10 && world.Doors.Count > 0; i++) sim.Run(1);
        Assert.Empty(world.Doors);
        Assert.Contains("damage=400 health=0 wear=400", Assert.Single(sink.Events, e => e.Kind == "boulder_struck_door").Detail);
    }

    [Fact]
    public void ATrapItRollsOverFiresAtIt_AndLosesAQuarterOfItsDefinitionsHealth()
    {
        (GameDatabase db, WorldState world) = Open();
        ref TileState trapTile = ref world.Tile(8, Row);
        trapTile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        trapTile.OwnerPlayerId = Rival;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = 8, Y = Row, TrapId = Dk2TerrainFixture.SpikeTrapId, PlayerId = Rival, NumberOfShots = 1 }]);
        TrapState spike = Assert.Single(world.Traps);
        ObjectState boulder = Rolling(db, world, 5, Row);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        for (int i = 0; i < 10 && !sink.Events.Any(e => e.Kind == "boulder_struck_trap"); i++) sim.Run(1);
        Assert.Contains(sink.Events, e => e.Kind == "trap_fired" && e.Detail.StartsWith($"trap={spike.Id} "));
        Assert.Equal(4000 - (4000 >> 2), spike.Health);
        Assert.Contains("damage=1000 health=3000", Assert.Single(sink.Events, e => e.Kind == "boulder_struck_trap").Detail);
    }

    [Fact]
    public void ItFrightensAnEnemyWithinItsFearRange_BoulderFearAmountScaledByHealthOverTheDefinitions()
    {
        (GameDatabase db, WorldState world) = Open(corridor: false);
        ObjectState boulder = Rolling(db, world, 5, Row);
        boulder.RollX = 0;
        boulder.RollSignX = 0;
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, Row + 2));
        GameSimulation sim = Sim(db, world, new ListEventSink());

        sim.Run(1);
        // One enemy in sight within four tiles; inside three, so it takes all 701 × 4000 / 4000.
        Assert.Equal(701, goblin.TempFear);
    }

    [Fact]
    public void TheBouldersFieldsSurviveASnapshotRoundTrip()
    {
        (GameDatabase db, WorldState world) = Open();
        ObjectState boulder = Rolling(db, world, 5, Row);
        boulder.RollSignY = -1;
        GameSimulation sim = Sim(db, world, new ListEventSink());
        sim.Run(2);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new BoulderSystem()]);
        ObjectState restored = copy.World.ObjectById(boulder.Id)!;
        Assert.Equal((boulder.Health, boulder.PosX, boulder.PosY, boulder.RollX, boulder.RollY, boulder.RollSpeed, boulder.Wear, boulder.RollSignX, boulder.RollSignY),
            (restored.Health, restored.PosX, restored.PosY, restored.RollX, restored.RollY, restored.RollSpeed, restored.Wear, restored.RollSignX, restored.RollSignY));
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }
}
