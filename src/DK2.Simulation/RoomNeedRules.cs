// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;

namespace DK2.Simulation;

/// <summary>The room messages 41 to 43 posted where a Treasury or a Lair is looked for and not
/// found: the gold carrier's store chooser, the gold a sale could not store and the Lair finder
/// (computer-keeper-messages.md section 2). Each producer has its own gates; they are not one rule.</summary>
/// <remarks>Not built here: the other room finders (Hatchery, Combat Pit, Graveyard, Library,
/// Casino, Temple, Torture, Training, Workshop), the claim failure <c>0x004E5A70</c>, the rescue
/// selector <c>0x0046D810</c> and <c>0x004BA420</c>'s callers other than the room sale.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-messages.md")]
public static class RoomNeedRules
{
    /// <summary>The availability kind of a room row (<c>0x004BAB20</c>).</summary>
    public const ushort RoomAvailabilityKind = 1;

    /// <summary>The availability state a buildable room has.</summary>
    public const int Available = 3;

    /// <summary>A full Lair's 42 waits until strictly more than this has passed since the room last
    /// changed size (<c>room+0x9F</c>).</summary>
    public const int FullRoomGraceSeconds = 4;

    /// <summary>The gold carrier's store chooser found nowhere (<c>0x00464400</c>): with no Treasury,
    /// 41 when Treasury availability is 3; otherwise 42 when no Treasury or unruined Heart has space,
    /// else 43 when none with space is reachable by the respect-doors route to its first slab. A store
    /// it could reach is one DK2 would have walked to, so nothing is posted. Data1 is Treasury.</summary>
    public static PlayerMessage? NoStoreForCarrier(SimulationContext context, CreatureState carrier)
    {
        WorldState world = context.World;
        byte player = carrier.OwnerPlayerId;
        CreatureDef definition = context.Database.CreatureById(carrier.CreatureTypeId);
        bool treasury = false, space = false, reachable = false;
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (room.OwnerPlayerId != player) continue;
            if (room.Definition.TypeId == RoomTypeId.Treasury) treasury = true;
            else if (room.Definition.TypeId != RoomTypeId.DungeonHeart || world.Player(player).HeartRuined) continue;
            if (!room.TileIndices.Any(tile => GoldStore.SpaceIn(world, context.Database, tile) > 0)) continue;
            space = true;
            int first = SlabChain.FirstSlab(world, room);
            if (CanRoute(context, carrier, definition, new TilePos(first % world.Width, first / world.Width), MoveOptions.Loose))
                reachable = true;
        }

