// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>Task selection: which job a worker takes next, and taking it.</summary>
public sealed partial class ImpTaskSystem
{
    /// <summary>
    /// Chooses a job, in the two tiers of the type comment.
    /// <para><b>Tier A — kind first, one whole-map search per kind</b>, in DK2's
    /// table order: claim the tile just dug (kind 14), carry a loose object (8),
    /// build placed traps and doors (9, 10), dig and mine marked tiles
    /// (1), claim floor (3), carry the unconscious (13), carry a corpse (12) —
    /// <c>imp-task-priority.md</c> §4, ADR-0058/0063/0065. Walking outwards and
    /// stopping at the first tile a job can be worked from gives the nearest job
    /// in steps, its route, and rules out the unreachable without sorting a
    /// candidate list. <b>Tier B — position first</b>, only once all of that is
    /// empty: <see cref="TrySweep"/>.</para>
    /// </summary>
    private bool TryTakeJob(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        // First, the kind of the job just ended (DK2's imp +0x98, written by every job handler
        // as it takes a job and zeroed by state 11 on a drop, so a dropped worker skips this).
        // The aimed retry below is one arm; the other is a per-kind record (0x006986A0) whose
        // only flag any kind sets is "this kind again": dig, mine and claim look for more of
        // their own kind, anywhere, before the tiers. So a worker that has claimed a tile
        // claims the nearest next one, and a claimed corridor runs on to the room at its end.
        ImpTaskKind last = creature.LastTaskKind;
        if (!creature.JustDropped && last != ImpTaskKind.None)
        {
            if (creature.DroppedTask == last)
            {
                // Still on the kind it was aimed at, and its last job has ended. More of
                // that kind anywhere on the map and the aimed bonus carries over; none
                // and this is the one place in DK2 that clears it.
                if (TryTakeJobOfKind(context, creature, definition, grid, last)) return true;

                context.Emit(SystemName, "aim_spent",
                    $"imp={creature.Id} player={creature.OwnerPlayerId} " +
                    $"task={creature.DroppedTask} reason=no_more_of_that_kind");
                creature.DroppedTask = ImpTaskKind.None;
            }

            // Not an else: a failed aimed retry falls through to the per-kind record
            // (0x00463EEC → 0x00463EF3), so a claim is searched for a second time and its
            // ring scan draws a second rand(4) (0x00468D54). A dig or mine repeat draws nothing.
            if (last is ImpTaskKind.Excavate or ImpTaskKind.Mine or ImpTaskKind.Claim
                && TryTakeJobOfKind(context, creature, definition, grid, last))
            {
                return true;
            }

            // Kind 15, after a dig or a mine and only then: nothing more to excavate, so put
            // down what it holds. Digging on comes first, so an imp whose seam ran out beside
            // another marked wall digs that with the load still in its hands.
            // A deposit keeps the kind (0x00464005 returns before the zeroing at 0x00464010),
            // so the walk to the store can claim on the way.
            if (last is ImpTaskKind.Excavate or ImpTaskKind.Mine
                && creature.GoldHeld > 0 && TryDepositTheLoad(context, creature))
                return true;
            creature.LastTaskKind = ImpTaskKind.None;
        }

        // The keeper's hand: a just-dropped worker looks around where it landed
        // before the tiers (ADR-0069).
        if (creature.JustDropped)
        {
            // The ticket is spent whether or not the look finds anything - DK2
            // clears it unconditionally, one instruction before the scan.
            creature.JustDropped = false;

            if (TryTakeDroppedJob(context, creature, definition, grid))
            {
                creature.DroppedTask = creature.LastTaskKind;
                context.Emit(SystemName, "dropped_onto_work",
                    $"imp={creature.Id} player={creature.OwnerPlayerId} " +
                    $"task={creature.DroppedTask} within={DropScanTiles}");
                return true;
            }

            context.Emit(SystemName, "dropped_onto_nothing",
                $"imp={creature.Id} player={creature.OwnerPlayerId} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) within={DropScanTiles}");

            // And on through the ordinary chain. A worker dropped in an empty
            // corridor still finds work - it just does not get paid extra for it.
        }

        // Tier A: kind first. One search per kind over the whole map, not one
        // scored search over a mixed list, because the order is an ordering.
        // DK2's table opens with kind 14, but its search 0x00468520 has no arm for
        // 14 (jump table 0x00468770 sends it to the default), so the just-dug claim
        // is taken only on the way (TryClaimOnTheWay, ADR-0286).

        // Kind 8 - carry a loose object to its room - is SECOND in DK2's table,
        // above marked excavation and above claiming, so an imp puts a book away
        // before it digs a wall the keeper marked (ADR-0065).
        if (TryTakeObject(context, creature, definition, grid)) return true;

        // Kinds 9 and 10, third: a crate from a Workshop to a blueprint (ADR-0214).
        if (TryTakeBlueprint(context, creature, grid)) return true;

        if (TryTakeMarkedExcavation(context, creature, grid)) return true;
        if (TryTakeClaim(context, creature, grid)) return true;

        // The last two entries of DK2's Tier A table, in its order: kind 13,
        // carry an unconscious creature, then kind 12, a corpse. Both below
        // claiming, so an imp finishes claiming the dungeon before it picks
        // anybody up - the original's ordering, and not an obvious one.
        if (TryTakeBody(context, creature, definition, grid, corpses: false)) return true;
        if (TryTakeBody(context, creature, definition, grid, corpses: true)) return true;

        // Tier B: position first.
        if (TrySweep(context, creature, definition, grid)) return true;

        // Nothing anywhere. DK2's only zeroing of the last kind is the one above the drop
        // scan (0x00464010), so it is already None here; with no kind to match, the
        // DroppedTask comparison can never fire again, and an aimed worker that runs out of
        // work keeps its bonus rather than losing it. The write below is a guard, not DK2's.
        creature.LastTaskKind = ImpTaskKind.None;
        return false;
    }

    /// <summary>
    /// What a worker the keeper has just put down looks for, and where.
    /// <para><b>A different order from <see cref="TryTakeJob"/>'s</b>: DK2's drop
    /// scan is one composite per-tile predicate, ordered excavation, claiming,
    /// traps and doors, carrying a body, reinforcing, gold, objects, corpses — so a
    /// worker dropped beside a marked wall digs it rather than putting a book away
    /// (ADR-0065, ADR-0214). One of its nine kinds is unidentified; kind 14,
    /// claiming the just-dug tile, is not among them. All
    /// of it inside <see cref="DropScanTiles"/> of where the worker landed,
    /// measured to <b>the tile the work is at</b> — CONFIRMED, owner's count: a
    /// marked wall four away does not pay, though a floor tile three does.</para>
    /// </summary>
    private bool TryTakeDroppedJob(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        // The ring scan of radius 0x2000 (0x0046402C): the landing tile, then diamond rings 1 to 3
        // from the chooser's side, each tile the filter admits given the chain 0x00469240 in its
        // order (Q488, ADR-0292). The kinds without a per-tile form search the one tile.
        WorldState world = context.World;
        TilePos at = creature.Tile;
        PlayerId player = creature.OwnerPlayerId;
        bool handsFree = !HandsFull(context, creature, definition);
        var rings = new DiamondRings(world, at, ChosenStartSide(context, ClaimStartSide(context, world, at, player)));
        try
        {
            while (rings.MoveNext())
            {
                TilePos tile = rings.Current;
                if (Math.Abs(tile.X - at.X) + Math.Abs(tile.Y - at.Y) > DropScanTiles) return false;
                if (!RingScanVisits(context, world, tile, at)) continue;
                _only = tile;
                if (TryExcavationAt(context, creature, grid, tile, player)) return true;
                if (TryClaimAt(context, creature, grid, tile)) return true;
                if (TryTakeBlueprintNear(context, creature, grid)) return true;
                if (TryTakeBody(context, creature, definition, grid, corpses: false)) return true;
                if (TryReinforceAt(context, creature, grid, tile)) return true;
                if (handsFree && CanFetchFrom(context, tile, player, creature.Id)
                    && Reaches(grid, creature, tile)
                    && CommitFetch(context, creature, tile))
                    return true;
                if (TryTakeObject(context, creature, definition, grid)) return true;
                if (TryTakeBody(context, creature, definition, grid, corpses: true)) return true;
                if (TrySecretDoorAt(context, creature, tile)) return true;
            }
            return false;
        }
        finally
        {
            _only = null;
        }
    }

