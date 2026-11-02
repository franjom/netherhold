// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>What an object asks of the sound manager when it is made and when it is deleted. <c>Object_Init</c>
/// (<c>0x0049B1C0</c>) requests <c>0xF1</c> with the object's handle <c>+0x2E</c> and <c>0xF2</c> with
/// <c>+0x32</c>, and <c>Object_OnDelete</c> (<c>0x0049B5E0</c>) <c>0xF4</c> with <c>+0x2E</c>, each in the definition's
/// category (<c>+0x10E</c>) at the object's position (<c>+0x1A</c>). DK2 skips the delete's request while the level is
/// being left or restored, which a simulation has neither of. Only the categories whose <c>SFX.map</c> holds the group
/// are asked (the rest are silent, and every gold pile would be an event): <c>F1</c> in the bed's and the chicken's,
/// <c>F2</c> in the bat's and the rat's, <c>F4</c> in the chicken's and the boulder's.
/// Ours: the world calls this from <see cref="WorldState.SpawnObject"/> and <see cref="WorldState.RemoveObject"/>,
/// so every creator and remover is covered; a position a creator sets after the call (the hatchery's chicken takes its
/// egg's) is not yet set, and the tile's centre stands in for it. The bed's second <c>0xF1</c>
/// (<c>Creature_CreateBed</c>, <c>0x0048AE27</c>, only when its effect was made) passes the handle the first one
/// returned, which a live handle retains, and is not asked.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/sound-effects.md#14-objects-and-specials")]
public static class ObjectSoundRules
{
    private const string SystemName = "Objects";

    internal static void Made(SimulationContext context, ObjectState thing)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        string category = context.Database.ObjectById(thing.ObjectTypeId)?.SoundCategory ?? "";
        if (HasGroup(category, ThingSoundEvent.ObjectMade)) ThingSoundRules.Request(context, SystemName, thing, ThingSoundEvent.ObjectMade);
        if (HasGroup(category, ThingSoundEvent.ObjectMadeSecond)) ThingSoundRules.Request(context, SystemName, thing, ThingSoundEvent.ObjectMadeSecond);
    }

    internal static void Removing(SimulationContext context, ObjectState thing)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        string category = context.Database.ObjectById(thing.ObjectTypeId)?.SoundCategory ?? "";
        if (HasGroup(category, ThingSoundEvent.ObjectRemoved)) ThingSoundRules.Request(context, SystemName, thing, ThingSoundEvent.ObjectRemoved);
    }

    /// <summary>Whether the shipped sound maps hold this group in this category (read 2026-11-02;
    /// <c>ObjectSoundGroupsAreTheShippedOnes</c> reads them again).</summary>
    public static bool HasGroup(string category, ThingSoundEvent sound) => sound switch
    {
        ThingSoundEvent.ObjectMade => Is(category, "OBJECT_BED") || Is(category, "OBJECT_CHICKEN"),
        ThingSoundEvent.ObjectMadeSecond => Is(category, "OBJECT_BAT") || Is(category, "OBJECT_RAT"),
        ThingSoundEvent.ObjectRemoved => Is(category, "OBJECT_CHICKEN") || Is(category, "TRAP_BOULDER"),
        _ => false,
    };

    private static bool Is(string category, string name) => string.Equals(category, name, StringComparison.OrdinalIgnoreCase);
}
