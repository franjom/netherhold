// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>The world's standing doors in their order, the last door id given and the version a
/// per-tile door cache reads. <see cref="WorldState"/> owns one and gives each new door its tile link.</summary>
internal sealed class DoorRegistry
{
    private readonly List<DoorState> _doors = [];

    public IReadOnlyList<DoorState> All => _doors;

    public List<DoorState> Raw => _doors;

    /// <summary>The last door id given; a snapshot restores it so ids stay unique.</summary>
    public int NextId { get; set; }

    /// <summary>Bumped by every add and removal. Derived, not state: neither hashed nor saved.</summary>
    public int Version { get; private set; }

    public void Add(DoorState door)
    {
        door.Id = ++NextId;
        _doors.Add(door);
        Version++;
    }

    public void Remove(DoorState door)
    {
        if (_doors.Remove(door)) Version++;
    }

    public DoorState? ById(int id)
    {
        foreach (DoorState door in _doors)
            if (door.Id == id) return door;
        return null;
    }

    public DoorState? At(int x, int y)
    {
        foreach (DoorState door in _doors)
            if (door.X == x && door.Y == y) return door;
        return null;
    }
}
