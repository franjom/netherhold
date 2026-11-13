// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DK2.Simulation;

namespace DK2.Tools;

/// <summary>Projects decoded data fields without evaluating computed properties or following world links.</summary>
internal static class SnapshotFields
{
    internal static SortedDictionary<string, string?> Capture(object? value, string path)
    {
        var result = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(value, path, 0);
        return result;

        void Put(string name, string? text)
        {
            if (!result.TryAdd(name, text)) throw new InvalidDataException($"Duplicate projected field: {name}");
        }

        void Walk(object? item, string name, int depth)
        {
            if (depth > 32) throw new InvalidDataException($"Snapshot field nesting exceeds 32 at {name}.");
            if (item is null) { Put(name, null); return; }
            Type type = item.GetType();
            if (type.IsPrimitive || type.IsEnum || item is string or decimal)
            {
                Put(name, item is IFormattable formattable
                    ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? ""
                    : item.ToString() ?? "");
                return;
            }
            if (item is WorldState or Delegate)
                throw new InvalidDataException($"A non-data reference was reached at {name}.");
            bool tracked = !type.IsValueType;
            if (tracked && !active.Add(item)) throw new InvalidDataException($"Cyclic snapshot field at {name}.");
            try
            {
                if (item is IDictionary dictionary)
                {
                    Put(name + ".$count", dictionary.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (DictionaryEntry entry in dictionary)
                        Walk(entry.Value, DictionaryPath(name, entry.Key), depth + 1);
                    return;
                }
                if (item is IEnumerable sequence)
                {
                    object?[] entries = sequence.Cast<object?>().ToArray();
                    Put(name + ".$count", entries.Length.ToString(CultureInfo.InvariantCulture));
                    bool set = type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>));
                    for (int i = 0; i < entries.Length; i++)
                    {
                        string index = set ? Key(entries[i]) : i.ToString(CultureInfo.InvariantCulture);
                        Walk(entries[i], name + "[" + index + "]", depth + 1);
                    }
                    return;
                }
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Put(name + ".$type", type.FullName ?? type.Name);
                foreach (FieldInfo field in fields)
                {
                    if (typeof(WorldState).IsAssignableFrom(field.FieldType) || typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
                    string member = FieldName(field.Name);
                    Walk(field.GetValue(item), name + "." + member, depth + 1);
                }
            }
            finally
            {
                if (tracked) active.Remove(item);
            }
        }
    }

    private static string FieldName(string name)
    {
        if (name.StartsWith('<') && name.IndexOf('>') is int end && end > 1) return name[1..end];
        return name.Length > 1 && name[0] == '_' ? char.ToUpperInvariant(name[1]) + name[2..] : name;
    }

    private static string Key(object? value) => JsonSerializer.Serialize(Capture(value, "value"));

    private static string DictionaryPath(string path, object key) => key is string text
        && text.Length > 0 && char.IsAsciiLetter(text[0]) && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? path + "." + text
            : path + "[" + Key(key) + "]";
}
