// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>A tick's commands grouped by type, each group in input order, built the first time a type
/// is asked for in a tick. Ours, not DK2's: a filter over <see cref="InputFrame.All"/> so a system
/// sees only its own commands without every system scanning every command.</summary>
internal sealed class CommandIndex
{
    private readonly Dictionary<Type, object> _byType = [];
    private IReadOnlyList<PlayerCommand> _all = Array.Empty<PlayerCommand>();

    public void Reset(IReadOnlyList<PlayerCommand> all)
    {
        _all = all;
        _byType.Clear();
    }

    public IReadOnlyList<T> Of<T>() where T : PlayerCommand
    {
        if (_all.Count == 0) return Array.Empty<T>();
        if (_byType.TryGetValue(typeof(T), out object? cached)) return (List<T>)cached;

        var matching = new List<T>();
        for (int i = 0; i < _all.Count; i++)
            if (_all[i] is T command) matching.Add(command);
        _byType[typeof(T)] = matching;
        return matching;
    }
}
