// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
namespace DK2.Presentation;
/// <summary>Scratch storage for the nine-point view volume and its two height-plane intersections.</summary>
internal sealed class ClassicVisibilityPolygonBuffer
{
    internal readonly List<Vector3> Points = new(96), Crossings = new(96);
    internal readonly List<TilePos> Boundary = new(96);
    internal readonly TilePos[] Tiles = new TilePos[96];
}
