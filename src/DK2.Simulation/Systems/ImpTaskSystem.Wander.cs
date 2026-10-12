// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>An idle worker's rest and wander (imp-idle-and-wandering.md, ADR-0224): state 10 finding
/// nothing hands over to 12 with a deadline; 12 plays an idle action or sets out on a trip in 87;
/// the trip resumes 1, which sends a worker back to 10 to look again.</summary>
public sealed partial class ImpTaskSystem
{
    /// <summary>State 10's "nothing found" arm: 12's deadline is now + 240 + rand(240), 60–120 s.</summary>
    public const int RestBaseTicks = 240;
    public const int RestRandomTicks = 240;

    /// <summary>How long idle action 43 holds state 12, one tick short of the 20-tick rest the
    /// two full-state captures show (mining-cadence.md §6). Measured as an outcome: the imp's
    /// animation blocks (171 and 172 frames at 30 fps) do not give it.</summary>
    public const int IdleAction43Ticks = 19;

    /// <summary>Action 44's, one short of the 25-tick rest. That 43 is the 20 and 44 the 25
    /// is STRONGLY INFERRED from the 2:1 split matching 12's roll.</summary>
    public const int IdleAction44Ticks = 24;

    /// <summary>How long another creature's idle action plays. <b>A guess</b>: the length is the
    /// animation's and is UNKNOWN; the imp's two were measured, not read.</summary>
    public const int IdleActionGuessTicks = 8;

    /// <summary><c>0x00480A00</c>'s room branch: the room types a wander may go to.</summary>
    private static readonly RoomTypeId[] WanderRooms =
    [
        RoomTypeId.Treasury, RoomTypeId.Lair, RoomTypeId.Hatchery, RoomTypeId.Library, RoomTypeId.TrainingRoom,
        (RoomTypeId)9, (RoomTypeId)10, (RoomTypeId)11, (RoomTypeId)12, (RoomTypeId)13, (RoomTypeId)14,
        (RoomTypeId)15, (RoomTypeId)16, (RoomTypeId)26,
    ];

