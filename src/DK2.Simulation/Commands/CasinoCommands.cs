// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>
/// Pulls one of this keeper's Casino levers, switching it between generous and stingy (Q423).
///
/// <para>The original's handler (<c>0x00513E50</c>) takes a thing id and the issuing player,
/// refuses unless the thing is a live object whose owner word matches the issuer and whose
/// <c>+0x138</c> names a room, then calls the room's toggle <c>0x004D69D0</c>: 0 goes to 1 and
/// 1 to 0. Ours, not DK2's: naming the lever by its tile, as <see cref="LockDoorCommand"/>
/// names a door.</para>
/// </summary>
public sealed record PullCasinoLeverCommand(PlayerId PlayerId, TilePos Tile) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tile.X);
        hash.Add(Tile.Y);
    }
}
