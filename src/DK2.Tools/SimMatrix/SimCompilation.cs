// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DK2.Tools.SimMatrix;

/// <summary>The simulation's source as one Roslyn compilation: <c>DK2.Core</c>, <c>DK2.Data.Model</c>,
/// <c>DK2.Pathfinding</c> and <c>DK2.Simulation</c> compiled together against the running platform's
/// assemblies. One assembly makes every internal member visible and every call resolvable.</summary>
internal sealed class SimCompilation
{
    /// <summary>The projects compiled, relative to the repository root.</summary>
    internal static readonly string[] Projects = ["src/DK2.Core", "src/DK2.Data.Model", "src/DK2.Pathfinding", "src/DK2.Simulation"];

    /// <summary>The project whose types are immutable definitions, never state.</summary>
    internal const string DefinitionsProject = "src/DK2.Data.Model";

    // What <ImplicitUsings>enable</ImplicitUsings> gives a library project.
    private const string GlobalUsings = """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    private SimCompilation(CSharpCompilation compilation, IReadOnlySet<SyntaxTree> definitionTrees, int files)
    {
        Compilation = compilation;
        DefinitionTrees = definitionTrees;
        Files = files;
    }

    internal CSharpCompilation Compilation { get; }

    /// <summary>The trees under <see cref="DefinitionsProject"/>.</summary>
    internal IReadOnlySet<SyntaxTree> DefinitionTrees { get; }

    /// <summary>Source files parsed, without the global-usings tree.</summary>
    internal int Files { get; }

    /// <summary>Parses and compiles the four projects under <paramref name="root"/>; throws
    /// <see cref="InvalidDataException"/> with the first errors when the source does not compile.</summary>
    internal static SimCompilation Load(string root)
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None);
        var files = Projects
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(Path.GetRelativePath(root, path)))
                .Select(path => (project, path)))
            // By the relative path with forward slashes: the tree order decides partial members' order, and with
            // it which column reaches a type first, so it must not differ between Windows and Linux.
            .OrderBy(f => Path.GetRelativePath(root, f.path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal)
            .ToArray();
        var trees = new SyntaxTree[files.Length];
        Parallel.For(0, files.Length, i =>
            trees[i] = CSharpSyntaxTree.ParseText(File.ReadAllText(files[i].path), options, files[i].path));
        var definitions = files.Select((f, i) => (f, i))
            .Where(x => x.f.project == DefinitionsProject)
            .Select(x => trees[x.i])
            .ToHashSet();

        return Create(trees, definitions, files.Length);
    }

    /// <summary>A compilation of <paramref name="sources"/> alone, none of them definitions: for tests.</summary>
    internal static SimCompilation FromText(params string[] sources)
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None);
        var trees = sources.Select((text, i) => CSharpSyntaxTree.ParseText(text, options, $"Source{i}.cs")).ToArray();
        return Create(trees, new HashSet<SyntaxTree>(), trees.Length);
    }

    private static SimCompilation Create(SyntaxTree[] trees, IReadOnlySet<SyntaxTree> definitions, int files)
    {
        var options = (CSharpParseOptions)trees[0].Options;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => Path.GetFileName(path) is var name && (name.StartsWith("System.", StringComparison.Ordinal)
                || name is "mscorlib.dll" or "netstandard.dll"))
            .Order(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "DK2.SimMatrix",
            [.. trees, CSharpSyntaxTree.ParseText(GlobalUsings, options, "GlobalUsings.g.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true,
                concurrentBuild: true));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
            throw new InvalidDataException(
                $"the simulation source does not compile as one assembly ({errors.Length} errors): "
                + string.Join("; ", errors.Take(5).Select(e => e.ToString())));
        return new SimCompilation(compilation, definitions, files);
    }

    private static bool IsBuildOutput(string relative)
    {
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("obj") || parts.Contains("bin");
    }
}
