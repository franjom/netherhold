// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The research tree: what the points <see cref="ResearchRules"/> banks buy
/// (docs/research/research-tree.md).
/// <para>The list is the level's Variables id-17 records, in file order (<see
/// cref="GlobalVariables.Research"/>). The keeper researches the first item still
/// RESEARCHABLE; once nothing is, the first spell that is AVAILABLE, not yet upgraded and
/// has a bonus shot. A finished item becomes a book on a Library shelf, and <b>the book is
/// the availability</b>. A finished upgrade makes no book and sets the spell's
/// <see cref="AvailabilityState.Upgraded"/>, which switches its casts to the bonus shot.</para>
/// <para>Deviations: the Library's two accept tests (<c>0x004E5480</c> and the lock at
/// <c>room+0x0A</c>) are not modelled, so every owned Library is a candidate; the
/// "no shelf" message 0x19 and its timer are not modelled; the book's room tag is stood in for
/// by its owner.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/research-tree.md")]
public static class ResearchTreeRules
{
    /// <summary>Research kinds, as the list uses them. Not availability kinds: see
    /// <see cref="AvailabilityKindOf"/>.</summary>
    public const int KindRoom = 1, KindDoor = 2, KindTrap = 3, KindSpell = 4;

    /// <summary>The Spell Book object, DK2's literal 4 in the grant functions and the shelf
    /// search (objects.md).</summary>
    public const byte BookObjectTypeId = 4;

    /// <summary>A shelf holds fewer than this many books and Specials (<c>0x004E1AC0</c>).</summary>
    public const int ShelfCapacity = 2;

    private const int Researchable = 2;

    /// <summary>The availability value a cast needs: AVAILABLE (3) in the keeper's table.</summary>
    public const int Available = 3;
    private const string SystemName = "Research";

    /// <summary>The availability kind that holds a research kind's items: room 1, door 4,
    /// trap 3, spell 5 — or 0 for a kind the original's switches have no arm for.</summary>
    public static ushort AvailabilityKindOf(int researchKind) => researchKind switch
    {
        KindRoom => 1,
        KindDoor => 4,
        KindTrap => 3,
        KindSpell => 5,
        _ => 0,
    };

    /// <summary><c>0x004C30F0</c>: whether the entry's item is still RESEARCHABLE. A kind with no
    /// arm answers yes, as the original's switch falls through to <c>return 1</c>.</summary>
    public static bool IsResearchable(WorldState world, byte playerId, ResearchEntry entry)
    {
        ushort kind = AvailabilityKindOf(entry.Kind);
        return kind == 0 || world.Availability(playerId, kind, entry.TypeId) == Researchable;
    }

    /// <summary>The upgrade phase's test (<c>0x004C3000</c>, second pass): a spell that is
    /// AVAILABLE, not yet upgraded, and whose record has a bonus shot.</summary>
    public static bool IsUpgradeTarget(
        WorldState world, GameDatabase database, byte playerId, ResearchEntry entry)
    {
        if (entry.Kind != KindSpell) return false;
        ushort kind = AvailabilityKindOf(KindSpell);
        return world.Availability(playerId, kind, entry.TypeId) == Available
            && !world.IsUpgraded(playerId, kind, entry.TypeId)
            && database.KeeperSpellById(entry.TypeId)?.CanBeUpgraded == true;
    }

    /// <summary><c>0x004C3000</c>: the first entry still RESEARCHABLE, else the first upgrade
    /// target, else none. Clearing a target that was set sends message 0x18.</summary>
    public static void ChooseTarget(SimulationContext context, byte playerId)
    {
        WorldState world = context.World;
        List<ResearchEntry> list = context.Database.Variables.Research;
        ref PlayerState player = ref world.Player(playerId);

        for (int i = 0; i < list.Count; i++)
        {
            if (!IsResearchable(world, playerId, list[i])) continue;
            player.ResearchTarget = i + 1;
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            ResearchEntry entry = list[i];
            if (entry.Kind is >= KindRoom and < KindSpell) continue;
            if (entry.Kind == KindSpell && !IsUpgradeTarget(world, context.Database, playerId, entry)) continue;
            player.ResearchTarget = i + 1;
            return;
        }

        if (player.ResearchTarget != 0)
            context.Emit(SystemName, "research_finished", $"player={playerId}");
        player.ResearchTarget = 0;
    }

