// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// The portal: the first room whose behaviour is implemented, and the first
/// thing in the simulation that makes a creature that is not an imp.
///
/// <para>The tests that matter most here are the <b>negative</b> ones. A portal
/// has four gates and each is easy to implement so loosely that it never
/// closes; a suite that only checked "a goblin eventually arrives" would pass
/// against a portal that ignored the pool, the cap and the lair entirely. So
/// every gate gets a test that it <i>stops</i> a creature, and one that lifting
/// it lets the creature through.</para>
///
/// <para>The three-way arrangement is deliberate: <see cref="PortalRules"/> is
/// tested here on the transcribed fixture, and
/// <c>DK2.Behavior.Tests.PortalOnRealDataTests</c> re-runs the same questions
/// against the shipped install so the fixture cannot drift from the game it
/// claims to copy.</para>
/// </summary>
public class PortalTests
{
    // 3 and 4, not 1 and 2: PlayerIds says 1 is the hero side and 2 is neutral,
    // and PortalSystem only runs a keeper's portals. The rest of this suite still
    // uses 1 and 2 as "a keeper" and "a rival", which no other system
    // distinguishes — see Q91.
    private const byte Keeper = 3;
    private const byte Rival = 4;
    private const byte GoblinId = 7;

    /// <summary>
    /// The global table plus one level's worth of pool and availability,
    /// applied the way the loader applies a real level's (ADR-0040).
    ///
    /// <para>This is the only invented part of the setup, and it is invented
    /// only in its <i>player id</i>: the shape and the numbers are level 1's —
    /// a pool of fifteen of one creature, and that creature AVAILABLE — re-keyed
    /// from level 1's player 3 to this fixture's keeper.</para>
    /// </summary>
    private static GameDatabase Database(int poolSize = 15, int availability = 3)
    {
        var level = new GlobalVariables();
        level.CreaturePool.Add(new CreaturePoolEntry(GoblinId, poolSize, Keeper));
        level.Availabilities.Add(new AvailabilityEntry(2, Keeper, GoblinId, availability));
        return Dk2TerrainFixture.Database().ForLevel(level);
    }

