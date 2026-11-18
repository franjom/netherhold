// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A check's way into the first-person view (<see cref="LaunchArguments.PossessFirstCreature"/>):
/// no spell is cast, so it needs no mana and no researched Possession. It writes the world outside a
/// turn, as a test rig does; a run that uses it is not a played game.</summary>
public static class PossessionStaging
{
    /// <summary>Marks the viewer's first creature possessed, with the fields the Possession spell's
    /// strike leaves (<see cref="PossessionRules.Strike"/>); null, with nothing written, when the
    /// viewer already possesses a creature or owns none the strike would take.</summary>
    public static CreatureState? PossessFirst(WorldState world, PlayerId viewer)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        ref PlayerState player = ref world.Player(viewer);
        if (player.PossessedCreatureId != 0) return null;
        foreach (CreatureState creature in world.OwnedCreatures(viewer))
        {
            if (creature.Possessed || JackInTheBoxRules.IsJack(creature)) continue;
            creature.Possessed = true;
            creature.FollowerCount = 0;
            creature.NextPossessionInfluenceTick = new GameTick(0);
            player.PossessedCreatureId = creature.Id;
            return creature;
        }
        return null;
    }
}
