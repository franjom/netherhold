// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>Action 105: transfer stored gold into the keeper's hand (hud-resources.md).</summary>
public sealed record WithdrawCounterGoldCommand(PlayerId PlayerId, GoldAmount Amount) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Amount.Value);
    }
}
