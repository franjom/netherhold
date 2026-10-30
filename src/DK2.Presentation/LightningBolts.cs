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
    internal float Symmetric() => (Next15() - 16384) / 16384f;
}

/// <summary>One thinned ribbon path in view-space tiles, with subdivision diagnostics.</summary>
public sealed record LightningPath(IReadOnlyList<Vector3> Nodes, int SubdivisionNodeCount,
    float SubdivisionMaximumSegmentLength);

public enum LightningAmplitude { Shot = 192, Creature = 256 }

public sealed class LightningBolts(uint seed)
{
    private readonly LightningRandom _random = new(seed);
    public const float SegmentLimit = .06f;
    public const float RibbonHalfWidth = .03f;

    public IReadOnlyList<LightningPath> Build(Vector3 start, Vector3 end, LightningAmplitude amplitude = LightningAmplitude.Shot)
    {
        if (!Finite(start) || !Finite(end)) throw new ArgumentOutOfRangeException(nameof(end));
        if (amplitude is not (LightningAmplitude.Shot or LightningAmplitude.Creature))
            throw new ArgumentOutOfRangeException(nameof(amplitude));
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
                for (int i = 2; i < nodes.Count - 1; i += 2) thinned.Add(nodes[i]);
                thinned.Add(nodes[^1]);
                nodes = thinned;
            }
            paths.Add(new(nodes, count, longest));

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
                    Bolt(middle, b + Jitter(forkScale));
            }
        }
    }

    private Vector3 Jitter(float scale) => new(_random.Symmetric() * scale,
        _random.Symmetric() * scale, _random.Symmetric() * scale);
    private static bool Finite(Vector3 point) => Finite(point.X) && Finite(point.Y) && Finite(point.Z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
