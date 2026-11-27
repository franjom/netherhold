// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A drawn point in tile units on DK2's axes: the views' <c>PosX</c>, <c>PosY</c> and <c>Height</c>.</summary>
public readonly record struct DrawPoint(float X, float Y, float Height);

/// <summary>Where a creature or an object is drawn between two simulation ticks. Render only: pure functions of the
/// views passed in; nothing here reads or writes the world.</summary>
/// <remarks>Creatures interpolate their position only and are drawn at their last tick's turns
/// (<see cref="MeshOrientation.Mesh"/> of <see cref="CreatureView.MeshYaw"/>); objects also interpolate their turns
/// (<see cref="ObjectTurn"/>). Whether that difference is a rule or an accident is undecided (godot step 2 audit,
/// open points), so both are kept as they were drawn.</remarks>
public static class PoseInterpolator
{
    private const int HalfTurn = 1024;
    private const int TurnMask = 2047;

    /// <summary>The draw clock in game seconds: the tick plus the frame's fraction of it, four ticks a second.</summary>
    public static double DrawSeconds(GameTick tick, float fraction)
    {
        return (tick.Value + (double)Clamp01(fraction)) / SimulationTiming.OriginalTicksPerGameSecond;
    }

    public static float Clamp01(float value)
    {
        if (value < 0f)
        {
            return 0f;
        }

        return value > 1f ? 1f : value;
    }

    /// <summary>A raw <see cref="Fp32"/> coordinate between two ticks, in tile units; the fraction is clamped.</summary>
    public static float Tiles(int previous, int current, float fraction)
    {
        float from = previous;
        return (from + (current - from) * Clamp01(fraction)) / Fp32.One;
    }

    public static DrawPoint Position(in CreatureView pose, float fraction)
    {
        return new DrawPoint(
        Tiles(pose.PreviousPosX, pose.PosX, fraction), Tiles(pose.PreviousPosY, pose.PosY, fraction),
        Tiles(pose.PreviousHeight, pose.Height, fraction));
    }

    public static DrawPoint Position(in ObjectView pose, float fraction)
    {
        return new DrawPoint(
        Tiles(pose.PreviousPosX, pose.PosX, fraction), Tiles(pose.PreviousPosY, pose.PosY, fraction),
        Tiles(pose.PreviousHeight, pose.Height, fraction));
    }

    /// <summary>The signed turn from <paramref name="previous"/> to <paramref name="current"/> the short way round,
    /// −1024 to 1023 in 1/2048ths of a turn.</summary>
    public static int ShortestArc(int previous, int current)
    {
        return ((current - previous + HalfTurn) & TurnMask) - HalfTurn;
    }

    /// <summary>An object's turns between two ticks: heading, pitch and roll each the short way round, the heading
    /// from <see cref="ObjectView.PreviousHeading"/> plus the half turn of <see cref="ObjectView.MeshYaw"/>. The
    /// fraction is used as given.</summary>
    public static RenderEuler ObjectTurn(in ObjectView pose, float fraction)
    {
        int turn = ShortestArc(pose.PreviousHeading, pose.Heading);
        float yaw = pose.PreviousHeading + HalfTurn + turn * fraction;
        float pitch = pose.PreviousPitch + ShortestArc(pose.PreviousPitch, pose.Pitch) * fraction;
        float roll = pose.PreviousRoll + ShortestArc(pose.PreviousRoll, pose.Roll) * fraction;
        return MeshOrientation.Mesh(yaw, pitch, roll);
    }
}
