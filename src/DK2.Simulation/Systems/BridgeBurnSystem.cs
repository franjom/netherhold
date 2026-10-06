// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Burns away a wooden bridge over lava and reverts the tile to the lava it
/// spanned. Lifetime and its build-time clock: <see cref="BridgeRules.LifetimeTicks"/>.
/// Replacement terrain: <see cref="BridgeRules.RevertsTo"/>, shared with selling
/// and destruction, so a burnt bridge leaves what a sold one leaves (ADR-0031,
/// CONFIRMED — owner, 2026-09-03, Q67).
/// <para>The tile half only. The new lava then costs a creature standing there
/// −100 a second via <see cref="CreatureHealthRules.TerrainHealthPerSecond"/>,
/// and it is not stranded: the pathfinder enters a mover's starting tile without
/// testing walkability. Not implemented: a dead body disappearing. UNKNOWN:
/// gold's fate, blocked behind objects (M6, Q67).</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/rooms-and-gold.md")]
public sealed class BridgeBurnSystem : ISimulationSystem
{
    private const string SystemName = "BridgeBurn";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;

        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.BurnTicksRemaining <= 0) continue;

            if (--tile.BurnTicksRemaining > 0) continue;

            byte was = tile.TerrainId;
            byte becomes = BridgeRules.RevertsTo(context.Database, tile);
            TerrainDef next = context.Database.TerrainById(becomes);

            PlayerId ownerBefore = tile.OwnerPlayerId;
            tile.TerrainId = becomes;
            tile.Health = next.StartingHealth;
            tile.Flags = BridgeRules.SpansWater;
            if (!next.IsTaggable) tile.TaggedBy = PlayerMask.None;
            // The water or lava left behind belongs to the neutral player, as every terrain write
            // read so far gives it through Tile_SetTerrain (0x0044FDBF, ADR-0362). STRONGLY
            // INFERRED: the burn's own writer is unread.
            if (!next.IsOwnable) tile.OwnerPlayerId = PlayerIds.Neutral;
            SlabChain.Changed(world, context.Database, i, was, ownerBefore);

            int lostGold = tile.GoldHeld;
            tile.GoldHeld = 0;

            context.Emit(SystemName, "bridge_burnt",
                $"player={tile.OwnerPlayerId} at=({i % world.Width},{i / world.Width}) " +
                $"terrain={was}->{becomes} " +
                "note=anything standing on it is unharmed here (creature health is not implemented, Q67)");

            if (lostGold > 0)
            {
                context.Emit(SystemName, "gold_lost",
                    $"player={tile.OwnerPlayerId} amount={lostGold} " +
                    "reason=bridge_burnt (what the original does with it is UNKNOWN, Q67)");
            }
        }
    }
}
