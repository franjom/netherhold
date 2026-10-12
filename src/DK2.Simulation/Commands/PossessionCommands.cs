// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed record LeavePossessionCommand(PlayerId PlayerId) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash) => base.HashInto(ref hash);
}

/// <summary>Action 26 (<c>0x00514830</c>): the possessed creature strikes (<paramref name="Melee"/>)
/// or uses <paramref name="Slot"/>, looking along <paramref name="Aim"/>. The simulation picks the target.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed record ActPossessedCreatureCommand(PlayerId PlayerId, PossessionAim Aim, bool Melee, PossessedSlot Slot) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        Aim.HashInto(ref hash);
        hash.Add(Melee);
        hash.Add(Slot.Value);
    }
}

/// <summary>Action 38 (<c>0x00516E40</c>): the possessed creature's followers are let go. The
/// input routine <c>0x0040FFB0</c> posts it (key binding 13, LIKELY); no packet decoder here builds it.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/possession-arms-and-inputs.md")]
public sealed record DismissFollowersCommand(PlayerId PlayerId) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash) => base.HashInto(ref hash);
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed record MovePossessedCreatureCommand(PlayerId PlayerId, PossessionPose Pose) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        Pose.HashInto(ref hash);
    }
}
