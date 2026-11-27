// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The drawn camera as the minimap reads it, in the renderers' frame (x east, y up, z north, so z is minus the tile row): where the
/// viewport's centre and corners meet the floor plane (<see cref="TileHeights.Floor"/>) and the heading, as Unity's
/// <c>DungeonMinimap</c> read them from its camera (plan step 3b). Rays leave <see cref="Position"/>.</summary>
/// <param name="Aspect">The viewport's width over its height.</param>
/// <param name="ProjectionCentreShift">The view axis's offset above the viewport's middle in half heights
/// (<see cref="ClassicCameraPose.ProjectionCentreShift"/>; Unity's <c>-projection[1, 2]</c>).</param>
public readonly record struct MinimapCameraView(Vector3 Position, Vector3 Forward, Vector3 Up, float VerticalFieldOfView,
    float Aspect, float ProjectionCentreShift)
{
    /// <summary>The view of a classic camera pose drawn on <paramref name="viewport"/>.</summary>
    public static MinimapCameraView Of(in ClassicCameraPose pose, CameraViewport viewport)
    {
        return new MinimapCameraView(pose.Position, pose.Forward,
        pose.Up, pose.VerticalFieldOfView, viewport.Width / (float)viewport.Height, pose.ProjectionCentreShift);
    }

    /// <summary>A forward this close to vertical has no horizontal direction of its own.</summary>
    private const float VerticalForward = 1e-4f;

    /// <summary>Unity's <c>eulerAngles.y</c> of a camera looking along <see cref="Forward"/>, in [0, 360) degrees: the
    /// turn about y from north (+z), clockwise seen from above. Looking straight down (the TopDown preset's pitch 0) the
    /// turn is <see cref="Up"/>'s, which leans the way a pitched forward points, as Unity's decomposition takes it.</summary>
    public float YawDegrees
    {
        get
        {
            bool vertical = Forward.X * Forward.X + Forward.Z * Forward.Z < VerticalForward * VerticalForward;
            float degrees = (vertical ? MathF.Atan2(Up.X, Up.Z) : MathF.Atan2(Forward.X, Forward.Z)) * (180f / MathF.PI);
            return degrees < 0 ? degrees + 360f : degrees;
        }
    }

    /// <summary>The heading the minimap turns by: <see cref="YawDegrees"/> x 2048 / 360, rounded half to even as Unity's
    /// <c>Mathf.RoundToInt</c> does, unmasked.</summary>
    public Dk2Angle Heading
    {
        get
        {
            return HeadingOf(YawDegrees);
        }
    }

    /// <inheritdoc cref="Heading"/>
    public static Dk2Angle HeadingOf(float yawDegrees)
    {
        return new Dk2Angle(unchecked((ushort)(int)Math.Round(yawDegrees * (2048f / 360))));
    }

    /// <summary>The ray through viewport point (<paramref name="x"/>, <paramref name="y"/>), 0 to 1 from the bottom left,
    /// as Unity's <c>ViewportPointToRay</c> casts it through the off-centre frustum; the direction is unit length.</summary>
    public Vector3 RayDirection(float x, float y)
    {
        var forward = Vector3.Normalize(Forward);
        var right = Vector3.Normalize(Vector3.Cross(Up, forward));
        var up = Vector3.Cross(forward, right);
        float tangent = MathF.Tan(VerticalFieldOfView * (MathF.PI / 360f));
        return Vector3.Normalize(forward + right * ((2 * x - 1) * tangent * Aspect)
            + up * ((2 * y - 1 - ProjectionCentreShift) * tangent));
    }

    /// <summary>Where the viewport's centre meets the floor plane; false when its ray misses it.</summary>
    public bool TryFloorCentre(out Vector3 point)
    {
        return TileTopPlane.TryHit(Position, RayDirection(.5f, .5f), TileHeights.Floor, out point);
    }

    /// <summary>The viewport's corners on the floor plane in map tiles (x east, y south), in the order
    /// <see cref="MinimapCameraFootprint.TryBuild"/> takes: top left, top right, bottom right, bottom left. False when
    /// any corner's ray misses the plane.</summary>
    public bool TryFloorCorners(Span<Vector2> corners)
    {
        if (corners.Length < 4)
        {
            throw new ArgumentException("Four corners required.", nameof(corners));
        }

        for (int i = 0; i < 4; i++)
        {
            if (!TileTopPlane.TryHit(Position, RayDirection(i == 1 || i == 2 ? 1 : 0, i < 2 ? 1 : 0), TileHeights.Floor,
                    out Vector3 ground))
            {
                return false;
            }

            corners[i] = new Vector2(ground.X, -ground.Z);
        }
        return true;
    }
}
