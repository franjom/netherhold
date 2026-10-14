// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A visual effect requested by a completed simulation change, in native position units.</summary>
public readonly record struct ViewEffectRequest(
    EffectId EffectId, PlayerId Owner, FpPosition3 Position, Dk2Angle Heading = default)
{
    public TilePos Tile => new(Position.X.Raw / Fp32.One, Position.Y.Raw / Fp32.One);
    public Fp32 Height => Position.Z;

    public static ViewEffectRequest AtTileCentre(
        EffectId effectId, PlayerId owner, TilePos tile, Fp32 height, Dk2Angle heading = default) =>
        new(effectId, owner, new FpPosition3(new Fp32(CreatureState.CentreOf(tile.X)),
            new Fp32(CreatureState.CentreOf(tile.Y)), height), heading);
}
