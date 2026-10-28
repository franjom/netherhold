// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>Activates the special object selected by the player; <paramref name="TargetId"/> is the
/// creature a targeted special (127, 140) was dropped on, action 88's word parameter.</summary>
public sealed record ActivateSpecialCommand(PlayerId PlayerId, int ObjectId, int TargetId = 0)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(ObjectId);
        hash.Add(TargetId);
    }
}
