// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

/// <summary>The darkening with distance DK2 applies to every vertex in the possession cameras and
/// camera paths, and in no other view (possession-warp.md §4; the projection sinks <c>0x0058ADC0</c>
/// and <c>0x0058AF70</c>, the colour packers <c>0x0058B440</c> and <c>0x0058B680</c>): full brightness
/// out to half the view distance, then straight down to black at the view distance, measured by a
/// diamond-shaped distance, not a round one. It scales the lit colour and moves nothing. An adapter
/// applies it per vertex or per pixel after lighting; alpha is left alone.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-warp.md")]
public static class PossessionFade
{
    /// <summary>The fade word of a vertex that is not faded: every channel keeps its value.</summary>
    public const int Full = 65536;

    /// <summary>The view distance is never more than this many tiles in the possession cameras
    /// (<c>0x00575AD0</c>, the float at <c>0x0066F9A8</c>).</summary>
    public const float PossessionCap = 32f;

    /// <summary>The view distance in tiles (<c>Camera_VisibilityDistance</c>, <c>0x0044D550</c>): the
    /// larger of twice the zoom and the preset's view distance, both in 1/4096 tile, capped in the
    /// possession cameras. Preset 2, with no zoom, gives 12.</summary>
    public static float ViewDistance(int zoom, int presetViewDistance, bool possessionCamera)
    {
        float tiles = Math.Max(2L * zoom, presetViewDistance) / (float)Fp32.One;
        return possessionCamera ? Math.Min(tiles, PossessionCap) : tiles;
    }

    /// <summary>The distance the fade measures: the depth along the view axis, in tiles, plus half
    /// the sum of the sideways and upward offsets as the projection scales them. <paramref name="scaleX"/>
    /// and <paramref name="scaleY"/> are the focal length over half the viewport's width and height
    /// (the first two diagonal terms of a host's projection matrix), so with a host's clip-space
    /// position this is <c>w + (|x| + |y|) / 2</c>. The shape therefore follows the field of view.</summary>
    public static float Distance(float viewX, float viewY, float depth, float scaleX, float scaleY)
    {
        return depth + (0.5f * (MathF.Abs(scaleX * viewX) + MathF.Abs(scaleY * viewY)));
    }

    /// <summary>The share of its lit colour a vertex keeps, 0 to 1.</summary>
    public static float Brightness(float distance, float viewDistance)
    {
        return Math.Clamp(2f * (viewDistance - distance) / viewDistance, 0f, 1f);
    }

    /// <summary>DK2's fade word: <see cref="Full"/> when nothing fades, else 0 to 65535, cut down.</summary>
    public static int Word(float distance, float viewDistance)
    {
        double t = 2.0 * ((double)viewDistance - distance) / viewDistance;
        return t >= 1 ? Full : t < 0 ? 0 : (int)Math.Floor(65536 * t);
    }

    /// <summary>A colour channel after the fade: <c>(channel * word) &gt;&gt; 16</c>.</summary>
    public static byte Shade(byte channel, int word)
    {
        return word >= Full ? channel : (byte)((channel * word) >> 16);
    }

    /// <summary>True when a triangle is not drawn at all: all three of its vertices are past the view distance.</summary>
    public static bool Dropped(float distanceA, float distanceB, float distanceC, float viewDistance)
    {
        return distanceA > viewDistance && distanceB > viewDistance && distanceC > viewDistance;
    }
}
