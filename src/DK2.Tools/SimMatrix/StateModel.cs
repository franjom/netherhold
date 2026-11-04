// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Microsoft.CodeAnalysis;

namespace DK2.Tools.SimMatrix;

/// <summary>Where a field sits: one of the matrix's columns, owned by a system, or not state.</summary>
internal enum FieldPlace
{
    NotState,
    Column,
    SystemOwned,
}

/// <summary>The world state the matrix tracks, grouped into columns. A state type is a source type
/// reachable from <c>WorldState</c> or <c>SimulationContext</c> through instance fields (type arguments,
/// array elements and every source implementation of an interface or abstract type included), outside
/// <c>DK2.Data.Model</c> and not an enum. A type takes the column of the nearest root member it is reached
/// through, or the column <see cref="TypeColumns"/> names for it.</summary>
internal sealed class StateModel
{
    internal static readonly string[] Columns =
    [
        "Tiles", "Creatures", "Players", "Rooms", "Objects & shots", "Doors & traps", "Heroes",
        "Level script", "Navigation", "Caches & scratch", "World", "RNG", "Commands in", "Events out",
    ];

    internal const int Tiles = 0, Creatures = 1, Players = 2, Rooms = 3, Objects = 4, DoorsTraps = 5, Heroes = 6,
        LevelScript = 7, Navigation = 8, Caches = 9, World = 10, Rng = 11, CommandsIn = 12, EventsOut = 13;

    /// <summary>Excluded: neither a column nor followed into.</summary>
    internal const int Excluded = -1;

    internal const string WorldStateType = "DK2.Simulation.WorldState";
    internal const string ContextType = "DK2.Simulation.SimulationContext";
    internal const string TileStateType = "DK2.Simulation.TileState";

    /// <summary>The first of <c>TileState</c>'s fields that belong to the room anchored on the tile; it and
    /// every field declared after it are Rooms, not Tiles (<c>RoomInstance</c> is derived and remembers nothing).</summary>
    internal const string FirstRoomFieldOfTile = "EntranceIdleTicks";

