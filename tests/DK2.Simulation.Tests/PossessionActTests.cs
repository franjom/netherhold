// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Action 26 (keeper-spell-possession.md, Possessed actions): the melee pick over the
/// mixed-class tile chain, the spell slot's gates, scan, branches and stamps.</summary>
public sealed class PossessionActTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    // Yaw 0x200 looks along +x; pitch 0x200 makes the aim horizontal: (4096, 0, 0).
    private const ushort East = 0x200, Level = 0x200;

    private static (GameDatabase Db, WorldState World) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Possessor());
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

    private static GameSimulation Sim(GameDatabase db, WorldState world, params ISimulationSystem[] more)
    {
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem(), .. more]);
        sim.Tick();
        sim.Tick();
        return sim;
    }

    private static ActPossessedCreatureCommand Act(CreatureState me, bool melee, int slot = 0, ushort yaw = East, ushort pitch = Level) =>
        new(Keeper, new(new(new(me.PosX), new(me.PosY), new(me.PosZ)), new(yaw), new(pitch)), melee, new(slot));

    private static void Place(CreatureState c, int x, int y)
    {
        c.PosX = x;
        c.PosY = y;
    }

    // ------------------------------------------------------------------ melee

    [Fact]
    public void MeleeStrikesTheEnemyInTheConeAndStampsBeforeTheSwing_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        GameSimulation sim = Sim(db, world, new CombatSystem());
        int before = enemy.Health;
        long now = sim.CurrentTick.Value;
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(now, me.TimeLastUsedMelee);
        Assert.Equal(now, me.SwingStartTick);
        Assert.True(enemy.Health < before);
        Assert.False(me.SwingInProgress);
    }

    [Fact]
    public void MeleeMissStillStampsAndStartsNothing_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        // Behind the aim: outside the strict cone.
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(9, 10));
        GameSimulation sim = Sim(db, world);
        long now = sim.CurrentTick.Value;
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(now, me.TimeLastUsedMelee);
        Assert.False(me.SwingInProgress);
    }

    [Fact]
    public void MeleeRechargeEqualityRefusesWithoutAStamp_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        GameSimulation sim = Sim(db, world);
        CreatureDef def = db.CreatureById(me.CreatureTypeId);
        long now = sim.CurrentTick.Value;
        // The interval: the least gap at which the strict gate opens, minus one.
        int interval = 0;
        while (!CombatRules.SwingMayBegin(db, Stamped(me, now - interval - 1), def, now)) interval++;
        me.TimeLastUsedMelee = now - interval;
        Assert.False(CombatRules.SwingMayBegin(db, me, def, now));
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(now - interval, me.TimeLastUsedMelee);
        Assert.False(me.SwingInProgress);
    }

    private static CreatureState Stamped(CreatureState me, long stamp)
    {
        me.TimeLastUsedMelee = stamp;
        return me;
    }

    [Fact]
    public void AFirstSurvivorIsKeptAgainstANearerButWorseAimedOne_NotNearestDistance_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        // Tile (11, 9) is visited before (11, 10). A: farther, near the aim; B: nearer, 43 degrees off.
        CreatureState a = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 9));
        CreatureState b = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        Place(a, 47104, 40664);
        Place(b, 45100, 45050);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal((FightTargetKind.Creature, a.Id), (me.SwingTargetKind, me.SwingTargetId));
    }

    [Fact]
    public void ANearerAndBetterAimedLaterCandidateReplacesTheFirst_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        CreatureState a = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 9));
        CreatureState b = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        Place(a, 47104, 40664);
        Place(b, 45156, 43308);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(b.Id, me.SwingTargetId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnOneTileTheLatestArrivalAcrossClassesIsMetFirst_StronglyInferredFromBinary(bool creatureArrivesLast)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(12, 12));
        // The trap: on the aim, farther. The creature: nearer, worse aimed. Neither replaces the
        // other, so the one the chain meets first is kept.
        TrapState trap;
        if (creatureArrivesLast)
        {
            trap = PlaceTrap(db, world, new TilePos(11, 10), Rival);
            Place(enemy, 45100, 45050);
        }
        else
        {
            Place(enemy, 45100, 45050);
            trap = PlaceTrap(db, world, new TilePos(11, 10), Rival);
        }
        Assert.Equal(enemy.TileLink.TileIndex, trap.TileLink.TileIndex);
        Assert.Equal(creatureArrivesLast, enemy.TileLink.Order > trap.TileLink.Order);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(creatureArrivesLast ? FightTargetKind.Creature : FightTargetKind.Trap, me.SwingTargetKind);
    }

    private static TrapState PlaceTrap(GameDatabase db, WorldState world, TilePos at, byte owner)
    {
        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = owner;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = at.X, Y = at.Y, TrapId = Dk2TerrainFixture.SentryTrapId, PlayerId = owner, NumberOfShots = 1 }]);
        TrapState trap = world.Traps[^1];
        trap.Hidden = false;
        return trap;
    }

    /// <summary>A standing door of the fixture's first non-barricade type, which stops sight on its tile.</summary>
    private static DoorState PlaceDoor(GameDatabase db, WorldState world, TilePos at, byte owner)
    {
        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = owner;
        byte doorId = db.Doors.Values.First(d => (d.Flags & DoorFlags.Barricade) == 0).DoorId;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = at.X, Y = at.Y, PlayerId = owner, DoorId = doorId, Unknown_0011 = new byte[3] }]);
        DoorState door = world.Doors[^1];
        Assert.False(door.UnderConstruction);
        return door;
    }

    [Theory]
    [InlineData(East + PossessionRules.AimConeHalfWidth, false)]
    [InlineData(East - PossessionRules.AimConeHalfWidth, false)]
    [InlineData(East + PossessionRules.AimConeHalfWidth - 1, true)]
    [InlineData(East - PossessionRules.AimConeHalfWidth + 1, true)]
    public void ATargetBearingExactlyOnTheConesEdgeIsRefused_BothEndsStrict_StronglyInferredFromBinary(int yaw, bool struck)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        // Straight east of the pose: bearing exactly East, so the yaw puts it on or inside an edge.
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: true, yaw: (ushort)yaw)));
        Assert.Equal(struck, me.SwingInProgress && me.SwingTargetId == enemy.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHiddenEnemyTrapIsNotAMeleeTarget_StronglyInferredFromBinary(bool hidden)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        TrapState trap = PlaceTrap(db, world, new TilePos(11, 10), Rival);
        trap.Hidden = hidden;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(!hidden, me.SwingInProgress && me.SwingTargetKind == FightTargetKind.Trap && me.SwingTargetId == trap.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AThingOfTheCreaturesOwnOwnerIsNotAMeleeTarget_StronglyInferredFromBinary(bool trap)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        if (trap) PlaceTrap(db, world, new TilePos(11, 10), Keeper);
        else world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(11, 10));
        GameSimulation sim = Sim(db, world);
        long now = sim.CurrentTick.Value;
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(now, me.TimeLastUsedMelee);
        Assert.False(me.SwingInProgress);
    }

    [Fact]
    public void ANearerWorseAimedCandidateReplacesWithinThe300Over256Margin_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        // A on (11, 10), met third: 10 degrees off the aim, dot ~4034. B on the pose's own tile, met
        // last: nearer, 25 degrees off, dot ~3712. 4034 < 3712 x 300 / 256 = 4350, so B replaces A;
        // a plain "better aimed" test (x 256 / 256) would keep A.
        CreatureState a = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        CreatureState b = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        Place(a, me.PosX + 4000, me.PosY - 705);
        Place(b, me.PosX + 2000, me.PosY - 933);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(b.Id, me.SwingTargetId);
    }

    [Fact]
    public void ACandidateAtEqualDistanceDoesNotReplace_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        // Mirror images on one tile: equal distance, equal aim. The later arrival is met first and
        // kept; the other is not strictly nearer.
        CreatureState second = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        CreatureState first = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        Place(first, me.PosX + 2400, me.PosY - 1800);
        Place(second, me.PosX + 2400, me.PosY + 1800);
        Assert.True(first.TileLink.Order > second.TileLink.Order);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(first.Id, me.SwingTargetId);
    }

    [Theory]
    [InlineData(East, 100)]
    [InlineData(0x600, 0)]
    public void GoldPickUpTakesTwiceTheLackCappedAt100AndPursesHalf_ReachHeadingHalfOnly_StronglyInferredFromBinary(
        ushort heading, int taken)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 11, 10);
        me.PosX = 45500;
        me.Heading = heading;
        ObjectState pile = world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, (10 * world.Width) + 11);
        pile.GoldHeld = 250;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(250 - taken, pile.GoldHeld);
        Assert.Equal(taken / 2, me.GoldHeld);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GoldReachEndsAtSixFifthsOfTheRadius_FootprintRadiusStandsInForTheUnreadSize_Hypothesis(bool atTheEdge)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        // The bound is read (6/5 of the summed radii, the object's 0); the creature's size is its
        // vtable +0x3C, unread, for which the footprint stands in.
        int bound = db.CreatureById(me.CreatureTypeId).FootprintRadius.Raw * 6 / 5;
        int edge = 0;
        while (((long)(edge + 1) * (edge + 1) >> 12) <= bound) edge++;
        int pileX = CreatureState.CentreOf(11);
        me.PosX = pileX - (atTheEdge ? edge : edge + 1);
        ObjectState pile = world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, (10 * world.Width) + 11);
        pile.GoldHeld = 250;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(atTheEdge, pile.GoldHeld < 250);
    }

    [Fact]
    public void ACreatureOnATortureDeviceIsStampedNotStruck_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.GoblinCreatureId, 10, 10);
        CreatureState victim = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        victim.Dk2StateId = TortureRules.TorturedState;
        GameSimulation sim = Sim(db, world);
        long now = sim.CurrentTick.Value;
        sim.Tick(InputFrame.Of(Act(me, melee: true)));
        Assert.Equal(now, me.TimeLastUsedMelee);
        Assert.False(me.SwingInProgress);
    }

    // ------------------------------------------------------------------ spells

    [Fact]
    public void NoTargetCastsFreeAimAtPosePlusAimAndStampsTheSlot_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 10, 10);
        GameSimulation sim = Sim(db, world, new CombatSystem());
        long now = sim.CurrentTick.Value;
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.Equal(now, me.SpellStamps[0]);
        Assert.Equal((me.PosX + 4096, me.PosY, 0, Level, PossessionRules.FreeAimRange),
            (me.SwingPointX, me.SwingPointY, me.SwingPointZ, me.SwingPitch, me.SwingRange));
        ShotState shot = Assert.Single(world.Shots);
        Assert.Equal(Dk2TerrainFixture.FireballShotId, shot.ShotTypeId);
        Assert.True(shot.VelX > 0);
        Assert.Equal(0, shot.VelY);
    }

    [Fact]
    public void TheSpellScanPrefersTheBestAimedEnemyOverTheNearest_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 5, 10);
        CreatureState far = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(12, 10));
        CreatureState near = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 10));
        // Near, met first: two tiles on and 3000 units aside, score 241 (under 256). Far: on the
        // aim, score 0, strictly lower, so it replaces near.
        near.PosY += 3000;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.Equal((SpellRules.SpellCast, FightTargetKind.Creature, far.Id), (me.SwingKind, me.SwingTargetKind, me.SwingTargetId));
    }

    [Fact]
    public void AnEnemyOutsideTheAccuracyThresholdIsNotTargeted_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 5, 10);
        // 25 degrees off the aim: score ~384, above 45 x 2048 / 360 = 256.
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(9, 12));
        Place(enemy, me.PosX + 8192, me.PosY + 3820);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.Equal(SpellRules.FreeAimCast, me.SwingKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSpellScanSkipsADownEnemy_StronglyInferredFromBinary(bool down)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 5, 10);
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(8, 10));
        if (down) enemy.Health = 0;
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.Equal(down ? (SpellRules.FreeAimCast, FightTargetKind.None) : (SpellRules.SpellCast, FightTargetKind.Creature),
            (me.SwingKind, me.SwingTargetKind));
    }

    // 0x31000 is seven tiles squared in DistanceSquared's units (49 x 4096), and the test is strict.
    [Theory]
    [InlineData(false, 6, true)]
    [InlineData(false, 7, false)]
    [InlineData(false, 9, false)]
    [InlineData(true, 6, true)]
    [InlineData(true, 7, false)]
    [InlineData(true, 9, false)]
    public void TrapsAndDoorsAreScannedOnlyWithinSevenTiles_StronglyInferredFromBinary(bool door, int tiles, bool targeted)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 2, 10);
        var at = new TilePos(2 + tiles, 10);
        if (door) PlaceDoor(db, world, at, Rival);
        else PlaceTrap(db, world, at, Rival);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        byte kind = door ? FightTargetKind.Door : FightTargetKind.Trap;
        Assert.Equal(targeted ? (SpellRules.SpellCast, kind) : (SpellRules.FreeAimCast, FightTargetKind.None),
            (me.SwingKind, me.SwingTargetKind));
    }

    [Fact]
    public void ADoorIsSightedFromOneTileNearerThePose_SoItsOwnBlockingTileDoesNotHideIt_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 5, 10);
        DoorState door = PlaceDoor(db, world, new TilePos(8, 10), Rival);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.Equal((SpellRules.SpellCast, FightTargetKind.Door, door.Id), (me.SwingKind, me.SwingTargetKind, me.SwingTargetId));
    }

    [Fact]
    public void AFriendlySpellScansTheCastersOwnSide_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        // The Priest's slot 0 is Haste, a friendly spell (flag bit 2).
        CreatureState me = Possess(db, world, Dk2TerrainFixture.PriestCreatureId, 5, 10);
        Assert.True(SpellRules.IsFriendly(SpellRules.SpellOf(db, db.CreatureById(me.CreatureTypeId), 0)!));
        CreatureState ally = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 10));
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.Equal((SpellRules.SpellCast, FightTargetKind.Creature, ally.Id), (me.SwingKind, me.SwingTargetKind, me.SwingTargetId));
    }

    [Fact]
    public void ASelfOnlySpellTargetsTheCasterWhateverTheScanFound_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        // The Thief's slot 0 is Invisible: friendly and self-only (flag bit 7).
        CreatureState me = Possess(db, world, Dk2TerrainFixture.ThiefCreatureId, 5, 10);
        Assert.True(SpellRules.IsSelfOnly(SpellRules.SpellOf(db, db.CreatureById(me.CreatureTypeId), 0)!));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 10));
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.Equal((SpellRules.SpellCast, FightTargetKind.Creature, me.Id), (me.SwingKind, me.SwingTargetKind, me.SwingTargetId));
    }

    [Fact]
    public void ASlotStillRechargingAtEqualityDoesNothingAndKeepsItsStamp_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 10, 10);
        GameSimulation sim = Sim(db, world);
        CreatureDef def = db.CreatureById(me.CreatureTypeId);
        long now = sim.CurrentTick.Value;
        int interval = 0;
        while (true)
        {
            me.SpellStamps[0] = now - interval - 1;
            if (SpellRules.CanCast(db, me, def, 0, now)) break;
            interval++;
        }
        me.SpellStamps[0] = now - interval;
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.Equal(now - interval, me.SpellStamps[0]);
        Assert.False(me.SwingInProgress);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(7)]
    public void ASlotOutsideTheSpellsIsRefusedByTheSlotBound_StronglyInferredFromBinary(int slot)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 10, 10);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: slot)));
        Assert.All(me.SpellStamps, s => Assert.Equal(0, s));
        Assert.False(me.SwingInProgress);
    }

    // SpellRules.SpellOf already refuses slot 3 (Slots is 3), so this cannot tell UseSlot's
    // follower-arm return from the slot bound: the guard is inert until a follower arm is built.
    [Fact]
    public void SlotThreeNeverStamps_RefusedByTheSlotBoundFirst_FollowerArmGuardUntested()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 10, 10);
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(12, 10));
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: PossessedSlot.FollowerArm)));
        Assert.All(me.SpellStamps, s => Assert.Equal(0, s));
        Assert.False(me.SwingInProgress);
    }

    [Fact]
    public void AFreeAimShotType30StartsNothingButStillStamps_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.PossessorCreatureId, 10, 10);
        GameSimulation sim = Sim(db, world);
        long now = sim.CurrentTick.Value;
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 2)));
        Assert.Equal(now, me.SpellStamps[2]);
        Assert.False(me.SwingInProgress);
    }

    [Fact]
    public void TheSpeedFlagModifiesSpeedByTheDataWordsAndStamps_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.PossessorCreatureId, 10, 10);
        GameSimulation sim = Sim(db, world);
        long now = sim.CurrentTick.Value;
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 1)));
        Assert.Equal(150, me.SpeedPercent);
        Assert.True(me.SpeedEffectTicks > 0);
        Assert.Equal(now, me.SpellStamps[1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheTeleportFlagMovesToTheHeartsFirstTileCentreAndStampsEitherWay_StronglyInferredFromBinary(bool heart)
    {
        var (db, world) = Open();
        if (heart)
            for (int y = 3; y <= 5; y++)
                for (int x = 3; x <= 5; x++)
                {
                    world.Tile(x, y).TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
                    world.Tile(x, y).OwnerPlayerId = Keeper;
                }
        CreatureState me = Possess(db, world, Dk2TerrainFixture.PossessorCreatureId, 12, 12);
        GameSimulation sim = Sim(db, world);
        long now = sim.CurrentTick.Value;
        (int x0, int y0) = (me.PosX, me.PosY);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.Equal(now, me.SpellStamps[0]);
        if (!heart)
        {
            Assert.Equal((x0, y0), (me.PosX, me.PosY));
            return;
        }
        int anchor = RoomIndex.Build(world, db).Single(r => r.OwnerPlayerId == Keeper).TileIndices[0];
        Assert.Equal((CreatureState.CentreOf(anchor % world.Width), CreatureState.CentreOf(anchor / world.Width), 0),
            (me.PosX, me.PosY, me.PosZ));
        Assert.Equal((me.PosX, me.PosY), (me.LastPosX, me.LastPosY));
    }

    // ------------------------------------------------------------------ state

    [Fact]
    public void FreeAimFieldsAndTileLinksSurviveASnapshot()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, Dk2TerrainFixture.WarlockCreatureId, 10, 10);
        world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, (10 * world.Width) + 12).GoldHeld = 50;
        PlaceTrap(db, world, new TilePos(13, 10), Rival);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Act(me, melee: false, slot: 0)));
        Assert.True(me.SwingInProgress);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new PossessionSystem(), new CombatSystem()]);
        CreatureState back = restored.World.CreatureById(me.Id)!;
        Assert.Equal((me.SwingPointX, me.SwingPointY, me.SwingPointZ, me.SwingPitch, me.SwingRange, me.TileLink),
            (back.SwingPointX, back.SwingPointY, back.SwingPointZ, back.SwingPitch, back.SwingRange, back.TileLink));
        Assert.Equal(world.Objects[0].TileLink, restored.World.Objects[0].TileLink);
        Assert.Equal(world.Traps[0].TileLink, restored.World.Traps[0].TileLink);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        // The restored world keeps relinking: a move gives the next order on both sides.
        me.PosX += 4096;
        back.PosX += 4096;
        Assert.Equal(me.TileLink, back.TileLink);
    }
}
