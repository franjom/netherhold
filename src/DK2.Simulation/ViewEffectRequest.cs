// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public enum EffectVisibility : byte { Gated, Ungated }

public readonly record struct ShotId(int Value)
{
    public static ShotId None => default;
}

public readonly record struct CreatureId(int Value)
{
    public static CreatureId None => default;
}

/// <summary>A visual effect requested by a completed simulation change, in native position units.
/// A nonzero <paramref name="AttachedCreatureId"/> ties the effect to that creature's prior body point
/// when <paramref name="AttachPoint"/> is zero, or its mesh point otherwise (effect <c>+0x133</c>,
/// <c>+0x135</c>): the view moves the effect and
/// its elements with the point each update and lets go when the creature is gone (<c>0x00434E80</c>);
/// <see cref="Position"/> is where it starts when the view cannot find the point.
/// A shot tie follows only the emitter, leaves born elements behind, and suppresses HP decay.
/// <see cref="CarriedVelocity"/> carries launch metadata; its application belongs to generation.</summary>
public readonly record struct ViewEffectRequest(
    EffectId EffectId, PlayerId Owner, FpPosition3 Position, Dk2Angle Heading = default,
    int AttachedCreatureId = 0, uint AttachPoint = 0)
{
    public ShotId AttachedShotId { get; init; }
    public EffectVisibility Visibility { get; init; }
    public FpPosition3? SecondPoint { get; init; }
    public FpPosition3? CarriedVelocity { get; init; }
    public bool RemoveWithShot { get; init; }
    public TilePos Tile => new(Position.X.Raw >> 12, Position.Y.Raw >> 12);
    public Fp32 Height => Position.Z;

    public static ViewEffectRequest AtTileCentre(
        EffectId effectId, PlayerId owner, TilePos tile, Fp32 height, Dk2Angle heading = default) =>
        new(effectId, owner, new FpPosition3(new Fp32(CreatureState.CentreOf(tile.X)),
            new Fp32(CreatureState.CentreOf(tile.Y)), height), heading);
}
