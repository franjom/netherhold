// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The Hatchery: coops, and the egg that appears on one.</summary>
/// <remarks><para>Every number is the original's, read out of <c>DKII.EXE</c>
/// (docs/research/hatchery.md; balance-variables-in-the-binary.md for how the
/// variable was identified).</para>
/// <para>The laying interval is
/// <c>(int)(ChickenGenerationTimePerHatchery / pow(tiles, 0.315477776)) × V19</c>,
/// where V19 is DK2's ticks-a-game-second — a balance variable no shipped file
/// supplies, because the <b>level header</b> does (Q132). It is 4 on every
/// shipped level, and <see cref="Systems.HatcherySystem"/> drives the laying
/// (ADR-0066).</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hatchery.md")]
public static class HatcheryRules
{
    /// <summary>Tiles per coop: <b>six</b>, integer division. A 3×3 hatchery gets one
    /// coop and a 6×6 gets six.</summary>
    public const int TilesPerCoop = 6;

    /// <summary>The cap on coops in one room: <b>36</b>. Reaching it would take 216
    /// tiles, so it is unreachable in practice on a shipped map — recorded
    /// because it is in the code, not because it bites.</summary>
    public const int MaxCoops = 36;

    /// <summary>The object a hatchery places: <b>35, the Chicken Coop</b>.
    /// <para>Named by id rather than by a type flag — the exception to
    /// objects.md §3's advice, and the original's own choice: its union dispatch
    /// branches on the literal 35, and the coop's record carries no type bit at
    /// all, only <see cref="ObjectFlags.Obstacle"/>.</para></summary>
    public const byte CoopObjectId = 35;

    /// <summary>What a hatchery lays: <b>47, the Egg</b> — and <i>not</i> 9, the Chicken.
    /// <para>Of the three objects a hatchery deals in, only the Chicken carries
    /// <see cref="ObjectFlags.TypeFood"/> and has a creature's
    /// <c>HpFromChicken</c> against it (375 for a Goblin). Trap: the Torture
    /// Pendulum (65) carries the flag too, which matters because the prison food
    /// search matches the flag where the hatchery search matches the id
    /// (docs/research/eating.md §6). What turns an egg into a chicken in DK2 is a
    /// state machine — their <c>StartState</c> differs, 0 against 10 (Q133).</para></summary>
    public const byte EggObjectId = 47;

    /// <summary>The Chicken itself, object <b>9</b> — what an egg becomes, and the only
    /// one of the three a creature can eat.
    /// <para><b>The id, not the flag</b>, is what the original's hatchery search
    /// matches on (<see cref="EatingRules.IsEdibleChicken"/>). Its prison search
    /// matches the flag instead, and the two are not the same set.</para></summary>
    public const byte ChickenObjectId = 9;

    /// <summary>How long an egg takes to become a chicken: about <b>two seconds</b>.
    /// <para>CONFIRMED by the owner (2026-09-05), who watched the whole
    /// appear-and-hatch sequence. Caveat: it is timed from an animation, so
    /// whether the simulation holds an egg in a state for exactly that long or
    /// the art covers a shorter transition is UNKNOWN.</para></summary>
    public const int EggHatchSeconds = 2;

    /// <summary>How many chickens a hatchery holds: <b>one per tile</b>.
    /// <para>CONFIRMED by the owner's measurement (2026-09-05): a 5x5 settles at
    /// more than twenty and a 3x3 at exactly nine. Taken from the Chicken's
    /// OBJECTS <c>RoomCapacity</c> of 1 rather than pinned as a constant, so
    /// this is a rule about capacity and not a magic nine.</para>
    /// <para>Trap: it is not <c>MaxFreeRangeChickensPerPlayer</c>, which is 12,
    /// real and checked before laying — the 5x5 count rules it out as the room's
    /// limit (<see cref="FreeRangeCap"/>).</para></summary>
    public static int FlockCapacity(
        GameDatabase database, RoomInstance hatchery)
    {
        ObjectDef? chicken = database.ObjectById(ChickenObjectId);
        int perTile = chicken?.RoomCapacity ?? 0;
        return hatchery.TileIndices.Count * perTile;
    }

