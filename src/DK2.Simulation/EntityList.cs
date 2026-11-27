// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections;

namespace DK2.Simulation;

/// <summary>A read-only view of one of the world's entity lists whose <c>foreach</c> uses the
/// list's own struct enumerator: through <see cref="IReadOnlyList{T}"/> every loop boxed one, and
/// the simulation loops over the creatures and the objects hundreds of times a tick (ADR-0696).
/// Assignable to <see cref="IReadOnlyList{T}"/> where a caller wants the interface.</summary>
public readonly struct EntityList<T> : IReadOnlyList<T>
{
    private readonly List<T> _items;

    public EntityList(List<T> items)
    {
        _items = items;
    }

    public int Count
    {
        get
        {
            return _items.Count;
        }
    }

    public T this[int index]
    {
        get
        {
            return _items[index];
        }
    }

    public List<T>.Enumerator GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _items.GetEnumerator();
    }
}
