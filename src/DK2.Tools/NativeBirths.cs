// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Simulation;

namespace DK2.Tools;

/// <summary>The creatures a native run gave a slot after a resync, read from its capture's
/// <c>creature-map.csv</c>, paired with ours as they are born. The packets after a resync name
/// creatures by DK2 slot; a slot first filled after the resync has no pairing in an entity map
/// recorded from another run, so without this a pickup lifts whoever that map's id is in ours,
/// or nobody.</summary>
/// <remarks>A birth is a slot gaining a creature type after being empty, <c>obj</c> (dead) or
/// another type. An owner change alone is a conversion of the same creature, not a birth. A
/// capture header holds the preceding completed turn (ADR-0119), so a birth first seen in
/// header <c>T</c> happened on turn <c>T - 1</c>. The resync's state is header <c>R</c>, applied
/// after our turn <c>R</c>: a birth in header <c>R + 1</c> is on turn <c>R</c> and not in it, so it
/// counts, and its row starts on <c>R + 1</c>, where the resync's own pairs start. A slot emptied
/// and refilled with the same type and owner within one turn is not seen.</remarks>
public sealed class NativeBirths
{
    /// <summary>How far apart, in turns, a native birth and ours may be and still pair.</summary>
    public const int Window = 100;

    private readonly List<Birth> _births;
    private readonly bool[] _paired;
    private readonly long _afterTurn;
    private HashSet<int>? _known;

    private NativeBirths(List<Birth> births, long afterTurn)
    {
        _births = births;
        _paired = new bool[births.Count];
        _afterTurn = afterTurn;
    }

    public readonly record struct Birth(uint Slot, long Turn, byte Type, byte Owner);

    public IReadOnlyList<Birth> Births => _births;
    public int PairedCount { get; private set; }

    /// <summary>Births seen in headers after <paramref name="afterTurn"/>, the resync's, in turn then
    /// slot order.</summary>
    public static NativeBirths Read(string path, long afterTurn)
    {
        using var reader = File.OpenText(path);
        if (reader.ReadLine() != "tick,id,type,owner,x,y,health,state,target")
            throw new InvalidDataException($"{path}: expected a capture creature-map.csv header.");
        var last = new Dictionary<uint, (string Type, string Owner)>();
        var births = new List<Birth>();
        string? line;
        for (int number = 2; (line = reader.ReadLine()) is not null; number++)
        {
            if (line.Length == 0) continue;
            string[] f = line.Split(',');
            if (f.Length != 9
                || !long.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out long tick)
                || !uint.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint slot))
                throw new InvalidDataException($"{path}:{number}: invalid creature-map row.");
            bool had = last.TryGetValue(slot, out (string Type, string Owner) was);
            last[slot] = (f[2], f[3]);
            if (!byte.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out byte type)
                || !byte.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out byte owner))
                continue;
            if (had && was.Type == f[2]) continue;
            if (tick > afterTurn) births.Add(new Birth(slot, tick - 1, type, owner));
        }
        births.Sort((a, b) => a.Turn != b.Turn ? a.Turn.CompareTo(b.Turn) : a.Slot.CompareTo(b.Slot));
        return new NativeBirths(births, afterTurn);
    }

    /// <summary>Every birth's slot names nobody from its turn until one of ours pairs with it.</summary>
    public void Reserve(PacketEntityMap map)
    {
        foreach (Birth birth in _births) map.Set(birth.Slot, From(birth), PacketEntityMap.NoCreature, PacketEntityKind.Creature);
    }

    /// <summary>At the resync: <see cref="Reserve"/>, and the creatures ours has then are not births.</summary>
    public void Begin(PacketEntityMap map, IEnumerable<CreatureState> creatures)
    {
        Reserve(map);
        _known = [.. creatures.Select(c => c.Id)];
    }

    /// <summary>After each turn from the resync: pairs every creature of ours not seen before
    /// (<see cref="TryPair"/>), with a <c>birth_paired</c> or <c>birth_unpaired</c> event when
    /// <paramref name="events"/> is given.</summary>
    public void PairNew(PacketEntityMap map, long turn, IEnumerable<CreatureState> creatures, TextWriter? events)
    {
        if (_known is null) return;
        foreach (CreatureState creature in creatures)
        {
            if (!_known.Add(creature.Id)) continue;
            bool paired = TryPair(map, turn, creature.Id, creature.CreatureTypeId, creature.OwnerPlayerId, out Birth native);
            events?.WriteLine(paired
                ? $"{turn}	replay	birth_paired	creature={creature.Id} type={creature.CreatureTypeId} slot={native.Slot} native_turn={native.Turn}"
                : $"{turn}	replay	birth_unpaired	creature={creature.Id} type={creature.CreatureTypeId} owner={creature.OwnerPlayerId}");
        }
    }

    /// <summary>Pairs a creature of ours born on <paramref name="turn"/> with the nearest unpaired
    /// native birth of its type and owner within <see cref="Window"/> turns (the earlier slot on a
    /// tie), and maps that slot to it from the native turn.</summary>
    public bool TryPair(PacketEntityMap map, long turn, int ours, byte type, byte owner, out Birth native)
    {
        native = default;
        int best = -1;
        long bestGap = long.MaxValue;
        for (int i = 0; i < _births.Count; i++)
        {
            Birth b = _births[i];
            if (_paired[i] || b.Type != type || b.Owner != owner) continue;
            long gap = Math.Abs(b.Turn - turn);
            if (gap > Window || gap >= bestGap) continue;
            best = i;
            bestGap = gap;
        }
        if (best < 0) return false;
        native = _births[best];
        _paired[best] = true;
        PairedCount++;
        map.Set(native.Slot, From(native), ours, PacketEntityKind.Creature);
        return true;
    }

    private long From(Birth birth) => Math.Max(birth.Turn, _afterTurn + 1);
}
