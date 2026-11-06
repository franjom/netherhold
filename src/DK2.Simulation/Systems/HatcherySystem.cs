// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// The Hatchery: per-tick egg and coop processes and laying checks; coop maintenance once a second.
/// <para>The rules are <see cref="HatcheryRules"/> — how many coops a room of a
/// given size gets and where they may stand, an <b>Egg</b> (object 47) laid at a
/// chosen floor point or a full room's coop, becoming a <b>Chicken</b> (object 9) on art completion,
/// and one bird per tile as the ceiling. STRONGLY INFERRED
/// (docs/research/hatchery.md, ADR-0066). The laying interval is multiplied by
/// variable 19, ticks per game second, which the <b>level header</b> supplies:
/// all 66 shipped levels carry 4 (Q132).</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hatchery.md")]
public sealed class HatcherySystem : ISimulationSystem
{
    private const string SystemName = "Hatchery";

    private readonly bool _lays;
    private readonly bool _stepObjects;
    private readonly HatcheryLayStep _layStep = new();

    /// <param name="lays">Whether this system also lays, as the Hatchery's room handler does (<c>0x004DE0A0</c>).
    /// A host that walks the rooms in DK2's order has <see cref="HatcheryLayStep"/> do it there, after the thing
    /// pass that steps the objects, and passes false.</param>
    public HatcherySystem(bool lays = true) : this(lays, stepObjects: true) { }

    internal HatcherySystem(bool lays, bool stepObjects)
    {
        _lays = lays;
        _stepObjects = stepObjects;
    }

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if (_stepObjects) StepObjectAnimations(context);

