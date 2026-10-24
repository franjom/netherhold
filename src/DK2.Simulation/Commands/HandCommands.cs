// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>Picks a creature up into the keeper's hand, by creature id: which creature is under
/// the cursor is a UI question, and resolving it here would put a camera in the simulation (as for
/// <see cref="TagTilesCommand"/>'s rectangle). Refused rather than faulted when the creature is
/// gone, somebody else's, already in a hand, down, or the hand is full
/// (<see cref="HandRules.CanPickUp"/>); a refused command emits nothing, as every command
/// here.</summary>
public sealed record PickUpCreatureCommand(PlayerId PlayerId, int CreatureId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(CreatureId);
    }
}

/// <summary>Lifts a Treasury or Heart tile's store pile (Gold Pile 1) into the hand, by tile: ours
/// keeps a store tile's gold in the tile (ADR-0015), so the pile has no object id until it is
/// lifted. DK2's action 59 names the object (<c>0x005135B0</c>) and hand state 2 lifts it whole
/// (<c>0x004C0CB0</c>, gold-hover-and-sparkle.md §4). Refused when the tile holds no store gold or
/// the hand is full (<see cref="HandRules.CanPickUpStoreGold"/>).</summary>
public sealed record PickUpStoreGoldCommand(PlayerId PlayerId, TilePos Tile)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tile.X);
        hash.Add(Tile.Y);
    }
}

/// <summary>Puts down whatever the keeper is holding on the given tile — a creature or an object,
/// whichever came last. One gesture, one command (ADR-0075): DK2 has a single drop handler
/// branching on the kind it read back, so the hand chooses what goes down. Last in, first out —
/// CONFIRMED (owner, 2026-09-06); naming the held thing here would discard that ordering
/// (<see cref="PlayerState.Hand"/>). A worker put down beside work the keeper has marked takes
/// the job and works it 12.5 % faster (<see cref="CreatureState.JustDropped"/>).
/// <para><paramref name="TargetCreatureId"/> is the creature under the cursor, 0 for none. DK2
/// keeps it in the keeper record (<c>+0x196</c>) rather than passing it to the handler, and only
/// a chicken or gold dropped onto a creature reads it (<see cref="DropDeliveryRules"/>,
/// ADR-0217).</para></summary>
public sealed record DropCommand(PlayerId PlayerId, TilePos Tile, int TargetCreatureId = 0,
    FpPosition2? Position = null)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Tile.X);
        hash.Add(Tile.Y);
        hash.Add(TargetCreatureId);
        hash.Add(Position.HasValue);
        if (Position is FpPosition2 position) position.HashInto(ref hash);
    }
}

/// <summary>Picks a loose object up into the keeper's hand — a chicken, a pile of gold, a special.
/// CONFIRMED (owner, Q196), with a screenshot of four held at once. By object id, for the reason
/// <see cref="PickUpCreatureCommand"/> gives; DK2's cursor search — 3x3 tiles, an octagonal
/// approximation (<c>max + min/2</c>), a quarter tile for an object against half for a creature,
/// creatures preferred — is UI, not simulation. Refused when the object is gone, already held, in
/// an imp's arms, the hand is full, or its definition lacks
/// <see cref="Data.Model.ObjectDef.CanBePickedUp"/>, 26 of the 145 records (<see cref="HandRules.CanPickUpObject"/>).</summary>
public sealed record PickUpObjectCommand(PlayerId PlayerId, int ObjectId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(ObjectId);
    }
}

/// <summary>The computer keeper's pay remedy (<c>0x004F9710</c>, buckets 0/4/5): takes the creature's
/// level-scaled wage from the Treasuries and Heart as <see cref="GoldStore.Spend"/> does, which may
/// give less, and puts it in the hand as a new pile of gold, ready for a <see cref="DropCommand"/>
/// naming the creature. Refused without a Treasury, for a creature not the keeper's, or when
/// nothing is taken. With a full hand the pile is left on tile 0, where DK2's factory makes it
/// (<see cref="Systems.HandSystem"/>). No human gesture does this.</summary>
public sealed record MintWageCommand(PlayerId PlayerId, int CreatureId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(CreatureId);
    }
}

/// <summary>Slaps a creature: it hurts (<c>SlapDamage</c>), it angers (<c>AngerSlap</c>, +3000 for
/// a Goblin and −5000 for Jack, whose whole record runs backwards), and it makes a worker work
/// faster for 25 s (<see cref="EfficiencyRules.SlapBonusSeconds"/>). There is no cooldown — every
/// slap counts however fast they come, because the original's <c>LastSlapTick</c> window guards
/// only its knock-back impulse (<see cref="HandRules.KnockBackWindowSeconds"/>). The work bonus
/// does not compound: a second slap rewrites it rather than adding to it (Q114).</summary>
public sealed record SlapCreatureCommand(PlayerId PlayerId, int CreatureId)
    : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(CreatureId);
    }
}