    /// <summary>
    /// One kind, whole map - DK2's <c>TakeJob(kind, 0x7fffffff)</c>, the retry
    /// that decides whether an aimed worker keeps its bonus.
    ///
    /// <para><see cref="ImpTaskKind.ClaimFreshlyDug"/> cannot arrive here:
    /// <see cref="KindOf"/> records kind 14's work as an ordinary
    /// <see cref="ImpTaskKind.Claim"/>, which is what DK2 stores.</para>
    /// </summary>
    private bool TryTakeJobOfKind(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        WorldPathGrid grid, ImpTaskKind kind)
    {
        return kind switch
        {
            ImpTaskKind.Excavate or ImpTaskKind.Mine => TryTakeMarkedExcavation(context, creature, grid),
            ImpTaskKind.Claim => TryTakeClaim(context, creature, grid),
            ImpTaskKind.Reinforce or ImpTaskKind.Fix => TryTakeReinforce(context, creature, grid),
            ImpTaskKind.FetchGold => HandsFull(context, creature, definition)
                ? TryDepositTheLoad(context, creature)
                : TryTakeFloorGold(context, creature, definition, grid),
            ImpTaskKind.CarryObject => TryTakeObject(context, creature, definition, grid),
            ImpTaskKind.BuildTrap or ImpTaskKind.BuildDoor => TryTakeBlueprint(context, creature, grid),
            ImpTaskKind.SecretDoor => TryTakeSecretDoor(context, creature),
            ImpTaskKind.CarryCorpse => TryTakeBody(context, creature, definition, grid, corpses: true),
            ImpTaskKind.CarryUnconscious => TryTakeBody(context, creature, definition, grid, corpses: false),
            _ => false,
        };
    }

