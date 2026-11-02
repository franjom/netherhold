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
public enum ThingSoundEvent : ushort { PickedUp = 0xC3, Dropped = 0xC4, BattleCall = 0xD6, Sacked = 0xDF }

/// <summary>One positional request. <paramref name="Voiced"/> is false where DK2 passes handle 0 (the
/// creature's lift and drop): the sound then neither replaces nor is retained by the thing's own voice.</summary>
public readonly record struct ThingSoundRequest(
    SoundSourceId Source, string Category, ThingSoundEvent Event, FpPosition3 Position, bool Voiced = true);

/// <summary>A sound a thing requests at its position. Read for creatures (<c>0xD6</c>, <c>0xDF</c>,
/// and the hand's <c>0xC3</c>/<c>0xC4</c>) and objects (the hand's <c>0xC3</c>/<c>0xC4</c>): positional
/// slot <c>+0x20</c> in the definition's category (creature <c>+0x6E3</c>, object <c>+0x10E</c>); each new kind's
/// slot and category are read at its call site before it is emitted here.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-leaving.md")]
public static class ThingSoundRules
{
    /// <summary>An object's request, in its definition's category at <paramref name="at"/> or, with none,
    /// its own position (the tile's centre, at floor height, while it keeps none). Nothing is emitted for
    /// an object with no category: the shipped objects whose category is empty have no group to play.</summary>
    public static void Request(SimulationContext context, string system, ObjectState thing, ThingSoundEvent sound,
        FpPosition2? at = null)
    {
        string category = context.Database.ObjectById(thing.ObjectTypeId)?.SoundCategory ?? "";
        if (category.Length == 0) return;
        bool placed = thing.PosX != 0 || thing.PosY != 0;
        FpPosition2 point = at ?? new FpPosition2(
            new Fp32(placed ? thing.PosX : CreatureState.CentreOf(thing.TileIndex % context.World.Width)),
            new Fp32(placed ? thing.PosY : CreatureState.CentreOf(thing.TileIndex / context.World.Width)));
        Request(context, system, new SoundSourceId(SoundSourceKind.Object, thing.Id), category, sound,
            new(point.X, point.Y, new Fp32(EggLaunchRules.FloorHeight(context.World, context.Database, point.X, point.Y))));
    }

    /// <summary>A creature's request with handle 0, which DK2 passes when lifting and dropping it.</summary>
    public static void RequestUnvoiced(SimulationContext context, string system, CreatureState creature,
        ThingSoundEvent sound, FpPosition3 position) =>
        Request(context, system, SoundSourceId.Of(new CreatureId(creature.Id)),
            context.Database.CreatureById(creature.CreatureTypeId).SoundCategory, sound, position, voiced: false);

    public static void Request(SimulationContext context, string system, CreatureState creature, ThingSoundEvent sound) =>
        Request(context, system, SoundSourceId.Of(new CreatureId(creature.Id)),
            context.Database.CreatureById(creature.CreatureTypeId).SoundCategory, sound,
            new(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ)));

    public static void Request(SimulationContext context, string system, SoundSourceId source, string category,
        ThingSoundEvent sound, FpPosition3 position, bool voiced = true)
    {
        if (category.Length > 31) category = category.Substring(0, 31);
        context.Events.Emit(new SimEvent(context.Tick, system, "thing_sound",
            $"{source.Kind.ToString().ToLowerInvariant()}={source.Value} event={(ushort)sound}")
        {
            ThingSound = new(source, category, sound, position, voiced),
        });
    }
}
