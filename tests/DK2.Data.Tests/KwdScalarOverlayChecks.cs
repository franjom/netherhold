// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using DK2.Core;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

internal static class KwdScalarOverlayChecks
{
    internal static void Check<T>(KwdRecordType type, int stride, Func<byte[], T> read,
        Func<T, KwdRecordImage, byte[]> write, string fieldList, string[]? excludedProperties = null,
        IReadOnlyDictionary<string, object>? customValues = null) where T : class
    {
        var fields = fieldList.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Split(':'))
            .ToDictionary(f => f[0], f => (Offset: Convert.ToInt32(f[1], 16), Width: int.Parse(f[2])));
        PropertyInfo[] properties = typeof(T).GetProperties().Where(p => p.SetMethod is not null
            && !(excludedProperties?.Contains(p.Name) ?? false)).ToArray();
        Assert.Equal(properties.Length, fields.Count);
        byte[] source = Enumerable.Repeat((byte)0x5A, stride).ToArray();
        source[0] = (byte)'a'; source[1] = 0;
        var template = new KwdRecordImage(type, source);
        Assert.Equal(source, write(read(source), template));
        foreach (PropertyInfo property in properties)
        {
            var field = fields[property.Name];
            T changed = read(source);
            Type scalar = property.PropertyType;
            object value = customValues is not null && customValues.TryGetValue(property.Name, out object? custom) ? custom
                : scalar == typeof(string) ? "edited"
                : scalar == typeof(int) ? (field.Width == 2 ? 1234 : -12345)
                : scalar == typeof(uint) ? 0xF1234567u
                : scalar == typeof(short) ? (short)-1234
                : scalar == typeof(ushort) ? (ushort)1234
                : scalar == typeof(byte) ? (byte)13
                : scalar == typeof(Fp32) ? new Fp32(-12345)
                : scalar == typeof(IReadOnlyList<byte>) ? Enumerable.Range(1, field.Width).Select(i => (byte)i).ToArray()
                : scalar.IsEnum ? Enum.ToObject(scalar, 0x81)
                : throw new InvalidOperationException($"No scalar edit value for {property.Name}.");
            property.SetValue(changed, value);
            byte[] written = write(changed, template);
            object? reread = property.GetValue(read(written));
            if (value is IEnumerable<byte> bytes) Assert.Equal(bytes, Assert.IsAssignableFrom<IEnumerable<byte>>(reread));
            else Assert.Equal(value, reread);
            Assert.False(source.SequenceEqual(written));
            for (int i = 0; i < stride; i++)
                if (i < field.Offset || i >= field.Offset + field.Width) Assert.Equal(source[i], written[i]);
        }
        Assert.Equal(source, template.ToBytes());
    }
}
