// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The Hatchery: coops, and the egg that appears on one.
///
/// <para><b>Read out of <c>DKII.EXE</c></b> on 2026-09-04, and every number here
/// is the original's — see <c>docs/research/hatchery.md</c> for the evidence and
/// <c>balance-variables-in-the-binary.md</c> for how the variable it reads was
/// identified.</para>
///
/// <para><b>Deliberately incomplete, and the missing piece is named.</b> DK2's
/// laying interval is
/// <c>(int)(ChickenGenerationTimePerHatchery / pow(tiles, 0.315477776)) × V19</c>,
/// and <b>V19 is a balance variable that no shipped file supplies</b> — its
/// global is read from 223 places in the binary as both multiplier and divisor
/// and is absent from all 1236 files in the install — because the <b>level
/// header</b> supplies it, which is what Q132 turned out to be. It is <b>4</b>
/// on every shipped level, so the interval has its unit and
/// <see cref="Systems.HatcherySystem"/> drives the laying (ADR-0066).</para>
///
/// <para>What is here is everything the binary settles outright: how many coops
/// a room gets, where they may stand, and where an egg appears. Written now
/// rather than with the clock for the reason <see cref="CreatureHealthRules.Eat"/>
/// and <see cref="CreatureDeathRules.Rescue"/> were: the transition is the part
/// worth getting right, it can be tested now, and waiting would mean writing it
/// blind.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hatchery.md")]
public static class HatcheryRules
{
    /// <summary>
    /// Tiles per coop: <b>six</b>, integer division. A 3×3 hatchery gets one
    /// coop and a 6×6 gets six.
    /// </summary>
    public const int TilesPerCoop = 6;

    /// <summary>
    /// The cap on coops in one room: <b>36</b>. Reaching it would take 216
    /// tiles, so it is unreachable in practice on a shipped map — recorded
    /// because it is in the code, not because it bites.
    /// </summary>
    public const int MaxCoops = 36;

    /// <summary>
    /// The object a hatchery places: <b>35, the Chicken Coop</b>.
    ///
    /// <para><b>Named by id rather than by a type flag</b>, which is the
    /// exception to <c>objects.md</c> §3's advice and the original's own choice:
    /// its union dispatch branches on the literal 35, and the coop's record
    /// carries no type bit at all — only <see cref="ObjectFlags.Obstacle"/>.
    /// There is nothing to ask.</para>
    /// </summary>
    public const byte CoopObjectId = 35;

    /// <summary>
    /// What a hatchery lays: <b>47, the Egg</b> — and <i>not</i> 9, the Chicken.
    ///
    /// <para>Two different objects. Of the three a hatchery deals in — coop, egg
    /// and chicken — only the Chicken carries
    /// <see cref="ObjectFlags.TypeFood"/>, and only the Chicken has a creature's
    /// <c>HpFromChicken</c> against it (375 for a Goblin). <b>It is not the only
    /// object in the game with that flag</b>, which this comment said until
    /// 2026-09-06: the <b>Torture Pendulum</b> (65) carries it too, and the
    /// difference matters because the original's prison food search matches the
    /// flag where its hatchery search matches the id
    /// (<c>docs/research/eating.md</c> §6). So something turns an
    /// egg into a chicken and this project had never accounted for it; the
    /// objects' differing <c>StartState</c> — 0 for the egg, 10 for the
    /// chicken — says both have a state machine to do it in. <b>Q133</b>, and
    /// the owner can settle it by looking at a hatchery.</para>
    /// </summary>
    public const byte EggObjectId = 47;

    /// <summary>
    /// The Chicken itself, object <b>9</b> — what an egg becomes, and the only
    /// one of the three a creature can eat.
    ///
    /// <para><b>The id, not the flag</b>, is what the original's hatchery search
    /// matches on (<see cref="EatingRules.IsEdibleChicken"/>). Its prison search
    /// matches the flag instead, and the two are not the same set.</para>
    /// </summary>
    public const byte ChickenObjectId = 9;

