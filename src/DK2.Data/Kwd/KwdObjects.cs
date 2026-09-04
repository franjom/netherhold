// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Parser for OBJECTS(240) records: 145 objects over 129,630 bytes, 894 each.
///
/// <para><b>The record that four half-built mechanics were waiting for.</b>
/// Chickens, corpses, gold piles and beds are all objects, and each of those
/// gaps has been recorded with "OBJECTS is unparsed (M6)" as its cause. This
/// reads the gameplay tail of the record and skips the art entirely.</para>
///
/// <para><b>Only the last 82 bytes matter.</b> The first 812 are nine art
/// resources and a light — meshes, icons, in-hand art — and none of it is a
/// rule. So this parser does not model an art resource at all, which is why it
/// is thirty lines rather than three hundred, and why nothing about DK2's art
/// pipeline has to be understood before a creature can sleep in a bed.</para>
///
/// <para><b>How the offsets were arrived at, and why they are believed.</b>
/// OpenKeeper's <c>readObjects</c> gives the field <i>order</i> (ADR-0003's
/// second amendment). The offsets are not copied from it — they are derived and
/// then checked, by the rule this project uses for any record without a
/// template:</para>
///
/// <list type="bullet">
/// <item><b>The arithmetic closes.</b> OpenKeeper names one unknown field
/// <c>x34c</c> after its offset. Laying the fields it lists after that one in
/// order — <c>u16 x34c, manaValue, tooltipStringId, nameStringId, slapEffectId,
/// deathEffectId, miscEffectId</c>, then <c>u8 objectId, startState,
/// roomCapacity, pickUpPriority</c>, then a 32-byte sound category — ends at
/// <b>0x37E</b>, which is <b>894</b>: the record stride, measured independently
/// from the chunk header long before any field list was consulted. A field list
/// that lands exactly on an independently measured stride cannot have a wrong
/// width in it that another wrong width cancels out.</item>
/// <item><b>The id column proves itself.</b> <see cref="ObjectDef.ObjectId"/>
/// at 0x35A reads <b>1 to 145, each exactly once</b> — the strongest possible
/// check on a stride and an offset at the same time, because a wrong stride
/// would scramble it and a wrong offset would not produce a permutation.</item>
/// <item><b>The names read.</b> 0x00 is a 32-byte name, as it is in every other
/// record type, and the column comes back as sensible English — and includes
/// the names an earlier session had already seen while scanning this file for
/// something else (Q63: Training Dummy, Gold Pile 1, Book Case, Chicken,
/// Chicken Coop, Egg, Prison Door).</item>
/// <item><b>The anchors land.</b> Object 22 is the Goblin's bed and 26 the
/// Vampire's, which the creature records name; 31 to 34 are the Graveyard's,
/// which the room record names. All of them carry <c>TypeLair</c> or belong to
/// the room that claims them. <c>ObjectsOnRealDataTests</c> asserts this by
/// name.</item>
/// </list>
///
/// <para><b>What is deliberately not read:</b> the nine art resources, the
/// light, <c>airFriction</c>, <c>material</c>, the three unknown bytes at 0x341,
/// <c>maxAngle</c>, the three effect ids and the sound category. Every one of
/// them is presentation or unexplained, and a field read here without a rule to
/// use it would be an invention nobody notices.</para>
/// </summary>
public static class KwdObjects
{
    private const int RecordSize = 894;

    // The gameplay tail. Everything before 0x32C is art.
    private const int OffsetName = 0x000;
    private const int OffsetWidth = 0x32C;
    private const int OffsetHeight = 0x330;
    private const int OffsetMass = 0x334;
    private const int OffsetSpeed = 0x338;
    private const int OffsetFlags = 0x344;
    private const int OffsetHp = 0x348;
    private const int OffsetManaValue = 0x34E;
    private const int OffsetTooltipStringId = 0x350;
    private const int OffsetNameStringId = 0x352;
    private const int OffsetObjectId = 0x35A;
    private const int OffsetStartState = 0x35B;
    private const int OffsetRoomCapacity = 0x35C;
    private const int OffsetPickUpPriority = 0x35D;

    public static List<ObjectDef> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<ObjectDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Objects) continue;

            int recordSize = chunk.ItemCount == 0 ? RecordSize : chunk.Content.Length / chunk.ItemCount;
            if (recordSize != RecordSize)
                throw new InvalidDataException(
                    $"OBJECTS record size {recordSize} is not {RecordSize}.");
            if (chunk.Content.Length != recordSize * chunk.ItemCount)
                throw new InvalidDataException("OBJECTS content size does not divide into records.");

            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(ParseRecord(chunk.Content, i * recordSize));
        }
        return result;
    }

    private static ObjectDef ParseRecord(byte[] rec, int b)
    {
        Fp32 Fp(int off) => new(BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + off)));
        ushort U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + off));

        return new ObjectDef
        {
            Name = Name(rec, b + OffsetName),
            ObjectId = rec[b + OffsetObjectId],
            Flags = (ObjectFlags)BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(b + OffsetFlags)),

            Hp = U16(OffsetHp),
            ManaValue = U16(OffsetManaValue),
            RoomCapacity = rec[b + OffsetRoomCapacity],
            StartState = rec[b + OffsetStartState],
            PickUpPriority = rec[b + OffsetPickUpPriority],

            Width = Fp(OffsetWidth),
            Height = Fp(OffsetHeight),
            Mass = Fp(OffsetMass),
            Speed = Fp(OffsetSpeed),

            NameStringId = U16(OffsetNameStringId),
            TooltipStringId = U16(OffsetTooltipStringId),
        };
    }

    private static string Name(byte[] rec, int at)
    {
        ReadOnlySpan<byte> s = rec.AsSpan(at, 32);
        int end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]).Trim();
    }
}
