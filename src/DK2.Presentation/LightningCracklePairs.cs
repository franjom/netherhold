// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

public readonly record struct LightningControlPoint(uint Id, Vector3 Position);
public readonly record struct LightningSegment(Vector3 Start, Vector3 End);

public sealed class LightningCracklePairs(IRandomAlgorithm random)
{
    public IEnumerable<LightningSegment> Choose(IReadOnlyList<LightningControlPoint> controlPoints)
    {
        LightningControlPoint[] points = controlPoints.Where(p => p.Id < 2000).ToArray();
        for (int i = points.Length - 1; i > 0; i--)
        {
            int previous = i;
            for (int j = i - 1; j >= 0; j--)
                if (unchecked(points[previous].Id - points[j].Id) > 1)
                {
                    if (random.NextInt(8 * i) == 1) yield return new(points[j].Position, points[i].Position);
                }
                else previous = j;
        }
    }
}
