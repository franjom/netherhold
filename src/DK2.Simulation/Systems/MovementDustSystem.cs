// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The foot dust of the creature update (<c>0x00488930</c> at <c>0x0048A06A</c>;
/// docs/research/creature-movement-dust.md): a creature whose stored previous position and
/// current position lie on different tiles, on a Dirt Path (terrain id 3, a literal), asks for
/// effect 117 at its position, one tile up, with its heading. One request per crossing, diagonal
/// or not, with no test of mode, speed, kind or flight. DK2 tests the crossing at the update's
/// entry, before the previous position is refreshed and before the state handler, and requests
/// after the handler with the entry tile's terrain; ours runs in the creature's update just before
/// its mover and compares the tile it saw last tick, which is the same unless a state handler moves
/// the creature within a tick. Held creatures and corpses are skipped as the other mirrors do (ADR-0206); whether
/// DK2's thing pass updates them is unread.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-movement-dust.md")]
public sealed class MovementDustSystem : ICreatureUpdateStep
{
    /// <summary>Foot Dust Effect, the literal the creature update requests.</summary>
    public const ushort FootDustEffectId = 117;

    /// <summary>The shipped Dirt Path, the literal terrain id the creature update tests.</summary>
    public const byte DirtPathTerrainId = 3;

    /// <summary>The request's height over the creature's position: one tile (<c>0x1000</c>).</summary>
    public const int RequestHeight = Fp32.One;

    public string Name => "movement dust";

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void Update(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        if (HandRules.IsHeld(creature) || CreatureDeathRules.IsCorpse(creature) || !world.Contains(creature.Tile))
        {
            creature.LastDustTile = -1;
            return;
        }
        int tile = world.TileIndex(creature.Tile);
        bool crossed = creature.LastDustTile >= 0 && creature.LastDustTile != tile;
        creature.LastDustTile = tile;
        if (!crossed || world.TileAtIndex(tile).TerrainId != DirtPathTerrainId) return;
        context.RequestEffect(Name, new ViewEffectRequest(new EffectId(FootDustEffectId),
            new PlayerId(creature.OwnerPlayerId),
            new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(RequestHeight)),
            new Dk2Angle((ushort)creature.Heading)));
    }
}