    /// <summary>The adder's second half (<c>0x004C2DC0</c> after the credit): re-choose when the
    /// target has stopped qualifying, and when the points reach its cost, deliver it and start
    /// the next. Nothing is spent when no Library can take it; the next credit tries again.</summary>
    public static void Advance(SimulationContext context, byte playerId)
    {
        WorldState world = context.World;
        List<ResearchEntry> list = context.Database.Variables.Research;
        ref PlayerState player = ref world.Player(playerId);
        if (player.ResearchTarget == 0) return;

        ResearchEntry entry = list[player.ResearchTarget - 1];
        if (!IsResearchable(world, playerId, entry) && !StillWanted(context, playerId, entry))
            ChooseTarget(context, playerId);

        if (player.ResearchTarget == 0) return;
        entry = list[player.ResearchTarget - 1];
        if (player.ResearchPoints < entry.Cost) return;
        if (!Deliver(context, playerId, entry)) return;

        player.ResearchPoints = 0;
        ChooseTarget(context, playerId);
    }

    /// <summary>Why a target that is no longer RESEARCHABLE is kept: it is a live upgrade
    /// target, or its kind is one the original's check does not branch on.</summary>
    private static bool StillWanted(SimulationContext context, byte playerId, ResearchEntry entry) =>
        entry.Kind is < KindRoom or > KindSpell
        || (entry.Kind == KindSpell && IsUpgradeTarget(context.World, context.Database, playerId, entry));

    /// <summary><c>0x004C6CF0</c> then <c>0x004E0870</c>: a random one of the keeper's Libraries
    /// takes the result if it has a shelf. False when there is no Library or no shelf.</summary>
    private static bool Deliver(SimulationContext context, byte playerId, ResearchEntry entry)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var libraries = new List<RoomInstance>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
            if (room.OwnerPlayerId == playerId && room.Definition.TypeId == RoomTypeId.Library)
                libraries.Add(room);
        if (libraries.Count == 0) return false;

        RoomInstance library = libraries[context.Rng.NextInt(libraries.Count)];
        if (FindShelf(context, library) < 0)
        {
            context.Emit(SystemName, "research_no_shelf",
                $"player={playerId} kind={entry.Kind} type={entry.TypeId}");
            return false;
        }

