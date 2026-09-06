// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// The Hatchery: coops, eggs, and chickens — once a game second.
///
/// <para><b>The last thing standing between this project and food.</b>
/// <see cref="HatcheryRules"/> has known how many coops a room of a given size
/// gets, where they may stand, that the room lays an <b>Egg</b> (object 47) at a
/// randomly chosen coop, that the egg becomes a <b>Chicken</b> (object 9) after
/// two seconds, and that a room holds one bird per tile — for four sittings,
/// with nothing to run any of it. What was missing was the clock, and the reason
/// written down was that DK2 multiplies its laying interval by a balance
/// variable <i>"no shipped file supplies"</i> (Q132).</para>
///
/// <para><b>That reason stopped being true and nobody noticed.</b> Q132 was
/// answered on 2026-09-05: the multiplier is variable 19, ticks per game second,
/// and the <b>level header</b> supplies it — all 66 shipped levels carry 4. The
/// blocker had been gone for a day while three documents still named it. Fifth
/// instance of a claim nothing checks (ADR-0066).</para>
///
/// <para><b>The eating half arrived the same week</b>
/// (<see cref="CreatureEatSystem"/>, ADR-0067): a hungry creature walks here,
/// picks the nearest bird once it is inside and eats it — which also means a
/// hatchery that had stopped at its one-bird-per-tile ceiling starts laying
/// again, because something is taking birds out of it. Nobody wrote that
/// interaction. What is still missing is a <b>prisoner</b>, which cannot move
/// and so cannot be fed (Q172).</para>
///
/// <para>Registered after <see cref="PortalSystem"/>, which is the other system
/// that makes something out of a room, and on the same once-a-game-second
/// cadence as the health and mana systems.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hatchery.md")]
public sealed class HatcherySystem : ISimulationSystem
{
    private const string SystemName = "Hatchery";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond != 0) return;

        // Hatching BEFORE laying, so an egg is not aged on the second it is
        // laid. Same reasoning as CreatureHealthSystem running last: a thing
        // that arrives this second has not yet spent one. The other order gives
        // a two-second egg a one-second life, which is what the first draft
        // did and what AnEggBecomesAChickenTwoSecondsAfterItIsLaid caught.
        Hatch(context);
        Lay(context);
    }

    /// <summary>
    /// Every hatchery gets its coops, then counts towards its next egg.
    /// </summary>
    private static void Lay(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.Definition.TypeId != RoomTypeId.Hatchery) continue;
            if (room.TileIndices.Count == 0) continue;

            // Coops are the room's furniture and they are placed rather than
            // built: a hatchery that grows gets the coops its new size allows,
            // on the tiles HatcheryRules picks. Cheap when there is nothing to
            // add, which is every second but the first.
            int added = HatcheryRules.PlaceCoops(world, database, room);
            if (added > 0)
            {
                context.Emit(SystemName, "coops_placed",
                    $"player={room.OwnerPlayerId} {Describe(world, room.TileIndices[0])} " +
                    $"added={added} tiles={room.TileCount} " +
                    $"wanted={HatcheryRules.CoopCount(room)}");
            }

            // The clock lives on the room's lowest tile, because a room here is
            // derived and cannot hold state of its own (TileState.HatchTicks).
            ref TileState clock = ref world.TileAtIndex(room.TileIndices[0]);
            clock.HatchTicks += SimulationTiming.TicksPerSecond;

            int intervalTicks = SimulationTiming.TicksFromSeconds(
                HatcheryRules.LayIntervalSeconds(database, room));

            if (intervalTicks <= 0 || clock.HatchTicks < intervalTicks) continue;
            clock.HatchTicks = 0;

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
                continue;
            }

            context.Emit(SystemName, "egg_laid",
                $"player={room.OwnerPlayerId} egg={egg.Id} " +
                $"{Describe(world, egg.TileIndex)} " +
                $"interval={HatcheryRules.LayIntervalSeconds(database, room)}s " +
                $"tiles={room.TileCount}");
        }
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
