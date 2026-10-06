// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-messages.md")]
public static class CombatMessageRules
{
    public static void UpdateBattle(WorldState world, CreatureState creature, long tick)
    {
        if (creature.Dk2StateId != CombatStates.Engaging || creature.StateEnteredTick != tick) return;
        if (creature.AttackType == CombatRules.AttackMode.SpellAtAlly) return;
        world.Messages.Post(world, creature.OwnerPlayerId, PlayerMessageKind.CombatStarted,
            creature.Tile.X, creature.Tile.Y);
    }

    public static void HeartDamaged(SimulationContext context, PlayerId player)
    {
        if (context.World.Messages.Unexpired(player.Value, PlayerMessageKind.HeartAttacked) is not null) return;
        int index = ImpSupplyRules.HeartTileOf(context.World, context.Database, player);
        if (index < 0) return;
        var tile = new TilePos(index % context.World.Width, index / context.World.Width);
        context.World.Messages.Post(context.World, player.Value, PlayerMessageKind.HeartAttacked, tile.X, tile.Y);
    }
}
