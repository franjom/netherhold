// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
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
        if (!LocationOne(context, cast.PlayerId, target.Tile)) return KeeperSpellRefusal.Location;
        if (spell.TargetSelector != 2) return KeeperSpellRefusal.UnsupportedTargetSelector;
        // Target selector 2, flags zero: foreign nonneutral, including allies and dying creatures.
        if (target.OwnerPlayerId == cast.PlayerId || target.OwnerPlayerId == PlayerIds.Neutral)
            return KeeperSpellRefusal.Target;
        var effect = spell.Shot(availability.Upgraded);
        if (!database.Shots.TryGetValue(effect.ShotTypeId, out ShotDef? shot)) return KeeperSpellRefusal.MissingShot;
        if (shot.ProcessType != ShotRules.LightningProcess) return KeeperSpellRefusal.UnsupportedShotProcess;
        return KeeperSpellRefusal.None;
    }

    private static bool LocationOne(SimulationContext context, byte playerId, TilePos at)
    {
        WorldState world = context.World;
        if (!world.Contains(at)) return false;
        int index = at.Y * world.Width + at.X;
        byte owner = world.TileAtIndex(index).OwnerPlayerId;
        if (owner == PlayerIds.Neutral || !world.AreAllied(playerId, owner)) return false;
        context.Navigation.Refresh();
        if ((context.Navigation.NavByte(at.X, at.Y) & 8) != 0) return false;
        RoomInstance? room = RoomIndex.RoomAt(world, context.Database, index);
        if (room is null) return true;
        switch (room.Definition.TypeId)
        {
            case RoomTypeId.Portal:
                return !room.IsInterior(index);
            case RoomTypeId.DungeonHeart:
                int first = SlabChain.FirstSlab(world, room);
                int dx = at.X - first % world.Width, dy = at.Y - first / world.Width;
                return !(dx > 0 && dx < 4 && dy > 0 && dy < 4);
            case RoomTypeId.Prison:
            case RoomTypeId.Temple:
            case RoomTypeId.CombatPit:
                for (int dy8 = -1; dy8 <= 1; dy8++)
                    for (int dx8 = -1; dx8 <= 1; dx8++)
                    {
                        int x = at.X + dx8, y = at.Y + dy8;
                        if (!world.Contains(x, y) || !room.TileIndices.Contains(y * world.Width + x)) return true;
                    }
                return false;
            default:
                return true;
        }
    }
}
