// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DK2.StyleAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamedEnumCastAnalyzer : DiagnosticAnalyzer
{
    private const string BaselineFile = "named-enum-casts.baseline";

    private static readonly DiagnosticDescriptor UseName = new DiagnosticDescriptor(
        "DK2C001", "Use a named enum member", "Use '{0}.{1}' for this value [{2}]",
        "Style", DiagnosticSeverity.Warning, isEnabledByDefault: true, customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    private static readonly DiagnosticDescriptor UpdateBaseline = new DiagnosticDescriptor(
        "DK2C002", "Update the named enum cast baseline", "{0}",
        "Style", DiagnosticSeverity.Warning, isEnabledByDefault: true, customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
    {
        get
        {
            return [UseName, UpdateBaseline];
        }
    }

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        AdditionalText? file = context.Options.AdditionalFiles.SingleOrDefault(
            candidate => Path.GetFileName(candidate.Path) == BaselineFile);
        SourceText? text = file?.GetText(context.CancellationToken);
        if (file is null || text is null || context.Compilation.AssemblyName is not { } assemblyName)
        {
            context.RegisterCompilationEndAction(static end => ReportBaseline(end, "The named enum cast baseline could not be read"));
            return;
        }

        var baseline = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TextLine line in text.Lines)
        {
            string entry = line.ToString();
            if (entry.Length == 0 || entry[0] == '#')
            {
                continue;
            }

            string[] columns = entry.Split('|');
            int separator = entry.LastIndexOf('|');
            if (columns.Length != 6 || !int.TryParse(columns[5], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                || count <= 0 || (columns[0] == assemblyName && baseline.ContainsKey(entry.Substring(0, separator))))
            {
                string message = $"Invalid named enum cast baseline entry at line {line.LineNumber + 1}";
                context.RegisterCompilationEndAction(end => ReportBaseline(end, message));
                return;
            }

            if (columns[0] == assemblyName)
            {
                baseline.Add(entry.Substring(0, separator), count);
            }
        }

        string root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(file.Path)!)!)!.Replace('\\', '/') + "/";
        var casts = new ConcurrentBag<Candidate>();
        context.RegisterSyntaxNodeAction(node => CollectCast(node, root, assemblyName, casts), SyntaxKind.CastExpression);
        context.RegisterCompilationEndAction(end => ReportCasts(end, casts, baseline));
    }

    private static void CollectCast(SyntaxNodeAnalysisContext context, string root, string assemblyName,
        ConcurrentBag<Candidate> casts)
    {
        var cast = (CastExpressionSyntax)context.Node;
        if (!IsNumericLiteral(cast.Expression)
            || context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            || context.SemanticModel.GetConstantValue(cast, context.CancellationToken) is not { HasValue: true, Value: { } value })
        {
            return;
        }

        string path = cast.SyntaxTree.FilePath.Replace('\\', '/');
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        path = path.Substring(root.Length);
        if (!(path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("tests/", StringComparison.Ordinal)
            || path.StartsWith("godot/DK2.Godot/", StringComparison.Ordinal))
            || path.Split('/').Any(part => part is "addons" or "bin" or "obj" or ".godot"))
        {
            return;
        }

        IFieldSymbol? namedMember = null;
        foreach (ISymbol symbol in enumType.GetMembers())
        {
            if (symbol is IFieldSymbol { HasConstantValue: true } field && Equals(field.ConstantValue, value))
            {
                if (namedMember is not null)
                {
                    return;
                }

                namedMember = field;
            }
        }

        if (namedMember is null)
        {
            return;
        }

        string? memberId = null;
        for (ISymbol? owner = context.ContainingSymbol; owner is not null && memberId is null; owner = owner.ContainingSymbol)
        {
            memberId = owner.GetDocumentationCommentId();
        }

        string enumName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string key = string.Join("|", assemblyName, path, memberId ?? "", enumName, Convert.ToString(value, CultureInfo.InvariantCulture));
        string memberName = SyntaxFacts.GetKeywordKind(namedMember.Name) == SyntaxKind.None ? namedMember.Name : "@" + namedMember.Name;
        casts.Add(new Candidate(key, cast.GetLocation(), enumName, memberName));
    }

    private static bool IsNumericLiteral(ExpressionSyntax expression)
    {
        if (expression is ParenthesizedExpressionSyntax parentheses)
        {
            return IsNumericLiteral(parentheses.Expression);
        }

        if (expression is PrefixUnaryExpressionSyntax unary
            && (unary.IsKind(SyntaxKind.UnaryMinusExpression) || unary.IsKind(SyntaxKind.UnaryPlusExpression)))
        {
            return IsNumericLiteral(unary.Operand);
        }

        return expression.IsKind(SyntaxKind.NumericLiteralExpression);
    }

    private static void ReportCasts(CompilationAnalysisContext context,
        ConcurrentBag<Candidate> casts, Dictionary<string, int> baseline)
    {
        foreach (IGrouping<string, Candidate> group in casts.GroupBy(cast => cast.Key, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            baseline.TryGetValue(group.Key, out int allowed);
            baseline.Remove(group.Key);
            Candidate[] ordered = [.. group.OrderBy(cast => cast.Location.SourceSpan.Start)];
            foreach (Candidate cast in ordered.Skip(allowed))
            {
                context.ReportDiagnostic(Diagnostic.Create(UseName, cast.Location, cast.EnumName, cast.Member, cast.Key));
            }

            if (ordered.Length < allowed)
            {
                ReportCount(context, group.Key, allowed, ordered.Length);
            }
        }

        foreach (KeyValuePair<string, int> entry in baseline.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            ReportCount(context, entry.Key, entry.Value, 0);
        }
    }

    private static void ReportCount(CompilationAnalysisContext context, string key, int previous, int current)
    {
        ReportBaseline(context, $"Reduce the named enum cast baseline count from {previous} to {current} [{key}]");
    }

    private static void ReportBaseline(CompilationAnalysisContext context, string message)
    {
        context.ReportDiagnostic(Diagnostic.Create(UpdateBaseline, Location.None, message));
    }

    private readonly struct Candidate
    {
        public Candidate(string key, Location location, string enumName, string member)
        {
            Key = key;
            Location = location;
            EnumName = enumName;
            Member = member;
        }

        public string Key { get; }
        public Location Location { get; }
        public string EnumName { get; }
        public string Member { get; }
    }
}
