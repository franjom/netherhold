// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>DK2's room furniture slots and coordinate-stable object choice.</summary>
/// <remarks>Room-specific decorative structures are separate passes and are not
/// represented here. The Temple Hand is included because it is the Temple's
/// attraction capacity, not decoration (Q89, Q270).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/auto-placed-furniture.md")]
public static class AutoFurnitureRules
{
    public const byte TempleHandObjectId = 66;

    /// <summary>Object 95, <i>Casino Pay Out Adjuster</i>: the Casino's lever (Q423).</summary>
    public const byte CasinoLeverObjectId = 95;

    /// <summary>Object 75, <i>Graveyard Crypt</i>: the Graveyard's own pass places it
    /// (<see cref="GraveyardRules.PlaceCrypts"/>), and <c>Room_IsFloorSlot</c> refuses its cell.</summary>
    public const byte CryptObjectId = 75;

    /// <summary>Builds the ordered furniture specification for one room.</summary>
    public static void DesiredFurniture(
        WorldState world, GameDatabase database, RoomInstance room,
        List<(int TileIndex, byte ObjectTypeId)> into)
    {
        if (into is null) throw new ArgumentNullException(nameof(into));
        into.Clear();

        if (room.Definition.TypeId == RoomTypeId.Hatchery)
        {
            var coops = new List<int>();
            HatcheryRules.CoopTiles(world, room, coops);
            foreach (int tile in coops) into.Add((tile, HatcheryRules.CoopObjectId));
            return;
        }

        var roomTiles = new HashSet<int>(room.TileIndices);

        if (room.Definition.TypeId == RoomTypeId.Temple)
        {
            int handTile = TempleHandTile(world, room, roomTiles);
            if (handTile >= 0) into.Add((handTile, TempleHandObjectId));
            return;
        }

        var slots = new List<int>();
        FloorSlots(world, room, roomTiles, slots);

        // The Graveyard's crypts are its own pass's (0x004DD5C0), placed before this one on a rebuild;
        // here they are kept where they stand, and their cells are no floor slots for the stones.
        if (room.Definition.TypeId == RoomTypeId.Graveyard)
            foreach (int crypt in CryptTiles(world, room)) into.Add((crypt, CryptObjectId));

        // The Casino's lever takes one of the floor slots, so no table stands on it. Placed
        // whatever the room's furniture list holds: it is the set-up's own object.
        if (room.Definition.TypeId == RoomTypeId.Casino)
        {
            int leverTile = CasinoLeverTile(world, slots);
            if (leverTile >= 0)
            {
                into.Add((leverTile, CasinoLeverObjectId));
                slots.Remove(leverTile);
            }
        }

        int choiceCount = FloorChoiceCount(room.Definition);
        if (room.Definition.Flags.HasFlag(RoomFlags.SpecialTiles) && choiceCount != 0)
        {
            foreach (int tile in slots)
            {
                int x = tile % world.Width;
                int y = tile / world.Width;
                int choice = CoordinateChoice(x, y, choiceCount);
                into.Add((tile, room.Definition.ObjectIds[choice]));
            }
        }

        AddWallFurniture(world, database, room, roomTiles, into);
        if (room.Definition.TypeId == RoomTypeId.TortureChamber)
        {
            int count = into.Count;
            for (int i = 0; i < count; i++)
                if (into[i].ObjectTypeId is 62 or 63) into.Add((into[i].TileIndex, TortureRules.WheelObjectId));
        }
    }