        if (!treasury)
            return world.Availability(player, RoomAvailabilityKind, (int)RoomTypeId.Treasury) == Available
                ? Post(context, player, PlayerMessageKind.RoomNeeded, RoomTypeId.Treasury)
                : null;
        if (!space) return Post(context, player, PlayerMessageKind.RoomFull, RoomTypeId.Treasury);
        return reachable ? null : Post(context, player, PlayerMessageKind.RoomUnreachable, RoomTypeId.Treasury);
    }

    /// <summary>Gold paid in at a tile that no Treasury or Heart could take (<c>0x004BA420</c>, after its
    /// spread over the stores): when the Treasury's availability is anything but 1, 41 with no Treasury
    /// and 42 with one. Data1 is Treasury.</summary>
    public static PlayerMessage? GoldLeftOver(SimulationContext context, byte player)
    {
        WorldState world = context.World;
        if (world.Availability(player, RoomAvailabilityKind, (int)RoomTypeId.Treasury) == 1) return null;
        bool treasury = RoomIndex.Build(world, context.Database)
            .Any(r => r.OwnerPlayerId == player && r.Definition.TypeId == RoomTypeId.Treasury);
        return Post(context, player, treasury ? PlayerMessageKind.RoomFull : PlayerMessageKind.RoomNeeded, RoomTypeId.Treasury);
    }

    /// <summary>The Lair finder found none (<c>0x004E0470</c>), with Lair availability 3: no Lair posts
    /// 41; else a Lair with a free slot that no route ignoring doors reaches posts 43; else a full Lair
    /// the respect-doors route reaches, more than <see cref="FullRoomGraceSeconds"/> past its last size
    /// change, posts 42. A reachable Lair with a slot is one DK2 would have chosen. Data1 is Lair.</summary>
    /// <remarks>DK2 also withholds 41 while a build or sale wave is queued (<c>0x004C7340</c>,
    /// <c>0x004C7360</c>). Ours lays a whole wave in its command's tick, so none is ever queued
    /// between ticks and the gate has nothing to test.</remarks>
    public static PlayerMessage? NoLairFor(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        byte player = creature.OwnerPlayerId;
        if (world.Availability(player, RoomAvailabilityKind, (int)RoomTypeId.Lair) != Available) return null;

        var lairs = RoomIndex.Build(world, context.Database)
            .Where(r => r.OwnerPlayerId == player && LairRules.IsLairRoom(context.Database, r)).ToList();
        if (lairs.Count == 0) return Post(context, player, PlayerMessageKind.RoomNeeded, RoomTypeId.Lair);

        bool fullAndSettled = false;
        long grace = SimulationTiming.TicksFromSeconds(FullRoomGraceSeconds);
        foreach (RoomInstance lair in lairs)
        {
            if (!CanReachRoom(context, creature, definition, lair, MoveOptions.Loose)) continue;
            if (LairRules.HasFreeSlot(world, context.Database, lair)) return null;
            if (world.CurrentTick.Value - SlabChain.SizeChangedTick(world, lair) > grace) fullAndSettled = true;
        }

        foreach (RoomInstance lair in lairs)
            if (LairRules.HasFreeSlot(world, context.Database, lair)
                && !CanReachRoom(context, creature, definition, lair, IgnoreDoors))
                return Post(context, player, PlayerMessageKind.RoomUnreachable, RoomTypeId.Lair);

        return fullAndSettled ? Post(context, player, PlayerMessageKind.RoomFull, RoomTypeId.Lair) : null;
    }

    /// <summary>The unreachable test's option word, 2: own locked doors passed, no door rule.</summary>
    private const int IgnoreDoors = 0x02;

    /// <summary><c>0x00471740</c>: a point in the room (<c>0x004E5000</c>, the first standable tile along
    /// its chain) and the creature's route test to it (<c>0x004D0E60</c>) with the given options.</summary>
    private static bool CanReachRoom(SimulationContext context, CreatureState creature, CreatureDef definition,
        RoomInstance room, int options)
    {
        int? point = StealGoldRules.FirstStandable(context.World, context.Database, definition, room);
        if (point is not int tile) return false;
        int w = context.World.Width;
        return CanRoute(context, creature, definition, new TilePos(tile % w, tile / w), options);
    }

    /// <summary><c>0x004D0E60</c> without loading a route: the creature's position to the tile's centre.</summary>
    private static bool CanRoute(SimulationContext context, CreatureState creature, CreatureDef definition, TilePos tile, int options)
    {
        if (creature.Tile == tile) return true;
        var from = new Steering.Fixed16Vector(creature.PosX << 4, creature.PosY << 4);
        var to = new Steering.Fixed16Vector(CreatureState.CentreOf(tile.X) << 4, CreatureState.CentreOf(tile.Y) << 4);
        return context.Navigation.Route(from, to, WorldSteering.ProfileOf(definition, creature.OwnerPlayerId, options), []);
    }

    /// <summary>Posts, and reports a new record; a retained duplicate comes back unchanged and unreported.</summary>
    private static PlayerMessage? Post(SimulationContext context, byte player, PlayerMessageKind kind, RoomTypeId room)
    {
        IReadOnlyList<PlayerMessage> entries = context.World.Messages.Entries;
        uint lastBefore = entries.Count == 0 ? 0 : entries[^1].Id;
        PlayerMessage? posted = context.World.Messages.PostRoom(context.World, player, kind, room);
        if (posted is PlayerMessage message && message.Id != lastBefore && entries[^1].Id == message.Id)
            context.Emit(nameof(RoomNeedRules), "room_message",
                $"player={player} kind={(int)kind} room={room} id={message.Id}");
        return posted;
    }
}
