// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DK2.Tools.Tests;

public sealed class CSharpStyleTests
{
    [Fact]
    public void OrdinarySystemImportsComeFirst()
    {
        string root = Repo.FindRepoRoot() ?? throw new InvalidOperationException("no repository above the test");
        List<string> violations = [];
        foreach (string path in ReadSourcePaths(root))
        {
            string relativePath = Path.GetRelativePath(root, path);
            CompilationUnitSyntax file = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: relativePath)
                .GetCompilationUnitRoot();
            CheckImportOrder(file.Usings, violations);
            foreach (BaseNamespaceDeclarationSyntax scope in file
                .DescendantNodes(static node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
                .OfType<BaseNamespaceDeclarationSyntax>())
            {
                CheckImportOrder(scope.Usings, violations);
            }
        }

        Assert.Empty(violations);
    }

    private static void CheckImportOrder(SyntaxList<UsingDirectiveSyntax> imports, List<string> violations)
    {
        bool seenOtherGlobal = false;
        bool seenOtherLocal = false;
        foreach (UsingDirectiveSyntax import in imports)
        {
            if (import.Alias is not null || import.StaticKeyword.RawKind != 0)
            {
                continue;
            }

            if (import.GlobalKeyword.RawKind != 0)
            {
                CheckImport(import, ref seenOtherGlobal, violations);
            }
            else
            {
                CheckImport(import, ref seenOtherLocal, violations);
            }
        }
    }

    private static void CheckImport(UsingDirectiveSyntax import, ref bool seenOther, List<string> violations)
    {
        NameSyntax? name = import.Name;
        while (name is QualifiedNameSyntax dotted)
        {
            name = dotted.Left;
        }

        bool isSystem = name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "System",
            AliasQualifiedNameSyntax qualified => qualified.Alias.Identifier.ValueText == "global"
                && qualified.Name.Identifier.ValueText == "System",
            _ => false,
        };
        if (!isSystem)
        {
            seenOther = true;
        }
        else if (seenOther)
        {
            FileLinePositionSpan location = import.GetLocation().GetLineSpan();
            violations.Add($"{location.Path}:{location.StartLinePosition.Line + 1}: {import}");
        }
    }

    private static IEnumerable<string> ReadSourcePaths(string root)
    {
        string[] folders = ["src", "tests", "godot/DK2.Godot"];
        foreach (string folder in folders)
        {
            foreach (string path in ReadDirectory(Path.Combine(root, folder)))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> ReadDirectory(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*.cs"))
        {
            if (!path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (Path.GetFileName(child) is "bin" or "obj" or ".godot" or "addons"
                || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (string path in ReadDirectory(child))
            {
                yield return path;
            }
        }
    }

    [Fact]
    public void ObjectCreationsSpellOutTheirTypes()
    {
        AssertNoCSharpSyntax<ImplicitObjectCreationExpressionSyntax>(static _ => true);
    }

    private static void AssertNoCSharpSyntax<T>(Func<T, bool> matches) where T : SyntaxNode
    {
        string root = Repo.FindRepoRoot() ?? throw new InvalidOperationException("no repository above the test");
        HashSet<string> violations = [];
        foreach (string path in ReadSourcePaths(root))
        {
            string relativePath = Path.GetRelativePath(root, path);
            string source = File.ReadAllText(path);
            CheckSource(CSharpParseOptions.Default);
            if (source.Contains("DEBUG", StringComparison.Ordinal))
            {
                CheckSource(CSharpParseOptions.Default.WithPreprocessorSymbols("DEBUG"));
            }

            void CheckSource(CSharpParseOptions options)
            {
                SyntaxNode file = CSharpSyntaxTree.ParseText(source, options, path: relativePath).GetRoot();
                foreach (T node in file.DescendantNodes().OfType<T>().Where(matches))
                {
                    FileLinePositionSpan location = node.GetLocation().GetLineSpan();
                    violations.Add($"[{location.Path}:{location.StartLinePosition.Line + 1}] {node}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ClassesAndStructsUseExplicitConstructors()
    {
        AssertNoCSharpSyntax<TypeDeclarationSyntax>(static type =>
            type is ClassDeclarationSyntax { ParameterList: not null }
                or StructDeclarationSyntax { ParameterList: not null });
    }
}
