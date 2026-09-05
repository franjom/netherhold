// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// What a prison does to the creatures in it — once a game second.
///
/// <para>Three things, in this order, and the order is the behaviour:</para>
/// <list type="number">
/// <item><b>A cell that is no longer a cell lets its prisoner go.</b> The
/// manual's <i>"losing a prison frees every enemy in it"</i>, and the same test
/// covers selling part of a prison out from under somebody, digging it away, or
/// a rival claiming the tile. Checking where the prisoner is standing rather
/// than hooking every path that can destroy a room is the same choice
/// <see cref="RoomInstance"/> makes: derived state cannot drift.</item>
/// <item><b>An overcrowded prison breaks out.</b>
/// <c>OvercrowdingJailBreakPercentagePerExtraCreature</c> = 50, per extra
/// creature, rolled once a second (<see cref="PrisonRules.JailBreakPercent"/>).
/// A prison only ever becomes overcrowded because a keeper put a prisoner into a
/// full one by hand — imps refuse to — which is the manual's <i>"manual
/// overcrowding"</i>.</item>
/// <item><b>Everybody left withers</b> by
/// <c>PrisonModifyCreatureHealthPerSecond</c> = -3, and a prisoner that runs out
/// <b>rises as a Skeleton</b> for the keeper whose prison it was standing in.
/// The Prison's <c>CreatedCreatureId</c> is 9 and the manual says it in words:
/// <i>"any creatures that perish in Prison will rise as Skeletons under your
/// control"</i>.</item>
/// </list>
///
/// <para><b>Dying in a prison is not the ordinary death.</b> There is no
/// collapse, no rescue window and no corpse: the creature is replaced where it
/// stands. That is what the room is <i>for</i>, and it is why a keeper who wants
/// a prisoner alive drops chickens on it (which needs the Hatchery's chickens to
/// be droppable — not built, so today a prisoner's clock runs out).</para>
///
/// <para>Registered after <see cref="CreatureHealthSystem"/>, on the same
/// cadence and the same phase, so a prisoner's second is settled once per second
/// by one system rather than argued over by two. The health system skips
/// prisoners entirely for that reason.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison.md")]
public sealed class PrisonSystem : ISimulationSystem
{
    private const string SystemName = "Prison";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond != 0) return;

        FreeThoseWhoseCellIsGone(context);
        BreakOutTheOvercrowded(context);
        Wither(context);
    }

    /// <summary>
    /// A prisoner standing anywhere but on a tile of a prison belonging to the
    /// keeper who imprisoned it is not a prisoner any more.
    /// </summary>
    private static void FreeThoseWhoseCellIsGone(SimulationContext context)
    {
        WorldState world = context.World;

        foreach (CreatureState creature in world.Creatures)
        {
            if (!PrisonRules.IsImprisoned(creature)) continue;

            // A prisoner in the keeper's hand is between cells, not out of one.
            // Where it lands is the drop's business (HandSystem).
            if (HandRules.IsHeld(creature)) continue;
            if (StandsInItsOwnPrison(context, creature)) continue;

            context.Emit(SystemName, "prisoner_freed",
                $"creature={creature.Id} owner={creature.OwnerPlayerId} " +
                $"was_held_by={creature.PrisonOwnerPlayerId} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=cell_gone");

            PrisonRules.Release(creature);
        }
    }

    private static bool StandsInItsOwnPrison(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;

        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (!PrisonRules.IsPrisonOf(room, (byte)creature.PrisonOwnerPlayerId)) continue;
            if (room.TileIndices.Contains(here)) return true;
        }

        return false;
    }

    /// <summary>
    /// Rolls each overcrowded prison's chance, and empties the ones that come
    /// up.
    /// </summary>
    private static void BreakOutTheOvercrowded(SimulationContext context)
    {
        WorldState world = context.World;

        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (room.Definition.TypeId != RoomTypeId.Prison) continue;

            int held = PrisonRules.PrisonerCount(world, room);
            int over = held - PrisonRules.Capacity(room);
            int percent = PrisonRules.JailBreakPercent(context.Database, over);
            if (percent <= 0) continue;

            // Rolled even when the chance is at or above certainty, so the
            // sequence a level draws does not depend on how full a prison is.
            bool broken = context.Rng.NextInt(100) < percent;
            if (!broken) continue;

            context.Emit(SystemName, "jail_break",
                $"player={room.OwnerPlayerId} {Describe(world, room.TileIndices[0])} " +
                $"held={held} capacity={PrisonRules.Capacity(room)} chance={percent}%");

            foreach (CreatureState creature in world.Creatures)
            {
                if (!PrisonRules.IsImprisoned(creature)) continue;
                if (creature.PrisonOwnerPlayerId != room.OwnerPlayerId) continue;

                int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
                if (!room.TileIndices.Contains(here)) continue;

                context.Emit(SystemName, "prisoner_freed",
                    $"creature={creature.Id} owner={creature.OwnerPlayerId} " +
                    $"was_held_by={creature.PrisonOwnerPlayerId} " +
                    $"at=({creature.Tile.X},{creature.Tile.Y}) reason=jail_break");

                PrisonRules.Release(creature);
            }
        }
    }

    /// <summary>Takes the second's health off every prisoner, and raises the skeletons.</summary>
    private static void Wither(SimulationContext context)
    {
        WorldState world = context.World;

        // Descending, so replacing a prisoner with a skeleton cannot skip its
        // neighbour.
        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState creature = world.Creatures[i];
            if (!PrisonRules.IsImprisoned(creature)) continue;
            if (HandRules.IsHeld(creature)) continue;

            // Read here rather than once above, so that a level with no prison
            // does not demand a variable that only a prison needs. The synthetic
            // databases the harness runs on carry only what a registered system
            // actually reads, and a system that reads on an empty loop makes
            // every one of them carry the whole table.
            creature.Health += PrisonRules.WitherPerSecond(context.Database);
            if (creature.Health > 0) continue;

            Raise(context, creature);
        }
    }

    /// <summary>
    /// Replaces a prisoner that has withered away with whatever its prison
    /// makes — the Prison's <c>CreatedCreatureId</c>, which is the Skeleton.
    ///
    /// <para>A prison whose record names no creature simply loses the prisoner,
    /// which is the honest reading of a zero in that column rather than a
    /// special case.</para>
    /// </summary>
    private static void Raise(SimulationContext context, CreatureState prisoner)
    {
        WorldState world = context.World;
        var keeper = (byte)prisoner.PrisonOwnerPlayerId;
        TilePos at = prisoner.Tile;

        byte raises = 0;
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (!PrisonRules.IsPrisonOf(room, keeper)) continue;
            if (!room.TileIndices.Contains((at.Y * world.Width) + at.X)) continue;

            raises = room.Definition.CreatedCreatureId;
            break;
        }

        CreatureDef definition = context.Database.CreatureById(prisoner.CreatureTypeId);
        world.RemoveCreature(prisoner.Id);

        if (raises == 0)
        {
            context.Emit(SystemName, "prisoner_perished",
                $"creature={prisoner.Id} type={definition.Name} player={keeper} " +
                $"at=({at.X},{at.Y}) raised=none");
            return;
        }

        CreatureState skeleton = world.SpawnCreature(context.Database, raises, keeper, at);

        context.Emit(SystemName, "prisoner_perished",
            $"creature={prisoner.Id} type={definition.Name} owner={prisoner.OwnerPlayerId} " +
            $"player={keeper} at=({at.X},{at.Y}) " +
            $"raised={context.Database.CreatureById(raises).Name} as={skeleton.Id}");
    }

    private static string Describe(WorldState world, int tileIndex) =>
        tileIndex < 0 ? "(none)" : $"({tileIndex % world.Width},{tileIndex / world.Width})";
}
