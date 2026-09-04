// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Walks the two KWD record types that are not a fixed stride: THINGS and
/// TRIGGERS.
///
/// <para><b>CONFIRMED</b> (2026-09-04): both are self-describing. Each record is
/// <c>u32 tag; u32 payloadSize; u8[payloadSize]</c>, so the whole chunk can be
/// split into records <b>without knowing a single payload layout</b>. That is
/// the fact that de-risks these two types: every other record type had to have
/// its stride derived before anything could be read, and these two need
/// nothing.</para>
///
/// <para><b>Validated structurally, not on trust</b> — the same bar every other
/// offset in this project had to clear. Across every tagged chunk the install
/// ships (133 of them: two per campaign level plus the camera chunk in
/// <c>GlobalVariables.kwd</c>, holding 6,318 THINGS and 9,161 TRIGGERS records),
/// walking <c>itemCount</c> records this way consumes each chunk <b>to the
/// byte</b>, with none left over and none short. See
/// <c>docs/research/kwd-container.md</c> §"THINGS and TRIGGERS are
/// self-describing".</para>
///
/// <para>And the payloads turn out to be fixed-size <i>per tag</i>: across the
/// whole campaign every tag carries exactly one payload size (see
/// <see cref="ThingPayloadSizes"/>). So THINGS is not really a variable-length
/// format at all — it is eleven fixed-size record types multiplexed into one
/// chunk, which is why a stride check could never have found it. (One chunk does
/// have a whole stride, and it is a trap for exactly this reason:
/// <c>GlobalVariables.kwd</c>'s THINGS chunk is 39 cameras of 92 bytes each, so
/// it divides perfectly and still is not a fixed-stride record type.)</para>
///
/// <para>This type deliberately stops at the tag and the payload bytes. Reading
/// a payload's <i>fields</i> is M6 work and belongs with the mechanic that needs
/// them.</para>
/// </summary>
public static class KwdTaggedRecords
{
    /// <summary>Bytes of header ahead of every payload: the tag and its size.</summary>
    public const int RecordHeaderSize = 8;

    /// <summary>One record of a THINGS or TRIGGERS chunk, undecoded.</summary>
    public readonly record struct TaggedRecord(int Tag, ReadOnlyMemory<byte> Payload);

    /// <summary>True for the two record types this walker understands.</summary>
    public static bool IsTagged(KwdRecordType type) =>
        type is KwdRecordType.Things or KwdRecordType.Triggers;

    /// <summary>
    /// Splits a THINGS or TRIGGERS chunk into its records.
    /// Throws when the walk does not consume the chunk exactly, because a
    /// self-describing format that does not add up means the chunk was
    /// misidentified — a silent partial read would be worse than a failure.
    /// </summary>
    public static List<TaggedRecord> Split(KwdChunk chunk)
    {
        if (!IsTagged(chunk.Type))
            throw new ArgumentException(
                $"{chunk.Type} is a fixed-stride record type, not a tagged one.", nameof(chunk));

        byte[] content = chunk.Content;
        var records = new List<TaggedRecord>(chunk.ItemCount);
        int at = 0;

        for (int i = 0; i < chunk.ItemCount; i++)
        {
            if (at + RecordHeaderSize > content.Length)
                throw new InvalidDataException(
                    $"{chunk.Type}: record {i} of {chunk.ItemCount} starts at {at}, " +
                    $"past the end of {content.Length} content bytes.");

            int tag = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(at));
            int size = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(at + 4));
            at += RecordHeaderSize;

            if (size < 0 || at + size > content.Length)
                throw new InvalidDataException(
                    $"{chunk.Type}: record {i} (tag {tag}) declares {size} payload bytes " +
                    $"but only {content.Length - at} remain.");

            records.Add(new TaggedRecord(tag, content.AsMemory(at, size)));
            at += size;
        }

        if (at != content.Length)
            throw new InvalidDataException(
                $"{chunk.Type}: {chunk.ItemCount} records consumed {at} of " +
                $"{content.Length} content bytes. A self-describing format that " +
                "does not add up means the chunk header was read wrongly.");

        return records;
    }

    /// <summary>
    /// The THINGS tags, and the payload size each one carries.
    ///
    /// <para>The tag values and names are OpenKeeper's (GPLv3, facts only —
    /// <c>docs/research/external-resources.md</c>); the <b>sizes are this
    /// project's own measurement</b> over the shipped data, and the two agree on
    /// all eleven tags that occur, including the Hero Party's peculiar 555.
    /// <c>InShippedData</c> is false for tag 202 alone: a dead body is a thing
    /// OpenKeeper parses that no shipped file places, so its 16 bytes rest on
    /// their field list and nothing else. The camera (205) very nearly joined
    /// it — it appears in no campaign map, and turned up only because the
    /// sweep was widened to the editor database.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<int, (string Name, int PayloadSize, bool InShippedData)>
        ThingPayloadSizes = new Dictionary<int, (string, int, bool)>
        {
            [194] = ("Object", 24, true),
            [195] = ("Trap", 16, true),
            [196] = ("Door", 20, true),
            [197] = ("ActionPoint", 56, true),
            [198] = ("NeutralCreature", 24, true),
            [199] = ("GoodCreature", 32, true),
            [200] = ("KeeperCreature", 28, true),
            [201] = ("HeroParty", 555, true),
            [202] = ("DeadBody", 16, false),
            [203] = ("EffectGenerator", 36, true),
            [204] = ("Room", 20, true),
            [205] = ("Camera", 84, true),
        };

    /// <summary>
    /// The TRIGGERS tags. Both carry a 16-byte payload in every shipped map.
    /// A generic trigger's payload is a small tagged union of its own (the
    /// target type picks the field layout inside those 16 bytes), which is why
    /// the size alone does not describe it.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, (string Name, int PayloadSize)>
        TriggerPayloadSizes = new Dictionary<int, (string, int)>
        {
            [213] = ("Generic", 16),
            [214] = ("Action", 16),
        };
}
