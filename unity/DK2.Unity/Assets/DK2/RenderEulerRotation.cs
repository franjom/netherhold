// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>A <see cref="RenderEuler"/> as a Unity rotation, composed in the order <see cref="MeshOrientation"/> states.</summary>
    public static class RenderEulerRotation
    {
        public static Quaternion Of(in RenderEuler turn) =>
            Quaternion.Euler(0f, turn.Yaw, 0f) * Quaternion.AngleAxis(turn.Roll, Vector3.forward) * Quaternion.Euler(turn.Pitch, 0f, 0f);

        public static Quaternion Heading(float yaw) => Quaternion.Euler(0f, yaw, 0f);
    }
}
