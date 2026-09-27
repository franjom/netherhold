// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;

namespace DK2.Tools;

/// <summary>Maps original entity slots to simulation entities at a turn. DK2
/// can reuse a slot after its first occupant dies.</summary>
public sealed class PacketEntityMap
{
    private readonly Dictionary<uint, SortedList<long, int>> _rows = [];

    public void Add(uint original, long fromTurn, int ours)
    {
        if (fromTurn < 1 || ours < 1)
            throw new InvalidDataException("Entity map turns and IDs must be positive.");
        if (!_rows.TryGetValue(original, out SortedList<long, int>? spans))
            _rows[original] = spans = [];
        if (spans.ContainsKey(fromTurn))
            throw new InvalidDataException(
                $"Original entity {original} has two mappings from turn {fromTurn}.");
        spans.Add(fromTurn, ours);
    }

    public bool TryResolve(uint original, long turn, out int ours)
    {
        ours = 0;
        if (!_rows.TryGetValue(original, out SortedList<long, int>? spans)) return false;
        for (int i = spans.Count - 1; i >= 0; i--)
            if (spans.Keys[i] <= turn)
            {
                ours = spans.Values[i];
                return true;
            }
        return false;
    }

    public static PacketEntityMap Read(string? path)
    {
        var map = new PacketEntityMap();
        if (path is null) return map;
        using var reader = File.OpenText(path);
        if (reader.ReadLine() != "original,from_turn,ours")
            throw new InvalidDataException($"{path}: expected original,from_turn,ours header.");
        string? line;
        for (int number = 2; (line = reader.ReadLine()) is not null; number++)
        {
            if (line.Length == 0) continue;
            string[] fields = line.Split(',');
            if (fields.Length != 3 ||
                !uint.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture,
                    out uint original) ||
                !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture,
                    out long from) ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture,
                    out int ours))
                throw new InvalidDataException($"{path}:{number}: invalid entity mapping.");
            map.Add(original, from, ours);
        }
        return map;
    }
}
