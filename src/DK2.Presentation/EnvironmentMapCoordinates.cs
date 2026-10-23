// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>Where a vertex samples its material's environment texture in DK2's environment pass
/// (kmf-materials.md §8): the static-mesh emitter <c>0x00582290</c> and the animated one
/// <c>0x00583DC0</c> compute it the same way, per vertex, with no clock.
/// <para>Nothing calls this at run time: the implementation is the classic vertex-lit shader
/// (<c>ClassicVertexLit.shader</c>, keyword <c>DK2_ENVIRONMENT_MAP</c>), which repeats the formula
/// inline. This class is only the tested reference of that formula; a change to one must be made
/// to the other.</para></summary>
public static class EnvironmentMapCoordinates
{
    /// <summary>The coordinate of one vertex. <paramref name="eye"/> is the vertex minus the camera,
    /// in camera space (rotated by the view matrix at <c>0x00760AC4</c>), and is normalised here;
    /// <paramref name="normal"/> is the vertex normal turned by the drawn thing's own rotation, in
    /// DK2's frame, and is halved here (constant 0.5 at <c>0x0066FC20</c>). Then
    /// <c>u = Nz·Ex − Nx·Ez + 0.5</c> and <c>v = Nz·Ey − Ny·Ez + 0.5</c> (<c>0x00582503</c>–<c>0x00582769</c>).
    /// CONFIRMED arithmetic; the frames of the two vectors are LIKELY (the normal gets no view
    /// rotation).</summary>
    public static Vector2 Of(Vector3 eye, Vector3 normal)
    {
        float length = eye.Length();
        Vector3 e = length > 0f ? eye / length : Vector3.Zero;
        Vector3 n = normal * 0.5f;
        return new Vector2(n.Z * e.X - n.X * e.Z + 0.5f, n.Z * e.Y - n.Y * e.Z + 0.5f);
    }

    /// <summary>DK2's frame of a direction given in the renderer's, the inverse of
    /// <c>RenderMesh.ToRenderFrame</c>'s <c>(x, −z, −y)</c>.</summary>
    public static Vector3 FromRenderFrame(Vector3 render) => new(render.X, -render.Z, -render.Y);

    /// <summary>A camera-space point in Direct3D's left-handed convention (z into the screen) from
    /// a right-handed view space whose z points out of it, as Unity's does. GUESS: that DK2's
    /// camera space is Direct3D's own; the matrix at <c>0x00760AC4</c> was not traced to its
    /// builder, so the axis directions, and with them the sign of v, are unconfirmed.</summary>
    public static Vector3 FromRightHandedView(Vector3 view) => new(view.X, view.Y, -view.Z);
}