    /// <summary>The general capacity arms established by Q270, and <c>Room_ComputeTotalCapacity</c>'s
    /// shared arm for the Temple and the Combat Pit (<c>0x004E3ED0</c>, types 13 and 16): the room's
    /// tiles that <c>Room_IsFloorSlot</c> rejects, its rim, with no +1 (Q543). Its default arm
    /// (<c>0x004E429A</c>: Treasury, Lair, Hatchery, Heart, Guard Room, the bridges and the hero
    /// rooms) stores the slab count <c>+0xA7</c> (ADR-0440).</summary>
    public static bool TryTotalCapacity(WorldState world, RoomInstance room, out int capacity)
    {
        bool plusOne;
        switch (room.Definition.TypeId)
        {
            case RoomTypeId.Temple:
            case RoomTypeId.CombatPit:
                capacity = room.TileCount - FloorSlotSet(world, room).Count;
                return true;
            // Not read here: the Portal's arm stores 0, the Prison's 0xFFFF (its own count is +0x4B,
            // PrisonRules.Capacity) and the Graveyard's its floor slots (FloorSlotCount, Q544).
            case RoomTypeId.Portal:
            case RoomTypeId.Prison:
            case RoomTypeId.Graveyard:
                capacity = 0;
                return false;
            case RoomTypeId.Library:
            case RoomTypeId.TrainingRoom:
            case RoomTypeId.Workshop:
            case RoomTypeId.Casino:
                plusOne = true;
                break;
            case RoomTypeId.TortureChamber:
                plusOne = false;
                break;
            default:
                capacity = room.TileCount;
                return true;
        }

        var roomTiles = new HashSet<int>(room.TileIndices);
        int count = 0;
        foreach (ObjectState thing in world.Objects)
        {
            if (!thing.AutoPlaced || thing.InHand || thing.OwnerPlayerId != room.OwnerPlayerId)
                continue;
            if (!roomTiles.Contains(thing.TileIndex)) continue;
            if (!room.Definition.ObjectIds.Take(6).Contains(thing.ObjectTypeId)) continue;
            count++;
        }

        capacity = count == 0 ? 0 : count + (plusOne ? 1 : 0);
        return true;
    }

    private static int FloorChoiceCount(RoomDef room)
    {
        int count = 0;
        while (count < 4 && count < room.ObjectIds.Count && room.ObjectIds[count] != 0)
            count++;

        if (count == 4 && room.Flags.HasFlag(RoomFlags.SpecialWalls)) count = 3;
        return count;
    }

    private static void AddWallFurniture(
        WorldState world, GameDatabase database, RoomInstance room,
        HashSet<int> roomTiles,
        List<(int TileIndex, byte ObjectTypeId)> into)
    {
        if (!room.Definition.Flags.HasFlag(RoomFlags.SpecialWalls)) return;

        int choiceCount = 0;
        while (choiceCount < 3 && choiceCount + 3 < room.Definition.ObjectIds.Count
               && room.Definition.ObjectIds[choiceCount + 3] != 0)
            choiceCount++;
        if (choiceCount == 0) return;

        var slots = new List<int>();
        WallSlots(world, database, room, roomTiles, slots);
        foreach (int tile in slots)
        {
            int choice = CoordinateChoice(tile % world.Width, tile / world.Width, choiceCount);
            into.Add((tile, room.Definition.ObjectIds[choice + 3]));
        }
    }

    /// <summary>The wall-furniture slots (<c>0x00454230</c>): room tiles with a straight wall on a
    /// side, of the parity of the first such tile in each orientation.</summary>
    private static void WallSlots(
        WorldState world, GameDatabase database, RoomInstance room, HashSet<int> roomTiles, List<int> slots) =>
        WallSlots(world, database, room, roomTiles, slots, sides: null);

    /// <summary>The side of a wall slot's wall, 0 to 3 for north, east, south and west (the wall pass's
    /// side byte, <c>casino.md</c> §3); -1 for a tile that is no wall slot of the room.</summary>
    public static int WallSide(WorldState world, GameDatabase database, RoomInstance room, int tileIndex)
    {
        var slots = new List<int>();
        var sides = new List<int>();
        WallSlots(world, database, room, new HashSet<int>(room.TileIndices), slots, sides);
        int at = slots.IndexOf(tileIndex);
        return at < 0 ? -1 : sides[at];
    }

    private static void WallSlots(
        WorldState world, GameDatabase database, RoomInstance room, HashSet<int> roomTiles, List<int> slots,
        List<int>? sides)
    {
        int? horizontalPhase = null;
        int? verticalPhase = null;
        foreach (int tile in SlabChain.Order(world, room))
        {
            int x = tile % world.Width;
            int y = tile / world.Width;
            for (int direction = 0; direction < 4; direction++)
            {
                if (!HasStraightWall(world, database, roomTiles, x, y, direction)) continue;

                int coordinate = direction % 2 == 0 ? x : y;
                ref int? phase = ref (direction % 2 == 0
                    ? ref horizontalPhase
                    : ref verticalPhase);
                phase ??= coordinate & 1;
                if ((coordinate & 1) != phase) continue;

                slots.Add(tile);
                sides?.Add(direction);
                break;
            }
        }
    }

