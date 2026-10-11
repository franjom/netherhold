// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

public static class TorchGeometry
{
    public static Vector3 Vertex(in TorchView torch, Vector3 vertex, TerrainLattice? lattice)
    {
        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, torch.Yaw * (MathF.PI / 1024f));
        Vector3 placed = Vector3.Transform(vertex, turn) + new Vector3(
            torch.MeshPosX / (float)Fp32.One, torch.Height / (float)Fp32.One, -torch.MeshPosY / (float)Fp32.One);
        if (lattice is null) return placed;
        Vector3 bent = lattice.Deform(new Vector3(placed.X, -placed.Z, -placed.Y), torch.Wall.X, torch.Wall.Y);
        return new Vector3(bent.X, -bent.Z, -bent.Y);
    }
}
