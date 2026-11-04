// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DK2.Tools.SimMatrix;

/// <summary>A system the tick runs, or a step a container runs for each creature or room.</summary>
internal enum RowKind
{
    System,
    CreatureStep,
    RoomStep,
}

/// <summary>One row of the matrix: a system in <c>SimulationSystems.Default()</c>, or one of a container's
/// steps, with the methods the tick enters it through.</summary>
/// <param name="Number">1-based, in tick order.</param>
/// <param name="Parent">The container's <see cref="Number"/> for a step; 0 for a system.</param>
internal sealed record SimRow(
    int Number,
    string Phase,
    string Name,
    RowKind Kind,
    int Parent,
    INamedTypeSymbol Type,
    IReadOnlyList<IMethodSymbol> Entries);

/// <summary>Reads the rows from the syntax of <c>SimulationSystems.Default()</c>: its phase methods in order,
/// each phase's elements in order, and a container's constructor arguments as its steps.</summary>
internal static class SimRows
{
    internal const string SystemsType = "DK2.Simulation.Systems.SimulationSystems";
    internal const string SystemInterface = "DK2.Simulation.ISimulationSystem";
    internal const string CreatureStepInterface = "DK2.Simulation.Systems.ICreatureUpdateStep";
    internal const string RoomStepInterface = "DK2.Simulation.Systems.IRoomTickStep";

    internal static IReadOnlyList<SimRow> Read(SimCompilation source)
    {
        var compilation = source.Compilation;
        var systems = Type(compilation, SystemsType);
        var system = Type(compilation, SystemInterface);
        var creatureStep = Type(compilation, CreatureStepInterface);
        var roomStep = Type(compilation, RoomStepInterface);
        var reader = new Reader(compilation, systems, system, creatureStep, roomStep);

        var main = systems.GetMembers("Default").OfType<IMethodSymbol>().Single();
        var rows = new List<SimRow>();
        if (reader.Returned(main) is not CollectionExpressionSyntax phases)
            throw new InvalidDataException("SimulationSystems.Default() is no longer one collection expression of phases.");
        foreach (var element in phases.Elements)
        {
            if (element is not SpreadElementSyntax { Expression: InvocationExpressionSyntax call }
                || reader.Model(call).GetSymbolInfo(call).Symbol is not IMethodSymbol phase)
                throw new InvalidDataException($"SimulationSystems.Default(): '{element}' is not a spread of a phase method.");
            if (reader.Returned(phase) is not CollectionExpressionSyntax members)
                throw new InvalidDataException($"SimulationSystems.{phase.Name}() is not one collection expression.");
            foreach (var member in members.Elements)
            {
                if (member is not ExpressionElementSyntax { Expression: var expression })
                    throw new InvalidDataException($"SimulationSystems.{phase.Name}(): '{member}' is not one system.");
                reader.Add(rows, phase.Name, expression, RowKind.System, 0);
            }
        }
        return rows;
    }

