// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later

using DK2.Assets;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The common wall-flame sprite, separate from the mounted torch model.</summary>
public static class ClassicTorchFlame
{
    public static AssetRef Asset => AssetRef.ForObject("wall_torch", AssetKind.Effect, "flame");
    public const int SizeRaw = 1024;

    public static FpPosition3 Position(in TorchView torch) => new(
        new Fp32(torch.MeshPosX + (torch.Yaw == 0 ? 512 : torch.Yaw == 1024 ? -512 : 0)),
        new Fp32(torch.MeshPosY + (torch.Yaw == 512 ? 512 : torch.Yaw == 1536 ? -512 : 0)),
        new Fp32(torch.Height + 1200));

}
