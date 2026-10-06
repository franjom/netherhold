// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>One entry of a keeper's blueprint list (<c>player+0xA21</c>): a door or a trap, by its
/// <see cref="DoorState.Id"/> or <see cref="TrapState.Id"/>, as DK2's list holds thing ids.</summary>
public readonly record struct BlueprintRef(byte Kind, int Id)
{
    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(Kind);
        hash.Add(Id);
    }
}

/// <summary>Trap and door crates: the Workshop that holds them, the keeper's list of blueprints
/// waiting for one, and the build a crate is carried to (<c>workshop-crates.md</c>, ADR-0214).</summary>
/// <remarks>Kind 0 is a door and kind 1 a trap (<c>0x00506BA0</c>); a crate matches a blueprint
/// by kind and by its object being the blueprint's crate (§1). A listed crate is owned by its
/// keeper and lies three to a tile off the furniture slots; an unlisted one is loose (§3, §4).
/// Ours has no room list: a crate is listed while its keeper owns it and it lies on one of the
/// keeper's Workshop tiles, and the newest is the highest id.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/workshop-crates.md")]
public static class CrateRules
{
    /// <summary>The kind byte for a door.</summary>
    public const byte DoorKind = 0;

    /// <summary>The kind byte for a trap.</summary>
    public const byte TrapKind = 1;

    /// <summary>The blueprint list's size: 128 thing ids, refused past it (<c>0x00505680</c>).</summary>
    public const int BlueprintCapacity = 128;

    /// <summary>Crates to a tile in a Workshop (<c>0x004F5040</c>, <c>0x004F6140</c>).</summary>
    public const int CratesPerTile = 3;

    /// <summary>The door the task and states 36 and 37 are for (task kind 10).</summary>
    public const int FetchDoorCrateState = 36;

    /// <summary>The door's install state.</summary>
    public const int InstallDoorState = 37;

    /// <summary>The trap the task and states 34 and 35 are for (task kind 9).</summary>
    public const int FetchTrapCrateState = 34;

    /// <summary>The trap's install state.</summary>
    public const int InstallTrapState = 35;

    /// <summary>State 68, which the crate maker sends every worker attached to the Workshop to.</summary>
    public const int CrateMadeState = 68;

    /// <summary>Whether the object is a crate.</summary>
    public static bool IsCrate(GameDatabase database, ObjectState thing) =>
        database.ObjectById(thing.ObjectTypeId) is { } def && def.Flags.HasFlag(ObjectFlags.TypeCrate);

    /// <summary>A crate's kind: a door's when a door definition names its object, else a trap's
    /// when a trap definition does, else null.</summary>
    public static byte? KindOf(GameDatabase database, byte objectTypeId)
    {
        foreach (DoorDef door in database.Doors.Values)
            if (door.CrateObjectId == objectTypeId) return DoorKind;
        foreach (TrapDef trap in database.Traps.Values)
            if (trap.CrateObjectId == objectTypeId) return TrapKind;
        return null;
    }

    /// <summary>The crate object a blueprint needs, or 0, also when its thing is gone.</summary>
    public static byte CrateFor(GameDatabase database, WorldState world, BlueprintRef blueprint)
    {
        if (blueprint.Kind == DoorKind)
            return world.DoorById(blueprint.Id) is { } door && database.Doors.TryGetValue(door.DoorId, out DoorDef? def) ? def.CrateObjectId : (byte)0;
        return world.TrapById(blueprint.Id) is { } trap && database.Traps.TryGetValue(trap.TrapId, out TrapDef? tdef) ? tdef.CrateObjectId : (byte)0;
    }

    /// <summary>Whether a blueprint entry still names a thing under construction.</summary>
    public static bool IsBlueprint(WorldState world, BlueprintRef blueprint) =>
        blueprint.Kind == DoorKind
            ? world.DoorById(blueprint.Id) is { UnderConstruction: true }
            : world.TrapById(blueprint.Id) is { UnderConstruction: true };

    /// <summary>The blueprint's tile index, or -1 when its thing is gone.</summary>
    public static int TileOf(WorldState world, BlueprintRef blueprint)
    {
        if (blueprint.Kind == DoorKind)
            return world.DoorById(blueprint.Id) is { } door ? (door.Y * world.Width) + door.X : -1;
        return world.TrapById(blueprint.Id) is { } trap ? (trap.Y * world.Width) + trap.X : -1;
    }

