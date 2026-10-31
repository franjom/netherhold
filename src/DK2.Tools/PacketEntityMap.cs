// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;

namespace DK2.Tools;

/// <summary>What an original slot maps to. <see cref="GoldTile"/> maps a floor gold pile
/// to the tile index it lay on, since piles are interchangeable and our mining makes its
/// own; the replay picks up whichever of our piles is on that tile.</summary>
public enum PacketEntityKind { Creature, Object, GoldTile }

/// <summary>Maps original entity slots to simulation entities at a turn. DK2
/// can reuse a slot after its first occupant dies.</summary>
public sealed class PacketEntityMap
{
    private readonly Dictionary<uint, SortedList<long, (PacketEntityKind Kind, int Id)>> _rows = [];

    public void Add(uint original, long fromTurn, int ours, PacketEntityKind kind)
    {
        if (fromTurn < 1 || ours < 1)
            throw new InvalidDataException("Entity map turns and IDs must be positive.");
        if (!_rows.TryGetValue(original, out SortedList<long, (PacketEntityKind Kind, int Id)>? spans))
            _rows[original] = spans = [];
        if (spans.ContainsKey(fromTurn))
            throw new InvalidDataException(
                $"Original entity {original} has two mappings from turn {fromTurn}.");
        spans.Add(fromTurn, (kind, ours));
    }

    /// <summary><see cref="Add"/>, replacing a row that starts on the same turn: a resync's own
    /// pairing (<c>WorldResync</c>) over the map's.</summary>
    public void Set(uint original, long fromTurn, int ours, PacketEntityKind kind)
    {
        if (_rows.TryGetValue(original, out SortedList<long, (PacketEntityKind Kind, int Id)>? spans))
            spans.Remove(fromTurn);
        Add(original, fromTurn, ours, kind);
    }

    /// <summary>Our id for a slot that names no creature of ours: no creature has it, so a pickup
    /// of that slot lifts nobody, as DK2's pickup of an empty slot does.</summary>
    public const int NoCreature = int.MaxValue;

    /// <summary>Replaces creature rows starting after <paramref name="turn"/> with
    /// <see cref="NoCreature"/> from the next turn: after a resync those rows pair slots with the
    /// ids of the run the map was recorded from, not this one (<see cref="NativeBirths"/>).</summary>
    public void DropCreatureRowsAfter(long turn)
    {
        foreach (SortedList<long, (PacketEntityKind Kind, int Id)> spans in _rows.Values)
        {
            bool dropped = false;
            for (int i = spans.Count - 1; i >= 0 && spans.Keys[i] > turn; i--)
                if (spans.Values[i].Kind == PacketEntityKind.Creature)
                {
                    spans.RemoveAt(i);
                    dropped = true;
                }
            if (dropped && !spans.ContainsKey(turn + 1)) spans.Add(turn + 1, (PacketEntityKind.Creature, NoCreature));
        }
    }

    public bool TryResolve(uint original, long turn, PacketEntityKind kind, out int ours)
    {
        ours = 0;
        if (!_rows.TryGetValue(original, out SortedList<long, (PacketEntityKind Kind, int Id)>? spans)) return false;
        for (int i = spans.Count - 1; i >= 0; i--)
            if (spans.Keys[i] <= turn)
            {
                if (spans.Values[i].Kind != kind) return false;
                ours = spans.Values[i].Id;
                return true;
            }
        return false;
    }

    public static PacketEntityMap Read(string? path)
    {
        var map = new PacketEntityMap();
        if (path is null) return map;
        using var reader = File.OpenText(path);
        if (reader.ReadLine() != "original,from_turn,ours,kind")
            throw new InvalidDataException($"{path}: expected original,from_turn,ours,kind header.");
        string? line;
        for (int number = 2; (line = reader.ReadLine()) is not null; number++)
        {
            if (line.Length == 0) continue;
            string[] fields = line.Split(',');
            if (fields.Length != 4 ||
                !uint.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture,
                    out uint original) ||
                !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture,
                    out long from) ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int ours) ||
                !Enum.TryParse(fields[3], ignoreCase: true, out PacketEntityKind kind) ||
                !Enum.IsDefined(kind))
                throw new InvalidDataException($"{path}:{number}: invalid entity mapping.");
            map.Add(original, from, ours, kind);
        }
        return map;
    }
}