    internal static INamedTypeSymbol Type(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName(name) ?? throw new InvalidDataException($"type {name} is not in the simulation source.");

    /// <summary>A type's name as the source writes it from outside: containing types, then the type.</summary>
    internal static string Display(INamedTypeSymbol type) =>
        type.ContainingType is { } outer ? Display(outer) + "." + type.Name : type.Name;

    private sealed class Reader(
        Compilation compilation,
        INamedTypeSymbol systems,
        INamedTypeSymbol system,
        INamedTypeSymbol creatureStep,
        INamedTypeSymbol roomStep)
    {
        private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];

        internal SemanticModel Model(SyntaxNode node)
        {
            if (!_models.TryGetValue(node.SyntaxTree, out var model))
                _models[node.SyntaxTree] = model = compilation.GetSemanticModel(node.SyntaxTree);
            return model;
        }

        /// <summary>The expression a method returns: its expression body, or its last return statement's.</summary>
        internal ExpressionSyntax? Returned(IMethodSymbol method)
        {
            var declaration = method.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>().Single();
            return declaration.ExpressionBody?.Expression
                   ?? declaration.Body?.Statements.OfType<ReturnStatementSyntax>().LastOrDefault()?.Expression;
        }

        internal void Add(List<SimRow> rows, string phase, ExpressionSyntax expression, RowKind kind, int parent)
        {
            // A helper that builds a container, such as CreatureUpdate(): its returned expression.
            if (expression is InvocationExpressionSyntax call
                && Model(call).GetSymbolInfo(call).Symbol is IMethodSymbol helper
                && SymbolEqualityComparer.Default.Equals(helper.ContainingType, systems))
            {
                Add(rows, phase, Returned(helper) ?? throw new InvalidDataException($"SimulationSystems.{helper.Name}() returns nothing readable."), kind, parent);
                return;
            }

            var type = Concrete(expression);
            var arguments = expression is BaseObjectCreationExpressionSyntax { ArgumentList: { } list } ? list.Arguments : default;
            var stepKind = arguments.Count > 0 ? StepKind(type) : null;
            string name = Display(type);
            if (stepKind is null && arguments.Count > 0)
                name += "(" + string.Join(", ", arguments.Select(a => a.ToString())) + ")";

            int number = rows.Count + 1;
            rows.Add(new SimRow(number, phase, name, kind, parent, type, Entries(type, kind)));
            if (stepKind is { } steps)
                foreach (var argument in arguments)
                    Add(rows, phase, argument.Expression, steps, number);
        }

        /// <summary>The kind of step a container takes as its constructor's <c>params</c> array, if any.</summary>
        private RowKind? StepKind(INamedTypeSymbol type)
        {
            foreach (var constructor in type.InstanceConstructors)
            {
                if (constructor.Parameters is [{ IsParams: true, Type: IArrayTypeSymbol { ElementType: var element } }])
                {
                    if (SymbolEqualityComparer.Default.Equals(element, creatureStep)) return RowKind.CreatureStep;
                    if (SymbolEqualityComparer.Default.Equals(element, roomStep)) return RowKind.RoomStep;
                }
            }
            return null;
        }

        private IReadOnlyList<IMethodSymbol> Entries(INamedTypeSymbol type, RowKind kind)
        {
            string[] names = kind switch
            {
                RowKind.System => ["Execute"],
                RowKind.CreatureStep => ["BeginTurn", "Update", "EndTurn"],
                _ => ["BeginTurn", "Applies", "Tick", "EndTurn"],
            };
            var face = kind switch { RowKind.System => system, RowKind.CreatureStep => creatureStep, _ => roomStep };
            return names
                .Select(n => face.GetMembers(n).OfType<IMethodSymbol>().Single())
                .Select(m => type.FindImplementationForInterfaceMember(m) as IMethodSymbol
                             ?? throw new InvalidDataException($"{Display(type)} does not implement {face.Name}.{m.Name}."))
                .Select(m => m.OriginalDefinition)
                .ToArray();
        }

        /// <summary>The class an element creates. A property typed as an interface, such as
        /// <c>combat.States</c>, resolves to the object its declaring type's constructor assigns it.</summary>
        private INamedTypeSymbol Concrete(ExpressionSyntax expression)
        {
            var model = Model(expression);
            switch (expression)
            {
                case BaseObjectCreationExpressionSyntax:
                    return model.GetTypeInfo(expression).Type as INamedTypeSymbol
                           ?? throw new InvalidDataException($"'{expression}' creates no named type.");
                case IdentifierNameSyntax when model.GetSymbolInfo(expression).Symbol is ILocalSymbol local:
                    var declarator = local.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<VariableDeclaratorSyntax>().Single();
                    return Concrete(declarator.Initializer?.Value ?? throw new InvalidDataException($"local '{local.Name}' has no initialiser."));
                case MemberAccessExpressionSyntax when model.GetSymbolInfo(expression).Symbol is IPropertySymbol property:
                    if (property.Type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } concrete) return concrete;
                    return Assigned(property);
                default:
                    return model.GetTypeInfo(expression).Type as INamedTypeSymbol
                           ?? throw new InvalidDataException($"'{expression}' has no named type.");
            }
        }

        /// <summary>The type of the object <paramref name="property"/>'s getter creates (<c>=&gt; _x ??= new X()</c>),
        /// or a constructor of its type assigns to it (<c>States = new StateHandlers(this)</c>).</summary>
        private INamedTypeSymbol Assigned(IPropertySymbol property)
        {
            foreach (var syntax in property.DeclaringSyntaxReferences.Select(r => r.GetSyntax()))
            {
                var created = syntax.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>()
                    .Select(n => Model(n).GetTypeInfo(n).Type).OfType<INamedTypeSymbol>()
                    .Where(t => compilation.HasImplicitConversion(t, property.Type))
                    .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                    .ToArray();
                if (created.Length == 1) return created[0];
            }
            foreach (var constructor in property.ContainingType.InstanceConstructors)
            foreach (var syntax in constructor.DeclaringSyntaxReferences.Select(r => r.GetSyntax()))
            foreach (var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (SymbolEqualityComparer.Default.Equals(Model(assignment).GetSymbolInfo(assignment.Left).Symbol, property))
                    return Concrete(assignment.Right);
            }
            throw new InvalidDataException(
                $"{Display(property.ContainingType)}.{property.Name} is an interface no constructor assigns a new object to.");
        }
    }
}
