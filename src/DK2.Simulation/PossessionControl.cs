// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

public readonly record struct PossessionPose(FpPosition3 Position, Dk2Angle Heading)
{
    public void HashInto(ref Hash64 hash)
    {
        Position.HashInto(ref hash);
        hash.Add(Heading.Raw);
    }
}

/// <summary>Action 26's camera: the pose the possessed creature looks from and the 11-bit yaw and
/// pitch it looks along (<c>0x00514830</c>'s packet).</summary>
public readonly record struct PossessionAim(FpPosition3 Origin, Dk2Angle Yaw, Dk2Angle Pitch)
{
    /// <summary>The packet widths: X and Y in 20 bits, Z in 14, all unsigned.</summary>
    public (int X, int Y, int Z) PacketOrigin =>
        (Origin.X.Raw & 0xFFFFF, Origin.Y.Raw & 0xFFFFF, Origin.Z.Raw & 0x3FFF);

    public void HashInto(ref Hash64 hash)
    {
        Origin.HashInto(ref hash);
        hash.Add(Yaw.Raw);
        hash.Add(Pitch.Raw);
    }
}

/// <summary>Action 26's slot: dword2's top nibble taken with an arithmetic shift, so -8 to 7.
/// Slot 3 is the follower arm; a slot outside the creature's spells is refused by the slot bound.</summary>
public readonly record struct PossessedSlot
{
    public const int FollowerArm = 3;

    public PossessedSlot(int value)
    {
        if (value is < -8 or > 7) throw new ArgumentOutOfRangeException(nameof(value), value, "A signed four-bit slot.");
        Value = (sbyte)value;
    }

    public sbyte Value { get; }
}
