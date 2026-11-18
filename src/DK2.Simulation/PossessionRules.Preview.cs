// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
namespace DK2.Simulation;

/// <summary>One thing chosen for first-person hover feedback, with its class-local id and owning tile.</summary>
public readonly record struct PossessionTarget(FightTargetKind Kind, int Id, TilePos Tile)
{
    public static PossessionTarget None => default;
}

public static partial class PossessionRules
{
    /// <summary>The per-frame client pick of 0x0040BBE0. Reads the action's target without stamping, attacking or recruiting.</summary>
    public static PossessionTarget PreviewTarget(WorldState world, GameDatabase database, ISightBlocker sight,
        CreatureState me, ActPossessedCreatureCommand action, HeartSites? hearts = null)
    {
        CreatureDef definition = database.CreatureById(me.CreatureTypeId);
        if (action.Melee)
        {
            ChainThing? picked = PickMeleeTarget(world, database, me, definition, action.Aim, hearts);
            return picked?.Thing switch
            {
                CreatureState creature => new(CreatureDeathRules.IsCorpse(creature) ? FightTargetKind.Body : FightTargetKind.Creature,
                    creature.Id, creature.Tile),
                ObjectState thing => new(FightTargetKind.Object, thing.Id, new(thing.TileIndex % world.Width, thing.TileIndex / world.Width)),
                DoorState door => new(FightTargetKind.Door, door.Id, new(door.X, door.Y)),
                TrapState trap => new(FightTargetKind.Trap, trap.Id, new(trap.TileLink.TileIndex % world.Width, trap.TileLink.TileIndex / world.Width)),
                ShotState shot => new(FightTargetKind.Shot, shot.Id, new(shot.PosX >> 12, shot.PosY >> 12)),
                PillarThing pillar => new(FightTargetKind.Heart, pillar.Id.Value, pillar.Target.Tile),
                _ => PossessionTarget.None,
            };
        }
        if (action.Slot.Value == PossessedSlot.FollowerArm)
            return PickFollower(world, database, sight, me, action.Aim) is { } follower
                ? new(FightTargetKind.Creature, follower.Id, follower.Tile) : PossessionTarget.None;
        if (action.Slot.Value >= definition.Spells.Count) return PossessionTarget.None;
        if (!database.CreatureSpells.TryGetValue(definition.Spells[action.Slot.Value].CreatureSpellId, out CreatureSpellDef? spell))
            return PossessionTarget.None;
        SpellTarget target = PickSpellTarget(world, database, sight, me, action.Aim, SpellRules.IsFriendly(spell), hearts);
        TilePos? tile = target.Kind switch
        {
            FightTargetKind.Creature => world.CreatureById(target.Id)?.Tile,
            FightTargetKind.Door => world.DoorById(target.Id) is { } door ? new(door.X, door.Y) : null,
            FightTargetKind.Trap => world.TrapById(target.Id) is { } trap
                ? new(trap.TileLink.TileIndex % world.Width, trap.TileLink.TileIndex / world.Width) : null,
            FightTargetKind.Heart => hearts?.SiteOf(target.Player)?.Pillars.FirstOrDefault(p => HeartPillarId.For(world, p).Value == target.Id).Tile,
            _ => null,
        };
        return tile is { } at ? new(target.Kind, target.Id, at) : PossessionTarget.None;
    }
}
