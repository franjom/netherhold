// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A visual effect requested by a completed simulation change, in native position units.
/// A nonzero <paramref name="AttachedCreatureId"/> ties the effect to that creature's mesh point
/// <paramref name="AttachPoint"/> (effect <c>+0x133</c>, <c>+0x135</c>): the view moves the effect and
/// its elements with the point each update and lets go when the creature is gone (<c>0x00434E80</c>);
/// <see cref="Position"/> is where it starts when the view cannot find the point.</summary>
public readonly record struct ViewEffectRequest(
    EffectId EffectId, PlayerId Owner, FpPosition3 Position, Dk2Angle Heading = default,
    int AttachedCreatureId = 0, uint AttachPoint = 0)
{
    public TilePos Tile => new(Position.X.Raw / Fp32.One, Position.Y.Raw / Fp32.One);
    public Fp32 Height => Position.Z;

    public static ViewEffectRequest AtTileCentre(
        EffectId effectId, PlayerId owner, TilePos tile, Fp32 height, Dk2Angle heading = default) =>
        new(effectId, owner, new FpPosition3(new Fp32(CreatureState.CentreOf(tile.X)),
            new Fp32(CreatureState.CentreOf(tile.Y)), height), heading);
}
