// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// Beds and sleep: the first thing a creature that is not a worker ever chooses
/// to do.
///
/// <para><b>This is the first driver for non-workers.</b> Until now the only
/// creature that moved under its own steam was an imp, and
/// <see cref="ImpTaskSystem"/> drove it; a goblin out of a portal stood on the
/// hole forever because nothing looked at it. This system is the other half —
/// it claims a bed, walks the creature to it, puts it to sleep and wakes it —
/// and it deliberately reuses the imps' pathfinder and the imps' movement
/// system rather than growing a second way to walk.</para>
///
/// <para>The rule it implements is CONFIRMED by the owner and lives in
/// <see cref="LairRules"/>. What is here is the sequencing.</para>
///
/// <para><b>A bed is claimed as early as possible, not when sleep is needed</b>
/// — the owner's words were <i>"when a creature enters a dungeon through a
/// portal it goes to a lair, finds a free tile and occupies it with its
/// bed"</i>, so occupation happens on arrival. A creature that arrives before
/// its keeper has a lair keeps trying every tick and takes a bed the moment one
/// exists.</para>
///
/// <para><b>Order.</b> Registered after <see cref="ImpTaskSystem"/>, so it sees
/// the same world an imp's job selection saw, and before
/// <see cref="CreatureHealthSystem"/>, so a creature that falls asleep this tick
/// is healed on the same second rather than a second late. It sets paths and
/// <see cref="CreatureMovementSystem"/> walks them on the following tick, which
/// is exactly how an imp's job works.</para>
///
/// <para><b>Workers are skipped entirely.</b> An imp has no bed
/// (<c>LairObjectId</c> is 0) and never sleeps, so the two systems cannot fight
/// over the same creature — but the guard is explicit rather than incidental,
/// because "an imp is excluded because its record happens to say 0" is the kind
/// of reasoning that stops being true when somebody edits a record.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-needs.md")]
public sealed class CreatureSleepSystem : ISimulationSystem
{
    private const string SystemName = "CreatureSleep";

    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];

    public CreatureSleepSystem() : this(new GridPathfinder()) { }

    public CreatureSleepSystem(IPathfinder pathfinder) =>
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);

            if (definition.Flags.HasFlag(CreatureFlags.IsWorker)) continue;
            if (!LairRules.NeedsBed(definition)) continue;

            // A bed on a tile that has stopped being this keeper's lair is not a
            // bed. Sold, dug out or taken by a rival while its owner was away.
            if (creature.LairTileIndex >= 0
                && !LairRules.BedStillValid(world, database, creature))
            {
                context.Emit(SystemName, "bed_lost",
                    $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} tile={creature.LairTileIndex}");

                LairRules.ReleaseBed(creature);
                if (creature.Activity is CreatureActivity.Sleeping
                    or CreatureActivity.GoingToSleep)
                {
                    Wake(creature);
                }
            }

            if (creature.LairTileIndex < 0) Claim(context, creature, definition);

            switch (creature.Activity)
            {
                case CreatureActivity.Sleeping:
                    Sleep(context, creature, definition);
                    break;

                case CreatureActivity.GoingToSleep:
                    Arriving(context, creature, definition);
                    break;

                default:
                    creature.AwakeTicks++;
                    if (LairRules.WantsToSleep(database, definition, creature))
                        SetOff(context, creature, definition);
                    break;
            }
        }
    }

    private static void Claim(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (!LairRules.TryClaimBed(context.World, context.Database, creature)) return;

        int index = creature.LairTileIndex;
        context.Emit(SystemName, "bed_claimed",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} object={definition.LairObjectId} " +
            $"tile=({index % context.World.Width},{index / context.World.Width})");
    }

    /// <summary>Routes a creature to its own bed, or leaves it where it is.</summary>
    private void SetOff(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        // No bed means nowhere to go. In DK2 this is what angers a creature
        // (angerNoLair), and anger does not exist yet — so it stays awake and
        // its awake counter keeps climbing. Q105.
        if (creature.LairTileIndex < 0) return;

        WorldState world = context.World;
        var bed = new TilePos(
            creature.LairTileIndex % world.Width, creature.LairTileIndex / world.Width);

        var grid = new WorldPathGrid(context.Database, world, definition);
        if (!_pathfinder.TryFindPath(grid, creature.Tile, bed, _route)) return;

        creature.Path.Clear();
        creature.Path.AddRange(_route);
        creature.PathIndex = 0;
        creature.Activity = CreatureActivity.GoingToSleep;

        context.Emit(SystemName, "going_to_sleep",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} to=({bed.X},{bed.Y}) " +
            $"steps={_route.Count} health={creature.Health}/{definition.Hp}");
    }

    /// <summary>Falls asleep once it is standing on its own bed.</summary>
    private static void Arriving(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        TilePos at = creature.Tile;
        int here = (at.Y * world.Width) + at.X;

        if (here != creature.LairTileIndex)
        {
            // Still walking. If the path has run out without arriving — the
            // route was blocked mid-walk — fall back to standing up, so a
            // creature cannot be stuck in GoingToSleep forever.
            if (creature.PathIndex < creature.Path.Count) return;
            Wake(creature);
            return;
        }

        creature.Activity = CreatureActivity.Sleeping;
        creature.SleepTicks = 0;

        context.Emit(SystemName, "fell_asleep",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} at=({at.X},{at.Y}) " +
            $"health={creature.Health}/{definition.Hp}");
    }

    private static void Sleep(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        creature.SleepTicks++;

        if (!LairRules.HasSleptEnough(definition, creature)) return;

        context.Emit(SystemName, "woke_up",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} slept={creature.SleepTicks} " +
            $"health={creature.Health}/{definition.Hp}");

        Wake(creature);
    }

    private static void Wake(CreatureState creature)
    {
        creature.Activity = CreatureActivity.Idle;
        creature.AwakeTicks = 0;
        creature.SleepTicks = 0;
        creature.Path.Clear();
        creature.PathIndex = 0;
    }
}
