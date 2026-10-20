// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Presentation;

public sealed partial class RoomConstructionParticles
{
    // Generation-zero stationary flat art is the footprint path. Other parent-art motion,
    // scale transitions and generation handlers need their own native evidence before use.
    private static bool HasDirectSprite(EffectDef effect) => effect.GenerationType == 0
        && effect.ArtResource.Type == 2 && (effect.ArtResource.Flags & 0x100) != 0
        && (effect.ArtResource.Flags & 4) == 0
        && effect.Mass == 0 && effect.MinSpeedXy == 0 && effect.MaxSpeedXy == 0
        && effect.MinSpeedYz == 0 && effect.MaxSpeedYz == 0 && effect.MinScale == effect.MaxScale;

    private static bool SupportsEffect(EffectDef effect) => effect.GenerationType == 4 || HasDirectSprite(effect);

    private void SpawnDirectSprite(Emitter emitter)
    {
        EffectDef effect = emitter.Definition;
        if (!HasDirectSprite(effect)) return;
        var point = emitter.Request.Position;
        // Share the sprite draw representation, not element generation: parent art is born
        // immediately, without any element random draws or a generated-child lifetime.
        var sprite = new ConstructionParticle
        {
            Definition = new EffectElementDef([])
            {
                Name = effect.Name, ArtResource = effect.ArtResource,
                MinHp = effect.MinHp, MaxHp = effect.MaxHp,
            },
            SpriteAsset = AssetRef.ForEffect(effect.Name),
            XRaw = point.X.Raw, PreviousXRaw = point.X.Raw,
            YRaw = point.Y.Raw, PreviousYRaw = point.Y.Raw,
            HeightRaw = point.Z.Raw, PreviousHeightRaw = point.Z.Raw,
            HeadingRaw = emitter.Request.Heading.Value, PreviousHeadingRaw = emitter.Request.Heading.Value,
            Health = emitter.Health, InitialHealth = emitter.Health,
            InitialScale = effect.MinScale,
        };
        _particles.Add(sprite);
        _parents.Add(sprite, emitter);
        emitter.ChildCount++;
    }
}