        // Fallback hatching precedes laying so an egg is not aged on its lay turn.
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond == 0) Hatch(context);
        if (_lays) RoomTickSystem.RunAlone(context, _layStep);
    }

    private static void StepObjectAnimations(SimulationContext context)
    {
        WorldState world = context.World;
        for (int i = world.Objects.Count - 1; i >= 0; i--)
        {
            ObjectState thing = world.Objects[i];
            StepObject(context, thing);
        }
    }

    internal static void StepObject(SimulationContext context, ObjectState thing)
    {
        WorldState world = context.World;
        if (thing.InHand || thing.CarriedByCreatureId >= 0) return;
        ObjectDef? definition = context.Database.ObjectById(thing.ObjectTypeId);
        if (thing.ObjectTypeId == HatcheryRules.EggObjectId && thing.ProcessState == EggLaunchRules.LaunchProcess)
        {
            EggLaunchRules.Step(context, thing);
            return;
        }
        if (thing.ObjectTypeId == HatcheryRules.EggObjectId && thing.ProcessState == 32
            && definition?.ArtSlot(0).ResourceType == 6)
        {
            thing.ProcessAgeTicks++;
            if (thing.ProcessCount != 0)
            {
                ObjectState chicken = HatcheryRules.Hatch(world, context.Database, thing);
                context.Emit(SystemName, "chicken_hatched",
                    $"player={chicken.OwnerPlayerId} egg={thing.Id} chicken={chicken.Id} " +
                    $"{Describe(world, chicken.TileIndex)} animation=complete");
                return;
            }
            if ((thing.PhysicalVelocityX != 0 || thing.PhysicalVelocityY != 0 || thing.PhysicalVelocityZ != 0)
                && !EggLaunchRules.Move(context, thing, default)) return;
            if (thing.ProcessAgeTicks < AnimationTimingRules.FirstCycleHoldTicks(definition.ArtSlot(0))) return;
            if (definition.ArtSlot(5).ResourceType == 0) return;
            thing.ProcessCount = 1;
            thing.ProcessDeadline = context.Tick.Value;
        }
        else if (thing.ObjectTypeId == HatcheryRules.CoopObjectId && thing.ProcessState == 29)
        {
            thing.ProcessAgeTicks++;
            if (thing.ProcessAgeTicks == 1) thing.ProcessDeadline = context.Tick.Value;
            if (definition?.ArtSlot(5).ResourceType != 6
                || thing.ProcessAgeTicks > AnimationTimingRules.FirstCycleHoldTicks(definition.ArtSlot(5)))
            {
                thing.ProcessState = 0;
                thing.ProcessAgeTicks = 0;
            }
        }
    }

    /// <summary>
    /// One Hatchery's tick: maintains its coops once a second and checks its last-generated stamp every turn.
    /// </summary>
    internal static void LayRoom(SimulationContext context, RoomInstance room)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        // Coops are not placed here: Room_Rebuild places them at the level's start and whenever the
        // room's tiles change (HatcheryRules.RebuildCoops, ADR-0672).

        // The clock lives on the room's lowest tile, because a room here is
        // derived and cannot hold state of its own (TileState.HatcheryLastGeneratedTick).
        ref TileState clock = ref world.TileAtIndex(room.TileIndices[0]);
        int intervalTicks = HatcheryRules.GenerationIntervalTicks(world, database, room);
        long now = context.Tick.Value + 1;
        if (now < clock.HatcheryLastGeneratedTick + intervalTicks) return;
        clock.HatcheryLastGeneratedTick = now;

        ObjectState? egg = HatcheryRules.TryLayEgg(world, database, room, context.Rng);
        if (egg is null)
        {
            // A full room lays nothing, and says so rather than silently
            // resetting - "why is my hatchery not producing" is a question
            // the log should answer.
            context.Emit(SystemName, "lay_refused",
                $"player={room.OwnerPlayerId} {Describe(world, room.TileIndices[0])} " +
                $"birds={HatcheryRules.Birds(world, room)} " +
                $"capacity={HatcheryRules.FlockCapacity(database, room)}");
            return;
        }

        context.Emit(SystemName, "egg_laid",
            $"player={room.OwnerPlayerId} egg={egg.Id} " +
            $"{Describe(world, egg.TileIndex)} " +
            $"interval={intervalTicks / SimulationTiming.TicksPerSecond}s " +
            $"tiles={room.TileCount}");
    }

    /// <summary>Every egg counts down, and becomes a chicken where it lies.</summary>
    private static void Hatch(SimulationContext context)
    {
        WorldState world = context.World;
        int hatchTicks = SimulationTiming.TicksFromSeconds(HatcheryRules.EggHatchSeconds);

        // Descending, because hatching replaces the egg with a chicken.
        for (int i = world.Objects.Count - 1; i >= 0; i--)
        {
            ObjectState thing = world.Objects[i];
            if (thing.ObjectTypeId != HatcheryRules.EggObjectId) continue;
            if (thing.ProcessState == EggLaunchRules.LaunchProcess) continue;
            if (context.Database.ObjectById(thing.ObjectTypeId)?.ArtSlot(0).ResourceType == 6) continue;

            thing.TimerTicks += SimulationTiming.TicksPerSecond;
            if (thing.TimerTicks < hatchTicks) continue;

            ObjectState chicken = HatcheryRules.Hatch(world, context.Database, thing);

            context.Emit(SystemName, "chicken_hatched",
                $"player={chicken.OwnerPlayerId} egg={thing.Id} chicken={chicken.Id} " +
                $"{Describe(world, chicken.TileIndex)} " +
                $"after={HatcheryRules.EggHatchSeconds}s");
        }
    }

    private static string Describe(WorldState world, int tileIndex) =>
        tileIndex < 0 ? "(none)" : $"({tileIndex % world.Width},{tileIndex / world.Width})";
}

/// <summary>The Hatchery's part of the room tick (<c>Hatchery_Tick</c>, <c>0x004DE0A0</c>, which only lays when its clock is
/// due). <see cref="HatcherySystem"/> does this itself when it runs without a room walk.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hatchery.md")]
public sealed class HatcheryLayStep : IRoomTickStep
{
    public string Name => "Hatchery";

    public void Execute(SimulationContext context) => RoomTickSystem.RunAlone(context, this);

    public bool Applies(SimulationContext context, RoomInstance room) =>
        room.Definition.TypeId == RoomTypeId.Hatchery && room.TileIndices.Count != 0;

    public void Tick(SimulationContext context, RoomInstance room) => HatcherySystem.LayRoom(context, room);
}
