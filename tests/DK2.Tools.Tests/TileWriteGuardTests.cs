// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DK2.Tools.Tests;

/// <summary>
/// <c>TileState</c>'s terrain, owner, ruin bit and Secret Door bit are properties that count each change in
/// <c>TileState.KeyWriteCount</c>, and the room sweep, the structure mask and the navigation bytes trust that count
/// instead of reading every tile. A whole <c>TileState</c> written into a live world's tile
/// (<c>world.Tile(p) = x</c>, <c>tiles[i] = x</c>, a ref local assigned through) copies the private fields without
/// running a setter, so those caches would go on answering from tiles that changed. This reads all of <c>src/</c> with
/// Roslyn and fails on such a write; the one legitimate bulk copy (<c>WorldState.CopyFrom</c>) is recognised by calling
/// <c>NoteBulkKeyWrite</c> in the same method, and filling an array a local variable was just given (<c>new TileState[n]</c>,
/// what <c>WorldState.FromMap</c> and the snapshot reader do before any world has the array) is not a write into a world.
/// What else is allowed is listed by name below with its reason, and an entry that no longer matches anything fails too,
/// so the list cannot rot.
/// </summary>
public class TileWriteGuardTests
{
    private static string Root
    {
        get
        {
            return Repo.FindRepoRoot() ?? throw new InvalidOperationException("no repository above the test");
        }
    }

    // A whole-tile write the guard lets through: where it is (file under the repository root, enclosing member) and why.
    private sealed record Allowed(string File, string Member, string Reason);

    private static readonly Allowed[] AllowList =
    [
        new("src/DK2.Presentation/ScenePresenter.cs", "SnapshotTiles",
            "copies the world's tiles into the presenter's own array to see which changed; that array is not a world's"),
    ];

    private const string GlobalUsings = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    private static readonly string[] BulkWriters = ["Copy", "CopyTo", "Fill", "Clear", "Reverse", "Sort"];

    // A file that writes a whole tile has to get one from somewhere: a type name, or the world's accessors.
    private static readonly string[] Hints = ["TileState", "Tile(", "TileAtIndex", "RawTiles", ".Tiles", "_tiles"];

    [Fact]
    public void NoSourceWritesAWholeTileIntoALiveWorld()
    {
        string root = Root;
        List<Violation> found = Scan(Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(Path.GetRelativePath(root, path)))
            .Select(path => (Relative(root, path), File.ReadAllText(path))));

        string[] unexpected = [.. found.Where(v => !AllowList.Any(a => Matches(a, v))).Select(v => v.ToString())];
        Assert.True(unexpected.Length == 0,
            "A whole TileState is written without TileState's setters, so the caches keyed on KeyWriteCount stay stale. "
            + "Assign the fields (TerrainId, OwnerPlayerId, RoomRuined, SecretDoor go through counted setters) or call "
            + "TileState.NoteBulkKeyWrite after a bulk copy:\n" + string.Join("\n", unexpected));