    /// <summary>
    /// How long an egg takes to become a chicken: about <b>two seconds</b>.
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-05), who watched one:
    /// <i>"it produces eggs that hatch, that entire animation of eggs appearing
    /// and hatching lasts about 2 seconds, and after that a chicken appears and
    /// roams around the hatchery."</i></para>
    ///
    /// <para><b>Timed from an animation, which is the caveat.</b> Two seconds is
    /// how long the whole appear-and-hatch sequence takes on screen; whether the
    /// simulation holds an egg in a state for exactly that long, or the art
    /// simply covers a shorter transition, is not settled. It is recorded as the
    /// observation it is, and nothing yet advances a clock towards it — see
    /// <see cref="Hatch"/>.</para>
    /// </summary>
    public const int EggHatchSeconds = 2;

    /// <summary>
    /// How many chickens a hatchery holds: <b>one per tile</b>.
    ///
    /// <para><b>CONFIRMED by the owner, by measurement</b> (2026-09-05). A 5×5
    /// settles at more than twenty and a <b>3×3 settles at exactly nine</b> —
    /// which is one per tile on both counts, and rules out the only other
    /// candidate this project had.</para>
    ///
    /// <para><b>And the record said so all along.</b> The Chicken's OBJECTS
    /// entry carries <c>RoomCapacity</c> = 1, a field whose doc reads "how many
    /// of it a room tile holds". Nobody had read it as a flock limit because
    /// nothing needed one. So the number is <b>not</b> a constant here: it is
    /// taken from the definition, which is what makes this a rule about
    /// <i>capacity</i> rather than a magic nine.</para>
    ///
    /// <para><b>It is not <c>MaxFreeRangeChickensPerPlayer</c></b>, which was
    /// this project's assumption until the owner looked. That variable is 12,
    /// real, and checked before laying — but 25 chickens in a 5×5 rules it out
    /// as the room's limit, so it caps loose birds instead. See
    /// <see cref="FreeRangeCap"/>.</para>
    /// </summary>
    public static int FlockCapacity(
        GameDatabase database, RoomInstance hatchery)
    {
        ObjectDef? chicken = database.ObjectById(ChickenObjectId);
        int perTile = chicken?.RoomCapacity ?? 0;
        return hatchery.TileIndices.Count * perTile;
    }

    /// <summary>
    /// How many chickens — hatched birds, not eggs — stand in this room.
    ///
    /// <para>Eggs are excluded on purpose. A room at capacity in chickens can
    /// still hold an egg that has not hatched, and counting the egg would make
    /// the room stop a bird early.</para>
    /// </summary>
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

    /// <summary>
    /// Chickens <i>and</i> eggs in this room — everything that is or is about to
    /// be a bird.
    ///
    /// <para>What the capacity is measured against, and the reason it is not
    /// just <see cref="Flock"/>: an egg is a chicken two seconds from now, so a
    /// room that laid while full would overshoot its own limit by however many
    /// eggs were in the air.</para>
    /// </summary>
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

    /// <summary>
    /// The smallest squared distance, <b>in whole tiles</b>, that two coops may
    /// be apart: 3, so a squared distance of 0, 1 or 2 is refused.
    ///
    /// <para>In plain terms: <b>not the same tile, not orthogonally adjacent,
    /// not diagonally adjacent.</b> Two tiles apart in a straight line is 4 and
    /// is allowed.</para>
    ///
    /// <para><b>The original compares fixed-point distances against 3.0</b> — a
    /// spacing of about 1.73 tiles — and the integer form above is exactly
    /// equivalent rather than an approximation, because a coop always stands at
    /// its tile's centre. A separation of <c>n</c> tiles is <c>n × 4096</c> in
    /// DK2's fixed point, its square is <c>n² × 4096</c> once scaled back, and
    /// <c>n² × 4096 &lt; 3 × 4096</c> is <c>n² &lt; 3</c>. Doing it in tiles
    /// keeps the arithmetic exact and needs no <see cref="Fp32"/> at all.</para>
    /// </summary>
    public const int MinCoopSeparationSquared = 3;

    /// <summary>How many coops this room is entitled to.</summary>
    public static int CoopCount(RoomInstance hatchery) =>
        Math.Min(hatchery.TileIndices.Count / TilesPerCoop, MaxCoops);

    /// <summary>
    /// Which tiles of this room should hold coops, in the order they are taken.
    ///
    /// <para><b>Deterministic, and the original is too</b>: it walks the room's
    /// own tile list and takes the first position that is far enough from every
    /// coop already placed, rather than choosing at random. So this needs no RNG
    /// and cannot drift — which matters, because the room is rebuilt from the
    /// tiles every time it is asked for (<see cref="RoomIndex"/>).</para>
    ///
    /// <para>May return fewer than <see cref="CoopCount"/> if the spacing rule
    /// runs out of room, which a long thin hatchery can do.</para>
    /// </summary>
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

    /// <summary>
    /// Brings this room's coops up to what its size entitles it to, and says how
    /// many it added.
    ///
    /// <para>Idempotent, so it is safe to call whenever the room changes: a coop
    /// already standing on a wanted tile is left alone. A coop on a tile the
    /// rule no longer wants — the room was dug into a different shape — is
    /// <b>not</b> removed, because whether DK2 tidies those up is UNKNOWN and
    /// removing one is the more destructive guess.</para>
    /// </summary>
    public static int PlaceCoops(
        WorldState world, GameDatabase database, RoomInstance hatchery)
    {
        var wanted = new List<int>();
        CoopTiles(world, hatchery, wanted);

        int added = 0;
        foreach (int index in wanted)
        {
            if (HasCoop(world, index)) continue;
            world.SpawnObject(database, CoopObjectId, hatchery.OwnerPlayerId, index);
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

    /// <summary>
    /// Lays one egg in this hatchery and hands it back, or returns null if there
    /// was nowhere to put it.
    ///
    /// <para><b>Two cases, and the original does them in this order.</b> If the
    /// room has coops, one is chosen <b>at random</b> and the egg goes there. If
    /// it has none, the room's <i>empty</i> tiles are counted and one of those is
    /// chosen at random instead — so a hatchery too small to earn a coop still
    /// produces, on the floor.</para>
    ///
    /// <para><b>The random draw is behaviour</b>, and it is the first place in
    /// this simulation where DK2's own generator position would be observable
    /// (Q3): two hatcheries of one size lay in the same rhythm but not in the
    /// same places. Ours is <see cref="SimRandom"/>, which is not DK2's
    /// generator and does not try to be — what the determinism gate needs is
    /// that the draw happens in a fixed order, and it does.</para>
    ///
    /// <para><b>A full room lays nothing</b>, where full is
    /// <see cref="FlockCapacity"/> — one bird per tile, measured by the owner at
    /// nine in a 3×3. <b>The limit is confirmed; the mechanism behind it is a
    /// reading.</b> DK2 might refuse to lay, or lay anyway and let the surplus
    /// rot; the owner reports both continuous production and birds vanishing, so
    /// both are in play. Refusing is what reproduces the measurement — a 3×3
    /// settling at <i>exactly</i> nine is a cap, not the balance point of two
    /// rates — and it is the choice that cannot run away. Q135.</para>
    ///
    /// <para><see cref="Systems.HatcherySystem"/> calls this on the room's own
    /// interval (ADR-0066). The per-player free-range cap is still not
    /// applied, and <b>the owner's observation is now the reason rather than
    /// a hedge</b>: a 5×5 hatchery holds <i>more than twenty</i> chickens
    /// (2026-09-05), where a cap of 12 on the room's flock would have stopped it
    /// at twelve. So <c>MaxFreeRangeChickensPerPlayer</c> counts something
    /// narrower than "chickens this keeper has" — loose ones, which is what its
    /// name says and what the Chicken union's own <c>FreeRange</c> field
    /// distinguishes. Enforcing it here would throttle a room that DK2 does not
    /// throttle.</para>
    /// </summary>
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
        WorldState world, GameDatabase database, RoomInstance hatchery, int tileIndex) =>
        world.SpawnObject(database, EggObjectId, hatchery.OwnerPlayerId, tileIndex);

    /// <summary>
    /// Turns an egg into a chicken on the same tile, and hands back the chicken.
    ///
    /// <para><b>The transition the owner confirmed</b> and the data had only
    /// implied: a hatchery lays object 47 and a creature eats object 9, and this
    /// is the step between. The chicken inherits the egg's tile and owner, and
    /// the egg ceases to exist — it is a replacement rather than a
    /// state change, because our object carries no state machine yet (DK2's
    /// does: every object has a <c>StateId</c> and a <c>StateAge</c>, and the
    /// egg's start state is 0 against the chicken's 10).</para>
    ///
    /// <para><see cref="Systems.HatcherySystem"/> drives this, counting
    /// <see cref="EggHatchSeconds"/> on the egg's own
    /// <see cref="ObjectState.TimerTicks"/> and hatching <b>before</b> it lays,
    /// so an egg is not aged on the second it is laid (ADR-0066).</para>
    ///
    /// <para>Throws if handed something that is not an egg, because silently
    /// hatching a bed would be worse than stopping.</para>
    /// </summary>
    public static ObjectState Hatch(
        WorldState world, GameDatabase database, ObjectState egg)
    {
        if (egg is null) throw new ArgumentNullException(nameof(egg));
        if (egg.ObjectTypeId != EggObjectId)
            throw new ArgumentException(
                $"Object {egg.Id} is type {egg.ObjectTypeId}, not an egg.", nameof(egg));

        int tile = egg.TileIndex;
        byte owner = egg.OwnerPlayerId;

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

    /// <summary>
    /// <c>MaxFreeRangeChickensPerPlayer</c> — <b>12</b> in the shipped table, and
    /// the original really does check it before laying at a coop: variable 86,
    /// whose global is one byte wide and has <b>exactly one reader in the whole
    /// binary</b>, which is that check.
    ///
    /// <para><b>It does not cap a hatchery's flock.</b> The owner watched a 5×5
    /// hold more than twenty chickens. Whatever the counter it is compared
    /// against holds, it is not "chickens this keeper owns" — see
    /// <see cref="TryLayEgg"/>. Exposed, not enforced.</para>
    /// </summary>
    /// <summary>
    /// The largest tile count the interval table covers. A hatchery bigger than
    /// this lays at the same rate as one exactly this big.
    ///
    /// <para>Not an arbitrary ceiling: <see cref="MaxCoops"/> is 36 and
    /// <see cref="TilesPerCoop"/> is 6, so a room stops gaining coops at 216
    /// tiles and there is nothing above that for a faster clock to feed.</para>
    /// </summary>
    public const int MaxTilesForInterval = 256;

    // pow(n, 0.315477776) for n = 1..256, scaled by 2^24 and rounded.
    //
    // A TABLE RATHER THAN Math.Pow, because this number decides when an egg
    // appears and eggs are hashed. IEEE-754 pow is not guaranteed bit-identical
    // across runtimes, and the truncation below sits on a knife edge: a 3x3
    // hatchery computes 7.99977 and takes 7, so a difference in the eighth
    // decimal place would hand one machine 8 and another 7 and diverge the run.
    // HatcheryTests regenerates this from Math.Pow and compares, so the table is
    // provably the formula while the simulation never touches a float.
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

    /// <summary>
    /// How long this hatchery waits between eggs, in <b>game seconds</b>.
    ///
    /// <para>Read from the original (<c>hatchery.md</c> §4), and the formula is
    /// exact:</para>
    ///
    /// <code>
    ///   interval = (int)( ChickenGenerationTimePerHatchery / pow(tiles, 0.315477776) ) * V19
    /// </code>
    ///
    /// <para>The truncation happens <b>before</b> the multiply by ticks-per-game-second,
    /// so this returns the truncated whole seconds and the caller converts —
    /// which is what keeps the arithmetic right while
    /// <see cref="SimulationTiming.TicksPerSecond"/> is still a placeholder
    /// (Q1).</para>
    ///
    /// <para><b>A bigger hatchery lays faster, on a curve.</b> With the shipped
    /// 16: one tile every 16 seconds, 3x3 every <b>7</b>, 5x5 every 5, 8x8 every
    /// 4.</para>
    ///
    /// <para><b>The 3x3 case is a quirk to reproduce rather than round away.</b>
    /// The exponent is typed rather than derived — it is not
    /// <c>log 2 / log 9</c> — and it lands at <c>pow(9, e) = 2.00006</c>, a hair
    /// <i>above</i> two. So 16/2.00006 is 7.99977 and the truncation takes
    /// <b>7</b>, where a designer thinking "nine tiles should be exactly half of
    /// one" would have expected 8. The smallest hatchery is one second faster
    /// than the round number, for ever, because of the sixth decimal place
    /// (Q134). <c>hatchery.md</c> §4's table said 8 for a day; its own header
    /// said "truncated".</para>
    /// </summary>
    public static int LayIntervalSeconds(GameDatabase database, RoomInstance hatchery) =>
        LayIntervalSecondsForTiles(database, hatchery.TileCount);

    /// <summary>
    /// <see cref="LayIntervalSeconds"/> by tile count alone — the room is only
    /// ever consulted for its size, and a rule that takes the number it uses is
    /// a rule a test can drive across the whole table.
    /// </summary>
    public static int LayIntervalSecondsForTiles(GameDatabase database, int tiles)
    {
        int clamped = Math.Clamp(tiles, 1, MaxTilesForInterval);
        int seconds = database[VariableTypeId.ChickenGenerationTimePerHatchery];

        return (int)(((long)seconds << 24) / PowScaled[clamped - 1]);
    }

    public static int FreeRangeCap(GameDatabase database) =>
        database[VariableTypeId.MaxFreeRangeChickensPerPlayer];
}
