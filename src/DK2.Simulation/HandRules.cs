// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The Hand of Evil: picking creatures up, dropping them, slapping them. Read from the
/// original's own handlers, whose fields these reproduce by name; addresses and evidence in
/// <c>docs/research/hand-of-evil.md</c>, decisions in ADR-0062.</summary>
public static class HandRules
{
    /// <summary>How many things one keeper may hold: <c>MaxNumberOfThingsInHand</c>, shipped at
    /// <b>64</b>. From the table, not pinned: the original compares against a per-keeper field this
    /// variable seeds.</summary>
    public static int Capacity(GameDatabase database) =>
        database[VariableTypeId.MaxNumberOfThingsInHand];

    /// <summary>How long a creature is held before the hand starts to affect its mood: <b>20 game
    /// seconds</b>, hardcoded in the original as <c>PickupTick + 20 x</c> variable 19, so moving a
    /// creature somewhere costs nothing. Trap: the table's
    /// <c>TimeInHandBeforeCreaturesBecomeAngrySeconds</c> is 1000, not this; UNKNOWN what it is for (Q160).</summary>
    public const int AngerGraceSeconds = 20;

    /// <summary>The grace period in our ticks.</summary>
    public const int AngerGraceTicks = AngerGraceSeconds * SimulationTiming.TicksPerSecond;

    /// <summary><b>There is no slap cooldown.</b> The original's <c>now &lt; LastSlapTick +</c>
    /// variable 19 test guards only the knock-back impulse; anger, damage, raised Efficiency and
    /// the speed boost land however recent the last slap was (hand-of-evil.md §4). Not implemented: the knock-back, there being no physics, so nothing reads this.</summary>
    public const int KnockBackWindowSeconds = 1;

    /// <summary>State 75: what the pick-up preparation <c>0x004BFBD0</c> enters, through idle when
    /// the current category is outside its entry mask, before the hand handler <c>0x004BC500</c>
    /// tests capacity; both the human handler <c>0x004C0BE0</c> and the keeper's routines call it
    /// first. Its update returns 1, so a creature the hand then refuses stays there; what else
    /// leaves it is UNKNOWN (hand-of-evil.md §2).</summary>
    public const int PreparationState = 75;

    /// <summary>Whether this creature is in somebody's hand — DK2's <c>StateFlags1.InHand</c>.
    /// Belongs beside <see cref="CreatureDeathRules.IsDown"/> wherever a system makes a creature act: a held creature does not walk, work, sleep or look for a lair.</summary>
    public static bool IsHeld(CreatureState creature) => creature.InHand;

    /// <summary>DK2's <c>LastDroppedCounter</c> (<c>+0x46D</c>): a word held at zero while the
    /// creature is in the hand and counted up once a turn otherwise, from its construction or
    /// its last drop (<c>0x00489A4D</c>, <c>0x00489A5F</c>). Wraps as the word does. Read by the
    /// keeper's hand routines, which leave a creature alone for 300 turns after a drop.</summary>
    public static ushort LastDroppedCounter(CreatureState creature, long now) =>
        creature.InHand ? (ushort)0 : unchecked((ushort)(now - creature.DroppedTick));

    /// <summary>Creature permission plus hand capacity. Remaining binary gates needing
    /// missing lifecycle state are listed in hand-of-evil.md §3.</summary>
    public static bool CanPickUp(
        WorldState world, GameDatabase database, CreatureState creature, PlayerId playerId)
        => HasPickupPermission(world, database, creature, playerId)
            && world.Player(playerId).Hand.Count < Capacity(database);