    /// <summary>The wander's states, ahead of the activity switch. True when this tick was theirs.</summary>
    private bool UpdateWander(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.UtilityNewState != CreatureStateMachine.Idle || creature.TargetTileIndex >= 0) return false;
            if (creature.HasRoute) return true;
            creature.StopMoving();
            StateShadow.Arrive(context, SystemName, creature);
            return true;
        }

        // State 1 sends a worker straight to 10 (0x004773D0). Ours runs 10's selection from 1 in the
        // same turn, as the rest of the imp's states always have.
        switch (creature.Dk2StateId)
        {
            case ImpStates.AfterDrop:
                creature.WorkerIdle = false;
                if (StateShadow.TryMove(context, SystemName, creature, ImpStates.TaskSelection))
                {
                    // State 11 (0x00463DB0): the kind just done is zeroed, the aimed kind kept.
                    creature.LastTaskKind = ImpTaskKind.None;
                    creature.JustDropped = true;
                }
                return true;
            case ImpStates.Resting:
                Rest(context, creature);
                return true;
            case ImpStates.Wandering:
                if (!Wander(context, creature, definition)) StateShadow.ToIdle(context, SystemName, creature);
                return true;
            default:
                return false;
        }
    }

    /// <summary>State 10 found nothing: 12, with its deadline, unless the creature is the special
    /// owner's (<c>[0x006F2548]</c>, the neutral player), for whom none is written.</summary>
    private static void ToRest(SimulationContext context, CreatureState creature)
    {
        if (creature.OwnerPlayerId != PlayerIds.Neutral)
            creature.StateDeadlineTick = context.Tick.Value + RestBaseTicks + context.Rng.NextInt(RestRandomTicks);
        StateShadow.Move(context, SystemName, creature, ImpStates.Resting);
    }

    /// <summary>State 12 (<c>0x00464240</c>): wait out a playing action; at the deadline, 10; after a
    /// trip, roll: under 25 wander, 25–74 action 43, 75–99 action 44; otherwise wander. The two
    /// actions' lengths are <see cref="IdleAction43Ticks"/> and <see cref="IdleAction44Ticks"/>.</summary>
    private static void Rest(SimulationContext context, CreatureState creature)
    {
        creature.WorkerIdle = true;
        long now = context.Tick.Value;
        if (now < creature.ActionUntilTick) return;
        if (creature.StateDeadlineTick <= now)
        {
            if (StateShadow.TryMove(context, SystemName, creature, ImpStates.TaskSelection))
                creature.WorkerIdle = false;
            return;
        }
        if (creature.WanderStamp != 0)
        {
            int roll = context.Rng.NextInt(100);
            if (roll >= 25)
            {
                creature.WanderStamp = 0;
                creature.ActionUntilTick = now + (roll < 75 ? IdleAction43Ticks : IdleAction44Ticks);
                context.Emit(SystemName, "idle_action",
                    $"imp={creature.Id} action={(roll < 75 ? 43 : 44)} at=({creature.Tile.X},{creature.Tile.Y})");
                return;
            }
        }
        if (CreatureStateMachine.TrySetState(context.Hooks, creature, ImpStates.Wandering)) creature.WanderStamp = now + 32;
    }

    /// <summary>State 87 (<c>0x004808E0</c>): a destination at ring radius 4–7, then each smaller one
    /// down to 1, else the creature's own position; a random sub-tile point in that tile; a walk with
    /// options <c>0x80</c> resuming 1 (238 for a Horny creature). One handler for every creature: a
    /// non-worker reaches it from state 3 (<see cref="CreatureJobSystem"/>, ADR-0228).</summary>
    internal static bool Wander(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (context.Tick.Value < creature.ActionUntilTick) return true;
        int x = creature.PosX, y = creature.PosY;
        int r = context.Rng.NextInt(4) + 4;
        (int X, int Y)? at = null;
        for (; r > 0; r--)
        {
            at = Destination(context, creature, r);
            if (at is not null) break;
        }
        if (at is { } d)
        {
            x = CreatureState.CentreOf(d.X);
            y = CreatureState.CentreOf(d.Y);
        }
        (int ox, int oy) = HeroJobRules.RandomSubTile(context);
        x = (x & ~0xFFF) | ox;
        y = (y & ~0xFFF) | oy;
        if (!context.Navigation.TryRouteToStance(creature, x, y, Navigation.MoveOptions.Loose)) return false;
        int resume = definition.Flags.HasFlag(CreatureFlags.IsHorny) ? 238 : CreatureStateMachine.Idle;
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, resume);
        return true;
    }

    /// <summary><c>0x00480A00(out, r)</c>: one time in eight a point in one of the keeper's rooms of
    /// <see cref="WanderRooms"/>; otherwise the square ring at radius <paramref name="r"/>, from a
    /// random start to its end without wrapping, the first tile in bounds, not solid, not a heart,
    /// the keeper's own and with no obstacle object (<c>0x00453600</c>).</summary>
    private static (int X, int Y)? Destination(SimulationContext context, CreatureState creature, int r)
    {
        WorldState world = context.World;
        int cx = creature.PosX >> 12, cy = creature.PosY >> 12;
        if (context.Rng.NextInt(8) == 0 && RoomDestination(context, creature) is { } room) return room;

        int n = r * 8, side = n >> 2;
        int start = context.Rng.NextInt(n);
        int x = 0, y = 0;
        for (int k = start; k < n; k++)
        {
            int o = k % side;
            switch (k / side)
            {
                case 0: x = cx + o - r; y = cy - r; break;
                case 1: x = cx + r; y = cy + o - r; break;
                case 2: x = cx + o - r; y = cy + r; break;
                default: x = cx - r; y = cy + o - r; break;
            }
            if (Wanderable(context, creature, x, y, heartTest: true)) return (x, y);
        }

        // DK2's fallback runs only when the last tile tried was (0, 0), and re-derives one ring tile
        // from the start index with its own arithmetic; the heart is not tested.
        if (x != 0 || y != 0) return null;
        int fx = 0, fy = 0;
        for (int k = 0; k < start; k++)
        {
            int o = k % side;
            switch (k / side)
            {
                case 0: fx = cx + o - r; fy = cy - r; break;
                case 1: fx = cx + r; fy = cy + o - r; break;
                case 2: fx = cx - r; fy = cy + o + r; break;
                default: fx = cx - r; fy = cy + o - r; break;
            }
        }
        return Wanderable(context, creature, fx, fy, heartTest: false) ? (fx, fy) : null;
    }

    /// <summary>The room branch: of the keeper's rooms in list order, the last eligible one, then any
    /// room after it, until one gives a point (<c>0x004E8430</c>). Ours: the room list is
    /// <see cref="RoomIndex.Build"/>'s order, and the point is a random walkable tile of the room with
    /// no obstacle, where DK2's two cell tests (<c>0x00454110</c>, <c>0x00454230</c>) turn on a cell
    /// byte that is unread.</summary>
    private static (int X, int Y)? RoomDestination(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        List<RoomInstance> rooms = RoomIndex.Build(world, context.Database);
        int remaining = 0, kept = 0;
        for (int i = 0; i < rooms.Count; i++)
        {
            RoomInstance room = rooms[i];
            if (room.OwnerPlayerId != creature.OwnerPlayerId) continue;
            rooms[kept++] = room;
            if (IsWanderRoom(room.Definition)) remaining++;
        }
        rooms.RemoveRange(kept, rooms.Count - kept);
        if (remaining == 0) return null;
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        foreach (RoomInstance room in rooms)
        {
            if (remaining != 0)
            {
                if (!IsWanderRoom(room.Definition)) continue;
                if (--remaining != 0) continue;
            }
            if (RoomPoint(world, context.Database, definition, room, context.Rng) is not int pick) continue;
            return (pick % world.Width, pick / world.Width);
        }
        return null;
    }

    /// <summary><c>0x004E8430</c>: choose among eligible room tiles in slab-chain order.</summary>
    public static int? RoomPoint(WorldState world, GameDatabase database, CreatureDef creature,
        RoomInstance room, SimRandom rng)
    {
        var grid = new WorldPathGrid(database, world, creature);
        var open = new List<int>();
        foreach (int tile in SlabChain.Order(world, room))
            if (grid.IsWalkable(tile % world.Width, tile / world.Width)
                && !HasObstacle(world, database, tile)) open.Add(tile);
        return open.Count == 0 ? null : open[rng.NextInt(open.Count)];
    }

    private static bool IsWanderRoom(RoomDef room) => room.TypeId is RoomTypeId t && Array.IndexOf(WanderRooms, t) >= 0;

    private static bool Wanderable(SimulationContext context, CreatureState creature, int x, int y, bool heartTest)
    {
        WorldState world = context.World;
        if (!world.Contains(new TilePos(x, y))) return false;
        ref TileState tile = ref world.Tile(x, y);
        if (context.Database.TerrainById(tile.TerrainId).IsSolid) return false;
        if (heartTest && context.Database.RoomByTerrain(tile.TerrainId)?.TypeId == RoomTypeId.DungeonHeart) return false;
        if (tile.OwnerPlayerId != creature.OwnerPlayerId) return false;
        return !HasObstacle(context, (y * world.Width) + x);
    }

    /// <summary><c>0x00453600</c>: an object on the tile whose definition is an obstacle.</summary>
    private static bool HasObstacle(SimulationContext context, int tileIndex)
        => HasObstacle(context.World, context.Database, tileIndex);

    private static bool HasObstacle(WorldState world, GameDatabase database, int tileIndex)
    {
        foreach (ObjectState o in world.Objects)
            if (o.TileIndex == tileIndex
                && database.Objects.TryGetValue(o.ObjectTypeId, out ObjectDef? def)
                && def.Flags.HasFlag(ObjectFlags.Obstacle))
                return true;
        return false;
    }
}
