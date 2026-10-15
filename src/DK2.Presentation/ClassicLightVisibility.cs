// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

public readonly record struct ClassicVisibilityHeightRange(float Minimum, float Maximum)
{
    public static readonly ClassicVisibilityHeightRange Native = new(-9, 0);
}

/// <summary>Native clipped camera volume and tile scan conversion; classic-light-visibility.md owns evidence.</summary>
public static class ClassicLightVisibility
{
    public static TilePos[] BuildPolygon(ClassicCameraPose pose, CameraViewport viewport,
        float nativeViewDistance, bool scaleViewVolume = false, ClassicVisibilityHeightRange? heights = null)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewport));
        if (!(nativeViewDistance > 0) || float.IsInfinity(nativeViewDistance))
            throw new ArgumentOutOfRangeException(nameof(nativeViewDistance));
        if (!(pose.VerticalFieldOfView > 0 && pose.VerticalFieldOfView < 180))
            throw new ArgumentOutOfRangeException(nameof(pose));
        ClassicVisibilityHeightRange slab = heights ?? ClassicVisibilityHeightRange.Native;
        if (!(slab.Minimum <= slab.Maximum)) throw new ArgumentOutOfRangeException(nameof(heights));

        float focal = (float)(viewport.Height / (2 * Math.Tan(pose.VerticalFieldOfView * Math.PI / 360)));
        float halfX = viewport.Width * 0.5f / focal;
        float halfY = viewport.Height * 0.5f / focal;
        float distance = nativeViewDistance * (scaleViewVolume ? (float)(2.0 / (halfX + 2.0)) : 1);
        Vector3 position = ToNative(pose.Position);
        Vector3 right = ToNative(Vector3.Cross(pose.Up, pose.Forward));
        Vector3 down = ToNative(-pose.Up);
        Vector3 forward = ToNative(pose.Forward);
        var points = new List<Vector3>(9);
        void Prepend(float x, float y, float z) => points.Insert(0,
            position + right * x + down * y + forward * z);
        Prepend(-1, -1, -1);
        Prepend(1, -1, -1);
        Prepend(-1, 1, -1);
        Prepend(1, 1, -1);
        Prepend(-halfX * distance - 1, -halfY * distance - 1, distance);
        Prepend(halfX * distance + 1, -halfY * distance - 1, distance);
        Prepend(-halfX * distance - 1, halfY * distance + 1, distance);
        Prepend(halfX * distance + 1, halfY * distance + 1, distance);
        Prepend(0, 0, nativeViewDistance);

        var crossings = new List<Vector3>();
        AddCrossings(points, slab.Minimum, crossings);
        AddCrossings(points, slab.Maximum, crossings);
        points.InsertRange(0, crossings);
        points.RemoveAll(p => p.Z < slab.Minimum || p.Z > slab.Maximum);
        for (int i = 0; i < points.Count;)
        {
            bool duplicate = false;
            for (int j = i + 1; j < points.Count; j++)
                if (Math.Abs(points[i].X - points[j].X) < 0.01f
                    && Math.Abs(points[i].Y - points[j].Y) < 0.01f)
                {
                    duplicate = true;
                    break;
                }
            if (duplicate) points.RemoveAt(i);
            else i++;
        }
        return WrapBoundary(points);
    }

    public static TilePos[] EnumerateTiles(ReadOnlySpan<TilePos> polygon)
    {
        if (polygon.Length == 0) return Array.Empty<TilePos>();
        int minimum = 0, maximumY = polygon[0].Y;
        for (int i = 1; i < polygon.Length; i++)
        {
            if (polygon[i].Y < polygon[minimum].Y) minimum = i;
            maximumY = Math.Max(maximumY, polygon[i].Y);
        }
        var tiles = new List<TilePos>();
        int left = minimum, right = minimum;
        int leftX = 0, rightX = 0, leftStep = 0, rightStep = 0;
        for (int y = polygon[minimum].Y; y < maximumY; y++)
        {
            AdvanceEdge(polygon, y, -1, ref left, ref leftX, ref leftStep);
            AdvanceEdge(polygon, y, 1, ref right, ref rightX, ref rightStep);
            int first = Math.Min(leftX >> 16, rightX >> 16);
            int last = Math.Max(leftX >> 16, rightX >> 16);
            for (int x = first; x <= last; x++) tiles.Add(new TilePos(x, y));
            leftX = unchecked(leftX + leftStep);
            rightX = unchecked(rightX + rightStep);
        }
        return tiles.ToArray();
    }

    private static void AdvanceEdge(ReadOnlySpan<TilePos> polygon, int y, int direction,
        ref int end, ref int x, ref int step)
    {
        if (polygon[end].Y > y) return;
        int start;
        do
        {
            start = end;
            end = (end + direction + polygon.Length) % polygon.Length;
        } while (polygon[end].Y <= y);
        x = unchecked(polygon[start].X << 16);
        step = unchecked((polygon[end].X << 16) - x) / (polygon[end].Y - y);
    }

    private static void AddCrossings(List<Vector3> points, float height, List<Vector3> crossings)
    {
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
            {
                Vector3 a = points[i], b = points[j];
                if (!((a.Z < height && height < b.Z) || (b.Z < height && height < a.Z))) continue;
                float ratio = (1f / (b.Z - a.Z)) * (height - a.Z);
                Vector3 point = a + (b - a) * ratio;
                crossings.Add(new Vector3(point.X, point.Y, height));
            }
    }

    private static TilePos[] WrapBoundary(List<Vector3> points)
    {
        if (points.Count == 0) return Array.Empty<TilePos>();
        int start = 0;
        for (int i = 1; i < points.Count; i++)
            if (points[i].Y < points[start].Y) start = i;
        var polygon = new List<TilePos> { ToTile(points[start]) };
        int current = start;
        float previousAngle = 0;
        while (true)
        {
            int next = start;
            float smallest = 1000, nextAngle = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (i == current) continue;
                double dx = (double)points[i].X - points[current].X;
                double dy = (double)points[i].Y - points[current].Y;
                float angle = dy == 0 && dx >= 0 ? 0 : (float)(Math.Atan2(-dy, -dx) + (float)Math.PI);
                float difference = angle - previousAngle;
                if (difference >= 0 && difference < smallest)
                {
                    next = i;
                    smallest = difference;
                    nextAngle = angle;
                }
            }
            if (next == start) return polygon.ToArray();
            polygon.Add(ToTile(points[next]));
            current = next;
            previousAngle = nextAngle;
        }
    }

    private static TilePos ToTile(Vector3 point) => new(ToTileCoordinate(point.X), ToTileCoordinate(point.Y));

    // Native x87 keeps both subtractions extended until the final single-precision store.
    private static int ToTileCoordinate(float value) =>
        (BitConverter.SingleToInt32Bits((float)((double)value - 0.49998998641967773 + 12582912.0))
            & 0x7fffff) - 0x400000;

    private static Vector3 ToNative(Vector3 unity) => new(unity.X, -unity.Z, -unity.Y);
}
