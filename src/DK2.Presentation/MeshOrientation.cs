// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>A mesh's turns in degrees, in <see cref="DK2.Art.RenderMesh"/>'s frame (X east, Y up, Z north). An adapter
/// composes them outermost first: <see cref="Yaw"/> about Y, then <see cref="Roll"/> about Z, then <see cref="Pitch"/>
/// about X, so the model turns about X first and about Y last.</summary>
public readonly record struct RenderEuler(float Yaw, float Roll, float Pitch);

/// <summary>DK2's mesh turns, 2048 to a turn, as <see cref="RenderEuler"/> degrees. Render only: it reads views and
/// particles, never the world.</summary>
/// <remarks>DK2 builds a mesh's matrix as M = S·Rx·Ry·Rz (<c>0x00580480</c>, object-drawing.md §4): the model turns
/// about its X by the pitch, its Y by the roll, then its Z by the heading. DK2's X is ours and its Y our −Z
/// (<see cref="DK2.Art.RenderMesh.ToRenderFrame"/>), so the pitch is negated here and the roll is not. The half turn
/// on the heading is also our frame mapping, not DK2's (<see cref="ObjectView.MeshYaw"/>); a light mesh's yaw
/// (<see cref="Heading"/> of a torch) has none.</remarks>
public static class MeshOrientation
{
    /// <summary>A whole mesh turn: a yaw already in the renderer's frame (<see cref="ObjectView.MeshYaw"/>, or an
    /// interpolated one), the pitch about the model's X and the roll about its Y, in 1/2048ths of a turn.</summary>
    public static RenderEuler Mesh(float meshYaw, float pitch, float roll)
    {
        return new RenderEuler(meshYaw * 360f / 2048f, roll * 360f / 2048f, -pitch * 360f / 2048f);
    }

    /// <summary>A heading-only turn (structures, wall torches), degrees about Y.</summary>
    public static float Heading(int yaw)
    {
        return yaw * 360f / 2048f;
    }

    /// <summary>A construction particle's mesh turn between two ticks: each angle takes the short way round
    /// (<see cref="LerpAngle"/>), then the heading gains the half turn.</summary>
    public static RenderEuler Particle(ConstructionParticle source, float fraction)
    {
        const float degreesPerUnit = 360f / 2048f;
        float heading = LerpAngle(source.PreviousHeadingRaw * degreesPerUnit, source.HeadingRaw * degreesPerUnit, fraction);
        float pitch = LerpAngle(source.PreviousPitchRaw * degreesPerUnit, source.PitchRaw * degreesPerUnit, fraction);
        float roll = LerpAngle(source.PreviousRollRaw * degreesPerUnit, source.RollRaw * degreesPerUnit, fraction);
        return new RenderEuler(heading + 180f, roll, -pitch);
    }

    /// <summary>Degrees from <paramref name="from"/> towards <paramref name="to"/> the short way round, at
    /// <paramref name="fraction"/> clamped to 0..1: Unity's <c>Mathf.LerpAngle</c>, which the particles were drawn with.</summary>
    public static float LerpAngle(float from, float to, float fraction)
    {
        float delta = Repeat(to - from, 360f);
        if (delta > 180f)
        {
            delta -= 360f;
        }

        return from + delta * PoseInterpolator.Clamp01(fraction);
    }

    private static float Repeat(float value, float length)
    {
        float wrapped = value - (float)Math.Floor(value / length) * length;
        if (wrapped < 0f)
        {
            return 0f;
        }

        return wrapped > length ? length : wrapped;
    }
}
