// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

internal static class KwdOverlay
{
    internal static byte[] TaggedImage(KwdRecordImage template, KwdRecordType type, int tag, int payloadSize)
    {
        byte[] bytes = template.ToBytes();
        if (template.Type != type || template.Tag != tag || bytes.Length != payloadSize + 8
            || BinaryPrimitives.ReadInt32LittleEndian(bytes) != tag
            || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != payloadSize)
            throw new InvalidDataException($"{type}: an explicit tag {tag}, {payloadSize}-byte payload template is required.");
        return bytes;
    }

    internal static byte[] Image(KwdRecordImage template, KwdRecordType type, int stride)
    {
        if (template.Type != type || template.Tag is not null || template.ConsumedLength != stride)
            throw new InvalidDataException($"{type}: an explicit untagged {stride}-byte record template is required.");
        return template.ToBytes();
    }

    internal static void Ascii(byte[] image, int offset, int width, string value,
        string original, bool trimmed)
    {
        if (value == original) return;
        if (value is null || value.Length > width || value.Any(c => c == 0 || c > 0x7F)
            || (trimmed && value.Trim() != value))
            throw new InvalidDataException($"Name at +0x{offset:X} must round-trip as {width}-byte ASCII.");
        for (int i = 0; i < value.Length; i++) image[offset + i] = (byte)value[i];
        if (value.Length < width) image[offset + value.Length] = 0;
    }
}
