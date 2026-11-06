// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>The in-place list update behind <see cref="WorldState.CopyFrom"/> (ADR-0693).</summary>
public static class StateCopy
{
    /// <summary>Brings <paramref name="target"/> to the same keys in the same order as
    /// <paramref name="source"/>, copying into the instance that already holds a key and creating one
    /// only for a key the target lacks, so a reader holding an instance across copies keeps it for as
    /// long as the key lives. Returns whether an instance was added or removed.</summary>
    public static bool Sync<T>(List<T> target, List<T> source) where T : class, IStateCopy<T>
    {
        bool changed = false;
        int i = 0;
        for (; i < source.Count; i++)
        {
            T item = source[i];
            if (i < target.Count && target[i].Key == item.Key)
            {
                target[i].CopyFrom(item);
                continue;
            }
            int found = -1;
            for (int k = i + 1; k < target.Count; k++)
                if (target[k].Key == item.Key) { found = k; break; }
            if (found >= 0)
            {
                target.RemoveRange(i, found - i);
                target[i].CopyFrom(item);
            }
            else
            {
                target.Insert(i, item.Clone());
            }
            changed = true;
        }
        if (target.Count > i)
        {
            target.RemoveRange(i, target.Count - i);
            changed = true;
        }
        return changed;
    }

    /// <summary>A copy of <paramref name="source"/> into <paramref name="target"/>, reusing it when
    /// the lengths agree.</summary>
    public static T[] Array<T>(T[] target, T[] source)
    {
        if (target.Length != source.Length) target = new T[source.Length];
        System.Array.Copy(source, target, source.Length);
        return target;
    }
}
