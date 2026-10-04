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
    Location, UnsupportedTargetSelector, Target, MissingShot, UnsupportedShotProcess, TargetAtMaxHealth, ExistingReaper,
    OneShotReaperUsed, MissingLevelDefinition
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-thunderbolt.md")]
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-create-gold.md")]
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-tremor.md")]
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-turncoat.md")]
public static class KeeperSpellRules
{
    public const byte Thunderbolt = 6;

    public const byte CreateGold = 13;

    /// <summary>Heal (10): shot 4, the Modify Health process, at a creature target selector 1 admits
    /// on a tile location selector 4 admits (keeper-spell-casting.md §4).</summary>
    public const byte Heal = 10;

    /// <summary>Inferno (9): shot 15, process 30, cast at a place location selector 1 admits, target
    /// selector 5 admitting anything (keeper-spell-inferno-chicken.md §2, §4).</summary>
    public const byte Inferno = 9;

    /// <summary>Chicken (11): shot 10, process 15, at a live non-neutral creature (target selector 3)
    /// on a tile location selector 1 admits (keeper-spell-inferno-chicken.md §2, §3).</summary>
    public const byte Chicken = 11;
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
        ShotState shot = ShotRules.Spawn(world, context.Database.Shots[effect.ShotTypeId], cast.PlayerId,
            target.PosX, target.PosY, effect.Data1, effect.Data2, 0,
            FightTargetKind.Creature, target.Id, target.OwnerPlayerId, -1, target.PosX, target.PosY);
        // 0x004BC1CF: a thing-targeting selector forces the shot's first collision onto the target.
        shot.ForcedCollision = TargetsAThing(spell.TargetSelector);
        player.Mana -= spell.ManaCost;
        player.OneShotManaSpent += spell.ManaCost;
        player.Availabilities[row] = availability with
        {
            LastCastTick = context.Tick.Value, LastCheckedTick = context.Tick.Value
        };
        return 0;
    }

    /// <summary>Action 54 through <c>0x004BBD30</c>: permission, the shot at the place, the
    /// payment and both clocks, for the supported place spells. The shot is not launched, so an
    /// Inferno stands where it is made. Returns the original helper's success flag.</summary>
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
        if (cast.SpellId == ReaperRules.OneShotSpellId) player.OneShotReaperUsed = true;
        return 1;
    }

    /// <summary>The position permission helper <c>0x004BB8A0</c> for the spells read: cost,
    /// cooldown, then the location and target validators the record selects.</summary>
    public static KeeperSpellRefusal CanCastAtPlace(SimulationContext context, CastSpellAtPlaceCommand cast)
    {
        if (!ExecutesAtPlace(cast.SpellId)) return KeeperSpellRefusal.UnsupportedSpell;
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
        if (cast.SpellId == ReaperRules.SpellId && ReaperRules.OwnedReaper(world, database, player) is not null)
            return KeeperSpellRefusal.ExistingReaper;
        if (cast.SpellId == ReaperRules.OneShotSpellId && player.OneShotReaperUsed)
            return KeeperSpellRefusal.OneShotReaperUsed;
        var at = new TilePos(cast.X >> 12, cast.Y >> 12);
        context.Navigation.Refresh();
        switch (spell.LocationSelector)
        {
            case 1: if (!LocationOne(world, database, context.Navigation, cast.PlayerId, at)) return KeeperSpellRefusal.Location; break;
            case 4: if (!LocationFour(world, context.Navigation, at)) return KeeperSpellRefusal.Location; break;
            case 5: break;
            default: return KeeperSpellRefusal.UnsupportedLocationSelector;
        }
        if (spell.TargetSelector is not (4 or 5)) return KeeperSpellRefusal.UnsupportedTargetSelector;
        if (!PlaceTargetAllowed(world, database, cast.PlayerId, spell, at)) return KeeperSpellRefusal.Target;
        var effect = spell.Shot(availability.Upgraded);
        if (!database.Shots.TryGetValue(effect.ShotTypeId, out ShotDef? shot)) return KeeperSpellRefusal.MissingShot;
        if (shot.ProcessType is not (ShotRules.CreateObjectProcess or ShotRules.InfernoProcess or ShotRules.TremorProcess or ReaperRules.ProcessType)) return KeeperSpellRefusal.UnsupportedShotProcess;
        if (shot.ProcessType == ReaperRules.ProcessType && effect.Data1 != 0 && database.Level?.OneShotReaperLevel is null)
            return KeeperSpellRefusal.MissingLevelDefinition;
        return KeeperSpellRefusal.None;
    }

    /// <summary>Whether a target selector casts at a thing (<c>0x005177B0</c>: entries 1, 2, 3 and 6
    /// of the runtime table at <c>0x006FCE40</c>) rather than at a place (4 and 5).</summary>
    public static bool TargetsAThing(byte targetSelector) => targetSelector is 1 or 2 or 3 or 6;

    /// <summary>The spells whose casts this simulation executes: Call to Arms, Inferno, Create Gold, Tremor and both Reaper spells at a place,
    /// Thunderbolt, Heal, Chicken and Turncoat at a creature. Create Imp keeps its own command.</summary>
    public static bool ExecutesAtPlace(byte spellId) => spellId is CallToArmsRules.SpellId or Inferno or CreateGold or TremorRules.SpellId or ReaperRules.OneShotSpellId or ReaperRules.SpellId;
    public static bool ExecutesOnCreature(byte spellId) => spellId is Thunderbolt or Heal or Chicken or TurncoatRules.SpellId or PossessionRules.SpellId;

    /// <summary>The place validators of <c>0x004BB8A0</c> for one spell at one tile: its location
    /// selector (1, 4 or 5 read) and target selector 4 or 5. The caller's grid must be current.</summary>
    public static bool PlaceAllowed(WorldState world, GameDatabase database, INavByteGrid navigation,
        byte playerId, KeeperSpellDef spell, TilePos at) =>
        LocationAllowed(world, database, navigation, playerId, spell, at)
        && PlaceTargetAllowed(world, database, playerId, spell, at);

    /// <summary>The place arm of the target validator: 4 (<c>0x005179D0</c>) applies the caster's own
    /// room exclusions; 5 (<c>0x00517F60</c>) admits anything; any other fails closed.</summary>
    private static bool PlaceTargetAllowed(WorldState world, GameDatabase database, byte playerId, KeeperSpellDef spell, TilePos at) =>
        spell.TargetSelector switch
        {
            4 => TargetFour(world, database, playerId, at),
            5 => world.Contains(at),
            _ => false,
        };

    /// <summary>The creature validators of <c>0x004BBA40</c>: the record's location selector at the
    /// target's tile, then its target selector on the creature.</summary>
    public static bool CreatureTargetAllowed(WorldState world, GameDatabase database, INavByteGrid navigation,
        byte playerId, KeeperSpellDef spell, CreatureState target) =>
        LocationAllowed(world, database, navigation, playerId, spell, target.Tile)
        && TargetAllowed(world, database, playerId, spell, target);

    /// <summary>The location validator the record selects (<c>0x005176B0</c>) at one tile: 1
    /// (<c>0x00517B80</c>) and 4 (<c>0x00517F10</c>) are read; any other fails closed.</summary>
    public static bool LocationAllowed(WorldState world, GameDatabase database, INavByteGrid navigation,
        byte playerId, KeeperSpellDef spell, TilePos at) => spell.LocationSelector switch
    {
        1 => LocationOne(world, database, navigation, playerId, at),
        4 => LocationFour(world, navigation, at),
        5 => true,
        _ => false,
    };

    /// <summary>The target validator the record selects (<c>0x00517730</c>) on a creature: 1
    /// (<c>0x00517800</c>), 2 (<c>0x00517930</c>), 3 (<c>0x00517990</c>) and6 are read; any other fails
    /// closed.</summary>
    public static bool TargetAllowed(WorldState world, GameDatabase database, byte playerId, KeeperSpellDef spell,
        CreatureState target) => spell.TargetSelector switch
    {
        1 => TargetOne(world, database, playerId, target),
        2 => TargetTwo(playerId, target),
        3 => target.OwnerPlayerId != PlayerIds.Neutral,
        6 => TargetSix(database, playerId, target),
        _ => false,
    };

    private static bool TargetSix(GameDatabase database, byte playerId, CreatureState target) =>
        !StatusEffectRules.IsStunned(target) && !HandRules.IsHeld(target)
        && target.Dk2StateId is not (193 or 278 or 279 or 280)
        && !target.CombatPitFighter && !ChickenRules.IsChicken(target)
        && !database.CreatureById(target.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsHorny)
        && !target.RebelActive && target.OwnerPlayerId == playerId;

    /// <summary>Target 1, flags zero: a creature holding a room id is admitted when that room is the
    /// caster's; a Combat Pit fighter when the room under it is; otherwise the caster's own creature,
    /// unless its kind is <c>IsHorny</c>. This simulation keeps no room id on a creature, so an
    /// attached creature's room owner is read from the floor it stands on.</summary>
    private static bool TargetOne(WorldState world, GameDatabase database, byte playerId, CreatureState target)
    {
        if (RoomMembershipRules.IsAttached(target))
            return world.Contains(target.Tile) && world.Tile(target.Tile).OwnerPlayerId == playerId;
        if (target.CombatPitFighter && world.Contains(target.Tile))
        {
            int index = target.Tile.Y * world.Width + target.Tile.X;
            if (RoomIndex.RoomAt(world, database, index) is not null && world.TileAtIndex(index).OwnerPlayerId == playerId) return true;
        }
        return !database.CreatureById(target.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsHorny) && target.OwnerPlayerId == playerId;
    }

    /// <summary>Target 2, flags zero: a creature owned by neither the caster nor neutral; allies and
    /// dying creatures included.</summary>
    private static bool TargetTwo(byte playerId, CreatureState target) =>
        target.OwnerPlayerId != playerId && target.OwnerPlayerId != PlayerIds.Neutral;

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

    /// <summary>The creature permission helper <c>0x004BBA40</c> for the spells read, then the
    /// cast helper's own maximum-health test: what the command would refuse before paying.</summary>
    public static KeeperSpellRefusal CanCast(SimulationContext context, CastSpellOnCreatureCommand cast)
    {
        // The researched creature casts share this boundary; Create Imp retains its own command.
        if (!ExecutesOnCreature(cast.SpellId)) return KeeperSpellRefusal.UnsupportedSpell;
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
        if (target is null || !CreatureStateMachine.HasState(target)) return KeeperSpellRefusal.MissingCreature;
        if (spell.LocationSelector is not (1 or 4 or 5)) return KeeperSpellRefusal.UnsupportedLocationSelector;
        context.Navigation.Refresh();
        if (!LocationAllowed(world, database, context.Navigation, cast.PlayerId, spell, target.Tile)) return KeeperSpellRefusal.Location;
        if (spell.TargetSelector is not (1 or 2 or 3 or 6)) return KeeperSpellRefusal.UnsupportedTargetSelector;
        if (!TargetAllowed(world, database, cast.PlayerId, spell, target)) return KeeperSpellRefusal.Target;
        var effect = spell.Shot(availability.Upgraded);
        if (!database.Shots.TryGetValue(effect.ShotTypeId, out ShotDef? shot)) return KeeperSpellRefusal.MissingShot;
        if (shot.ProcessType is not (ShotRules.LightningProcess or ShotRules.ModifyHealthProcess or ShotRules.ChickenProcess or TurncoatRules.ProcessType or PossessionRules.ProcessType))
            return KeeperSpellRefusal.UnsupportedShotProcess;
        // 0x004BC13C, after the permission and before the shot and the payment: a positive Modify
        // Health carrier wants its target strictly below the level-scaled maximum.
        if (shot.ProcessType == ShotRules.ModifyHealthProcess && effect.Data1 > 0
            && target.Health >= CreatureHealthRules.MaxHealth(database, database.CreatureById(target.CreatureTypeId), target))
            return KeeperSpellRefusal.TargetAtMaxHealth;
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
