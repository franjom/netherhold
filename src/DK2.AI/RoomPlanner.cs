// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI;

/// <summary>
/// Which room next, next to what, and in which direction — the original's room
/// planner (docs/KEEPER_AI.md §5.5), STRONGLY INFERRED. Pure functions over the view.
/// </summary>
public static class RoomPlanner
{
    /// <summary>Rooms the original never chooses to build: the bridges, the heart, the guard room, the portal.</summary>
    private static readonly byte[] NeverBuilt =
    [
        (byte)RoomTypeId.WoodenBridge, (byte)RoomTypeId.StoneBridge, (byte)RoomTypeId.HeroStoneBridge,
        (byte)RoomTypeId.DungeonHeart, (byte)RoomTypeId.GuardRoom, (byte)RoomTypeId.Portal,
    ];

    /// <summary>
    /// The first room in the build order — <c>RoomList</c>, then ids 16 to 26 — that
    /// is available to the keeper and not yet owned. Null when every room is
    /// built, which the caller records as <c>AllRoomsBuilt</c>. The original's
    /// second pass (rooms researchable but not yet available, yielding an empty
    /// room-shaped dig) is not reproduced, although availability state 2 exists.
    /// </summary>
    public static byte? ChooseRoom(
        KeeperView view, KeeperAIParameters p, Func<byte, bool> isAvailable,
        byte currentRoomToBuild = 0)
    {
        foreach (byte id in Candidates(p))
        {
            if (id == 0 || Array.IndexOf(NeverBuilt, id) >= 0) continue;
            if (id == (currentRoomToBuild & 0x7F)) continue;
            if (!view.Database.Rooms.TryGetValue((RoomTypeId)id, out RoomDef? def)) continue;
            if (!def.IsBuildable || !isAvailable(id)) continue;
            if (view.OwnRoomOfType(id) is not null) continue;
            return id;
        }
        return null;
    }

    private static IEnumerable<byte> Candidates(KeeperAIParameters p)
    {
        foreach (byte id in p.RoomList) yield return id;
        for (byte id = 16; id <= 26; id++) yield return id;
    }

    /// <summary>The original's ideal-neighbour table: which own room a new room is sited from.</summary>
    public static RoomTypeId? IdealNeighbour(byte roomId) => (RoomTypeId)roomId switch
    {
        RoomTypeId.Treasury => RoomTypeId.DungeonHeart,
        RoomTypeId.Lair => RoomTypeId.Hatchery,
        RoomTypeId.Hatchery => RoomTypeId.Lair,
        RoomTypeId.Library => RoomTypeId.Lair,
        RoomTypeId.TrainingRoom => RoomTypeId.DungeonHeart,
        RoomTypeId.Workshop => RoomTypeId.DungeonHeart,
        RoomTypeId.Prison => RoomTypeId.TrainingRoom,
        RoomTypeId.TortureChamber => RoomTypeId.Prison,
        RoomTypeId.Temple => RoomTypeId.TortureChamber,
        RoomTypeId.Graveyard => RoomTypeId.TortureChamber,
        RoomTypeId.Casino => RoomTypeId.Treasury,
        RoomTypeId.CombatPit => RoomTypeId.Prison,
        _ => null,
    };

    /// <summary>
    /// Where the site search for <paramref name="roomId"/> starts (<c>0x004FC690</c>), always a room's
    /// stored position (<see cref="KeeperView.PositionOf"/>, <c>+0x27</c>), never its bounding-box
    /// middle: with <c>PlaceRoomsNextToIdeal</c> set or no ideal neighbour, a random non-bridge room
    /// (<c>0x004C63D0</c>), else the heart position; otherwise a random room of the ideal type, else a
    /// random non-bridge room, else the heart position. DK2's redraw of a Wooden Bridge after that
    /// last draw cannot fire, since the draw never returns a bridge (ADR-0394).
    /// </summary>
    public static TilePos? Origin(KeeperView view, KeeperAIParameters p, byte roomId, SimRandom rng)
    {
        if (!p.PlaceRoomsNextToIdeal && IdealNeighbour(roomId) is RoomTypeId ideal
            && view.RandomRoomOfType(view.PlayerId, ideal, rng) is KeeperRoomView neighbour)
            return view.PositionOf(neighbour);

        KeeperRoomView? random = view.RandomNonBridgeRoom(view.PlayerId, rng);
        if (random is not null) return view.PositionOf(random);
        return view.Heart;
    }

    /// <summary>The search direction a room prefers (<c>0x004FC690</c>, <c>0x004FCE70</c>): the defend
    /// direction for ids below 6, else the open direction, before any jitter.</summary>
    public static int PreferredDirection(byte roomId, KeeperAIState state) =>
        roomId < 6 ? state.DefendDirection : state.OpenDirection;

