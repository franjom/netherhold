// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Where each object stands, which way it faces and which of its arts it shows
/// (furniture-placement.md, object-drawing.md), and the decorations DK2's rooms place that the
/// simulation does not hold.</summary>
public sealed partial class WorldView
{
    private const int One = Fp32.One, Half = One / 2, FloorZ = One;

    private ObjectView? AttachedWheel(WorldState world, CreatureState creature)
    {
        if (!TortureRules.IsBeingTortured(creature)) return null;
        int tile = creature.Tile.Y * world.Width + creature.Tile.X;
        ObjectState? device = TortureRules.FreeDeviceAt(world, _database,
            TortureRules.KeeperOf(creature), tile, creature.Id);
        return device?.ObjectTypeId == TortureRules.WheelObjectId ? Placed(world, device, creature.Tile) : null;
    }

    // Object types the placement switches on (Objects.kwd ids).
    private const byte FloorGoldId = 1, GoldBagId = 2, StorePileId = 3, TortureWheelId = 8, ChickenId = 9,
        DungeonHeartId = 13, TrainingDummyId = 14, RouletteWheelId = 30, CoopId = 35, WorkBenchId = 46,
        SkullId = 49, LavatoryId = 51, BookCaseId = 64, TempleHandId = 66, GuardPostId = 70, CryptId = 75,
        HeartArchesId = 86, RouletteTableId = 87, BigStepsId = 88, ArchPillarId = 94, CasinoLeverId = 95,
        HeartObstacleId = 100, PrisonDoorId = 109, TempleCandlestickId = 111, PrisonDoorBarId = 116,
        CryptBellId = 142, WorkshopPillarId = 80, TortureCoalsId = 62, TortureWaterId = 63;

    // A decoration's id: minus (tile × slots + slot + 1), one slot per thing a tile can hold.
    private const int DecorSlots = 20;
    private const int PileSlot = 0, HeartSlot = 1, PillarSlot = 8, CornerSlot = 9, DoorSlot = 13, BarSlot = 14,
        BellSlot = 15, RouletteSlot = 16, WheelSlot = 18, ArchPillarSlot = 19;

    private readonly List<RoomInstance> _rooms = [];
    private readonly Dictionary<int, AssetRef> _objectArt = [];

    // The decorations, built once a tick as the room facts are (EnsureRooms), and the tiles holding
    // a Pillar-flagged object while they are built.
    private readonly List<ObjectView> _decorations = [];
    private readonly HashSet<int> _pillarTiles = [];
    private WorldState? _decorationsWorld;
    private GameTick _decorationsTick;

    /// <summary>Whether an object id is a decoration of <see cref="Decorations"/>, not the simulation's.</summary>
    public static bool IsDecoration(int objectId) => objectId < 0;

    /// <summary>
    /// The objects DK2's room passes place that the simulation does not hold: the Heart's set
    /// (<c>0x004DB700</c>), room pillars (<c>0x004EF1F0</c>), the Guard Room's posts, the Workshop's
    /// pillars, the Temple's candlesticks, the Prison's door and bar, the Crypt's bell, a roulette
    /// table's two wheels, the wheel beside Torture coals or water, and a treasury or heart tile's
    /// gold (<c>0x004F4440</c>; ours keeps that gold in the tile). Visible tiles only.
    /// <para>Deviates: in DK2 these are objects with ids, and a pillar also sets its tile's movement
    /// gate (<c>furniture-placement.md</c> §5), which ours does not model.</para>
    /// </summary>
    public IEnumerable<ObjectView> Decorations(WorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        EnsureDecorations(world);
        foreach (ObjectView decoration in _decorations)
            if (IsVisible(world, decoration.Tile)) yield return decoration;
    }

    private void EnsureDecorations(WorldState world)
    {
        if (ReferenceEquals(_decorationsWorld, world) && _decorationsTick == world.CurrentTick) return;
        EnsureRooms(world);
        _decorationsWorld = world;
        _decorationsTick = world.CurrentTick;
        _decorations.Clear();
        _pillarTiles.Clear();
        foreach (ObjectState thing in world.Objects)
            if (!thing.InHand && _database.ObjectById(thing.ObjectTypeId) is { } definition
                && definition.Flags.HasFlag(ObjectFlags.Pillar))
                _pillarTiles.Add(thing.TileIndex);
        for (int i = 0; i < _rooms.Count; i++) Decorate(world, _rooms[i], _roomFacts[i], _decorations);
        foreach (ObjectState thing in world.Objects) DecorateBeside(world, thing, _decorations);
    }