    /// <summary>The column of each <c>WorldState</c> member. A member missing here falls into World and is listed
    /// as unmapped, which a test fails on.</summary>
    internal static readonly IReadOnlyDictionary<string, int> WorldMembers = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["_tiles"] = Tiles, ["_pendingWork"] = Tiles, ["_nextTileLinkOrder"] = Tiles, ["TileHighlights"] = Tiles,
        ["NextSlabOrder"] = Tiles, ["NextTagOrder"] = Tiles, ["NextPlaceClaim"] = Tiles,
        ["_creatures"] = Creatures, ["CreatureSlots"] = Creatures, ["_creaturesByOwnership"] = Creatures,
        ["_nextCreatureId"] = Creatures, ["_nextChainOrder"] = Creatures,
        ["_players"] = Players, ["ActivePlayerOrderSlots"] = Players, ["Messages"] = Players, ["Carry"] = Players,
        ["LocalPlayerId"] = Players,
        ["RoomBuildWaveSlots"] = Rooms, ["_templeSacrifices"] = Rooms, ["_graveyards"] = Rooms,
        ["RoomBorderCursorList"] = Rooms, ["LevelStartWorkshops"] = Rooms, ["NextRoomListPosition"] = Rooms,
        ["_objects"] = Objects, ["_nextObjectId"] = Objects, ["_shots"] = Objects, ["_nextShotId"] = Objects,
        ["_effectGenerators"] = Objects,
        ["_doors"] = DoorsTraps, ["_traps"] = DoorsTraps, ["SecretDoorEntries"] = DoorsTraps,
        ["_heroParties"] = Heroes, ["HeroSlotArray"] = Heroes,
        ["_actionPoints"] = LevelScript, ["Triggers"] = LevelScript,
        ["_navStamps"] = Navigation,
        ["StructureCache"] = Caches, ["RoomSweepCache"] = Caches,
        ["TremorSlots"] = World, ["GameMode"] = World, ["CurrentTick"] = World, ["FogOfWarEnabled"] = World,
        ["Width"] = World, ["Height"] = World,
        ["_creatureIdentityRandom"] = Rng, ["RoomConstructionRandom"] = Rng,
        ["RoomTilesChanged"] = EventsOut, ["HeartHealthChanged"] = EventsOut, ["GameEnded"] = EventsOut,
        ["ObjectMade"] = EventsOut, ["ObjectRemoving"] = EventsOut,
    };

    /// <summary>The column of each <c>SimulationContext</c> member. A plumbing member's own reads are not
    /// counted, but the type it holds is still reached in its column (<see cref="PlumbingMembers"/>).</summary>
    internal static readonly IReadOnlyDictionary<string, int> ContextMembers = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["World"] = World, ["Rng"] = Rng, ["Navigation"] = Navigation, ["Events"] = EventsOut, ["Triggers"] = LevelScript,
        ["Database"] = Excluded, ["_profiler"] = Excluded, ["_requestEffect"] = Excluded, ["_requestSound"] = Excluded,
        ["Tick"] = World,
        ["_input"] = CommandsIn, ["_commands"] = CommandsIn,
        ["TremorRecoveryCompletedThisTick"] = Caches, ["TremorMoodDispatchedThisTick"] = Caches,
        ["AngryChooserRanThisTick"] = Caches, ["IdledByCombatHandlerThisTick"] = Caches,
        ["ReinforcementActionsPolledThisTick"] = Caches, ["WorkerPreludeProcessedThisTick"] = Caches,
        ["WorkerPreludeClaimedThisTick"] = Caches, ["TremorRebellionStartedThisTick"] = Caches,
        ["UpdateStartStateId"] = Caches,
    };

    /// <summary><c>SimulationContext</c> members that only hand out a reference: reading them is plumbing.</summary>
    internal static readonly IReadOnlySet<string> PlumbingMembers =
        new HashSet<string>(["World", "Database", "Rng", "Navigation", "Events", "Triggers", "_profiler", "_requestEffect", "_requestSound"], StringComparer.Ordinal);

    /// <summary>Types whose column is fixed whatever reaches them, by simple name.</summary>
    internal static readonly IReadOnlyDictionary<string, int> TypeColumns = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["TunnelRecord"] = Heroes,
        ["TriggerExecutor"] = LevelScript, ["TriggerRun"] = LevelScript, ["TriggerState"] = LevelScript,
        ["TriggerTree"] = LevelScript, ["ActionPointState"] = LevelScript,
        ["NavMesh"] = Navigation, ["MeshRouter"] = Navigation, ["WorldSteering"] = Navigation,
        ["SteeringBody"] = Navigation, ["GateRecord"] = Navigation, ["NavStamp"] = Navigation,
        ["RoomIndex"] = Caches, ["StructureBlocks"] = Caches, ["FunnelBuffers"] = Caches, ["RoomInstance"] = Caches,
        ["SimRandom"] = Rng, ["Xoshiro256StarStar"] = Rng,
        ["InputFrame"] = CommandsIn, ["CommandIndex"] = CommandsIn, ["PlayerCommand"] = CommandsIn,
        ["StateEntryLog"] = Excluded, ["SimulationMarker"] = Excluded, ["SimulationMarkers"] = Excluded,
        ["ISimulationProfiler"] = Excluded, ["ISimulationEventSink"] = EventsOut,
    };

    private readonly Dictionary<INamedTypeSymbol, int> _typeColumn = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<int>> _reachedFrom = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> _systemTypes = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> _systemOwnedTypes = new(SymbolEqualityComparer.Default);
    private readonly SourceIndex _index;
    private readonly INamedTypeSymbol _world, _context, _tile;
    private readonly int _firstRoomField;

    internal StateModel(SourceIndex index)
    {
        _index = index;
        var compilation = index.Compilation;
        _world = SimRows.Type(compilation, WorldStateType);
        _context = SimRows.Type(compilation, ContextType);
        _tile = SimRows.Type(compilation, TileStateType);
        _firstRoomField = _tile.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst)
            .Select((f, i) => (f, i)).Single(x => x.f.Name == FirstRoomFieldOfTile).i;
        var system = SimRows.Type(compilation, SimRows.SystemInterface);

        // Roots: WorldState's members in declared order, then SimulationContext's. The walk is breadth first from
        // every root at once, so a type takes the column of its nearest root; ties at that depth are all kept,
        // and the first in root order is its column.
        var roots = new List<(INamedTypeSymbol Type, int Column)>();
        foreach (var (member, type) in InstanceMembers(_world))
        {
            if (!WorldMembers.TryGetValue(member.Name, out int column))
            {
                Unmapped.Add($"WorldState.{member.Name}");
                column = World;
            }
            roots.AddRange(Components(type).Select(t => (t, column)));
        }
        foreach (var (member, type) in InstanceMembers(_context))
        {
            if (!ContextMembers.TryGetValue(member.Name, out int column))
            {
                Unmapped.Add($"SimulationContext.{member.Name}");
                column = World;
            }
            if (column == Excluded || member.Name == "World") continue;
            roots.AddRange(Components(type).Select(t => (t, column)));
        }
        var depth = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        var level = roots;
        for (int d = 1; level.Count > 0; d++)
        {
            var next = new List<(INamedTypeSymbol Type, int Column)>();
            foreach (var (type, reachedColumn) in level)
            {
                if (SymbolEqualityComparer.Default.Equals(type, _world) || SymbolEqualityComparer.Default.Equals(type, _context)) continue;
                int column = TypeColumns.TryGetValue(type.Name, out int fixedColumn) ? fixedColumn : reachedColumn;
                if (depth.TryGetValue(type, out int seen))
                {
                    // A tie is recorded, not followed: what the type holds goes with its first column.
                    if (seen == d && !_reachedFrom[type].Contains(column)) _reachedFrom[type].Add(column);
                    continue;
                }
                else
                {
                    depth[type] = d;
                    _reachedFrom[type] = [column];
                }
                if (column == Excluded) continue;
                foreach (var (_, fieldType) in InstanceMembers(type))
                    next.AddRange(Components(fieldType).Select(t => (t, column)));
                next.AddRange(_index.Implementations(type).Select(t => (t, column)));
            }
            level = next;
        }
        foreach (var (type, from) in _reachedFrom)
            _typeColumn[type] = from[0];

        // Systems, and the source types their instance fields reach that are not state.
        foreach (var type in index.Types.Where(t => t.TypeKind == TypeKind.Class && t.AllInterfaces.Contains(system, SymbolEqualityComparer.Default)))
            _systemTypes.Add(type);
        var owned = new Queue<(INamedTypeSymbol, int)>();
        foreach (var type in _systemTypes)
            foreach (var (_, fieldType) in InstanceMembers(type))
                foreach (var reached in Components(fieldType))
                    owned.Enqueue((reached, 0));
        while (owned.TryDequeue(out var item))
        {
            var (type, _) = item;
            if (_typeColumn.ContainsKey(type) || SymbolEqualityComparer.Default.Equals(type, _world)
                || SymbolEqualityComparer.Default.Equals(type, _context) || !_systemOwnedTypes.Add(type)) continue;
            foreach (var (_, fieldType) in InstanceMembers(type))
                foreach (var reached in Components(fieldType))
                    owned.Enqueue((reached, 0));
            foreach (var derived in _index.Implementations(type))
                owned.Enqueue((derived, 0));
        }
    }

    /// <summary>Root members neither table names; each falls into World.</summary>
    internal List<string> Unmapped { get; } = [];

    /// <summary>Every state type and its column, <see cref="Excluded"/> included.</summary>
    internal IReadOnlyDictionary<INamedTypeSymbol, int> TypeColumn => _typeColumn;

    /// <summary>State types reached from more than one column at their nearest depth. A value type among them is
    /// shared: its own fields count for nothing, and a write into one counts as a write of the field that holds it.</summary>
    internal IEnumerable<(INamedTypeSymbol Type, IReadOnlyCollection<int> Columns)> MultiColumn =>
        _reachedFrom.Where(p => p.Value.Count > 1 && !TypeColumns.ContainsKey(p.Key.Name)).Select(p => (p.Key, (IReadOnlyCollection<int>)p.Value));

    /// <summary>A value type that counts through the field holding it, not in a column of its own: one declared
    /// outside <c>DK2.Simulation</c> (a position, an angle, a tick), or one several columns reach at the same depth.</summary>
    internal bool IsShared(INamedTypeSymbol type) =>
        type.IsValueType && _typeColumn.ContainsKey(type) && !TypeColumns.ContainsKey(type.Name)
        && (_reachedFrom[type].Count > 1 || !type.ToDisplayString().StartsWith("DK2.Simulation.", StringComparison.Ordinal));

    internal bool IsSystemType(INamedTypeSymbol type) =>
        _systemTypes.Contains(type) || (type.ContainingType is { } outer && IsSystemType(outer));

    internal bool IsExcludedType(INamedTypeSymbol type) =>
        _typeColumn.TryGetValue(type, out int c) && c == Excluded
        || TypeColumns.TryGetValue(type.Name, out int fixedColumn) && fixedColumn == Excluded;

    /// <summary>Where a field, auto-property or event lives, and its column.</summary>
    /// <param name="writtenStatics">Static fields some method writes: only those are state.</param>
    internal (FieldPlace Place, int Column) Place(ISymbol member, IReadOnlySet<ISymbol> writtenStatics)
    {
        var owner = member.ContainingType;
        if (member.IsStatic && !writtenStatics.Contains(member)
            && (SymbolEqualityComparer.Default.Equals(owner, _world) || SymbolEqualityComparer.Default.Equals(owner, _context)))
            return (FieldPlace.NotState, 0);
        if (SymbolEqualityComparer.Default.Equals(owner, _world))
            return WorldMembers.TryGetValue(member.Name, out int c) ? Columned(c) : (FieldPlace.Column, World);
        if (SymbolEqualityComparer.Default.Equals(owner, _context))
        {
            if (PlumbingMembers.Contains(member.Name)) return (FieldPlace.NotState, 0);
            return ContextMembers.TryGetValue(member.Name, out int c) ? Columned(c) : (FieldPlace.Column, World);
        }
        if (IsSystemType(owner)) return (FieldPlace.SystemOwned, 0);
        if (member.IsStatic)
        {
            if (!writtenStatics.Contains(member)) return (FieldPlace.NotState, 0);
            if (_systemOwnedTypes.Contains(owner)) return (FieldPlace.SystemOwned, 0);
            return _typeColumn.TryGetValue(owner, out int c) && c != Excluded ? (FieldPlace.Column, c)
                : IsExcludedType(owner) ? (FieldPlace.NotState, 0) : (FieldPlace.Column, Caches);
        }
        if (SymbolEqualityComparer.Default.Equals(owner, _tile))
        {
            int order = Array.IndexOf(_tile.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst).ToArray(), member);
            return (FieldPlace.Column, order >= _firstRoomField ? Rooms : Tiles);
        }
        if (_systemOwnedTypes.Contains(owner)) return (FieldPlace.SystemOwned, 0);
        if (!_typeColumn.TryGetValue(owner, out int column)) return (FieldPlace.NotState, 0);
        if (IsShared(owner)) return (FieldPlace.NotState, 0);
        return Columned(column);
    }

    private static (FieldPlace, int) Columned(int column) => column == Excluded ? (FieldPlace.NotState, 0) : (FieldPlace.Column, column);

    /// <summary>A type's instance fields and the fields behind its auto-properties, its source base types' included.</summary>
    internal static IEnumerable<(ISymbol Member, ITypeSymbol Type)> InstanceMembers(INamedTypeSymbol type)
    {
        for (var t = type; t is not null && t.DeclaringSyntaxReferences.Length > 0; t = t.BaseType)
        {
            foreach (var member in t.GetMembers())
            {
                if (member.IsStatic) continue;
                switch (member)
                {
                    case IFieldSymbol { IsConst: false } field:
                        yield return (field.AssociatedSymbol ?? field, field.Type);
                        break;
                    case IEventSymbol e when !e.IsAbstract:
                        yield return (e, e.Type);
                        break;
                }
            }
        }
    }

    /// <summary>The source types a field of <paramref name="type"/> can hold: the type, its type arguments,
    /// array elements and nullable underlying types; not enums and not <c>DK2.Data.Model</c>.</summary>
    private IEnumerable<INamedTypeSymbol> Components(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (var t in Components(array.ElementType)) yield return t;
                break;
            case INamedTypeSymbol named:
                foreach (var argument in named.TypeArguments)
                    foreach (var t in Components(argument)) yield return t;
                if (named.TypeKind != TypeKind.Enum && named.TypeKind != TypeKind.Delegate && _index.IsStateSource(named.OriginalDefinition))
                    yield return named.OriginalDefinition;
                break;
        }
    }
}
