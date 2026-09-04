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
}
