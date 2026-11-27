// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;

namespace DK2.Presentation;

/// <summary>DK2's shadow-source rule for one caster (research/creature-shadows.md, <i>Shadow selection</i>): the torches
/// within <see cref="ClassicMeshShadow.MaximumDistance"/> (3D), the <see cref="ClassicMeshShadow.MaximumSources"/> nearest
/// kept (equal distances by the lower key), the fade distance the next one's distance when there is one and the range
/// otherwise. Offer every torch after <see cref="Begin"/>; allocation-free.</summary>
public sealed class ClassicShadowSourceSelector
{
    private const int Kept = ClassicMeshShadow.MaximumSources + 1;
    private readonly Vector3[] _nearest = new Vector3[Kept];
    private readonly float[] _distances = new float[Kept];
    private readonly int[] _keys = new int[Kept];
    private Vector3 _centre;
    private int _count;

    /// <summary>The selected sources, at most <see cref="ClassicMeshShadow.MaximumSources"/>.</summary>
    public int Count
    {
        get
        {
            return Math.Min(_count, ClassicMeshShadow.MaximumSources);
        }
    }

    /// <summary>The fade distance every selected source's strength uses.</summary>
    public float FadeDistance
    {
        get
        {
            return _count > ClassicMeshShadow.MaximumSources
        ? MathF.Sqrt(_distances[ClassicMeshShadow.MaximumSources]) : ClassicMeshShadow.MaximumDistance;
        }
    }

    public Vector3 Source(int index)
    {
        return _nearest[index];
    }

    public int SourceKey(int index)
    {
        return _keys[index];
    }

    public void Begin(Vector3 casterCentre)
    {
        _centre = casterCentre;
        _count = 0;
    }

    public void Offer(int key, Vector3 position)
    {
        float squared = Vector3.DistanceSquared(_centre, position);
        if (squared >= ClassicMeshShadow.MaximumDistance * ClassicMeshShadow.MaximumDistance)
        {
            return;
        }

        int slot = 0;
        while (slot < _count && (_distances[slot] < squared || (_distances[slot] == squared && _keys[slot] < key)))
        {
            slot++;
        }

        if (slot >= Kept)
        {
            return;
        }

        for (int j = Math.Min(_count, Kept - 1); j > slot; j--)
        {
            _distances[j] = _distances[j - 1];
            _nearest[j] = _nearest[j - 1];
            _keys[j] = _keys[j - 1];
        }
        _distances[slot] = squared;
        _nearest[slot] = position;
        _keys[slot] = key;
        _count = Math.Min(_count + 1, Kept);
    }

    /// <summary>The fade distance when <paramref name="key"/> is a selected source, else zero (it casts no shadow).</summary>
    public float FadeFor(int key)
    {
        for (int i = 0; i < Count; i++)
        {
            if (_keys[i] == key)
            {
                return FadeDistance;
            }
        }

        return 0f;
    }
}

/// <summary>Which torches a modern renderer gives a shadow map (not DK2's: it has none): the
/// <paramref name="chosen"/>.Length nearest the view's focus horizontally, within <paramref name="reach"/>, equal distances
/// by the lower key, nearest first. A torch in <c>held</c> (the last choice) counts <c>hysteresis</c> tiles nearer, so a
/// focus moving between two torches does not swap their maps back and forth. Allocation-free.</summary>
public static class ClassicTorchShadowSlots
{
    /// <summary>The adapters' cap: each shadowed torch renders its casters again and every pixel near it samples its
    /// map, so the two nearest (owner, 2026-11-18).</summary>
    public const int MaximumTorches = 2;

    public static int Choose(Vector3 focus, ReadOnlySpan<int> keys, ReadOnlySpan<Vector3> positions, float reach,
#pragma warning disable IDE0301 // Preserve the existing empty-span lowering of the forwarding overload.
        Span<int> chosen)
    {
        return Choose(focus, keys, positions, reach, ReadOnlySpan<int>.Empty, 0f, chosen);
    }
#pragma warning restore IDE0301

    public static int Choose(Vector3 focus, ReadOnlySpan<int> keys, ReadOnlySpan<Vector3> positions, float reach,
        ReadOnlySpan<int> held, float hysteresis, Span<int> chosen)
    {
        if (keys.Length != positions.Length)
        {
            throw new ArgumentException("One key per torch position.", nameof(keys));
        }

        if (hysteresis < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hysteresis));
        }

        int count = 0;
        for (int i = 0; i < positions.Length; i++)
        {
            float distance = Distance(focus, keys[i], positions[i], held, hysteresis);
            if (!(distance <= reach))
            {
                continue;
            }

            int slot = 0;
            while (slot < count && Before(focus, keys, positions, held, hysteresis, chosen[slot], distance, keys[i]))
            {
                slot++;
            }

            if (slot >= chosen.Length)
            {
                continue;
            }

            for (int j = Math.Min(count, chosen.Length - 1); j > slot; j--)
            {
                chosen[j] = chosen[j - 1];
            }

            chosen[slot] = i;
            count = Math.Min(count + 1, chosen.Length);
        }
        return count;
    }

    private static bool Before(Vector3 focus, ReadOnlySpan<int> keys, ReadOnlySpan<Vector3> positions,
        ReadOnlySpan<int> held, float hysteresis, int index, float distance, int key)
    {
        float other = Distance(focus, keys[index], positions[index], held, hysteresis);
        return other < distance || (other == distance && keys[index] < key);
    }

    /// <summary>The horizontal distance, less the hysteresis for a held torch (so it also reaches that much farther).</summary>
    private static float Distance(Vector3 focus, int key, Vector3 position, ReadOnlySpan<int> held, float hysteresis)
    {
        float x = focus.X - position.X, z = focus.Z - position.Z;
        float distance = MathF.Sqrt((x * x) + (z * z));
        return held.IndexOf(key) >= 0 ? distance - hysteresis : distance;
    }
}
