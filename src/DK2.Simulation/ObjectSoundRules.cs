// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>What an object asks of the sound manager when it is made and when it is deleted. <c>Object_Init</c>
/// (<c>0x0049B1C0</c>) requests <c>0xF1</c> with the object's handle <c>+0x2E</c> and <c>0xF2</c> with
/// <c>+0x32</c>, and <c>Object_OnDelete</c> (<c>0x0049B5E0</c>) <c>0xF4</c> with <c>+0x2E</c>, each in the definition's
/// category (<c>+0x10E</c>) at the object's position (<c>+0x1A</c>), for every object; which categories have a sample
/// is the player's business. DK2 skips the delete's request while the level is being left or restored, which a
/// simulation has neither of. The world calls this from <see cref="WorldState.SpawnObject"/> and
/// <see cref="WorldState.RemoveObject"/>, so every creator and remover is covered, once a simulation holds the world:
/// level-load, hatchery-initialiser and snapshot-restore spawns are silent, and a position a creator sets after the call
/// (the hatchery's chicken takes its egg's) is not yet set, so the tile's centre stands in. The bed's second
/// <c>0xF1</c> (<c>Creature_CreateBed</c>, <c>0x0048AE27</c>, only when its effect was made) passes the handle the first
/// one returned, which a live handle retains, and is not asked.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-objects.md#1-objects-and-specials")]
public static class ObjectSoundRules
{
    private const string SystemName = "Objects";

    internal static void Made(SimulationContext context, ObjectState thing)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        ThingSoundRules.Request(context, SystemName, thing, ThingSoundEvent.ObjectMade);
        ThingSoundRules.Request(context, SystemName, thing, ThingSoundEvent.ObjectMadeSecond);
    }

    internal static void Removing(SimulationContext context, ObjectState thing)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        ThingSoundRules.Request(context, SystemName, thing, ThingSoundEvent.ObjectRemoved);
    }
}
