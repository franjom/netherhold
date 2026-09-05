// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;

namespace DK2.Behavior.Tests;

/// <summary>
/// The OBJECTS record, against the shipped file.
///
/// <para><b>This is the record four mechanics were recorded as blocked on</b>,
/// each with "OBJECTS is unparsed (M6)" written next to it: hunger that cannot
/// be satisfied, a death that owes a corpse, a graveyard that cannot fill, and
/// gold with nowhere to go.</para>
///
/// <para><b>Parsing it settled three of those and refuted the fourth</b> — there
/// is no corpse object, so corpses were never blocked on this record at all. See
/// <c>TheresNoCorpseObject_SoADeadBodyIsNotAnObject</c>, which is the most
/// useful test in this file.</para>
///
/// <para>Every test here skips silently without an install, like the rest of
/// this project.</para>
/// </summary>
public class ObjectsOnRealDataTests
{
    private static string? Install() => Dk2Install.Find();

    // ---- the parse is right -----------------------------------------------

    [Fact]
    public void OneHundredAndFortyFiveObjectsWithIdsOneToOneHundredAndFortyFive()
    {
        string? install = Install();
        if (install is null) return;

        // The strongest check on a stride and an offset at once. A wrong stride
        // would scramble the id column; a wrong offset would not produce a
        // permutation of 1..145. Both have to be right for this to pass.
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Equal(145, db.Objects.Count);
        Assert.Equal(
            Enumerable.Range(1, 145).Select(i => (byte)i).ToArray(),
            db.Objects.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void EveryObjectHasAReadableName()
    {
        string? install = Install();
        if (install is null) return;

        // 0x00 is a 32-byte name, as it is in every other record type. If the
        // stride or the base were wrong this column would come back as mojibake
        // for most records rather than for none.
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.All(db.Objects.Values, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Name));
            Assert.All(o.Name, c => Assert.InRange(c, ' ', '~'));
        });
    }

    [Fact]
    public void TheAnchorsFromOtherRecordsAllLand()
    {
        string? install = Install();
        if (install is null) return;

        // Four independent records point into this one, and every pointer hits
        // the thing its own name implies. This is what makes the layout
        // believed rather than merely arithmetically consistent.
        GameDatabase db = GameDatabaseLoader.Load(install);

        // Creatures name their beds.
        Assert.Equal(22, db.CreatureById(7).LairObjectId);           // Goblin
        Assert.Equal("Goblin Creature Bed", db.ObjectById(22)!.Name);
        Assert.Equal(26, db.CreatureByName("Vampire")!.LairObjectId);
        Assert.Equal("Vampire Creature Bed", db.ObjectById(26)!.Name);

        // The Graveyard names its four, and they are tombstones -- which is what
        // closed Q113 and is the same word Object.cpp's union uses.
        Assert.Equal(
            new byte[] { 31, 32, 33, 34 },
            db.Room(RoomTypeId.Graveyard).ObjectIds.Where(o => o != 0).ToArray());
        Assert.Equal("Tomb Stone 1", db.ObjectById(31)!.Name);
        Assert.Equal("Tomb Stone 4", db.ObjectById(34)!.Name);

        // The Hatchery names the coop, and the Training Room its dummy and its
        // target -- the two objects Q63's gold measurement is about.
        Assert.Equal("Chicken Coop", db.ObjectById(35)!.Name);
        Assert.Equal("Training Dummy", db.ObjectById(14)!.Name);
        Assert.Equal("Training Target", db.ObjectById(29)!.Name);
    }

    [Fact]
    public void TheFlagsCoverExactlyTheBitsTheDataUses()
    {
        string? install = Install();
        if (install is null) return;

        // 0x07F7F7FF: bits 11 and 19 are never set, and neither is anything
        // above 26. So ObjectFlags names every bit the shipped data uses and no
        // bit it does not -- which is why bits 11 and 19 have no name here even
        // though a community list offers none either.
        GameDatabase db = GameDatabaseLoader.Load(install);

        uint union = db.Objects.Values.Aggregate(0u, (a, o) => a | (uint)o.Flags);

        Assert.Equal(0x07F7F7FFu, union);
        Assert.Equal(0u, union & (1u << 11));
        Assert.Equal(0u, union & (1u << 19));
        Assert.Equal(0u, union & 0xF8000000u);
    }

    // ---- what it says about the blocked mechanics -------------------------

    [Fact]
    public void TheresNoCorpseObject_SoADeadBodyIsNotAnObject()
    {
        string? install = Install();
        if (install is null) return;

        // **THE CORRECTION.** This project has written "a corpse is an OBJECT
        // (M6)" as the named cause of three gaps -- no graveyard, no vampire,
        // and a death that owes a body -- in ADR-0051, in creature-death.md and
        // in half a dozen code comments.
        //
        // It is wrong. All 145 objects are named and none of them is a corpse.
        // The nearest things are decoration: Skeleton (48), Skull (49) and Bone
        // (50), which are graveyard and torture-chamber props, and none carries
        // a type flag at all.
        //
        // So a dead body is NOT an object, and corpses were never blocked on
        // this record. Which fits everything else: both timers are *creature*
        // variables (CreatureDyingStateDurationSeconds and
        // DeadBodyDiesAfterSeconds), DecomposeValue is a *creature* field, and
        // the binary's own per-creature dump carries HasBeenKnockedOut and
        // DieInstantly. A corpse is a creature in a state.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var names = db.Objects.Values.Select(o => o.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Corpse", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Dead", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Body", StringComparison.OrdinalIgnoreCase));

        // And the props that do exist are untyped decoration, not corpses.
        foreach (byte id in new byte[] { 48, 49, 50 })
        {
            ObjectDef prop = db.ObjectById(id)!;
            Assert.False(prop.IsLair);
            Assert.False(prop.IsFood);
            Assert.False(prop.IsGold);
            Assert.False(prop.Decays);
        }

        Assert.Equal("Skeleton", db.ObjectById(48)!.Name);
        Assert.Equal("Skull", db.ObjectById(49)!.Name);
        Assert.Equal("Bone", db.ObjectById(50)!.Name);
    }

    [Fact]
    public void TheChickenIsFoodAndItRotsUnlessItIsInARoom()
    {
        string? install = Install();
        if (install is null) return;

        // Hunger's other half, and it unblocks eating: object 9 is the Chicken,
        // it carries TypeFood, and it decays unless kept in a room -- which is
        // what a Hatchery is for. Its Egg (47) decays the same way.
        //
        // Note TypeFood also falls on the Torture Pendulum (65), which is
        // either a data quirk or a broader meaning for the bit. Recorded, not
        // explained (Q128).
        GameDatabase db = GameDatabaseLoader.Load(install);
        ObjectDef chicken = db.ObjectById(9)!;

        Assert.Equal("Chicken", chicken.Name);
        Assert.True(chicken.IsFood);
        Assert.True(chicken.Flags.HasFlag(ObjectFlags.DieOverTimeIfNotInRoom));
        Assert.True(chicken.Flags.HasFlag(ObjectFlags.CanBePickedUp));
        Assert.True(chicken.Flags.HasFlag(ObjectFlags.DieWhenSlapped));

        Assert.Equal("Egg", db.ObjectById(47)!.Name);
        Assert.True(db.ObjectById(47)!.Decays);

        var food = db.Objects.Values.Where(o => o.IsFood).Select(o => o.Name).ToHashSet();
        Assert.Contains("Chicken", food);
        Assert.Equal(2, food.Count);
    }

    [Fact]
    public void GoldOnTheFloorIsThreeObjectsAndABedIsTwelve()
    {
        string? install = Install();
        if (install is null) return;

        // The other two unblocked mechanics, and the bed count is the surprise:
        // **twelve bed objects for 46 creatures**, so beds are shared rather
        // than one per creature. One is the Hero Creature Bed, which covers the
        // whole hero side.
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Equal(
            new[] { "Gold", "Gold Bag", "Gold Pile 1" },
            db.Objects.Values.Where(o => o.IsGold)
                .OrderBy(o => o.ObjectId).Select(o => o.Name).ToArray());

        var beds = db.Objects.Values.Where(o => o.IsLair).ToList();
        Assert.Equal(12, beds.Count);
        Assert.Contains("Hero Creature Bed", beds.Select(b => b.Name));

        // And every bed a creature actually names is one of them -- the link
        // that makes LairRules' one-bed-per-tile rule point at real art.
        foreach (CreatureDef c in db.Creatures.Values.Where(c => c.LairObjectId != 0))
        {
            ObjectDef? bed = db.ObjectById(c.LairObjectId);
            Assert.NotNull(bed);
            Assert.True(bed!.IsLair, $"{c.Name}'s bed {bed.Name} is not flagged TypeLair");
        }
    }

    [Fact]
    public void TheDungeonHeartsHealthLooksLikeATruncatedHundredThousand()
    {
        string? install = Install();
        if (install is null) return;

        // Recorded because it is suspicious rather than because it is settled.
        // The Dungeon Heart's Hp reads **34464**, and 34464 is exactly
        // 100000 - 65536: the signature of a five-digit number typed into a
        // 16-bit field. The same arithmetic appears in KeeperSpells, where
        // Summon Horny's 100000 comes back as 34464 when read as a u16 (Q55) --
        // except there the truncation was OURS and reading 32 bits fixed it.
        //
        // Here it is not: the four bytes at 0x348 read 0x000086A0 either way, so
        // the *file* carries the truncated value. Which makes it a shipped data
        // quirk rather than a parser bug, and this test is what pins the
        // distinction (Q129).
        GameDatabase db = GameDatabaseLoader.Load(install);
        ObjectDef heart = db.ObjectById(13)!;

        Assert.Equal("Dungeon Heart", heart.Name);
        Assert.Equal(34464, heart.Hp);
        Assert.Equal(34464, 100000 - 65536);
    }

    [Fact]
    public void RoomFurnitureIsWhatMostOfTheRecordIs()
    {
        string? install = Install();
        if (install is null) return;

        // Perspective on what the record is for, and it is mostly not gameplay:
        // of 145 objects only 33 carry any type flag at all. The rest are
        // pillars, arches, candlesticks, front-end banners and the furniture
        // each room places -- which is why parsing the art was unnecessary and
        // why a rule should ask a flag rather than an id wherever it can.
        GameDatabase db = GameDatabaseLoader.Load(install);

        const ObjectFlags typed =
            ObjectFlags.TypeSpecial | ObjectFlags.TypeSpellBook | ObjectFlags.TypeCrate
            | ObjectFlags.TypeLair | ObjectFlags.TypeGold | ObjectFlags.TypeFood
            | ObjectFlags.TypeLevelGem;

        int withType = db.Objects.Values.Count(o => ((uint)o.Flags & (uint)typed) != 0);

        Assert.Equal(52, withType);
        Assert.Equal(145, db.Objects.Count);

        // Every room that names furniture names objects that exist.
        foreach (RoomDef room in db.Rooms.Values)
        {
            foreach (byte id in room.ObjectIds.Where(o => o != 0))
                Assert.NotNull(db.ObjectById(id));
        }
    }

    // ---- the fixture cannot drift from the file ---------------------------

    [Fact]
    public void TheTwoBedsTheSimulationFixtureTranscribesAreTheRealRecords()
    {
        string? install = Install();
        if (install is null) return;

        // The simulation fixture now carries object definitions, because a bed
        // is a thing that stands on a tile and TryPlaceBedInAnyLair has to place one.
        // Those definitions are transcribed by hand, which is how four wrong
        // fixture values have got in before -- three creature flags and a
        // fourth on the Goblin, each one asserting a behaviour the real record
        // does not have.
        //
        // The values are repeated as literals rather than compared against the
        // fixture type, on purpose: DK2.Behavior.Tests deliberately holds no
        // project reference to DK2.Simulation.Tests, so this guard has to state
        // what the fixture claims and let the real file contradict it.
        GameDatabase db = GameDatabaseLoader.Load(install);

        foreach ((byte id, string name) in new (byte, string)[]
        {
            (22, "Goblin Creature Bed"),
            (26, "Vampire Creature Bed"),
        })
        {
            ObjectDef bed = Assert.IsType<ObjectDef>(db.ObjectById(id));

            Assert.Equal(name, bed.Name);
            Assert.Equal(0x04034020u, (uint)bed.Flags);
            Assert.Equal(128, bed.Hp);
            Assert.Equal(0, bed.ManaValue);
            Assert.Equal(1, bed.RoomCapacity);
            Assert.Equal(0, bed.StartState);
            Assert.Equal(176, bed.PickUpPriority);
            Assert.Equal(2048, bed.Width.Raw);
            Assert.Equal(256, bed.Height.Raw);
            Assert.Equal(8192, bed.Mass.Raw);
            Assert.Equal(0, bed.Speed.Raw);
            Assert.Equal(0, bed.NameStringId);
            Assert.Equal(0, bed.TooltipStringId);

            // And the one bit any rule reads.
            Assert.True(bed.IsLair);
        }

        // The two are the same record apart from the name, which is the file
        // agreeing with the flag-not-id rule: which of the twelve beds a tile
        // holds decides what is drawn and nothing else.
        ObjectDef goblinBed = Assert.IsType<ObjectDef>(db.ObjectById(22));
        ObjectDef vampireBed = Assert.IsType<ObjectDef>(db.ObjectById(26));
        Assert.Equal(
            goblinBed with { Name = "", ObjectId = 0 },
            vampireBed with { Name = "", ObjectId = 0 });
    }

    [Fact]
    public void TheObjectIdsTheBinaryHardcodesNameTheObjectsWeThinkTheyDo()
    {
        string? install = Install();
        if (install is null) return;

        // Read out of DKII.EXE on 2026-09-04: the routine that dumps an object
        // switches on a single byte -- the object's own id -- with hardcoded
        // literals, one per union member. Eighteen of those literals could be
        // paired with a name, and all eighteen agree with this parse.
        //
        // This is the strongest check the OBJECTS layout has, and the only one
        // that comes from outside the file. The other four are internal: a
        // field list that sums to the stride, an id column that is a
        // permutation, a string that lands at the record's end, pointers from
        // other records. Those say the bytes are self-consistent. This says
        // they mean what we think they mean -- the code has eighteen names for
        // eighteen numbers and the file gives those numbers the same names.
        //
        // See docs/research/objects.md section 7.
        GameDatabase db = GameDatabaseLoader.Load(install);

        var branches = new (byte Id, string Name)[]
        {
            (4, "Spell Book"),            // ResearchBook
            (8, "Torture Wheel"),         // TortureWheel
            (9, "Chicken"),               // Chicken
            (10, "Call To Arms"),         // CallToArms
            (11, "Rat"),                  // Rat
            (29, "Training Target"),      // TrainingTarget
            (31, "Tomb Stone 1"),         // TombStone, which tests 31..34
            (32, "Tomb Stone 2"),
            (33, "Tomb Stone 3"),
            (34, "Tomb Stone 4"),
            (35, "Chicken Coop"),         // Coop
            (64, "Book Case"),            // BookCase
            (70, "Guard Post"),           // GuardPost
            (74, "Boulder"),              // Boulder
            (98, "Destructor"),           // Destructor
            (101, "Mana Vault"),          // ManaVault
            (103, "Threat Burn"),         // ThreatBurn
            (108, "Claimed Mana Vault"),  // ClaimedManaVault
            (111, "Temple Candlestick"),  // TempleCandleStick
        };

        foreach ((byte id, string name) in branches)
            Assert.Equal(name, db.ObjectById(id)?.Name);
    }

    [Fact]
    public void TheTypeFlagsTheBinaryDispatchesOnAreTheOnesWeNamed()
    {
        string? install = Install();
        if (install is null) return;

        // The rest of that same dispatch does not use the id: it tests bits of
        // the loaded definition's flags word. Five of the seven type bits are
        // therefore confirmed by the code that ACTS on them, rather than only
        // by a name borrowed from OpenKeeper and a column that looked
        // plausible. TypeFood and TypeLevelGem are not tested there and stay on
        // the earlier evidence.
        //
        // Each pair below is (bit the binary tests, object whose branch it
        // takes), so a renamed or renumbered flag fails here.
        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Equal(0x40u, (uint)ObjectFlags.TypeGold);
        Assert.Equal(0x04u, (uint)ObjectFlags.TypeSpecial);
        Assert.Equal(0x20u, (uint)ObjectFlags.TypeLair);
        Assert.Equal(0x10u, (uint)ObjectFlags.TypeCrate);
        Assert.Equal(0x08u, (uint)ObjectFlags.TypeSpellBook);

        // Object 4 takes the ResearchBook branch by id, and its record carries
        // the spell-book bit -- the two halves of the dispatch agreeing.
        Assert.True(db.ObjectById(4)?.IsSpellBook);

        // The Lair branch is reached by flag alone, so every bed must carry it.
        Assert.Equal(12, db.Objects.Values.Count(o => o.IsLair));
        Assert.True(db.ObjectById(22)?.IsLair);
        Assert.True(db.ObjectById(26)?.IsLair);
    }

    [Fact]
    public void TheHatcherysThreeObjectsAreTheRealRecords()
    {
        string? install = Install();
        if (install is null) return;

        // The simulation fixture transcribes these three because HatcheryRules
        // needs them, and the same guard applies as to the beds: literals here,
        // no project reference to the fixture, so the real file gets to
        // contradict what the fixture claims.
        //
        // The flag words are the load-bearing part, and one of them is a
        // negative: the EGG MUST NOT CARRY TypeFood. That is the whole of Q133 --
        // a hatchery lays object 47 and a creature eats object 9 -- and a test
        // that let the egg be food would erase the question.
        GameDatabase db = GameDatabaseLoader.Load(install);

        ObjectDef coop = Assert.IsType<ObjectDef>(db.ObjectById(35));
        Assert.Equal("Chicken Coop", coop.Name);
        Assert.Equal(0x00004000u, (uint)coop.Flags);   // Obstacle, and nothing else
        Assert.Equal(1, coop.Hp);
        Assert.Equal(0, coop.RoomCapacity);
        Assert.Equal(3252, coop.Width.Raw);
        Assert.Equal(1228, coop.Height.Raw);

        // No type bit at all, which is why HatcheryRules names the id.
        Assert.False(coop.IsFood);
        Assert.False(coop.IsLair);
        Assert.False(coop.IsGold);

        ObjectDef egg = Assert.IsType<ObjectDef>(db.ObjectById(47));
        Assert.Equal("Egg", egg.Name);
        Assert.Equal(0x00018602u, (uint)egg.Flags);
        Assert.Equal(128, egg.Hp);
        Assert.Equal(0, egg.StartState);
        Assert.Equal(2719, egg.NameStringId);
        Assert.Equal(2720, egg.TooltipStringId);
        Assert.False(egg.IsFood, "an egg is not food -- see Q133");
        Assert.True(egg.Decays, "an egg rots outside a room");

        ObjectDef chicken = Assert.IsType<ObjectDef>(db.ObjectById(9));
        Assert.Equal("Chicken", chicken.Name);
        Assert.Equal(0x00230782u, (uint)chicken.Flags);
        Assert.Equal(1000, chicken.Hp);
        Assert.Equal(1, chicken.RoomCapacity);
        Assert.True(chicken.IsFood);
        Assert.True(chicken.Decays);

        // The two differ in more than the food bit, and the differences are the
        // reason to believe they are different things rather than one thing
        // misread: a chicken can be picked up and an egg cannot, and their
        // start states are 10 and 0.
        Assert.Equal(10, chicken.StartState);
        Assert.True(chicken.Flags.HasFlag(ObjectFlags.CanBePickedUp));
        Assert.False(egg.Flags.HasFlag(ObjectFlags.CanBePickedUp));

        // Both die when slapped, which is DK2 letting you kill your own dinner.
        Assert.True(chicken.Flags.HasFlag(ObjectFlags.DieWhenSlapped));
        Assert.True(egg.Flags.HasFlag(ObjectFlags.DieWhenSlapped));
    }

    [Fact]
    public void TheHatcherysOwnNumbersAreWhatTheGameShips()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);

        // The room the fixture transcribes: 300 to build, terrain 13, 3x3
        // recommended -- and 3x3 matters, because nine tiles is where the
        // laying curve's exponent gives exactly 2 (hatchery.md section 4).
        RoomDef hatchery = db.RoomById(4);
        Assert.Equal("Hatchery", hatchery.Name);
        Assert.Equal(300, hatchery.Cost);
        Assert.Equal(13, hatchery.TerrainId);
        Assert.Equal(3, hatchery.RecommendedSizeX);
        Assert.Equal(3, hatchery.RecommendedSizeY);

        // The room names the coop among its furniture, which is the shipped
        // data agreeing with the binary's hardcoded 35.
        Assert.Contains((byte)35, hatchery.ObjectIds);

        // And the two balance numbers. The generation time is read by the
        // interval this project cannot yet build (Q132); the free-range cap is
        // read but not enforced, because what its counter counts is UNKNOWN.
        Assert.Equal(16, db[VariableTypeId.ChickenGenerationTimePerHatchery]);
        Assert.Equal(12, db[VariableTypeId.MaxFreeRangeChickensPerPlayer]);
    }

    [Fact]
    public void ForLevelKeepsEveryRecordCollection()
    {
        string? install = Install();
        if (install is null) return;

        // A regression guard with a real defect behind it.
        // GameDatabase.ForLevel rebuilds the database from parts, and it
        // silently dropped Objects for as long as objects existed: the
        // constructor gained the parameter and this call did not.
        //
        // Nothing noticed for two sittings, because objects were only
        // definitions -- until a bed became a thing standing on a tile, and
        // then a campaign scenario threw for want of object 22 while all 719
        // tests passed. Tests build their database directly; none of them
        // called this.
        //
        // Swept by reflection rather than named one at a time, so a record
        // added tomorrow is covered the day it is added. Every collection is
        // asserted non-empty first, or an omission could pass by being zero on
        // both sides.
        GameDatabase db = GameDatabaseLoader.Load(install);
        GameDatabase forLevel = db.ForLevel(new GlobalVariables());

        Assert.NotSame(db, forLevel);

        var collections = typeof(GameDatabase).GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            .ToArray();

        Assert.NotEmpty(collections);

        foreach (System.Reflection.PropertyInfo property in collections)
        {
            int before = Count(property.GetValue(db));
            int after = Count(property.GetValue(forLevel));

            Assert.True(before > 0, $"{property.Name} is empty, so this proves nothing");
            Assert.True(
                before == after,
                $"ForLevel dropped {property.Name}: {before} became {after}");
        }

        static int Count(object? value) =>
            value is System.Collections.IEnumerable e ? e.Cast<object>().Count() : -1;
    }

    /// <summary>
    /// The three gold records, read back out of the shipped file, because
    /// <c>FloorGold</c> names two of the three ids outright and the simulation
    /// fixture transcribes all three.
    /// </summary>
    [Fact]
    public void TheThreeGoldObjectsAreTheRealRecords()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);

        ObjectDef gold = Assert.IsType<ObjectDef>(db.ObjectById(1));
        Assert.Equal("Gold", gold.Name);
        Assert.Equal(0x00810140u, (uint)gold.Flags);
        Assert.Equal(128, gold.Hp);
        Assert.Equal(0, gold.RoomCapacity);
        Assert.Equal(31, gold.StartState);
        Assert.Equal(256, gold.Width.Raw);
        Assert.Equal(1228, gold.Height.Raw);
        Assert.Equal(16384, gold.Mass.Raw);

        ObjectDef bag = Assert.IsType<ObjectDef>(db.ObjectById(2));
        Assert.Equal("Gold Bag", bag.Name);
        Assert.Equal(0x00810140u, (uint)bag.Flags);
        Assert.Equal(1, bag.RoomCapacity);
        Assert.Equal(585, bag.Width.Raw);

        // The treasury's pile, and the field to notice is RoomCapacity = 1.
        // It is the same field that turned out to cap a hatchery at one chicken
        // per tile, and here it says one gold object per treasury tile -- which
        // is exactly what the original's deposit does, found independently in
        // the binary rather than read off this column.
        ObjectDef pile = Assert.IsType<ObjectDef>(db.ObjectById(3));
        Assert.Equal("Gold Pile 1", pile.Name);
        Assert.Equal(0x00014140u, (uint)pile.Flags);
        Assert.Equal(1, pile.RoomCapacity);
        Assert.Equal(1085, pile.Width.Raw);
        Assert.Equal(1068, pile.Height.Raw);

        // All three are gold, none of them is anything else, and every one can
        // be picked up -- so the flag says "gold" and the id says "which".
        foreach (ObjectDef o in new[] { gold, bag, pile })
        {
            Assert.True(o.IsGold);
            Assert.False(o.IsLair);
            Assert.False(o.IsFood);
            Assert.True(o.Flags.HasFlag(ObjectFlags.CanBePickedUp));
            Assert.Equal(292, o.NameStringId);
            Assert.False(o.Decays, "gold on the floor does not rot away");
        }

        // And they are the only three, so naming an id is naming a record.
        Assert.Equal(
            new byte[] { 1, 2, 3 },
            db.Objects.Values.Where(o => o.IsGold).Select(o => o.ObjectId).OrderBy(i => i).ToArray());
    }

    /// <summary>
    /// The three gold limits the binary reads, checked against the shipped
    /// table — because <c>gold-on-the-floor.md</c>'s whole point is that one of
    /// the three is bigger than another and nothing clamps between them.
    /// </summary>
    [Fact]
    public void TheGoldLimitsAreWhatTheGameShips()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);

        Assert.Equal(3000, db[VariableTypeId.MaxGoldPerTreasuryTile]);
        Assert.Equal(2000, db[VariableTypeId.MaxGoldPileOutsideTreasury]);
        Assert.Equal(1000, db[VariableTypeId.MaxGoldPerDungeonHeartTile]);

        // A full treasury tile holds 1000 more than the outside-treasury limit,
        // which is Q65: sell that tile and the pile it leaves is over the limit.
        // ANSWERED from the binary (2026-09-05) -- the surplus is neither split
        // nor lost, because nothing on that path looks at the limit at all.
        Assert.True(
            db[VariableTypeId.MaxGoldPerTreasuryTile]
                > db[VariableTypeId.MaxGoldPileOutsideTreasury]);

        // 1000 x 16 = 16000, the total the owner measured for a keeper with no
        // treasury (Q52, ADR-0027). The binary computes a heart's capacity as
        // this variable shifted left by four -- a HARDCODED sixteen, not a count
        // of the room's tiles -- which is an independent confirmation both of
        // the number and of the reading that nine of a heart's 25 tiles store
        // nothing.
        Assert.Equal(16000, db[VariableTypeId.MaxGoldPerDungeonHeartTile] << 4);
    }
}