    /// <summary>
    /// The dispatcher's kind 7 alone (<c>0x00468520</c>): the ring scan over the whole map with
    /// the gold handler <c>0x0046B6D0</c>, from <see cref="DrawnStartSide"/>, the first tile
    /// that gives a fetch winning. The scan's own tile is unfiltered; every ring tile passes
    /// <see cref="RingScanVisits"/>, which never admits water, so a pile that fell in water is
    /// fetched only from under the imp's feet. Level 2 seed01: imp 430, aimed at gold, twice
    /// walks past the 273 at (9,24) in water for a pile on its own floor (ADR-0343).
    /// <para><see cref="TrySweep"/> asks the same question beside the reinforce one, because in
    /// Tier B distance is the primary key; here it is asked alone, for the same-kind retry.</para>
    /// </summary>
    private bool TryTakeFloorGold(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        if (HandsFull(context, creature, definition)) return false;

        PlayerId player = creature.OwnerPlayerId;
        WorldState world = context.World;
        var rings = new DiamondRings(world, creature.Tile, DrawnStartSide(context));
        while (rings.MoveNext())
        {
            TilePos tile = rings.Current;
            if (!InScanRange(tile)) continue;
            if (!RingScanVisits(context, world, tile, creature.Tile)) continue;
            if (!CanFetchFrom(context, tile, player, creature.Id)) continue;
            if (!Reaches(grid, creature, tile)) continue;
            if (CommitFetch(context, creature, tile)) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a search may stop here - always, unless a drop scan is running.
    /// </summary>
    private bool InScanRange(TilePos at)
    {
        return _only is not TilePos only || at == only;
    }

    /// <summary>
    /// The task kind DK2 records for a job of this priority.
    ///
    /// <para><see cref="JobPriority.ClaimFreshlyDug"/> maps to
    /// <see cref="ImpTaskKind.Claim"/> because the original's kind 14 raises a
    /// kind 3 at the tile and takes that; the byte it leaves behind is a 3.</para>
    /// </summary>
    private static ImpTaskKind KindOf(JobPriority priority, TileWorkKind kind)
    {
        return priority switch
        {
            _ when kind == TileWorkKind.MineGold => ImpTaskKind.Mine,
            _ when kind == TileWorkKind.RepairWall => ImpTaskKind.Fix,
            JobPriority.MarkedExcavation => ImpTaskKind.Excavate,
            JobPriority.Reinforce => ImpTaskKind.Reinforce,
            _ => ImpTaskKind.Claim,
        };
    }

    /// <summary>
    /// Tier B (<c>0x004640bb</c>): the ring scan from the worker over the whole map, started on
    /// the side <see cref="ClaimStartSide"/> picks, with the chain <c>0x00469580</c> at each tile:
    /// the tile as a wall to reinforce or repair (<see cref="TryReinforceAt"/>), then the gold on
    /// it, then its loose objects (<c>0x0046B9C0</c>, <see cref="TryObjectAt"/>, ADR-0519). The first
    /// tile that gives a job wins, so a wall one ring nearer beats a pile under the next ring.
    /// The fourth step exposes a marked Secret Door (<c>0x0046CB80</c>, kind 11; ADR-0536).
    /// </summary>
    private bool TrySweep(
        SimulationContext context, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        WorldState world = context.World;
        PlayerId player = creature.OwnerPlayerId;
        bool handsFree = !HandsFull(context, creature, definition);
        TilePos at = creature.Tile;

        var rings = new DiamondRings(world, at, ChosenStartSide(context, ClaimStartSide(context, world, at, player)));
        while (rings.MoveNext())
        {
            TilePos tile = rings.Current;
            if (!RingScanVisits(context, world, tile, at)) continue;
            if (TryReinforceAt(context, creature, grid, tile)) return true;
            if (handsFree && CanFetchFrom(context, tile, player, creature.Id)
                && Reaches(grid, creature, tile)
                && CommitFetch(context, creature, tile))
                return true;
            // A guard of ours with no counterpart in 0x00469580 or state 10 (0x00463DF0), the same
            // as TryTakeObject's: state 10 follows a drop, so a carrying imp should not get here.
            if (creature.CarriedObjectId < 0 && creature.CarriedCreatureId < 0
                && TryObjectAt(context, creature, grid, tile))
                return true;
            if (TrySecretDoorAt(context, creature, tile)) return true;
        }
        return false;
    }

    /// <summary>
    /// The reinforce handler (<c>0x0046aea0</c>) on one wall: its four sides from the one
    /// <see cref="MiningSlots.StartSide"/> gives for the worker, each a stand of the keeper's own
    /// floor, not solid (an owned reinforced wall beside it is not a side); the first whose face is free, or held by a farther worker, and which the worker can
    /// reach is taken. A free side it cannot reach deletes the wall's task (<c>0x00508040</c>),
    /// whoever else holds a side, and the next side is tried (<see cref="ForgetReinforceTask"/>).
    /// imp-task-stealing.md §4.
    /// </summary>
    private bool TryReinforceAt(SimulationContext context, CreatureState creature, WorldPathGrid grid, TilePos wall)
    {
        WorldState world = context.World;
        if (!context.Database.TerrainById(world.Tile(wall).TerrainId).IsSolid) return false;
        (int side, int step) = MiningSlots.StartSide(creature.Tile.X - wall.X, creature.Tile.Y - wall.Y);
        for (int turn = 0; turn < 4; turn++, side = (side + step) & 3)
        {
            var stand = new TilePos(wall.X + SideDx[side], wall.Y + SideDy[side]);
            // A side counts only when it is not solid (and the keeper's, which TryJobOn tests).
            if (!world.Contains(stand) || context.Database.TerrainById(world.Tile(stand).TerrainId).IsSolid) continue;
            if (!TryJobOn(context, grid, stand, wall, creature.OwnerPlayerId, JobPriority.Reinforce, creature.Id,
                          out int tileIndex, out TileWorkKind kind))
                continue;
            if (!Reaches(grid, creature, stand)
                || !CommitJob(context, creature, tileIndex, kind, JobPriority.Reinforce))
            {
                if (!ImpCrowding.FaceIsFull(world, context.Database, tileIndex, stand, creature.Id, false, kind))
                    ForgetReinforceTask(context, tileIndex, creature.Id);
                continue;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// The wall's kind-6 task deleted (<c>0x00508040</c>) after a walk to a free side failed
    /// (<c>0x0046AEA0</c>): every other worker holding a side of it loses the task and goes to 10,
    /// as state 22's check <c>0x00508070(+0x8A, 6)</c> at <c>0x004663C4</c> sends it once no
    /// action, or action 0x15, is playing. Ours drops them at once; DK2 drops a reinforcer at its
    /// next check, and a walker presumably on arrival (unread) (ADR-0380).
    /// </summary>
    private static void ForgetReinforceTask(SimulationContext context, int wallIndex, int askingCreatureId)
    {
        int pool = TaskStealing.PoolOf(context.World, askingCreatureId);
        foreach (CreatureState other in context.World.Creatures)
        {
            if (other.Id == askingCreatureId || other.TargetTileIndex != wallIndex
                || !TaskStealing.InPool(pool, other)
                || other.TargetWork is not (TileWorkKind.Reinforce or TileWorkKind.RepairWall))
                continue;
            context.Emit(SystemName, "task_forgotten",
                $"imp={other.Id} by={askingCreatureId} {Describe(context.World, wallIndex)} {other.TargetWork}");
            LoseTask(context, other);
        }
    }

    /// <summary>
    /// The ring scan's tile filter (<c>0x00468d10</c>): tile byte <c>+0xB</c> bit <c>0x20</c>,
    /// recomputed by <c>0x004502d0</c>, is clear when the tile or one of its four neighbours is
    /// owned, the tile is not water or lava, and it or a neighbour is open floor. A set tile is
    /// visited only when it carries a dig or mine mark (Q487). The scan's own centre is not
    /// filtered: <c>0x00468D10</c> calls the chain on it at <c>0x00468DA0</c> with no <c>+0xB</c>
    /// test, which only the ring loops make (<c>0x00468E99</c>, <c>0x00468F71</c>; ADR-0379).
    /// </summary>
    private static bool RingScanVisits(SimulationContext context, WorldState world, TilePos at, TilePos centre)
    {
        if (at == centre) return true;
        ref TileState tile = ref world.Tile(at);
        if (tile.IsTaggedByAnyone) return true;
        TerrainDef terrain = context.Database.TerrainById(tile.TerrainId);
        if (terrain.IsWater || terrain.IsLava) return false;
        bool owned = IsOwned(tile.OwnerPlayerId), open = !terrain.IsSolid;
        for (int i = 0; i < 4 && !(owned && open); i++)
        {
            var n = new TilePos(at.X + SideDx[i], at.Y + SideDy[i]);
            if (!world.Contains(n)) continue;
            ref TileState next = ref world.Tile(n);
            TerrainDef nt = context.Database.TerrainById(next.TerrainId);
            owned |= IsOwned(next.OwnerPlayerId);
            open |= !nt.IsSolid && !nt.IsWater && !nt.IsLava;
        }
        return owned && open;

        static bool IsOwned(PlayerId owner) => owner.Value is not (PlayerIds.Unowned or PlayerIds.Neutral);
    }

    /// <summary>
    /// Where the ring scan starts when its caller passes no chooser (the dispatcher's kinds 6, 7,
    /// 8 and 16): <c>rand(4)</c>. <c>0x00468d10</c> draws it from the world's random word into the
    /// side slot on entry, before anything else (<c>0x00468d54</c>, <c>0x00468d60</c>), on every
    /// call (ADR-0351, Q526; ADR-0294 had dropped it on a wrong reading of that slot).
    /// </summary>
    private static int DrawnStartSide(SimulationContext context) => context.Rng.NextInt(4);

    /// <summary>A scan with a chooser still makes <see cref="DrawnStartSide"/>'s draw on entry; the
    /// chooser then overwrites the side (<c>0x00468deb</c>).</summary>
    private static int ChosenStartSide(SimulationContext context, int chosen)
    {
        DrawnStartSide(context);
        return chosen;
    }

    /// <summary>The dispatcher's kinds 6 and 16 (<c>0x00468520</c>): the ring scan over the whole
    /// map with the reinforce handler alone, started on <see cref="DrawnStartSide"/>, not the chooser's.
    /// ADR-0292.</summary>
    private bool TryTakeReinforce(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        var rings = new DiamondRings(world, creature.Tile, DrawnStartSide(context));
        while (rings.MoveNext())
        {
            TilePos tile = rings.Current;
            if (RingScanVisits(context, world, tile, creature.Tile) && TryReinforceAt(context, creature, grid, tile)) return true;
        }
        return false;
    }

    /// <summary>
    /// DK2's dig-and-mine walk (<c>0x00469AF0</c>, kinds 1 and 2 alike): every marked wall,
    /// then every marked seam, ranked by <b>straight-line</b> distance from the worker to the
    /// tile's centre, and the nearest with a face it can work from is taken. A later task
    /// replaces the best only when strictly nearer, so a wall beats a seam at the same
    /// distance. After a mining job, seams are measured from the seam just mined instead of
    /// from the worker. Of two walls at one distance the later marked comes first: DK2's list
    /// order, which its diggers' off-centre stance makes decide real choices (ADR-0288).
    /// <para>The drop scan hands each tile of its rings to <see cref="TryExcavationAt"/> instead.</para>
    /// </summary>
    private bool TryTakeMarkedExcavation(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        PlayerId player = creature.OwnerPlayerId;
        int width = world.Width;
        bool fromSeam = creature.LastTaskKind == ImpTaskKind.Mine && creature.LastTaskTileIndex >= 0;
        long seamX = fromSeam ? (creature.LastTaskTileIndex % width) * 4096L + 2048 : 0;
        long seamY = fromSeam ? (creature.LastTaskTileIndex / width) * 4096L + 2048 : 0;

        // The walk drops every task of its kinds whose tile can no longer be dug (0x00469BC8,
        // 0x00469EBF), so a digger whose action is still running then raises no kind 14 (ADR-0305).
        _excavation.Clear();
        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                if (tile.DigTaskOutlivesWallFor.Contains(player))
                {
                    tile.DigTaskOutlivesWallFor = tile.DigTaskOutlivesWallFor.Without(player);
                    context.Emit(SystemName, "dig_task_dropped", $"imp={creature.Id} {Describe(world, y * width + x)}");
                }
                if (!tile.IsTaggedBy(player)) continue;
                if (!TileWorkRules.TryTaggedWork(context.Database, tile, player, out TileWorkKind kind)) continue;
                if (!HasOpenFace(world, context.Database, x, y)) continue;
                bool seam = kind == TileWorkKind.MineGold;
                long dx = x * 4096L + 2048 - (seam && fromSeam ? seamX : creature.PosX);
                long dy = y * 4096L + 2048 - (seam && fromSeam ? seamY : creature.PosY);
                _excavation.Add(new ExcavationCandidate(dx * dx + dy * dy, seam, tile.TagOrder, y * width + x));
            }
        }
        _excavation.Sort();

        // The walk's one choice is taken or lost: a failed walk gives the whole selection up
        // (0x00469AF0's tail returns 0), it does not move on to the next wall (Q590).
        foreach (ExcavationCandidate candidate in _excavation)
        {
            var target = new TilePos(candidate.TileIndex % width, candidate.TileIndex / width);
            if (TryExcavationAt(context, creature, grid, target, player)) return true;
            if (_excavationWalkFailed) return false;
        }
        return false;
    }

    /// <summary>
    /// One wall of DK2's dig-and-mine walk (<c>0x00469AF0</c>): its four sides from the one facing
    /// the worker, or facing the seam it last mined (<see cref="MiningSlots.StartSide"/>), each open
    /// and reachable side's three slots in order. The first free slot is taken. A held one counts
    /// only when the worker is nearer the wall's centre than the holder is to its side tile's centre;
    /// for a mine, when the worker, from where it stands, is nearer the side tile's corner than the
    /// holder is. The last such is taken if no slot is free, the
    /// holder set to 10. A slot whose cell's pile is over the limit is passed over when mining
    /// (Q140). Q155, ADR-0289.
    /// </summary>
    private bool TryExcavationAt(
        SimulationContext context, CreatureState creature, WorldPathGrid grid, TilePos target, PlayerId player)
    {
        _excavationWalkFailed = false;
        WorldState world = context.World;
        int targetIndex = target.Y * world.Width + target.X;
        bool afterMining = creature.LastTaskKind == ImpTaskKind.Mine && creature.LastTaskTileIndex >= 0;
        TilePos from = afterMining
            ? new TilePos(creature.LastTaskTileIndex % world.Width, creature.LastTaskTileIndex / world.Width)
            : creature.Tile;
        (int side, int step) = MiningSlots.StartSide(from.X - target.X, from.Y - target.Y);

        TilePos? taken = null;
        byte takenSlot = MiningSlots.None;
        TileWorkKind takenWork = default;
        CreatureState? takenFrom = null;
        long mine = -1;
        for (int turn = 0; turn < 4; turn++, side = (side + step) & 3)
        {
            var stand = new TilePos(target.X + SideDx[side], target.Y + SideDy[side]);
            if (!world.Contains(stand) || context.Database.TerrainById(world.Tile(stand).TerrainId).IsSolid) continue;
            if (!TryJobOn(context, grid, stand, target, player, JobPriority.MarkedExcavation, creature.Id,
                          out _, out TileWorkKind work, crowding: false))
                continue;
            if (!Reaches(grid, creature, stand)) continue;

            bool mining = work == TileWorkKind.MineGold;
            if (!PointReachable(context, creature, mining ? stand.X * 4096 : CreatureState.CentreOf(stand.X),
                    mining ? stand.Y * 4096 : CreatureState.CentreOf(stand.Y)))
                continue;
            if (mining)
                mine = TaskStealing.DistanceSquared(creature.PosX, creature.PosY, stand.X * 4096L, stand.Y * 4096L);
            else if (mine < 0)
                mine = TaskStealing.DistanceSquared(creature.PosX, creature.PosY,
                    CreatureState.CentreOf(target.X), CreatureState.CentreOf(target.Y));
            (long px, long py) = mining
                ? (stand.X * 4096L, stand.Y * 4096L)
                : (CreatureState.CentreOf(stand.X), CreatureState.CentreOf(stand.Y));

            int face = MiningSlots.Face(stand, target);
            int standIndex = stand.Y * world.Width + stand.X;
            for (int along = 0; along < MiningSlots.PerFace; along++)
            {
                byte slot = (byte)(face * MiningSlots.PerFace + along);
                if (work == TileWorkKind.MineGold
                    && FloorGold.PileInCell(world, standIndex, MiningSlots.Cell(slot)) > FloorGold.MaxPile(context.Database))
                    continue;
                CreatureState? holder = MiningSlots.Holder(world, targetIndex, slot, creature.Id);
                if (holder is null)
                {
                    (taken, takenSlot, takenWork, takenFrom) = (stand, slot, work, null);
                    break;
                }
                if (!TaskStealing.CanLoseItsTask(holder)) continue;
                if (mine < TaskStealing.DistanceSquared(holder.PosX, holder.PosY, px, py))
                    (taken, takenSlot, takenWork, takenFrom) = (stand, slot, work, holder);
            }
            if (taken is not null && takenFrom is null) break;
        }

        if (taken is not TilePos chosen) return false;
        if (!Reaches(grid, creature, chosen)) return false;
        if (takenFrom is not null) TakeTaskFrom(context, takenFrom, creature, targetIndex);
        if (CommitJob(context, creature, targetIndex, takenWork, JobPriority.MarkedExcavation, takenSlot)) return true;
        _excavationWalkFailed = true;
        return false;
    }

    /// <summary><c>Route_IsPointReachable(point, 0x80)</c> (<c>0x00471630</c>): a route query that
    /// respects doors. <c>0x00469AF0</c> asks it of each dig side (the side tile's centre, or its
    /// corner for a mine), and the objects and body handlers of the thing's own position, before
    /// walking to or stealing it; the selection grid has no doors (Q590, ADR-0472).</summary>
    private bool PointReachable(SimulationContext context, CreatureState creature, int x, int y)
    {
        _reachPoints.Clear();
        return context.Navigation.Route(
            new Steering.Fixed16Vector(creature.PosX << 4, creature.PosY << 4), new Steering.Fixed16Vector(x << 4, y << 4),
            Navigation.WorldSteering.ProfileOf(context.Database.CreatureById(creature.CreatureTypeId),
                creature.OwnerPlayerId, Navigation.MoveOptions.Loose),
            _reachPoints, refresh: false);
    }

    private readonly List<Steering.Fixed16Vector> _reachPoints = [];

    /// <summary>Set when <see cref="TryExcavationAt"/> chose a slot and its walk could not start.</summary>
    private bool _excavationWalkFailed;

    /// <summary>DK2's four sides in its own order (<c>0x00698A60</c>): north, east, south, west.</summary>
    private static readonly int[] SideDx = [0, 1, 0, -1];
    private static readonly int[] SideDy = [-1, 0, 1, 0];

    /// <summary>The handler's <c>0x0048AD30(10)</c> on the holder: it goes to 10 without its task
    /// and chooses again; its last kind stays.</summary>
    private static void TakeTaskFrom(SimulationContext context, CreatureState holder, CreatureState taker, int tileIndex)
    {
        context.Emit(SystemName, "task_stolen",
            $"imp={taker.Id} from={holder.Id} {Describe(context.World, tileIndex)} " +
            $"{(holder.SecretDoorTask ? "SecretDoor" : holder.TargetWork.ToString())} " +
            $"holder_state={holder.Dk2StateId}");
        LoseTask(context, holder);
    }

    /// <summary>A worker whose task is gone: no job, back to 10 (<c>0x0048AD30(10)</c>).</summary>
    private static void LoseTask(SimulationContext context, CreatureState holder)
    {
        holder.ClearJob();
        if (CreatureStateMachine.HasState(holder))
        {
            StateShadow.Arrive(context, SystemName, holder);
            StateShadow.Move(context, SystemName, holder, ImpStates.TaskSelection);
        }
        holder.TimerTicks = 0;
    }

    /// <summary>A claim or reinforce holder of this face the asker may take the task from: the one
    /// worker there, able to lose it, and farther from the tile's centre (Q155, ADR-0289).</summary>
    private static CreatureState? HolderToTakeFrom(
        SimulationContext context, int tileIndex, TilePos face, int askingCreatureId, TileWorkKind kind)
    {
        if (kind is not (TileWorkKind.Claim or TileWorkKind.AttackTile or TileWorkKind.ConvertRoom
                or TileWorkKind.Reinforce or TileWorkKind.RepairWall))
            return null;
        WorldState world = context.World;
        CreatureState? asking = world.CreatureById(askingCreatureId);
        if (asking is null) return null;
        int faceIndex = face.Y * world.Width + face.X;
        foreach (CreatureState other in world.Creatures)
        {
            if (other.Id == askingCreatureId || other.TargetTileIndex != tileIndex
                || other.OwnerPlayerId != asking.OwnerPlayerId) continue;
            if (ImpCrowding.FaceOf(world, other) != faceIndex) continue;
            if (!TaskStealing.CanLoseItsTask(other)) return null;
            var tile = new TilePos(tileIndex % world.Width, tileIndex / world.Width);
            return TaskStealing.NearerTheTile(asking, (other.PosX, other.PosY), tile) ? other : null;
        }
        return null;
    }

    /// <summary>DK2's first test of a face (<c>terrain+0x17B &amp; 1</c>): a side that is not
    /// solid. Cheap, so a marked tile deep in rock never costs a route search.</summary>
    private static bool HasOpenFace(WorldState world, GameDatabase database, int x, int y)
    {
        for (int i = 0; i < NeighbourDx.Length; i++)
        {
            int nx = x + NeighbourDx[i], ny = y + NeighbourDy[i];
            if (nx < 0 || ny < 0 || nx >= world.Width || ny >= world.Height) continue;
            if (!database.TerrainById(world.Tile(nx, ny).TerrainId).IsSolid) return true;
        }
        return false;
    }

    /// <summary>
    /// A keeper's worker on a walk claims a just-dug tile it steps onto (<c>0x00464170</c>, from
    /// the creature update <c>0x00488930</c>): on entering a new tile, more than two tiles (either
    /// axis) from its task tile, with a last task of kind 1–7 or 16, the kind-14 handler
    /// <c>0x00469600</c> runs on the tile underfoot — the only taker of a kind-14 task. Deposit,
    /// job and fetch walks alike: Level 1 seeds 3–5 open their first room from a walk to a dig
    /// (Q489, ADR-0303).
    /// </summary>
    private bool TryClaimOnTheWay(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        if (!PlayerIds.IsKeeper(creature.OwnerPlayerId) || creature.LastTaskTileIndex < 0) return false;
        TilePos at = creature.Tile;
        if (at.X == creature.LastPosX / Fp32.One && at.Y == creature.LastPosY / Fp32.One) return false;

        WorldState world = context.World;
        int tx = creature.LastTaskTileIndex % world.Width, ty = creature.LastTaskTileIndex / world.Width;
        if (Math.Abs(at.X - tx) <= 2 && Math.Abs(at.Y - ty) <= 2) return false;
        if ((byte)creature.LastTaskKind is not ((>= 1 and <= 7) or 16)) return false;

        ref TileState tile = ref world.Tile(at);
        if (!tile.FreshlyDug) return false;
        if (!TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, creature.OwnerPlayerId))
        {
            tile.FreshlyDug = false;
            return false;
        }
        if (!TryJobOn(context, grid, at, at, creature.OwnerPlayerId, JobPriority.ClaimFreshlyDug, creature.Id,
                      out int tileIndex, out TileWorkKind kind))
            return false;

        _route.Clear();
        context.Emit(SystemName, "claimed_on_the_way",
            $"imp={creature.Id} at=({at.X},{at.Y}) gold={creature.GoldHeld}");
        return CommitJob(context, creature, tileIndex, kind, JobPriority.ClaimFreshlyDug);
    }

    /// <summary>
    /// DK2's claim search (kind 3: <c>0x00468520</c> runs the ring scan <c>0x00468d10</c> with the
    /// claim handler <c>0x0046ab50</c>, whole map). The worker's own tile first, then diamond rings
    /// of Manhattan radius 1, 2, …, each walked clockwise from one corner to the next — north to
    /// east, east to south, south to west, west to north, both corners visited — starting at the
    /// side <see cref="ClaimStartSide"/> picks. The first tile the handler accepts is taken: the
    /// claim predicates, then a route to it (a tile with no route is passed over).
    /// <para>A claim held by a farther imp is taken through <see cref="TryJobOn"/> and
    /// <see cref="HolderToTakeFrom"/> (Q155, ADR-0289). Not built: the <c>+0xB</c> bit
    /// <c>0x20</c> pre-filter (Q487). imp-task-priority.md §2.2, ADR-0285.</para>
    /// </summary>
    private bool TryTakeClaim(SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {

        WorldState world = context.World;
        TilePos at = creature.Tile;
        var rings = new DiamondRings(world, at, ChosenStartSide(context, ClaimStartSide(context, world, at, creature.OwnerPlayerId)));
        while (rings.MoveNext())
            if (TryClaimAt(context, creature, grid, rings.Current)) return true;
        return false;
    }

    /// <summary>
    /// The ring scan's walk (<c>0x00468d10</c>): the centre first, then diamond rings of Manhattan
    /// radius 1, 2, …, each walked clockwise from one corner to the next — north to east, east to
    /// south, south to west, west to north, both corners visited — from the given start side,
    /// until a whole ring lies outside the map. imp-claim-path.md §2.
    /// </summary>
    private struct DiamondRings
    {
        private readonly WorldState _world;
        private readonly TilePos _at;
        private readonly int _side;
        private int _r, _s, _i;
        private bool _inMap;

        public DiamondRings(WorldState world, TilePos at, int side)
        {
            _world = world;
            _at = at;
            _side = side;
            _r = 0;
            _s = 0;
            _i = -1;
            _inMap = false;
            Current = at;
        }

        public TilePos Current { get; private set; }

        public bool MoveNext()
        {
            if (_r == 0)
            {
                _r = 1;
                return _world.Contains(_at);
            }
            while (true)
            {
                if (++_i > _r)
                {
                    _i = 0;
                    if (++_s == 4)
                    {
                        if (!_inMap) return false;
                        _s = 0;
                        _r++;
                        _inMap = false;
                    }
                }
                int k = (_side + _s) & 3;
                Current = k switch
                {
                    0 => new TilePos(_at.X + _i, _at.Y - _r + _i),
                    1 => new TilePos(_at.X + _r - _i, _at.Y + _i),
                    2 => new TilePos(_at.X - _i, _at.Y + _r - _i),
                    _ => new TilePos(_at.X - _r + _i, _at.Y - _i),
                };
                if (!_world.Contains(Current)) continue;
                _inMap = true;
                return true;
            }
        }
    }

    private bool TryClaimAt(SimulationContext context, CreatureState creature, WorldPathGrid grid, TilePos target)
    {
        if (!TryJobOn(context, grid, target, target, creature.OwnerPlayerId, JobPriority.Claim, creature.Id,
                      out int tileIndex, out TileWorkKind kind))
            return false;
        if (!Reaches(grid, creature, target)) return false;
        return CommitJob(context, creature, tileIndex, kind, JobPriority.Claim);
    }

    /// <summary>
    /// Which side of the ring the claim search starts on (<c>0x00469340</c>): the four neighbours
    /// in the order north, east, south, west, each graded by <see cref="ClaimSideGrade"/>, and the
    /// first of the lowest grades wins.
    /// </summary>
    private static int ClaimStartSide(SimulationContext context, WorldState world, TilePos at, PlayerId playerId)
    {
        int best = 0, bestGrade = int.MaxValue;
        for (int k = 0; k < 4; k++)
        {
            var n = k switch
            {
                0 => new TilePos(at.X, at.Y - 1),
                1 => new TilePos(at.X + 1, at.Y),
                2 => new TilePos(at.X, at.Y + 1),
                _ => new TilePos(at.X - 1, at.Y),
            };
            int grade = ClaimSideGrade(context, world, n, playerId);
            if (grade >= bestGrade) continue;
            best = k;
            bestGrade = grade;
        }
        return best;
    }

    /// <summary>
    /// A neighbour's grade for <see cref="ClaimStartSide"/>, lower first: <c>0x00469460</c> sorts the
    /// tile into a class and the byte table at <c>0x00698628</c> ranks the classes. A marked wall or
    /// seam 1; floor to take that touches the keeper's floor — a rival's room 3, a rival's floor 4,
    /// unowned floor 5; a wall to reinforce or repair (not gold, below MaxHealth, unowned or the
    /// keeper's own, touching its floor: <c>0x00453dc0</c>, <c>0x00453e30</c>) 12; anything else 127.
    /// </summary>
    private static int ClaimSideGrade(SimulationContext context, WorldState world, TilePos at, PlayerId playerId)
    {
        if (!world.Contains(at)) return 127;
        ref TileState tile = ref world.Tile(at);
        TerrainDef terrain = context.Database.TerrainById(tile.TerrainId);
        bool unowned = tile.OwnerPlayerId is PlayerIds.Unowned or PlayerIds.Neutral;
        if (!terrain.IsSolid)
        {
            if (terrain.IsWater || terrain.IsLava || tile.OwnerPlayerId == playerId
                || !TouchesOwnLand(context, world, at, playerId))
                return 127;
            return terrain.IsRoom ? 3 : unowned ? 5 : 4;
        }
        if (tile.IsTaggedBy(playerId)) return 1;
        if (!terrain.IsImpenetrable && terrain.GoldValue == 0 && tile.Health != terrain.MaxHealth
            && (unowned || tile.OwnerPlayerId == playerId) && TouchesOwnLand(context, world, at, playerId))
            return 12;
        return 127;
    }

    /// <summary>
    /// Commits a worker to tile work along the route the last search planned. False, with nothing
    /// committed, when the walk cannot start: the tile-work handlers' takes fail when their route
    /// request does (<c>0x004651A0</c> → <c>0x00471890</c> → <c>0x004D0F60</c>, imp-task-stealing.md
    /// §7, ADR-0471). A held slot is kicked after the walk starts, as claim and reinforce do; the
    /// dig caller kicks first, and the just-dug claim's walk to its own tile never fails (Q590).
    /// </summary>
    private bool CommitJob(
        SimulationContext context, CreatureState creature, int tileIndex, TileWorkKind kind, JobPriority priority,
        byte? slot = null)
    {
        TilePos standing = _route.Count > 0 ? _route[^1] : creature.Tile;
        WorldState world = context.World;
        byte workSlot = slot ?? (kind == TileWorkKind.MineGold
            ? MiningSlots.FreeSlot(world, context.Database, tileIndex, standing,
                new TilePos(tileIndex % world.Width, tileIndex / world.Width), creature.Id)
            : MiningSlots.None);

        // A digger walks to its slot's point and a reinforcer to its side's, as DK2's handlers
        // route; the rest to the tile's centre. Where it stands is what task stealing measures.
        var target = new TilePos(tileIndex % world.Width, tileIndex / world.Width);
        int face = MiningSlots.Face(standing, target);
        bool walks;
        if (workSlot != MiningSlots.None)
        {
            (long sx, long sy) = MiningSlots.StancePoint(standing, workSlot);
            walks = context.Navigation.TryRouteToStance(creature, sx, sy, Navigation.MoveOptions.ExactSpot);
        }
        else if (kind is TileWorkKind.Reinforce or TileWorkKind.RepairWall && face >= 0)
        {
            (long sx, long sy) = TaskStealing.ReinforceStance(standing, face);
            walks = context.Navigation.TryRouteToStance(creature, sx, sy, Navigation.MoveOptions.ExactSpot);
        }
        else if (kind is TileWorkKind.Claim or TileWorkKind.AttackTile or TileWorkKind.ConvertRoom)
        {
            // The claim handler walks to the centre even from inside the target tile.
            walks = context.Navigation.TryRouteToStance(creature, CreatureState.CentreOf(target.X),
                CreatureState.CentreOf(target.Y), Navigation.MoveOptions.Spot);
        }
        else
        {
            walks = context.Navigation.TryRoute(creature, standing, Navigation.MoveOptions.Spot);
        }
        if (!walks)
        {
            context.Emit(SystemName, "job_unreachable",
                $"imp={creature.Id} {Describe(world, tileIndex)} {kind} priority={priority}");
            return false;
        }

        bool onTheTile = standing.Y * world.Width + standing.X == tileIndex;
        if (slot is null && ImpCrowding.FaceIsFull(world, context.Database, tileIndex, standing, creature.Id, onTheTile, kind)
            && HolderToTakeFrom(context, tileIndex, standing, creature.Id, kind) is CreatureState holder)
            TakeTaskFrom(context, holder, creature, tileIndex);

        creature.TargetTileIndex = tileIndex;
        creature.TargetWork = kind;
        creature.SecretDoorTask = false;
        creature.WorkSlot = workSlot;

        // A worker already on its stance tile still walks to its point (0x004651A0 always walks),
        // so a route loaded to the point counts, not only one the search found steps for.
        bool routed = _route.Count > 0 || creature.IsRouted;
        if (!routed) creature.TimerTicks = WorkCadence.FirstApplicationTicks;

        SetOutInState(context, creature, kind, routed);

        creature.LastTaskKind = KindOf(priority, kind);
        creature.LastTaskTileIndex = tileIndex;

        context.Emit(SystemName, "job_taken",
            $"imp={creature.Id} player={creature.OwnerPlayerId} " +
            $"{Describe(context.World, tileIndex)} {kind} priority={priority} steps={_route.Count}");
        return true;
    }

    /// <summary>A job copied from a DK2 capture (<see cref="WorldResync"/>): the walk
    /// <see cref="CommitJob"/> starts, to the native side and slot instead of a search's route
    /// (a dig or mine to its slot's stance point, a reinforce to its side's, claim-like work to
    /// the tile's centre), so an Imp that DK2 had on its way to a wall is on its way to the same
    /// face of it. No holder is kicked, no event of a choice is raised; a resync is not a choice.
    /// The last kind and tile are written as a choice writes them; the caller restores DK2's.
    /// False when no route reaches the stance.</summary>
    internal static bool ReissueJob(
        SimulationContext context, CreatureState creature, int tileIndex, TileWorkKind kind, int side, byte slot)
    {
        WorldState world = context.World;
        var target = new TilePos(tileIndex % world.Width, tileIndex / world.Width);
        bool onTheTile = kind is TileWorkKind.Claim or TileWorkKind.AttackTile or TileWorkKind.ConvertRoom;
        TilePos standing = onTheTile ? target : new TilePos(target.X + SideDx[side & 3], target.Y + SideDy[side & 3]);
        if (!world.Contains(standing)) return false;
        // DK2 keeps the side and the slot along it; ours is one byte, face * 3 + along, for a
        // dig as for a mine (the dig search hands CommitJob its slot too).
        bool excavation = kind is TileWorkKind.MineGold or TileWorkKind.Dig or TileWorkKind.DigOwnWall or TileWorkKind.DigEnemyWall;
        int face = MiningSlots.Face(standing, target);
        byte workSlot = excavation && face >= 0 ? (byte)((face * MiningSlots.PerFace) + (slot % MiningSlots.PerFace)) : MiningSlots.None;

        bool walks;
        if (workSlot != MiningSlots.None)
        {
            (long sx, long sy) = MiningSlots.StancePoint(standing, workSlot);
            walks = context.Navigation.TryRouteToStance(creature, sx, sy, Navigation.MoveOptions.ExactSpot);
        }
        else if (kind is TileWorkKind.Reinforce or TileWorkKind.RepairWall)
        {
            if (face < 0) return false;
            (long sx, long sy) = TaskStealing.ReinforceStance(standing, face);
            walks = context.Navigation.TryRouteToStance(creature, sx, sy, Navigation.MoveOptions.ExactSpot);
        }
        else if (onTheTile)
        {
            walks = context.Navigation.TryRouteToStance(creature, CreatureState.CentreOf(target.X),
                CreatureState.CentreOf(target.Y), Navigation.MoveOptions.Spot);
        }
        else
        {
            walks = context.Navigation.TryRoute(creature, standing, Navigation.MoveOptions.Spot);
        }
        if (!walks) return false;

        creature.TargetTileIndex = tileIndex;
        creature.TargetWork = kind;
        creature.SecretDoorTask = false;
        creature.WorkSlot = workSlot;
        bool routed = creature.IsRouted;
        if (!routed) creature.TimerTicks = WorkCadence.FirstApplicationTicks;
        SetOutInState(context, creature, kind, routed);

        JobPriority priority = kind switch
        {
            TileWorkKind.MineGold or TileWorkKind.Dig or TileWorkKind.DigOwnWall or TileWorkKind.DigEnemyWall => JobPriority.MarkedExcavation,
            TileWorkKind.Reinforce or TileWorkKind.RepairWall => JobPriority.Reinforce,
            _ => JobPriority.Claim,
        };
        creature.LastTaskKind = KindOf(priority, kind);
        creature.LastTaskTileIndex = tileIndex;
        context.Emit(WorldResync.SystemName, "job_reissued",
            $"imp={creature.Id} {Describe(world, tileIndex)} {kind} side={side} slot={slot} routed={routed}");
        return true;
    }

    /// <summary>
    /// Whether a worker standing at <paramref name="standing"/> can do work of
    /// this priority on <paramref name="target"/>.
    ///
    /// <para>Three conditions, and the middle one is what changed with Q44.
    /// The work must <i>apply</i> to the tile; the worker must be in the
    /// <see cref="WorkStance"/> that job is done from — <b>on</b> the tile for
    /// floor, <b>beside</b> it for a wall; and, unless the keeper asked for the
    /// work by name, the tile must touch ground that keeper already holds.</para>
    /// </summary>
    private static bool TryJobOn(
        SimulationContext context,
        WorldPathGrid grid,
        TilePos standing,
        TilePos target,
        PlayerId playerId,
        JobPriority priority,
        int askingCreatureId,
        out int tileIndex,
        out TileWorkKind kind,
        bool crowding = true)
    {
        tileIndex = -1;
        kind = default;

        WorldState world = context.World;
        if (!world.Contains(target)) return false;

        ref TileState tile = ref world.Tile(target);

        switch (priority)
        {
            // The just-dug tile: claimable, freshly dug, and with none of its
            // EIGHT neighbours still carrying this keeper's dig mark. Eight and
            // not four, and a dig mark and not a mining one, because the
            // original's handler looks up its kind-1 task (a marked tile with no
            // GoldValue) on each of the eight tiles around it, and only kind 1
            // (Q154). After that it is an ordinary claim, done standing on it.
            case JobPriority.ClaimFreshlyDug:
                if (!tile.FreshlyDug) return false;
                if (IsAlliedFloor(world, tile, playerId)) return false;
                if (!TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, playerId)) return false;
                if (ADigIsStillMarkedAround(context, world, target, playerId)) return false;
                kind = TileWorkKind.Claim;
                break;

            case JobPriority.MarkedExcavation:
                if (!tile.IsTaggedBy(playerId)) return false;
                if (!TileWorkRules.TryTaggedWork(context.Database, tile, playerId, out kind)) return false;
                break;

            // Claiming floor, attacking a rival's floor and taking somebody
            // else's room are one priority and three kinds of work, each with its
            // own rate (ClaimTileHealth +825, AttackTileHealth −140,
            // ConvertRoomHealth −4000). Their predicates partition the tiles, so
            // no order between them has to be chosen here. Hostile floor falls to
            // Dirt Path and is then an ordinary claim (Q68, owner).
            case JobPriority.Claim:
                // 0x0046AB50 never takes a tile whose navigation byte holds a door, which only a
                // closed, completed door writes (0x004A66F0, imp-claim-path.md §4, ADR-0443).
                if (world.DoorAt(target.X, target.Y) is { UnderConstruction: false, Open: false })
                    return false;
                if (IsAlliedFloor(world, tile, playerId)) return false;
                if (TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, playerId))
                    kind = TileWorkKind.Claim;
                else if (TileWorkRules.Applies(context.Database, tile, TileWorkKind.AttackTile, playerId))
                    kind = TileWorkKind.AttackTile;
                else if (TileWorkRules.Applies(context.World, context.Database, tile, TileWorkKind.ConvertRoom, playerId))
                    kind = TileWorkKind.ConvertRoom;
                else
                    return false;
                break;

            // A wall is only worth reinforcing where it faces the keeper's own
            // ground (Q295, owner). Today a guard rather than a change -- see
            // TouchesOwnFloor for why it cannot yet fire.
            // The handler's second pass fixes the keeper's own damaged wall (kind 16, Q219,
            // ADR-0387): a rival's imps digging in are slowed by the defender's repairs.
            case JobPriority.Reinforce:
                if (DigMarkCancelsWallWork(context.Database, tile, playerId)) return false;
                if (TileWorkRules.Applies(context.Database, tile, TileWorkKind.Reinforce, playerId))
                    kind = TileWorkKind.Reinforce;
                else if (TileWorkRules.Applies(context.Database, tile, TileWorkKind.RepairWall, playerId))
                    kind = TileWorkKind.RepairWall;
                else
                    return false;
                if (!TouchesOwnFloor(context, world, target, playerId)) return false;
                break;

            default:
                return false;
        }

        // An imp claims a floor tile by standing on it (Q44, CONFIRMED) and digs
        // a wall from beside it (Q27, CONFIRMED): a gold block takes three imps
        // a side, twelve in all. One stance rule decides which (WorkStance), and
        // a worker in the wrong one has no job here.
        bool onTheTile = WorkStance.IsOnTheTile(context.Database, world, grid, target, kind);
        if (onTheTile != (standing == target))
        {
            kind = default;
            return false;
        }

        // Claiming and reinforcing spread from ground the keeper already holds
        // (CONFIRMED, Q42); marked work does not, because the keeper asked for it
        // by name. Which tile carries the condition follows from the stance: a
        // wall job is taken from the keeper's own land, so the STANDING tile must
        // be owned; a floor job is done standing on the tile being claimed, not
        // owned yet, so it is the TARGET that must touch owned land instead.
        if (priority != JobPriority.MarkedExcavation)
        {
            bool reachesOwnLand = onTheTile
                ? TouchesOwnLand(context, world, target, playerId)
                : StandsOnOwnLand(context, world, standing, playerId);

            if (!reachesOwnLand)
            {
                kind = default;
                return false;
            }
        }

        // Three workers per dig face, one per reinforce face or claim (Q43/Q44). Rejecting the
        // FACE, not the tile, sends the surplus on to the next standing tile, block or priority
        // rather than queueing. A full face stays open to a nearer claimer or reinforcer, whom
        // CommitJob lets take it (Q155).
        int index = (target.Y * world.Width) + target.X;
        if (crowding
            && ImpCrowding.FaceIsFull(world, context.Database, index, standing, askingCreatureId, onTheTile, kind)
            && HolderToTakeFrom(context, index, standing, askingCreatureId, kind) is null)
        {
            kind = default;
            return false;
        }

        // Mining needs a free slot on this face: one no other miner holds, whose cell's pile holds
        // no more than MaxGoldPileOutsideTreasury (0x00469AF0, 0x0046A550; Q140).
        if (crowding && kind == TileWorkKind.MineGold
            && MiningSlots.FreeSlot(world, context.Database, index, standing, target, askingCreatureId)
                == MiningSlots.None)
        {
            kind = default;
            return false;
        }

        tileIndex = index;
        return true;
    }

    /// <summary>
    /// Whether this tile belongs to the keeper and is the kind of tile a keeper
    /// can hold at all.
    /// </summary>
    private static bool StandsOnOwnLand(
        SimulationContext context, WorldState world, TilePos standing, PlayerId playerId)
    {
        if (!world.Contains(standing)) return false;

        ref TileState tile = ref world.Tile(standing);
        return tile.OwnerPlayerId == playerId
            && context.Database.TerrainById(tile.TerrainId).IsOwnable;
    }

    /// <summary>
    /// A digger or miner whose action ended with nothing left to dig: if its task is still in
    /// the pool, the tile gets the kind-14 task when it is plain path touching the keeper's floor
    /// (<c>0x004655D0</c>, <c>0x00465870</c>: <c>0x00508070</c>, then <c>0x00453FC0</c> and
    /// <c>0x00453B30</c>). A digger removes the dig's entry; a miner leaves the seam's to the
    /// next dig walk, as its own removal tests for kind 1. Level 1 seeds 1 and 6 raise none: an
    /// imp chose a dig between the wall's fall and the diggers' last action (ADR-0305).
    /// </summary>
    private static void RaiseClaimOfWhatWasDug(SimulationContext context, PlayerId playerId, int tileIndex, TileWorkKind work)
    {
        if (playerId > TileState.MaxTaggingPlayerId) return;
        WorldState world = context.World;
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        if (!tile.DigTaskOutlivesWallFor.Contains(playerId)) return;
        if (work != TileWorkKind.MineGold) tile.DigTaskOutlivesWallFor = tile.DigTaskOutlivesWallFor.Without(playerId);
        if (!tile.FreshlyDug && TileWorkRules.Applies(context.Database, tile, TileWorkKind.Claim, playerId)
            && TouchesOwnLand(context, world, new TilePos(tileIndex % world.Width, tileIndex / world.Width), playerId))
        {
            tile.FreshlyDug = true;
            context.Emit(SystemName, "claim_raised", $"player={playerId} {Describe(world, tileIndex)}");
        }
    }

    /// <summary>
    /// Whether any tile orthogonally adjacent to <paramref name="target"/> is the
    /// keeper's own <b>floor</b> — the condition on an unprompted job the worker
    /// does standing on the target itself (CONFIRMED, Q42).
    /// <para>READ from <c>0x00453b30</c>: each of the four neighbours counts only
    /// when its owner is the keeper, its terrain is Ownable and it is not Solid, so
    /// the keeper's own walls do not start a claim. Level 2 live capture
    /// (level2-live1): the heart's walls touch the dug Lair on three sides, and the
    /// first claim is the one tile touching the heart's gap, then outward from it.
    /// The test is <see cref="OwnFloorRules"/>, shared with bridge placement.</para>
    /// </summary>
    internal static bool TouchesOwnLand(
        SimulationContext context, WorldState world, TilePos target, PlayerId playerId) =>
        OwnFloorRules.Touches(context.Database, world, target, playerId);

    /// <summary>Both claim handlers (<c>0x0046967F</c>, <c>0x0046ABCF</c>) refuse a tile whose owner is
    /// an ally, the keeper included, before choosing between claim, attack and conversion; a neutral
    /// tile skips the test, so the level's keeper-with-neutral alliances (Q576) leave it claimable.
    /// Owner 0 counts as neutral here, where the binary compares the neutral tag only; no tile has it.</summary>
    private static bool IsAlliedFloor(WorldState world, in TileState tile, PlayerId playerId) =>
        !TileWorkRules.IsNeutralTile(tile) && world.AreAllied(playerId, tile.OwnerPlayerId);

    /// <summary>
    /// Whether any of the eight tiles around <paramref name="target"/> is
    /// marked by this keeper for work that is <i>digging</i> — the test that
    /// holds a just-dug tile's claim back while the corridor is still being
    /// cut (Q154). Mining a seam next door does not hold it back: the original
    /// looks for its dig kind only, and its dig kind is every marked tile whose
    /// terrain has no <c>GoldValue</c> — rock and walls alike — so here it is
    /// every tagged kind but <see cref="TileWorkKind.MineGold"/>.
    /// </summary>
    private static bool ADigIsStillMarkedAround(
        SimulationContext context, WorldState world, TilePos target, PlayerId playerId)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var at = new TilePos(target.X + dx, target.Y + dy);
                if (!world.Contains(at)) continue;

                ref TileState neighbour = ref world.Tile(at);
                if (!neighbour.IsTaggedBy(playerId)) continue;
                if (!TileWorkRules.TryTaggedWork(context.Database, neighbour, playerId, out TileWorkKind kind)) continue;
                if (kind != TileWorkKind.MineGold) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether one of the wall's four sides faces floor this keeper owns, which
    /// is what makes it reinforceable. CONFIRMED (owner, 2026-09-10): "as soon
    /// as the floor tile is claimed walls are eligible to be reinforced".
    /// <para><b>Four, not eight</b> — ours: a wall reinforces the face it shows
    /// a corridor, and targets already come from four neighbours.</para>
    /// <para><b>It cannot fire today</b>, and is kept as a guard; ADR-0132 has
    /// the argument and what would change it.</para>
    /// </summary>
    private static bool TouchesOwnFloor(
        SimulationContext context, WorldState world, TilePos target, PlayerId playerId)
    {
        ReadOnlySpan<int> dx = [1, -1, 0, 0];
        ReadOnlySpan<int> dy = [0, 0, 1, -1];

        for (int i = 0; i < 4; i++)
        {
            var at = new TilePos(target.X + dx[i], target.Y + dy[i]);
            if (!world.Contains(at)) continue;

            ref TileState neighbour = ref world.Tile(at);
            if (neighbour.OwnerPlayerId != playerId) continue;
            if (context.Database.TerrainById(neighbour.TerrainId).IsSolid) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the creature's target is still workable — and, if the keeper
    /// asked for it by name, still asked for.
    ///
    /// The mark is only a condition on excavation. An unprompted claim or
    /// reinforce was never tagged, so testing the tag for those would abandon
    /// the job on the tick after it was taken and take it again on the next,
    /// forever.
    /// </summary>
    private static bool JobStillStands(SimulationContext context, CreatureState creature)
    {
        ref TileState tile = ref context.World.TileAtIndex(creature.TargetTileIndex);

        if (IsExcavation(creature.TargetWork) && !tile.IsTaggedBy(creature.OwnerPlayerId))
            return false;

        if (creature.TargetWork is TileWorkKind.Reinforce or TileWorkKind.RepairWall
            && DigMarkCancelsWallWork(context.Database, tile, creature.OwnerPlayerId))
            return false;

        if (TileWorkRules.TryResolveDigTask(
                context.Database, tile, creature.TargetWork, creature.OwnerPlayerId,
                out TileWorkKind currentKind))
        {
            creature.TargetWork = currentKind;
            return true;
        }

        return TileWorkRules.Applies(
            context.World, context.Database, tile, creature.TargetWork, creature.OwnerPlayerId);
    }

    /// <summary>
    /// The keeper's dig mark on a wall ends its reinforce and repair work (Q621, ADR-0533). The
    /// pool adder <c>0x00507BB0</c> raising kind 1 (the tag, through <c>0x00512D20</c>) first
    /// removes the tile's kind-6 and kind-16 entries (<c>0x00507EF0</c>), and refuses to raise 6
    /// or 16 while a kind-1 entry stands at the tile. A reinforcer in state 22 finds its task gone
    /// at its next check (<c>0x004663C4</c>, once no action plays), clears it and goes to 10,
    /// which the work-state wait in <c>ImpTaskSystem.Update</c> mirrors. The wall's health is not
    /// touched, so a partial reinforce stays on the tile for the dig to take down. Kind 2 (gold)
    /// removes nothing, so only a goldless wall's tag cancels (0x00512D20's kind-1 arm). Each keeper
    /// has its own pool (player <c>+0x1105</c>): only the Imp's own keeper's tag counts.
    /// </summary>
    private static bool DigMarkCancelsWallWork(GameDatabase database, in TileState tile, PlayerId owner) =>
        tile.IsTaggedBy(owner) && database.TerrainById(tile.TerrainId).GoldValue == 0;

    /// <summary>
    /// Whether this kind of work can only have come from a dig marker.
    ///
    /// Reads <see cref="TileWorkRules.TaggedWork"/> rather than listing kinds,
    /// so that a kind added to the marker mapping cannot quietly fall out of
    /// this test.
    /// </summary>
    private static bool IsExcavation(TileWorkKind kind)
    {
        IReadOnlyList<TileWorkKind> tagged = TileWorkRules.TaggedWork;
        for (int i = 0; i < tagged.Count; i++)
            if (tagged[i] == kind) return true;
        return false;
    }

    /// <summary>
    /// Whether the worker is where it needs to be to swing — which depends on
    /// the job, not only on the distance. See <see cref="WorkStance"/>.
    /// </summary>
    private static bool IsInWorkingPosition(
        SimulationContext context, CreatureState creature, WorldPathGrid grid)
    {
        WorldState world = context.World;
        var target = new TilePos(
            creature.TargetTileIndex % world.Width, creature.TargetTileIndex / world.Width);

        if (WorkStance.IsInPosition(context.Database, world, grid, creature.Tile, target, creature.TargetWork))
            return true;

        // A dig whose wall has fallen is still worked from beside it: the dig entry asks only that
        // the worker's tile be within one of the task's on each axis, diagonals included
        // (0x004654F0: |dx| <= 1 and |dy| <= 1), not where the floor would put a stance.
        if (creature.TargetWork is TileWorkKind.Dig or TileWorkKind.DigOwnWall or TileWorkKind.DigEnemyWall
            && !TileWorkRules.TryResolveDigTask(context.Database, world.Tile(target), creature.TargetWork,
                creature.OwnerPlayerId, out _)
            && Math.Abs(creature.Tile.X - target.X) <= 1 && Math.Abs(creature.Tile.Y - target.Y) <= 1)
            return true;

        // A worker at work still settling on its stance point may be nudged across the tile's edge
        // by a neighbour; its route still ends on the stance tile, and that is where it works.
        (int entry, int work) = ImpStates.For(creature.TargetWork);
        return creature.Dk2StateId == entry || creature.Dk2StateId == work
            ? creature.IsRouted && WorkStance.IsInPosition(
                context.Database, world, grid, creature.Destination, target, creature.TargetWork)
            : false;
    }
}
