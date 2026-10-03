// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

public static partial class KwdTriggers
{
    private readonly record struct ParameterField(int Offset, int Width, bool Signed);

    private static IReadOnlyDictionary<string,long> ReadParameters(IReadOnlyDictionary<string,ParameterField> layout, byte[] bytes) =>
        layout.ToDictionary(p=>p.Key,p=>ReadParameter(bytes,p.Value));

    private static long ReadParameter(byte[] bytes, ParameterField field) => field.Width switch
    {
        1 => bytes[field.Offset],
        2 when field.Signed => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(field.Offset)),
        2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(field.Offset)),
        4 when field.Signed => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(field.Offset)),
        4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(field.Offset)),
        _ => throw new InvalidDataException("Unsupported trigger parameter width."),
    };

    public static byte[] WriteRecord(TriggerRecord row, KwdRecordImage template)
    {
        if (row.Tag is not (213 or 214)) throw new InvalidDataException("Unknown trigger tag.");
        byte[] image=KwdOverlay.TaggedImage(template,KwdRecordType.Triggers,row.Tag,16);
        byte[] source=image.AsSpan(8,8).ToArray();
        if (row.Type!=image[22]) throw new InvalidDataException("Trigger type changes need an independently validated payload template.");
        if (row.Body.Length!=8) throw new InvalidDataException("Trigger bodies require eight explicit bytes.");
        var layout=row.Tag==213 ? GenericParameterLayout(row.Type) : ActionParameterLayout(row.Type);
        if(row.Parameters.Count!=layout.Count || row.Parameters.Keys.Any(k=>!layout.ContainsKey(k)))
            throw new InvalidDataException("Trigger parameters must match the existing type's decoded schema.");
        byte?[] requested=new byte?[8];
        void Merge(int at, ReadOnlySpan<byte> value)
        {
            for(int i=0;i<value.Length;i++)
            {
                if(requested[at+i] is byte previous && previous!=value[i])
                    throw new InvalidDataException("Conflicting edits to overlapping trigger body views.");
                requested[at+i]=value[i];
            }
        }
        void Scalar(int at,int width,bool signed,long value)
        {
            var field=new ParameterField(at,width,signed);
            if(value==ReadParameter(source,field)) return;
            long max=width==1 ? byte.MaxValue : width==2 ? signed ? short.MaxValue : ushort.MaxValue : signed ? int.MaxValue : uint.MaxValue;
            long min=signed ? width==2 ? short.MinValue : int.MinValue : 0;
            if(value<min || value>max) throw new InvalidDataException("Trigger parameter does not fit its binary field.");
            byte[] bytes=new byte[width];
            if(width==1) bytes[0]=(byte)value;
            else if(width==2 && signed) BinaryPrimitives.WriteInt16LittleEndian(bytes,(short)value);
            else if(width==2) BinaryPrimitives.WriteUInt16LittleEndian(bytes,(ushort)value);
            else if(signed) BinaryPrimitives.WriteInt32LittleEndian(bytes,(int)value);
            else BinaryPrimitives.WriteUInt32LittleEndian(bytes,(uint)value);
            Merge(at,bytes);
        }
        if (!row.Body.Span.SequenceEqual(source)) Merge(0,row.Body.Span);
        switch(row)
        {
            case GenericTriggerRecord c when row.Tag==213: Scalar(4,4,false,c.BodyValue); break;
            case ActionTriggerRecord c when row.Tag==214:
                Scalar(4,2,false,c.BodyWord4); Scalar(6,2,false,c.BodyWord6);
                Scalar(0,4,false,c.BodyDword0); Scalar(4,4,false,c.BodyDword4); break;
            default: throw new InvalidDataException("Trigger record class does not match its tag.");
        }
        foreach(var entry in layout)
            Scalar(entry.Value.Offset,entry.Value.Width,entry.Value.Signed,row.Parameters[entry.Key]);
        for(int i=0;i<8;i++) if(requested[i] is byte value) image[i+8]=value;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(16),row.TriggerId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(18),row.NextTriggerId);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(20),row.ChildTriggerId);
        image[23]=row.RepeatCount;
        return image;
    }
}
