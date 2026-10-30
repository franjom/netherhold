// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Presentation;

public sealed partial class RoomConstructionParticles
{
    // Generation-zero stationary sprite art: footprints (flat) and the room-border flame. Parent
    // motion and the other generation handlers need their own native evidence before use.
    private static bool HasDirectSprite(EffectDef effect) => effect.GenerationType == 0
        && effect.ArtResource.Type is 2 or 3
        && effect.Mass == 0 && effect.MinSpeedXy == 0 && effect.MaxSpeedXy == 0
        && effect.MinSpeedYz == 0 && effect.MaxSpeedYz == 0;

    private static bool SupportsEffect(EffectDef effect) => effect.GenerationType is 3 or 4 or 6 or 18 || HasDirectSprite(effect);

    /// <summary>The parent's own sprite (<c>0x004347E0</c>'s art arm), born at the instance's point
    /// with its lifetime. Its size follows the first of definition flags 2 (MaxScale shrinking to
    /// MinScale over the lifetime), 4 (MinScale growing to MaxScale over it) and 0x20 (MinScale
    /// growing to MaxScale over half of it, then back to 0, <c>0x004B3B40</c>); without them it is
    /// one draw in MinScale..MaxScale, none when the two are equal. Art flag 2 tints it with the
    /// owner's palette colour, or the cycling one for the neutral player; otherwise the tint is 0.
    /// The animation starts at its first frame: the parent's phase install is unread.</summary>
    private void SpawnDirectSprite(Emitter emitter)
    {
        EffectDef effect = emitter.Definition;
        if (!HasDirectSprite(effect)) return;
        var point = emitter.Request.Position;
        int health = emitter.Health;
        int initial = effect.MinScale, target = effect.MinScale, steps = 0;
        bool returns = false;
        if ((effect.Flags & 2) != 0) (initial, steps) = (effect.MaxScale, health);
        else if ((effect.Flags & 4) != 0) (target, steps) = (effect.MaxScale, health);
        else if ((effect.Flags & 0x20) != 0) (target, steps, returns) = (effect.MaxScale, health / 2, true);
        else initial = target = DrawExclusive(effect.MinScale, effect.MaxScale);
        int frames = effect.ArtResource.Unknown_0044.Length >= 10
            ? BitConverter.ToUInt16(effect.ArtResource.Unknown_0044.Span.Slice(8, 2)) : 0;
        (byte red, byte green, byte blue) = (effect.ArtResource.Flags & 2) != 0
            ? ParentColour(emitter.Request.Owner) : ((byte)0, (byte)0, (byte)0);
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
            IsEmitterArt = true,
            XRaw = point.X.Raw, PreviousXRaw = point.X.Raw,
            YRaw = point.Y.Raw, PreviousYRaw = point.Y.Raw,
            HeightRaw = point.Z.Raw, PreviousHeightRaw = point.Z.Raw,
            HeadingRaw = emitter.Request.Heading.Value, PreviousHeadingRaw = emitter.Request.Heading.Value,
            Health = health, InitialHealth = health,
            InitialScale = initial, ScaleTarget = target,
            FrameDuration = (effect.ArtResource.Flags & 4) != 0 ? frames : 0,
            Red = red, Green = green, Blue = blue,
        };
        int step = steps > 0 ? (target - initial) / steps : 0;
        if (step != 0) sprite.SetParentScaleRamp(step, steps, returns);
        else sprite.ScaleTarget = initial;
        _particles.Add(sprite);
        _parents.Add(sprite, emitter);
        emitter.ChildCount++;
    }

    /// <summary>The parent's palette tint (<c>0x004347E0</c>): the owner's colour through slot
    /// <c>+0x290</c>, or for the neutral player the cycling one through <c>+0x298</c>
    /// (<c>0x0050F880</c>: palette number <c>floor(ms / 100) % 7 + 1</c> of the wall clock).
    /// Unlike an element's, no channel is raised to 48.</summary>
    private (byte Red, byte Green, byte Blue) ParentColour(PlayerId owner) =>
        ClassicKeeperPalette.Colour(owner.Value == PlayerIds.Neutral
            ? new PlayerId((byte)(_clock().Milliseconds / 100 % 7 + 1)) : owner);
}
