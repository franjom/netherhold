// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A system whose work is one room at a time: its part of DK2's room tick
/// (<c>Room_Tick</c>, <c>0x004E4B00</c>). Inside a <see cref="RoomTickSystem"/> it runs for each room
/// it <see cref="Applies"/> to between the steps declared before and after it; run alone, it sweeps the
/// rooms itself in the same order (<see cref="RoomTickSystem.RunAlone"/>).</summary>
public interface IRoomTickStep : ISimulationSystem
{
    /// <summary>Once a tick, before the first room's tick: what the step reads once for the whole turn.</summary>
    void BeginTurn(SimulationContext context) { }

    /// <summary>Whether this step does anything for this room: its type's handler, or every room's.</summary>
    bool Applies(SimulationContext context, RoomInstance room);

    /// <summary>This step's part of one room's tick.</summary>
    void Tick(SimulationContext context, RoomInstance room);

    /// <summary>Once a tick, after the last room's tick.</summary>
    void EndTurn(SimulationContext context) { }
}

/// <summary>DK2's room list in the per-turn frame (<c>0x004EC450</c>): the room manager links each new
/// room at the head of its in-use list (<c>0x004EC490</c>, <c>0x004EC559..0x004EC57E</c>) and the walk
/// goes from the head along each room's <c>+0x21</c>, so every player's rooms tick newest first, all
/// types together, one room's whole tick before the next one's. A room's tick is its type's handler
/// (slot <c>+0x15</c> of the table <c>0x006A1E9F</c>), then the border step <c>0x004E7CD0</c> and the
/// health step (<c>0x004E4B00</c>). The steps run in the order they are declared, which is that order
/// (workshop-timers.md section 4).</summary>
/// <remarks>The list is the derived rooms by <see cref="RoomListPosition"/>, newest first, ties by
/// lowest tile. A rival's capture gives a room a new position here where DK2's list does not move it.
/// The rooms are those standing when the walk begins.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/workshop-timers.md")]
public sealed class RoomTickSystem : ISimulationSystem
{
    private readonly IRoomTickStep[] _steps;
    private readonly List<RoomInstance> _rooms = [];
    private readonly Comparison<RoomInstance> _newestFirst;
    private WorldState? _world;

    public RoomTickSystem(params IRoomTickStep[] steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _newestFirst = (a, b) => Compare(_world!, a, b);
    }

    public string Name => "room ticks";

    /// <summary>The steps, in the order each room takes them.</summary>
    public IReadOnlyList<IRoomTickStep> Steps => _steps;

    public void Execute(SimulationContext context)
    {
        foreach (IRoomTickStep step in _steps) step.BeginTurn(context);
        _world = context.World;
        _rooms.Clear();
        _rooms.AddRange(RoomIndex.Build(context.World, context.Database));
        _rooms.Sort(_newestFirst);
        foreach (RoomInstance room in _rooms)
            foreach (IRoomTickStep step in _steps)
                if (step.Applies(context, room)) step.Tick(context, room);
        foreach (IRoomTickStep step in _steps) step.EndTurn(context);
    }

    /// <summary>Steps on their own, over the whole room list in the walk's order: what a step's
    /// <see cref="ISimulationSystem.Execute"/> does in a host that runs it without the others.</summary>
    public static void RunAlone(SimulationContext context, params IRoomTickStep[] steps) =>
        new RoomTickSystem(steps).Execute(context);

    /// <summary>Every room, newest first: <see cref="RoomListPosition"/> descending, then lowest tile
    /// descending, as <see cref="RoomLists.Of"/> orders a player's rooms.</summary>
    public static List<RoomInstance> NewestFirst(WorldState world, GameDatabase database)
    {
        var rooms = new List<RoomInstance>(RoomIndex.Build(world, database));
        rooms.Sort((a, b) => Compare(world, a, b));
        return rooms;
    }

    private static int Compare(WorldState world, RoomInstance a, RoomInstance b)
    {
        int byAge = world.TileAtIndex(b.TileIndices[0]).RoomListPosition.Value
            .CompareTo(world.TileAtIndex(a.TileIndices[0]).RoomListPosition.Value);
        return byAge != 0 ? byAge : b.TileIndices[0].CompareTo(a.TileIndices[0]);
    }
}