    /// <summary>Whether a bed is hidden: state 83 (<c>0x0046FA60</c>) clears a bed's draw bit while
    /// its creature sleeps, which then shows its own sleep clip, unless the bed is the Hero
    /// Creature Bed (6). Deviates: the Mistress's bed first plays its door clip (art 7); and DK2
    /// does this only when the creature's art 12 is loaded and a creature vtable <c>+0x278</c> call
    /// on the bed returns 0, both assumed true here (neither is read).</summary>
    private bool BedHidden(WorldState world, ObjectState thing)
    {
        if (thing.ObjectTypeId == HeroBedId || thing.CreatureId <= 0) return false;
        if (_database.ObjectById(thing.ObjectTypeId) is not { } definition || !definition.Flags.HasFlag(ObjectFlags.TypeLair))
            return false;
        return world.CreatureById(thing.CreatureId) is { Dk2StateId: LairRules.SleepingState };
    }

    private const byte HeroBedId = 6;

    /// <summary>An object of the simulation, placed.</summary>
    private ObjectView Placed(WorldState world, ObjectState thing, TilePos at)
    {
        int x = at.X, y = at.Y, tileIndex = (y * world.Width) + x;
        int posX = (x * One) + Half, posY = (y * One) + Half, z = FloorHeight(world, x, y);
        int heading = 0, scale = One, slot = 0, pitch = 0, roll = 0;
        ObjectDef? definition = _database.ObjectById(thing.ObjectTypeId);
        RoomInstance? room = RoomAt(world, tileIndex);

        // A wall-pass object faces into the room, moved toward its wall (0x004EE9CE).
        int side = room is not null && thing.AutoPlaced && AutoFurnitureRules.IsWallFurniture(room.Definition, thing)
            ? AutoFurnitureRules.WallSide(world, _database, room, tileIndex)
            : -1;
        if (side >= 0)
        {
            heading = ((side + 2) & 3) << 9;
            int inset = AutoFurnitureRules.WallInset(thing.ObjectTypeId);
            posX += Dx8[side * 2] * inset;
            posY += Dy8[side * 2] * inset;
            if (thing.ObjectTypeId == TortureWheelId) (z, roll) = WallWheel(world, x, y, z);
        }
        else
        {
            switch (thing.ObjectTypeId)
            {
                case FloorGoldId or GoldBagId:
                    // Object_AddGold 0x0049C760: half size empty, growing with the gold to 5000.
                    scale = thing.GoldHeld < 5000 ? 0x800 + (thing.GoldHeld / 2) : 0x11C4;
                    break;
                case TortureWheelId:
                    // Laid flat by +0x64 = 0x600, then spun about the vertical (0x004A1C60).
                    heading = TortureWheelHeading(world, x, y);
                    pitch = 0x600;
                    break;
                case TrainingDummyId or WorkBenchId:
                    heading = AutoFurnitureRules.Choice(x, y, 0x800);
                    break;
                case BookCaseId:
                    // The arm at 0x004EE6A5: book_case1..3 by the selector, never book_case4.
                    heading = (y & 1) << 10;
                    slot = 5 + AutoFurnitureRules.Choice(x, y, 3);
                    break;
                case >= SkullId and <= LavatoryId:
                {
                    uint seed = AutoFurnitureRules.SelectorSeed(x, y);
                    posX += AutoFurnitureRules.SelectorNext(ref seed, 0x800) - 0x400;
                    posY += AutoFurnitureRules.SelectorNext(ref seed, 0x800) - 0x400;
                    heading = AutoFurnitureRules.SelectorNext(ref seed, 0x800);
                    break;
                }
                case CoopId:
                    heading = AutoFurnitureRules.Choice(x, y, 4) << 9;
                    z = 0xE00;
                    break;
                case TempleHandId or PrisonDoorId or PrisonDoorBarId:
                    heading = 0x400;
                    break;
                case CasinoLeverId:
                    heading = 0x400;
                    z = FloorZ;
                    break;
                case CryptId:
                    heading = 0x200;
                    break;
                case ChickenId:
                    // Object_Init draws 0x1000 + random(0x800); ours has no draw to keep, so the id stands in.
                    scale = One + Spread(thing.Id, 0x800);
                    break;
                default:
                    // A bed's heading is a simulation-stream draw (0x0048AD50) ours does not keep.
                    if (definition is not null && definition.Flags.HasFlag(ObjectFlags.TypeLair))
                        heading = Spread(thing.Id, 0x800);
                    break;
            }
        }

        return new ObjectView(thing.Id, thing.ObjectTypeId, thing.OwnerPlayerId, at, ObjectArt(thing.ObjectTypeId, slot),
            thing.GoldHeld, posX, posY, z, heading & 0x7FF, scale, pitch, roll & 0x7FF);
    }