        if (AvailabilityKindOf(entry.Kind) == 0) return false;
        Grant(context, library, entry);
        return true;
    }

    /// <summary>The grant functions (<c>0x004E0F00</c> for spells, <c>0x004E0BE0</c> doors,
    /// <c>0x004E0A50</c> traps, <c>0x004E0D70</c> rooms): an item already AVAILABLE and not
    /// upgraded is upgraded; anything else becomes a book on a shelf.</summary>
    private static void Grant(SimulationContext context, RoomInstance library, ResearchEntry entry)
    {
        WorldState world = context.World;
        byte owner = library.OwnerPlayerId;
        ushort kind = AvailabilityKindOf(entry.Kind);

        if (world.Availability(owner, kind, entry.TypeId) == Available
            && !world.IsUpgraded(owner, kind, entry.TypeId))
        {
            world.SetUpgraded(owner, kind, entry.TypeId, true);
            context.Emit(SystemName, "research_upgraded",
                $"player={owner} kind={entry.Kind} type={entry.TypeId}");
            return;
        }

        int shelf = FindShelf(context, library);
        if (shelf < 0) return;

        ObjectState book = world.SpawnObject(context.Database, BookObjectTypeId, PlayerIds.Neutral, shelf);
        book.ResearchKind = (byte)entry.Kind;
        book.ResearchTypeId = (byte)entry.TypeId;
        bool kept = Shelve(world, context.Database, owner, book);
        context.Emit(SystemName, "research_completed",
            $"player={owner} kind={entry.Kind} type={entry.TypeId} book={(kept ? book.Id : -1)}");
    }

    /// <summary><c>0x004E12A0</c>: a Library takes a book. It becomes the room owner's, and
    /// unless it is a Special its item becomes AVAILABLE. A spell book for a spell the keeper
    /// already has is destroyed instead. Returns whether the book was kept.</summary>
    public static bool Shelve(WorldState world, GameDatabase database, byte ownerPlayerId, ObjectState book)
    {
        book.OwnerPlayerId = ownerPlayerId;
        ushort kind = AvailabilityKindOf(book.ResearchKind);

        if (book.ObjectTypeId == BookObjectTypeId && book.ResearchKind == KindSpell
            && world.Availability(ownerPlayerId, kind, book.ResearchTypeId) == Available)
        {
            world.RemoveObject(book.Id);
            return false;
        }

        if (kind != 0 && database.ObjectById(book.ObjectTypeId)?.IsSpecial != true)
            world.SetAvailability(ownerPlayerId, kind, book.ResearchTypeId, Available);
        return true;
    }

    /// <summary><c>0x004E1590</c>: a book has left <paramref name="ownerPlayerId"/>'s
    /// possession. With no other book of the same kind and type, the item goes back to
    /// RESEARCHABLE. <see cref="AvailabilityState.Upgraded"/> is left alone.</summary>
    /// <remarks>Not yet called: nothing in this project takes a shelved book out of a Library
    /// (a sold or captured Library, a book picked up). The rule is here so the first such path
    /// has it to call.</remarks>
    public static void Unshelved(WorldState world, byte ownerPlayerId, ObjectState book)
    {
        ushort kind = AvailabilityKindOf(book.ResearchKind);
        if (kind == 0) return;

        foreach (ObjectState other in world.Objects)
        {
            if (other.Id == book.Id || other.OwnerPlayerId != ownerPlayerId) continue;
            if (other.ObjectTypeId == BookObjectTypeId && other.ResearchKind == book.ResearchKind
                && other.ResearchTypeId == book.ResearchTypeId) return;
        }

        world.SetAvailability(ownerPlayerId, kind, book.ResearchTypeId, Researchable);
    }

    /// <summary><c>0x004E1AC0</c>: a shelf in <paramref name="library"/> — a floor slot holding
    /// fewer than <see cref="ShelfCapacity"/> books and Specials — searched along the room's
    /// chain from a random slab and wrapping once, or -1. One draw per call.</summary>
    public static int FindShelf(SimulationContext context, RoomInstance library)
    {
        WorldState world = context.World;
        int[] chain = SlabChain.Order(world, library);
        int start = context.Rng.NextInt(library.TileCount);
        HashSet<int> slots = AutoFurnitureRules.FloorSlotSet(world, library);

        for (int i = start; i < chain.Length; i++)
            if (HasShelfSpace(world, context.Database, library, slots, chain[i])) return chain[i];
        for (int i = 0; i < start && i < chain.Length; i++)
            if (HasShelfSpace(world, context.Database, library, slots, chain[i])) return chain[i];
        return -1;
    }

    private static bool HasShelfSpace(
        WorldState world, GameDatabase database, RoomInstance library, HashSet<int> slots, int tile)
    {
        if (!slots.Contains(tile)) return false;

        int stored = 0;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.TileIndex != tile || thing.InHand || thing.CarriedByCreatureId >= 0) continue;
            if (thing.OwnerPlayerId != library.OwnerPlayerId) continue;
            if (thing.ObjectTypeId == BookObjectTypeId || database.ObjectById(thing.ObjectTypeId)?.IsSpecial == true)
                stored++;
        }

        return stored < ShelfCapacity;
    }
}
