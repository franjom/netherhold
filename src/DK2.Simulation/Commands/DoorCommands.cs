// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Locks or unlocks one of this keeper's own doors.
///
/// <para>The original's handler (<c>0x00513800</c>, doors-and-traps.md) takes a thing id and the
/// issuing player, and refuses unless the thing is live, its type is 4 — a door — and its owner
/// word matches the issuer. It then <b>toggles</b>: a door whose lock mode is zero goes to 3, and
/// a door at any other mode goes to 0. There is no way to ask for a particular mode, which is one
/// half of why modes 1 and 2 are unreachable in v1.70.</para>
///
/// <para>Ours, not DK2's: naming the door by its tile. The original's interface hands the handler
/// a thing id chosen by a click, and how a click becomes that id belongs to the UI layer.</para>
/// </summary>
public sealed record LockDoorCommand(byte PlayerId, TilePos Tile) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tile.X);
        hash.Add(Tile.Y);
    }
}
