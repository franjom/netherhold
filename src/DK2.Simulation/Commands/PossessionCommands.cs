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
