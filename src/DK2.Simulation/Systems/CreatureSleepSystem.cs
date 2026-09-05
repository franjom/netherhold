// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// Beds and sleep: how a creature gets a bed, walks to it, sleeps in it and
/// wakes — the first thing a creature that is not a worker ever chooses to do.
///
/// <para><b>This is the first driver for non-workers.</b> Until beds existed the
/// only creature that moved under its own steam was an imp; a goblin out of a
/// portal stood on the hole forever because nothing looked at it. This system
/// is the other half, and it deliberately reuses the imps' pathfinder and the
/// imps' movement system rather than growing a second way to walk.</para>
///
/// <para><b>Getting a bed is a journey with a test at the end</b> (ADR-0060;
/// <c>docs/research/lair-acquisition.md</c> §4, §5, read from the original's
/// own lair state on 2026-09-05 after the owner filmed it). The shape:</para>
///
/// <list type="number">
/// <item><b>An idle creature with no bed tries to get one before anything
/// else, whatever its mood</b> (Q150). Sleep need is the second way in: a
/// creature whose <c>TimeAwake</c> has run out with no bed makes the same
/// attempt. The attempt <i>fails</i> when no reachable lair has a free tile,
/// and only then does the creature do anything else, retrying at its next idle
/// decision — here every <see cref="LairRules.IdleRedecisionTicks"/>.</item>
/// <item><b>The room is chosen first, by distance to its centre</b>: the
/// nearest reachable lair with a free slot, preferring one that holds no bed
/// of the other alignment (Q151; <see cref="CreatureFlags.IsGood"/> against
/// <see cref="CreatureFlags.IsEvil"/>). Nothing about its size or whether it
/// is "functional" — the finder tests room type, owner, reach, a free slot,
/// distance and the alignment of existing beds, and that is all.</item>
/// <item><b>The tile is chosen on arrival</b>: the tile underfoot, then eight
/// neighbours in a fixed order, then a walk of the room from a random start,
/// each tested for "the keeper's lair tile with no bed on it". Nothing is
/// reserved — two creatures heading for one room is normal, and the one that
/// arrives second finds its tile taken and looks next door. That is what the
/// owner filmed: creatures crossing a lair, walking up to an occupied tile,
/// rejecting it and carrying on; in a 1×5 lair the tiles went 1, 3, 5, 4.</item>
/// <item><b>The bed goes down where the creature stands</b>, at the tile
/// centre, and then the creature <b>sleeps at once only if it is hurt past
/// <c>CreatureSleepsWhenBelowPercentHealth</c></b>; otherwise the state simply
/// ends and it is idle. Tiredness has its own way back in.</item>
/// </list>
///
/// <para><b>What is the original's and what is ours.</b> The triggers, the
/// room test and preference, the on-arrival search order and its predicate,
/// the absence of reservation, the no-size test, the post-bedding health
/// threshold and the <c>TimeSleep</c> gate are read from the binary
/// (STRONGLY INFERRED; the owner's recordings CONFIRM the outcome). Ours are:
/// the idle retry cadence, the tile of the room the walk aims at (Q157, the
/// room centre), the order of the room walk (ascending tile index rather than
/// the room's join order), and the absence of the third-of-a-tile jitter the
/// original puts on the walk target — our mover walks to tile centres, and
/// the bed sits at the centre either way.</para>
///
/// <para><b>A bed that stops existing is replaced, not mourned</b> — CONFIRMED
/// by the owner (2026-09-04): <i>"if a lair tile with bed on it is sold, then
/// the creature whose bed it was loses a place to sleep. It needs to find a new
/// free lair tile to place its bed, otherwise anger mechanic is triggered, it
/// gets upset, then angry then it leaves the dungeon."</i> Losing a bed drops
/// the creature back to idle, and idle with no bed is the attempt above — the
/// same journey by the same route, so a creature whose lair is partly sold
/// walks to a surviving tile, and only a creature that finds <i>no</i> free
/// tile is left bedless and on the anger ladder
/// (<see cref="CreatureAngerSystem"/>).</para>
///
/// <para><b>Order.</b> Registered after <see cref="ImpTaskSystem"/> and before
/// <see cref="CreatureHealthSystem"/>, so a creature that falls asleep this
/// tick is healed on the same second rather than a second late. It sets paths
/// and <see cref="CreatureMovementSystem"/> walks them on the following tick,
/// exactly as an imp's job works.</para>
///
/// <para><b>Workers are skipped entirely.</b> An imp has no bed
/// (<c>LairObjectId</c> is 0) and never sleeps, so the two systems cannot fight
/// over the same creature — but the guard is explicit rather than incidental,
/// because "an imp is excluded because its record happens to say 0" is the kind
/// of reasoning that stops being true when somebody edits a record.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/lair-acquisition.md")]
public sealed class CreatureSleepSystem : ISimulationSystem
{
    private const string SystemName = "CreatureSleep";

    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];
    private readonly List<TilePos> _fallbackRoute = [];

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
            // *Before* the attempt below, because getting a bed is the creature
            // acting: a walk and a placement, and something lying unconscious
            // on the floor can do neither. The cost is that a creature whose
            // bed is sold while it lies dying can no longer be carried anywhere
            // — Q107, and it costs nothing today because carrying does not
            // exist.
            if (CreatureDeathRules.IsDown(creature)) continue;

            // A creature walking out of the dungeon does not stop for a nap,
            // and above all does not go for a bed on its way past the lair —
            // which it would, having given its own up when it decided to go
            // (CreatureAngerSystem.Leave). Without this line a deserter takes
            // the last free tile out from under a creature that intends to
            // stay, and then leaves with it.
            if (creature.Activity == CreatureActivity.Leaving) continue;

            switch (creature.Activity)
            {
                case CreatureActivity.Sleeping:
                    Sleep(context, creature, definition);
                    break;

                case CreatureActivity.GoingToSleep:
                    Arriving(context, creature, definition);
                    break;

                case CreatureActivity.SeekingLair:
                    creature.AwakeTicks++;
                    Seeking(context, creature, definition);
                    break;

                default:
                    creature.AwakeTicks++;
                    if (bedIndex >= 0)
                    {
                        if (LairRules.WantsToSleep(database, definition, creature))
                            SetOff(context, creature, definition);
                        break;
                    }

                    // No bed. An idle creature's first act is to go and get
                    // one (Q150) — and a tired one's too, which is the same
                    // attempt. Only from idle: a non-worker has no other
                    // state to be in today, and when it has, the attempt
                    // belongs at its idle decision, not mid-job.
                    if (creature.Activity != CreatureActivity.Idle) break;

                    // Same countdown shape as an imp's idle delay: a failed
                    // attempt sets the timer, and the next one runs when it
                    // has counted down — every IdleRedecisionTicks exactly.
                    creature.TimerTicks--;
                    if (creature.TimerTicks > 0) break;

                    if (!TrySetOutForALair(context, creature, definition))
                        creature.TimerTicks = LairRules.IdleRedecisionTicks;
                    break;
            }
        }
    }

    // ---- getting a bed ----------------------------------------------------

    /// <summary>
    /// The attempt: choose a room and set off for it, or report why not.
    ///
    /// <para>The finder keeps two answers as it walks the keeper's lairs in
    /// order of distance: the nearest reachable one with a free slot, and the
    /// nearest such that also holds no bed of the other side. The second wins
    /// when it exists. Reach is a real route over this creature's own
    /// <see cref="WorldPathGrid"/>, planned once here and then walked, so a
    /// lair behind water is reachable for a goblin and not for a vampire.</para>
    ///
    /// <para>Rooms are visited nearest first so that the path search — the
    /// expensive part — stops at the first preferred room it can reach; the
    /// nearest reachable room of any kind is remembered on the way as the
    /// fallback. Equal distances fall back to the room with the lowest tile
    /// index, which is <see cref="RoomIndex.Build"/>'s own order and is
    /// reproducible (ADR-0001).</para>
    ///
    /// <para>The failure reasons are the original's three interface messages —
    /// no lair, every lair full, every lair unreachable — and nothing here
    /// invents a grace period for them.</para>
    /// </summary>
    private bool TrySetOutForALair(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var candidates = new List<(long Distance, int FirstTile, RoomInstance Room)>();
        int lairs = 0;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != creature.OwnerPlayerId) continue;
            if (!LairRules.IsLairRoom(database, room)) continue;
            lairs++;

            if (!LairRules.HasFreeSlot(world, database, room)) continue;
            candidates.Add((
                LairRules.DistanceToCentreSquared(room, creature), room.TileIndices[0], room));
        }

        candidates.Sort((a, b) =>
            a.Distance != b.Distance
                ? a.Distance.CompareTo(b.Distance)
                : a.FirstTile.CompareTo(b.FirstTile));

        var grid = new WorldPathGrid(database, world, definition);
        RoomInstance? chosen = null;
        RoomInstance? fallback = null;
        bool preferred = false;
        int reachable = 0;

        foreach ((_, _, RoomInstance room) in candidates)
        {
            int arrival = LairRules.ArrivalTile(world, room);
            var at = new TilePos(arrival % world.Width, arrival / world.Width);
            if (!_pathfinder.TryFindPath(grid, creature.Tile, at, _route)) continue;
            reachable++;

            if (!LairRules.HoldsBedOfOtherAlignment(world, database, room, definition))
            {
                chosen = room;
                preferred = true;
                break;
            }

            if (fallback is null)
            {
                fallback = room;
                _fallbackRoute.Clear();
                _fallbackRoute.AddRange(_route);
            }
        }

        if (chosen is null && fallback is not null)
        {
            chosen = fallback;
            _route.Clear();
            _route.AddRange(_fallbackRoute);
        }

        if (chosen is null)
        {
            string reason = lairs == 0 ? "no_lair"
                : candidates.Count == 0 ? "lair_full"
                : "lair_unreachable";
            context.Emit(SystemName, "lair_attempt_failed",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} at=({creature.Tile.X},{creature.Tile.Y}) " +
                $"reason={reason} lairs={lairs} with_slot={candidates.Count} reachable={reachable}");
            return false;
        }

        creature.Path.Clear();
        creature.Path.AddRange(_route);
        creature.PathIndex = 0;
        creature.TimerTicks = 0;
        creature.Activity = CreatureActivity.SeekingLair;

        TilePos centre = LairRules.CentreTile(chosen);
        int target = LairRules.ArrivalTile(world, chosen);
        context.Emit(SystemName, "lair_sought",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} at=({creature.Tile.X},{creature.Tile.Y}) " +
            $"room_centre=({centre.X},{centre.Y}) tiles={chosen.TileCount} " +
            $"free={LairRules.Capacity(chosen) - LairRules.Occupied(world, database, chosen)} " +
            $"to=({target % world.Width},{target / world.Width}) steps={_route.Count} " +
            $"{(preferred ? "preferred" : "fallback")} candidates={candidates.Count}");
        return true;
    }

    /// <summary>
    /// The lair state on arrival — at the room, or at a tile found within it.
    ///
    /// <para>Nothing happens while the creature still has route to walk. When
    /// it runs out — arrived, or stopped short because a waypoint became
    /// unwalkable — the tile underfoot is tested. Not the keeper's lair at all
    /// (sold or captured while it walked, or a blocked route): the state ends
    /// and the creature is idle, to try again at its next idle decision. Free:
    /// the bed goes down here. Taken: the local search runs from where the
    /// creature now stands, and a found tile is a new short walk in the same
    /// state; a room with no free tile sends the creature back through the
    /// finder, possibly to another lair, and only when that fails too does the
    /// state end with nothing.</para>
    /// </summary>
    private void Seeking(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.PathIndex < creature.Path.Count) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;
        TilePos at = creature.Tile;
        int here = (at.Y * world.Width) + at.X;

        if (!LairRules.IsLairTileOf(world, database, here, creature.OwnerPlayerId))
        {
            context.Emit(SystemName, "lair_attempt_failed",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} at=({at.X},{at.Y}) reason=not_in_lair");
            GiveUp(creature);
            return;
        }

        if (LairRules.IsFreeLairTile(world, database, here, creature.OwnerPlayerId))
        {
            MakeBed(context, creature, definition, here);
            return;
        }

        // Somebody's bed is on this tile — placed since the room was chosen, or
        // the arrival tile was simply already taken. Look next door.
        RoomInstance room = RoomIndex.RoomAt(world, database, here)!;
        if (LairRules.TryFindFreeTile(
                world, database, room, at, creature.OwnerPlayerId, context.Rng,
                out int found, out LairRules.TileFound how))
        {
            var to = new TilePos(found % world.Width, found / world.Width);
            var grid = new WorldPathGrid(database, world, definition);
            if (_pathfinder.TryFindPath(grid, at, to, _route))
            {
                creature.Path.Clear();
                creature.Path.AddRange(_route);
                creature.PathIndex = 0;

                context.Emit(SystemName, "lair_tile_found",
                    $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} at=({at.X},{at.Y}) " +
                    $"taken=({at.X},{at.Y}) to=({to.X},{to.Y}) " +
                    $"how={how.ToString().ToLowerInvariant()} steps={_route.Count}");
                return;
            }
        }

        // This room has nothing for it. Another lair, if there is one.
        if (TrySetOutForALair(context, creature, definition)) return;
        GiveUp(creature);
    }

    /// <summary>
    /// The bed goes down on the tile the creature is standing on, and what
    /// happens next is decided by health alone.
    ///
    /// <para><b>Moving house</b> is the original's rule and is kept although no
    /// path here reaches it: a creature that finds a tile while owning a bed in
    /// another room destroys the old bed. Here a bed is either valid — and its
    /// owner never seeks — or already released, so the branch is a guard with
    /// an event, not a mechanic anybody will see.</para>
    /// </summary>
    private static void MakeBed(
        SimulationContext context, CreatureState creature, CreatureDef definition, int tileIndex)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        int old = LairRules.BedTileIndex(world, database, creature);
        if (old >= 0 && old != tileIndex)
        {
            LairRules.ReleaseBed(world, database, creature);
            context.Emit(SystemName, "moved_house",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} from={old} to={tileIndex}");
        }

        LairRules.PlaceBed(world, database, creature, tileIndex);
        creature.Path.Clear();
        creature.PathIndex = 0;
        creature.TimerTicks = 0;

        TilePos at = creature.Tile;
        context.Emit(SystemName, "bed_claimed",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} object={definition.LairObjectId} " +
            $"tile=({at.X},{at.Y}) health={creature.Health}/{definition.Hp}");

        if (CreatureHealthRules.ShouldSleepFromWounds(database, definition, creature))
        {
            FallAsleep(context, creature, definition);
            return;
        }

        creature.Activity = CreatureActivity.Idle;
    }

    /// <summary>The attempt ended with nothing. Idle, and try again later.</summary>
    private static void GiveUp(CreatureState creature)
    {
        creature.Activity = CreatureActivity.Idle;
        creature.Path.Clear();
        creature.PathIndex = 0;
        creature.TimerTicks = LairRules.IdleRedecisionTicks;
    }

    // ---- sleeping in a bed it has ------------------------------------------

    /// <summary>Routes a creature to its own bed.</summary>
    private void SetOff(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
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

        FallAsleep(context, creature, definition);
    }

    private static void FallAsleep(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        creature.Activity = CreatureActivity.Sleeping;
        creature.SleepTicks = 0;
        creature.Path.Clear();
        creature.PathIndex = 0;

        TilePos at = creature.Tile;
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
