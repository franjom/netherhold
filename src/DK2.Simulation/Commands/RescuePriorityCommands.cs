// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>Sets one player's casualty-priority bit; does not capture or change ownership.</summary>
public sealed record SetRescuePriorityCommand(PlayerId PlayerId, int CreatureId, bool Enabled) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(CreatureId);
        hash.Add(Enabled);
    }
}
