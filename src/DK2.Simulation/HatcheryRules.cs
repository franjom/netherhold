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
/// global is read from 223 places in the binary as both multiplier and divisor,
/// written by exactly one instruction, and absent from all 1236 files in the
/// install. Without it the interval has a shape and no unit, so <b>there is no
/// clock here and nothing drives laying</b>. Q132.</para>
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
    /// <para>Two different objects. Only the Chicken carries
    /// <see cref="ObjectFlags.TypeFood"/>, and only the Chicken has a creature's
    /// <c>HpFromChicken</c> against it (375 for a Goblin). So something turns an
    /// egg into a chicken and this project had never accounted for it; the
    /// objects' differing <c>StartState</c> — 0 for the egg, 10 for the
    /// chicken — says both have a state machine to do it in. <b>Q133</b>, and
    /// the owner can settle it by looking at a hatchery.</para>
    /// </summary>
    public const byte EggObjectId = 47;

    /// <summary>
    /// The Chicken itself, object <b>9</b>. Nothing here creates one yet — see
    /// <see cref="EggObjectId"/>.
    /// </summary>
    public const byte ChickenObjectId = 9;

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
    /// <para><b>Nothing calls this on a clock</b>, because the interval's unit is
    /// unresolved (see the type doc, and Q132). Nor is the per-player free-range
    /// cap applied: DK2 gates the coop path on a counter it keeps on the player
    /// against <c>MaxFreeRangeChickensPerPlayer</c> (12), and what that counter
    /// counts — loose chickens only, or everything a hatchery has made — is
    /// UNKNOWN. Guessing it would silently throttle a room.</para>
    /// </summary>
    public static ObjectState? TryLayEgg(
        WorldState world, GameDatabase database, RoomInstance hatchery, SimRandom rng)
    {
        if (rng is null) throw new ArgumentNullException(nameof(rng));

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
    /// the original really does check it before laying at a coop.
    ///
    /// <para>Exposed but not enforced; see <see cref="TryLayEgg"/> for why.</para>
    /// </summary>
    public static int FreeRangeCap(GameDatabase database) =>
        database[VariableTypeId.MaxFreeRangeChickensPerPlayer];
}
