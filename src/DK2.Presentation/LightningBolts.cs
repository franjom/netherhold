// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>The private presentation CRT stream; never a simulation random source.</summary>
public sealed class LightningRandom(uint seed)
{
    private uint _seed = seed;
    public int Next15()
    {
        _seed = unchecked(_seed * 0x343FD + 0x269EC3);
        return (int)((_seed >> 16) & 0x7FFF);
    }
    internal float Symmetric()
    {
        return (Next15() - 16384) / 16384f;
    }
}

/// <summary>One thinned ribbon path in view-space tiles, with subdivision diagnostics.</summary>
public sealed record LightningPath(IReadOnlyList<Vector3> Nodes, int SubdivisionNodeCount,
    float SubdivisionMaximumSegmentLength);

public enum LightningAmplitude { Shot = 192, Creature = 256 }

/// <summary>A reused bolt path, valid until the next BuildInto call.</summary>
public sealed class LightningPathBuffer
{
    internal List<Vector3> Points { get; } = new List<Vector3>();
    public IReadOnlyList<Vector3> Nodes
    {
        get
        {
            return Points;
        }
    }

    public int SubdivisionNodeCount { get; internal set; }
    public float SubdivisionMaximumSegmentLength { get; internal set; }
}

public sealed class LightningBolts(uint seed)
{
    private readonly LightningRandom _random = new LightningRandom(seed);
    private readonly List<LightningPathBuffer> _pool = new List<LightningPathBuffer>();
    private readonly List<LightningPathBuffer> _paths = new List<LightningPathBuffer>();
    private int _usedPaths;
    public const float SegmentLimit = .06f;
    public const float RibbonHalfWidth = .03f;

    /// <summary>The returned paths and their nodes are reused by the next call.</summary>
    public IReadOnlyList<LightningPathBuffer> BuildInto(Vector3 start, Vector3 end,
        LightningAmplitude amplitude = LightningAmplitude.Shot)
    {
        if (!Finite(start) || !Finite(end))
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        if (amplitude is not (LightningAmplitude.Shot or LightningAmplitude.Creature))
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude));
        }

        _paths.Clear();
        _usedPaths = 0;
        BoltInto(start, end, amplitude);
        return _paths;
    }

    private void BoltInto(Vector3 start, Vector3 end, LightningAmplitude amplitude)
    {
        if (_usedPaths == _pool.Count)
        {
            _pool.Add(new LightningPathBuffer());
        }

        LightningPathBuffer path = _pool[_usedPaths++];
        List<Vector3> nodes = path.Points;
        nodes.Clear();
        nodes.Add(start);
        path.SubdivisionMaximumSegmentLength = 0;
        SubdivideInto(path, start, end, end, Vector3.Distance(start, end) / 4, amplitude);
        path.SubdivisionNodeCount = nodes.Count;
        while (nodes.Count > 32)
        {
            Vector3 last = nodes[nodes.Count - 1];
            int count = 1;
            for (int i = 2; i < nodes.Count - 1; i += 2)
            {
                nodes[count++] = nodes[i];
            }

            nodes[count++] = last;
            nodes.RemoveRange(count, nodes.Count - count);
        }
        _paths.Add(path);
    }

    private void SubdivideInto(LightningPathBuffer path, Vector3 left, Vector3 right,
        Vector3 end, float forkScale, LightningAmplitude amplitude)
    {
        float length = Vector3.Distance(left, right);
        if (length <= SegmentLimit)
        {
            path.SubdivisionMaximumSegmentLength = Math.Max(path.SubdivisionMaximumSegmentLength, length);
            path.Points.Add(right);
            return;
        }
        Vector3 middle = (left + right) * .5f + Jitter((int)amplitude * length / 2048);
        SubdivideInto(path, left, middle, end, forkScale, amplitude);
        SubdivideInto(path, middle, right, end, forkScale, amplitude);
        if (forkScale > .2f && _random.Next15() < 256 / forkScale)
        {
            BoltInto(middle, end + Jitter(forkScale), amplitude);
        }
    }

    public IReadOnlyList<LightningPath> Build(Vector3 start, Vector3 end, LightningAmplitude amplitude = LightningAmplitude.Shot)
    {
        if (!Finite(start) || !Finite(end))
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        if (amplitude is not (LightningAmplitude.Shot or LightningAmplitude.Creature))
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude));
        }

        var paths = new List<LightningPath>();
        Bolt(start, end);
        return paths;

        void Bolt(Vector3 a, Vector3 b)
        {
            float forkScale = Vector3.Distance(a, b) / 4;
            var nodes = new List<Vector3> { a };
            float longest = 0;
            Subdivide(a, b);
            int count = nodes.Count;
            while (nodes.Count > 32)
            {
                var thinned = new List<Vector3> { nodes[0] };
                for (int i = 2; i < nodes.Count - 1; i += 2)
                {
                    thinned.Add(nodes[i]);
                }

                thinned.Add(nodes[^1]);
                nodes = thinned;
            }
            paths.Add(new LightningPath(nodes, count, longest));

            void Subdivide(Vector3 left, Vector3 right)
            {
                float length = Vector3.Distance(left, right);
                if (length <= SegmentLimit)
                {
                    longest = Math.Max(longest, length);
                    nodes.Add(right);
                    return;
                }
                Vector3 middle = (left + right) * .5f + Jitter((int)amplitude * length / 2048);
                Subdivide(left, middle);
                Subdivide(middle, right);
                if (forkScale > .2f && _random.Next15() < 256 / forkScale)
                {
                    Bolt(middle, b + Jitter(forkScale));
                }
            }
        }
    }

    private Vector3 Jitter(float scale)
    {
        return new Vector3(_random.Symmetric() * scale,
        _random.Symmetric() * scale, _random.Symmetric() * scale);
    }

    private static bool Finite(Vector3 point)
    {
        return Finite(point.X) && Finite(point.Y) && Finite(point.Z);
    }

    private static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