    private static WorldState World(GameDatabase database) =>
        WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);

    private static GameSimulation Simulation(GameDatabase database, WorldState world) =>
        new(database, world, seed: 99, [new PortalSystem()]);

    /// <summary>
    /// Paints a room the way a keeper who owns it would have it. Portals are not
    /// buildable, so a test that wants one has to place the terrain directly —
    /// which is exactly what a level file does.
    /// </summary>
    private static void Paint(
        GameDatabase database, WorldState world,
        RoomTypeId type, int x0, int y0, int w, int h, byte owner = Keeper)
    {
        RoomDef room = database.Room(type);
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                RoomRules.Place(database, ref tile, room, owner);
            }
        }
    }

    private static void Portal(GameDatabase db, WorldState w, int x0, int y0, byte owner = Keeper) =>
        Paint(db, w, RoomTypeId.Portal, x0, y0, 3, 3, owner);

    private static int Goblins(WorldState world, byte owner = Keeper) =>
        world.Creatures.Count(c => c.OwnerPlayerId == owner && c.CreatureTypeId == GoblinId);

    /// <summary>Long enough for several intervals at the fixture's 25 seconds.</summary>
    private const int LongEnough = 25 * SimulationTiming.TicksPerSecond * 4;

    // ---- the happy path, and its exact timing -----------------------------

    [Fact]
    public void APortalWithALairProducesACreature()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(LongEnough);

        Assert.True(Goblins(world) > 0, "a portal with a lair produced nothing");
    }

    [Fact]
    public void TheFirstCreatureArrivesOnTheTickTheIntervalSays()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
        GameSimulation sim = Simulation(db, world);

        // Zero creatures, so the scaling term is zero and the requirement is
        // the bare 25 seconds. The clock is stepped and read in the same pass,
        // so the arrival lands on the interval's own tick, not the one after.
        int interval = 25 * SimulationTiming.TicksPerSecond;

        sim.Run(interval - 1);
        Assert.Equal(0, Goblins(world));

        sim.Run(1);
        Assert.Equal(1, Goblins(world));
    }

    [Fact]
    public void ACreatureAppearsOnTheHoleInTheMiddleOfThePortal()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(LongEnough);

        CreatureState goblin = world.Creatures.First(c => c.CreatureTypeId == GoblinId);
        Assert.Equal(new TilePos(6, 6), goblin.Tile);
    }

    [Fact]
    public void TheWaitGrowsWithTheNumberOfCreaturesHeld()
    {
        GameDatabase db = Database();

        // max(base, base * held / 2), so the scaling only bites past two.
        //
        // That the wait grows is CONFIRMED (owner, 2026-09-04). The step of
        // base/2 is not: it is the part of Q86 still open, and it is what the
        // n=4 and n=15 rows below pin. If the real slope turns out to be
        // different, those two rows are what fails.
        int baseTicks = 25 * SimulationTiming.TicksPerSecond;
        Assert.Equal(baseTicks, PortalRules.SpawnIntervalTicks(db, 0));
        Assert.Equal(baseTicks, PortalRules.SpawnIntervalTicks(db, 1));
        Assert.Equal(baseTicks, PortalRules.SpawnIntervalTicks(db, 2));
        Assert.Equal(baseTicks * 2, PortalRules.SpawnIntervalTicks(db, 4));
        Assert.Equal(baseTicks * 15 / 2, PortalRules.SpawnIntervalTicks(db, 15));

        // Halving never rounds, because a whole number of seconds at 20 ticks a
        // second is always even. If TicksPerSecond ever becomes odd this fails
        // rather than quietly drifting.
        Assert.Equal(0, baseTicks % 2);
    }

    // ---- gate 0: the creature's own flags --------------------------------

    /// <summary>
    /// The fixture Goblin, with its flags replaced. Everything else — terrain,
    /// rooms, variables, spells — is the fixture's own, so a test built on this
    /// differs from the rest of the suite in exactly one creature flag word.
    /// </summary>
    private static GameDatabase DatabaseWithGoblinFlags(CreatureFlags flags)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        CreatureDef goblin = fixture.CreatureById(GoblinId) with { Flags = flags };

        var level = new GlobalVariables();
        level.CreaturePool.Add(new CreaturePoolEntry(GoblinId, 15, Keeper));
        level.Availabilities.Add(new AvailabilityEntry(2, Keeper, GoblinId, 3));

        return new GameDatabase(
            fixture.Terrain.Values,
            fixture.Rooms.Values,
            fixture.Creatures.Values
                .Where(c => c.CreatureId != GoblinId)
                .Append(goblin),
            fixture.Variables,
            fixture.KeeperSpells.Values).ForLevel(level);
    }

    [Fact]
    public void ACreatureWithoutThePortalFlagNeverArrives()
    {
        // Unlocked, pooled, attracted — and still ineligible, because the
        // entrance tests the creature's own flags first. This is the case the
        // shipped campaign actually contains: nine hero creatures are AVAILABLE
        // and pooled for keepers on the Frosty levels and carry no portal flag.
        GameDatabase db = DatabaseWithGoblinFlags(CreatureFlags.IsEvil);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(LongEnough);

        Assert.DoesNotContain(world.Creatures, c => c.CreatureTypeId == GoblinId);
    }

    [Fact]
    public void AUniqueCreatureNeverArrivesEvenCarryingThePortalFlag()
    {
        // Which is why DK2 needs two tests rather than one: all thirteen unique
        // creatures carry AvailableViaPortal, so the flag alone would let a
        // Unique Dark Angel walk out of a portal.
        GameDatabase db = DatabaseWithGoblinFlags(
            CreatureFlags.IsEvil | CreatureFlags.AvailableViaPortal | CreatureFlags.IsUnique);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(LongEnough);

        Assert.DoesNotContain(world.Creatures, c => c.CreatureTypeId == GoblinId);
    }

    [Fact]
    public void TheFlagGateIsTheTwoFlagsAndNothingElse()
    {
        CreatureDef goblin = Dk2TerrainFixture.Goblin();

        Assert.True(PortalRules.CanComeFromPortal(goblin));
        Assert.False(PortalRules.CanComeFromPortal(
            goblin with { Flags = goblin.Flags & ~CreatureFlags.AvailableViaPortal }));
        Assert.False(PortalRules.CanComeFromPortal(
            goblin with { Flags = goblin.Flags | CreatureFlags.IsUnique }));

        // A worker is excluded by the same gate rather than by a special case:
        // the Imp carries no portal flag, so nothing has to know it is an imp.
        Assert.False(PortalRules.CanComeFromPortal(Dk2TerrainFixture.Imp()));
    }

    // ---- gate 1: attraction ----------------------------------------------

    [Fact]
    public void APortalWithNoLairProducesNothingHowaverLongItWaits()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);

        Simulation(db, world).Run(LongEnough);

        Assert.Empty(world.Creatures);
    }

    [Fact]
    public void BuildingTheLairLetsTheBankedWaitThrough()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        GameSimulation sim = Simulation(db, world);

        // Long past the interval with nothing to attract anything.
        sim.Run(LongEnough);
        Assert.Empty(world.Creatures);

        // The clock kept running while the portal was blocked, so the first
        // goblin is owed and arrives on the next tick rather than 25 seconds
        // later. That is the whole reason the clock counts up.
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
        sim.Run(1);

        Assert.Equal(1, Goblins(world));
    }

    [Fact]
    public void ARoomTooSmallForACreaturesAttractionDoesNotAttractIt()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        CreatureDef goblin = db.CreatureById(GoblinId);

        // The Goblin's own slot has no minimum, so demanding one is the way to
        // test the size clause on the fixture. Nine tiles wanted, four given.
        var demanding = goblin with
        {
            Attractions = [new CreatureAttraction(1, (byte)RoomTypeId.Lair, 9)],
        };

        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 2, 2);
        List<RoomInstance> rooms = RoomIndex.Build(world, db);

        Assert.False(PortalRules.IsAttracted(demanding, rooms));
        Assert.True(PortalRules.IsAttracted(goblin, rooms));
    }

    [Fact]
    public void ACreaturesMinimumIsOneRoomsSizeAndNotTheKeepersTotal()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var demanding = db.CreatureById(GoblinId) with
        {
            Attractions = [new CreatureAttraction(1, (byte)RoomTypeId.Lair, 9)],
        };

        WorldState world = World(db);

        // Three separate 2x2 lairs: twelve tiles between them, and not one of
        // them is the nine-tile room the creature asked for.
        Paint(db, world, RoomTypeId.Lair, 5, 5, 2, 2);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 2, 2);
        Paint(db, world, RoomTypeId.Lair, 5, 9, 2, 2);
        Assert.False(PortalRules.IsAttracted(demanding, RoomIndex.Build(world, db)));

        // One room of nine, and it is satisfied.
        Paint(db, world, RoomTypeId.Lair, 9, 8, 3, 3);
        Assert.True(PortalRules.IsAttracted(demanding, RoomIndex.Build(world, db)));
    }

    [Fact]
    public void ACreatureThatWantsTwoRoomsNeedsBoth()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var fussy = db.CreatureById(GoblinId) with
        {
            Attractions =
            [
                new CreatureAttraction(1, (byte)RoomTypeId.Lair, 0),
                new CreatureAttraction(1, (byte)RoomTypeId.Treasury, 0),
            ],
        };

        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
        Assert.False(PortalRules.IsAttracted(fussy, RoomIndex.Build(world, db)));

        Paint(db, world, RoomTypeId.Treasury, 9, 7, 1, 1);
        Assert.True(PortalRules.IsAttracted(fussy, RoomIndex.Build(world, db)));
    }

    [Fact]
    public void ARivalsLairDoesNotAttractAnythingToYourPortal()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1, owner: Rival);

        Simulation(db, world).Run(LongEnough);

        Assert.Empty(world.Creatures);
    }

    // ---- gate 2: availability --------------------------------------------

    [Fact]
    public void ACreatureThisLevelHasNotUnlockedNeverArrives()
    {
        // UNAVAILABLE (1) rather than AVAILABLE (3).
        GameDatabase db = Database(availability: 1);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(LongEnough);

        Assert.Empty(world.Creatures);
    }

    [Fact]
    public void AvailabilityIsPerPlayerAndNotPerLevel()
    {
        GameDatabase db = Database();

        Assert.True(PortalRules.IsUnlocked(db, Keeper, GoblinId));
        Assert.False(PortalRules.IsUnlocked(db, Rival, GoblinId));

        // A creature the level never mentions is not unlocked for anybody. That
        // is what makes level 1 a tutorial rather than a free-for-all.
        Assert.False(PortalRules.IsUnlocked(db, Keeper, 3));
    }

    // ---- gate 3: the pool ------------------------------------------------

    [Fact]
    public void APoolOfThreeStopsAtThree()
    {
        GameDatabase db = Database(poolSize: 3);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        // Twenty intervals' worth at the fixture's rate, ignoring the scaling,
        // so running out of patience cannot be mistaken for running out of pool.
        Simulation(db, world).Run(25 * SimulationTiming.TicksPerSecond * 20 * 4);

        Assert.Equal(3, Goblins(world));
    }

    [Fact]
    public void APoolOfNothingProducesNothing()
    {
        GameDatabase db = Database(poolSize: 0);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(LongEnough);

        Assert.Empty(world.Creatures);
    }

    [Fact]
    public void APoolIsAStandingLimitSoAVacancyIsFilled()
    {
        // The reading this rests on: fifteen in the pool means fifteen at a
        // time, not fifteen ever (Q87). Remove one of three and a fourth
        // arrives; under the draining reading it never would.
        GameDatabase db = Database(poolSize: 3);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
        GameSimulation sim = Simulation(db, world);

        sim.Run(25 * SimulationTiming.TicksPerSecond * 20 * 4);
        Assert.Equal(3, Goblins(world));

        world.RemoveCreature(world.Creatures.First(c => c.CreatureTypeId == GoblinId).Id);
        Assert.Equal(2, Goblins(world));

        sim.Run(25 * SimulationTiming.TicksPerSecond * 4);
        Assert.Equal(3, Goblins(world));
    }

    // ---- gate 4: the cap -------------------------------------------------

    [Fact]
    public void TheCapIsFifteenForOnePortalAndFiveMoreForEachAfterIt()
    {
        GameDatabase db = Database();

        Assert.Equal(0, PortalRules.CreatureCapacity(db, 0));
        Assert.Equal(15, PortalRules.CreatureCapacity(db, 1));
        Assert.Equal(20, PortalRules.CreatureCapacity(db, 2));
        Assert.Equal(35, PortalRules.CreatureCapacity(db, 5));
    }

    [Fact]
    public void TheCapStopsAPortalWithPoolToSpare()
    {
        // Pool of 40, cap of 15. Whichever gate is missing, this fails.
        GameDatabase db = Database(poolSize: 40);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        // The scaling makes the last few slow, so this has to be generous:
        // fifteen creatures at up to 25 x 15 / 2 seconds each.
        Simulation(db, world).Run(25 * SimulationTiming.TicksPerSecond * 200);

        Assert.Equal(15, Goblins(world));
    }

    [Fact]
    public void ImpsDoNotCountAgainstThePortalCap()
    {
        GameDatabase db = Database();
        WorldState world = World(db);

        CreatureDef imp = db.CreatureByName("Imp")!;
        for (int i = 0; i < 30; i++)
            world.SpawnCreature(db, imp.CreatureId, Keeper, new TilePos(2, 2));

        Assert.Equal(0, PortalRules.CreatureCount(world, db, Keeper));

        world.SpawnCreature(db, GoblinId, Keeper, new TilePos(2, 2));
        Assert.Equal(1, PortalRules.CreatureCount(world, db, Keeper));
    }

    [Fact]
    public void ARivalsCreaturesDoNotCountAgainstYourCap()
    {
        GameDatabase db = Database();
        WorldState world = World(db);

        for (int i = 0; i < 5; i++)
            world.SpawnCreature(db, GoblinId, Rival, new TilePos(2, 2));

        Assert.Equal(0, PortalRules.CreatureCount(world, db, Keeper));
        Assert.Equal(5, PortalRules.CreatureCount(world, db, Rival));
    }

    // ---- what counts as a portal at all ----------------------------------

    [Fact]
    public void NoPortalMeansNoCreaturesHoweverManyLairsThereAre()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, 5, 5, 3, 3);

        Simulation(db, world).Run(LongEnough);

        Assert.Empty(world.Creatures);
    }

    [Fact]
    public void APortalTooSmallToBeAPortalProducesNothing()
    {
        GameDatabase db = Database();
        WorldState world = World(db);

        // 2x3: portal terrain, but under the room's own 3x3 minimum.
        Paint(db, world, RoomTypeId.Portal, 5, 5, 2, 3);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(LongEnough);

        Assert.Empty(world.Creatures);
    }

    [Fact]
    public void APortalWhoseCentreBelongsToSomebodyElseProducesNothing()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        // The hole in the middle taken by a rival. What is left is eight tiles
        // of the keeper's portal with no hole for anything to come out of.
        Paint(db, world, RoomTypeId.Portal, 6, 6, 1, 1, owner: Rival);

        Simulation(db, world).Run(LongEnough);

        Assert.Empty(world.Creatures);
    }

    [Fact]
    public void ARivalsPortalProducesForTheRivalAndNotForYou()
    {
        var level = new GlobalVariables();
        level.CreaturePool.Add(new CreaturePoolEntry(GoblinId, 15, Rival));
        level.Availabilities.Add(new AvailabilityEntry(2, Rival, GoblinId, 3));
        GameDatabase db = Dk2TerrainFixture.Database().ForLevel(level);

        WorldState world = World(db);
        Portal(db, world, 5, 5, owner: Rival);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1, owner: Rival);

        Simulation(db, world).Run(LongEnough);

        Assert.Equal(0, Goblins(world, Keeper));
        Assert.True(Goblins(world, Rival) > 0);
    }

    [Fact]
    public void ThreeByThreeIsExactlyTheTwoPortals()
    {
        // PortalRules.IsEntrance names the two portals by id, on the grounds
        // that they are the only ThreeByThree rooms. This is the assertion that
        // keeps that reasoning honest on the fixture; the real-data suite runs
        // it against all 26 shipped rooms.
        GameDatabase db = Dk2TerrainFixture.Database();
        RoomDef[] threeByThree = db.Rooms.Values
            .Where(r => r.Construction == TileConstruction.ThreeByThree)
            .ToArray();

        Assert.Equal(["Portal"], threeByThree.Select(r => r.Name).ToArray());
        Assert.All(threeByThree, r => Assert.True(PortalRules.IsEntrance(r)));
    }

    // ---- the quirk, kept on purpose --------------------------------------

    [Fact]
    public void TwoPortalsRipeOnTheSameTickBothProduce()
    {
        // Reproduced, not fixed (fidelity priority 4). The keeper's creature
        // count is read once a tick, so the second portal does not see the
        // first one's arrival — neither the longer interval it implies nor the
        // slot it took. OpenKeeper documents the same behaviour in the
        // original and replicates it deliberately.
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Portal(db, world, 5, 9);
        Paint(db, world, RoomTypeId.Lair, 12, 5, 1, 1);

        Simulation(db, world).Run(25 * SimulationTiming.TicksPerSecond);

        Assert.Equal(2, Goblins(world));
    }

    [Fact]
    public void TwoPortalsRaiseTheCapByFive()
    {
        GameDatabase db = Database(poolSize: 40);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Portal(db, world, 5, 9);
        Paint(db, world, RoomTypeId.Lair, 12, 5, 1, 1);

        Simulation(db, world).Run(25 * SimulationTiming.TicksPerSecond * 400);

        // 15 + 5, plus at most one overshoot from the once-a-tick count above.
        Assert.InRange(Goblins(world), 20, 21);
    }

    // ---- the clock -------------------------------------------------------

    [Fact]
    public void AnUnclaimedPortalsClockDoesNotRun()
    {
        GameDatabase db = Database();
        WorldState world = World(db);

        // The real unclaimed case: a portal belonging to the neutral player,
        // which is what 167 of the campaign's portals are.
        Portal(db, world, 5, 5, owner: PlayerIds.Neutral);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(LongEnough);

        // The neutral player is registered in this world, so the portal is not
        // skipped for want of a player — it is skipped because the neutral
        // player is not a keeper. What this pins is the clock: it stays at
        // zero, so whoever claims this portal waits a full interval rather than
        // being handed the wait it banked while nobody owned it (Q86).
        Assert.Empty(world.Creatures);
        Assert.Equal(0, world.Tile(6, 6).EntranceIdleTicks);
    }

    [Fact]
    public void TheClockLivesOnTheHoleAndNowhereElse()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);

        Simulation(db, world).Run(100);

        Assert.Equal(100, world.Tile(6, 6).EntranceIdleTicks);
        for (int y = 5; y <= 7; y++)
        {
            for (int x = 5; x <= 7; x++)
            {
                if (x == 6 && y == 6) continue;
                Assert.Equal(0, world.Tile(x, y).EntranceIdleTicks);
            }
        }
    }

    [Fact]
    public void TheClockResetsOnAnArrivalRatherThanCarryingTheOvershoot()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
        GameSimulation sim = Simulation(db, world);

        int interval = 25 * SimulationTiming.TicksPerSecond;
        sim.Run(interval);
        Assert.Equal(1, Goblins(world));
        Assert.Equal(0, world.Tile(6, 6).EntranceIdleTicks);

        sim.Run(5);
        Assert.Equal(5, world.Tile(6, 6).EntranceIdleTicks);
    }
}
