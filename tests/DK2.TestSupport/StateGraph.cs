// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace DK2.TestSupport;

/// <summary>Walks two object graphs field by field, by reflection, and fails where they differ or
/// where the second shares a mutable reference with the first. It is what holds a hand-written or
/// generated copy (<c>WorldState.CopyFrom</c>, ADR-0693) to every field, including the ones no hash
/// covers: a field added to a state class without a line in its CopyFrom fails here.</summary>
public static class StateGraph
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>What <c>WorldState.CopyFrom</c> leaves alone: derived caches, events, the bound
    /// generators and its own scratch, none of them state (<c>WorldState.HashInto</c>).</summary>
    public static readonly IReadOnlySet<string> WorldIgnored = new HashSet<string>
    {
        "StructureCache", "RoomSweepCache", "RoomTilesChanged", "HeartHealthChanged", "GameEnded", "ObjectMade",
        "ObjectRemoving", "_creatureIdentityRandom", "RoomConstructionRandom", "LevelStartWorkshops", "_copyIndex",
        "_staleSecretDoorKeys", "Version",
    };

    /// <summary>The immutable members a cloned thing may share with the original: a hero party's
    /// members and name, an effect generator's ids, and the unread bytes a level record carries.</summary>
    public static readonly IReadOnlySet<string> WorldShared = new HashSet<string>
    {
        "Members", "Name", "EffectIds", "_unknown001E", "_unknown0011",
    };

    /// <summary>Asserts <paramref name="copy"/> is a deep copy of <paramref name="original"/>.
    /// <paramref name="ignored"/> names members not compared (caches, events, bound generators);
    /// <paramref name="shared"/> names members whose reference may be the same object, because it is
    /// immutable. A member named <c>LinkWorld</c> must point at its own graph's root.</summary>
    public static void AssertDeepCopy(object original, object copy, IReadOnlySet<string> ignored, IReadOnlySet<string> shared)
    {
        var walk = new Walk(original, copy, ignored, shared);
        walk.Visit(original, copy, original.GetType().Name);
    }

    private sealed class Walk(object rootA, object rootB, IReadOnlySet<string> ignored, IReadOnlySet<string> shared)
    {
        private readonly HashSet<(object, object)> _seen = new(ReferencePairComparer.Instance);

        public void Visit(object? a, object? b, string path)
        {
            if (a is null || b is null)
            {
                Assert.True(a is null && b is null, $"{path}: {(a is null ? "null" : "a value")} against {(b is null ? "null" : "a value")}");
                return;
            }
            Type type = a.GetType();
            Assert.True(type == b.GetType(), $"{path}: {type.Name} against {b.GetType().Name}");
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                Assert.True(a.Equals(b), $"{path}: {a} against {b}");
                return;
            }
            if (a is Delegate) return;
            if (type.IsValueType)
            {
                VisitFields(a, b, type, path);
                return;
            }
            if (ReferenceEquals(a, b))
            {
                Assert.Fail($"{path}: the copy shares the original's {type.Name}");
            }
            if (!_seen.Add((a, b))) return;
            switch (a)
            {
                case Array arrayA:
                    var arrayB = (Array)b;
                    Assert.True(arrayA.Length == arrayB.Length, $"{path}: {arrayA.Length} elements against {arrayB.Length}");
                    for (int i = 0; i < arrayA.Length; i++) Visit(arrayA.GetValue(i), arrayB.GetValue(i), $"{path}[{i}]");
                    return;
                case IDictionary dictionaryA:
                    var dictionaryB = (IDictionary)b;
                    Assert.True(dictionaryA.Count == dictionaryB.Count, $"{path}: {dictionaryA.Count} entries against {dictionaryB.Count}");
                    foreach (DictionaryEntry entry in dictionaryA)
                    {
                        Assert.True(dictionaryB.Contains(entry.Key), $"{path}: the copy lacks key {entry.Key}");
                        Visit(entry.Value, dictionaryB[entry.Key], $"{path}[{entry.Key}]");
                    }
                    return;
                case IEnumerable enumerableA when type.IsGenericType || type.IsArray:
                    object[] itemsA = [.. enumerableA.Cast<object>()];
                    object[] itemsB = [.. ((IEnumerable)b).Cast<object>()];
                    Assert.True(itemsA.Length == itemsB.Length, $"{path}: {itemsA.Length} items against {itemsB.Length}");
                    for (int i = 0; i < itemsA.Length; i++) Visit(itemsA[i], itemsB[i], $"{path}[{i}]");
                    return;
                default:
                    VisitFields(a, b, type, path);
                    return;
            }
        }

        private void VisitFields(object a, object b, Type type, string path)
        {
            for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(Instance | BindingFlags.DeclaredOnly))
                {
                    string name = MemberName(field);
                    if (ignored.Contains(name)) continue;
                    object? valueA = field.GetValue(a), valueB = field.GetValue(b);
                    string here = $"{path}.{name}";
                    if (name == "LinkWorld")
                    {
                        Assert.True(valueA is null ? valueB is null : ReferenceEquals(valueA, rootA) && ReferenceEquals(valueB, rootB),
                            $"{here}: each thing links its own world");
                        continue;
                    }
                    if (shared.Contains(name) && (ReferenceEquals(valueA, valueB) || field.FieldType.IsValueType)) continue;
                    if (valueA is Array { Length: 0 } && ReferenceEquals(valueA, valueB)) continue;
                    Visit(valueA, valueB, here);
                }
            }
        }

        private static string MemberName(FieldInfo field)
        {
            string name = field.Name;
            if (name.StartsWith('<') && name.EndsWith(">k__BackingField", StringComparison.Ordinal))
                return name[1..name.IndexOf('>')];
            return name;
        }
    }

    private sealed class ReferencePairComparer : IEqualityComparer<(object, object)>
    {
        public static readonly ReferencePairComparer Instance = new();

        public bool Equals((object, object) x, (object, object) y) =>
            ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

        public int GetHashCode((object, object) pair) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(pair.Item1), RuntimeHelpers.GetHashCode(pair.Item2));
    }
}
