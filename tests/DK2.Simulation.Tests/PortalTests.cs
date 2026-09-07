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

    /// <summary>
    /// The interval is <c>(n² + base)</c> game seconds against this portal's own
    /// generation tally — read from the binary, then measured twice.
    ///
    /// <para>This replaces <c>TheWaitGrowsWithTheNumberOfCreaturesHeld</c>,
    /// which pinned <c>max(base, base × n ÷ 2)</c> and had been green for weeks
    /// with a comment admitting the slope was ours. It broke on the first line
    /// when the real formula went in, which is what it was renamed to do.</para>
    /// </summary>
    [Fact]
    public void TheWaitIsTheSquareOfThisPortalsOwnTallyPlusTheBase()
    {
        GameDatabase db = Database();
        int second = SimulationTiming.TicksPerSecond;

        // n^2 + 25 seconds, and the base is an addition rather than a factor.
        Assert.Equal(25 * second, PortalRules.SpawnIntervalTicks(db, 0));
        Assert.Equal(26 * second, PortalRules.SpawnIntervalTicks(db, 1));
        Assert.Equal(29 * second, PortalRules.SpawnIntervalTicks(db, 2));
        Assert.Equal(34 * second, PortalRules.SpawnIntervalTicks(db, 3));
        Assert.Equal(41 * second, PortalRules.SpawnIntervalTicks(db, 4));
        Assert.Equal(50 * second, PortalRules.SpawnIntervalTicks(db, 5));

        // Where the old formula's Math.Max floor said nothing changed for three
        // arrivals: it gave 25, 25, 25 for n = 0, 1, 2. The owner measured
        // 25, 26, 32 and 25, 26, 30 on two separate runs, so the low end is
        // where the two formulas differ most visibly and where the old one was
        // furthest from the game.
        Assert.NotEqual(
            PortalRules.SpawnIntervalTicks(db, 0),
            PortalRules.SpawnIntervalTicks(db, 1));

        // A worn-out portal, and it is a big number on purpose: at n = 15 a
        // level-25 portal takes four minutes. Nothing lowers the tally, so this
        // is a portal that has genuinely worn out rather than one that is busy.
        Assert.Equal(250 * second, PortalRules.SpawnIntervalTicks(db, 15));

        // No rounding anywhere: integer seconds squared and added, then
        // converted once. The old formula needed a halving and an argument
        // about why it never rounded.
        Assert.Equal(0, PortalRules.SpawnIntervalTicks(db, 7) % second);

        // A negative tally cannot arise, but the clamp is what makes that true
        // rather than a hope.
        Assert.Equal(25 * second, PortalRules.SpawnIntervalTicks(db, -3));
    }

    /// <summary>
    /// The owner's two timing runs, as data. Base 25, gaps in seconds, measured
    /// by hand off a claimed portal on 2026-09-07 (Q86,
    /// <c>observation-checklist.md</c>).
    ///
    /// <para>The tolerance is <b>4 seconds a gap</b>, and it is not slack: these
    /// were read off a wall clock by a human watching a game, and the owner said
    /// so both times — <i>"not completely precise"</i>, <i>"I am still measuring
    /// by hand"</i>. What the test is for is the <b>shape</b>: the old formula
    /// misses run 1's third gap by 7 seconds and its sixth by 11, and no
    /// tolerance that admits the quadratic admits that too.</para>
    ///
    /// <para><b>Run 1 has one gap outside the tolerance and it is named in the
    /// data rather than absorbed by a wider one.</b> Its fourth gap is 40
    /// seconds against a predicted 34, and every gap after it is back inside
    /// tolerance — so the cumulative error steps once and stays flat instead of
    /// wandering, which is what a one-off block looks like: the portal came due,
    /// nothing was eligible, it waited, and the clock was stamped at the actual
    /// arrival. Widening the tolerance to 7 seconds would have hidden it and
    /// would also have let the old formula through on three of the six gaps.
    /// HYPOTHESIS — a single mistimed reading explains it equally well, and the
    /// owner's own caveat allows for that.</para>
    /// </summary>
    [Theory]
    // run 1, 2026-09-07: seven arrivals, the run that first broke the old slope.
    // Index 3 is the 40-second gap discussed above.
    [InlineData(new[] { 25, 26, 30, 40, 41, 51, 59 }, 3)]
    // run 2, same day, re-run deliberately to settle the per-portal question.
    // Every gap inside tolerance, so no exception is allowed.
    [InlineData(new[] { 25, 26, 32, 33, 42 }, -1)]
    public void ThePredictedGapsMatchTheOwnersStopwatch(
        int[] observedSeconds, int knownAnomalyIndex)
    {
        GameDatabase db = Database();
        const int Tolerance = 4;

        int outside = 0;
        for (int n = 0; n < observedSeconds.Length; n++)
        {
            int predicted =
                PortalRules.SpawnIntervalTicks(db, n) / SimulationTiming.TicksPerSecond;
            int error = Math.Abs(observedSeconds[n] - predicted);

            if (error <= Tolerance) continue;

            outside++;
            Assert.Equal(knownAnomalyIndex, n);

            // Even the anomaly is bounded: a block long enough to matter would
            // not look like this, and 10 seconds is well inside what a single
            // missed arrival would cost.
            Assert.InRange(error, Tolerance + 1, 10);
        }

        // Exactly as many outliers as the data declares — so a future change
        // that starts missing a second gap fails rather than passing quietly
        // under the first one's licence.
        Assert.Equal(knownAnomalyIndex >= 0 ? 1 : 0, outside);
    }

    /// <summary>
    /// <b>The measurement that settled Q86's last open half.</b> A second portal
    /// claimed after the first had produced five creatures started its own
    /// series again — the owner measured gaps of 27, 30, 31, 33 seconds from the
    /// claim.
    ///
    /// <para>Three formulas, one measurement. A per-room tally predicts 25, 26,
    /// 29, 34 (mean absolute error 2.3 s). A keeper-wide tally predicts 50, 61,
    /// 74, 89 (38.3 s). The formula this project shipped for weeks predicted
    /// 62, 75, 87, 100 (50.8 s). It is not a close call, and the visible
    /// version needs no clock at all: <b>a freshly claimed portal runs at full
    /// speed however worn out its neighbour is.</b></para>
    /// </summary>
    [Fact]
    public void ASecondPortalRunsItsOwnTallyAndNotTheKeepersFifth()
    {
        GameDatabase db = Database();
        int second = SimulationTiming.TicksPerSecond;

        int[] observed = { 27, 30, 31, 33 };
        const int FirstPortalHadProduced = 5;

        for (int n = 0; n < observed.Length; n++)
        {
            int perRoom = PortalRules.SpawnIntervalTicks(db, n) / second;
            int keeperWide =
                PortalRules.SpawnIntervalTicks(db, FirstPortalHadProduced + n) / second;

            // The per-room reading is what the code computes and what was
            // measured; the keeper-wide reading is more than twice as far out on
            // the very first gap and diverges from there.
            Assert.InRange(observed[n], perRoom - 4, perRoom + 4);
            Assert.True(
                Math.Abs(observed[n] - perRoom) < Math.Abs(observed[n] - keeperWide),
                $"gap {n}: observed {observed[n]}s is nearer per-room {perRoom}s " +
                $"than keeper-wide {keeperWide}s");
        }

        // And the headline, without the arithmetic: a brand new portal beside a
        // worn one is not slowed by it.
        Assert.Equal(25 * second, PortalRules.SpawnIntervalTicks(db, 0));
        Assert.Equal(250 * second, PortalRules.SpawnIntervalTicks(db, 15));
    }

    /// <summary>
    /// A rival taking a worn-out portal gets it at the base interval — the
    /// tally and the clock both go back to zero.
    ///
    /// <para><b>CONFIRMED both ways, 2026-09-07.</b> The owner, unprompted:
    /// <i>"if an enemy keeper takes one of the portals from me [...] then yes,
    /// it resets to zero for that portal and that keeper"</i>. And in the
    /// binary: DK2's room-ownership-change routine fires the room type's own
    /// handler after re-linking the room to its new owner, and for both portal
    /// types that handler stamps the clock and zeros the tally.</para>
    ///
    /// <para>This was the last open part of Q86 and the reason the formula sat
    /// unbuilt for a day — a tally that never reset risked a portal that grinds
    /// to a halt where the original's recovers.</para>
    /// </summary>
    [Fact]
    public void ARivalTakingAWornPortalGetsItAtTheBaseInterval()
    {
        GameDatabase db = Database();
        WorldState world = World(db);

        // A portal owned by the keeper, worn down to a four-minute wait.
        ref TileState centre = ref world.Tile(1, 1);
        centre.TerrainId = db.Room(RoomTypeId.Portal).TerrainId;
        centre.OwnerPlayerId = Keeper;
        centre.Health = db.TerrainById(centre.TerrainId).StartingHealth;
        centre.EntranceCreaturesGenerated = 15;
        centre.EntranceIdleTicks = 200;

        Assert.Equal(
            250 * SimulationTiming.TicksPerSecond,
            PortalRules.SpawnIntervalTicks(db, centre.EntranceCreaturesGenerated));

        // The rival takes the tile. ConvertRoom is the takeover work, and it is
        // the same path an imp drives.
        while (world.Tile(1, 1).OwnerPlayerId == Keeper)
        {
            TileWorkResult result = TileWorkRules.Apply(
                db, ref world.Tile(1, 1), TileWorkKind.ConvertRoom, Rival);
            Assert.NotEqual(TileWorkOutcome.Rejected, result.Outcome);
        }

        ref TileState taken = ref world.Tile(1, 1);
        Assert.Equal(Rival, taken.OwnerPlayerId);

        // Both, and the clock matters as much as the tally: without it a rival
        // could inherit a portal already most of the way to its next creature.
        Assert.Equal(0, taken.EntranceCreaturesGenerated);
        Assert.Equal(0, taken.EntranceIdleTicks);
        Assert.Equal(
            25 * SimulationTiming.TicksPerSecond,
            PortalRules.SpawnIntervalTicks(db, taken.EntranceCreaturesGenerated));
    }

    /// <summary>
    /// Both portal types raise the creature cap, and neither the count nor the
    /// rate is ours.
    ///
    /// <para><b>CONFIRMED from the binary, 2026-09-07.</b> DK2's capacity
    /// routine walks the player's own room list twice — once counting room type
    /// 3 (Portal) and once type <c>0x19</c> (Hero Portal) — then returns
    /// <c>first + (total - 1) * perAdditional</c>. This project counted only
    /// type 3, so a keeper who took a Hero Portal received its arrivals but not
    /// its five extra places.</para>
    ///
    /// <para>The second variable is the interesting half. A raw scan for its
    /// global finds <b>one hit, the loader's setter</b> — the exact signature
    /// this project has used fourteen times to call a variable cut. It is live:
    /// its byte sits immediately after the first variable's, and the compiler
    /// loads a <i>word</i> and takes the high half, so the second address never
    /// appears in any instruction at all.</para>
    /// </summary>
    [Fact]
    public void BothPortalTypesRaiseTheCapAndTheRateIsRead()
    {
        GameDatabase db = Database();

        Assert.Equal(0, PortalRules.CreatureCapacity(db, 0));
        Assert.Equal(15, PortalRules.CreatureCapacity(db, 1));
        Assert.Equal(20, PortalRules.CreatureCapacity(db, 2));
        Assert.Equal(35, PortalRules.CreatureCapacity(db, 5));

        // A Portal's terrain is a portal's and ordinary floor is not, which is
        // what the ownership reset tests on every converted tile. The Hero
        // Portal half is asserted on the shipped data instead, in
        // PortalOnRealDataTests — this fixture defines no room 25, and asking
        // for one by id here threw rather than answering.
        Assert.True(PortalRules.IsEntrance(db.Room(RoomTypeId.Portal)));
        Assert.True(PortalRules.IsPortalTerrain(db, db.Room(RoomTypeId.Portal).TerrainId));
        Assert.False(PortalRules.IsPortalTerrain(db, Dk2TerrainFixture.ClaimedPath));
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
        RoomDef portal = Dk2TerrainFixture.Database().Room(RoomTypeId.Portal);

        Assert.True(PortalRules.CanComeFromPortal(goblin, portal));
        Assert.False(PortalRules.CanComeFromPortal(
            goblin with { Flags = goblin.Flags & ~CreatureFlags.AvailableViaPortal }, portal));
        Assert.False(PortalRules.CanComeFromPortal(
            goblin with { Flags = goblin.Flags | CreatureFlags.IsUnique }, portal));

        // A worker is excluded by the same gate rather than by a special case:
        // the Imp carries no portal flag, so nothing has to know it is an imp.
        Assert.False(PortalRules.CanComeFromPortal(Dk2TerrainFixture.Imp(), portal));
    }

    // ---- the two portals are not interchangeable --------------------------

    /// <summary>
    /// The room type picks the creature flag, so the two portals admit disjoint
    /// rosters — and a keeper who claims a Hero Portal gets a room that will not
    /// emit any creature the keeper has.
    ///
    /// <para>CONFIRMED by the owner 2026-09-07: <i>"hero portal is not something
    /// a keeper can use, nor can an evil keeper summon good creatures"</i>, and
    /// <i>"hero portal IS AN ENTRANCE, but for goodly heroes"</i>. And in the
    /// binary, in one condition: the candidate builder requires
    /// <c>def+0x686 &amp; 0x400</c> when <c>room+0x18 == 3</c> and
    /// <c>def+0x68a &amp; 8</c> when <c>room+0x18 == 0x19</c>.</para>
    ///
    /// <para>The Hero Portal room def is synthesised from the Portal's here
    /// because the fixture defines no room 25 — asking the fixture for it by id
    /// once broke sixty unrelated tests. What the rule reads off the room is its
    /// id and nothing else, so re-labelling the Portal exercises exactly the
    /// branch under test. <c>PortalOnRealDataTests</c> does the same against the
    /// shipped Hero Portal.</para>
    ///
    /// <para><b>The room type really is the discriminator, confirmed in play</b>
    /// (owner, 2026-09-07, Q211): a keeper who claims one on Frosty's Castle
    /// receives the mercenary roster as its own units. Had the gate been the
    /// <i>owner</i> — the intuitive reading, and the one this test would have
    /// passed just as happily under — that portal would have produced nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTwoPortalsAdmitDisjointCreaturesBecauseTheRoomTypePicksTheFlag()
    {
        GameDatabase db = Database();
        RoomDef portal = db.Room(RoomTypeId.Portal);
        RoomDef heroPortal = portal with { RoomId = (byte)RoomTypeId.HeroPortal };

        CreatureDef goblin = Dk2TerrainFixture.Goblin();

        // The keeper's own creature: fine out of a Portal, refused by a Hero
        // Portal however the keeper came to own one.
        Assert.True(PortalRules.CanComeFromPortal(goblin, portal));
        Assert.False(PortalRules.CanComeFromPortal(goblin, heroPortal));

        // And the mirror image. The nine real creatures shaped like this carry
        // AvailableViaHeroPortal and not AvailableViaPortal, which is why a
        // keeper's portal cannot produce a Knight even on the Frosty levels
        // where the level unlocks and pools one.
        CreatureDef heroSide = goblin with
        {
            Flags = goblin.Flags & ~CreatureFlags.AvailableViaPortal,
            Flags2 = CreatureFlags2.AvailableViaHeroPortal,
        };

        Assert.True(PortalRules.CanComeFromPortal(heroSide, heroPortal));
        Assert.False(PortalRules.CanComeFromPortal(heroSide, portal));

        // The uniqueness test sits outside the or in the original, so it refuses
        // both sides rather than only the keeper's.
        Assert.False(PortalRules.CanComeFromPortal(
            heroSide with { Flags = heroSide.Flags | CreatureFlags.IsUnique }, heroPortal));
        Assert.False(PortalRules.CanComeFromPortal(
            goblin with { Flags = goblin.Flags | CreatureFlags.IsUnique }, portal));
    }

    /// <summary>
    /// A portal too broken to produce anything still raises the creature cap.
    ///
    /// <para>DK2's capacity routine walks the owner's room list and tests
    /// <c>room[0x18] == type</c> and nothing else — no size, no centre tile, no
    /// notion of the room working. So the keeper here holds one working portal
    /// and one ring whose middle a rival has taken, and is capped at 20 rather
    /// than 15.</para>
    ///
    /// <para>This is the same rule that makes <b>claiming a dead Hero Portal
    /// raise a keeper's creature limit</b>, which cannot be tested here for want
    /// of a room 25 in the fixture.</para>
    /// </summary>
    [Fact]
    public void APortalTooBrokenToProduceStillRaisesTheCap()
    {
        GameDatabase db = Database();
        WorldState world = World(db);

        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        // A second portal, then a rival takes its middle. The keeper keeps a
        // 4-connected ring: still one room, still a portal, no longer able to
        // produce because it does not hold the tile a creature climbs out of.
        Portal(db, world, 10, 6);
        Paint(db, world, RoomTypeId.Portal, 11, 7, 1, 1, Rival);

        var sim = new GameSimulation(db, world, seed: 99, [new PortalSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Run(LongEnough);

        List<SimEvent> arrivals =
            sink.Events.Where(e => e.Kind == "creature_entered").ToList();

        Assert.NotEmpty(arrivals);
        Assert.Contains("capacity=20", arrivals[0].Detail);

        // And the broken one really is broken: every arrival came out of the
        // working portal's hole.
        int workingHole = (6 * world.Width) + 6;
        Assert.All(arrivals, e => Assert.Contains($"portal_tile={workingHole}", e.Detail));
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
