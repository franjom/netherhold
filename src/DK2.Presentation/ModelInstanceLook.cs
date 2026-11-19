// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>What a drawn model's whole instance adds to its surfaces' looks, for an engine that sets it once per drawn
/// thing rather than as a material per value: the light <see cref="ObjectMaterialLook.Tinted"/> or
/// <see cref="ObjectMaterialLook.InstanceTinted"/> adds, and the alpha <see cref="ObjectMaterialLook.Faded"/> writes.
/// A surface drawn through it takes <see cref="Shape"/>, its added light plus <see cref="Tint"/> and its colour's alpha
/// times <see cref="Alpha"/>, which is that surface's tinted and faded look (<c>ModelInstanceLookTests</c>).</summary>
/// <param name="Tint">The light added to a look that takes added light.</param>
/// <param name="Alpha">The fade's alpha; 1 drawn as it is.</param>
public readonly record struct ModelInstanceLook(Vector3 Tint, float Alpha)
{
    /// <summary>Neither tinted nor faded: a creature, a torch, a lock, a plan.</summary>
    public static readonly ModelInstanceLook None = new(Vector3.Zero, 1f);

    /// <summary>An object at its terrain tint (an object view's added light) and its alpha (object <c>+0x8F</c>).</summary>
    public static ModelInstanceLook Object(TerrainLightColour tint, byte alpha) =>
        new(ObjectMaterialLook.TintLight(tint), ObjectMaterialLook.FadeAlpha(alpha));

    /// <summary>A structure at its RenderInfo tint, or nothing when its plan replaces its looks.</summary>
    public static ModelInstanceLook Structure(byte instanceTint, bool plan) =>
        plan ? None : new(ObjectMaterialLook.InstanceTintLight(instanceTint, instanceTint, instanceTint), 1f);

    /// <summary>Whether the model fades: its looks blend (<see cref="Shape"/>).</summary>
    public bool Faded => Alpha < 1f;

    /// <summary>Whether its environment passes draw: a faded one draws black, adding nothing (<see cref="ObjectMaterialLook.Faded"/>).</summary>
    public bool EnvironmentDrawn => !Faded;

    /// <summary>The look a surface's material draws with under this instance: the fade's blend when faded.</summary>
    public ObjectMaterialLook Shape(in ObjectMaterialLook look) => Faded ? look.FadeShape() : look;
}
