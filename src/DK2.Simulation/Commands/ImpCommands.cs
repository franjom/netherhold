// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>Casts Create Imp at an exact ground position, a tile centre, or the
/// dungeon Heart when neither is supplied. Permission and cost follow ADR-0312;
/// fractional cast coordinates follow imp-create-position.md.</summary>
public sealed record CreateImpCommand : PlayerCommand
{
    public TilePos? At { get; }
    public FpPosition2? Position { get; }

    public CreateImpCommand(PlayerId PlayerId, TilePos? At = null) : base(PlayerId) => this.At = At;

    public CreateImpCommand(PlayerId PlayerId, FpPosition2 Position) : base(PlayerId)
    {
        this.Position = Position;
        At = Position.Tile;
    }

    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(At.HasValue);
        if (At is { } at)
        {
            hash.Add(at.X);
            hash.Add(at.Y);
        }
        hash.Add(Position.HasValue);
        if (Position is { } position) position.HashInto(ref hash);
    }
}
