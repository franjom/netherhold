// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DK2.Tools.SimMatrix;

/// <summary>What one method does on its own: the fields, auto-properties and events it reads and writes,
/// the source methods it calls, and how many calls it makes through a delegate.</summary>
internal sealed class MethodSummary
{
    internal static readonly MethodSummary Empty = new();

    internal HashSet<ISymbol> Reads { get; } = new(SymbolEqualityComparer.Default);
    internal HashSet<ISymbol> Writes { get; } = new(SymbolEqualityComparer.Default);
    internal HashSet<IMethodSymbol> Calls { get; } = new(SymbolEqualityComparer.Default);
    internal int DelegateCalls { get; set; }

    /// <summary>Events and delegate fields this method invokes: emits, not writes, unless the field is an output.</summary>
    internal HashSet<ISymbol> Raises { get; } = new(SymbolEqualityComparer.Default);
}

/// <summary>Walks one method's operation tree, lambdas and local functions included, into a <see cref="MethodSummary"/>.</summary>
/// <remarks>A write is an assignment target, a compound or coalescing assignment, an increment or decrement,
/// a <c>ref</c> or <c>out</c> argument, a mutating call on a collection outside the source, or a non-readonly call on
/// a struct. A write into a struct, through an indexer or into an array element also writes what holds it. Not
/// writes: object initialisers, a constructor writing its own instance, and anything reached through a local that
/// holds an object the same method created.</remarks>
internal sealed class MethodWalker
{
    /// <summary>Methods outside the source that change their receiver.</summary>
    internal static readonly IReadOnlySet<string> MutatingCalls = new HashSet<string>(StringComparer.Ordinal)
    {
        "Add", "AddRange", "Insert", "InsertRange", "Remove", "RemoveAt", "RemoveAll", "RemoveRange", "RemoveWhere",
        "Clear", "Sort", "Reverse", "Push", "Pop", "TryPop", "Enqueue", "Dequeue", "TryDequeue", "TryAdd", "TryRemove",
        "Fill", "UnionWith", "ExceptWith", "IntersectWith", "SymmetricExceptWith", "Set", "SetAll", "EnsureCapacity",
        "TrimExcess", "GetOrAdd", "AddOrUpdate", "TryUpdate", "Replace", "Shuffle",
    };

