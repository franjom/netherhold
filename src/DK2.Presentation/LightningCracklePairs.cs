// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

public readonly record struct LightningControlPoint(uint Id, Vector3 Position)
{
    /// <summary>A point in the renderer's world frame (Y up) as DK2's world point, (x, −z, y).</summary>
    public static LightningControlPoint FromRenderWorld(uint id, Vector3 render)
    {
        return new LightningControlPoint(id, new Vector3(render.X, -render.Z, render.Y));
    }
}
public readonly record struct LightningSegment(Vector3 Start, Vector3 End);

public sealed class LightningCracklePairs
{
    private readonly IRandomAlgorithm _random;

    public LightningCracklePairs(IRandomAlgorithm random)
    {
        _random = random;
    }

    private readonly List<LightningControlPoint> _points = new List<LightningControlPoint>();

    public void ChooseInto(IReadOnlyList<LightningControlPoint> controlPoints, List<LightningSegment> output)
    {
        output.Clear();
        _points.Clear();
        for (int i = 0; i < controlPoints.Count; i++)
        {
            if (controlPoints[i].Id < 2000)
            {
                _points.Add(controlPoints[i]);
            }
        }

        for (int i = _points.Count - 1; i > 0; i--)
        {
            int previous = i;
            for (int j = i - 1; j >= 0; j--)
            {
                if (unchecked(_points[previous].Id - _points[j].Id) > 1)
                {
                    if (_random.NextInt(8 * i) == 1)
                    {
                        output.Add(new LightningSegment(_points[j].Position, _points[i].Position));
                    }
                }
                else
                {
                    previous = j;
                }
            }
        }
    }
    public IEnumerable<LightningSegment> Choose(IReadOnlyList<LightningControlPoint> controlPoints)
    {
        LightningControlPoint[] points = [.. controlPoints.Where(p => p.Id < 2000)];
        for (int i = points.Length - 1; i > 0; i--)
        {
            int previous = i;
            for (int j = i - 1; j >= 0; j--)
            {
                if (unchecked(points[previous].Id - points[j].Id) > 1)
                {
                    if (_random.NextInt(8 * i) == 1)
                    {
                        yield return new LightningSegment(points[j].Position, points[i].Position);
                    }
                }
                else
                {
                    previous = j;
                }
            }
        }
    }
}
