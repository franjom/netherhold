// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed record LeavePossessionCommand(byte PlayerId) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash) => base.HashInto(ref hash);
}

/// <summary>Action 26 (<c>0x00514830</c>): the possessed creature strikes (<paramref name="Melee"/>)
/// or uses <paramref name="Slot"/>, looking along <paramref name="Aim"/>. The simulation picks the target.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed record ActPossessedCreatureCommand(byte PlayerId, PossessionAim Aim, bool Melee, PossessedSlot Slot) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        Aim.HashInto(ref hash);
        hash.Add(Melee);
        hash.Add(Slot.Value);
    }
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed record MovePossessedCreatureCommand(byte PlayerId, PossessionPose Pose) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        Pose.HashInto(ref hash);
    }
}
