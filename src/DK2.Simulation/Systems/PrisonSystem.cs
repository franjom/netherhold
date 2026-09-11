// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>What a prison does to the creatures in it, once a game second: frees any
/// whose cell is gone, rolls a jail break in the overcrowded, then withers the
/// rest.</summary>
/// <remarks>That order is the behaviour, and each step's rule and numbers are on its
/// own method. Dying in a prison is not the ordinary death: no collapse, no rescue
/// window, no corpse — the creature is replaced where it stands, which is what the
/// room is for, and a keeper who wants a prisoner alive drops a chicken on it
/// (ADR-0074, ADR-0075). Registered after <see cref="CreatureHealthSystem"/>, on the
/// same cadence and phase, so exactly one system settles a prisoner's second; the
/// health system skips prisoners for that reason.</remarks>
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

    /// <summary>A prisoner standing anywhere but on a tile of a prison belonging to
    /// the keeper who imprisoned it is not a prisoner any more.</summary>
    /// <remarks>The manual's "losing a prison frees every enemy in it", and the same
    /// test covers selling part of a prison out from under somebody, digging it away,
    /// or a rival claiming the tile. Testing where the prisoner stands, rather than
    /// hooking every path that can destroy a room, is <see cref="RoomInstance"/>'s own
    /// choice: derived state cannot drift.</remarks>
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

    /// <summary>Rolls each overcrowded prison's chance, and empties the ones that
    /// come up.</summary>
    /// <remarks><c>OvercrowdingJailBreakPercentagePerExtraCreature</c> = 50, per extra
    /// creature, rolled once a second (<see cref="PrisonRules.JailBreakPercent"/>). A
    /// prison only ever becomes overcrowded because a keeper put a prisoner into a
    /// full one by hand — imps refuse to — which is the manual's "manual
    /// overcrowding".</remarks>
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

    /// <summary>Takes the second's health off every prisoner, and raises the
    /// skeletons.</summary>
    /// <remarks><c>PrisonModifyCreatureHealthPerSecond</c> = -3. A prisoner that runs
    /// out rises as a Skeleton for the keeper whose prison it was standing in: the
    /// manual's "any creatures that perish in Prison will rise as Skeletons under your
    /// control".
    /// <para><b>Only the ones the drain finishes.</b> A prisoner already out of health
    /// when the drain comes round was killed by something else, and that one leaves a body
    /// on the prison's rim instead of rising (owner, 2026-09-11, ADR-0140). No cap on the
    /// Skeletons yet, though DK2 shows one and it is this prison's capacity (Q307).</para>
    /// </remarks>
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

            // Out of health before the drain reached it, so the drain is not what
            // killed it: a slain captive leaves a body, not a Skeleton (ADR-0140).
            if (creature.Health <= 0)
            {
                Slain(context, creature);
                continue;
            }

            // Read here rather than once above, so that a level with no prison
            // does not demand a variable that only a prison needs. The synthetic
            // databases the harness runs on carry only what a registered system
            // actually reads, and a system that reads on an empty loop makes
            // every one of them carry the whole table.
            CreatureDef definition =
                context.Database.CreatureById(creature.CreatureTypeId);

            if (!PrisonRules.Withers(creature)) continue;   // neutral in a neutral cell

            int wither = PrisonRules.WitherPerSecond(context.Database, definition);
            if (wither == 0) continue;   // never eats, never withers - Q173

            creature.Health += wither;
            if (creature.Health > 0) continue;

            Raise(context, creature);
        }
    }

    /// <summary>Lets go of a prisoner something else killed, putting its body outside the
    /// bars first.</summary>
    /// <remarks>
    /// <para>Observed 2026-09-11: a Monk slapped to death in a prison left a corpse lying
    /// on the <b>rim</b>, outside the fence, while the live captives stayed inside it. The
    /// body is moved to the first free rim tile of the prison it died in — which is
    /// what puts it where an imp can reach it without passing the gate.</para>
    /// <para>The death itself is not booked here. Releasing it is enough:
    /// <see cref="CreatureHealthSystem"/> skips prisoners and picks this one up on the next
    /// game second, where standing on a Prison tile already means no rescuable window
    /// (Q303, ADR-0138) — so the ordinary path produces exactly the corpse observed.
    /// </para></remarks>
    private static void Slain(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        var keeper = (byte)creature.PrisonOwnerPlayerId;
        TilePos died = creature.Tile;

        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (!PrisonRules.IsPrisonOf(room, keeper)) continue;
            if (!room.TileIndices.Contains((died.Y * world.Width) + died.X)) continue;

            foreach (int tileIndex in room.TileIndices)
            {
                if (room.IsInterior(tileIndex)) continue;
                if (Occupied(world, tileIndex, creature.Id)) continue;

                creature.PosX = CreatureState.CentreOf(tileIndex % world.Width);
                creature.PosY = CreatureState.CentreOf(tileIndex / world.Width);
                break;
            }

            break;
        }

        PrisonRules.Release(creature);

        context.Emit(SystemName, "prisoner_slain",
            $"creature={creature.Id} player={keeper} " +
            $"died_at=({died.X},{died.Y}) body_at=({creature.Tile.X},{creature.Tile.Y})");
    }

    /// <summary>Whether anything but <paramref name="exceptId"/> stands on this tile.
    /// <b>Free</b>, in the owner's "first free outermost tile", means free of creatures:
    /// a tile already carrying a body or a live one is passed over.</summary>
    private static bool Occupied(WorldState world, int tileIndex, int exceptId)
    {
        foreach (CreatureState other in world.Creatures)
        {
            if (other.Id == exceptId) continue;
            if ((other.Tile.Y * world.Width) + other.Tile.X == tileIndex) return true;
        }

        return false;
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
