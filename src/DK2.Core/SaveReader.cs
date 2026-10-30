// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;

namespace DK2.Core;

/// <summary>Allocation bounds for positional saves. Limits come from the encoding or owning collection.</summary>
public static class SaveReader
{
    public static long Remaining(BinaryReader reader)
    {
        if (!reader.BaseStream.CanSeek)
            throw new SaveIncompatibleException(SaveIncompatibleReason.Corrupt, "A save requires a seekable stream.");
        return reader.BaseStream.Length - reader.BaseStream.Position;
    }

    public static int Count(BinaryReader reader, string collection, int minBytesPerElement, int maximum = int.MaxValue)
        => CheckCount(reader, reader.ReadInt32(), collection, minBytesPerElement, maximum);

    public static int CheckCount(BinaryReader reader, int count, string collection,
        int minBytesPerElement, int maximum = int.MaxValue)
    {
        if (count < 0 || count > maximum)
            throw Error(reader, SaveIncompatibleReason.OutOfRange, $"Invalid {collection} count {count}");
        if ((long)count * minBytesPerElement > Remaining(reader))
            throw Error(reader, SaveIncompatibleReason.Truncated, $"Not enough bytes for {collection} count {count}");
        return count;
    }

    public static byte[] Bytes(BinaryReader reader, int length, string collection)
    {
        CheckCount(reader, length, collection, 1);
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw Error(reader, SaveIncompatibleReason.Truncated, $"Truncated {collection}");
        return bytes;
    }

    public static string String(BinaryReader reader)
    {
        uint length = 0;
        for (int shift = 0; shift <= 28; shift += 7)
        {
            byte part = reader.ReadByte();
            if (shift == 28 && part > 7)
                throw Error(reader, SaveIncompatibleReason.OutOfRange, "Invalid string byte length");
            length |= (uint)(part & 0x7F) << shift;
            if ((part & 0x80) == 0)
                return new UTF8Encoding(false, true).GetString(Bytes(reader, (int)length, "string"));
        }
        throw Error(reader, SaveIncompatibleReason.Corrupt, "Invalid string length prefix");
    }

    public static SaveIncompatibleException Error(BinaryReader reader, SaveIncompatibleReason reason, string message)
        => new(reason, message, reader.BaseStream.CanSeek ? reader.BaseStream.Position : null);

    public static SaveIncompatibleException Wrap(Exception exception, long position)
        => new(exception is EndOfStreamException ? SaveIncompatibleReason.Truncated
                : exception is OverflowException or ArgumentOutOfRangeException or IndexOutOfRangeException
                    ? SaveIncompatibleReason.OutOfRange : SaveIncompatibleReason.Corrupt,
            exception is EndOfStreamException ? "The save is truncated" : $"Invalid save data: {exception.Message}",
            position, exception);

    public static bool IsMalformed(Exception exception) => exception is EndOfStreamException or InvalidDataException
        or OverflowException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException
        or InvalidCastException or FormatException;
}
