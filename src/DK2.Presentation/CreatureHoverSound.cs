// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The sounds <c>Hand_UpdateHoverTarget</c> (<c>0x0040C290</c>, pick kind 2) asks while the Hand hovers over the viewer's own
/// hoverable creature (<c>Creature_HandHoverable</c>, <c>0x0048E670</c>), only on game turns that are a multiple of three times the tick
/// rate (the container's <c>+8</c>, 4): the current action record's sound word when an action runs and the word is nonzero, otherwise,
/// while the creature holds no room place (<c>+0x3B9</c>), <c>0xCA</c>, <c>0xCB</c> or <c>0xCC</c> by its mood band (<c>+0x28A</c>: content,
/// unhappy, angry). The request is the creature's handle, category and position. Presentation: it reads the world and changes nothing.
/// Deviations: ours tells the action only for the ones <see cref="CreatureCurrentAction"/> names, so a running action ours does not name
/// sounds the mood where DK2 sounds its word; the tool and the refresh that call DK2's function are not gated (docs/research/sound-effects-continuous.md
/// section 2).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-continuous.md#2-the-action-table")]
public static class CreatureHoverSound
{
    /// <summary>Three times the tick rate: <c>turn % (rate * 3) == 0</c>.</summary>
    public const int TurnPeriod = SimulationTiming.TicksPerSecond * 3;

    public static SoundRequest? For(WorldState world, GameDatabase database, WorldHoverHit hit, PlayerId viewer)
    {
        if (hit.Kind != WorldHoverKind.Creature || world.CurrentTick.Value % TurnPeriod != 0) return null;
        if (world.CreatureById(hit.EntityId) is not { } creature || creature.OwnerPlayerId != viewer
            || !HandRules.HasPickupPermission(world, database, creature, viewer)) return null;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        uint word = 0;
        if (CreatureCurrentAction.IsRunning(creature, world.CurrentTick))
            word = CreatureActionTable.SoundWord(CreatureCurrentAction.IndexOf(creature, definition, world.CurrentTick));
        if (word == 0)
        {
            if (creature.HeldPlace.IsHeld) return null;
            word = CreatureAngerRules.MoodOf(definition, creature) switch
            {
                CreatureMood.Content => 0xCA,
                CreatureMood.Unhappy => 0xCB,
                _ => 0xCC,
            };
        }
        if (definition.SoundCategory.Length == 0) return null;
        return new SoundRequest(new SoundCategory(definition.SoundCategory.ToLowerInvariant()), new SoundId(word),
            new Vector3(creature.PosX / 4096f, creature.PosZ / 4096f, -creature.PosY / 4096f),
            WorldSoundDirector.CreatureVoice(creature.Id));
    }
}
