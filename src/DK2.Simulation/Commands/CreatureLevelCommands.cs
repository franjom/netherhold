// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>The computer keeper's readiness grant: one ordinary level-up (<c>0x0048B120</c>) for an
/// own creature, which DK2's readiness helper <c>0x004FAB10</c> applies directly and this build
/// issues as a command so the effect is hashed with the input (ADR-0298). Refused for a creature
/// that is not the player's, dead, finished or at the top level. No human action corresponds.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-threat.md")]
public sealed record GrantCreatureLevelCommand(byte PlayerId, int CreatureId) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(CreatureId);
    }
}
