// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>A manufacturer's states in its keeper's Workshop (<c>workshop-states.md</c>, ADR-0404): 63
/// finds a free machine and walks to it or beside it, 64 faces it and starts action 32, 65 works it
/// and chooses again. The credit hook runs under all three (<see cref="Systems.CreatureJobSystem"/>).</summary>
/// <remarks>Ours: a state entered this turn runs from the next. The machines' process states,
/// effects and sounds are presentation and not kept.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/workshop-states.md")]
public static class WorkshopStep
{
    /// <summary>63 (<c>0x00470110</c>): the chooser.</summary>
    public const int ChoosingState = 63;

    /// <summary>64 (<c>0x00470470</c>): at the machine, facing it, then the work.</summary>
    public const int AtTheMachineState = 64;

    /// <summary>65 (<c>0x00470860</c>): working.</summary>
    public const int WorkingState = 65;

    /// <summary>The machines: 15 (<i>Anvil</i>), 16 and 46, in 64's search order 46, 15, 16.</summary>
    public const byte Anvil = 15, Machine16 = 16, Machine46 = 46;

    /// <summary>Action 32's art slot (record <c>0x006E4538</c> <c>+0x14</c>).</summary>
    public const int WorkArtSlot = 22;

    /// <summary>The modes of state <c>+0xB9</c>: at the machine, and at a stand point beside it.</summary>
    public const byte AtIt = 1, BesideIt = 2;

    /// <summary><c>0x006BC350</c>: north, east, south, west.</summary>
    private static readonly (int Dx, int Dy)[] Sides = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>70 (<c>0x00470C10</c>): back into a Workshop that changed.</summary>
    public const int ReturningState = 70;

    public static bool IsWorkshopState(int state) => state is ChoosingState or AtTheMachineState or WorkingState or ReturningState;

    public static bool IsMachine(byte type) => type is Anvil or Machine16 or Machine46;

