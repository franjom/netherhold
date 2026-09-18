// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Makes or breaks an alliance between two keepers.
///
/// <para>The original has two handlers, one each way (<c>0x00513FF0</c> make, <c>0x00513F60</c>
/// break, doors-and-traps.md). Both read the two players out of one packet and write <b>both</b>
/// arrays at <c>player+0x1AE</c>, so an alliance is symmetric by construction, and neither asks
/// whether the issuer is one of the pair.</para>
///
/// <para>Ours, not DK2's: one command with a flag where the original has two handlers, the same
/// shape <see cref="TagTilesCommand"/> uses.</para>
/// </summary>
public sealed record SetAllianceCommand(byte PlayerId, byte OtherPlayerId, bool Allied)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(OtherPlayerId);
        hash.Add(Allied);
    }
}
