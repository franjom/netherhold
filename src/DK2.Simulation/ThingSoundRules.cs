// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The kinds of thing that request world sounds; each owns its own range of voices.</summary>
public enum SoundSourceKind : byte { Creature, Object, Door, Trap, Shot, Room, Terrain }

/// <summary>The thing a world sound belongs to, so that its next request replaces its last one.</summary>
public readonly record struct SoundSourceId(SoundSourceKind Kind, int Value)
{
    public static SoundSourceId Of(CreatureId creature) => new(SoundSourceKind.Creature, creature.Value);
}

/// <summary>An event number as DK2 passes it to the sound manager: an SFX.map group in the
/// requester's category.</summary>
public enum ThingSoundEvent : ushort { BattleCall = 0xD6, Sacked = 0xDF }

public readonly record struct ThingSoundRequest(
    SoundSourceId Source, string Category, ThingSoundEvent Event, FpPosition3 Position);

/// <summary>A positional request through the sound manager's slot <c>+0x20</c> (<c>0x005678F0</c>).
/// The category is the requester's definition's, never the event's.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-leaving.md")]
public static class ThingSoundRules
{
    public static void Request(SimulationContext context, string system, CreatureState creature, ThingSoundEvent sound) =>
        Request(context, system, SoundSourceId.Of(new CreatureId(creature.Id)),
            context.Database.CreatureById(creature.CreatureTypeId).SoundCategory, sound,
            new(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ)));

    public static void Request(SimulationContext context, string system, SoundSourceId source, string category,
        ThingSoundEvent sound, FpPosition3 position)
    {
        if (category.Length > 31) category = category.Substring(0, 31);
        context.Events.Emit(new SimEvent(context.Tick, system, "thing_sound",
            $"{source.Kind.ToString().ToLowerInvariant()}={source.Value} event={(ushort)sound}")
        {
            ThingSound = new(source, category, sound, position),
        });
    }
}
