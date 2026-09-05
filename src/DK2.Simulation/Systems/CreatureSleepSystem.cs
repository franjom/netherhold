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
/// <para><b>⚠ HOW A BED IS CLAIMED IS CONTRADICTED BY THE ORIGINAL, on two
/// counts, and is left standing on purpose.</b> This system claims a bed on the
/// first tick one is free, from wherever the creature is standing, with no walk
/// and no reason. The owner recorded DK2 doing neither of those things
/// (2026-09-05):</para>
///
/// <list type="number">
/// <item><b>The trigger is not availability.</b> A free lair tile sat there
/// while a bedless Goblin and a bedless Bile Demon went off to the Training
/// Room. The search began only when the creature's mood turned <i>Unhappy</i>
/// with "no lair" as the reason — so a happy bedless creature does an ordinary
/// job instead. <b>Q150.</b></item>
/// <item><b>Claiming is a journey with a test at the end.</b> Creatures were
/// watched crossing a lair, walking up to an occupied tile, rejecting it and
/// carrying on to a free one; in a controlled 1×5 lair the tiles went 1, 3, 5,
/// 4. Occupancy is decided <i>on arrival</i>, not when choosing.</item>
/// </list>
///
/// <para><b>Why it is not fixed here.</b> Half the replacement is read out of
/// the binary and half is not: the tile search (own tile, then a fixed table of
/// neighbours, then a chained walk of the room) is known, and so is that the
/// room is chosen by distance to its centre — but the trigger (Q150) and the
/// test that makes one lair room preferred over another (Q151) are not. Building
/// it on a guess would be the failure this project keeps repeating: the right
/// outcome from the wrong rule. See <c>docs/research/lair-acquisition.md</c>,
/// which is the design, and note that what is here today is still built on words
/// the owner gave and this project only half implemented — <i>"it <b>goes</b> to
/// a lair, finds a free tile and occupies it with its bed"</i>.</para>
///
/// <para><b>A bed that stops existing is replaced, not mourned</b> — CONFIRMED
/// by the owner (2026-09-04): <i>"if a lair tile with bed on it is sold, then
/// the creature whose bed it was loses a place to sleep. It needs to find a new
/// free lair tile to place its bed, otherwise anger mechanic is triggered, it
/// gets upset, then angry then it leaves the dungeon."</i> That is exactly the
/// order below — <c>bed_lost</c>, then <c>Claim</c> on the same tick — so a
/// creature whose lair is partly sold moves along to a surviving tile without
/// ever noticing, and only a creature that finds <i>no</i> free tile is left
/// bedless. The consequence of being left bedless is the anger ladder, which is
/// the third confirmation of it from a third cause (Q9, Q105) and is still not
/// implemented: its unit is unknown (Q106, ADR-0050). So today such a creature
/// simply stays awake for ever.</para>
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
            int bedIndex = LairRules.BedTileIndex(world, database, creature);
            if (bedIndex >= 0 && !LairRules.BedStillValid(world, database, creature))
            {
                context.Emit(SystemName, "bed_lost",
                    $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} tile={bedIndex}");

                LairRules.ReleaseBed(world, database, creature);
                bedIndex = -1;
                if (creature.Activity is CreatureActivity.Sleeping
                    or CreatureActivity.GoingToSleep)
                {
                    Wake(creature);
                }
            }

            // A collapsed creature does nothing at all (ADR-0051), and where
            // this line sits is the whole of that rule.
            //
            // *After* the validity check above, because a bed that stopped
            // being a lair tile stopped existing whatever its owner is doing —
            // that is the world removing something, not the creature acting,
            // and a bed object left behind would make BedTaken lie.
            //
            // *Before* the claim below, because claiming is the creature
            // acting: the owner's words are that it "goes to a lair, finds a
            // free tile and occupies it with its bed", which is a walk and a
            // placement, and something lying unconscious on the floor can do
            // neither. The cost is that a creature whose bed is sold while it
            // lies dying can no longer be carried anywhere — Q107, and it costs
            // nothing today because carrying does not exist. The alternative
            // costs something now: a dying creature would take the last free
            // bed out from under a goblin that walked in on its own feet.
            if (CreatureDeathRules.IsDown(creature)) continue;

            // A creature walking out of the dungeon does not stop for a nap,
            // and above all does not claim a bed on its way past the lair —
            // which it would, having given its own up when it decided to go
            // (CreatureAngerSystem.Leave). Without this line a deserter takes
            // the last free bed out from under a creature that intends to stay,
            // and then leaves with it.
            if (creature.Activity == CreatureActivity.Leaving) continue;

            if (bedIndex < 0) Claim(context, creature, definition);

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

        int index = LairRules.BedTileIndex(context.World, context.Database, creature);
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
        WorldState world = context.World;
        int index = LairRules.BedTileIndex(world, context.Database, creature);
        if (index < 0) return;

        var bed = new TilePos(index % world.Width, index / world.Width);

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

        if (here != LairRules.BedTileIndex(world, context.Database, creature))
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