    /// <summary>The represented creature-permission gates, independent of hand capacity.
    /// State exclusions inspect the current state, not a travel resume slot.</summary>
    public static bool HasPickupPermission(
        WorldState world, GameDatabase database, CreatureState creature, PlayerId playerId)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        // Permission also covers original states whose simulation activity is not implemented.
        if (CreatureDeathRules.HasCollapsed(creature) || creature.InHand
            || creature.Dk2StateId is CreatureDeathRules.FallingState
                or CreatureDeathRules.DyingState or CreatureStateMachine.NoState) return false;
        // A nonzero removal stamp refuses any creature, an imp or not (0x0048E6A0, ADR-0277).
        if (creature.ManaRemovalStamp != 0) return false;
        if (!definition.Flags.HasFlag(CreatureFlags.CanBePickedUp)) return false;
        if (creature.Dk2StateId is 76 or 193) return false;
        if (creature.OwnerPlayerId != playerId)
            return creature.IsTool || (creature.SneakingAs != 0 && creature.SneakingAs == playerId.Value)
                || creature.PrisonOwnerPlayerId == playerId.Value;
        if (creature.CombatPitFighter)
            return world.Contains(creature.Tile)
                && world.Tile(creature.Tile.X, creature.Tile.Y).OwnerPlayerId == playerId;
        if (creature.Dk2StateId is 51 or 52 || TurncoatRules.IsActive(creature)) return false;
        if (definition.Flags.HasFlag(CreatureFlags.IsWorker) && (creature.Flags & 9) == 0) return false;
        // The stored room must be nobody's or the owner's. A Prison writes that membership on
        // entry (0x0047B740 calls 0x004E5A70), so an own creature in another keeper's cells is
        // refused; other rooms' membership is not stored here (creature-pickup.md).
        return !PrisonRules.IsImprisoned(creature) || creature.PrisonOwnerPlayerId == playerId.Value;
    }

    /// <summary>Whether this object is in somebody's hand.</summary>
    public static bool IsHeld(ObjectState thing) => thing.InHand;

    /// <summary>Whether <paramref name="thing"/> may be lifted by <paramref name="playerId"/> now.
    /// CONFIRMED (owner, Q196): the eligible set is <see cref="ObjectDef.CanBePickedUp"/>, 26 of
    /// the 145 records; the cursor search that reads it also admits the Dungeon Heart by id, a
    /// targeting special case. Neither the object's owner nor its tile is tested: action 59's
    /// handler (<c>0x005135B0</c>, hand state 2 <c>0x004C0CB0</c>) lifts any live object, so
    /// the claimed-land rule is the cursor's (<see cref="MayReachObject"/>). An object in an
    /// imp's arms is refused, ours in form and DK2's in effect.</summary>
    public static bool CanPickUpObject(
        WorldState world, GameDatabase database, ObjectState thing, PlayerId playerId)
    {
        ObjectDef? definition = database.ObjectById(thing.ObjectTypeId);
        if (definition is null || !definition.CanBePickedUp) return false;
        if (thing.InHand) return false;
        if (thing.CarriedByCreatureId >= 0) return false;
        return world.Player(playerId).Hand.Count < Capacity(database);
    }

    /// <summary>Whether the keeper's cursor may offer <paramref name="thing"/> to the hand:
    /// <b>gold only from the keeper's own claimed tile</b> (owner, Q196), a rival's floor
    /// refusing too. An input rule: DK2's own replay of <c>l2live2</c> (seed 2, ticks 232 to
    /// 234) lifts two piles off unclaimed dirt, as the recorded action carries no test (ADR-0310).</summary>
    public static bool MayReachObject(WorldState world, GameDatabase database, ObjectState thing, PlayerId playerId) =>
        database.ObjectById(thing.ObjectTypeId)?.IsGold != true
        || world.TileAtIndex(thing.TileIndex).OwnerPlayerId == playerId;

    /// <summary>Whether <paramref name="playerId"/> may lift the store pile on <paramref name="tile"/>
    /// now: a Treasury or Heart tile holding gold (one Gold Pile 1, <c>0x004F3C60</c>), the pile's
    /// definition liftable, a free hand slot. Like <see cref="CanPickUpObject"/>, no owner test:
    /// that is the cursor's (<see cref="MayReachStoreGold"/>).</summary>
    public static bool CanPickUpStoreGold(WorldState world, GameDatabase database, TilePos tile, PlayerId playerId)
    {
        if (!world.Contains(tile) || world.Tile(tile).GoldHeld <= 0) return false;
        if (database.ObjectById(FloorGold.TreasuryPileObjectId) is not { CanBePickedUp: true }) return false;
        if (world.Player(playerId).Hand.Count >= Capacity(database)) return false;
        RoomInstance? room = RoomIndex.RoomAt(world, database, world.TileIndex(tile));
        return room?.Definition.TypeId is RoomTypeId.Treasury or RoomTypeId.DungeonHeart;
    }

    /// <summary>Whether the cursor offers a store pile to <paramref name="playerId"/>'s hand: the pile
    /// is the room owner's (<c>0x004F3C60</c> creates it with the room's <c>+0x1D</c>), and
    /// <c>0x0049CBB0</c> publishes an object only to its owner.</summary>
    public static bool MayReachStoreGold(WorldState world, TilePos tile, PlayerId playerId) =>
        world.Contains(tile) && world.Tile(tile).OwnerPlayerId == playerId;

    /// <summary>Whether a drop of <paramref name="item"/> on <paramref name="tile"/> lands: the one
    /// rule the drop cage's colour and <see cref="Systems.HandSystem"/> both ask. DK2's gate
    /// (<see cref="CanDrop"/>), then for a creature our landing guard (<see cref="CreatureLands"/>).</summary>
    public static bool DropLands(
        WorldState world, GameDatabase database, PlayerId playerId, HandItem item, TilePos tile)
    {
        if (!CanDrop(world, database, playerId, item, tile)) return false;
        if (item.IsObject) return true;
        CreatureState creature = world.CreatureById(item.Id)!;
        return CreatureLands(world, database, playerId, creature, tile, out _);
    }

    /// <summary>Past DK2's gate (ADR-0526): a creature must be able to stand on the tile
    /// (<see cref="WorldPathGrid.IsWalkable"/>) unless the tile disposes of it
    /// (<see cref="DisposalRules"/>; never a prisoner) or is one of a Heart's eight platform tiles
    /// around the centre, which only a creature the gate let onto the inner 3×3 reaches: an Imp
    /// on its own Heart lands there and walks off (owner, 2026-10-23). So this refuses only a
    /// Portal centre for a non-evil worker, and a prisoner on the Heart's centre.</summary>
    public static bool CreatureLands(
        WorldState world, GameDatabase database, PlayerId playerId, CreatureState creature,
        TilePos tile, out bool disposed)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        disposed = !PrisonRules.IsImprisoned(creature)
            && DisposalRules.Disposes(world, database, definition, tile, playerId);
        return disposed || IsHeartPlatformRing(world, database, tile)
            || new WorldPathGrid(database, world, definition).IsWalkable(tile.X, tile.Y);
    }

    /// <summary>A Heart's inner 3×3 (<see cref="IsHeartInner"/>) less its centre: the eight raised
    /// platform tiles <see cref="StructureBlocks"/> keeps out of the path grid.</summary>
    private static bool IsHeartPlatformRing(WorldState world, GameDatabase database, TilePos tile)
    {
        if (!world.Contains(tile)
            || database.RoomByTerrain(world.Tile(tile.X, tile.Y).TerrainId)?.TypeId != RoomTypeId.DungeonHeart
            || RoomIndex.RoomAt(world, database, world.TileIndex(tile)) is not { } heart) return false;
        int dx = tile.X - heart.MinX, dy = tile.Y - heart.MinY;
        return dx is >= 1 and <= 3 && dy is >= 1 and <= 3 && !(dx == 2 && dy == 2);
    }

    /// <summary>DK2's drop gate <c>0x0040D8F0</c>: whether <paramref name="playerId"/> may put
    /// <paramref name="item"/> down on <paramref name="tile"/>. In DK2 it runs in the input layer and
    /// alone decides whether a click becomes a drop; ours asks it when the drop executes
    /// (placement-cursor.md, "The drop cage's colour"; ADR-0526). A creature goes through the map
    /// test <c>0x00450910</c> and the creature-flag arms, an object through <c>0x00450BE0</c>. Our
    /// hand holds no dead body, so <c>0x00450D40</c> has no arm here. A missing thing is refused.</summary>
    public static bool CanDrop(
        WorldState world, GameDatabase database, PlayerId playerId, HandItem item, TilePos tile)
    {
        if (!world.Contains(tile)) return false;
        if (item.IsObject)
            return world.ObjectById(item.Id) is { } thing && MapAllowsObject(world, database, playerId, thing.ObjectTypeId, tile);
        return world.CreatureById(item.Id) is { } creature && CreatureMayDrop(world, database, playerId, creature, tile);
    }

    /// <summary>Terrain 35, Hero Lair, where <c>0x0040D8F0</c> lets an unbeaten tool down whatever the
    /// map test said.</summary>
    private const byte HeroLairTerrainId = 35;

    /// <summary><c>0x0040D8F0</c>'s creature arm: the map test, then the flags at <c>+0x3EA</c>.</summary>
    private static bool CreatureMayDrop(
        WorldState world, GameDatabase database, PlayerId playerId, CreatureState creature, TilePos tile)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        bool tool = creature.IsTool && !HasBeenKnockedOut(creature);
        if (!MapAllowsCreature(world, database, playerId, definition, tile))
            return tool && world.Tile(tile.X, tile.Y).TerrainId == HeroLairTerrainId;
        if (creature.OwnerPlayerId != playerId && IsPortalCentre(world, database, tile, heroPortalToo: false)) return false;
        if (!tool) return true;
        RoomTypeId? room = database.RoomByTerrain(world.Tile(tile.X, tile.Y).TerrainId)?.TypeId;
        if (room is RoomTypeId.Prison or RoomTypeId.Temple or RoomTypeId.CombatPit) return !IsFloorSlot(world, tile);
        return room != RoomTypeId.TortureChamber;
    }

    /// <summary>DK2's <c>HasBeenKnockedOut</c> (<c>+0x3EA</c> bit 10), set on entering the death fall,
    /// state 216 (<c>0x00459560</c> and <c>0x004596D0</c>), and cleared by nothing found. Ours keeps no
    /// such bit: a prisoner was knocked out to be taken, and a creature still on the floor is; one
    /// that got up again is missed.</summary>
    private static bool HasBeenKnockedOut(CreatureState creature) =>
        PrisonRules.IsImprisoned(creature) || CreatureDeathRules.HasCollapsed(creature);

    /// <summary><c>0x00450910</c> (CWorld slot <c>+0x230</c>): whether a creature of this type may be
    /// dropped here. A worker goes onto allied ground or neutral roomless dry floor, a non-worker
    /// onto allied ground only, each with its Heart, Portal, Prison and Combat Pit exceptions.</summary>
    private static bool MapAllowsCreature(
        WorldState world, GameDatabase database, PlayerId playerId, CreatureDef definition, TilePos tile)
    {
        if (IsSolidOrDoor(world, database, tile)) return false;
        ref TileState cell = ref world.Tile(tile.X, tile.Y);
        RoomTypeId? room = database.RoomByTerrain(cell.TerrainId)?.TypeId;
        bool own = cell.OwnerPlayerId == playerId;
        // The one neutral id [0x006F2548], player 2; a loaded map gives non-ownable ground to it.
        bool neutral = cell.OwnerPlayerId == PlayerIds.Neutral;
        bool allied = !neutral && world.AreAllied(playerId, cell.OwnerPlayerId);

        if (definition.Flags.HasFlag(CreatureFlags.IsWorker))
        {
            bool evil = definition.Flags.HasFlag(CreatureFlags.IsEvil);
            if (room == RoomTypeId.CombatPit && IsFloorSlot(world, tile)) return false;
            if (room == RoomTypeId.DungeonHeart && !own && IsHeartInner(world, database, tile)) return false;
            if (evil && room is (RoomTypeId.Portal or RoomTypeId.HeroPortal) && IsPortalCentre(world, database, tile, heroPortalToo: true)) return false;
            if (!evil && room == RoomTypeId.DungeonHeart && IsHeartInner(world, database, tile)) return false;
            if (evil && room == RoomTypeId.Prison && IsFloorSlot(world, tile)) return false;
            if (allied) return true;
            TerrainDef terrain = database.TerrainById(cell.TerrainId);
            return neutral && !terrain.IsWater && !terrain.IsLava && room is null;
        }

        if (!allied) return false;
        if (room == RoomTypeId.CombatPit && IsFloorSlot(world, tile))
            return own && CombatPitRules.PitAt(world, database, world.TileIndex(tile)) is { } pit && RoomNeedRules.DropTest(world, pit);
        if (room == RoomTypeId.DungeonHeart) return !IsHeartInner(world, database, tile);
        return !(room == RoomTypeId.Portal && !own && IsPortalCentre(world, database, tile, heroPortalToo: false));
    }

    /// <summary><c>0x00450BE0</c> (CWorld slot <c>+0x234</c>): an object goes onto allied ground, or
    /// anywhere open with <see cref="ObjectFlags.CanBeDroppedOnAnyLand"/> (only Call To Arms ships
    /// it); a Chicken or a Call To Arms never onto a Portal centre or a Heart's inner 3×3.</summary>
    private static bool MapAllowsObject(
        WorldState world, GameDatabase database, PlayerId playerId, byte objectTypeId, TilePos tile)
    {
        if (IsSolidOrDoor(world, database, tile)) return false;
        ref TileState cell = ref world.Tile(tile.X, tile.Y);
        bool neutral = cell.OwnerPlayerId == PlayerIds.Neutral;
        bool allowed = database.ObjectById(objectTypeId)?.Flags.HasFlag(ObjectFlags.CanBeDroppedOnAnyLand) == true
            || (!neutral && world.AreAllied(playerId, cell.OwnerPlayerId));
        if (objectTypeId is not (HatcheryRules.ChickenObjectId or CallToArmsRules.ObjectTypeId)) return allowed;
        RoomTypeId? room = database.RoomByTerrain(cell.TerrainId)?.TypeId;
        if (room == RoomTypeId.Portal && IsPortalCentre(world, database, tile, heroPortalToo: false)) return false;
        return allowed && !(room == RoomTypeId.DungeonHeart && IsHeartInner(world, database, tile));
    }

    /// <summary>Navigation-grid bit 3 (solid) or a door (<c>0x004528E0</c>: the same byte's high
    /// nibble). Ours from what writes that byte (<see cref="Navigation.WorldSteering"/>): solid
    /// terrain, and a closed, completed door; an open one leaves floor.</summary>
    private static bool IsSolidOrDoor(WorldState world, GameDatabase database, TilePos tile)
    {
        if (database.TerrainById(world.Tile(tile.X, tile.Y).TerrainId).IsSolid) return true;
        return world.DoorAt(tile.X, tile.Y) is { UnderConstruction: false, Open: false } door && (door.PlayerId & 0xF) != 0;
    }

    /// <summary>Whether the tile and its neighbour belong to one room: same terrain, same owner.</summary>
    private static bool SameRoom(WorldState world, in TileState cell, int x, int y) =>
        world.Contains(x, y) && world.Tile(x, y).TerrainId == cell.TerrainId
        && world.Tile(x, y).OwnerPlayerId == cell.OwnerPlayerId;

    /// <summary><c>Room_IsFloorSlot</c> (<c>0x00454110</c>) for Prison, Temple and Combat Pit, whose
    /// type rule accepts every interior cell (<c>+0x0C == 0xFF</c>: all eight neighbours in the room).</summary>
    private static bool IsFloorSlot(WorldState world, TilePos tile)
    {
        ref TileState cell = ref world.Tile(tile.X, tile.Y);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx | dy) != 0 && !SameRoom(world, cell, tile.X + dx, tile.Y + dy)) return false;
        return true;
    }

    /// <summary>A Portal's (and for <paramref name="heroPortalToo"/> a Hero Portal's) centre: cell
    /// byte <c>+0x0C == 4</c>, the 3×3 code <c>0x004EF730</c> gives a tile with its own room on all
    /// four sides.</summary>
    private static bool IsPortalCentre(WorldState world, GameDatabase database, TilePos tile, bool heroPortalToo)
    {
        ref TileState cell = ref world.Tile(tile.X, tile.Y);
        RoomTypeId? room = database.RoomByTerrain(cell.TerrainId)?.TypeId;
        if (room != RoomTypeId.Portal && !(heroPortalToo && room == RoomTypeId.HeroPortal)) return false;
        return SameRoom(world, cell, tile.X - 1, tile.Y) && SameRoom(world, cell, tile.X + 1, tile.Y)
            && SameRoom(world, cell, tile.X, tile.Y - 1) && SameRoom(world, cell, tile.X, tile.Y + 1);
    }

    /// <summary><c>0x004DA150</c> returning 0: the Heart's inner 3×3, its first slab plus 1 to 3 on
    /// each axis (ours the bounding box's corner, as <see cref="DropLandingRules"/> reads it).</summary>
    private static bool IsHeartInner(WorldState world, GameDatabase database, TilePos tile)
    {
        if (RoomIndex.RoomAt(world, database, world.TileIndex(tile)) is not { } heart) return false;
        int dx = tile.X - heart.MinX, dy = tile.Y - heart.MinY;
        return dx is >= 1 and <= 3 && dy is >= 1 and <= 3;
    }

    /// <summary>Whether <paramref name="creature"/> may be slapped by
    /// <paramref name="playerId"/>: <see cref="CreatureFlags.CanBeSlapped"/>, the keeper's own
    /// creature at liberty, not in a hand, not down, and no timing gate (see
    /// <see cref="KnockBackWindowSeconds"/>). A prisoner may be slapped only by its jailer. Horny is
    /// excluded before every other test, as in the original: a slap on
    /// <see cref="CreatureFlags.IsHorny"/> runs another routine and never reaches the work bonus
    /// (Q158).</summary>
    public static bool CanSlap(
        GameDatabase database, CreatureState creature, PlayerId playerId)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (!definition.Flags.HasFlag(CreatureFlags.CanBeSlapped)) return false;
        if (definition.Flags.HasFlag(CreatureFlags.IsHorny)) return false;
        if (creature.InHand) return false;

        // A prisoner answers to its jailer and to nobody else, its own keeper included:
        // "a keeper can't slap its own creature in someone else's dungeon" (owner,
        // 2026-09-11, ADR-0140). Anything at liberty is its owner's as before.
        if (PrisonRules.IsImprisoned(creature))
            return creature.PrisonOwnerPlayerId == playerId.Value
                   && !CreatureDeathRules.IsDown(creature);

        if (creature.OwnerPlayerId != playerId) return false;

        return !CreatureDeathRules.IsDown(creature);
    }

    /// <summary>Whether a held creature's mood should take <c>AngerInHand</c> on this tick: once a
    /// game second, from <see cref="AngerGraceTicks"/> after it was picked up. The original tests
    /// <c>now % </c>variable 19<c> == 0</c>, the last tick of each game second — the cadence the
    /// anger ladder already runs on (ADR-0017), so this rides the same clock.</summary>
    public static bool AngerDueThisTick(CreatureState creature, GameTick now)
    {
        if (!creature.InHand) return false;
        if (now.Value - creature.PickupTick <= AngerGraceTicks) return false;

        return (now.Value + 1) % SimulationTiming.TicksPerSecond == 0;
    }
}
