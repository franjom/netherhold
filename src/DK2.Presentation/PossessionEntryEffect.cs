// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The unpaid staging rig's projection of a Possession shot's first-tick creation request.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-entry-effect.md")]
public static class PossessionEntryEffect
{
    private const byte PossessionSpellId = 2;

    public static ViewEffectRequest? ForCreature(GameDatabase database, CreatureState target, PlayerId owner)
    {
        KeeperSpellDef? spell = database.KeeperSpellById(PossessionSpellId);
        if (spell == null || !database.Shots.TryGetValue(spell.Shot(upgraded: false).ShotTypeId, out ShotDef? shot)
            || shot.CreationEffectId == 0) return null;
        var point = new FpPosition3(new(target.PosX), new(target.PosY), new(Fp32.One + target.PosZ));
        return new ViewEffectRequest(new EffectId(shot.CreationEffectId), owner, point)
        {
            SecondPoint = point,
        };
    }
}
