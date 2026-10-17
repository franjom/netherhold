// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// A row-major 3x3 camera rotation in the engine's native view convention
/// (the matrix 0x00598880 hands the renderer in camera-path mode).
/// </summary>
public readonly record struct KcsRotation(
    float M00, float M01, float M02,
    float M10, float M11, float M12,
    float M20, float M21, float M22);

/// <summary>
/// One 56-byte <c>.KCS</c> frame after the loader's conversion (0x0044CFB0).
/// </summary>
/// <param name="X">Offset from the path origin, in tiles.</param>
/// <param name="Y">Offset from the path origin, in tiles.</param>
/// <param name="Z">Height in tiles, positive downward; the camera stands <c>1 - Z</c> tiles up.</param>
/// <param name="Rotation">The native view rotation.</param>
/// <param name="FieldOfView">Horizontal field of view, radians (focal = width / (2 tan(fov/2))).</param>
/// <param name="ViewDistance">The authored view distance; the renderer takes <c>x 0.002</c> tiles.</param>
public readonly record struct KcsFrame(
    float X, float Y, float Z, KcsRotation Rotation, float FieldOfView, float ViewDistance);
