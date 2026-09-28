// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI;

/// <summary>A versioned observation packet. Static definitions are supplied on read.</summary>
public static class KeeperViewSnapshot
{
    public const int FormatVersion = 1;
    private const uint Magic = 0x49414B44;

    public static byte[] ToBytes(KeeperView view)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic); w.Write(FormatVersion);
        w.Write(view.PlayerId); w.Write(view.Tick.Value); w.Write(view.Status);
        w.Write(view.Width); w.Write(view.Height); w.Write(view.Gold); w.Write(view.ImpCount);
        w.Write(view.Heart.HasValue);
        if (view.Heart is TilePos heart) { w.Write(heart.X); w.Write(heart.Y); }
        for (int i = 0; i < view.TileCount; i++)
        {
            KeeperTileView tile = view.Tile(view.FromIndex(i));
            w.Write(tile.TerrainId); w.Write(tile.OwnerPlayerId); w.Write(tile.TaggedBy);
        }
        w.Write(view.AvailabilityEntries.Count);
        foreach (AvailabilityState entry in view.AvailabilityEntries)
        {
            w.Write(entry.Kind); w.Write(entry.TypeId); w.Write(entry.Value);
        }
        w.Write(view.Creatures.Count);
        foreach (KeeperCreatureView c in view.Creatures)
        {
            w.Write(c.Id); w.Write(c.CreatureTypeId); w.Write(c.OwnerPlayerId);
            w.Write((int)c.Activity); w.Write(c.Health); w.Write(c.Level);
            w.Write(c.LastSlapTick); w.Write(c.CanSlap);
        }
        w.Write(view.AllRooms.Count);
        foreach (KeeperRoomView room in view.AllRooms)
        {
            w.Write(room.Definition.RoomId); w.Write(room.OwnerPlayerId);
            w.Write(room.MinX); w.Write(room.MinY); w.Write(room.MaxX); w.Write(room.MaxY);
            w.Write(room.TileIndices.Count);
            foreach (int index in room.TileIndices) w.Write(index);
        }
        w.Flush();
        return stream.ToArray();
    }

    public static KeeperView FromBytes(byte[] bytes, GameDatabase database)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var r = new BinaryReader(stream, Encoding.UTF8);
        if (r.ReadUInt32() != Magic || r.ReadInt32() != FormatVersion)
            throw new InvalidDataException("Unsupported keeper observation format.");
        byte player = r.ReadByte();
        var tick = new GameTick(r.ReadInt64());
        byte status = r.ReadByte();
        int width = r.ReadInt32(), height = r.ReadInt32();
        if (width <= 0 || height <= 0 || (long)width * height > bytes.Length / 4)
            throw new InvalidDataException("Invalid observation dimensions.");
        int gold = r.ReadInt32(), imps = r.ReadInt32();
        TilePos? heart = r.ReadBoolean() ? new TilePos(r.ReadInt32(), r.ReadInt32()) : null;
        var tiles = new KeeperTileView[width * height];
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = new KeeperTileView(r.ReadByte(), r.ReadByte(), r.ReadUInt16());
        var availability = new AvailabilityState[ReadCount(r, 10)];
        for (int i = 0; i < availability.Length; i++)
            availability[i] = new AvailabilityState(r.ReadUInt16(), r.ReadInt32(), r.ReadInt32());
        var creatures = new KeeperCreatureView[ReadCount(r, 27)];
        for (int i = 0; i < creatures.Length; i++)
            creatures[i] = new KeeperCreatureView(r.ReadInt32(), r.ReadByte(), r.ReadByte(),
                (CreatureActivity)r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt64(), r.ReadBoolean());
        var rooms = new KeeperRoomView[ReadCount(r, 22)];
        for (int i = 0; i < rooms.Length; i++)
        {
            RoomDef definition = database.RoomById(r.ReadByte());
            byte owner = r.ReadByte();
            int minX = r.ReadInt32(), minY = r.ReadInt32(), maxX = r.ReadInt32(), maxY = r.ReadInt32();
            var indices = new int[ReadCount(r, 4)];
            for (int j = 0; j < indices.Length; j++)
            {
                indices[j] = r.ReadInt32();
                if ((uint)indices[j] >= tiles.Length)
                    throw new InvalidDataException("Room tile is outside the observation.");
            }
            rooms[i] = new KeeperRoomView(definition, owner, indices, minX, minY, maxX, maxY);
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing keeper observation data.");
        return new KeeperView(database, player, tick, status, width, height, gold, imps,
            heart, tiles, availability, creatures, rooms);
    }

    private static int ReadCount(BinaryReader r, int minimumEntryBytes)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > (r.BaseStream.Length - r.BaseStream.Position) / minimumEntryBytes)
            throw new InvalidDataException("Invalid observation entry count.");
        return count;
    }
}
