// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>What a creature's clip request is, for deduplication: a renderer starts the clip again only when this changes.
/// <see cref="Clip"/> is default when the creature plays no animation.</summary>
public readonly record struct CreatureClipIdentity(AssetRef Clip, GameTick? StartedAt, bool Loops, bool Deferred);

/// <summary>What an animated object's clip request is, for deduplication.</summary>
public readonly record struct ObjectClipIdentity(AssetRef Mesh, GameTick? StartedAt, bool Loops, TerrainLightColour AddedLight,
    Fp32 InitialPhase);

/// <summary>The rules a renderer follows to start a creature's or an object's clip. Render only.</summary>
public static class AnimationClipRequest
{
    public static CreatureClipIdentity Identity(in CreatureView creature, AssetRef animation)
    {
        return new(creature.PlaysAnimation ? animation : default, creature.AnimationStartedAt, creature.AnimationLoops,
            creature.AnimationDeferred);
    }

    public static ObjectClipIdentity Identity(in ObjectView pose, AssetRef mesh)
    {
        return new(mesh, pose.AnimationStartedAt, pose.AnimationLoops, pose.AddedLight, pose.AnimationInitialPhase);
    }

    /// <summary>A deferred clip waits for the playing one to finish (<see cref="RenderAnimationClock.Replace"/>).</summary>
    public static AnimationReplacement Replacement(in CreatureView creature)
    {
        return creature.AnimationDeferred ? AnimationReplacement.Deferred : AnimationReplacement.Immediate;
    }

    /// <summary>The clip's start on the draw clock (<see cref="PoseInterpolator.DrawSeconds"/>): the turn after the one
    /// it was started in, or now when the view gives no start.</summary>
    public static double StartSeconds(GameTick? startedAt, double drawSeconds)
    {
        return startedAt.HasValue
        ? (startedAt.Value.Value + 1d) / SimulationTiming.OriginalTicksPerGameSecond
        : drawSeconds;
    }
}
