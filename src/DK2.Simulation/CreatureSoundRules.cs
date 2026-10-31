// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public enum CreatureSoundEvent : ushort { Sacked = 0xDF }

public readonly record struct CreatureSoundRequest(
    CreatureId Creature, string Category, CreatureSoundEvent Event, FpPosition3 Position);

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-leaving.md")]
public static class CreatureSoundRules
{
    public static void Request(SimulationContext context, string system, CreatureState creature, CreatureSoundEvent sound)
    {
        string category = context.Database.CreatureById(creature.CreatureTypeId).SoundCategory;
        if (category.Length > 31) category = category.Substring(0, 31);
        context.Events.Emit(new SimEvent(context.Tick, system, "creature_sound", $"creature={creature.Id} event={(ushort)sound}")
        {
            CreatureSound = new(new(creature.Id), category, sound,
                new(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ))),
        });
    }
}
