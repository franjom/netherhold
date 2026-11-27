// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Assets;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The common wall-flame sprite, separate from the mounted torch model.</summary>
public static class ClassicTorchFlame
{
    public static AssetRef Asset
    {
        get
        {
            return AssetRef.ForObject("wall_torch", AssetKind.Effect, "flame");
        }
    }

    public const int SizeRaw = 1024;

    public static FpPosition3 Position(in TorchView torch)
    {
        return new FpPosition3(
        new Fp32(torch.MeshPosX + (torch.Yaw == 0 ? 512 : torch.Yaw == 1024 ? -512 : 0)),
        new Fp32(torch.MeshPosY + (torch.Yaw == 512 ? 512 : torch.Yaw == 1536 ? -512 : 0)),
        new Fp32(torch.Height + 1200));
    }

    /// <summary>The flame's centre in the renderers' frame: <see cref="Position"/> bent by the lattice of the torch's wall
    /// (<see cref="TerrainLattice.Deform"/>, in DK2's frame, z down) when there is one.</summary>
    public static Vector3 RenderPoint(in TorchView torch, TerrainLattice? lattice)
    {
        FpPosition3 raw = Position(torch);
        var point = new Vector3(raw.X.Raw / 4096f, raw.Y.Raw / 4096f, -raw.Z.Raw / 4096f);
        if (lattice != null)
        {
            point = lattice.Deform(point, torch.Wall.X, torch.Wall.Y);
        }

        return new Vector3(point.X, -point.Z, -point.Y);
    }
}

/// <summary>Each lit wall flame's phase offset in the shared <see cref="ClassicTorchFlameAnimation"/>: drawn from one
/// <see cref="Dk2Lcg"/> seeded 1, in the order the torches are first seen, and kept until the torch goes.</summary>
public sealed class ClassicTorchFlamePhases
{
    private readonly Dk2Lcg _random = new Dk2Lcg(1);
    private readonly Dictionary<int, int> _offsets = [];

    /// <summary>The flame's offset over <paramref name="floorTileIndex"/>, 0 to 4095; a torch not yet seen draws one.</summary>
    public int OffsetOf(int floorTileIndex)
    {
        if (!_offsets.TryGetValue(floorTileIndex, out int offset))
        {
            _offsets.Add(floorTileIndex, offset = _random.NextInt(4096));
        }

        return offset;
    }

    /// <summary>Forgets a gone torch; lit again, it draws a new offset.</summary>
    public bool Remove(int floorTileIndex)
    {
        return _offsets.Remove(floorTileIndex);
    }

    /// <summary>Forgets every torch; the generator runs on, so torches seen again draw new offsets.</summary>
    public void Clear()
    {
        _offsets.Clear();
    }
}
