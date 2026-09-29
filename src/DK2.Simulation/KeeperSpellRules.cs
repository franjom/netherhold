// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;

namespace DK2.Simulation;

public enum KeeperSpellRefusal
{
    None, UnsupportedSpell, UnsupportedFlags, MissingDefinition, MissingPlayer,
    NoSpellEntry, Cooldown, Mana, MissingCreature, UnsupportedLocationSelector,
    Location, UnsupportedTargetSelector, Target, MissingShot, UnsupportedShotProcess
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-thunderbolt.md")]
public static class KeeperSpellRules
{
    public const byte Thunderbolt = 6;
    public const ushort SpellAvailabilityKind = 5;

    // Zero is the original helper's result even when creation and payment succeeded.
    // The separate refusal describes our command outcome, never the original return.
    public static int CastOnCreature(SimulationContext context, CastSpellOnCreatureCommand cast,
        out KeeperSpellRefusal refusal)
    {
        refusal = CanCast(context, cast);
        if (refusal != KeeperSpellRefusal.None) return 0;
        WorldState world = context.World;
        KeeperSpellDef spell = context.Database.KeeperSpellById(cast.SpellId)!;
        CreatureState target = world.CreatureById(cast.CreatureId)!;
        ref PlayerState player = ref world.Player(cast.PlayerId);
        int row = player.Availabilities.FindIndex(a => a.Kind == SpellAvailabilityKind && a.TypeId == cast.SpellId);
        AvailabilityState availability = player.Availabilities[row];
        var effect = spell.Shot(availability.Upgraded);
        ShotRules.Spawn(world, context.Database.Shots[effect.ShotTypeId], cast.PlayerId,
            target.PosX, target.PosY, effect.Data1, effect.Data2, 0,
            FightTargetKind.Creature, target.Id, target.OwnerPlayerId, -1, target.PosX, target.PosY);
        player.Mana -= spell.ManaCost;
        player.OneShotManaSpent += spell.ManaCost;
        player.Availabilities[row] = availability with
        {
            LastCastTick = context.Tick.Value, LastCheckedTick = context.Tick.Value
        };
        return 0;
    }

    /// <summary>Action 54 through <c>0x004BBD30</c>: permission, the shot at the place, the
    /// payment and both clocks. Only Call to Arms (3) is read end to end. Returns the original
    /// helper's success flag.</summary>
    public static int CastAtPlace(SimulationContext context, CastSpellAtPlaceCommand cast, out KeeperSpellRefusal refusal)
    {
        refusal = CanCastAtPlace(context, cast);
        if (refusal != KeeperSpellRefusal.None) return 0;
        WorldState world = context.World;
        KeeperSpellDef spell = context.Database.KeeperSpellById(cast.SpellId)!;
        ref PlayerState player = ref world.Player(cast.PlayerId);
        int row = player.Availabilities.FindIndex(a => a.Kind == SpellAvailabilityKind && a.TypeId == cast.SpellId);
        AvailabilityState availability = player.Availabilities[row];
        var effect = spell.Shot(availability.Upgraded);
        ShotRules.Spawn(world, context.Database.Shots[effect.ShotTypeId], cast.PlayerId,
            cast.X, cast.Y, effect.Data1, effect.Data2, 0, FightTargetKind.None, -1, cast.PlayerId, -1, cast.X, cast.Y);
        player.Mana -= spell.ManaCost;
        player.OneShotManaSpent += spell.ManaCost;
        player.Availabilities[row] = availability with
        {
            LastCastTick = context.Tick.Value, LastCheckedTick = context.Tick.Value
        };
        return 1;
    }

