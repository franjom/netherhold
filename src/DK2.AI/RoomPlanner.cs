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
    /// room-shaped dig) is not reproduced: this codebase has no research state yet.
    /// </summary>
    public static byte? ChooseRoom(KeeperView view, KeeperAIParameters p, Func<byte, bool> isAvailable)
    {
        foreach (byte id in Candidates(p))
        {
            if (id == 0 || Array.IndexOf(NeverBuilt, id) >= 0) continue;
            if (!view.Database.Rooms.TryGetValue(id, out RoomDef? def)) continue;
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
    /// Where the site search for <paramref name="roomId"/> starts: the ideal
    /// neighbour's centre, unless <c>PlaceRoomsNextToIdeal</c> is set (then a random
    /// own room); with no ideal, or none owned, a random own room avoiding bridges;
    /// with no rooms at all, the heart.
    /// </summary>
    public static TilePos? Origin(KeeperView view, KeeperAIParameters p, byte roomId, SimRandom rng)
    {
        if (!p.PlaceRoomsNextToIdeal && IdealNeighbour(roomId) is RoomTypeId ideal
            && view.OwnRoomOfType((byte)ideal) is RoomInstance neighbour)
            return KeeperView.Centre(neighbour);

        RoomInstance? random = RandomOwnRoom(view, rng, avoidBridges: true);
        if (random is not null) return KeeperView.Centre(random);
        return view.Heart;
    }

    public static RoomInstance? RandomOwnRoom(KeeperView view, SimRandom rng, bool avoidBridges)
    {
        var pool = new List<RoomInstance>();
        foreach (RoomInstance r in view.OwnRooms)
            if (!avoidBridges || !BridgeRules.IsBridge(r.Definition)) pool.Add(r);
        if (pool.Count == 0) return null;
        return pool[rng.NextInt(pool.Count)];
    }

    /// <summary>Rooms with id below 6 go in the defend direction; the rest in the open one, jittered by one step.</summary>
    public static int DirectionFor(byte roomId, KeeperAIState state, SimRandom rng) =>
        roomId < 6 ? state.DefendDirection : (state.OpenDirection + rng.NextInt(3) - 1) & 3;

    /// <summary>
    /// A neutral room of the wanted type the keeper may dig to and claim: inside
    /// the build area, and within <c>NeutralRoomRangeFromHeart</c> of the heart or
    /// <c>NeutralRoomRangeFromClaimedArea × 2 + 3</c> of an own room. The original's
    /// distance metric was not pinned; Chebyshev distance is used here.
    /// </summary>
    public static RoomInstance? ClaimableNeutralRoom(KeeperView view, KeeperAIParameters p, byte roomId, BuildArea area)
    {
        if (view.Heart is not TilePos heart) return null;
        RoomInstance? best = null;
        int bestDist = int.MaxValue;
        int nearOwn = (p.NeutralRoomRangeFromClaimedArea * 2) + 3;

        foreach (RoomInstance r in view.AllRooms)
        {
            if (r.Definition.RoomId != roomId || !KeeperView.IsNeutral(r)) continue;
            TilePos c = KeeperView.Centre(r);
            if (!area.Contains(c)) continue;

            bool inRange = c.ChebyshevDistance(heart) <= p.NeutralRoomRangeFromHeart;
            if (!inRange)
                foreach (RoomInstance own in view.OwnRooms)
                    if (c.ChebyshevDistance(KeeperView.Centre(own)) <= nearOwn) { inRange = true; break; }
            if (!inRange) continue;

            int d = KeeperView.DistanceSquared(c, heart);
            if (d < bestDist)
            {
                bestDist = d;
                best = r;
            }
        }
        return best;
    }
}
