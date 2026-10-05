// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;

namespace DK2.Simulation;

/// <summary>The room messages 41 to 43 posted where a Treasury, a Lair or a Hatchery is looked for
/// and not found: the gold carrier's store chooser, the gold a sale could not store, the Lair finder
/// and the Hatchery finder (computer-keeper-messages.md section 2). Each producer has its own gates.</summary>
/// <remarks>Not built here: the other room finders (Combat Pit, Graveyard, Library,
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

    /// <summary>The Hatchery finder found none (<c>0x004DED00</c>), with Hatchery availability 3: no
    /// Hatchery posts 41; else one holding a chicken that no route ignoring doors reaches posts 43;
    /// else an empty one the respect-doors route reaches, that has laid an egg and is more than
    /// <see cref="FullRoomGraceSeconds"/> past its last size change, posts 42. A reachable one holding
    /// a chicken is one DK2 would have chosen. Data1 is Hatchery.</summary>
    /// <remarks>DK2 withholds all three while a build or sale wave is queued (<c>0x004C7340</c>,
    /// <c>0x004C7360</c>); as for the Lair, ours never has one queued between ticks.</remarks>
    public static PlayerMessage? NoHatcheryFor(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        byte player = creature.OwnerPlayerId;
        if (world.Availability(player, RoomAvailabilityKind, (int)RoomTypeId.Hatchery) != Available) return null;

        var hatcheries = RoomIndex.Build(world, context.Database)
            .Where(r => EatingRules.IsHatcheryOf(r, player) && r.TileIndices.Count > 0).ToList();
        if (hatcheries.Count == 0) return Post(context, player, PlayerMessageKind.RoomNeeded, RoomTypeId.Hatchery);

        bool emptyAndSettled = false;
        long grace = SimulationTiming.TicksFromSeconds(FullRoomGraceSeconds);
        foreach (RoomInstance hatchery in hatcheries)
        {
            if (!CanReachRoom(context, creature, definition, hatchery, MoveOptions.Loose)) continue;
            if (EatingRules.HoldsAChicken(world, hatchery)) return null;
            if (HatcheryRules.ChickensGenerated(world, hatchery) != 0
                && world.CurrentTick.Value - SlabChain.SizeChangedTick(world, hatchery) > grace)
                emptyAndSettled = true;
        }

        foreach (RoomInstance hatchery in hatcheries)
            if (EatingRules.HoldsAChicken(world, hatchery)
                && !CanReachRoom(context, creature, definition, hatchery, IgnoreDoors))
                return Post(context, player, PlayerMessageKind.RoomUnreachable, RoomTypeId.Hatchery);

        return emptyAndSettled ? Post(context, player, PlayerMessageKind.RoomFull, RoomTypeId.Hatchery) : null;
    }

    /// <summary>The creature type <c>0x004F2B80</c> tests (<c>+0x36F == 4</c>): the Mistress, whose
    /// missing Torture Chamber is message 99, not 41, when its owner (<c>+0x28</c>) is the searching
    /// player, as it always is here: ours searches for the creature's owner. Ours does not carry 99.</summary>
    public const byte MistressCreatureId = 4;

    /// <summary>A job-room finder found no room (computer-keeper-messages.md §2): Training
    /// <c>0x004F3560</c>, Temple <c>0x004F1A20</c>, Torture <c>0x004F2B80</c>, Casino <c>0x004D75F0</c>
    /// with its availability row, Library <c>0x004E2500</c> with the Lair's, and Workshop
    /// <c>0x004F6200</c>. No room posts 41; else a room with space no route ignoring doors reaches posts
    /// 43; else a reached full room past <see cref="FullRoomGraceSeconds"/> posts 42 when its
    /// <c>+0xA3</c> holds (<see cref="IsEquipped"/>), 44 naming it when not.</summary>
    /// <remarks>Built only to 41 for the Library (storage and research-point gates read, their state
    /// not kept here) and the Workshop (player byte <c>+0xA1C</c> unread); the Casino's party branch
    /// (player <c>+0xF4A</c>) is taken as off. The Mistress posts 99 for a missing chamber, which ours
    /// does not carry. 44 names the last flagged room in our room order, not DK2's room list.</remarks>
    public static PlayerMessage? NoJobRoomFor(
        SimulationContext context, CreatureState creature, CreatureDef definition, RoomTypeId wants)
    {
        if (wants is not (RoomTypeId.TrainingRoom or RoomTypeId.Temple or RoomTypeId.TortureChamber
            or RoomTypeId.Casino or RoomTypeId.Library or RoomTypeId.Workshop)) return null;
        WorldState world = context.World;
        byte player = creature.OwnerPlayerId;
        RoomTypeId row = wants == RoomTypeId.Library ? RoomTypeId.Lair : wants;
        if (world.Availability(player, RoomAvailabilityKind, (int)row) != Available) return null;

        var rooms = RoomIndex.Build(world, context.Database)
            .Where(r => r.OwnerPlayerId == player && r.Definition.TypeId == wants && r.TileIndices.Count > 0).ToList();
        if (rooms.Count == 0)
            return wants == RoomTypeId.TortureChamber && creature.CreatureTypeId == MistressCreatureId
                ? null : Post(context, player, PlayerMessageKind.RoomNeeded, wants);
        if (wants is RoomTypeId.Library or RoomTypeId.Workshop) return null;

        bool full = false;
        RoomInstance? unequipped = null;
        long grace = SimulationTiming.TicksFromSeconds(FullRoomGraceSeconds);
        foreach (RoomInstance room in rooms)
        {
            if (!CanReachRoom(context, creature, definition, room, MoveOptions.Loose)) continue;
            if (HasPlace(world, context.Database, room, creature)) return null; // DK2 would have taken it
            if (wants == RoomTypeId.TortureChamber && RoomCapacityRules.HasRoom(world, context.Database, room, creature.Id)) continue;
            if (world.CurrentTick.Value - SlabChain.SizeChangedTick(world, room) <= grace) continue;
            if (IsEquipped(world, room)) full = true;
            else unequipped = room;
        }

        foreach (RoomInstance room in rooms)
            if (HasPlace(world, context.Database, room, creature, forUnreachable: true)
                && !CanReachRoom(context, creature, definition, room, IgnoreDoors))
                return Post(context, player, PlayerMessageKind.RoomUnreachable, wants);
        if (full) return Post(context, player, PlayerMessageKind.RoomFull, wants);
        return unequipped is null ? null
            : Post(context, player, PlayerMessageKind.RoomUnequipped, wants, SlabChain.FirstSlab(world, unequipped));
    }

    /// <summary>The finder's place test: two creatures' units for a Torture Chamber; a Casino passes
    /// with a lever (<c>+0x53</c>) as well, except for 43, which wants both.</summary>
    private static bool HasPlace(WorldState world, GameDatabase database, RoomInstance room, CreatureState creature,
        bool forUnreachable = false)
    {
        int units = room.Definition.TypeId == RoomTypeId.TortureChamber ? 2 * RoomCapacityRules.Units : RoomCapacityRules.Units;
        bool place = RoomCapacityRules.HasRoom(world, database, room, creature.Id, units);
        if (room.Definition.TypeId != RoomTypeId.Casino) return place;
        bool lever = CasinoRules.Lever(world, room) is not null;
        return forUnreachable ? place && lever : place || lever;
    }

    /// <summary>Room <c>+0xA3</c> as each type's after-tile-change pass sets it: a Training Dummy (14)
    /// or Target (29) attached (<c>0x004F2EA0</c>), a Torture Wheel or Electric Chair
    /// (<c>0x004F1F10</c>), any interior tile for a Casino (<c>0x004D6ED0</c>); a Temple's is always 0.</summary>
    /// <remarks>Deviates: DK2 stores the flag when the pass runs, at a rebuild; ours reads the
    /// furniture now, so 42 and 44 can differ while an object is placed or removed between rebuilds.</remarks>
    public static bool IsEquipped(WorldState world, RoomInstance room) => room.Definition.TypeId switch
    {
        RoomTypeId.TrainingRoom => HasAttached(world, room, 14, 29),
        RoomTypeId.TortureChamber => HasAttached(world, room, TortureRules.WheelObjectId, TortureRules.ElectricChairObjectId),
        RoomTypeId.Casino => room.InteriorTileCount > 0,
        _ => false,
    };

    private static bool HasAttached(WorldState world, RoomInstance room, byte first, byte second)
    {
        foreach (ObjectState thing in world.Objects)
            if (thing.AutoPlaced && (thing.ObjectTypeId == first || thing.ObjectTypeId == second)
                && EatingRules.Contains(room, thing.TileIndex))
                return true;
        return false;
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
    private static PlayerMessage? Post(SimulationContext context, byte player, PlayerMessageKind kind, RoomTypeId room, int roomTag = 0)
    {
        IReadOnlyList<PlayerMessage> entries = context.World.Messages.Entries;
        uint lastBefore = entries.Count == 0 ? 0 : entries[^1].Id;
        PlayerMessage? posted = context.World.Messages.PostRoom(context.World, player, kind, room, roomTag);
        if (posted is PlayerMessage message && message.Id != lastBefore && entries[^1].Id == message.Id)
            context.Emit(nameof(RoomNeedRules), "room_message",
                $"player={player} kind={(int)kind} room={room} id={message.Id}");
        return posted;
    }
}
