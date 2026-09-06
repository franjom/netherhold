// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// One row of a level's PLAYERS(180) record — a player slot as the level
/// defines it: who plays, with how much gold, and, for a computer keeper, the
/// personality block that drives it.
///
/// <para><b>Layout CONFIRMED, semantics STRONGLY INFERRED</b> from the retail
/// loader that consumes this record (<c>KEEPER_AI.md</c> §3.1 on the
/// <c>claude/keeper-ai</c> branch): all 66 shipped <c>*Players.kld</c> files
/// divide exactly into 205-byte records, and the loader's reads pin the gold,
/// the AI flag, the 158-byte block, the trigger id, the player id, the camera
/// and the name. OpenKeeper's independent parser (GPLv3) agrees on every field
/// boundary and was used as a cross-check only.</para>
///
/// <para>Every level carries a <c>Good Player</c> (id 1) and a <c>Neutral
/// Player</c> (id 2) with the AI flag set; the game's player tick skips both
/// before the computer keeper ever runs, so <see cref="IsComputerKeeper"/> is
/// the question a caller usually means.</para>
/// </summary>
public sealed record PlayerDef
{
    /// <summary>Player id: 1 good, 2 neutral, 3 and up keepers (<see cref="PlayerIds"/>).</summary>
    public required byte PlayerId { get; init; }

    /// <summary>The editor's name for the slot, e.g. "Gormagon" or "Good Player".</summary>
    public required string Name { get; init; }

    /// <summary>Gold in the treasury at the start of the level.</summary>
    public required int StartingGold { get; init; }

    /// <summary>The record's AI flag as shipped. Set on the good and neutral rows too.</summary>
    public required bool HasAiFlag { get; init; }

    /// <summary>Trigger id the level attaches to this player.</summary>
    public required ushort TriggerId { get; init; }

    /// <summary>Starting camera tile, used by the original only for the local human player.</summary>
    public required ushort CameraX { get; init; }
    public required ushort CameraY { get; init; }

    /// <summary>The 158-byte tuning block, decoded. Present on every record, meaningful only when the AI flag is set.</summary>
    public required KeeperAIParameters Ai { get; init; }

    /// <summary>True for a slot the retail game actually runs a computer keeper for: AI flag set and a keeper id.</summary>
    public bool IsComputerKeeper => HasAiFlag && PlayerIds.IsKeeper(PlayerId);
}
