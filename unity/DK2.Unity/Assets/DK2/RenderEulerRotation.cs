// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>A <see cref="RenderEuler"/> as a Unity rotation: <see cref="RenderRotation"/>'s, which needs no frame change
    /// here.</summary>
    public static class RenderEulerRotation
    {
        public static Quaternion Of(in RenderEuler turn) => ToUnity(RenderRotation.Of(turn));

        public static Quaternion Heading(float yaw) => ToUnity(RenderRotation.Heading(yaw));

        private static Quaternion ToUnity(System.Numerics.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