    /// <summary>Every furniture slot of the room, floor (<c>0x00454110</c>) and wall
    /// (<c>0x00454230</c>), whether or not anything stands there: the tiles the Workshop's crates
    /// are not set down on (<see cref="CrateRules.Arrange"/>). Neither test asks the room's
    /// special-tiles or special-walls flag.</summary>
    public static void FurnitureSlots(WorldState world, GameDatabase database, RoomInstance room, HashSet<int> into)
    {
        into.Clear();
        var roomTiles = new HashSet<int>(room.TileIndices);
        var slots = new List<int>();
        FloorSlots(world, room, roomTiles, slots);
        into.UnionWith(slots);
        slots.Clear();
        WallSlots(world, database, room, roomTiles, slots);
        into.UnionWith(slots);
    }

    private static bool HasStraightWall(
        WorldState world, GameDatabase database, HashSet<int> roomTiles,
        int x, int y, int direction)
    {
        ReadOnlySpan<int> dx = [0, 1, 0, -1];
        ReadOnlySpan<int> dy = [-1, 0, 1, 0];
        int sideX = dx[direction];
        int sideY = dy[direction];
        int alongX = -sideY;
        int alongY = sideX;

        return IsWall(world, database, x + sideX, y + sideY)
            && IsRoom(world, roomTiles, x + alongX, y + alongY)
            && IsRoom(world, roomTiles, x - alongX, y - alongY)
            && IsWall(world, database, x + sideX + alongX, y + sideY + alongY)
            && IsWall(world, database, x + sideX - alongX, y + sideY - alongY);
    }

    private static bool IsRoom(WorldState world, HashSet<int> roomTiles, int x, int y) =>
        world.Contains(x, y) && roomTiles.Contains((y * world.Width) + x);

    private static bool IsWall(WorldState world, GameDatabase database, int x, int y)
    {
        if (!world.Contains(x, y)) return false;
        TerrainFlags flags = database.TerrainById(world.Tile(x, y).TerrainId).Flags;
        return flags.HasFlag(TerrainFlags.Solid)
            && flags.HasFlag(TerrainFlags.AllowRoomWalls);
    }

    /// <summary>The rooms whose floor slots are one parity of a checkerboard: Guard Room, Workshop
    /// and Casino (<c>FUN_004EF5E0</c>).</summary>
    public static bool UsesCheckerboard(RoomTypeId? type) =>
        type is RoomTypeId.GuardRoom or RoomTypeId.Workshop or RoomTypeId.Casino;

    private static int TempleHandTile(
        WorldState world, RoomInstance room, HashSet<int> roomTiles)
    {
        foreach (int tile in room.TileIndices)
        {
            if (!IsSurroundedByRoom(world, roomTiles, tile)) continue;

            int x = tile % world.Width;
            int y = tile / world.Width;
            bool surroundedBySlots = true;
            for (int dy = -1; dy <= 1 && surroundedBySlots; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int neighbour = ((y + dy) * world.Width) + x + dx;
                if (!IsSurroundedByRoom(world, roomTiles, neighbour))
                {
                    surroundedBySlots = false;
                    break;
                }
            }

            if (surroundedBySlots) return tile;
        }
        return -1;
    }

    /// <summary>The tiles of <paramref name="room"/> that DK2's floor-slot predicate
    /// <c>0x00454110</c> accepts: its floor-furniture slots. A Library stores research books
    /// only on these (library-and-research.md §7).</summary>
    public static HashSet<int> FloorSlotSet(WorldState world, RoomInstance room)
    {
        var slots = new List<int>();
        FloorSlots(world, room, new HashSet<int>(room.TileIndices), slots);
        return new HashSet<int>(slots);
    }

    /// <summary>How many floor slots a room has (<c>0x00454110</c> over its tiles): a Graveyard's
    /// <c>TotalCapacity</c>.</summary>
    public static int FloorSlotCount(WorldState world, RoomInstance room)
    {
        var slots = new List<int>();
        FloorSlots(world, room, new HashSet<int>(room.TileIndices), slots);
        return slots.Count;
    }

