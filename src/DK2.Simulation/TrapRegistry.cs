// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The world's standing traps in their order and the last trap id given.
/// <see cref="WorldState"/> owns one and gives each new trap its tile link.</summary>
internal sealed class TrapRegistry
{
    private readonly List<TrapState> _traps = [];

    public IReadOnlyList<TrapState> All => _traps;

    public List<TrapState> Raw => _traps;

    /// <summary>The last trap id given; a snapshot restores it so ids stay unique.</summary>
    public int NextId { get; set; }

    public void Add(TrapState trap)
    {
        trap.Id = ++NextId;
        _traps.Add(trap);
    }

    public void Remove(TrapState trap) => _traps.Remove(trap);

    public TrapState? ById(int id)
    {
        foreach (TrapState trap in _traps)
            if (trap.Id == id) return trap;
        return null;
    }

    public TrapState? At(int x, int y, PlayerId owner)
    {
        foreach (TrapState trap in _traps)
            if (trap.X == x && trap.Y == y && trap.PlayerId == owner) return trap;
        return null;
    }
}