    /// <summary>A wheel off the floor: drawn <c>0x800</c> up (its <c>+0x99</c> draw offset) and spun
    /// about its own axle, state <c>0x1E</c> writing the floor wheel's turn into <c>+0x68</c>.</summary>
    private static (int Height, int Roll) WallWheel(WorldState world, int x, int y, int floor) =>
        (floor + 0x800, TortureWheelHeading(world, x, y));

    /// <summary><c>0x004F2950</c>: a floor Torture Wheel turns ((x + y) &amp; 3) + 4 a turn, one way
    /// on odd tiles and the other on even ones.</summary>
    private static int TortureWheelHeading(WorldState world, int x, int y)
    {
        long turn = world.CurrentTick.Value;
        int rate = ((x + y) & 3) + 4;
        int heading = (int)((turn * rate) & 0x7FF);
        return ((x + y) & 1) == 1 ? heading : -heading;
    }

    /// <summary>A stand-in for a simulation-stream draw ours does not keep: a value below
    /// <paramref name="range"/> mixed from an id.</summary>
    private static int Spread(int id, int range) => (int)((unchecked((uint)id * 0x9E3779B1u) >> 8) % (uint)range);

    private void Decorate(WorldState world, RoomInstance room, in RoomFacts facts, List<ObjectView> into)
    {
        RoomTypeId? type = room.Definition.TypeId;
        PlayerId owner = new(room.OwnerPlayerId);
        switch (type)
        {
            case RoomTypeId.DungeonHeart:
                DecorateHeart(world, room, owner, into);
                break;
            case RoomTypeId.GuardRoom:
                Corners(world, room, owner, GuardPostId, PostOffsets, FloorZ, into);
                break;
            case RoomTypeId.Workshop:
                Corners(world, room, owner, WorkshopPillarId, PostOffsets, -1, into);
                break;
            case RoomTypeId.Temple:
                Corners(world, room, owner, TempleCandlestickId, CandleOffsets, FloorZ, into);
                break;
            case RoomTypeId.Prison when facts.DoorTile >= 0:
                into.Add(Decoration(world, facts.DoorTile, DoorSlot, PrisonDoorId, owner, 0, 0, -1, 0x400));
                into.Add(Decoration(world, facts.DoorTile, BarSlot, PrisonDoorBarId, owner, 0, 0, -1, 0x400));
                break;
            case RoomTypeId.Crypt:
                DecorateCryptBell(world, room, owner, into);
                break;
        }

        if (PillarOf(type) is byte pillar) Pillars(world, room, owner, pillar, into);
        if (type is RoomTypeId.Treasury or RoomTypeId.DungeonHeart) StorePiles(world, room, owner, into);
    }

    /// <summary><c>0x004DB700</c> by slab-chain index: Arch Pillars at 7, 16 and 18, and at 12 (the
    /// centre of a level's 5×5, STRONGLY INFERRED from the chain order) the Obstacle, the Heart,
    /// three Big Steps and the Arches. Deviates: a ruined Heart draws none of them (DK2 skips the
    /// pass for a room with <c>+0x9A</c> bit 0; what stands after the ruin is unread). DK2 creates
    /// the Arch Pillars at height 0; they are drawn on the floor here (ASSUMED, as for the pillars).</summary>
    private void DecorateHeart(WorldState world, RoomInstance room, PlayerId owner, List<ObjectView> into)
    {
        if (world.HasPlayer(owner) && world.Player(owner).HeartRuined) return;
        int[] chain = SlabChain.Order(world, room);
        if (chain.Length <= 18) return;
        int[] pillarAt = [7, 16, 18];
        (int X, int Y)[] pillarPoint = [(0x800, 0x4CD), (0x667, 0x19A), (0x999, 0x19A)];
        for (int i = 0; i < 3; i++)
        {
            int tile = chain[pillarAt[i]];
            into.Add(Decoration(world, tile, ArchPillarSlot, ArchPillarId, owner,
                pillarPoint[i].X - Half, pillarPoint[i].Y - Half, -1, 0));
        }

        // The Obstacle is the neutral player's, or nobody's for a room nobody owns.
        int centre = chain[12];
        PlayerId obstacleOwner = room.OwnerPlayerId == 0 ? new PlayerId(0) : PlayerIds.Neutral;
        into.Add(Decoration(world, centre, HeartSlot, HeartObstacleId, obstacleOwner, 0, 0, -1, 0));
        into.Add(Decoration(world, centre, HeartSlot + 1, DungeonHeartId, owner, 0, 0, -1, 0));
        for (int n = 0; n < 3; n++)
            into.Add(Decoration(world, centre, HeartSlot + 2 + n, BigStepsId, owner, 0, 0, -1, ((n * 0x155) & 0x3FF) << 1));
        into.Add(Decoration(world, centre, HeartSlot + 5, HeartArchesId, owner, 0, 0, -1, 0));
    }

