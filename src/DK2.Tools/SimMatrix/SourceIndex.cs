// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DK2.Tools.SimMatrix;

/// <summary>The compilation's source types, and for each interface member, abstract or virtual method
/// every source method a call to it can land in.</summary>
internal sealed class SourceIndex
{
    private readonly IReadOnlySet<SyntaxTree> _definitions;
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _derived = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, List<IMethodSymbol>> _dispatch = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<SyntaxTree, SemanticModel> _models = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

    internal SourceIndex(SimCompilation source)
    {
        Compilation = source.Compilation;
        _definitions = source.DefinitionTrees;
        var types = new List<INamedTypeSymbol>();
        Collect(Compilation.Assembly.GlobalNamespace, types);
        Types = types;

        foreach (INamedTypeSymbol type in types)
        {
            for (INamedTypeSymbol? b = type.BaseType; b is not null; b = b.BaseType)
            {
                if (IsSource(b))
                {
                    Add(_derived, b.OriginalDefinition, type);
                }
            }

            foreach (INamedTypeSymbol face in type.AllInterfaces)
            {
                if (IsSource(face))
                {
                    Add(_derived, face.OriginalDefinition, type);
                }
            }

            if (type.TypeKind == TypeKind.Interface)
            {
                continue;
            }

            foreach (INamedTypeSymbol face in type.AllInterfaces)
            {
                foreach (ISymbol member in face.GetMembers())
                {
                    switch (type.FindImplementationForInterfaceMember(member))
                    {
                        case IMethodSymbol method when member is IMethodSymbol faceMethod:
                            AddDispatch(faceMethod, method);
                            break;
                        case IPropertySymbol property when member is IPropertySymbol faceProperty:
                            AddDispatch(faceProperty.GetMethod, property.GetMethod);
                            AddDispatch(faceProperty.SetMethod, property.SetMethod);
                            break;
                    }
                }
            }
            foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
            {
                for (IMethodSymbol? overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
                {
                    AddDispatch(overridden, method);
                }
            }
        }
    }

    internal Compilation Compilation { get; }

    /// <summary>Every type declared in the source, nested types included.</summary>
    internal IReadOnlyList<INamedTypeSymbol> Types { get; }

    internal SemanticModel Model(SyntaxTree tree)
    {
        return _models.GetOrAdd(tree, t => Compilation.GetSemanticModel(t));
    }

    /// <summary>A type declared in the source, outside <c>DK2.Data.Model</c>.</summary>
    internal bool IsStateSource(INamedTypeSymbol type)
    {
        return type.DeclaringSyntaxReferences is { Length: > 0 } references && !_definitions.Contains(references[0].SyntaxTree);
    }

    /// <summary>Source classes and structs that derive from or implement <paramref name="type"/>.</summary>
    internal IEnumerable<INamedTypeSymbol> Implementations(INamedTypeSymbol type)
    {
        return _derived.TryGetValue(type.OriginalDefinition, out List<INamedTypeSymbol>? list) ? list : [];
    }

    /// <summary>The source methods a call to <paramref name="method"/> can run: its overrides and implementations,
    /// and itself when it has a body.</summary>
    internal IEnumerable<IMethodSymbol> Targets(IMethodSymbol method)
    {
        if (_dispatch.TryGetValue(method, out List<IMethodSymbol>? list))
        {
            foreach (IMethodSymbol target in list)
            {
                yield return target;
            }
        }

        if (!method.IsAbstract && IsSource(method))
        {
            yield return method;
        }
    }

    internal static bool IsSource(ISymbol symbol)
    {
        return symbol.DeclaringSyntaxReferences.Length > 0;
    }

    /// <summary>The definition a call names: the unreduced, unconstructed method.</summary>
    internal static IMethodSymbol Definition(IMethodSymbol method)
    {
        return (method.ReducedFrom ?? method).OriginalDefinition;
    }

    private void AddDispatch(IMethodSymbol? from, IMethodSymbol? to)
    {
        if (from is null || to is null)
        {
            return;
        }

        to = Definition(to);
        if (!IsSource(to))
        {
            return;
        }

        from = Definition(from);
        if (SymbolEqualityComparer.Default.Equals(from, to))
        {
            return;
        }

        if (!_dispatch.TryGetValue(from, out List<IMethodSymbol>? list))
        {
            _dispatch[from] = list = [];
        }

        if (!list.Contains(to, SymbolEqualityComparer.Default))
        {
            list.Add(to);
        }
    }

    private static void Add(Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> map, INamedTypeSymbol key, INamedTypeSymbol value)
    {
        if (!map.TryGetValue(key, out List<INamedTypeSymbol>? list))
        {
            map[key] = list = [];
        }

        if (!list.Contains(value, SymbolEqualityComparer.Default))
        {
            list.Add(value);
        }
    }

    private static void Collect(INamespaceOrTypeSymbol container, List<INamedTypeSymbol> types)
    {
        foreach (ISymbol member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                Collect(ns, types);
            }
            else if (member is INamedTypeSymbol type && IsSource(type))
            {
                types.Add(type);
                Collect(type, types);
            }
        }
    }
}