    /// <summary>Whether a room tile is a floor slot (<c>Room_IsFloorSlot</c> <c>0x00454110</c>).</summary>
    public static bool IsFloorSlot(WorldState world, RoomInstance room, int tileIndex)
    {
        if (room.Definition.TypeId is RoomTypeId.Portal or RoomTypeId.DungeonHeart
            or RoomTypeId.WoodenBridge or RoomTypeId.StoneBridge or RoomTypeId.HeroStoneBridge)
            return false;
        var slots = new List<int>();
        FloorSlots(world, room, new HashSet<int>(room.TileIndices), slots);
        return slots.Contains(tileIndex);
    }

    /// <summary>The floor-furniture slots: interior tiles, and for the checkerboard rooms only
    /// those of the parity of the leftmost one (<c>FUN_004EF5E0</c>). In index order.</summary>
    private static void FloorSlots(
        WorldState world, RoomInstance room, HashSet<int> roomTiles, List<int> slots)
    {
        foreach (int tile in room.TileIndices)
            if (IsSurroundedByRoom(world, roomTiles, tile)) slots.Add(tile);

        // The Graveyard arm refuses a cell holding a crypt (0x0045416C).
        if (room.Definition.TypeId == RoomTypeId.Graveyard && slots.Count > 0)
        {
            HashSet<int> crypts = CryptTiles(world, room);
            if (crypts.Count > 0) slots.RemoveAll(crypts.Contains);
        }

        if (!UsesCheckerboard(room.Definition.TypeId) || slots.Count == 0) return;

        int anchor = slots[0];
        foreach (int tile in slots)
            if ((tile % world.Width) < (anchor % world.Width)) anchor = tile;
        int phase = ((anchor % world.Width) + (anchor / world.Width)) & 1;
        slots.RemoveAll(tile =>
            (((tile % world.Width) + (tile / world.Width)) & 1) != phase);
    }

    /// <summary>Where the Casino's set-up <c>0x004D6ED0</c> puts its lever: the first slab in the
    /// room's chain that <c>0x00454110</c> accepts and whose three neighbours to the north
    /// (north-west, north, north-east) it does not. For a Casino that predicate is the
    /// floor-furniture slot: an interior slab of the room's checkerboard parity. So a Casino
    /// needs an interior, 3×3 at least, and the lever is the first slot of its top interior
    /// row (owner, 2026-09-24). Ours walks the slots in index order; DK2's chain order is not
    /// read (<c>state-category-hooks.md</c> §3.1).</summary>
    public static int CasinoLeverTile(WorldState world, List<int> slots)
    {
        var slotSet = new HashSet<int>(slots);
        foreach (int tile in slots)
        {
            int x = tile % world.Width;
            int y = tile / world.Width;
            if (!IsRoom(world, slotSet, x - 1, y - 1)
                && !IsRoom(world, slotSet, x, y - 1)
                && !IsRoom(world, slotSet, x + 1, y - 1))
                return tile;
        }
        return -1;
    }

    /// <summary>The room's tiles with a crypt (object 75) on them.</summary>
    public static HashSet<int> CryptTiles(WorldState world, RoomInstance room)
    {
        var tiles = new HashSet<int>();
        foreach (ObjectState thing in world.Objects)
            if (thing.ObjectTypeId == CryptObjectId && !thing.InHand && room.Contains(thing.TileIndex)) tiles.Add(thing.TileIndex);
        return tiles;
    }

