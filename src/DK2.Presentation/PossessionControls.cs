// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>What the possessor has selected and what Fire sends (possession-client.md §4): DK2's
/// interface byte <c>+0x13D9</c>, bit 0 melee, bits 1 and 2 specials, bits 3 to 7 the spell slot.
/// That a possession starts on melee is the owner's recollection (2026-11-14), not a read.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-client.md")]
public sealed class PossessionControls
{
    private const int MeleeBit = 1, SpecialBits = 6;

    public byte Selection { get; private set; } = MeleeBit;
    public bool Melee
    {
        get
        {
            return (Selection & MeleeBit) != 0;
        }
    }

    public int Slot
    {
        get
        {
            return Selection >> 3;
        }
    }

    public void Reset()
    {
        Selection = MeleeBit;
    }

    /// <summary>Binding 6 (<c>0x004058E7</c>): melee on, the slot kept.</summary>
    public void SelectMelee()
    {
        Selection = (byte)((Selection & ~SpecialBits) | MeleeBit);
    }

    /// <summary>Bindings 7 to 9 (<c>0x0040F9E0</c> with 0 to 2): taken only when the definition has
    /// that spell slot, its spell id is not zero and the creature has reached its level.</summary>
    public void SelectSpell(int slot, CreatureDef definition, int level)
    {
        if (slot < 0 || slot >= definition.Spells.Count)
        {
            return;
        }

        CreatureSpellSlot spell = definition.Spells[slot];
        if (spell.CreatureSpellId == 0 || spell.LevelAvailable > level)
        {
            return;
        }

        Selection = (byte)(slot << 3);
    }

    /// <summary>Binding 12 (<c>0x0040F9E0</c> with 3): the reserved slot, the followers' arm.</summary>
    public void SelectFollowers()
    {
        Selection = PossessedSlot.FollowerArm << 3;
    }

    public static OpenPossessedPrisonCommand? PickLock(WorldState world, GameDatabase database, PlayerId player,
        PossessionView view, CreatureState creature)
    {
        var cameraPoint = new TilePos(view.X >> 12, view.Y >> 12);
        return PrisonAt(cameraPoint) && PrisonAt(creature.Tile)
            ? new OpenPossessedPrisonCommand(player, creature.Tile) : null;

        bool PrisonAt(TilePos tile)
        {
            return world.Contains(tile)
            && RoomIndex.RoomAt(world, database, world.TileIndex(tile)) is { Definition.TypeId: RoomTypeId.Prison };
        }
    }
    /// <summary>Binding 11 selects hypnosis only for kind 5 (<c>0x0040FDA0</c>).</summary>
    public void SelectSecondary(CreatureDef definition)
    {
        if (definition.SecondaryAbility == PossessionAbilityKind.Hypnosis)
        {
            Selection = 4;
        }
    }
    /// <summary>The built immediate primary arms: form, sneak at the camera tile and prayer.</summary>
    public PlayerCommand? UsePrimary(PlayerId player, PossessionView view, CreatureDef definition)
    {
        return UsePrimary(player, view, definition, CreatureId.None, CreatureId.None);
    }

    public PlayerCommand? UsePrimary(PlayerId player, PossessionView view, CreatureDef definition, CreatureId target, CreatureId source)
    {
        return definition.PrimaryAbility switch
        {
            PossessionAbilityKind.Sneak => new ToggleSneakCommand(player, view.X >> 12, view.Y >> 12),
            PossessionAbilityKind.ToggleFlag11 => new TogglePossessionFormCommand(player),
            PossessionAbilityKind.Prayer => new TogglePossessionPrayerCommand(player),
            PossessionAbilityKind.Fear => new FrightenPossessedCreatureCommand(player, target, source, new FearAmount(2000)),
            _ => null,
        };
    }

    /// <summary>Sniper's client gate (<c>0x0040FDF0</c>): multiplier above one and an unlocked nonzero slot.</summary>
    public static bool CanToggleSniper(CreatureDef definition, int level)
    {
        if ((uint)definition.SniperZoom.Raw >> 12 <= 1)
        {
            return false;
        }

        for (int i = 0; i < Math.Min(3, definition.Spells.Count); i++)
        {
            if (definition.Spells[i].CreatureSpellId != 0 && definition.Spells[i].LevelAvailable <= level)
            {
                return true;
            }
        }

        return false;
    }
    /// <summary>Special hypnosis selection takes precedence over the ordinary action-26 fall-through.</summary>
    public PlayerCommand SubmitFire(PlayerId player, PossessionView view, CreatureDef definition)
    {
        return !Melee && (Selection & SpecialBits) == 4 && definition.SecondaryAbility == PossessionAbilityKind.Hypnosis
            ? new HypnotiseCommand(player, Aim(view)) : Fire(player, view);
    }

    /// <summary><c>0x004100F0</c>'s action 26, from the camera's point and its yaw and pitch.</summary>
    public ActPossessedCreatureCommand Fire(PlayerId player, PossessionView view)
    {
        return new ActPossessedCreatureCommand(player, Aim(view), Melee, new PossessedSlot(Slot));
    }

    /// <summary>The once-a-turn pose of <c>0x004039A0</c>.</summary>
    public static MovePossessedCreatureCommand Pose(PlayerId player, PossessionView view, in PossessedBody body)
    {
        (int x, int y, int height, int heading) = view.Pose(body);
        return new MovePossessedCreatureCommand(player,
            new PossessionPose(new FpPosition3(new Fp32(x), new Fp32(y), new Fp32(height)), new Dk2Angle((ushort)heading)));
    }

    public static PossessionAim Aim(PossessionView view)
    {
        return new PossessionAim(new FpPosition3(new Fp32(view.X), new Fp32(view.Y), new Fp32(view.Z)),
            new Dk2Angle((ushort)view.Yaw), new Dk2Angle((ushort)view.Pitch));
    }
}
