// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.ObjectModel;
using DK2.Data.Model;

namespace DK2.AI.Players;

/// <summary>One room of one square side, as the simulation's rules make it (stage 3 plan, Task 3a):
/// <paramref name="Attraction"/> is what it adds to the portal budget of the creatures it caps,
/// <paramref name="Gold"/> what it stores.</summary>
public readonly record struct RoomSizeFacts(int Side, int FloorSlots, int Attraction, int Gold);

/// <summary>What a keeper can know about one room type from the game's definitions: its price,
/// its facts at each measured side, and whether its attraction caps any creature type.</summary>
public sealed record RoomFacts
{
    public RoomFacts(RoomTypeId room, int costPerTile, IEnumerable<RoomSizeFacts> sizes, bool capsCreatures)
    {
        Room = room;
        CostPerTile = costPerTile;
        Sizes = new ReadOnlyCollection<RoomSizeFacts>([.. (sizes ?? throw new ArgumentNullException(nameof(sizes))).OrderBy(s => s.Side)]);
        CapsCreatures = capsCreatures;
    }

    public RoomTypeId Room { get; }
    public int CostPerTile { get; }
    public IReadOnlyList<RoomSizeFacts> Sizes { get; }
    public bool CapsCreatures { get; }

    /// <summary>The smallest measured side at which the room does anything a keeper builds it for: draws
    /// a creature it caps, or stores gold. Null for a room that works by area alone (any size will do).</summary>
    public int? SmallestUsefulSide
    {
        get
        {
            foreach (RoomSizeFacts size in Sizes)
            {
                if (CapsCreatures ? size.Attraction > 0 : size.Gold > 0)
                {
                    return size.Side;
                }
            }

            return null;
        }
    }

    /// <summary>The smallest measured side with at least <paramref name="slots"/> floor slots, or null.</summary>
    public int? SideForSlots(int slots)
    {
        foreach (RoomSizeFacts size in Sizes)
        {
            if (size.FloorSlots >= slots)
            {
                return size.Side;
            }
        }

        return null;
    }

    /// <summary>The smallest measured side whose attraction reaches <paramref name="attraction"/>, or null.</summary>
    public int? SideFor(int attraction)
    {
        foreach (RoomSizeFacts size in Sizes)
        {
            if (size.Attraction >= attraction)
            {
                return size.Side;
            }
        }

        return null;
    }
}

/// <summary>The room facts the campaign player reads (stage 3 plan, Task 3a): definition data,
/// generated from the simulation's own rules by <c>DK2.Harness keeper-knowledge</c> and loaded
/// before tick 0, like <see cref="CampaignPlayerOptions.WorkerTypes"/>. Read-only during play.</summary>
public sealed class KeeperKnowledge
{
    public static readonly KeeperKnowledge None = new([]);

    private readonly Dictionary<RoomTypeId, RoomFacts> _rooms;

    public KeeperKnowledge(IEnumerable<RoomFacts> rooms)
    {
        _rooms = [];
        foreach (RoomFacts room in rooms ?? throw new ArgumentNullException(nameof(rooms)))
        {
            if (!_rooms.TryAdd(room.Room, room))
            {
                throw new ArgumentException($"{room.Room} is described twice.", nameof(rooms));
            }
        }
    }

    public RoomFacts? Room(RoomTypeId room) => _rooms.TryGetValue(room, out RoomFacts? facts) ? facts : null;

    public int Count => _rooms.Count;
}