    private static bool IsSurroundedByRoom(
        WorldState world, HashSet<int> roomTiles, int tile)
    {
        int x = tile % world.Width;
        int y = tile / world.Width;
        if (x <= 0 || y <= 0 || x >= world.Width - 1 || y >= world.Height - 1) return false;

        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            if (!roomTiles.Contains(((y + dy) * world.Width) + x + dx)) return false;
        }
        return true;
    }

    /// <summary>An object's position, its <c>+0x1A</c>: its tile's centre, moved <see cref="WallInset"/>
    /// toward its wall for the Alcove Barrels (68, <c>0x004EEA07</c>) and the Library Book (58,
    /// <c>0x004EECEB</c>), sides north, east, south, west (<c>casino.md</c> §3).
    /// <para>Deviates: every wall-pass object is moved, the default arm <c>0x004EEE1F</c> included
    /// (<c>furniture-placement.md</c> §3), so the Workshop's wall Anvils and Buckets stand <c>0x700</c>
    /// toward their wall, correcting ADR-0404's centred machines (Q581).</para></summary>
    public static (int X, int Y) FurniturePosition(WorldState world, GameDatabase database, RoomInstance room, ObjectState thing)
    {
        int x = CreatureState.CentreOf(thing.TileIndex % world.Width);
        int y = CreatureState.CentreOf(thing.TileIndex / world.Width);
        if (!thing.AutoPlaced || !IsWallFurniture(room.Definition, thing)) return (x, y);
        int inset = WallInset(thing.ObjectTypeId);
        return WallSide(world, database, room, thing.TileIndex) switch
        {
            0 => (x, y - inset),
            1 => (x + inset, y),
            2 => (x, y + inset),
            3 => (x - inset, y),
            _ => (x, y),
        };
    }

    /// <summary>Whether this object's type appears in the room's wall-pass list.</summary>
    public static bool IsWallFurniture(RoomDef room, ObjectState thing)
    {
        if (!room.Flags.HasFlag(RoomFlags.SpecialWalls)) return false;
        for (int i = 3; i <= 5 && i < room.ObjectIds.Count; i++)
        {
            if (room.ObjectIds[i] == 0) break;
            if (room.ObjectIds[i] == thing.ObjectTypeId) return true;
        }
        return false;
    }

    /// <summary>The room pillar corner slots (furniture-placement.md §5), shared with the view.</summary>
    public static IEnumerable<int> RoomPillarCorners(WorldState world, RoomInstance room, TilePos tile)
    {
        bool InRoom(int x, int y) => world.Contains(x, y) && room.Contains(y * world.Width + x);
        for (int corner = 0; corner < 4; corner++)
        {
            int dx = corner < 2 ? 1 : -1;
            int dy = corner is 0 or 3 ? -1 : 1;
            if (InRoom(tile.X - dx, tile.Y) || InRoom(tile.X, tile.Y - dy)) continue;
            bool block = true;
            for (int j = 0; j < 3 && block; j++)
                for (int i = 0; i < 3 && block; i++)
                    block = InRoom(tile.X + i * dx, tile.Y + j * dy);
            if (block) yield return corner;
        }
    }

    /// <summary>How far the wall pass moves an object toward its wall, by its arm (index bytes
    /// <c>0x004EF130</c>, table <c>0x004EF114</c>): the Torture Wheel <c>0x7C0</c>, Torture Coals and
    /// Water <c>0x480</c>, every other type <c>0x700</c>, the default arm <c>0x004EEE1F</c> included
    /// (<c>furniture-placement.md</c> §3, which corrects ADR-0404's centred Anvils and Buckets).</summary>
    public static int WallInset(byte objectTypeId) => objectTypeId switch
    {
        8 => 0x7C0,
        62 or 63 => 0x480,
        _ => 0x700,
    };

    /// <summary>The coordinate-seeded selector Room_Rebuild draws from: a furniture slot's choice, a
    /// bookcase's variant (<c>library-states.md</c> §4).</summary>
    public static int Choice(int x, int y, int count) => CoordinateChoice(x, y, count);

    private static int CoordinateChoice(int x, int y, int count)
    {
        uint seed = SelectorSeed(x, y);
        return SelectorNext(ref seed, count);
    }

    /// <summary>The selector's seed for a tile, <c>Step(y, 0x2286) XOR Step(x, 0x16F2)</c>. Each floor
    /// arm seeds it afresh and then draws in order (the Book Case's at <c>0x004EE6E3</c>).</summary>
    public static uint SelectorSeed(int x, int y)
    {
        uint sx = unchecked((uint)x);
        uint sy = unchecked((uint)y);
        return Step(ref sy, 0x2286) ^ Step(ref sx, 0x16F2);
    }

    /// <summary>The selector's next draw below <paramref name="count"/>.</summary>
    public static int SelectorNext(ref uint seed, int count) => (int)Step(ref seed, unchecked((uint)count));

    private static uint Step(ref uint seed, uint modulus)
    {
        seed = unchecked(seed * 0x0019660D + 0x3C6EF35F);
        uint rotated = (seed >> 13) | (seed << 19);
        return rotated % modulus;
    }
}
