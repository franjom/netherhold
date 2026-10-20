// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>Original constructor name/blood allocation; see ADR-0521 and the function records.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/world-hover-text.md")]
public static class CreatureIdentityRules
{
    public const ushort UnassignedNameIndex = ushort.MaxValue;
    public const int NameCount = 500;
    public const int BloodGroupCount = 7;

    public static void Initialize(WorldState world, CreatureState creature, SimRandom random)
    {
        if (creature.NameIndex != UnassignedNameIndex || !CreatureStateMachine.HasState(creature)) return;
        // The constructor has already linked this creature to its owner. Its cleared
        // field participates in the search, so candidate zero collides with itself.
        creature.NameIndex = 0;
        int name = random.NextInt(NameCount);
        creature.NameIndex = (ushort)FindUnused(world, creature, name, NameCount, bloodGroup: false);
        creature.BloodGroupIndex = 0;
        int blood = random.NextInt(BloodGroupCount - 1);
        creature.BloodGroupIndex = (byte)FindUnused(world, creature, blood, BloodGroupCount, bloodGroup: true);
    }

    private static int FindUnused(WorldState world, CreatureState creature, int first, int count, bool bloodGroup)
    {
        int candidate = first;
        do
        {
            bool used = false;
            foreach (CreatureState other in world.Creatures)
            {
                if (other.OwnerPlayerId != creature.OwnerPlayerId || !CreatureStateMachine.HasState(other)) continue;
                if ((bloodGroup ? other.BloodGroupIndex : other.NameIndex) != candidate) continue;
                used = true;
                break;
            }
            if (!used) return candidate;
            candidate = (candidate + 1) % count;
        } while (candidate != first);
        return first;
    }
}
