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
    public const int FormatVersion = 19;
    private const uint Magic = 0x49414B44;

    public static byte[] ToBytes(KeeperView view)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic); w.Write(FormatVersion);
        w.Write(view.PlayerId.Value); w.Write(view.Tick.Value); w.Write(view.Status); w.Write(view.AlliedWith);
        w.Write(view.PlayerOrder.Count);
        foreach (PlayerId player in view.PlayerOrder) w.Write(player.Value);
        w.Write(view.Width); w.Write(view.Height); w.Write(view.Gold); w.Write(view.ImpCount);
        w.Write(view.Mana);
        w.Write(view.Heart.HasValue);
        if (view.Heart is TilePos heart) { w.Write(heart.X); w.Write(heart.Y); }
        for (int i = 0; i < view.TileCount; i++)
        {
            KeeperTileView tile = view.Tile(view.FromIndex(i));
            w.Write(tile.TerrainId); w.Write(tile.OwnerPlayerId); w.Write(tile.TaggedBy.Bits);
            w.Write(tile.NavigationByte);
        }
        w.Write(view.AvailabilityEntries.Count);
        foreach (AvailabilityState entry in view.AvailabilityEntries)
        {
            w.Write((ushort)entry.Kind); w.Write(entry.TypeId); w.Write(entry.Value); w.Write(entry.Upgraded);
            w.Write(entry.LastCastTick);
        }
        w.Write(view.Creatures.Count);
        foreach (KeeperCreatureView c in view.Creatures)
        {
            w.Write(c.Id); w.Write(c.CreatureTypeId); w.Write(c.OwnerPlayerId);
            w.Write((int)c.Activity); w.Write(c.Health); w.Write(c.Level);
            w.Write(c.LastSlapTick); w.Write(c.CanSlap);
            w.Write(c.PosX); w.Write(c.PosY); w.Write(c.InHand); w.Write(c.CarriedByCreatureId);
            w.Write(c.RescuePriorityMask);
            w.Write(c.PickupPermitted); w.Write(c.CanPickUp);
            w.Write(c.StateId); w.Write(c.ResolvedStateId); w.Write(c.MayFight); w.Write(c.MaxHealth);
            w.Write(c.LastDroppedCounter); w.Write(c.InMyHand); w.Write(c.OwnershipRank);
            w.Write(c.UnderCallToArms);
            w.Write(c.FightTargetOwner); w.Write(c.CallToArmsSiteAllowed); w.Write(c.HeartRouteExists);
            w.Write(c.SpellTargetAllowed); w.Write(c.CombatPitFighter); w.Write(c.AttachedToRoom);
            w.Write(c.HealTargetAllowed); w.Write(c.ChickenTargetAllowed); w.Write(c.InfernoPlaceAllowed);
            w.Write(c.Experience); w.Write((int)c.DroppedToDoJob);
            for (int b = 0; b < CreatureAnger.Count; b++) w.Write(c.Anger[(AngerBucket)b]);
            w.Write(c.ActivityCode); w.Write(c.SpeedChanging);
        }
        w.Write(view.Traps.Count);
        foreach (KeeperTrapView t in view.Traps)
        {
            w.Write(t.TrapTypeId); w.Write(t.OwnerPlayerId); w.Write(t.X); w.Write(t.Y); w.Write(t.Threat);
        }
        w.Write(view.AllRooms.Count);
        foreach (KeeperRoomView room in view.AllRooms)
        {
            w.Write(room.Definition.RoomId); w.Write(room.OwnerPlayerId);
            w.Write(room.MinX); w.Write(room.MinY); w.Write(room.MaxX); w.Write(room.MaxY);
            w.Write(room.TileIndices.Count);
            foreach (int index in room.TileIndices) w.Write(index);
            w.Write(room.FloorSlots.Count);
            foreach (int index in room.FloorSlots) w.Write(index);
            foreach (int index in room.ChainOrder) w.Write(index);
            w.Write(room.ObjectListHead);
        }
        w.Write(view.Messages.Count);
        foreach (PlayerMessage m in view.Messages)
        {
            w.Write(m.Id); w.Write(m.PlayerId.Value); w.Write((uint)m.Kind);
            w.Write(m.Data1); w.Write(m.Data2); w.Write(m.Age); w.Write(m.Timeout);
            w.Write(m.NeedsTab); w.Write(m.Handled);
        }
        w.Write(view.CallToArms.On); w.Write(view.CallToArms.ObjectId);
        w.Write(view.CallToArms.X); w.Write(view.CallToArms.Y); w.Write(view.CallToArms.ManaTick);
        foreach (byte status in view.StatusEntries) w.Write(status);
        foreach (bool ruined in view.HeartRuinedEntries) w.Write(ruined);
        int[] refused = view.CallRefusedTiles.ToArray();
        w.Write(refused.Length);
        foreach (int index in refused) w.Write(index);
        int[] sightRefused = view.SightRefusedTiles.ToArray();
        w.Write(sightRefused.Length);
        foreach (int index in sightRefused) w.Write(index);
        w.Write(view.HandCount);
        w.Flush();
        return stream.ToArray();
    }

    public static KeeperView FromBytes(byte[] bytes, GameDatabase database)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var r = new BinaryReader(stream, Encoding.UTF8);
        if (r.ReadUInt32() != Magic || r.ReadInt32() != FormatVersion)
            throw new InvalidDataException("Unsupported keeper observation format.");
        PlayerId player = r.ReadByte();
        var tick = new GameTick(r.ReadInt64());
        byte status = r.ReadByte();
        byte alliedWith = r.ReadByte();
        var playerIds = new PlayerId[ReadCount(r, 1)];
        for (int i = 0; i < playerIds.Length; i++) playerIds[i] = r.ReadByte();
        int width = r.ReadInt32(), height = r.ReadInt32();
        if (width <= 0 || height <= 0 || (long)width * height > bytes.Length / 5)
            throw new InvalidDataException("Invalid observation dimensions.");
        int gold = r.ReadInt32(), imps = r.ReadInt32();
        int mana = r.ReadInt32();
        TilePos? heart = r.ReadBoolean() ? new TilePos(r.ReadInt32(), r.ReadInt32()) : null;
        var tiles = new KeeperTileView[width * height];
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = new KeeperTileView(r.ReadByte(), r.ReadByte(), new PlayerMask(r.ReadUInt16()), r.ReadByte());
        var availability = new AvailabilityState[ReadCount(r, 19)];
        for (int i = 0; i < availability.Length; i++)
            availability[i] = new AvailabilityState((AvailabilityKind)r.ReadUInt16(), r.ReadInt32(), r.ReadInt32(), r.ReadBoolean(),
                r.ReadInt64());
        var creatures = new KeeperCreatureView[ReadCount(r, 109)];
        for (int i = 0; i < creatures.Length; i++)
        {
            creatures[i] = new KeeperCreatureView(r.ReadInt32(), r.ReadByte(), r.ReadByte(),
                (CreatureActivity)r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt64(), r.ReadBoolean(),
                r.ReadInt32(), r.ReadInt32(), r.ReadBoolean(), r.ReadInt32(), r.ReadUInt16(),
                r.ReadBoolean(), r.ReadBoolean(),
                r.ReadInt32(), r.ReadInt32(), r.ReadBoolean(), r.ReadInt32(),
                r.ReadUInt16(), r.ReadBoolean(), r.ReadInt32(), r.ReadBoolean(),
                r.ReadByte(), r.ReadBoolean(), r.ReadBoolean(), r.ReadBoolean(), r.ReadBoolean(), r.ReadBoolean(),
                r.ReadBoolean(), r.ReadBoolean(), r.ReadBoolean(),
                r.ReadInt32(), (JobType)r.ReadInt32());
            var anger = new CreatureAnger();
            for (int b = 0; b < CreatureAnger.Count; b++) anger[(AngerBucket)b] = r.ReadInt32();
            creatures[i] = creatures[i] with { Anger = anger, ActivityCode = r.ReadInt32(), SpeedChanging = r.ReadBoolean() };
        }
        var traps = new KeeperTrapView[ReadCount(r, 12)];
        for (int i = 0; i < traps.Length; i++)
            traps[i] = new KeeperTrapView(r.ReadByte(), r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadUInt16());
        var rooms = new KeeperRoomView[ReadCount(r, 30)];
        for (int i = 0; i < rooms.Length; i++)
        {
            RoomDef definition = database.RoomById(r.ReadByte());
            PlayerId owner = r.ReadByte();
            int minX = r.ReadInt32(), minY = r.ReadInt32(), maxX = r.ReadInt32(), maxY = r.ReadInt32();
            var indices = new int[ReadCount(r, 4)];
            for (int j = 0; j < indices.Length; j++)
            {
                indices[j] = r.ReadInt32();
                if ((uint)indices[j] >= tiles.Length)
                    throw new InvalidDataException("Room tile is outside the observation.");
            }
            var slots = new int[ReadCount(r, 4)];
            for (int j = 0; j < slots.Length; j++)
            {
                slots[j] = r.ReadInt32();
                if (Array.IndexOf(indices, slots[j]) < 0)
                    throw new InvalidDataException("Floor slot is not a tile of its room.");
            }
            var chain = new int[indices.Length];
            for (int j = 0; j < chain.Length; j++) chain[j] = r.ReadInt32();
            if (!new HashSet<int>(chain).SetEquals(indices) || chain.Distinct().Count() != chain.Length)
                throw new InvalidDataException("A room's chain order is not its tiles.");
            rooms[i] = new KeeperRoomView(definition, owner, indices, minX, minY, maxX, maxY, slots, chain, r.ReadInt32());
        }
        var messages = new PlayerMessage[ReadCount(r, 27)];
        for (int i = 0; i < messages.Length; i++)
            messages[i] = new PlayerMessage(r.ReadUInt32(), r.ReadByte(),
                (PlayerMessageKind)r.ReadUInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadUInt32(), r.ReadUInt32(), r.ReadBoolean(), r.ReadBoolean());
        var callToArms = new CallToArmsState(r.ReadBoolean(), r.ReadInt32(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt32());
        var statuses = new byte[8];
        for (int i = 0; i < statuses.Length; i++) statuses[i] = r.ReadByte();
        var heartRuined = new bool[8];
        for (int i = 0; i < heartRuined.Length; i++) heartRuined[i] = r.ReadBoolean();
        var refused = new int[ReadCount(r, 4)];
        for (int i = 0; i < refused.Length; i++)
        {
            refused[i] = r.ReadInt32();
            if ((uint)refused[i] >= tiles.Length)
                throw new InvalidDataException("Refused tile is outside the observation.");
        }
        var sightRefused = new int[ReadCount(r, 4)];
        for (int i = 0; i < sightRefused.Length; i++)
        {
            sightRefused[i] = r.ReadInt32();
            if ((uint)sightRefused[i] >= tiles.Length)
                throw new InvalidDataException("Refused tile is outside the observation.");
        }
        int handCount = r.ReadInt32();
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing keeper observation data.");
        return new KeeperView(database, player, tick, status, alliedWith, playerIds, width, height, gold, imps,
            heart, tiles, availability, creatures, traps, rooms, messages, callToArms, mana, statuses, refused, heartRuined,
            sightRefused, handCount);
    }

    private static int ReadCount(BinaryReader r, int minimumEntryBytes)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > (r.BaseStream.Length - r.BaseStream.Position) / minimumEntryBytes)
            throw new InvalidDataException("Invalid observation entry count.");
        return count;
    }
}