    /// <summary>The blueprint's owner, or 0 when its thing is gone.</summary>
    public static byte OwnerOf(WorldState world, BlueprintRef blueprint) =>
        blueprint.Kind == DoorKind
            ? world.DoorById(blueprint.Id)?.PlayerId ?? 0
            : world.TrapById(blueprint.Id)?.PlayerId ?? 0;

    /// <summary><c>0x00505680</c>: appends a blueprint unless it is already there or the list is full.</summary>
    public static void Register(WorldState world, PlayerId playerId, BlueprintRef blueprint)
    {
        if (!PlayerIds.IsKeeper(playerId.Value) || !world.HasPlayer(playerId)) return;
        List<BlueprintRef> list = world.Player(playerId).Blueprints;
        if (list.Contains(blueprint) || list.Count >= BlueprintCapacity) return;
        list.Add(blueprint);
    }

    /// <summary><c>0x005056E0</c>: takes a blueprint out of its keeper's list, closing the gap.</summary>
    public static void Unregister(WorldState world, PlayerId playerId, BlueprintRef blueprint)
    {
        if (world.HasPlayer(playerId)) world.Player(playerId).Blueprints.Remove(blueprint);
    }

    /// <summary>The blueprint of <paramref name="kind"/> on <paramref name="tileIndex"/> owned by
    /// <paramref name="playerId"/> and needing <paramref name="crateObjectId"/>, or null: states
    /// 35 and 37 walk the keeper's own traps or doors for the one on their tile.</summary>
    public static BlueprintRef? BlueprintAt(
        WorldState world, GameDatabase database, PlayerId playerId, byte kind, int tileIndex, byte crateObjectId)
    {
        int count = kind == DoorKind ? world.Doors.Count : world.Traps.Count;
        for (int i = 0; i < count; i++)
        {
            var candidate = new BlueprintRef(kind, kind == DoorKind ? world.Doors[i].Id : world.Traps[i].Id);
            if (OwnerOf(world, candidate) != playerId || !IsBlueprint(world, candidate)) continue;
            if (TileOf(world, candidate) != tileIndex || CrateFor(database, world, candidate) != crateObjectId) continue;
            return candidate;
        }

        return null;
    }

    /// <summary>States 35 and 37: the crate is used up and the thing is built. A door's navigation
    /// byte is the steering's, which reads the construction flag on its next refresh; a trap's
    /// completion (<c>0x004A96D0</c>) counts its Triggers (<see cref="TrapRules.Complete"/>). The
    /// Secret Door's terrain update (<c>0x00450FF0</c>) is not built.</summary>
    public static void Build(WorldState world, GameDatabase database, BlueprintRef blueprint, ObjectState crate)
    {
        world.RemoveObject(crate.Id);
        PlayerId owner = OwnerOf(world, blueprint);
        if (blueprint.Kind == DoorKind) { if (world.DoorById(blueprint.Id) is { } door) door.UnderConstruction = false; }
        else if (world.TrapById(blueprint.Id) is { } trap) TrapRules.Complete(world, database, trap);
        Unregister(world, owner, blueprint);
    }

    // ------------------------------------------------------------------ the Workshop

    /// <summary>Whether <paramref name="thing"/> is listed in <paramref name="workshop"/>.</summary>
    public static bool IsListedIn(WorldState world, GameDatabase database, RoomInstance workshop, ObjectState thing) =>
        thing.OwnerPlayerId == workshop.OwnerPlayerId && thing.CarriedByCreatureId < 0 && !thing.InHand
        && workshop.Contains(thing.TileIndex) && IsCrate(database, thing);

    /// <summary>The crates listed in <paramref name="workshop"/>, newest first.</summary>
    public static void CratesIn(WorldState world, GameDatabase database, RoomInstance workshop, List<ObjectState> into)
    {
        into.Clear();
        foreach (ObjectState thing in world.Objects)
            if (IsListedIn(world, database, workshop, thing)) into.Add(thing);
        into.Sort((a, b) => b.Id.CompareTo(a.Id));
    }