    /// <summary>How many chickens — hatched birds, not eggs — stand in this room.
    /// <para>Eggs are excluded on purpose. A room at capacity in chickens can
    /// still hold an egg that has not hatched, and counting the egg would make
    /// the room stop a bird early.</para></summary>
    public static int Flock(WorldState world, RoomInstance hatchery)
    {
        int count = 0;
        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].ObjectTypeId != ChickenObjectId) continue;
            if (!hatchery.TileIndices.Contains(objects[i].TileIndex)) continue;
            count++;
        }
        return count;
    }

    /// <summary>Chickens <i>and</i> eggs in this room — everything that is or is about to
    /// be a bird.
    /// <para>What the capacity is measured against rather than
    /// <see cref="Flock"/>: an egg is a chicken two seconds from now, so a room
    /// that laid while full would overshoot its limit by the eggs in the
    /// air.</para></summary>
    public static int Birds(WorldState world, RoomInstance hatchery)
    {
        int count = 0;
        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            byte type = objects[i].ObjectTypeId;
            if (type != ChickenObjectId && type != EggObjectId) continue;
            if (!hatchery.TileIndices.Contains(objects[i].TileIndex)) continue;
            count++;
        }
        return count;
    }

    /// <summary>Whether this hatchery is holding as many birds as it can.</summary>
    public static bool FlockIsFull(
        WorldState world, GameDatabase database, RoomInstance hatchery) =>
        Birds(world, hatchery) >= FlockCapacity(database, hatchery);

    /// <summary>The smallest squared distance, <b>in whole tiles</b>, that two coops may
    /// be apart: 3, so 0, 1 or 2 is refused — not the same tile, not
    /// orthogonally adjacent, not diagonally adjacent; two tiles in a straight
    /// line is 4 and is allowed.
    /// <para>The original compares fixed-point distances against 3.0 — about
    /// 1.73 tiles — and the integer form is exactly equivalent, not an
    /// approximation, because a coop always stands at its tile's centre:
    /// <c>n² × 4096 &lt; 3 × 4096</c> is <c>n² &lt; 3</c>.</para></summary>
    public const int MinCoopSeparationSquared = 3;

    /// <summary>How many coops this room is entitled to.</summary>
    public static int CoopCount(RoomInstance hatchery) =>
        Math.Min(hatchery.TileIndices.Count / TilesPerCoop, MaxCoops);

    /// <summary>Which tiles of this room should hold coops, in the order they are taken.
    /// <para>Deterministic, as the original is: it walks the room's own tile
    /// list and takes the first position far enough from every coop already
    /// placed, rather than choosing at random. So this needs no RNG and cannot
    /// drift, which matters because the room is rebuilt from the tiles every
    /// time it is asked for (<see cref="RoomIndex"/>).</para>
    /// <para>May return fewer than <see cref="CoopCount"/> when the spacing rule
    /// runs out of room, which a long thin hatchery can do.</para></summary>
    public static void CoopTiles(WorldState world, RoomInstance hatchery, List<int> into)
    {
        if (into is null) throw new ArgumentNullException(nameof(into));
        into.Clear();

        int wanted = CoopCount(hatchery);
        if (wanted == 0) return;

        int width = world.Width;
        foreach (int index in hatchery.TileIndices)
        {
            int x = index % width;
            int y = index / width;

            bool tooClose = false;
            foreach (int placed in into)
            {
                int dx = (placed % width) - x;
                int dy = (placed / width) - y;
                if ((dx * dx) + (dy * dy) < MinCoopSeparationSquared)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose) continue;

            into.Add(index);
            if (into.Count == wanted) return;
        }
    }

    /// <summary>The coops standing in this room, in placement order.</summary>
    public static void Coops(WorldState world, RoomInstance hatchery, List<ObjectState> into)
    {
        if (into is null) throw new ArgumentNullException(nameof(into));
        into.Clear();

        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].ObjectTypeId != CoopObjectId) continue;
            if (!hatchery.TileIndices.Contains(objects[i].TileIndex)) continue;
            into.Add(objects[i]);
        }
    }

    /// <summary>Brings this room's coops up to what its size entitles it to, and says how
    /// many it added.
    /// <para>Idempotent, so it is safe to call whenever the room changes: a coop
    /// already on a wanted tile is left alone. The owning
    /// <see cref="Systems.AutoFurnitureSystem"/> reconciles obsolete coops when
    /// a room changes shape.</para></summary>
    public static int PlaceCoops(
        WorldState world, GameDatabase database, RoomInstance hatchery)
    {
        var wanted = new List<int>();
        CoopTiles(world, hatchery, wanted);

        int added = 0;
        foreach (int index in wanted)
        {
            if (HasCoop(world, index)) continue;
            world.SpawnObject(database, CoopObjectId, hatchery.OwnerPlayerId, index).AutoPlaced = true;
            added++;
        }
        return added;
    }

    /// <summary>Whether a coop already stands on this tile.</summary>
    public static bool HasCoop(WorldState world, int tileIndex)
    {
        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i].TileIndex == tileIndex && objects[i].ObjectTypeId == CoopObjectId)
                return true;
        return false;
    }

    /// <summary>Lays one egg here and hands it back, or null for nowhere to put it.
    /// Called by <see cref="Systems.HatcherySystem"/> on the room's own interval
    /// (ADR-0066).
    /// <para>Two cases, in the original's order: with coops, one is chosen <b>at
    /// random</b> and the egg goes there; with none, an <i>empty</i> tile of the
    /// room is, so a hatchery too small to earn a coop still produces on the
    /// floor. The draw is behaviour and the first place DK2's own generator
    /// position would be observable (Q3). A full room
    /// (<see cref="FlockCapacity"/>) lays nothing: the limit is CONFIRMED, the
    /// mechanism a reading — DK2 might lay and let the surplus rot (Q135).</para></summary>
    public static ObjectState? TryLayEgg(
        WorldState world, GameDatabase database, RoomInstance hatchery, SimRandom rng)
    {
        if (rng is null) throw new ArgumentNullException(nameof(rng));

        // A full room lays nothing. See the doc above for what "full" is and
        // why the mechanism behind it is a reading rather than a certainty.
        if (FlockIsFull(world, database, hatchery)) return null;

        var coops = new List<ObjectState>();
        Coops(world, hatchery, coops);

        if (coops.Count > 0)
        {
            ObjectState coop = coops[rng.NextInt(coops.Count)];
            return Lay(world, database, hatchery, coop.TileIndex);
        }

        var empty = new List<int>();
        foreach (int index in hatchery.TileIndices)
            if (!AnythingOn(world, index)) empty.Add(index);

        if (empty.Count == 0) return null;

        return Lay(world, database, hatchery, empty[rng.NextInt(empty.Count)]);
    }

    private static ObjectState Lay(
        WorldState world, GameDatabase database, RoomInstance hatchery, int tileIndex)
    {
        // 0x004DE620 bumps room+0x51 on every egg it creates, on both paths.
        world.TileAtIndex(hatchery.TileIndices[0]).ChickensGenerated++;
        return world.SpawnObject(database, EggObjectId, hatchery.OwnerPlayerId, tileIndex);
    }

    /// <summary>The room's <c>ChickensGenerated</c> (<c>room+0x51</c>): eggs laid since it was made
    /// (<see cref="TileState.ChickensGenerated"/>).</summary>
    public static int ChickensGenerated(WorldState world, RoomInstance hatchery) =>
        hatchery.TileIndices.Count == 0 ? 0 : world.TileAtIndex(hatchery.TileIndices[0]).ChickensGenerated;

    /// <summary>Turns an egg into a chicken on the same tile, and hands back the chicken.
    /// Throws if handed something that is not an egg.
    /// <para>The chicken inherits the egg's tile and owner and the egg ceases to
    /// exist. Deviates: a replacement rather than a state change, because our
    /// object carries no state machine where DK2's has a <c>StateId</c> and a
    /// <c>StateAge</c> per object.</para>
    /// <para>Driven by <see cref="Systems.HatcherySystem"/>, which counts
    /// <see cref="EggHatchSeconds"/> on the egg's own
    /// <see cref="ObjectState.TimerTicks"/> and hatches <b>before</b> it lays, so
    /// an egg is not aged on the second it is laid (ADR-0066).</para></summary>
    public static ObjectState Hatch(
        WorldState world, GameDatabase database, ObjectState egg)
    {
        if (egg is null) throw new ArgumentNullException(nameof(egg));
        if (egg.ObjectTypeId != EggObjectId)
            throw new ArgumentException(
                $"Object {egg.Id} is type {egg.ObjectTypeId}, not an egg.", nameof(egg));

        int tile = egg.TileIndex;
        PlayerId owner = egg.OwnerPlayerId;

        world.RemoveObject(egg.Id);
        return world.SpawnObject(database, ChickenObjectId, owner, tile);
    }

    /// <summary>Whether any object at all stands on this tile.</summary>
    public static bool AnythingOn(WorldState world, int tileIndex)
    {
        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i].TileIndex == tileIndex) return true;
        return false;
    }

    /// <summary>The largest tile count the interval table covers. A hatchery bigger than
    /// this lays at the same rate as one exactly this big.
    /// <para>Not an arbitrary ceiling: <see cref="MaxCoops"/> is 36 and
    /// <see cref="TilesPerCoop"/> is 6, so a room stops gaining coops at 216
    /// tiles and there is nothing above that for a faster clock to feed.</para></summary>
    public const int MaxTilesForInterval = 256;

    // pow(n, 0.315477776) for n = 1..256, scaled by 2^24 and rounded.
    //
    // A TABLE RATHER THAN Math.Pow: this number decides when an egg appears and
    // eggs are hashed, IEEE-754 pow is not guaranteed bit-identical across
    // runtimes, and the truncation sits on a knife edge (a 3x3 computes 7.99977
    // and takes 7). HatcheryTests regenerates this from Math.Pow and compares.
    private static readonly int[] PowScaled =
    [
        16777216, 20877965, 23726903, 25981035, 27875932, 29526320, 30997705, 32331415,
        33555383, 34689471, 35748362, 36743252, 37682896, 38574279, 39423080, 40233979,
        41010889, 41757114, 42475474, 43168399, 43837997, 44486109, 45114356, 45724174,
        46316838, 46893488, 47455150, 48002747, 48537116, 49059015, 49569138, 50068118,
        50556534, 51034922, 51503773, 51963542, 52414650, 52857487, 53292417, 53719779,
        54139889, 54553042, 54959515, 55359568, 55753444, 56141374, 56523573, 56900246,
        57271584, 57637771, 57998978, 58355368, 58707098, 59054314, 59397155, 59735756,
        60070243, 60400737, 60727353, 61050201, 61369386, 61685010, 61997168, 62305952,
        62611451, 62913750, 63212929, 63509067, 63802239, 64092516, 64379969, 64664663,
        64946664, 65226032, 65502828, 65777110, 66048931, 66318347, 66585409, 66850166,
        67112668, 67372961, 67631090, 67887098, 68141029, 68392923, 68642820, 68890758,
        69136775, 69380908, 69623190, 69863657, 70102341, 70339274, 70574489, 70808014,
        71039881, 71270117, 71498750, 71725808, 71951317, 72175302, 72397790, 72618803,
        72838367, 73056504, 73273237, 73488587, 73702578, 73915228, 74126559, 74336591,
        74545343, 74752834, 74959083, 75164109, 75367927, 75570557, 75772015, 75972317,
        76171480, 76369519, 76566450, 76762288, 76957049, 77150745, 77343392, 77535003,
        77725593, 77915174, 78103759, 78291361, 78477993, 78663667, 78848395, 79032188,
        79215059, 79397018, 79578077, 79758246, 79937537, 80115959, 80293523, 80470240,
        80646118, 80821168, 80995399, 81168821, 81341442, 81513272, 81684320, 81854594,
        82024103, 82192855, 82360859, 82528123, 82694654, 82860461, 83025551, 83189931,
        83353610, 83516595, 83678892, 83840509, 84001453, 84161731, 84321349, 84480314,
        84638633, 84796311, 84953357, 85109774, 85265571, 85420752, 85575324, 85729292,
        85882663, 86035442, 86187634, 86339246, 86490282, 86640748, 86790649, 86939990,
        87088777, 87237014, 87384707, 87531860, 87678478, 87824566, 87970129, 88115171,
        88259697, 88403711, 88547218, 88690222, 88832728, 88974739, 89116260, 89257295,
        89397848, 89537924, 89677525, 89816657, 89955322, 90093525, 90231270, 90368560,
        90505399, 90641790, 90777738, 90913245, 91048315, 91182952, 91317160, 91450940,
        91584297, 91717234, 91849755, 91981861, 92113558, 92244847, 92375732, 92506215,
        92636301, 92765992, 92895290, 93024199, 93152721, 93280860, 93408618, 93535998,
        93663003, 93789635, 93915897, 94041792, 94167323, 94292491, 94417300, 94541752,
        94665849, 94789595, 94912991, 95036039, 95158743, 95281105, 95403127, 95524811,
        95646160, 95767175, 95887860, 96008216, 96128245, 96247950, 96367333, 96486396,
    ];

    /// <summary>How long this hatchery waits between eggs, in <b>game seconds</b>.
    /// <para>Read from the original (docs/research/hatchery.md §4). The
    /// truncation happens <b>before</b> the multiply by ticks-per-game-second, so
    /// this returns truncated whole seconds and the caller converts. A bigger
    /// hatchery lays faster on a curve: with the shipped 16, one tile every 16
    /// seconds, 3x3 every <b>7</b>, 5x5 every 5, 8x8 every 4.</para>
    /// <para>Trap: the 3x3 case is a quirk, not a rounding error to tidy. The
    /// exponent is typed rather than derived — not <c>log 2 / log 9</c> — and
    /// <c>pow(9, e)</c> is 2.00006, a hair above two, so 16/2.00006 is 7.99977
    /// and the truncation takes <b>7</b> where 8 looks right (Q134).</para></summary>
    public static int LayIntervalSeconds(GameDatabase database, RoomInstance hatchery) =>
        LayIntervalSecondsForTiles(database, hatchery.TileCount);

    /// <summary><see cref="LayIntervalSeconds"/> by tile count alone — the room is only
    /// ever consulted for its size, and a rule that takes the number it uses is
    /// a rule a test can drive across the whole table.</summary>
    public static int LayIntervalSecondsForTiles(GameDatabase database, int tiles)
    {
        int clamped = Math.Clamp(tiles, 1, MaxTilesForInterval);
        int seconds = database[VariableTypeId.ChickenGenerationTimePerHatchery];

        return (int)(((long)seconds << 24) / PowScaled[clamped - 1]);
    }

    /// <summary><c>MaxFreeRangeChickensPerPlayer</c> — <b>12</b> in the shipped table, and
    /// the original does check it before laying at a coop: variable 86, one byte
    /// wide, with exactly one reader in the binary.
    /// <para>Trap: it does not cap a hatchery's flock. The owner watched a 5x5
    /// hold more than twenty chickens, so whatever counter it is compared
    /// against, it is not "chickens this keeper owns" (<see cref="TryLayEgg"/>).
    /// Exposed, not enforced.</para></summary>
    public static int FreeRangeCap(GameDatabase database) =>
        database[VariableTypeId.MaxFreeRangeChickensPerPlayer];
}
