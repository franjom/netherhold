// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The portal: the first room whose behaviour is implemented, and the
/// first thing in the simulation that makes a creature that is not an imp. A
/// portal has four gates, each easy to implement so loosely that it never closes,
/// so every gate gets both a test that it stops a creature and one that lifting it
/// lets the creature through — a suite that only checked "a goblin eventually
/// arrives" would pass against a portal that ignored the pool, the cap and the
/// lair. <see cref="PortalRules"/> is tested here on the transcribed fixture and
/// <c>DK2.Behavior.Tests.PortalOnRealDataTests</c> re-runs the same questions
/// against the shipped install, so the fixture cannot drift.</summary>
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

    /// <summary>The owner's two timing runs, as data: base 25, gaps in seconds,
    /// measured by hand off a claimed portal on 2026-09-07 (Q86,
    /// observation-checklist.md). Tolerance is 4 seconds a gap because these were
    /// read off a wall clock ("not completely precise", "I am still measuring by
    /// hand"); the test is for the shape — the old formula misses run 1's third
    /// gap by 7 seconds and its sixth by 11.</summary>
    /// <remarks>Run 1's fourth gap is 40 seconds against a predicted 34 and is
    /// named in the data rather than absorbed by a wider tolerance: every gap
    /// after it is back inside tolerance, so the cumulative error steps once and
    /// stays flat, which is what a one-off block looks like — the portal came due,
    /// nothing was eligible, it waited, and the clock was stamped at the actual
    /// arrival. HYPOTHESIS: one mistimed reading explains it equally well.</remarks>
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

    /// <summary>A freshly claimed portal runs at full speed however worn out its
    /// neighbour is — the tally is per room, not keeper-wide. CONFIRMED (owner,
    /// Q86): a second portal claimed after the first had produced five creatures
    /// started its own series again, with measured gaps of 27, 30, 31, 33 seconds.
    /// A per-room tally predicts 25, 26, 29, 34 (mean absolute error 2.3 s); a
    /// keeper-wide one predicts 50, 61, 74, 89 (38.3 s).</summary>
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

    /// <summary>A rival taking a worn-out portal gets it at the base interval: the
    /// tally and the clock both go back to zero. CONFIRMED both ways, 2026-09-07
    /// (Q86) — the owner, unprompted: "if an enemy keeper takes one of the portals
    /// from me [...] then yes, it resets to zero for that portal and that keeper";
    /// and in the binary, DK2's room-ownership-change routine fires the room type's
    /// own handler after re-linking the room to its new owner, and for both portal
    /// types that handler stamps the clock and zeros the tally.</summary>
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
            TileWorkResult result = RoomHealthRules.Convert(
                world, db, new TilePos(1, 1), Rival);
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

    /// <summary>Both portal types raise the creature cap, and neither the count
    /// nor the rate is ours. STRONGLY INFERRED from the binary (2026-09-07): DK2's
    /// capacity routine walks the player's room list twice — once counting room
    /// type 3 (Portal), once type <c>0x19</c> (Hero Portal) — then returns
    /// <c>first + (total - 1) * perAdditional</c>.</summary>
    /// <remarks>A raw scan for the second variable's global finds one hit, the
    /// loader's setter — the signature this project has used fourteen times to
    /// call a variable cut. It is live anyway: its byte sits immediately after the
    /// first variable's and the compiler loads a <i>word</i> and takes the high
    /// half, so the second address never appears in any instruction (ADR-0085's
    /// rule: record "no reader found", and say what was scanned).</remarks>
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
    private static GameDatabase DatabaseWithGoblinFlags(CreatureFlags flags) =>
        DatabaseWithGoblin(goblin => goblin with { Flags = flags });

    /// <summary>The fixture Goblin wanting a Lair of at least one tile: a size the shipped
    /// Goblin does not ask, so its portal waits for the Lair (0x004DC86B).</summary>
    private static GameDatabase DatabaseWithSizedLair() =>
        DatabaseWithGoblin(goblin => goblin with
        {
            Attractions = [new CreatureAttraction(1, (byte)RoomTypeId.Lair, 1), new CreatureAttraction(0, 0, 0)],
        });

    private static GameDatabase DatabaseWithGoblin(Func<CreatureDef, CreatureDef> change)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        CreatureDef goblin = change(fixture.CreatureById(GoblinId));

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

    /// <summary>The <b>room type</b>, not the owner, picks the creature flag, so
    /// the two portals admit disjoint rosters. CONFIRMED (owner, 2026-09-07):
    /// "hero portal is not something a keeper can use, nor can an evil keeper
    /// summon good creatures", and in play (Q211) a keeper who claims one on
    /// Frosty's Castle receives the mercenary roster as its own units — under the
    /// intuitive owner-gated reading that portal would have produced nothing. In
    /// the binary the candidate builder requires <c>def+0x686 &amp; 0x400</c> when
    /// <c>room+0x18 == 3</c> and <c>def+0x68a &amp; 8</c> when
    /// <c>room+0x18 == 0x19</c>. The Hero Portal room def is synthesised from the
    /// Portal's because the fixture defines no room 25; the rule reads only the
    /// room id, so re-labelling exercises the branch under test, and
    /// <c>PortalOnRealDataTests</c> does the same against the shipped room.</summary>
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

    /// <summary>A portal too broken to produce anything still raises the creature
    /// cap: DK2's capacity routine walks the owner's room list and tests
    /// <c>room[0x18] == type</c> and nothing else — no size, no centre tile, no
    /// notion of the room working. The keeper here holds one working portal and
    /// one ring whose middle a rival has taken, and is capped at 20 rather than
    /// 15. The same rule makes claiming a dead Hero Portal raise a keeper's limit,
    /// which cannot be tested here for want of a room 25 in the fixture.</summary>
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

    /// <summary>Portal_CreateArrival asks for the creature definition's Portal Entrance effect at
    /// the spawn point before the Entrance action (<c>0x004DCB8F</c>; ADR-0476): the beams of white
    /// light from the hole the owner sees as a creature climbs out.</summary>
    [Fact]
    public void AnArrivalAsksForTheCreaturesEntranceEffectAtTheHole()
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        var level = new GlobalVariables();
        level.CreaturePool.Add(new CreaturePoolEntry(GoblinId, 15, Keeper));
        level.Availabilities.Add(new AvailabilityEntry(2, Keeper, GoblinId, 3));
        GameDatabase db = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values,
            fixture.Creatures.Values.Select(c => c.CreatureId == GoblinId ? c with { EntranceEffectId = 181 } : c),
            fixture.Variables).ForLevel(level);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        GameSimulation sim = Simulation(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Run(LongEnough);

        SimEvent arrival = sink.Events.First(e => e.Kind == "creature_entered");
        SimEvent request = Assert.Single(sink.Events, e => e.EffectRequest.HasValue && e.Tick == arrival.Tick);
        ViewEffectRequest effect = request.EffectRequest!.Value;
        Assert.Equal(new EffectId(181), effect.EffectId);
        Assert.Equal(Keeper, effect.Owner.Value);
        int hole = (6 * world.Width) + 6;
        Assert.Contains($"portal_tile={hole}", arrival.Detail);
        Assert.Equal(new FpPosition3(new Fp32(6 * Fp32.One + Fp32.One / 2), new Fp32(6 * Fp32.One + Fp32.One / 2),
            new Fp32(0)), effect.Position);
        Assert.Equal(default, effect.Heading);
    }

    [Fact]
    public void AnArrivalOfACreatureWithNoEntranceEffectAsksForNothing()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
        GameSimulation sim = Simulation(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Run(LongEnough);
        Assert.Contains(sink.Events, e => e.Kind == "creature_entered");
        Assert.DoesNotContain(sink.Events, e => e.EffectRequest.HasValue);
    }

    // ---- gate 1: attraction ----------------------------------------------

    /// <summary>The Goblin wants a Lair of size 0, and 0x004DC810 refuses only a total below
    /// the size, so no Lair at all still passes; the Lair's weight is flat (0x004DC153).
    /// DK2's Level 4 keeper got its first Goblin at 887 with only a Portal and a Heart.</summary>
    [Fact]
    public void APortalWithNoLairStillLetsAGoblinThrough()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);

        Simulation(db, world).Run(LongEnough);

        Assert.NotEqual(0, Goblins(world));
    }

    [Fact]
    public void ACreatureWhoseRoomHasASizeProducesNothingWithoutIt()
    {
        GameDatabase db = DatabaseWithSizedLair();
        WorldState world = World(db);
        Portal(db, world, 5, 5);

        Simulation(db, world).Run(LongEnough);

        Assert.Empty(world.Creatures);
    }

    /// <summary>The wait is not banked: 0x004DBBE0 restamps the clock at every due roll,
    /// whatever the roll produced (ADR-0314). This replaced a test that pinned the opposite,
    /// which was a design choice nothing in the binary supported.</summary>
    [Fact]
    public void BuildingTheLairWaitsForTheNextRoll()
    {
        GameDatabase db = DatabaseWithSizedLair();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        GameSimulation sim = Simulation(db, world);
        int interval = 25 * SimulationTiming.TicksPerSecond;

        // Two and a half intervals with nothing to attract anything: two empty rolls,
        // each restarting the clock, which now stands half an interval in.
        sim.Run((interval * 5) / 2);
        Assert.Empty(world.Creatures);
        Assert.Equal(interval / 2, world.Tile(6, 6).EntranceIdleTicks);

        // The lair does not let a goblin out at once; the next roll does.
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
        sim.Run((interval / 2) - 1);
        Assert.Equal(0, Goblins(world));
        sim.Run(1);
        Assert.Equal(1, Goblins(world));
    }

    // ---- PortalsOpen (action 37) -------------------------------------------

    /// <summary>The negative control: the same portal, open, produces on its first roll.</summary>
    [Fact]
    public void AnOpenPortalProducesOnItsFirstRoll()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(25 * SimulationTiming.TicksPerSecond);

        Assert.True(PortalRules.PortalsOpen(world, Keeper));
        Assert.Equal(1, Goblins(world));
        Assert.Equal(1, world.Tile(6, 6).EntranceCreaturesGenerated);
    }

    /// <summary>Level 2's shape: closed over a due roll, the roll is spent — the clock
    /// restarts, the tally holds, no creature — and reopening does not hand the roll back.
    /// The next creature comes one whole interval later.</summary>
    [Fact]
    public void AClosedPortalSpendsItsRollAndProducesNothing()
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
        world.Player(Keeper).PortalsClosed = true;
        GameSimulation sim = Simulation(db, world);
        int interval = 25 * SimulationTiming.TicksPerSecond;

        sim.Run(interval);
        Assert.Equal(0, Goblins(world));
        Assert.Equal(0, world.Tile(6, 6).EntranceIdleTicks);
        Assert.Equal(0, world.Tile(6, 6).EntranceCreaturesGenerated);

        world.Player(Keeper).PortalsClosed = false;
        sim.Run(interval - 1);
        Assert.Equal(0, Goblins(world));
        sim.Run(1);
        Assert.Equal(1, Goblins(world));
    }

    /// <summary>Action 37 (<c>0x004C892D</c>): byte 0 zero closes, anything else opens, and
    /// only for the player whose tree ran it. Level 2 ships both values.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void ActionThirtySevenWritesPortalsOpen(byte available, bool closed)
    {
        GameDatabase db = Database();
        WorldState world = World(db);
        world.Player(Keeper).PortalsClosed = !closed;
        TriggerRecord[] records =
        [
            new ActionTriggerRecord(new byte[16])
            {
                Body = new byte[8], TriggerId = 0xFFFF, Type = 37,
                Parameters = new Dictionary<string, long> { ["Available"] = available },
            },
        ];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, 0xFFFF)]);

        TriggerRunResult result = new TriggerExecutor(tree, world.Triggers, world, db).Run(10);

        Assert.Empty(result.Partial);
        Assert.Equal(closed, world.Player(Keeper).PortalsClosed);
        Assert.False(world.Player(Rival).PortalsClosed);
    }

    /// <summary>A closed portal still makes its draw, as DK2's pick runs before the creator
    /// refuses: the RNG and the boosts move exactly as an open roll's would.</summary>
    [Fact]
    public void AClosedRollDrawsAsAnOpenOneDoes()
    {
        (ulong, ulong, ulong, ulong) Draws(bool closed)
        {
            GameDatabase db = Database();
            WorldState world = World(db);
            Portal(db, world, 5, 5);
            Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
            world.Player(Keeper).PortalsClosed = closed;
            var sim = new GameSimulation(db, world, seed: 99, [new PortalSystem()]);
            sim.Run(25 * SimulationTiming.TicksPerSecond);
            return sim.Rng.SaveState();
        }

        Assert.Equal(Draws(closed: false), Draws(closed: true));
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
        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(world, db);

        Assert.False(PortalRules.IsAttracted(demanding, rooms));
        Assert.True(PortalRules.IsAttracted(goblin, rooms));
    }

    [Fact]
    public void ACreaturesMinimumIsTheKeepersTotalSlabsOfThatRoomType()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var demanding = db.CreatureById(GoblinId) with
        {
            Attractions = [new CreatureAttraction(1, (byte)RoomTypeId.Lair, 9)],
        };

        WorldState world = World(db);

        // Two separate 2x2 lairs: eight tiles between them, short of nine.
        Paint(db, world, RoomTypeId.Lair, 5, 5, 2, 2);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 2, 2);
        Assert.False(PortalRules.IsAttracted(demanding, RoomIndex.Build(world, db)));

        // A third 2x2 takes the total to twelve. No single room is nine tiles
        // and the creature comes anyway, because 0x004C6EB0 sums the room list
        // rather than measuring one room. This test asserted the opposite until
        // the binary was read: the old reading was OpenKeeper's.
        Paint(db, world, RoomTypeId.Lair, 5, 9, 2, 2);
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
                new CreatureAttraction(1, (byte)RoomTypeId.Treasury, 1),
            ],
        };

        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);
        Assert.False(PortalRules.IsAttracted(fussy, RoomIndex.Build(world, db)));

        Paint(db, world, RoomTypeId.Treasury, 9, 7, 1, 1);
        Assert.True(PortalRules.IsAttracted(fussy, RoomIndex.Build(world, db)));
    }

    [Fact]
    public void ALairAttractedTypeIsNeverCappedHoweverManyAreHeld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        CreatureDef goblin = db.CreatureById(GoblinId);
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        // The Lair arm skips the draw-down, so holding twenty changes nothing.
        for (int i = 0; i < 20; i++)
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(9, 5));

        Assert.True(AttractionBudget.Admits(world, goblin, RoomIndex.Build(world, db), Keeper));
    }

    [Fact]
    public void ATempleAdmitsTwoOfItsTypeAndThenStops()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var angel = db.CreatureById(GoblinId) with
        {
            Attractions = [new CreatureAttraction(1, (byte)RoomTypeId.Temple, 0)],
        };

        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Temple, 5, 5, 5, 5);
        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(world, db);
        RoomInstance temple = rooms.Single(r => r.Definition.TypeId == RoomTypeId.Temple);

        // No Hand, no contribution: this is the owner's 3x5 temple.
        Assert.Equal(0, AttractionBudget.Contribution(world, temple));
        Assert.False(AttractionBudget.Admits(world, angel, rooms, Keeper));

        world.SpawnObject(db, AutoFurnitureRules.TempleHandObjectId, Keeper, temple.TileIndices[0])
            .AutoPlaced = true;
        Assert.Equal(2, AttractionBudget.Contribution(world, temple));

        // Two arrive, and the third is refused: the owner watched exactly this
        // happen on 2026-09-11, and the 2 was read off the binary four days before.
        Assert.True(AttractionBudget.Admits(world, angel, rooms, Keeper));
        world.SpawnCreature(db, GoblinId, Keeper, new TilePos(5, 5));
        Assert.True(AttractionBudget.Admits(world, angel, rooms, Keeper));
        world.SpawnCreature(db, GoblinId, Keeper, new TilePos(5, 5));
        Assert.False(AttractionBudget.Admits(world, angel, rooms, Keeper));
    }

    [Fact]
    public void APrisonOrPitContributesItsInteriorCellsAndACapacityRoomItsFurniture()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.Prison, 5, 5, 3, 3);
        Paint(db, world, RoomTypeId.CombatPit, 9, 1, 5, 4);
        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(world, db);
        RoomInstance prison = rooms.Single(r => r.Definition.TypeId == RoomTypeId.Prison);
        RoomInstance pit = rooms.Single(r => r.Definition.TypeId == RoomTypeId.CombatPit);

        // 0x00454110 tests the cell's interior byte (+0x0C == 0xFF) before its type
        // switch, so arm 9's unconditional true only ever sees interior cells: a 3x3
        // prison counts its one middle cell, a 5x4 pit its 3x2 middle, never the rim.
        Assert.Equal(1, AttractionBudget.Contribution(world, prison));
        Assert.Equal(6, AttractionBudget.Contribution(world, pit));

        Paint(db, world, RoomTypeId.Library, 12, 5, 3, 3);
        rooms = RoomIndex.Build(world, db);
        RoomInstance library = rooms.Single(r => r.Definition.TypeId == RoomTypeId.Library);
        AutoFurnitureRules.TryTotalCapacity(world, library, out int capacity);
        Assert.Equal(capacity, AttractionBudget.Contribution(world, library));
    }

    /// <summary>Level 4's captured Guard Rooms drew DK2's eleven Dark Elves where ours drew none:
    /// <c>0x004E3ED0</c>'s jump table sends the Guard Room to its default arm, the slab count
    /// <c>+0xA7</c>, which <c>0x004DCE70</c> returns for it (ADR-0440).</summary>
    [Fact]
    public void AGuardRoomContributesItsTilesAndAdmitsThatManyOfItsType()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var elf = db.CreatureById(GoblinId) with
        {
            Attractions = [new CreatureAttraction(1, (byte)RoomTypeId.GuardRoom, 0)],
        };
        WorldState world = World(db);
        Paint(db, world, RoomTypeId.GuardRoom, 5, 5, 3, 3);
        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(world, db);
        RoomInstance guard = rooms.Single(r => r.Definition.TypeId == RoomTypeId.GuardRoom);

        Assert.Equal(9, AttractionBudget.Contribution(world, guard));
        for (int i = 0; i < 9; i++)
        {
            Assert.True(AttractionBudget.Admits(world, elf, rooms, Keeper));
            world.SpawnCreature(db, GoblinId, Keeper, new TilePos(5, 5));
        }
        Assert.False(AttractionBudget.Admits(world, elf, rooms, Keeper));
    }

    /// <summary>The arms that store something other than the slab count stay unread: the
    /// Portal's 0, the Prison's 0xFFFF and the Graveyard's floor slots (ADR-0440).</summary>
    [Theory]
    [InlineData(RoomTypeId.Hatchery, true)]
    [InlineData(RoomTypeId.Treasury, true)]
    [InlineData(RoomTypeId.Lair, true)]
    [InlineData(RoomTypeId.Prison, false)]
    [InlineData(RoomTypeId.Graveyard, false)]
    [InlineData(RoomTypeId.Portal, false)]
    public void TheDefaultCapacityArmIsTheTileCount_AndPortalPrisonGraveyardStayUnmodelled(RoomTypeId type, bool read)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Paint(db, world, type, 5, 5, 3, 3);
        RoomInstance room = RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == type);

        Assert.Equal(read, AutoFurnitureRules.TryTotalCapacity(world, room, out int capacity));
        Assert.Equal(read ? 9 : 0, capacity);
    }

    [Fact]
    public void ARivalsLairDoesNotAttractAnythingToYourPortal()
    {
        GameDatabase db = DatabaseWithSizedLair();
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1, owner: Rival);

        Simulation(db, world).Run(LongEnough);

        Assert.Empty(world.Creatures);
    }

    // ---- not a gate: availability ----------------------------------------

    [Fact]
    public void ACreatureMadeUnavailableStillComesThroughThePortal()
    {
        // UNAVAILABLE (1) rather than AVAILABLE (3). The pick 0x004DBF90 never reads the
        // creature table: Level 3 makes its Firefly, Goblin and Warlock unavailable at the
        // start and its portal produced all three in the recording (ADR-0316). The pool is
        // what keeps a level's roster closed.
        GameDatabase db = Database(availability: 1);
        WorldState world = World(db);
        Portal(db, world, 5, 5);
        Paint(db, world, RoomTypeId.Lair, 9, 5, 1, 1);

        Simulation(db, world).Run(LongEnough);

        Assert.NotEmpty(world.Creatures);
        Assert.All(world.Creatures, c => Assert.Equal(GoblinId, c.CreatureTypeId));
    }

    [Fact]
    public void AvailabilityIsPerPlayerAndNotPerLevel()
    {
        // The table still exists per player, for the creature panel and the scripts that
        // write it; the portal just does not consult it.
        GameDatabase db = Database();

        Assert.True(PortalRules.IsUnlocked(db, Keeper, GoblinId));
        Assert.False(PortalRules.IsUnlocked(db, Rival, GoblinId));
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

        Assert.Equal(["Portal", "Hero Portal"], threeByThree.Select(r => r.Name).ToArray());
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

        // Short of the first roll, which would restart the clock (ADR-0314).
        Simulation(db, world).Run(50);

        Assert.Equal(50, world.Tile(6, 6).EntranceIdleTicks);
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