    // Convex-corner offsets, NE, SE, SW, NW: the Guard Post's and Workshop Pillar's 0x82A at angle
    // 0x10C + 0x200k (0x004DDE80, 0x004F5650), and the Temple candlestick's diagonal × 0x400 (0x004F1240).
    private static readonly (int X, int Y)[] PostOffsets = [(0x5FB, -0x58E), (0x58E, 0x5FC), (-0x5FC, 0x58F), (-0x58F, -0x5FB)];
    private static readonly (int X, int Y)[] CandleOffsets = [(0x400, -0x400), (0x400, 0x400), (-0x400, 0x400), (-0x400, -0x400)];

    /// <summary>Every room tile, and each corner k (NE, SE, SW, NW) whose three neighbours toward it
    /// are outside the room (tile byte <c>+0xC</c> masks <c>0xF8</c>, <c>0xE3</c>, <c>0x8F</c>, <c>0x3E</c>).
    /// A height of −1 is the Workshop Pillar's 0, drawn on the floor (ASSUMED, as for the pillars).</summary>
    private void Corners(WorldState world, RoomInstance room, PlayerId owner, byte objectTypeId,
        (int X, int Y)[] offsets, int z, List<ObjectView> into)
    {
        foreach (int tile in room.TileIndices)
        {
            int x = tile % world.Width, y = tile / world.Width;
            int mask = SameRoomMask(world, x, y, world.Tile(x, y));
            for (int k = 0; k < 4; k++)
            {
                int corner = (2 * k) + 1;
                int three = (1 << (corner - 1)) | (1 << corner) | (1 << ((corner + 1) & 7));
                if ((mask & three) != 0) continue;
                into.Add(Decoration(world, tile, CornerSlot + k, objectTypeId, owner, offsets[k].X, offsets[k].Y, z, 0));
            }
        }
    }

    private static byte? PillarOf(RoomTypeId? type) => type switch
    {
        RoomTypeId.Treasury => 76,
        RoomTypeId.Hatchery => 78,
        RoomTypeId.CombatPit => 79,
        RoomTypeId.TortureChamber => 82,
        RoomTypeId.Graveyard => 84,
        RoomTypeId.Casino => 85,
        RoomTypeId.Crypt => 141,
        _ => null,
    };

    // Pillar offsets toward k (NE, SE, SW, NW): 0x5A8 at angle 0x100 + 0x200k (0x004EF1F0).
    private static readonly (int X, int Y)[] PillarOffsets = [(0x3FF, -0x400), (0x3FF, 0x3FF), (-0x400, 0x3FF), (-0x400, -0x400)];

    /// <summary><c>0x004EF1F0</c>: a pillar in each tile whose two sides away from diagonal k are
    /// outside the room and from which a 3×3 of the room extends toward k, a quarter tile inward,
    /// heading ((k − 1) &amp; 3) &lt;&lt; 9; not on a tile holding a Pillar-flagged object.
    /// <para>ASSUMED: DK2 creates a pillar at height 0, and whether it then settles on the floor is
    /// unread (furniture-placement.md §8); it is drawn on the floor here. Deviates: DK2 asks the
    /// tile's type and owner, not the room, which differ for two such rooms touching diagonally.</para></summary>
    private void Pillars(WorldState world, RoomInstance room, PlayerId owner, byte pillar, List<ObjectView> into)
    {
        foreach (int tile in room.TileIndices)
        {
            int x = tile % world.Width, y = tile / world.Width;
            for (int k = 0; k < 4; k++)
            {
                int dx = Dx8[(2 * k) + 1], dy = Dy8[(2 * k) + 1];
                if (InRoom(world, room, x - dx, y) || InRoom(world, room, x, y - dy)) continue;
                if (!Block(world, room, x, y, dx, dy) || _pillarTiles.Contains(tile)) continue;
                // Its +0x95/+0x97 draw offset undoes the move, so DK2 draws it at the centre, its
                // heading turning the column into the tile's outer corner (object-drawing.md §4).
                into.Add(Decoration(world, tile, PillarSlot, pillar, owner, 0, 0, -1, ((k - 1) & 3) << 9));
            }
        }
    }