    /// <summary><c>0x004F5B00</c> and <c>0x004F5B70</c>: the first crate in the room's list of
    /// the kind with this object, or null.</summary>
    public static ObjectState? CrateIn(
        WorldState world, GameDatabase database, RoomInstance workshop, byte kind, byte crateObjectId)
    {
        ObjectState? newest = null;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.ObjectTypeId != crateObjectId || !IsListedIn(world, database, workshop, thing)) continue;
            if (KindOf(database, thing.ObjectTypeId) != kind) continue;
            if (newest is null || thing.Id > newest.Id) newest = thing;
        }

        return newest;
    }

    /// <summary>The Workshop's part of the capacity test, <c>0x004F6140</c>: its crates, three to a
    /// tile, must stay fewer than its tiles less its furniture.</summary>
    /// <remarks>DK2 counts the room's tiles (<c>room+0xA7</c>) and the furniture on its furniture
    /// list whose object is on the definition's object list. Ours counts the placed furniture of
    /// those objects on the room's tiles.</remarks>
    public static bool WorkshopHasRoom(WorldState world, GameDatabase database, RoomInstance workshop)
    {
        int furniture = 0, crates = 0;
        foreach (ObjectState thing in world.Objects)
        {
            if (!workshop.Contains(thing.TileIndex)) continue;
            if (thing.AutoPlaced && !thing.InHand && thing.OwnerPlayerId == workshop.OwnerPlayerId
                && IsFurnitureObject(workshop.Definition, thing.ObjectTypeId)) furniture++;
            else if (IsListedIn(world, database, workshop, thing)) crates++;
        }

        return crates / CratesPerTile < workshop.TileCount - furniture;
    }

    private static bool IsFurnitureObject(RoomDef definition, byte objectTypeId)
    {
        foreach (byte id in definition.ObjectIds)
        {
            if (id == 0) return false;
            if (id == objectTypeId) return true;
        }

        return false;
    }

    /// <summary><c>0x004F57F0</c>: the crate joins the room when it has space, owned by the room's
    /// keeper, and the crates are set down again. A room without space first sends one of the
    /// creatures working in it to idle (<c>0x004E69B0</c>) and asks once more; a crate takes no
    /// place of its own, so that helps only a room that was already over its capacity.</summary>
    /// <remarks>Ours takes the working creature with the lowest id, where DK2 takes the head of the
    /// room's attached list, whose order is not read.</remarks>
    public static bool List(in StateHookContext hooks, RoomInstance workshop, ObjectState crate)
    {
        WorldState world = hooks.World;
        GameDatabase database = hooks.Database;
        int units = database.ObjectById(crate.ObjectTypeId)?.RoomCapacity ?? 0;
        if (!RoomCapacityRules.HasRoom(world, database, workshop, units: units))
        {
            CreatureState? worker = RoomCapacityRules.FirstOccupant(world, workshop);
            if (worker != null) CreatureStateMachine.TrySetState(hooks, worker, CreatureStateMachine.Idle);
            if (!RoomCapacityRules.HasRoom(world, database, workshop, units: units)) return false;
        }

        crate.OwnerPlayerId = workshop.OwnerPlayerId;
        Arrange(world, database, workshop);
        return true;
    }

    /// <summary><c>0x004F5860</c>: the crate leaves the room and belongs to nobody, and the rest are
    /// set down again.</summary>
    public static void Unlist(WorldState world, GameDatabase database, RoomInstance workshop, ObjectState crate)
    {
        crate.OwnerPlayerId = PlayerIds.Unowned;
        Arrange(world, database, workshop);
    }

    /// <summary><c>0x004F5040</c>: the room's crates, newest first, three to a tile along the
    /// room's tiles that are neither a floor-furniture slot (<c>0x00454110</c>) nor a
    /// wall-furniture slot (<c>0x00454230</c>). Crates past the last such tile stay where they
    /// are. Tiles are visited in the room's chain order.</summary>
    public static void Arrange(WorldState world, GameDatabase database, RoomInstance workshop)
    {
        var crates = new List<ObjectState>();
        CratesIn(world, database, workshop, crates);
        if (crates.Count == 0) return;

        var slots = new HashSet<int>();
        AutoFurnitureRules.FurnitureSlots(world, database, workshop, slots);
        int next = 0;
        foreach (int tile in SlabChain.Order(world, workshop))
        {
            if (slots.Contains(tile)) continue;
            for (int k = 0; k < CratesPerTile && next < crates.Count; k++) crates[next++].TileIndex = tile;
            if (next == crates.Count) return;
        }
    }

    /// <summary>The tiles a crate may be set down on in <paramref name="workshop"/>, in chain
    /// order: the tiles <see cref="Arrange"/> uses.</summary>
    public static void CrateTiles(WorldState world, GameDatabase database, RoomInstance workshop, List<int> into)
    {
        into.Clear();
        var slots = new HashSet<int>();
        AutoFurnitureRules.FurnitureSlots(world, database, workshop, slots);
        foreach (int tile in SlabChain.Order(world, workshop))
            if (!slots.Contains(tile)) into.Add(tile);
    }
}