    /// <summary>One turn of a manufacturer. False when it is left idle, which ends the job.</summary>
    public static bool Turn(SimulationContext context, string system, CreatureState creature)
    {
        if (RoomEvictRules.Evict(context, system, creature, RoomTypeId.Workshop)) return true;
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.HasRoute) return true;
            creature.StopMoving();
            return StateShadow.Arrive(context, system, creature);
        }

        long age = context.Tick.Value - creature.StateEnteredTick;
        if (age == 0) return true;
        return creature.Dk2StateId switch
        {
            ChoosingState => Choose(context, system, creature, age),
            AtTheMachineState => AtTheMachine(context, system, creature),
            WorkingState => Work(context, system, creature, age),
            ReturningState => RoomEvictRules.BackIn(context, system, creature, RoomTypeId.Workshop, ChoosingState, endsAction: true),
            _ => false,
        };
    }

    // ----------------------------------------------------------------------------- 63

    /// <summary>63 (workshop-states.md §4): with no order, action 43 once and then idle; off its own
    /// Workshop it stays; otherwise the finder's machine, at it (mode 1) or beside it (mode 2).</summary>
    private static bool Choose(SimulationContext context, string system, CreatureState creature, long age)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        long now = context.Tick.Value;
        if (age == 1) creature.StopMoving();
        if (world.Player(creature.OwnerPlayerId).ManufactureQueue.Count == 0)
        {
            if (age == 1)
                creature.ActionUntilTick = now + AnimationTimingRules.CountedHoldTicks(database.CreatureById(creature.CreatureTypeId), 20, 0);
            return now < creature.ActionUntilTick;
        }

        if (OwnWorkshopUnder(world, database, creature) is null) return true;
        if (!creature.HeldPlace.IsHeld || RoomIndex.RoomAt(world, database, creature.HeldPlace.TileIndex) is not { } held)
            return true;
        if (Find(world, database, held, creature, out ObjectState? machine, out (int X, int Y) beside) is not { } mode)
            return true;

        (int mx, int my) = AutoFurnitureRules.FurniturePosition(world, database, held, machine!);
        (int x, int y) = beside;
        creature.MachineMode = mode;
        if (mode == AtIt)
        {
            int r = ResearchStep.StandOff(database, creature, machine!);
            int side = AutoFurnitureRules.WallSide(world, database, held, machine!.TileIndex);
            int angle = side >= 0
                ? ((((side + 2) & 3) * 0x200) + 0x780) & Bearing.Mask
                : AutoFurnitureRules.Choice(machine.TileIndex % world.Width, machine.TileIndex / world.Width, Bearing.Turn) & Bearing.Mask;
            (x, y) = (mx + ((TrapRules.Sine(angle) * r) >> 16), my + (-(TrapRules.Cosine(angle) * r) >> 16));
        }

        if (Walk(context, system, creature, x, y, AtTheMachineState))
            (creature.StateTargetX, creature.StateTargetY) = (mx, my);
        return true;
    }

    /// <summary><c>0x004F5BE0</c> (workshop-states.md §3): the nearest machine no place-holder works at
    /// (mode 1); else the nearest no place-holder works beside (mode 2), with its stand point. Null
    /// with none, where DK2 searches again without end.</summary>
    public static byte? Find(
        WorldState world, GameDatabase database, RoomInstance room, CreatureState seeker,
        out ObjectState? machine, out (int X, int Y) point)
    {
        point = default;
        machine = Nearest(world, database, room, seeker, AtIt);
        if (machine is not null) return AtIt;
        machine = Nearest(world, database, room, seeker, BesideIt);
        if (machine is null) return null;

        (int mx, int my) = AutoFurnitureRules.FurniturePosition(world, database, room, machine);
        point = (mx, my);
        int r = ResearchStep.StandOff(database, seeker, machine);
        int wall = AutoFurnitureRules.WallSide(world, database, room, machine.TileIndex);
        int? angle = null;
        if (wall >= 0) angle = ((((wall + 2) & 3) * 0x200) + 0x180) & Bearing.Mask;
        else
        {
            int x0 = machine.TileIndex % world.Width, y0 = machine.TileIndex / world.Width;
            for (int k = 0; k < Sides.Length && angle is null; k++)
            {
                var at = new TilePos(x0 + Sides[k].Dx, y0 + Sides[k].Dy);
                if (world.Contains(at) && !AnyoneOn(world, (at.Y * world.Width) + at.X)) angle = (k & 3) << 9;
            }
        }

        if (angle is int a) point = (mx + ((TrapRules.Sine(a) * r) >> 16), my + (-(TrapRules.Cosine(a) * r) >> 16));
        return BesideIt;
    }

    private static ObjectState? Nearest(WorldState world, GameDatabase database, RoomInstance room, CreatureState seeker, byte mode)
    {
        ObjectState? found = null;
        uint best = uint.MaxValue;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.InHand || !IsMachine(thing.ObjectTypeId) || !EatingRules.Contains(room, thing.TileIndex)) continue;
            (int x, int y) = AutoFurnitureRules.FurniturePosition(world, database, room, thing);
            uint distance = Distance(seeker.PosX - x, seeker.PosY - y);
            if (distance >= best || Worked(world, room, x, y, mode)) continue;
            found = thing;
            best = distance;
        }

        return found;
    }

    /// <summary>A place-holder of the room (the seeker too) whose target is this machine in this mode.</summary>
    private static bool Worked(WorldState world, RoomInstance room, int x, int y, byte mode)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.HeldPlace.In(room) && c.MachineMode == mode && c.StateTargetX == x && c.StateTargetY == y
                && c.StateTargetX >= 0 && c.StateTargetY >= 0 && world.Contains(new TilePos(x >> 12, y >> 12)))
                return true;
        return false;
    }

    // ----------------------------------------------------------------------------- 64, 65

    /// <summary>64: on the target's tile a machine of type 46, 15 or 16 to face; none, 63 and on
    /// anyway. Then action 32, 6 to 12 more plays, and 65.</summary>
    private static bool AtTheMachine(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int tile = ((creature.StateTargetY >> 12) * world.Width) + (creature.StateTargetX >> 12);
        ObjectState? machine = OnTile(world, tile, Machine46) ?? OnTile(world, tile, Anvil) ?? OnTile(world, tile, Machine16);
        if (machine is null)
        {
            if (!StateShadow.TryMove(context, system, creature, ChoosingState)) return false;
        }
        else if (CasinoStep.TurnTo(context, creature, creature.StateTargetX, creature.StateTargetY) != 0)
            return true;

        int count = context.Rng.NextInt(7) + 6;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        creature.ActionUntilTick = context.Tick.Value + AnimationTimingRules.CountedHoldTicks(definition, WorkArtSlot, count);
        context.Emit(system, "workshop_work", $"creature={creature.Id} count={count} until={creature.ActionUntilTick}");
        StateShadow.TryMove(context, system, creature, WorkingState);
        return true;
    }

    /// <summary>65: it turns to the machine; moved, the work ends; while it runs, it waits; then 63.</summary>
    private static bool Work(SimulationContext context, string system, CreatureState creature, long age)
    {
        long now = context.Tick.Value;
        CasinoStep.TurnTo(context, creature, creature.StateTargetX, creature.StateTargetY);
        bool moved = creature.WaitPosX != creature.PosX || creature.WaitPosY != creature.PosY;
        if (age > 1 && moved && now < creature.ActionUntilTick) creature.ActionUntilTick = 0;
        (creature.WaitPosX, creature.WaitPosY) = (creature.PosX, creature.PosY);
        if (now < creature.ActionUntilTick) return true;
        return StateShadow.TryMove(context, system, creature, ChoosingState);
    }

    // --------------------------------------------------------------------------- shared

    private static RoomInstance? OwnWorkshopUnder(WorldState world, GameDatabase database, CreatureState creature)
    {
        if (!world.Contains(creature.Tile)) return null;
        RoomInstance? room = RoomIndex.RoomAt(world, database, (creature.Tile.Y * world.Width) + creature.Tile.X);
        return room is not null && room.Definition.TypeId == RoomTypeId.Workshop && room.OwnerPlayerId == creature.OwnerPlayerId
            ? room : null;
    }

    private static ObjectState? OnTile(WorldState world, int tile, byte type)
    {
        foreach (ObjectState thing in world.Objects)
            if (!thing.InHand && thing.TileIndex == tile && thing.ObjectTypeId == type) return thing;
        return null;
    }

    private static bool AnyoneOn(WorldState world, int tile)
    {
        foreach (CreatureState c in world.Creatures)
            if (!CreatureDeathRules.IsCorpse(c) && world.Contains(c.Tile) && (c.Tile.Y * world.Width) + c.Tile.X == tile)
                return true;
        return false;
    }

    private static uint Distance(int dx, int dy)
    {
        long ax = Math.Abs((long)dx), ay = Math.Abs((long)dy);
        return unchecked((uint)((ax * ax) >> 12) + (uint)((ay * ay) >> 12));
    }

    private static bool Walk(SimulationContext context, string system, CreatureState creature, int x, int y, int resume)
    {
        if (!context.Navigation.TryRouteTo(creature, x, y, MoveOptions.Loose)) return false;
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, resume);
        context.Emit(system, "workshop_walk", $"creature={creature.Id} to=({x >> 12},{y >> 12}) resume={resume}");
        return true;
    }
}