    /// <summary>The position permission helper <c>0x004BB8A0</c> for the spells read: cost,
    /// cooldown, then the location and target validators the record selects.</summary>
    public static KeeperSpellRefusal CanCastAtPlace(SimulationContext context, CastSpellAtPlaceCommand cast)
    {
        if (cast.SpellId != CallToArmsRules.SpellId) return KeeperSpellRefusal.UnsupportedSpell;
        if (cast.Flags != 0) return KeeperSpellRefusal.UnsupportedFlags;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        KeeperSpellDef? spell = database.KeeperSpellById(cast.SpellId);
        if (spell is null) return KeeperSpellRefusal.MissingDefinition;
        if (!world.HasPlayer(cast.PlayerId)) return KeeperSpellRefusal.MissingPlayer;
        ref PlayerState player = ref world.Player(cast.PlayerId);
        int row = player.Availabilities.FindIndex(a => a.Kind == SpellAvailabilityKind && a.TypeId == cast.SpellId);
        if (row < 0) return KeeperSpellRefusal.NoSpellEntry;
        AvailabilityState availability = player.Availabilities[row];
        if (spell.ManaCost != 0 && spell.ManaCost > player.Mana) return KeeperSpellRefusal.Mana;
        long cooldown = (uint)unchecked(spell.CooldownRaw * SimulationTiming.OriginalTicksPerGameSecond) >> 12;
        if (availability.LastCastTick != 0 && context.Tick.Value - availability.LastCastTick < cooldown)
            return KeeperSpellRefusal.Cooldown;
        var at = new TilePos(cast.X >> 12, cast.Y >> 12);
        context.Navigation.Refresh();
        switch (spell.LocationSelector)
        {
            case 1: if (!LocationOne(world, database, context.Navigation, cast.PlayerId, at)) return KeeperSpellRefusal.Location; break;
            case 4: if (!LocationFour(world, context.Navigation, at)) return KeeperSpellRefusal.Location; break;
            default: return KeeperSpellRefusal.UnsupportedLocationSelector;
        }
        if (spell.TargetSelector != 4) return KeeperSpellRefusal.UnsupportedTargetSelector;
        if (!TargetFour(world, database, cast.PlayerId, at)) return KeeperSpellRefusal.Target;
        var effect = spell.Shot(availability.Upgraded);
        if (!database.Shots.TryGetValue(effect.ShotTypeId, out ShotDef? shot)) return KeeperSpellRefusal.MissingShot;
        if (shot.ProcessType != ShotRules.CreateObjectProcess) return KeeperSpellRefusal.UnsupportedShotProcess;
        return KeeperSpellRefusal.None;
    }

    /// <summary>Whether a target selector casts at a thing (<c>0x005177B0</c>: entries 1, 2, 3 and 6
    /// of the runtime table at <c>0x006FCE40</c>) rather than at a place (4 and 5).</summary>
    public static bool TargetsAThing(byte targetSelector) => targetSelector is 1 or 2 or 3 or 6;

    /// <summary>The spells whose casts this simulation executes: Call to Arms at a place and
    /// Thunderbolt at a creature. Create Imp keeps its own command.</summary>
    public static bool ExecutesAtPlace(byte spellId) => spellId == CallToArmsRules.SpellId;
    public static bool ExecutesOnCreature(byte spellId) => spellId == Thunderbolt;

    /// <summary>The place validators of <c>0x004BB8A0</c> for one spell at one tile: its location
    /// selector (1 or 4 read) and target selector 4. The caller's grid must be current.</summary>
    public static bool PlaceAllowed(WorldState world, GameDatabase database, INavByteGrid navigation,
        byte playerId, KeeperSpellDef spell, TilePos at)
    {
        bool location = spell.LocationSelector switch
        {
            1 => LocationOne(world, database, navigation, playerId, at),
            4 => LocationFour(world, navigation, at),
            _ => false,
        };
        return location && spell.TargetSelector == 4 && TargetFour(world, database, playerId, at);
    }

    /// <summary>The creature validators of <c>0x004BBA40</c> for the spells read: location 1 at the
    /// target's tile and target selector 2, a foreign non-neutral creature.</summary>
    public static bool CreatureTargetAllowed(WorldState world, GameDatabase database, INavByteGrid navigation,
        byte playerId, KeeperSpellDef spell, CreatureState target) =>
        spell.LocationSelector == 1 && spell.TargetSelector == 2
        && target.OwnerPlayerId != playerId && target.OwnerPlayerId != PlayerIds.Neutral
        && LocationOne(world, database, navigation, playerId, target.Tile);

    /// <summary>Location 4 (<c>0x00517F10</c>): no ownership; the navigation grid's solid bit
    /// refuses, and cast flag bit 1 would also want tile flag <c>0x2000</c>.</summary>
    private static bool LocationFour(WorldState world, INavByteGrid navigation, TilePos at)
    {
        if (!world.Contains(at)) return false;
        return (navigation.NavByte(at.X, at.Y) & 8) == 0;
    }

    /// <summary>Target 4 at a place (<c>0x005179D0</c>): a room of the caster's own applies the
    /// Portal, Heart, Prison, Temple and Combat Pit interior exclusions; anything else passes.</summary>
    private static bool TargetFour(WorldState world, GameDatabase database, byte playerId, TilePos at)
    {
        if (!world.Contains(at)) return false;
        int index = at.Y * world.Width + at.X;
        RoomInstance? room = RoomIndex.RoomAt(world, database, index);
        if (room is null || world.TileAtIndex(index).OwnerPlayerId != playerId) return true;
        return !RoomExcludes(world, room, at, index);
    }