        string[] unused = [.. AllowList.Where(a => !found.Any(v => Matches(a, v))).Select(a => $"{a.File} {a.Member}")];
        Assert.True(unused.Length == 0, "AllowList entries that match no whole-tile write any more; remove them:\n" + string.Join("\n", unused));
    }

    [Fact]
    public void TheGuardSeesEachWayOfWritingAWholeTileIntoAWorld()
    {
        const string model = """
            namespace DK2.Simulation;
            public struct TileState
            {
                private byte _terrainId;
                public byte TerrainId { readonly get => _terrainId; set => _terrainId = value; }
                public int Health;
                internal static void NoteBulkKeyWrite() { }
            }
            public sealed class WorldState
            {
                private readonly TileState[] _tiles = new TileState[4];
                public ref TileState Tile(int x, int y) => ref _tiles[y * 2 + x];
                public ref TileState TileAtIndex(int i) => ref _tiles[i];
                public ReadOnlySpan<TileState> Tiles => _tiles;
                public TileState[] RawTiles => _tiles;
            }
            """;
        string[] violations =
        [
            "world.Tile(1, 1) = other;",
            "world.TileAtIndex(2) = default;",
            "world.RawTiles[1] = other;",
            "world.Tile(0, 0) = new TileState { Health = 3 };",
            "ref TileState alias = ref world.Tile(0, 1); alias = other;",
            "(world.RawTiles[0], world.RawTiles[1]) = (world.RawTiles[1], world.RawTiles[0]);",
            "System.Array.Copy(copy.RawTiles, world.RawTiles, 4);",
            "copy.RawTiles.AsSpan().CopyTo(world.RawTiles);",
            "System.Array.Fill(world.RawTiles, other);",
            "var alias = world.RawTiles; alias[0] = other;",
        ];
        foreach (string line in violations)
        {
            List<Violation> found = Scan([("Case.cs", Wrap(model, line))]);
            Assert.True(found.Count > 0, $"not caught: {line}");
            Assert.All(found, v => Assert.Equal("Case.cs", v.File));
        }

        string[] allowed =
        [
            "TileState copy = world.Tile(1, 1); copy = other; copy.Health = 2;",
            "world.Tile(1, 1).TerrainId = 4; world.Tile(1, 1).Health++;",
            "var spare = new TileState[4]; spare[0] = other;",
            "System.Array.Copy(copy.RawTiles, world.RawTiles, 4); TileState.NoteBulkKeyWrite();",
            "TileState seen; seen = world.Tile(0, 0); ref TileState rebound = ref world.Tile(0, 1); rebound = ref world.Tile(1, 1);",
        ];
        foreach (string line in allowed)
        {
            Assert.Empty(Scan([("Case.cs", Wrap(model, line))]));
        }
    }

    [Fact]
    public void TheGuardFiresOnTheRealWorldStateSourceWhenItIsBrokenInMemory()
    {
        // The real files, edited in memory only: the guard has to pass on them as they are and fail on each break.
        const string statePath = "src/DK2.Simulation/WorldState.cs", copyPath = "src/DK2.Simulation/WorldState.Copy.cs";
        const string count = "TileState.NoteBulkKeyWrite();";
        string state = File.ReadAllText(Path.Combine(Root, statePath)), copy = File.ReadAllText(Path.Combine(Root, copyPath));
        Assert.Contains(count, copy);
        Assert.Empty(Scan([(statePath, state), (copyPath, copy)]));

        List<Violation> uncounted = Scan([(statePath, state), (copyPath, copy.Replace(count, ""))]);
        Assert.Contains(uncounted, v => v.File == copyPath && v.Member == "CopyFrom");

        ClassDeclarationSyntax worldState = CSharpSyntaxTree.ParseText(state).GetCompilationUnitRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "WorldState");
        foreach (string body in new[] { "_tiles[0] = other;", "TileAtIndex(0) = other;", "Tile(0, 0) = default;" })
        {
            string broken = state.Insert(worldState.CloseBraceToken.SpanStart, $"    internal void Oops(TileState other) {{ {body} }}\n");
            List<Violation> found = Scan([(statePath, broken), (copyPath, copy)]);
            Assert.Contains(found, v => v.File == statePath && v.Member == "Oops");
        }
    }

    private static string Wrap(string model, string statements)
    {
        return "using DK2.Simulation;\n" + model + $$"""

        public static class Case
        {
            public static void Run(DK2.Simulation.WorldState world, DK2.Simulation.WorldState copy, DK2.Simulation.TileState other)
            {
                {{statements}}
            }
        }
        """;
    }

    private sealed record Violation(string File, int Line, string Member, string Code)
    {
        public override string ToString()
        {
            return $"{File}:{Line} in {Member}: {Code}";
        }
    }

    private static bool Matches(Allowed allowed, Violation v)
    {
        return allowed.File == v.File && allowed.Member == v.Member;
    }

    private static List<Violation> Scan(IEnumerable<(string Path, string Text)> sources)
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None);
        var trees = sources
            .Where(s => Hints.Any(h => s.Text.Contains(h, StringComparison.Ordinal)))
            .OrderBy(s => s.Path, StringComparer.Ordinal)
            .Select(s => CSharpSyntaxTree.ParseText(s.Text, options, s.Path))
            .ToList();
        // Only files that could write a tile are compiled; the one that declares TileState names it, so it is in.
        var compilation = CSharpCompilation.Create(
            "DK2.TileWriteGuard",
            [.. trees, CSharpSyntaxTree.ParseText(GlobalUsings, options, "GlobalUsings.g.cs")],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true, concurrentBuild: true));
        INamedTypeSymbol? tile = compilation.GetTypeByMetadataName("DK2.Simulation.TileState");
        Assert.NotNull(tile);

        var found = new System.Collections.Concurrent.ConcurrentBag<Violation>();
        Parallel.ForEach(trees, tree =>
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression, Right: not RefExpressionSyntax } assignment:
                        foreach (ExpressionSyntax target in Targets(assignment.Left))
                        {
                            if (WritesWholeTile(model, target, tile))
                            {
                                found.Add(At(tree, node, target.ToString() + " = ..."));
                            }
                        }

                        break;
                    case InvocationExpressionSyntax call when IsBulkWrite(model, call, tile) && !CountsTheBulkWrite(call):
                        found.Add(At(tree, node, call.ToString()));
                        break;
                }
            }
        });
        return [.. found.OrderBy(v => v.File, StringComparer.Ordinal).ThenBy(v => v.Line)];
    }

    private static IEnumerable<ExpressionSyntax> Targets(ExpressionSyntax left)
    {
        switch (left)
        {
            case ParenthesizedExpressionSyntax p:
                foreach (ExpressionSyntax t in Targets(p.Expression))
                {
                    yield return t;
                }

                break;
            case TupleExpressionSyntax tuple:
                foreach (ArgumentSyntax argument in tuple.Arguments)
                {
                    foreach (ExpressionSyntax t in Targets(argument.Expression))
                    {
                        yield return t;
                    }
                }

                break;
            default:
                yield return left;
                break;
        }
    }

    // The target is a TileState that something else holds: a call returning a ref, an array or span element, a ref
    // local, a ref or out parameter. A plain local, parameter or field of a class is a copy, and writing it is nothing.
    private static bool WritesWholeTile(SemanticModel model, ExpressionSyntax target, INamedTypeSymbol tile)
    {
        if (!SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(target).Type, tile))
        {
            return false;
        }

        switch (target)
        {
            case InvocationExpressionSyntax:
                return true;
            case ElementAccessExpressionSyntax element:
                return !IsFreshLocalArray(model, element);
        }
        return model.GetSymbolInfo(target).Symbol switch
        {
            ILocalSymbol local => local.RefKind != RefKind.None,
            IParameterSymbol parameter => parameter.RefKind != RefKind.None,
            IPropertySymbol property => property.ReturnsByRef,
            IMethodSymbol method => method.ReturnsByRef,
            _ => false,
        };
    }

    // `var tiles = new TileState[n]; tiles[i] = ...`: no world holds that array yet.
    private static bool IsFreshLocalArray(SemanticModel model, ElementAccessExpressionSyntax element)
    {
        return model.GetSymbolInfo(element.Expression).Symbol is ILocalSymbol { RefKind: RefKind.None } local
        && local.DeclaringSyntaxReferences.Length == 1
        && local.DeclaringSyntaxReferences[0].GetSyntax() is VariableDeclaratorSyntax
        {
            Initializer.Value: ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax,
        };
    }

    private static bool IsBulkWrite(SemanticModel model, InvocationExpressionSyntax call, INamedTypeSymbol tile)
    {
        string? name = call.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            IdentifierNameSyntax i => i.Identifier.ValueText,
            _ => null,
        };
        if (name is null || !BulkWriters.Contains(name))
        {
            return false;
        }

        var types = call.ArgumentList.Arguments.Select(a => model.GetTypeInfo(a.Expression).Type).ToList();
        if (call.Expression is MemberAccessExpressionSyntax { Expression: { } receiver })
        {
            types.Add(model.GetTypeInfo(receiver).Type);
        }

        return types.Any(t => HoldsTiles(t, tile));
    }

    // TileState[], Span<TileState>, ReadOnlySpan<TileState>, List<TileState> and the like.
    private static bool HoldsTiles(ITypeSymbol? type, INamedTypeSymbol tile)
    {
        return type switch
        {
            IArrayTypeSymbol array => SymbolEqualityComparer.Default.Equals(array.ElementType, tile),
            INamedTypeSymbol named => named.TypeArguments.Any(a => SymbolEqualityComparer.Default.Equals(a, tile)),
            _ => false,
        };
    }

    // WorldState.CopyFrom copies the whole array and then counts it as one change.
    private static bool CountsTheBulkWrite(InvocationExpressionSyntax call)
    {
        return call.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() is { } method
        && method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i.Expression.ToString().EndsWith("NoteBulkKeyWrite", StringComparison.Ordinal));
    }

    private static Violation At(SyntaxTree tree, SyntaxNode node, string code)
    {
        string member = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>()
            .Select(m => m switch
            {
                MethodDeclarationSyntax x => x.Identifier.ValueText,
                ConstructorDeclarationSyntax x => x.Identifier.ValueText,
                PropertyDeclarationSyntax x => x.Identifier.ValueText,
                _ => null,
            })
            .FirstOrDefault(n => n is not null) ?? "<member>";
        return new Violation(tree.FilePath, tree.GetLineSpan(node.Span).StartLinePosition.Line + 1, member, code);
    }

    private static IEnumerable<MetadataReference> References()
    {
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => Path.GetFileName(path) is var name && (name.StartsWith("System.", StringComparison.Ordinal)
                || name is "mscorlib.dll" or "netstandard.dll"))
            .Order(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    private static string Relative(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool IsBuildOutput(string relative)
    {
        return relative.Split(['/', '\\']).Any(part => part is "obj" or "bin");
    }
}