    /// <summary>Static methods outside the source that write an argument, and that parameter's name.</summary>
    private static readonly IReadOnlyDictionary<string, string> WritingStatics = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["System.Array.Clear"] = "array", ["System.Array.Fill"] = "array", ["System.Array.Sort"] = "array",
        ["System.Array.Reverse"] = "array", ["System.Array.Copy"] = "destinationArray",
        ["System.Array.ConstrainedCopy"] = "destinationArray",
        ["System.Runtime.InteropServices.CollectionsMarshal.SetCount"] = "list",
        ["System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault"] = "dictionary",
    };

    private readonly IMethodSymbol _method;
    private readonly Func<IMethodSymbol, bool> _stop;
    private readonly Func<IMethodSymbol, ISymbol?> _sink;
    private readonly MethodSummary _summary = new();
    private readonly HashSet<ILocalSymbol> _fresh = new(SymbolEqualityComparer.Default);
    private readonly SourceIndex _index;

    private MethodWalker(IMethodSymbol method, SourceIndex index, Func<IMethodSymbol, bool> stop, Func<IMethodSymbol, ISymbol?> sink)
    {
        _method = method;
        _index = index;
        _stop = stop;
        _sink = sink;
    }

    /// <param name="stop">Calls not followed: other rows' entry points.</param>
    /// <param name="sink">For a call that emits an event, the key it writes; such a call is not followed.</param>
    internal static MethodSummary Summarise(
        IMethodSymbol method, SourceIndex index, Func<IMethodSymbol, bool> stop, Func<IMethodSymbol, ISymbol?> sink)
    {
        // An auto-property's accessor reached through an interface or a virtual call: the property itself.
        if (method.AssociatedSymbol is IPropertySymbol property && IsAutoProperty(property))
        {
            var summary = new MethodSummary();
            (method.MethodKind == MethodKind.PropertySet ? summary.Writes : summary.Reads).Add(property.OriginalDefinition);
            return summary;
        }

        var walker = new MethodWalker(method, index, stop, sink);
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            if (node is not (BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or ArrowExpressionClauseSyntax)) continue;
            if (index.Model(node.SyntaxTree).GetOperation(node) is not { } body) continue;
            walker.FindFresh(body);
            walker.Visit(body);
        }
        return walker._summary;
    }

    /// <summary>A source property with no accessor body: it reads and writes a hidden field.</summary>
    internal static bool IsAutoProperty(IPropertySymbol property)
    {
        if (property.IsAbstract || property.DeclaringSyntaxReferences.Length == 0) return false;
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } accessors }) return false;
            if (accessors.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null)) return false;
        }
        return true;
    }

    /// <summary>Locals declared with an object the method creates and never assigned anything else.</summary>
    private void FindFresh(IOperation body)
    {
        foreach (var declarator in body.Descendants().OfType<IVariableDeclaratorOperation>())
            if (IsCreation(declarator.Initializer?.Value))
                _fresh.Add(declarator.Symbol);
        foreach (var assignment in body.Descendants().OfType<ISimpleAssignmentOperation>())
            if (Unwrap(assignment.Target) is ILocalReferenceOperation local && !IsCreation(assignment.Value))
                _fresh.Remove(local.Local);
    }

    private bool IsCreation(IOperation? value) =>
        Unwrap(value) is IObjectCreationOperation or IArrayCreationOperation
            or ICollectionExpressionOperation or IAnonymousObjectCreationOperation;

    private void Visit(IOperation? operation)
    {
        switch (operation)
        {
            case null:
                return;
            case ISimpleAssignmentOperation assignment:
                Write(assignment.Target, read: false);
                Visit(assignment.Value);
                return;
            case ICompoundAssignmentOperation compound:
                Write(compound.Target, read: true);
                Call(compound.OperatorMethod);
                Visit(compound.Value);
                return;
            case ICoalesceAssignmentOperation coalesce:
                Write(coalesce.Target, read: true);
                Visit(coalesce.Value);
                return;
            case IIncrementOrDecrementOperation step:
                Write(step.Target, read: true);
                Call(step.OperatorMethod);
                return;
            case IDeconstructionAssignmentOperation deconstruction:
                Write(deconstruction.Target, read: false);
                Visit(deconstruction.Value);
                return;
            case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument:
                Write(argument.Value, read: argument.Parameter.RefKind == RefKind.Ref);
                return;
            case IFieldReferenceOperation field:
                Read(Key(field.Field));
                Visit(field.Instance);
                return;
            case IPropertyReferenceOperation property:
                ReadProperty(property);
                return;
            case IEventReferenceOperation e:
                Read(e.Event.OriginalDefinition);
                Visit(e.Instance);
                return;
            case IEventAssignmentOperation subscription:
                Write(subscription.EventReference, read: true);
                Visit(subscription.HandlerValue);
                return;
            case IInvocationOperation invocation:
                Invocation(invocation);
                return;
            case IConditionalAccessOperation conditional:
                Visit(conditional.Operation);
                Visit(conditional.WhenNotNull);
                return;
            case IConditionalAccessInstanceOperation:
                return;
            case IObjectCreationOperation creation:
                Call(creation.Constructor);
                foreach (var argument in creation.Arguments) Visit(argument);
                Visit(creation.Initializer);
                return;
            case IMethodReferenceOperation reference:
                Call(reference.Method);
                Visit(reference.Instance);
                return;
            case IUnaryOperation { OperatorMethod: { } unary } u:
                Call(unary);
                Visit(u.Operand);
                return;
            case IBinaryOperation { OperatorMethod: { } binary } b:
                Call(binary);
                Visit(b.LeftOperand);
                Visit(b.RightOperand);
                return;
            case IConversionOperation { OperatorMethod: { } conversion } c:
                Call(conversion);
                Visit(c.Operand);
                return;
            default:
                foreach (var child in operation.ChildOperations) Visit(child);
                return;
        }
    }

    private void ReadProperty(IPropertyReferenceOperation reference)
    {
        var property = reference.Property.OriginalDefinition;
        if (IsAutoProperty(property)) Read(property);
        else if (property.GetMethod is { } getter) Call(getter);
        Visit(reference.Instance);
        foreach (var argument in reference.Arguments) Visit(argument);
    }

    private void Invocation(IInvocationOperation invocation)
    {
        var target = invocation.TargetMethod;
        var definition = SourceIndex.Definition(target);
        if (target.MethodKind == MethodKind.DelegateInvoke)
        {
            _summary.DelegateCalls++;
            // Raising an event, or calling a delegate field: an emit through it, not a write of it.
            if (Unwrap(invocation.Instance) is IEventReferenceOperation raised) _summary.Raises.Add(raised.Event.OriginalDefinition);
            else if (Unwrap(invocation.Instance) is IFieldReferenceOperation callback) _summary.Raises.Add(Key(callback.Field));
        }
        else if (_sink(definition) is { } key) _summary.Writes.Add(key);
        else if (target.MethodKind != MethodKind.LocalFunction && !_stop(definition))
        {
            if (SourceIndex.IsSource(definition))
            {
                _summary.Calls.Add(definition);
                if (MutatesStruct(invocation)) Write(invocation.Instance!, read: true);
            }
            else if (invocation.Instance is { } receiver
                     && ((MutatingCalls.Contains(target.Name) && IsCollection(receiver.Type)) || MutatesStruct(invocation)))
                Write(receiver, read: true);
            else if (target.IsStatic && WritingStatics.TryGetValue($"{target.ContainingType.ToDisplayString()}.{target.Name}", out string? parameter)
                     && invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == parameter) is { } written)
                Write(written.Value, read: true);
            else if (target.Name == "CopyTo" && invocation.Arguments.Length > 0 && IsCollection(invocation.Instance?.Type))
                Write(invocation.Arguments[0].Value, read: false);
        }
        Visit(invocation.Instance);
        foreach (var argument in invocation.Arguments) Visit(argument);
    }

    /// <summary>A non-readonly method of a struct, called on a struct: not one an enum inherits from
    /// <c>System.Enum</c> (<c>HasFlag</c>, <c>ToString</c>), nor a primitive's.</summary>
    private static bool MutatesStruct(IInvocationOperation invocation) =>
        invocation.Instance is { Type: { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None } } and not IInstanceReferenceOperation
        && invocation.TargetMethod.ContainingType.TypeKind == TypeKind.Struct
        && !invocation.TargetMethod.IsStatic
        && !invocation.TargetMethod.IsReadOnly
        && !invocation.TargetMethod.ContainingType.IsReadOnly;

    /// <summary>A receiver the mutating names apply to: a class or interface, or a span; not a string, an enum
    /// or a value like <c>DateTime</c> whose <c>Add</c> returns a new one.</summary>
    private static bool IsCollection(ITypeSymbol? type) =>
        type is { SpecialType: SpecialType.None } && type.TypeKind != TypeKind.Enum && (!type.IsValueType || type.IsRefLikeType);

    private void Call(IMethodSymbol? method)
    {
        if (method is null) return;
        var definition = SourceIndex.Definition(method);
        if (SourceIndex.IsSource(definition) && !_stop(definition)) _summary.Calls.Add(definition);
    }

    /// <summary>Records <paramref name="target"/> as written, and whatever holds it when it is a struct's field,
    /// an indexer's or an array's element.</summary>
    private void Write(IOperation target, bool read)
    {
        switch (Unwrap(target))
        {
            case IFieldReferenceOperation field:
                Record(Key(field.Field), field.Instance, read);
                WriteHolder(field.Instance);
                return;
            case IPropertyReferenceOperation property:
                var definition = property.Property.OriginalDefinition;
                if (IsAutoProperty(definition))
                    Record(definition, property.Instance, read);
                else if (SourceIndex.IsSource(definition) && !property.Property.IsIndexer)
                {
                    // `List<T> Raw => _list;`: a mutation through the accessor writes the field it returns.
                    if (definition.SetMethod is null && Returned(definition) is { } returned) Record(returned, property.Instance, read);
                    Call(definition.SetMethod);
                    Call(definition.GetMethod);
                }
                else if (SourceIndex.IsSource(definition))
                {
                    Call(definition.SetMethod);
                    if (read) Call(definition.GetMethod);
                    if (property.Instance is { } indexed) Write(indexed, read: true);
                }
                else if (property.Instance is { } holder)
                    Write(holder, read: true);
                if (IsAutoProperty(definition) || !property.Property.IsIndexer) WriteHolder(property.Instance);
                else Visit(property.Instance);
                foreach (var argument in property.Arguments) Visit(argument);
                return;
            case IArrayElementReferenceOperation element:
                Write(element.ArrayReference, read: true);
                foreach (var index in element.Indices) Visit(index);
                return;
            case IEventReferenceOperation e:
                Record(e.Event.OriginalDefinition, e.Instance, read);
                Visit(e.Instance);
                return;
            case ITupleOperation tuple:
                foreach (var element in tuple.Elements) Write(element, read);
                return;
            case IInvocationOperation invocation when invocation.TargetMethod.ReturnsByRef:
                Visit(invocation);
                return;
            case IInvocationOperation { TargetMethod.Name: "AsSpan" or "AsMemory" } span:
                if (span.Instance is { } spanned) Write(spanned, read: true);
                else if (span.Arguments.Length > 0) Write(span.Arguments[0].Value, read: true);
                return;
            case var other:
                Visit(other);
                return;
        }
    }

    /// <summary>A write into a struct writes the field or element that holds it; one into a class does not.</summary>
    private void WriteHolder(IOperation? instance)
    {
        if (instance is { Type.IsValueType: true } and not IInstanceReferenceOperation) Write(instance, read: true);
        else Visit(instance);
    }

    /// <summary>The field or auto-property a get-only property's expression body returns from its own instance.</summary>
    private ISymbol? Returned(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            var body = reference.GetSyntax() switch
            {
                PropertyDeclarationSyntax { ExpressionBody: { } arrow } => arrow.Expression,
                PropertyDeclarationSyntax { AccessorList.Accessors: [{ Keyword.ValueText: "get", ExpressionBody: { } getter }] } => getter.Expression,
                _ => null,
            };
            if (body is null) continue;
            switch (Unwrap(_index.Model(body.SyntaxTree).GetOperation(body)))
            {
                case IFieldReferenceOperation { Instance: null or IInstanceReferenceOperation } field:
                    return Key(field.Field);
                case IPropertyReferenceOperation { Instance: null or IInstanceReferenceOperation } auto when IsAutoProperty(auto.Property.OriginalDefinition):
                    return auto.Property.OriginalDefinition;
            }
        }
        return null;
    }

    private void Record(ISymbol key, IOperation? instance, bool read)
    {
        if (read) Read(key);
        if (!Counts(key, instance)) return;
        _summary.Writes.Add(key);
    }

    /// <summary>False for an object initialiser, a constructor writing its own instance or statics, and a write
    /// through a local that holds an object this method created.</summary>
    private bool Counts(ISymbol key, IOperation? instance)
    {
        var holder = Unwrap(instance);
        if (holder is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver }) return false;
        if (_method.MethodKind == MethodKind.Constructor
            && OwnRoot(holder) is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
            return false;
        var root = Root(holder);
        if (_method.MethodKind == MethodKind.StaticConstructor && holder is null
            && SymbolEqualityComparer.Default.Equals(key.ContainingType, _method.ContainingType))
            return false;
        return root is not ILocalReferenceOperation local || !_fresh.Contains(local.Local);
    }

    /// <summary>What a write lands in, through structs and arrays only: <c>Spot.X</c> and <c>Spots[0].X</c> are
    /// the instance's own, <c>_world.Tick</c> is not.</summary>
    private IOperation? OwnRoot(IOperation? operation)
    {
        for (int depth = 0; depth < 64; depth++)
        {
            switch (Unwrap(operation))
            {
                case IFieldReferenceOperation { Type.IsValueType: true, Instance: { } inner }: operation = inner; break;
                case IPropertyReferenceOperation { Type.IsValueType: true, Instance: { } inner }: operation = inner; break;
                case IArrayElementReferenceOperation element:
                    operation = Unwrap(element.ArrayReference) is IFieldReferenceOperation { Instance: var owner } ? owner : null;
                    break;
                case var other: return other;
            }
        }
        return null;
    }

    private IOperation? Root(IOperation? operation)
    {
        for (int depth = 0; ; depth++)
        {
            if (depth == 64) return null;
            switch (Unwrap(operation))
            {
                case IFieldReferenceOperation { Instance: { } inner }: operation = inner; break;
                case IPropertyReferenceOperation { Instance: { } inner }: operation = inner; break;
                case IArrayElementReferenceOperation element: operation = element.ArrayReference; break;
                case var other: return other;
            }
        }
    }

    private void Read(ISymbol key) => _summary.Reads.Add(key);

    /// <summary>A field's key: the property it backs, when it is an auto-property's or a <c>field</c> keyword's.</summary>
    private static ISymbol Key(IFieldSymbol field) => (field.AssociatedSymbol ?? field).OriginalDefinition;

    /// <summary>What <c>x?.</c> stands for: the receiver of the conditional access whose right-hand side holds it.</summary>
    private static IOperation? Receiver(IConditionalAccessInstanceOperation instance)
    {
        IOperation child = instance;
        for (var parent = instance.Parent; parent is not null; child = parent, parent = parent.Parent)
            if (parent is IConditionalAccessOperation conditional && ReferenceEquals(conditional.WhenNotNull, child))
                return conditional.Operation;
        return null;
    }

    private IOperation? Unwrap(IOperation? operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation { OperatorMethod: null } conversion: operation = conversion.Operand; break;
                case IParenthesizedOperation parenthesized: operation = parenthesized.Operand; break;
                case IConditionalAccessInstanceOperation instance: operation = Receiver(instance); break;
                default: return operation;
            }
        }
    }
}
