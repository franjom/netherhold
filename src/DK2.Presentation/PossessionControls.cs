// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>What the possessor has selected and what Fire sends (possession-client.md §4): DK2's
/// interface byte <c>+0x13D9</c>, bit 0 melee and bits 3 to 7 the spell slot. The two special
/// abilities (bits 1 and 2, bindings 10 and 11) are not built: their kinds are not parsed yet.
/// That a possession starts on melee is a HYPOTHESIS.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-client.md")]
public sealed class PossessionControls
{
    private const int MeleeBit = 1, SpecialBits = 6;

    public byte Selection { get; private set; } = MeleeBit;
    public bool Melee => (Selection & MeleeBit) != 0;
    public int Slot => Selection >> 3;

    public void Reset() => Selection = MeleeBit;

    /// <summary>Binding 6 (<c>0x004058E7</c>): melee on, the slot kept.</summary>
    public void SelectMelee() => Selection = (byte)((Selection & ~SpecialBits) | MeleeBit);

    /// <summary>Bindings 7 to 9 (<c>0x0040F9E0</c> with 0 to 2): taken only when the definition has
    /// that spell slot, its spell id is not zero and the creature has reached its level.</summary>
    public void SelectSpell(int slot, CreatureDef definition, int level)
    {
        if (slot < 0 || slot >= definition.Spells.Count) return;
        CreatureSpellSlot spell = definition.Spells[slot];
        if (spell.CreatureSpellId == 0 || spell.LevelAvailable > level) return;
        Selection = (byte)(slot << 3);
    }

    /// <summary>Binding 12 (<c>0x0040F9E0</c> with 3): the reserved slot, the followers' arm.</summary>
    public void SelectFollowers() => Selection = PossessedSlot.FollowerArm << 3;

    /// <summary><c>0x004100F0</c>'s action 26, from the camera's point and its yaw and pitch.</summary>
    public ActPossessedCreatureCommand Fire(PlayerId player, PossessionView view) =>
        new(player, Aim(view), Melee, new PossessedSlot(Slot));

    /// <summary>The once-a-turn pose of <c>0x004039A0</c>.</summary>
    public static MovePossessedCreatureCommand Pose(PlayerId player, PossessionView view, in PossessedBody body)
    {
        (int x, int y, int height, int heading) = view.Pose(body);
        return new MovePossessedCreatureCommand(player,
            new PossessionPose(new FpPosition3(new Fp32(x), new Fp32(y), new Fp32(height)), new Dk2Angle((ushort)heading)));
    }

    public static PossessionAim Aim(PossessionView view) =>
        new(new FpPosition3(new Fp32(view.X), new Fp32(view.Y), new Fp32(view.Z)),
            new Dk2Angle((ushort)view.Yaw), new Dk2Angle((ushort)view.Pitch));
}
