// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public readonly record struct EffectInstanceId(int Value);
public readonly record struct ProceduralEffectView(EffectInstanceId Id, ProceduralArtKind Kind, FpPosition3 Position);

public sealed partial class RoomConstructionParticles
{
    private readonly Dictionary<CreatureId, CreatureLightningVisual> _creatureLightning = [];

    public bool ChooseCreatureCrackle(CreatureId creature, GameTick tick, bool animationInstalled) =>
        _creatureLightning.TryGetValue(creature, out CreatureLightningVisual? visual)
            && visual.Choose(tick, animationInstalled);

    public void CollectProceduralEffects(List<ProceduralEffectView> effects)
    {
        effects.Clear();
        foreach (Emitter emitter in _emitters)
            if (emitter.Definition.ArtResource.Type == 7)
                effects.Add(new(new(emitter.Id), emitter.Definition.ArtResource.ProceduralKind, emitter.Request.Position));
    }
}
