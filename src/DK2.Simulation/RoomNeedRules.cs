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
/// <remarks>Also the rescue selector's (<c>0x0046D810</c>), the job-room finders' and both Combat Pit
/// finders'; the corpse selections' are <see cref="GraveyardRules.ForCorpse"/> and the claim
/// failure's (<c>0x004E5A70</c>) <see cref="RoomCapacityRules"/>. Not built: <c>0x004BA420</c>'s
/// callers other than the room sale.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-messages.md")]
public static class RoomNeedRules
{
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
        PlayerId player = carrier.OwnerPlayerId;
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
            return world.Availability(player, AvailabilityKind.Room, (int)RoomTypeId.Treasury) == Available
                ? Post(context, player, PlayerMessageKind.RoomNeeded, RoomTypeId.Treasury)
                : null;
        if (!space) return Post(context, player, PlayerMessageKind.RoomFull, RoomTypeId.Treasury);
        return reachable ? null : Post(context, player, PlayerMessageKind.RoomUnreachable, RoomTypeId.Treasury);
    }

    /// <summary>Gold paid in at a tile that no Treasury or Heart could take (<c>0x004BA420</c>, after its
    /// spread over the stores): when the Treasury's availability is anything but 1, 41 with no Treasury
    /// and 42 with one. Data1 is Treasury.</summary>
    public static PlayerMessage? GoldLeftOver(SimulationContext context, PlayerId player)
    {
        WorldState world = context.World;
        if (world.Availability(player, AvailabilityKind.Room, (int)RoomTypeId.Treasury) == 1) return null;
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
        PlayerId player = creature.OwnerPlayerId;
        if (world.Availability(player, AvailabilityKind.Room, (int)RoomTypeId.Lair) != Available) return null;

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
        PlayerId player = creature.OwnerPlayerId;
        if (world.Availability(player, AvailabilityKind.Room, (int)RoomTypeId.Hatchery) != Available) return null;

        var hatcheries = RoomIndex.Build(world, context.Database)
            .Where(r => EatingRules.IsHatcheryOf(r, player.Value) && r.TileIndices.Count > 0).ToList();
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

    /// <summary>The rescue selector's verdict on a room type (<c>0x0046D810</c>, cached on the worker's
    /// state at <c>+0x109</c> for the Lair and <c>+0x113</c> for the Prison).</summary>
    public enum RescueRoom : byte
    {
        /// <summary>No room and availability other than 3: no message, no task.</summary>
        None = 0,
        /// <summary>No room, availability 3: 41.</summary>
        NoRoom = 1,
        /// <summary>A room with a place, none the <c>0x80</c> route reaches: 43.</summary>
        Unreachable = 2,
        /// <summary>No room with a place: 42.</summary>
        Full = 3,
        /// <summary>A reachable room with a place: the carry.</summary>
        Usable = 4,
    }

    /// <summary>The rescue selector's room test, for <see cref="RoomTypeId.Lair"/> or
    /// <see cref="RoomTypeId.Prison"/>: the keeper's count of the type (<c>0x004C5FD0</c>), then a room
    /// with a place for one (<c>0x004C6510</c>; a Prison's used <c>+0x6B</c> + 1 within <c>+0x4B</c>),
    /// then such a room whose position (<c>+0x2B</c>, <see cref="Position"/>) the worker's <c>0x80</c>
    /// route reaches (<c>0x004C6610</c>, which also needs a Prison's <c>Activation</c>). Which room is
    /// nearest does not matter here: any reachable one gives result 4.</summary>
    public static RescueRoom RescueResult(
        SimulationContext context, CreatureState worker, CreatureDef definition, RoomTypeId type)
    {
        WorldState world = context.World;
        PlayerId player = worker.OwnerPlayerId;
        var rooms = RoomIndex.Build(world, context.Database)
            .Where(r => r.OwnerPlayerId == player && r.Definition.TypeId == type).ToList();
        if (rooms.Count == 0)
            return world.Availability(player, AvailabilityKind.Room, (int)type) == Available ? RescueRoom.NoRoom : RescueRoom.None;

        var placed = rooms.Where(r => type == RoomTypeId.Prison
            ? PrisonRules.PrisonerCount(world, r) + 1 <= PrisonRules.Capacity(r)
            : LairRules.HasFreeSlot(world, context.Database, r)).ToList();
        if (placed.Count == 0) return RescueRoom.Full;

        var scratch = new List<Steering.Fixed16Vector>();
        foreach (RoomInstance room in placed)
        {
            if (type == RoomTypeId.Prison && !PrisonRules.IsActivated(world, room)) continue;
            if (ChickenRules.Reachable(context, worker, definition, Position(world, room), scratch))
                return RescueRoom.Usable;
        }

        return RescueRoom.Unreachable;
    }

    /// <summary><c>Player_FindNearestRoomWithSpace</c> (<c>0x004C6610</c>) for a Lair, as the casualty
    /// chooser <c>0x0046E610</c> asks it for a body with no bed: of the worker's keeper's Lairs with a
    /// free bed, the one whose position (<see cref="Position"/>) is strictly nearest the body by
    /// squared distance and that the worker reaches with option <c>0x80</c>, or null.</summary>
    internal static RoomInstance? NearestLairWithSpace(
        SimulationContext context, CreatureState worker, CreatureDef definition, CreatureState body)
    {
        WorldState world = context.World;
        RoomInstance? best = null;
        long bestDistance = long.MaxValue;
        var scratch = new List<Steering.Fixed16Vector>();
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (room.OwnerPlayerId != worker.OwnerPlayerId || !LairRules.IsLairRoom(context.Database, room)) continue;
            if (!LairRules.HasFreeSlot(world, context.Database, room)) continue;
            TilePos at = Position(world, room);
            long dx = (long)CreatureState.CentreOf(at.X) - body.PosX, dy = (long)CreatureState.CentreOf(at.Y) - body.PosY;
            long distance = ((dx * dx) >> 12) + ((dy * dy) >> 12);
            if (distance >= bestDistance || !ChickenRules.Reachable(context, worker, definition, at, scratch)) continue;
            (best, bestDistance) = (room, distance);
        }

        return best;
    }

    /// <summary>Room <c>+0x2B</c> as <c>Room_UpdateCentrePos</c> (<c>0x004E4590</c>) writes it: the
    /// centroid's tile, or a tile spiralled to from it, that <c>Room_IsFloorSlot</c> (<c>0x00454110</c>)
    /// rejects, so a rim tile for a room with an interior. The spiral's order is not read; ours takes
    /// the nearest such tile, ties to the lower index (STRONGLY INFERRED as the same tile).</summary>
    public static TilePos Position(WorldState world, RoomInstance room)
    {
        int w = world.Width;
        long sumX = 0, sumY = 0;
        foreach (int index in room.TileIndices) { sumX += index % w; sumY += index / w; }
        int cx = (int)(sumX / room.TileIndices.Count), cy = (int)(sumY / room.TileIndices.Count);
        HashSet<int> slots = AutoFurnitureRules.FloorSlotSet(world, room);
        int best = room.TileIndices[0];
        long bestDistance = long.MaxValue;
        foreach (int index in room.TileIndices)
        {
            if (slots.Contains(index)) continue;
            long dx = index % w - cx, dy = index / w - cy;
            long d = (dx * dx) + (dy * dy);
            if (d < bestDistance) { bestDistance = d; best = index; }
        }

        return new TilePos(best % w, best / w);
    }

    /// <summary>The selector's message for a body it meets (<c>0x0046D810</c>): 41, 43 or 42 for results
    /// 1, 2 and 3, Data1 the room type, Data2 0, with no pending-build or four-second gate.</summary>
    public static PlayerMessage? ForRescue(SimulationContext context, PlayerId player, RescueRoom result, RoomTypeId type) =>
        result switch
        {
            RescueRoom.NoRoom => Post(context, player, PlayerMessageKind.RoomNeeded, type),
            RescueRoom.Unreachable => Post(context, player, PlayerMessageKind.RoomUnreachable, type),
            RescueRoom.Full => Post(context, player, PlayerMessageKind.RoomFull, type),
            _ => null,
        };

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
    /// <remarks>A Workshop posts 43/42/44 only while an order is queued (player <c>+0xA1C</c>); a
    /// Casino's search, while its keeper has a jackpot party (<c>+0xF4A</c>), meets only Casinos with
    /// a party on (<c>+0x41</c>); a Mistress's missing Torture Chamber is 99. The Library has its own
    /// finder (<see cref="FindLibrary"/>). 44 names the last flagged room in our room order, not DK2's
    /// room list.</remarks>
    public static PlayerMessage? NoJobRoomFor(
        SimulationContext context, CreatureState creature, CreatureDef definition, RoomTypeId wants)
    {
        if (wants == RoomTypeId.Library)
        {
            FindLibrary(context, creature, definition, out PlayerMessage? posted);
            return posted;
        }

        if (wants is not (RoomTypeId.TrainingRoom or RoomTypeId.Temple or RoomTypeId.TortureChamber
            or RoomTypeId.Casino or RoomTypeId.Workshop)) return null;
        WorldState world = context.World;
        PlayerId player = creature.OwnerPlayerId;
        if (world.Availability(player, AvailabilityKind.Room, (int)wants) != Available) return null;

        var rooms = RoomIndex.Build(world, context.Database)
            .Where(r => r.OwnerPlayerId == player && r.Definition.TypeId == wants && r.TileIndices.Count > 0).ToList();
        if (rooms.Count == 0)
            return Post(context, player, wants == RoomTypeId.TortureChamber && creature.CreatureTypeId == MistressCreatureId
                ? PlayerMessageKind.RoomWanted : PlayerMessageKind.RoomNeeded, wants);
        if (wants == RoomTypeId.Workshop && world.Player(player).ManufactureQueue.Count == 0) return null;
        bool party = wants == RoomTypeId.Casino && world.Player(player).CasinoParties != 0;

        bool full = false;
        RoomInstance? unequipped = null;
        long grace = SimulationTiming.TicksFromSeconds(FullRoomGraceSeconds);
        foreach (RoomInstance room in rooms)
        {
            if (party && !CasinoJackpotRules.IsPartyOn(world, room)) continue;
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

    /// <summary>The Library finder <c>0x004E2500</c>, which <c>Job_TryResearch</c> calls with a research
    /// target set. Each reachable Library (option <c>0x80</c>) first asks for a shelf
    /// (<see cref="ResearchTreeRules.FindShelf"/>, one draw), then for a place; one with both is a
    /// candidate, ranked by the squared distance from the creature to its first slab's centre
    /// (<c>0x0043C260</c>): the nearest whose holders it does not object to wins, else the nearest.
    /// A Library past <see cref="FullRoomGraceSeconds"/> without a shelf flags 45, without a place 42,
    /// either one 44 when its <c>+0xA3</c> does not hold. With no candidate, under the Lair's
    /// availability row: no Library posts 41; with no research target (<c>+0x213</c>) nothing more;
    /// then 43 for one with a shelf and a place that the option-2 route misses (another draw each),
    /// 45, 42 and 44, both of them naming the last flagged room.</summary>
    /// <remarks>Ours walks its rooms in index order, not DK2's newest-first list, so an equal distance
    /// can pick a different room; the draws are one per reachable Library either way.</remarks>
    public static RoomInstance? FindLibrary(
        SimulationContext context, CreatureState creature, CreatureDef definition, out PlayerMessage? posted)
    {
        posted = null;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        PlayerId player = creature.OwnerPlayerId;
        var libraries = RoomIndex.Build(world, database)
            .Where(r => r.OwnerPlayerId == player && r.Definition.TypeId == RoomTypeId.Library && r.TileIndices.Count > 0).ToList();
        CreatureFlags against = definition.Flags.HasFlag(CreatureFlags.IsGood) ? CreatureFlags.IsEvil : CreatureFlags.IsGood;

        bool storage = false, full = false, unequipped = false;
        RoomInstance? last = null, nearest = null, friendly = null;
        long best = long.MaxValue, bestFriendly = long.MaxValue;
        long grace = SimulationTiming.TicksFromSeconds(FullRoomGraceSeconds);
        foreach (RoomInstance room in libraries)
        {
            if (!CanReachRoom(context, creature, definition, room, MoveOptions.Loose)) continue;
            bool shelf = ResearchTreeRules.FindShelf(context, room) >= 0;
            if (shelf && RoomCapacityRules.HasRoom(world, database, room, creature.Id))
            {
                int first = SlabChain.FirstSlab(world, room);
                long dx = (long)CreatureState.CentreOf(first % world.Width) - creature.PosX;
                long dy = (long)CreatureState.CentreOf(first / world.Width) - creature.PosY;
                long distance = ((dx * dx) >> 12) + ((dy * dy) >> 12);
                if (distance < best) (nearest, best) = (room, distance);
                if (distance < bestFriendly && !HoldsOneItObjectsTo(world, database, room, against))
                    (friendly, bestFriendly) = (room, distance);
                continue;
            }

            if (world.CurrentTick.Value - SlabChain.SizeChangedTick(world, room) <= grace) continue;
            if (!IsEquipped(world, room)) { unequipped = true; last = room; }
            else if (!shelf) { storage = true; last = room; }
            else full = true;
        }

        if ((friendly ?? nearest) is { } found) return found;
        posted = LibraryMessage(context, creature, definition, libraries, storage, full, unequipped, last);
        return null;
    }

    /// <summary><c>0x004E2500</c>'s holder test, <c>Creature_ObjectsToAlignment</c> over the room's
    /// occupants: one of the other alignment holds a place in it.</summary>
    private static bool HoldsOneItObjectsTo(WorldState world, GameDatabase database, RoomInstance room, CreatureFlags against)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.HeldPlace.In(room) && database.CreatureById(c.CreatureTypeId).Flags.HasFlag(against)) return true;
        return false;
    }

    private static PlayerMessage? LibraryMessage(SimulationContext context, CreatureState creature, CreatureDef definition,
        List<RoomInstance> libraries, bool storage, bool full, bool unequipped, RoomInstance? last)
    {
        WorldState world = context.World;
        PlayerId player = creature.OwnerPlayerId;
        if (world.Availability(player, AvailabilityKind.Room, (int)RoomTypeId.Lair) != Available) return null;
        if (libraries.Count == 0) return Post(context, player, PlayerMessageKind.RoomNeeded, RoomTypeId.Library);
        // The finder's own +0x213 test; Job_TryResearch, like JobRules.Refused, has already asked it.
        if (world.Player(player).ResearchTarget == 0) return null;

        foreach (RoomInstance room in libraries)
            if (ResearchTreeRules.FindShelf(context, room) >= 0
                && RoomCapacityRules.HasRoom(world, context.Database, room, creature.Id)
                && !CanReachRoom(context, creature, definition, room, IgnoreDoors))
                return Post(context, player, PlayerMessageKind.RoomUnreachable, RoomTypeId.Library);
        if (storage) return Post(context, player, PlayerMessageKind.RoomStorageFull, RoomTypeId.Library, SlabChain.FirstSlab(world, last!));
        if (full) return Post(context, player, PlayerMessageKind.RoomFull, RoomTypeId.Library);
        return unequipped ? Post(context, player, PlayerMessageKind.RoomUnequipped, RoomTypeId.Library, SlabChain.FirstSlab(world, last!)) : null;
    }

    /// <summary>The spectator finder (<c>0x004D8510</c>, job 26) found no Combat Pit: only pits with a
    /// fight on (<see cref="CombatPitSpectatorRules.FightOn"/>, which stamps each) count, so it never
    /// posts 41. A reachable one without a place past <see cref="FullRoomGraceSeconds"/> flags 42, or
    /// 44 without an interior; then at availability 3, 43 for one with a fight and a place the
    /// option-2 route misses, 42, 44.</summary>
    public static PlayerMessage? NoSpectatorPitFor(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        PlayerId player = creature.OwnerPlayerId;
        var pits = RoomIndex.Build(world, context.Database)
            .Where(r => r.OwnerPlayerId == player && r.Definition.TypeId == RoomTypeId.CombatPit && r.TileIndices.Count > 0).ToList();

        bool candidate = false, full = false;
        RoomInstance? unequipped = null;
        long grace = SimulationTiming.TicksFromSeconds(FullRoomGraceSeconds);
        foreach (RoomInstance pit in pits)
        {
            if (!CombatPitSpectatorRules.FightOn(context, pit) || !CanReachRoom(context, creature, definition, pit, MoveOptions.Loose)) continue;
            if (RoomCapacityRules.HasRoom(world, context.Database, pit, creature.Id)) { candidate = true; continue; }
            if (world.CurrentTick.Value - SlabChain.SizeChangedTick(world, pit) <= grace) continue;
            if (IsEquipped(world, pit)) full = true;
            else unequipped = pit;
        }

        if (candidate || pits.Count == 0) return null;
        if (world.Availability(player, AvailabilityKind.Room, (int)RoomTypeId.CombatPit) != Available) return null;
        foreach (RoomInstance pit in pits)
            if (CombatPitSpectatorRules.FightOn(context, pit) && !CanReachRoom(context, creature, definition, pit, IgnoreDoors)
                && RoomCapacityRules.HasRoom(world, context.Database, pit, creature.Id))
                return Post(context, player, PlayerMessageKind.RoomUnreachable, RoomTypeId.CombatPit);
        if (full) return Post(context, player, PlayerMessageKind.RoomFull, RoomTypeId.CombatPit);
        return unequipped is null ? null
            : Post(context, player, PlayerMessageKind.RoomUnequipped, RoomTypeId.CombatPit, SlabChain.FirstSlab(world, unequipped));
    }

    /// <summary>The fighter's pit finder (<c>CombatPit_FindForCreature</c>, <c>0x004D8870</c>), which
    /// the Training handler (<c>0x00485240</c>) calls for a creature at or above
    /// <c>TrainingRoomMaxExperienceLevel</c> and below <c>CombatPitMaxExperienceLevel</c>, only for its
    /// messages. A reachable pit draws a floor slot (<c>0x004E8210</c>, one draw when it has any) and
    /// asks the drop test (<see cref="DropTest"/>): no floor slot past
    /// <see cref="FullRoomGraceSeconds"/> flags 42; a failed drop test past it flags 44 when
    /// <c>+0xA3</c> is clear, else 42. Then no pit 41; 43 for a pit the option-2 route misses whose
    /// slot draw and drop test pass; 42; 44 naming the room.</summary>
    public static PlayerMessage? NoFightingPitFor(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        PlayerId player = creature.OwnerPlayerId;
        var pits = RoomIndex.Build(world, context.Database)
            .Where(r => r.OwnerPlayerId == player && r.Definition.TypeId == RoomTypeId.CombatPit && r.TileIndices.Count > 0).ToList();

        bool candidate = false, full = false;
        RoomInstance? unequipped = null;
        long grace = SimulationTiming.TicksFromSeconds(FullRoomGraceSeconds);
        foreach (RoomInstance pit in pits)
        {
            if (!CanReachRoom(context, creature, definition, pit, MoveOptions.Loose)) continue;
            bool slot = RandomFloorSlot(context, pit);
            if (slot && DropTest(world, pit)) { candidate = true; continue; }
            if (world.CurrentTick.Value - SlabChain.SizeChangedTick(world, pit) <= grace) continue;
            if (slot && !IsEquipped(world, pit)) unequipped = pit;
            else full = true;
        }

        if (candidate || world.Availability(player, AvailabilityKind.Room, (int)RoomTypeId.CombatPit) != Available) return null;
        if (pits.Count == 0) return Post(context, player, PlayerMessageKind.RoomNeeded, RoomTypeId.CombatPit);
        foreach (RoomInstance pit in pits)
            if (!CanReachRoom(context, creature, definition, pit, IgnoreDoors) && RandomFloorSlot(context, pit) && DropTest(world, pit))
                return Post(context, player, PlayerMessageKind.RoomUnreachable, RoomTypeId.CombatPit);
        if (full) return Post(context, player, PlayerMessageKind.RoomFull, RoomTypeId.CombatPit);
        return unequipped is null ? null
            : Post(context, player, PlayerMessageKind.RoomUnequipped, RoomTypeId.CombatPit, SlabChain.FirstSlab(world, unequipped));
    }

    /// <summary><c>0x004E8210(1, 0)</c>'s answer: whether the room has a floor slot, drawing one of them
    /// at random (the draw's tile is not used here).</summary>
    private static bool RandomFloorSlot(SimulationContext context, RoomInstance room)
    {
        int count = AutoFurnitureRules.FloorSlotSet(context.World, room).Count;
        if (count == 0) return false;
        context.Rng.NextInt(count);
        return true;
    }

    /// <summary><c>0x004D8430</c> at a floor slot: true while the creatures standing on the pit's floor
    /// slots number fewer than its floor slots (<c>0x004D84FB</c> <c>CMP EBP,EDI</c>). Creatures in the
    /// hand are off the map and do not count.</summary>
    internal static bool DropTest(WorldState world, RoomInstance pit)
    {
        HashSet<int> slots = AutoFurnitureRules.FloorSlotSet(world, pit);
        int standing = 0;
        foreach (CreatureState c in world.Creatures)
            if (!HandRules.IsHeld(c) && world.Contains(c.Tile) && slots.Contains(world.TileIndex(c.Tile))) standing++;
        return standing < slots.Count;
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
    /// <remarks>DK2 stores the flag when the pass runs, at a rebuild; ours reads the furniture now.
    /// The same in practice: the player cannot place this furniture, which follows the room's size
    /// (owner, 2026-10-05), and <see cref="Systems.AutoFurnitureSystem"/> changes it only with the shape.</remarks>
    public static bool IsEquipped(WorldState world, RoomInstance room) => room.Definition.TypeId switch
    {
        RoomTypeId.TrainingRoom => HasAttached(world, room, 14, 29),
        RoomTypeId.TortureChamber => HasAttached(world, room, TortureRules.WheelObjectId, TortureRules.ElectricChairObjectId),
        RoomTypeId.Casino or RoomTypeId.Library or RoomTypeId.CombatPit => room.InteriorTileCount > 0,
        RoomTypeId.Workshop => HasAttached(world, room, AnvilObjectId, SteelBucketObjectId)
            || HasAttached(world, room, WorkBenchObjectId, WorkBenchObjectId),
        _ => false,
    };

    private const byte AnvilObjectId = 15, SteelBucketObjectId = 16, WorkBenchObjectId = 46;

    private static bool HasAttached(WorldState world, RoomInstance room, byte first, byte second)
    {
        foreach (ObjectState thing in world.Objects)
            if (thing.AutoPlaced && (thing.ObjectTypeId == first || thing.ObjectTypeId == second)
                && EatingRules.Contains(room, thing.TileIndex))
                return true;
        return false;
    }

    /// <summary>The unreachable test's option word, 2: own locked doors passed, no door rule.</summary>
    private const Navigation.MoveOptions IgnoreDoors = (Navigation.MoveOptions)0x02;

    /// <summary><c>0x00471740</c>: a point in the room (<c>0x004E5000</c>, the first standable tile along
    /// its chain) and the creature's route test to it (<c>0x004D0E60</c>) with the given options.</summary>
    private static bool CanReachRoom(SimulationContext context, CreatureState creature, CreatureDef definition,
        RoomInstance room, Navigation.MoveOptions options)
    {
        int? point = StealGoldRules.FirstStandable(context.World, context.Database, definition, room);
        if (point is not int tile) return false;
        int w = context.World.Width;
        return CanRoute(context, creature, definition, new TilePos(tile % w, tile / w), options);
    }

    /// <summary><c>0x004D0E60</c> without loading a route: the creature's position to the tile's centre.</summary>
    private static bool CanRoute(SimulationContext context, CreatureState creature, CreatureDef definition, TilePos tile, Navigation.MoveOptions options)
    {
        if (creature.Tile == tile) return true;
        var from = new Steering.Fixed16Vector(creature.PosX << 4, creature.PosY << 4);
        var to = new Steering.Fixed16Vector(CreatureState.CentreOf(tile.X) << 4, CreatureState.CentreOf(tile.Y) << 4);
        return context.Navigation.Route(from, to, WorldSteering.ProfileOf(definition, creature.OwnerPlayerId, options), []);
    }

    /// <summary>Posts, and reports a new record; a retained duplicate comes back unchanged and unreported.</summary>
    internal static PlayerMessage? Post(SimulationContext context, PlayerId player, PlayerMessageKind kind, RoomTypeId room, int roomTag = 0)
    {
        IReadOnlyList<PlayerMessage> entries = context.World.Messages.Entries;
        uint lastBefore = entries.Count == 0 ? 0 : entries[^1].Id;
        PlayerMessage? posted = context.World.Messages.PostRoom(context.World, player.Value, kind, room, roomTag);
        if (posted is PlayerMessage message && message.Id != lastBefore && entries[^1].Id == message.Id)
            context.Emit(nameof(RoomNeedRules), "room_message",
                $"player={player} kind={(int)kind} room={room} id={message.Id}");
        return posted;
    }
}