    /// <summary><c>0x004FCE70</c> after a failed direction: preferred, then +1, then -1, then +2; null
    /// once +2 has failed, when the caller reseeds or gives up.</summary>
    public static int? NextSearchDirection(int current, int preferred)
    {
        if (current == preferred) return (current + 1) & 3;
        if (current == ((preferred + 1) & 3)) return (current - 2) & 3;
        if (current == ((preferred - 1) & 3)) return (current - 1) & 3;
        return null;
    }

    public static KeeperRoomView? RandomOwnRoom(KeeperView view, SimRandom rng, bool avoidBridges)
    {
        var pool = new List<KeeperRoomView>();
        foreach (KeeperRoomView r in view.OwnRooms)
            if (!avoidBridges || !BridgeRules.IsBridge(r.Definition)) pool.Add(r);
        if (pool.Count == 0) return null;
        return pool[rng.NextInt(pool.Count)];
    }

    /// <summary>Rooms with id below 6 go in the defend direction; the rest in the open one, jittered by one step.</summary>
    public static int DirectionFor(byte roomId, KeeperAIState state, SimRandom rng) =>
        roomId < 6 ? state.DefendDirection : (state.OpenDirection + rng.NextInt(3) - 1) & 3;

    /// <summary>Nearest neutral candidate, then the planner's range and boundary gates.</summary>
    public static KeeperRoomView? ClaimableNeutralRoom(KeeperView view, KeeperAIParameters p, byte roomId, BuildArea area) =>
        ClaimableNeutralRoom(view, p, roomId, area, out _);

    public static KeeperRoomView? ClaimableNeutralRoom(
        KeeperView view, KeeperAIParameters p, byte roomId, BuildArea area, out TilePos origin)
    {
        origin = default;
        if (view.Heart is not TilePos heart) return null;
        bool portal = roomId is (byte)RoomTypeId.Portal or (byte)RoomTypeId.HeroPortal;
        KeeperRoomView? best = null;
        int bestDist = int.MaxValue;
        // 0x004C61C0 on the neutral player's room list, newest first: the first slab (+0x2D) nearest
        // the heart position, the first strict minimum kept, Portal and Hero Portal one type.
        for (int i = view.AllRooms.Count - 1; i >= 0; i--)
        {
            KeeperRoomView r = view.AllRooms[i];
            bool matches = portal
                ? r.Definition.TypeId is RoomTypeId.Portal or RoomTypeId.HeroPortal
                : r.Definition.RoomId == roomId;
            if (!matches || !KeeperView.IsNeutral(r)) continue;
            int distance = KeeperView.DistanceSquared(view.FirstSlabOf(r), heart);
            if (distance < bestDist) { bestDist = distance; best = r; }
        }
        if (best is null) return null;
        // 0x004FC690 reads stored positions (+0x27): the target's, and the own room's nearest it,
        // which is the origin. A portal's range runs from its first slab (+0x2D) to that origin,
        // inclusive; another room's from the heart position to the target, strict (ADR-0394).
        TilePos target = view.PositionOf(best);
        if (!area.Contains(target) || view.NearestOwnRoomByPosition(target) is not KeeperRoomView from) return null;
        origin = view.PositionOf(from);
        int rangeSquared = p.NeutralRoomRangeFromHeart * p.NeutralRoomRangeFromHeart;
        bool inRange = portal
            ? KeeperView.DistanceSquared(view.FirstSlabOf(best), origin) <= rangeSquared
            : KeeperView.DistanceSquared(target, heart) < rangeSquared;
        if (inRange) return best;
        if (OwnLandOnBoundary(view, target, p.NeutralRoomRangeFromClaimedArea) is not TilePos boundary) return null;
        origin = boundary;
        return best;
    }

    public static bool HasOwnLandOnBoundary(KeeperView view, TilePos centre, int claimedRange) =>
        OwnLandOnBoundary(view, centre, claimedRange) is not null;

    /// <summary>0x004FC3B0: left, right, top, bottom; each edge extends one tile beyond its positive end.</summary>
    public static TilePos? OwnLandOnBoundary(KeeperView view, TilePos centre, int claimedRange)
    {
        int radius = claimedRange + 1;
        TilePos? best = null;
        int bestDistance = int.MaxValue;
        for (int edge = 0; edge < 4; edge++)
            for (int offset = -radius; offset <= radius + 1; offset++)
            {
                TilePos tile = edge switch
                {
                    0 => new(centre.X - radius, centre.Y + offset),
                    1 => new(centre.X + radius, centre.Y + offset),
                    2 => new(centre.X + offset, centre.Y - radius),
                    _ => new(centre.X + offset, centre.Y + radius),
                };
                int distance = KeeperView.DistanceSquared(tile, centre);
                if (view.IsMine(tile) && distance < bestDistance)
                {
                    best = tile;
                    bestDistance = distance;
                }
            }
        return best;
    }
}
