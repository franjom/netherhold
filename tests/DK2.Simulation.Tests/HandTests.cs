// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>
/// The Hand of Evil — picking up, dropping, slapping — and Efficiency, which is
/// what the hand is for (ADR-0062, docs/research/hand-of-evil.md).
///
/// <para>Two of these assertions came from the original rather than from a
/// file, and they are the ones worth keeping honest: an imp mines a gold seam
/// for <b>273</b> gold a swing, <b>309-310</b> once it has been slapped or
/// aimed, and <b>341</b> with both — the owner read all three off DK2's status
/// bar and its gold counter on 2026-09-05, and they are the whole reason
/// Efficiency exists in this project.</para>
/// </summary>
public class HandTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte GoblinId = 7;
    private const int Second = SimulationTiming.TicksPerSecond;

    private static readonly TilePos Floor = new(2, 2);

    /// <summary>
    /// A tile of Rock the keeper's room touches, and the floor tile beside it.
    ///
    /// <para>The fixture map is a 3x3 claimed room in the corner and solid rock
    /// everywhere else, so the gold seam at (5,5) has no walkable neighbour to
    /// stand a worker on. Marked work has to be on the room's edge for a drop
    /// to be able to aim at it, which is also how it works in the game.</para>
    /// </summary>
    private static readonly TilePos Marked = new(4, 2);
    private static readonly TilePos Beside = new(3, 2);

    /// <summary>The fixture's gold seam: 660 health and 3000 gold, which is the
    /// seam the owner measured.</summary>
    private static readonly TilePos Seam = new(5, 5);

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db) => WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

    private static GameSimulation Simulation(
        GameDatabase db, WorldState world, ListEventSink? sink = null)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(),
            new TileTagSystem(),
            new CreatureMovementSystem(),
            new ImpTaskSystem(),
            new TileWorkSystem(),
            new CreatureAngerSystem(),
        ]);
        if (sink is not null) sim.ObserveWith(sink);
        return sim;
    }

    private static List<HandItem> HandOf(WorldState world) => world.Player(Keeper).Hand;

    /// <summary>
    /// Just the ids, for the assertions that predate the hand holding anything
    /// but creatures (ADR-0075).
    /// </summary>
    private static List<int> HandIdsOf(WorldState world) =>
        [.. HandOf(world).Select(h => h.Id)];

    // ---- picking up -------------------------------------------------------

    [Fact]
    public void AnImpPickedUpLeavesTheDungeonWithoutLeavingTheLevel()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));

        Assert.True(imp.InHand);
        Assert.Equal([imp.Id], HandIdsOf(world));

        // Still a creature in the world's list — the hand is a place, not a
        // deletion, which is what lets the drop put it back.
        Assert.Contains(imp, world.Creatures);
    }

    [Fact]
    public void AHeldImpDoesNotMoveAnInchHoweverLongItIsHeld()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, Marked)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));

        (int x, int y) = (imp.PosX, imp.PosY);
        for (int i = 0; i < 60 * Second; i++) sim.Tick();

        // There is marked rock two tiles away and the imp has had a minute.
        Assert.Equal(x, imp.PosX);
        Assert.Equal(y, imp.PosY);
        Assert.Equal(CreatureActivity.Idle, imp.Activity);
    }

    [Fact]
    public void AKeeperCannotPickUpSomebodyElsesCreature()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState theirs = world.SpawnCreature(db, ImpId, PlayerIds.Neutral, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, theirs.Id)));

        Assert.False(theirs.InHand);
        Assert.Empty(HandOf(world));
    }

    [Fact]
    public void TheSameCreatureCannotBePickedUpTwice()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));

        Assert.Equal([imp.Id], HandIdsOf(world));
    }

    [Fact]
    public void TheHandHoldsMaxNumberOfThingsInHandAndRefusesTheSixtyFifth()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        int capacity = HandRules.Capacity(db);
        Assert.Equal(64, capacity);

        var imps = new List<CreatureState>();
        for (int i = 0; i <= capacity; i++)
            imps.Add(world.SpawnCreature(db, ImpId, Keeper, Floor));

        GameSimulation sim = Simulation(db, world);
        foreach (CreatureState imp in imps)
            sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));

        Assert.Equal(capacity, HandOf(world).Count);
        Assert.False(imps[^1].InHand);
    }

    // ---- dropping ---------------------------------------------------------

    [Fact]
    public void TheHandPutsDownWhatItPickedUpLast()
    {
        // DK2's drop handler reads back the slot it is about to free, so the
        // hand is a stack. A keeper holding A then B drops B first.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState first = world.SpawnCreature(db, ImpId, Keeper, Floor);
        CreatureState second = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, first.Id)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, second.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(3, 3))));

        Assert.False(second.InHand);
        Assert.True(first.InHand);
        Assert.Equal([first.Id], HandIdsOf(world));
    }

    [Fact]
    public void ADroppedCreatureStandsWhereItWasPutAndNotWhereItWasTaken()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(1, 3))));

        Assert.False(imp.InHand);
        Assert.Equal(new TilePos(1, 3), imp.Tile);
        Assert.Empty(HandOf(world));
    }

    [Fact]
    public void ACreatureCannotBeDroppedIntoRock()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(8, 8))));

        // Refused, and the imp is still in the hand rather than lost in a wall.
        Assert.True(imp.InHand);
        Assert.Equal([imp.Id], HandIdsOf(world));
    }

    // ---- Efficiency: the three numbers the owner read ---------------------

    [Fact]
    public void AnUnhandledImpWorksAtEightyPercent()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);

        Assert.Equal(80, EfficiencyRules.Percent(imp, db.CreatureById(ImpId)));
    }

    [Fact]
    public void TheOwnersStatusBar_AnImpMinesSixtyHealthAtEightyPercentAndSeventyFiveAtAHundred()
    {
        // OWNER MEASUREMENT (2026-09-05), read off DK2's own gold counter:
        //
        //   272-273 gold a swing at 80%, 309-310 at 90%, 341 at 100%
        //
        // A gold seam is 3000 gold over 660 health, so a swing is
        // health x 3000/660 = health x 4.5454 gold. MineGoldHealth is -300 a
        // second, over the four ticks of a game second.
        int atEighty = EfficiencyRules.Scale(-60, 80);
        int atNinety = EfficiencyRules.Scale(-60, 90);
        int atHundred = EfficiencyRules.Scale(-60, 100);

        Assert.Equal(-60, atEighty);    // 272.7 gold — owner read 272-273
        Assert.Equal(-68, atNinety);    // 309.1 gold — owner read 309-310
        Assert.Equal(-75, atHundred);   // 340.9 gold — owner read 341

        // The 90% case is the one that pins the rounding: 67.5 is a half, and
        // 67 would have read 304.5 gold. It reads 309.
        Assert.Equal(309, (int)(atNinety * -3000L / 660));
    }

    [Fact]
    public void AtTheDefaultRateScalingChangesNothingAtAll()
    {
        // Which is why the whole suite survived Efficiency arriving: every
        // number this project measured before it existed was an 80% number.
        for (int amount = -5000; amount <= 5000; amount += 7)
            Assert.Equal(amount, EfficiencyRules.Scale(amount, EfficiencyRules.BasePercent));
    }

    [Fact]
    public void ASlapRaisesEfficiencyToNinetyAndTenSlapsDoNotRaiseItFurther()
    {
        // CONFIRMED by the owner, who tried: "it raises from 273 to 310 after
        // the first slap and stays there no matter how many times i slap".
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);
        CreatureDef definition = db.CreatureById(ImpId);

        for (int i = 0; i < 10; i++)
        {
            sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, imp.Id)));
            for (int t = 0; t < Second; t++) sim.Tick();
        }

        Assert.Equal(90, EfficiencyRules.Percent(imp, definition));
    }

    [Fact]
    public void TheSlapBonusRunsOutAfterTwentyFiveGameSecondsAndNotSooner()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);
        CreatureDef definition = db.CreatureById(ImpId);

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, imp.Id)));

        for (int i = 0; i < EfficiencyRules.SlapBonusTicks - 1; i++) sim.Tick();
        Assert.Equal(90, EfficiencyRules.Percent(imp, definition));

        sim.Tick();
        Assert.Equal(80, EfficiencyRules.Percent(imp, definition));
    }

    [Fact]
    public void EverySlapCountsHoweverFastTheyComeAndThereIsNoCooldown()
    {
        // A first draft of this mechanic had a one-second cooldown on the whole
        // slap, read out of the original's `now < LastSlapTick + var19` test.
        // That test guards ONE thing - the knock-back impulse - and the anger,
        // the damage and the raised Efficiency all land regardless. So a keeper
        // who can click fast angers a creature as fast as they can click, and
        // the six countable slaps of Q114 were six because a hand is slow.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        int slap = db.CreatureById(GoblinId).AngerSlap;
        Assert.Equal(3000, slap);

        // Five slaps on five consecutive ticks - a quarter of a game second, so
        // the once-a-second lairless ladder has not contributed anything yet.
        for (int i = 0; i < 5; i++)
            sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, goblin.Id)));

        Assert.Equal(5 * slap, goblin.Anger);
    }

    [Fact]
    public void SixSlapsMakeAGoblinSulkAndElevenMakeItLeave()
    {
        // The owner's Salamander, whose AngerSlap is also 3000 (Q114): exactly
        // six slaps to unhappy, then five more to angry.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        CreatureDef definition = db.CreatureById(GoblinId);
        GameSimulation sim = Simulation(db, world);

        var moods = new List<CreatureMood>();
        for (int i = 0; i < 11; i++)
        {
            sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, goblin.Id)));
            moods.Add(CreatureAngerRules.MoodOf(definition, goblin));
        }

        Assert.Equal(CreatureMood.Content, moods[4]);
        Assert.Equal(CreatureMood.Unhappy, moods[5]);
        Assert.Equal(CreatureMood.Unhappy, moods[9]);
        Assert.Equal(CreatureMood.Angry, moods[10]);
    }

    [Fact]
    public void FiveSlapsKillAnImp()
    {
        // The Imp's slapDamage is 100 against 500 health — a piece of DK2 this
        // project's fixture had transcribed as 0 and nothing had read until the
        // hand arrived.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        Assert.Equal(500, imp.Health);
        Assert.Equal(100, db.CreatureById(ImpId).SlapDamage);

        for (int i = 0; i < 5; i++)
        {
            sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, imp.Id)));
            for (int t = 0; t < Second; t++) sim.Tick();
        }

        Assert.Equal(0, imp.Health);
    }

    // ---- the keeper's twelve and a half percent ---------------------------

    [Fact]
    public void AnImpDroppedNextToMarkedWorkWorksTwelveAndAHalfPercentFaster()
    {
        // CONFIRMED by the owner (2026-09-05): "after I mark a tile for
        // excavation and drop an imp adjacent to it its efficiency jumps to
        // 90%."
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, Marked)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Beside)));

        // The drop only buys the look; the look happens in the same tick,
        // because the job system runs after the hand and the timer was zeroed
        // on the way down (ADR-0069). So the ticket is already spent.
        Assert.False(imp.JustDropped);
        Assert.Equal(ImpTaskKind.Excavate, imp.DroppedTask);
        Assert.Equal(90, EfficiencyRules.Percent(imp, db.CreatureById(ImpId)));
    }

    [Theory]
    [InlineData(1, 90)]
    [InlineData(2, 90)]
    [InlineData(3, 90)]
    [InlineData(4, 80)]
    public void TheDropLooksThreeTilesAndNotAFourth(int gap, int expected)
    {
        // CONFIRMED by the owner off the status bar (2026-09-06), counting
        // marked tiles up a corridor: three tiles boost, the fourth does not.
        //
        // DK2's own scan is nominally TWO tiles - it is handed 0x2000 against a
        // 0x1000 tile - and reaches three because its loop tests the bound
        // after scanning a ring and with a strict greater-than. An off-by-one
        // in the original is a rule of the original (ADR-0002), so three is
        // what is built and this is the test that pins it.
        GameDatabase db = Db;
        WorldState world = World(db);

        // The whole interior paved and claimed, so that nothing else - no wall
        // to reinforce, no floor to claim - is in range to muddy the reading.
        // Then one block of rock, marked, at the distance under test.
        Pave(world, 1, 1, 14, 10);
        var mark = new TilePos(4 + gap, 5);
        ref TileState wall = ref world.Tile(mark.X, mark.Y);
        wall.TerrainId = Dk2TerrainFixture.Rock;
        wall.OwnerPlayerId = PlayerIds.Neutral;

        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, mark)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(4, 5))));

        Assert.Equal(expected, EfficiencyRules.Percent(imp, db.CreatureById(ImpId)));
        Assert.Equal(
            expected == 90 ? ImpTaskKind.Excavate : ImpTaskKind.None, imp.DroppedTask);
    }

    [Fact]
    public void AnImpDroppedOnEmptyGroundGetsNothingForIt()
    {
        // The other half of the same observation, and the half that makes it a
        // rule rather than a coincidence: "but if I drop it on a random tile
        // where there is nothing marked by me its efficiency remains at 80%."
        //
        // "Empty" has to mean empty of every kind the drop scan tries, not just
        // of marks - reinforcing is one of the nine, so a drop three tiles from
        // any of the keeper's own rock would pay after all. Hence the wide
        // claimed floor: this is a worker put down in the middle of a finished
        // dungeon, which is what the owner's "random tile" was.
        GameDatabase db = Db;
        WorldState world = World(db);
        Pave(world, 1, 1, 9, 9);

        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(5, 5))));
        sim.Tick();

        Assert.Equal(ImpTaskKind.None, imp.DroppedTask);
        Assert.Equal(80, EfficiencyRules.Percent(imp, db.CreatureById(ImpId)));
    }

    /// <summary>Paves w-by-h of the keeper's own claimed floor at (x,y).</summary>
    private static void Pave(WorldState world, int x, int y, int w, int h)
    {
        for (int j = y; j < y + h; j++)
        {
            for (int i = x; i < x + w; i++)
            {
                ref TileState tile = ref world.Tile(i, j);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }
        }
    }

    [Fact]
    public void ASlappedImpDroppedOntoItsWorkReachesAHundredPercent()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, Marked)));
        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Beside)));
        sim.Tick();

        Assert.Equal(100, EfficiencyRules.Percent(imp, db.CreatureById(ImpId)));
    }

    [Fact]
    public void TheAimingBonusSurvivesTheWalkAndTheDigging()
    {
        // CONFIRMED by the owner (2026-09-06): "it stays at 90% until it gets
        // to that marked tile and starts excavating, it doesn't revert to 80%."
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, Marked)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Beside)));
        sim.Tick();

        Assert.Equal(ImpTaskKind.Excavate, imp.DroppedTask);

        // Every tick from here to the moment the wall falls, at ninety.
        for (int i = 0; i < 60 * Second; i++)
        {
            sim.Tick();
            if (world.Tile(Marked.X, Marked.Y).TerrainId != Dk2TerrainFixture.Rock) break;
            Assert.Equal(90, EfficiencyRules.Percent(imp, db.CreatureById(ImpId)));
        }

        Assert.NotEqual(Dk2TerrainFixture.Rock, world.Tile(Marked.X, Marked.Y).TerrainId);
    }

    [Fact]
    public void TheAimingBonusCarriesOnToTheNextTileOfTheSameKind()
    {
        // The half the owner has not watched and the binary is plain about: the
        // bonus is not spent by finishing the tile. DK2 keeps DroppedTask
        // through a job ending and only clears it when a later evaluation - one
        // where the worker is still an excavator - cannot find another
        // excavation anywhere. So a keeper who drops an imp on a marked seam
        // pays for the whole seam, not the first block of it (ADR-0069, Q177).
        GameDatabase db = Db;
        WorldState world = World(db);
        Pave(world, 1, 1, 14, 10);

        // Two marked blocks, one in range of the drop and one far away.
        var near = new TilePos(6, 5);
        var far = new TilePos(13, 9);
        foreach (TilePos at in new[] { near, far })
        {
            ref TileState rock = ref world.Tile(at.X, at.Y);
            rock.TerrainId = Dk2TerrainFixture.Rock;
            rock.OwnerPlayerId = PlayerIds.Neutral;
        }

        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, near, far)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(4, 5))));

        Assert.Equal(ImpTaskKind.Excavate, imp.DroppedTask);

        // The near block falls; the far one - never in range of the drop - is
        // what the worker walks to next, and it walks there still at 90%.
        for (int i = 0; i < 240 * Second; i++)
        {
            sim.Tick();
            if (world.Tile(near.X, near.Y).TerrainId != Dk2TerrainFixture.Rock) break;
        }

        Assert.NotEqual(Dk2TerrainFixture.Rock, world.Tile(near.X, near.Y).TerrainId);

        // Past the abandon and the re-evaluation.
        for (int i = 0; i < 4 * Second; i++) sim.Tick();

        Assert.Equal(ImpTaskKind.Excavate, imp.DroppedTask);
        Assert.Equal(90, EfficiencyRules.Percent(imp, db.CreatureById(ImpId)));
    }

    [Fact]
    public void TheAimingBonusIsSpentWhenThereIsNoMoreWorkOfThatKind()
    {
        // DK2 clears DroppedTask in exactly one place: a later evaluation where
        // the worker is still on the kind it was aimed at and no more of that
        // kind can be found anywhere on the map. So the bonus is not "worth one
        // job" - which is what this project built for a day - it is worth the
        // rest of that kind of work (ADR-0069).
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, Marked)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Beside)));
        sim.Tick();

        Assert.Equal(ImpTaskKind.Excavate, imp.DroppedTask);

        // The one marked tile falls, there is no other, and the next evaluation
        // that still calls itself an excavator finds nothing to excavate.
        for (int i = 0; i < 240 * Second && imp.DroppedTask != ImpTaskKind.None; i++)
            sim.Tick();

        Assert.Equal(ImpTaskKind.None, imp.DroppedTask);
        Assert.Equal(80, EfficiencyRules.Percent(imp, db.CreatureById(ImpId)));
    }

    [Fact]
    public void TheOwnersGoldCounter_273Then309Then341PerSwing()
    {
        // THE OWNER MEASUREMENT, and the whole reason Efficiency exists here.
        // Read straight off DK2's gold counter on 2026-09-05, one imp alone on
        // a fresh map with the others in hand:
        //
        //   "it is 272-273, and 309-310 [slapped]"
        //   "after a slap it gathers 341"
        //
        // A seam is 660 health and 3000 gold, and MineGoldHealth is -300 a
        // second over four ticks a game second. If a refactor ever moves these
        // three numbers, this is the assertion that came from the game.
        // Ranges, because the owner read ranges - and the ranges are real
        // rather than sloppy: gold comes off a running total that truncates, so
        // consecutive swings at one rate genuinely alternate by a unit.
        Assert.InRange(FirstSwingGold(80), 272, 273);
        Assert.InRange(FirstSwingGold(90), 309, 310);
        Assert.InRange(FirstSwingGold(100), 340, 341);
    }

    [Fact]
    public void ASlapTakesASwingOffASeamAndTwoSwingsWithAWellAimedDrop()
    {
        Assert.Equal(11, SwingsThroughSeam(80));
        Assert.Equal(10, SwingsThroughSeam(90));
        Assert.Equal(9, SwingsThroughSeam(100));
    }

    private static int FirstSwingGold(int efficiencyPercent)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        ref TileState tile = ref world.Tile(Seam);
        tile.SetTaggedBy(Keeper, true);

        return TileWorkRules.Apply(
            db, ref tile, TileWorkKind.MineGold, Keeper, efficiencyPercent).GoldReleased;
    }

    private static int SwingsThroughSeam(int efficiencyPercent)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        ref TileState tile = ref world.Tile(Seam);
        tile.SetTaggedBy(Keeper, true);

        int swings = 0;
        int gold = 0;
        while (tile.TerrainId == Dk2TerrainFixture.Gold && swings < 100)
        {
            gold += TileWorkRules.Apply(
                db, ref tile, TileWorkKind.MineGold, Keeper, efficiencyPercent).GoldReleased;
            swings++;
        }

        // However fast it is mined, a seam is worth exactly its goldValue:
        // the running total truncates between swings and never short-changes
        // the last one (Q21, and the owner watched four consecutive decrements
        // to prove it).
        Assert.Equal(3000, gold);
        return swings;
    }

    [Fact]
    public void ASlappedImpDigsThroughAWallInFewerTicksThanAnUnslappedOne()
    {
        // The end-to-end version: the same imp, the same wall, one difference.
        Assert.True(TicksToDigOut(slapped: true) < TicksToDigOut(slapped: false));
    }

    private static int TicksToDigOut(bool slapped)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Beside);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, Marked)));
        if (slapped) sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, imp.Id)));

        int ticks = 0;
        while (world.Tile(Marked).TerrainId == Dk2TerrainFixture.Rock
               && ticks < 200 * Second)
        {
            sim.Tick();
            ticks++;
        }

        Assert.NotEqual(Dk2TerrainFixture.Rock, world.Tile(Marked).TerrainId);
        return ticks;
    }

    // ---- the hand and the mood --------------------------------------------

    [Fact]
    public void AGoblinHeldBrieflyIsNoneTheWiser()
    {
        // The 20-second grace period, which is why moving a creature across the
        // dungeon costs a keeper nothing.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.Anger = 10_000;
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        int before = goblin.Anger;

        for (int i = 0; i < HandRules.AngerGraceTicks; i++) sim.Tick();

        // The lairless ladder keeps running - a creature in the hand still
        // knows it has no bed - so what "none the wiser" means precisely is
        // that AngerInHand has not landed once. +100 a second and nothing else.
        Assert.Equal(before + (HandRules.AngerGraceSeconds * 100), goblin.Anger);
    }

    [Fact]
    public void AGoblinHeldPastTheGracePeriodCalmsDownByAngerInHandEachSecond()
    {
        // AngerInHand is -250 for a Goblin: the hand SOOTHES most creatures,
        // which is the opposite of what its name suggests and is why the field
        // had to be re-read as signed (ADR, creature-anger.md).
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.Anger = 20_000;
        GameSimulation sim = Simulation(db, world);

        Assert.Equal(-250, db.CreatureById(GoblinId).AngerInHand);

        // A second Goblin, left standing, so the lairless ladder that both of
        // them are climbing cancels out and only the hand's own delta is left.
        CreatureState standing = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(3, 3));
        standing.Anger = 20_000;

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        for (int i = 0; i < HandRules.AngerGraceTicks + (10 * Second); i++) sim.Tick();

        // Ten game seconds past the grace period is ten applications of -250,
        // and the held Goblin is exactly that much calmer than its twin.
        Assert.True(goblin.Anger < standing.Anger);
        Assert.Equal(10 * -250, goblin.Anger - standing.Anger);
    }

    [Fact]
    public void AnAngryCreatureInTheHandDecidesNothingUntilItLands()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        goblin.Anger = CreatureAngerRules.Ceiling;
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));
        for (int i = 0; i < 30 * Second; i++) sim.Tick();

        Assert.Equal(0, goblin.LastAngryChoiceTick);
        Assert.NotEqual(CreatureActivity.Leaving, goblin.Activity);
    }

    // ---- the round trip ---------------------------------------------------

    [Fact]
    public void AHandSurvivesASnapshot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(sim), db, SimulationSystems.Default());

        Assert.Equal([HandItem.Of(imp)], restored.World.Player(Keeper).Hand);

        CreatureState back = restored.World.CreatureById(imp.Id)!;
        Assert.True(back.InHand);
        Assert.Equal(imp.PickupTick, back.PickupTick);
        Assert.Equal(imp.LastSlapTick, back.LastSlapTick);
        Assert.Equal(imp.WorkFasterTicks, back.WorkFasterTicks);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }

    // ---- the hand holds things, not only creatures (ADR-0075, Q196) -------

    /// <summary>
    /// CONFIRMED by the owner, 2026-09-06: <i>"I can pick up a chicken and drop
    /// it in the prison, or anywhere in the dungeon, on my owned tiles"</i>,
    /// with a screenshot of four held at once.
    /// </summary>
    [Fact]
    public void TheHandLiftsAChickenAndPutsItDownAgain()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Pave(world, 1, 1, 4, 4);
        ObjectState bird = world.SpawnObject(
            db, HatcheryRules.ChickenObjectId, Keeper, Index(world, Floor));

        GameSimulation sim = Simulation(db, world);
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, bird.Id)));

        Assert.True(bird.InHand);
        Assert.Equal([HandItem.Of(bird)], HandOf(world));

        var landing = new TilePos(3, 3);
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, landing)));

        Assert.False(bird.InHand);
        Assert.Empty(HandOf(world));
        Assert.Equal(Index(world, landing), bird.TileIndex);
    }

    [Fact]
    public void TheHandWillNotLiftAThingThatIsNotLiftable()
    {
        // CanBePickedUp is 26 of the 145 records, and a Book Case is not one of
        // them - nor is any spell book, crate or bed. A keeper does not
        // hand-carry a finished trap to a workshop; an imp does.
        GameDatabase db = Db;
        WorldState world = World(db);
        Pave(world, 1, 1, 4, 4);
        ObjectState shelf = world.SpawnObject(
            db, Dk2TerrainFixture.BookCaseObjectId, Keeper, Index(world, Floor));

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, shelf.Id)));

        Assert.False(shelf.InHand);
        Assert.Empty(HandOf(world));
        Assert.Contains(sink.Events, e => e.Kind == "object_pickup_refused");
    }

    [Fact]
    public void AChickenMayOnlyBeDroppedOnTheKeepersOwnGround()
    {
        // CONFIRMED by the owner - "on my owned tiles". Where the rule lives in
        // the original is unread: its drop handler has no ownership test at all,
        // so the gate is in the input layer that draws the marker the
        // screenshot shows. And it is NOT CanBeDroppedOnAnyLand, which is set on
        // one record and read by nothing in the executable.
        GameDatabase db = Db;
        WorldState world = World(db);
        Pave(world, 1, 1, 3, 3);
        ObjectState bird = world.SpawnObject(
            db, HatcheryRules.ChickenObjectId, Keeper, Index(world, Floor));

        // An unowned patch of floor beside the keeper's.
        ref TileState loose = ref world.Tile(6, 6);
        loose.TerrainId = Dk2TerrainFixture.ClaimedPath;
        loose.OwnerPlayerId = PlayerIds.Neutral;

        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, bird.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(6, 6))));

        // Refused, and still in the hand rather than dropped anywhere else.
        Assert.True(bird.InHand);
        Assert.Single(HandOf(world));
        Assert.Contains(sink.Events, e =>
            e.Kind == "drop_refused" && e.Detail.Contains("not_owned"));

        // The keeper's own ground takes it.
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(2, 2))));
        Assert.False(bird.InHand);
    }

    [Fact]
    public void TheHandPutsDownTheLastThingItLiftedWhateverKindItIs()
    {
        // Last in, first out, CONFIRMED by the owner - and it holds ACROSS
        // kinds, because DK2 keeps one array of things rather than one per
        // kind. Creature first, chicken second, so the chicken lands first.
        GameDatabase db = Db;
        WorldState world = World(db);
        Pave(world, 1, 1, 4, 4);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        ObjectState bird = world.SpawnObject(
            db, HatcheryRules.ChickenObjectId, Keeper, Index(world, Floor));

        GameSimulation sim = Simulation(db, world);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, bird.Id)));

        Assert.Equal([HandItem.Of(imp), HandItem.Of(bird)], HandOf(world));

        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(3, 3))));

        Assert.False(bird.InHand);
        Assert.True(imp.InHand);
        Assert.Equal([HandItem.Of(imp)], HandOf(world));
    }

    [Fact]
    public void AChickenInTheKeepersHandIsNotFood()
    {
        // A held object is out of the dungeon, exactly as a held creature is.
        // Nothing may find it, eat it or decay it while it is up there.
        GameDatabase db = Db;
        WorldState world = World(db);
        Pave(world, 1, 1, 4, 4);
        ObjectState bird = world.SpawnObject(
            db, HatcheryRules.ChickenObjectId, Keeper, Index(world, Floor));

        Assert.True(EatingRules.IsEdibleChicken(bird));

        GameSimulation sim = Simulation(db, world);
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, bird.Id)));

        Assert.False(EatingRules.IsEdibleChicken(bird));
        Assert.False(EatingRules.IsFood(db, bird) && !bird.InHand);
    }

    [Fact]
    public void AMixedHandSurvivesASnapshot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        Pave(world, 1, 1, 4, 4);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, Floor);
        ObjectState bird = world.SpawnObject(
            db, HatcheryRules.ChickenObjectId, Keeper, Index(world, Floor));

        GameSimulation sim = Simulation(db, world);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, bird.Id)));

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(sim), db, SimulationSystems.Default());

        Assert.Equal(
            [HandItem.Of(imp), HandItem.Of(bird)], restored.World.Player(Keeper).Hand);
        Assert.True(restored.World.ObjectById(bird.Id)!.InHand);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }

    private static int Index(WorldState world, TilePos at) => (at.Y * world.Width) + at.X;
}