    public static KeeperSpellRefusal CanCast(SimulationContext context, CastSpellOnCreatureCommand cast)
    {
        // Other keeper effects have not been read end to end; Create Imp retains its own command.
        if (cast.SpellId != Thunderbolt) return KeeperSpellRefusal.UnsupportedSpell;
        // The ordinary packet path is read; bypass/inverted targeting is a separate boundary.
        if (cast.Flags != 0) return KeeperSpellRefusal.UnsupportedFlags;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        KeeperSpellDef? spell = database.KeeperSpellById(cast.SpellId);
        if (spell is null) return KeeperSpellRefusal.MissingDefinition;
        if (!world.HasPlayer(cast.PlayerId)) return KeeperSpellRefusal.MissingPlayer;
        ref PlayerState player = ref world.Player(cast.PlayerId);
        int row = player.Availabilities.FindIndex(a => a.Kind == SpellAvailabilityKind && a.TypeId == cast.SpellId);
        // Availability is not tested: neither action 55 nor 0x004BBF90 reads it, and the
        // interface or the computer keeper gates it, as for Create Imp (ADR-0247, ADR-0260
        // amended). The entry is still needed: it holds the clocks and the Upgraded flag.
        if (row < 0) return KeeperSpellRefusal.NoSpellEntry;
        AvailabilityState availability = player.Availabilities[row];
        long cooldown = (uint)unchecked(spell.CooldownRaw * SimulationTiming.OriginalTicksPerGameSecond) >> 12;
        if (availability.LastCastTick != 0 && context.Tick.Value - availability.LastCastTick < cooldown)
            return KeeperSpellRefusal.Cooldown;
        if (spell.ManaCost != 0 && spell.ManaCost > player.Mana) return KeeperSpellRefusal.Mana;
        CreatureState? target = world.CreatureById(cast.CreatureId);
        if (target is null || target.Activity == CreatureActivity.Dead) return KeeperSpellRefusal.MissingCreature;
        if (spell.LocationSelector != 1) return KeeperSpellRefusal.UnsupportedLocationSelector;
        context.Navigation.Refresh();
        if (!LocationOne(world, database, context.Navigation, cast.PlayerId, target.Tile)) return KeeperSpellRefusal.Location;
        if (spell.TargetSelector != 2) return KeeperSpellRefusal.UnsupportedTargetSelector;
        // Target selector 2, flags zero: foreign nonneutral, including allies and dying creatures.
        if (target.OwnerPlayerId == cast.PlayerId || target.OwnerPlayerId == PlayerIds.Neutral)
            return KeeperSpellRefusal.Target;
        var effect = spell.Shot(availability.Upgraded);
        if (!database.Shots.TryGetValue(effect.ShotTypeId, out ShotDef? shot)) return KeeperSpellRefusal.MissingShot;
        if (shot.ProcessType != ShotRules.LightningProcess) return KeeperSpellRefusal.UnsupportedShotProcess;
        return KeeperSpellRefusal.None;
    }

    private static bool LocationOne(WorldState world, GameDatabase database, INavByteGrid navigation, byte playerId, TilePos at)
    {
        if (!world.Contains(at)) return false;
        int index = at.Y * world.Width + at.X;
        byte owner = world.TileAtIndex(index).OwnerPlayerId;
        if (owner == PlayerIds.Neutral || !world.AreAllied(playerId, owner)) return false;
        if ((navigation.NavByte(at.X, at.Y) & 8) != 0) return false;
        RoomInstance? room = RoomIndex.RoomAt(world, database, index);
        return room is null || !RoomExcludes(world, room, at, index);
    }

    /// <summary>The room-position exclusions the validators share: a Portal's interior, the
    /// Heart's three-by-three middle, and a Prison, Temple or Combat Pit slab with all eight
    /// neighbours in the room.</summary>
    private static bool RoomExcludes(WorldState world, RoomInstance room, TilePos at, int index)
    {
        switch (room.Definition.TypeId)
        {
            case RoomTypeId.Portal:
                return room.IsInterior(index);
            case RoomTypeId.DungeonHeart:
                int first = SlabChain.FirstSlab(world, room);
                int dx = at.X - first % world.Width, dy = at.Y - first / world.Width;
                return dx > 0 && dx < 4 && dy > 0 && dy < 4;
            case RoomTypeId.Prison:
            case RoomTypeId.Temple:
            case RoomTypeId.CombatPit:
                for (int dy8 = -1; dy8 <= 1; dy8++)
                    for (int dx8 = -1; dx8 <= 1; dx8++)
                    {
                        int x = at.X + dx8, y = at.Y + dy8;
                        if (!world.Contains(x, y) || !room.TileIndices.Contains(y * world.Width + x)) return false;
                    }
                return true;
            default:
                return false;
        }
    }
}