    private static bool Block(WorldState world, RoomInstance room, int x, int y, int dx, int dy)
    {
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
                if (!InRoom(world, room, x + (i * dx), y + (j * dy))) return false;
        return true;
    }

    /// <summary><c>0x004D8CB0</c>: the bell on the first chain tile whose eight neighbours are all
    /// the Crypt's floor.</summary>
    private void DecorateCryptBell(WorldState world, RoomInstance room, PlayerId owner, List<ObjectView> into)
    {
        foreach (int tile in SlabChain.Order(world, room))
        {
            int x = tile % world.Width, y = tile / world.Width;
            if (SameRoomMask(world, x, y, world.Tile(x, y)) != 0xFF) continue;
            bool interior = true;
            for (int k = 0; k < 8 && interior; k++)
                interior = SameRoomMask(world, x + Dx8[k], y + Dy8[k], world.Tile(x + Dx8[k], y + Dy8[k])) == 0xFF;
            if (!interior) continue;
            into.Add(Decoration(world, tile, BellSlot, CryptBellId, owner, 0, 0, -1, 0));
            return;
        }
    }

    /// <summary>A treasury or heart tile's gold as DK2's Gold Pile 1 (<c>0x004F4440</c>): Urn1 below a
    /// third of the tile cap (1000 on a live Heart, else 3000), Urn2 below two thirds, else the
    /// Chest. Its heading is a simulation-stream draw ours does not keep (<c>0x004F4090</c>).</summary>
    private void StorePiles(WorldState world, RoomInstance room, PlayerId owner, List<ObjectView> into)
    {
        bool liveHeart = room.Definition.TypeId == RoomTypeId.DungeonHeart
            && !(world.HasPlayer(owner) && world.Player(owner).HeartRuined);
        int cap = liveHeart ? 1000 : 3000;
        foreach (int tile in room.TileIndices)
        {
            int gold = world.Tile(tile % world.Width, tile / world.Width).GoldHeld;
            if (gold <= 0) continue;
            int slot = gold < cap / 3 ? 5 : gold < 2 * cap / 3 ? 6 : 0;
            ObjectView pile = Decoration(world, tile, PileSlot, StorePileId, owner, 0, 0, -1, Spread(tile, 0x800), slot);
            into.Add(new ObjectView(pile.Id, StorePileId, owner, pile.Tile, pile.Mesh, gold,
                pile.PosX, pile.PosY, pile.Height, pile.Heading, pile.Scale));
        }
    }

    /// <summary>Objects DK2 creates beside a simulation object: a roulette table's two wheels at
    /// ±<c>0x7D0</c> on y (the floor pass), and the Torture Wheel beside Coals or Water, moved
    /// <c>0x7C0</c> toward the same wall.</summary>
    private void DecorateBeside(WorldState world, ObjectState thing, List<ObjectView> into)
    {
        if (thing.InHand || !thing.AutoPlaced) return;
        int tile = thing.TileIndex;
        PlayerId owner = new(thing.OwnerPlayerId);
        if (thing.ObjectTypeId == RouletteTableId)
        {
            into.Add(Decoration(world, tile, RouletteSlot, RouletteWheelId, owner, 0, 0x7D0, -1, 0));
            into.Add(Decoration(world, tile, RouletteSlot + 1, RouletteWheelId, owner, 0, -0x7D0, -1, 0));
        }
        else if (thing.ObjectTypeId is TortureCoalsId or TortureWaterId && RoomAt(world, tile) is { } room)
        {
            if (world.Objects.Any(o => o.TileIndex == tile && o.ObjectTypeId == TortureWheelId)) return;
            int side = AutoFurnitureRules.WallSide(world, _database, room, tile);
            if (side < 0) return;
            int inset = AutoFurnitureRules.WallInset(TortureWheelId);
            int x = tile % world.Width, y = tile / world.Width;
            (int z, int roll) = WallWheel(world, x, y, FloorHeight(world, x, y));
            ObjectView wheel = Decoration(world, tile, WheelSlot, TortureWheelId, owner,
                Dx8[side * 2] * inset, Dy8[side * 2] * inset, z, ((side + 2) & 3) << 9);
            into.Add(new ObjectView(wheel.Id, TortureWheelId, owner, wheel.Tile, wheel.Mesh, 0,
                wheel.PosX, wheel.PosY, wheel.Height, wheel.Heading, wheel.Scale, 0, roll & 0x7FF));
        }
    }

