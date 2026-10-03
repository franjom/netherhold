// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

internal static class KwdNestedOverlayChecks
{
    internal static void Art<T>(KwdRecordType type, int stride, int offset, Func<byte[], T> read,
        Func<T, KwdRecordImage, byte[]> write, Func<T, KwdArtResource> select,
        Func<T, KwdArtResource, T> replace) where T : class => Check(type, stride, offset, 84,
            read, write, select, replace,
            "Name:0:64 Flags:40:4 Unknown_0044:44:12 Type:50:1 Unknown_0051:51:1 Unknown_0052:52:1 Unknown_0053:53:1");

    internal static void Light<T>(KwdRecordType type, int stride, int offset, Func<byte[], T> read,
        Func<T, KwdRecordImage, byte[]> write, Func<T, KwdLight> select,
        Func<T, KwdLight, T> replace) where T : class => Check(type, stride, offset, 24,
            read, write, select, replace,
            "X:0:4 Y:4:4 Z:8:4 Radius:C:4 Flags:10:4 Red:14:1 Green:15:1 Blue:16:1 Alpha:17:1");

    private static void Check<T, TNested>(KwdRecordType type, int stride, int offset, int width,
        Func<byte[], T> read, Func<T, KwdRecordImage, byte[]> write, Func<T, TNested> select,
        Func<T, TNested, T> replace, string fields) where T : class where TNested : class
    {
        byte[] Frame(byte[] nested)
        {
            byte[] parent = Enumerable.Repeat((byte)0x5A, stride).ToArray();
            parent[0] = (byte)'a'; parent[1] = 0; nested.CopyTo(parent, offset); return parent;
        }
        KwdScalarOverlayChecks.Check(type, width, bytes => select(read(Frame(bytes))), (row, image) =>
        {
            byte[] source = Frame(image.ToBytes());
            byte[] result = write(replace(read(source), row), new KwdRecordImage(type, source));
            for (int i = 0; i < source.Length; i++)
                if (i < offset || i >= offset + width) Assert.Equal(source[i], result[i]);
            return result[offset..(offset + width)];
        }, fields);
    }
}
