// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>Picks a creature up into the keeper's hand, by creature id: which creature is under
/// the cursor is a UI question, and resolving it here would put a camera in the simulation (as for
/// <see cref="TagTilesCommand"/>'s rectangle). Refused rather than faulted when the creature is
/// gone, somebody else's, already in a hand, down, or the hand is full
/// (<see cref="HandRules.CanPickUp"/>); a refused command emits nothing, as every command
/// here.</summary>
public sealed record PickUpCreatureCommand(byte PlayerId, int CreatureId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(CreatureId);
    }
}

/// <summary>Puts down whatever the keeper is holding on the given tile — a creature or an object,
/// whichever came last. One gesture, one command (ADR-0075): DK2 has a single drop handler
/// branching on the kind it read back, and it takes no argument but a position, so the hand
/// chooses. Last in, first out — CONFIRMED (owner, 2026-09-06); that ordering is behaviour, so
/// naming a creature here would discard it (<see cref="PlayerState.Hand"/>). A worker put down
/// beside work the keeper has marked takes the job and works it 12.5 % faster
/// (<see cref="CreatureState.JustDropped"/>); dropping it on empty ground is worth nothing.</summary>
public sealed record DropCommand(byte PlayerId, TilePos Tile)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tile.X);
        hash.Add(Tile.Y);
    }
}

/// <summary>Picks a loose object up into the keeper's hand — a chicken, a pile of gold, a special.
/// CONFIRMED (owner, Q196), with a screenshot of four held at once. By object id, for the reason
/// <see cref="PickUpCreatureCommand"/> gives; DK2's cursor search — 3x3 tiles, an octagonal
/// approximation (<c>max + min/2</c>), a quarter tile for an object against half for a creature,
/// creatures preferred — is UI, not simulation. Refused when the object is gone, already held, in
/// an imp's arms, the hand is full, or its definition lacks
/// <see cref="Data.Model.ObjectDef.CanBePickedUp"/>, 26 of the 145 records (<see cref="HandRules.CanPickUpObject"/>).</summary>
public sealed record PickUpObjectCommand(byte PlayerId, int ObjectId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(ObjectId);
    }
}

/// <summary>Slaps a creature: it hurts (<c>SlapDamage</c>), it angers (<c>AngerSlap</c>, +3000 for
/// a Goblin and −5000 for Jack, whose whole record runs backwards), and it makes a worker work
/// faster for 25 s (<see cref="EfficiencyRules.SlapBonusSeconds"/>). There is no cooldown — every
/// slap counts however fast they come, because the original's <c>LastSlapTick</c> window guards
/// only its knock-back impulse (<see cref="HandRules.KnockBackWindowSeconds"/>). The work bonus
/// does not compound: a second slap rewrites it rather than adding to it (Q114).</summary>
public sealed record SlapCreatureCommand(byte PlayerId, int CreatureId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(CreatureId);
    }
}