    /// <summary>A decoration at its tile's centre plus an offset; a height of −1 is the tile's floor.</summary>
    private ObjectView Decoration(WorldState world, int tileIndex, int slot, byte objectTypeId, PlayerId owner,
        int offsetX, int offsetY, int z, int heading, int artSlot = 0)
    {
        int x = tileIndex % world.Width, y = tileIndex / world.Width;
        return new ObjectView(-((tileIndex * DecorSlots) + slot + 1), objectTypeId, owner, new TilePos(x, y),
            ObjectArt(objectTypeId, artSlot), 0, (x * One) + Half + offsetX, (y * One) + Half + offsetY,
            z < 0 ? FloorHeight(world, x, y) : z, heading & 0x7FF, One);
    }

    private RoomInstance? RoomAt(WorldState world, int tileIndex)
    {
        EnsureRooms(world);
        int index = _roomOf[tileIndex];
        return index == 0 ? null : _rooms[index - 1];
    }

    /// <summary>An object's art: file art 0, or one of 5 to 8 (<see cref="AssetCatalogue.ObjectArt"/>).</summary>
    private AssetRef ObjectArt(byte objectTypeId, int slot)
    {
        if (slot == 0)
        {
            _objectMeshes.TryGetValue(objectTypeId, out AssetRef mesh);
            return mesh;
        }
        int key = (objectTypeId << 4) | slot;
        if (!_objectArt.TryGetValue(key, out AssetRef art))
        {
            art = _database.ObjectById(objectTypeId) is { } definition
                ? AssetCatalogue.ObjectArt(definition.Name, slot)
                : default;
            _objectArt[key] = art;
        }
        return art;
    }

    /// <summary>
    /// <c>Tile_FloorHeight</c> (<c>0x004545C0</c>) at a tile's centre, where a resting object
    /// settles: the floor slots of the Hatchery (<c>0xE00</c>), Temple (0), Graveyard and Crypt
    /// (<c>0xF00</c>) and Combat Pit (<c>0x400</c>); the Heart's centre and the Portal's (0);
    /// bridges (<c>0x1199</c>); water and lava (<c>0xA66</c>); else <c>0x1000</c>.
    /// <para>Deviates: DK2 slopes the edges of those four rooms by the neighbour mask and a bank by
    /// its own; this answers at the centre with no slope.</para>
    /// </summary>
    private int FloorHeight(WorldState world, int x, int y)
    {
        TileState tile = world.Tile(x, y);
        TerrainDef terrain = _database.TerrainById(tile.TerrainId);
        if (_database.RoomsByTerrain.TryGetValue(tile.TerrainId, out RoomDef? room))
        {
            if (BridgeRules.IsBridge(room)) return 0x1199;
            bool interior = SameRoomMask(world, x, y, tile) == 0xFF;
            switch (room.TypeId)
            {
                case RoomTypeId.DungeonHeart:
                    return FactsAt(world, x, y) is { } heart && x == heart.FirstX + 2 && y == heart.FirstY + 2 ? 0 : FloorZ;
                case RoomTypeId.Portal:
                    return interior ? 0 : FloorZ;
                case RoomTypeId.Hatchery when interior:
                    return 0xE00;
                case RoomTypeId.Temple when interior:
                    return 0;
                case RoomTypeId.Graveyard or RoomTypeId.Crypt when interior:
                    return 0xF00;
                case RoomTypeId.CombatPit when interior:
                    return 0x400;
            }
            return FloorZ;
        }
        return (terrain.Flags & TerrainFlags.ConstructionTypeWater) != 0 ? 0xA66 : FloorZ;
    }
}
