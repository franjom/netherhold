// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Pathfinding;

/// <summary>A binary min-heap of tile indices keyed on (cost, tile index). The second key is what
/// makes the choice between equally cheap tiles reproducible, and is the tie order every search in
/// this assembly shares, so two searches that pop the same tiles pop them in the same order.</summary>
internal sealed class TileHeap
{
    private readonly List<int> _tile = [];
    private readonly List<int> _key = [];

    public int Count => _tile.Count;

    public void Clear()
    {
        _tile.Clear();
        _key.Clear();
    }

    public void Push(int tile, int key)
    {
        _tile.Add(tile);
        _key.Add(key);

        int child = _tile.Count - 1;
        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (!Less(child, parent)) break;
            Swap(child, parent);
            child = parent;
        }
    }

    public int Pop()
    {
        int result = _tile[0];
        int last = _tile.Count - 1;

        _tile[0] = _tile[last];
        _key[0] = _key[last];
        _tile.RemoveAt(last);
        _key.RemoveAt(last);

        int parent = 0;
        while (true)
        {
            int left = (2 * parent) + 1;
            if (left >= _tile.Count) break;

            int right = left + 1;
            int best = right < _tile.Count && Less(right, left) ? right : left;
            if (!Less(best, parent)) break;

            Swap(best, parent);
            parent = best;
        }

        return result;
    }

    private bool Less(int a, int b) =>
        _key[a] != _key[b] ? _key[a] < _key[b] : _tile[a] < _tile[b];

    private void Swap(int a, int b)
    {
        (_tile[a], _tile[b]) = (_tile[b], _tile[a]);
        (_key[a], _key[b]) = (_key[b], _key[a]);
    }
}
