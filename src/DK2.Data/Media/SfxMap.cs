// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Media;

public readonly record struct SoundPriority(uint Value);

public readonly record struct SoundId(uint Value);
/// <summary>One-based sample/archive ids, cumulative selection weight and authored duration.</summary>
public sealed record SoundSample(uint Index, SoundArchiveId Archive, uint CumulativeWeight, uint DurationMilliseconds);
/// <summary>One-based target and inclusive intensity range; the word at+4 remains uninterpreted.</summary>
public sealed record SoundLink(uint Target, ushort Unknown0x04, byte Minimum, byte Maximum);
/// <summary>Raw bytes+8..41 retain volume/pitch/delay fields and unknown item fields without inventing meanings.</summary>
public sealed record SoundItem(IReadOnlyList<SoundSample> Sounds, IReadOnlyList<SoundLink> Links, byte[] Unknown0x08);
/// <summary>Raw bytes+8..19 retain request priority, flags and parameter ids; the reader preserves their exact bytes.</summary>
public sealed record SoundGroup(SoundId Id, IReadOnlyList<SoundItem> Items, byte[] Unknown0x08);
/// <summary>Raw bytes+4..23 retain the event metadata; runtime UI resolution uses only its groups.</summary>
public sealed record SoundEvent(IReadOnlyList<SoundGroup> Groups, byte[] Unknown0x04);

/// <summary>Sound events, groups, items, samples and links; front-end-assets.md section 5.3 (STRONGLY INFERRED).</summary>
public sealed class SfxMap
{
    private SfxMap(IReadOnlyList<SoundEvent> events)
    {
        Events = events;
        Groups = events.SelectMany(e => e.Groups).ToArray();
    }
    public IReadOnlyList<SoundEvent> Events { get; }
    public IReadOnlyList<SoundGroup> Groups { get; }

    public static SfxMap Read(ReadOnlySpan<byte> data)
    {
        using var reader = SoundMapReader.Open(data, 0xE9612C00, 0xB00009B4);
        int count = SoundMapReader.Count(reader, 24);
        var eventCounts = new int[count];
        var eventUnknown = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            eventCounts[i] = SoundMapReader.Count(reader, 20);
            eventUnknown[i] = SoundMapReader.Bytes(reader, 20);
        }
        var groupHeaders = new List<(SoundId Id, int Count, byte[] Unknown)>();
        foreach (int groupCount in eventCounts)
            for (int i = 0; i < groupCount; i++)
            {
                SoundMapReader.Require(reader, 20);
                var id = new SoundId(reader.ReadUInt32());
                int items = SoundMapReader.Count(reader, 42);
                groupHeaders.Add((id, items, SoundMapReader.Bytes(reader, 12)));
            }
        var groups = new List<SoundGroup>();
        foreach (var header in groupHeaders)
        {
            var itemHeaders = new (int Sounds, int Links, byte[] Unknown)[header.Count];
            for (int i = 0; i < header.Count; i++)
            {
                int sounds = SoundMapReader.Count(reader, 16);
                int links = SoundMapReader.Count(reader, 8);
                itemHeaders[i] = (sounds, links, SoundMapReader.Bytes(reader, 34));
            }
            var items = new SoundItem[header.Count];
            for (int i = 0; i < items.Length; i++)
            {
                var sounds = new SoundSample[itemHeaders[i].Sounds];
                for (int j = 0; j < sounds.Length; j++)
                {
                    SoundMapReader.Require(reader, 16);
                    uint index = reader.ReadUInt32();
                    uint unknown4 = reader.ReadUInt32(), unknown8 = reader.ReadUInt32();
                    sounds[j] = new SoundSample(index, new SoundArchiveId(checked((int)reader.ReadUInt32())), unknown4, unknown8);
                }
                var links = new SoundLink[itemHeaders[i].Links];
                for (int j = 0; j < links.Length; j++)
                {
                    SoundMapReader.Require(reader, 8);
                    links[j] = new SoundLink(reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadByte(), reader.ReadByte());
                }
                items[i] = new SoundItem(sounds, links, itemHeaders[i].Unknown);
            }
            groups.Add(new SoundGroup(header.Id, items, header.Unknown));
        }
        var events = new SoundEvent[count];
        int cursor = 0;
        for (int i = 0; i < events.Length; i++)
        {
            events[i] = new SoundEvent(groups.Skip(cursor).Take(eventCounts[i]).ToArray(), eventUnknown[i]);
            cursor += eventCounts[i];
        }
        SoundMapReader.End(reader);
        return new SfxMap(events);
    }
}
