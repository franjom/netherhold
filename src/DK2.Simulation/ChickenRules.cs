// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>The Chicken spell on the creature it strikes (docs/research/keeper-spell-inferno-chicken.md
/// §3): shot process 15 stamps <see cref="CreatureState.ChickenTicks"/> and asks for state 96, whose
/// handler walks the creature to its own keeper's nearest reachable Hatchery or wanders it a tile at
/// a time; the timer's end sends it back to idle whatever it is doing. The timer, not the state, is
/// what the fight gate, the fear accumulator and the Combat Pit finder read.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-inferno-chicken.md")]
public static class ChickenRules
{
    private const string SystemName = "chicken";

    /// <summary>DK2's state 96, group 3 and category 7: from another group it is always entered; from
    /// another group-3 state only from category 7, so dying and frozen creatures refuse it.</summary>
    public const int ChickenState = 96;

    /// <summary>The 3×3 sub-tile points the wander picks from (<c>0x00698EC8</c>): a sixth, a half
    /// and five sixths of a tile on each axis, x varying fastest.</summary>
    private static readonly int[] SubTile = [0x2AA, 0x7FF, 0xD54];

    /// <summary>Whether the creature is a chicken now: the timer runs.</summary>
    public static bool IsChicken(CreatureState creature) => creature.ChickenTicks > 0;

    /// <summary>Process 15, <c>0x004AE2A0</c>, on the creature struck: a live creature whose kind lacks
    /// <c>IsImmuneToChicken</c> takes the timer (data1 seconds, a store, so a second cast restarts it)
    /// and then the request for state 96. The store comes first, so a creature that refuses the
    /// state still carries the timer. Its <c>0x004B32A0</c> call has radius 2 and does nothing.</summary>
    public static void Strike(SimulationContext context, ShotState shot, CreatureState target, CreatureDef targetDef)
    {
        if (target.Activity == CreatureActivity.Dead) return;
        if (targetDef.Flags.HasFlag(CreatureFlags.IsImmuneToChicken))
        {
            context.Emit(SystemName, "chicken_immune", $"shot={shot.Id} target={target.Id}");
            return;
        }
        target.ChickenTicks = shot.Data1 * SimulationTiming.OriginalTicksPerGameSecond;
        bool entered = CreatureStateMachine.TrySetState(target, ChickenState, context.Tick.Value);
        // DK2 moves only a walking state (0x0048A23B), so a route goes with the state it left.
        if (entered) target.StopMoving();
        context.Emit(SystemName, "chickened",
            $"shot={shot.Id} target={target.Id} ticks={target.ChickenTicks} state={(entered ? "96" : "refused")}");
    }

    /// <summary>The countdown's end in <c>0x0048E160</c>: <c>SetState(1)</c> whatever state the creature
    /// is in. Its clearing of the mover byte <c>+0xF2</c> is not modelled (Q416).</summary>
    public static void TimerEnded(SimulationContext context, CreatureState creature)
    {
        bool idled = CreatureStateMachine.TrySetState(creature, CreatureStateMachine.Idle, context.Tick.Value);
        if (idled) creature.StopMoving();
        context.Emit(SystemName, "unchickened", $"creature={creature.Id} idle={(idled ? 1 : 0)}");
    }

    /// <summary>State 96's handler, <c>0x00463BE0</c>, for a creature standing in it. With a Hatchery of
    /// its own keeper it can reach, the nearest: a random room tile is drawn and not used
    /// (<c>0x004E4D90</c>), then the walk to the room (<c>0x00471A70</c>), which inside the room is
    /// idle and back to 96. Otherwise, or when that walk fails, a random cardinal neighbour that is
    /// not solid, water or lava (its own tile when none is), a random sub-tile point there, and a walk
    /// to it resuming 96. The handler succeeds either way.</summary>
    public static void Handle(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        RoomInstance? hatchery = NearestReachableHatchery(context, creature);
        if (hatchery is not null)
        {
            _ = context.Rng.NextInt(hatchery.TileCount);
            if (StealGoldRules.ToRoom(context, SystemName, creature, hatchery, ChickenState, MoveOptions.Loose)) return;
        }

        TilePos here = creature.Tile;
        TilePos to = here;
        int direction = context.Rng.NextInt(4);
        for (int tries = 0; tries < 4; tries++, direction = (direction + 1) & 3)
        {
            TilePos next = direction switch
            {
                0 => new TilePos(here.X + 1, here.Y),
                1 => new TilePos(here.X - 1, here.Y),
                2 => new TilePos(here.X, here.Y + 1),
                _ => new TilePos(here.X, here.Y - 1),
            };
            if (!CanWanderOnto(world, context.Database, next)) continue;
            to = next;
            break;
        }
        int point = context.Rng.NextInt(9);
        int x = (to.X << 12) | SubTile[point % 3], y = (to.Y << 12) | SubTile[point / 3];
        bool walking = TunnelRules.RouteOrTunnel(context, SystemName, creature, x, y,
            StateShadow.TravelToPoint, ChickenState, MoveOptions.Loose, to.X, to.Y);
        context.Emit(SystemName, "chicken_wander",
            $"creature={creature.Id} from=({here.X},{here.Y}) to=({to.X},{to.Y}) walking={(walking ? 1 : 0)}");
    }

    /// <summary>The wander's terrain test (<c>+0x17B</c> bits 0, 7 and 8): not solid, water or lava.
    /// Off the map counts as solid; shipped maps are bordered by rock.</summary>
    private static bool CanWanderOnto(WorldState world, GameDatabase database, TilePos at)
    {
        if (!world.Contains(at)) return false;
        TerrainDef terrain = database.TerrainById(world.Tile(at).TerrainId);
        return !terrain.IsSolid && !terrain.IsWater && !terrain.IsLava;
    }

    /// <summary><c>0x004C6750</c> on the creature's own player for room type 4: the Hatchery whose
    /// position is strictly nearest the creature, among those it can reach (<c>0x00471630</c>, options
    /// <c>0x80</c>). DK2 measures to the room's <c>NavigationPos</c> (<c>+0x2B</c>), the room tile nearest
    /// the centroid that its cell test leaves; ours measures to the centroid's tile, or the room tile
    /// nearest it, and breaks a tie by room order rather than DK2's newest first.</summary>
    public static RoomInstance? NearestReachableHatchery(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        RoomInstance? best = null;
        long bestDistance = long.MaxValue;
        var scratch = new List<Fixed16Vector>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!EatingRules.IsHatcheryOf(room, creature.OwnerPlayerId) || room.TileIndices.Count == 0) continue;
            TilePos position = Position(world, room);
            long dx = Math.Abs((long)CreatureState.CentreOf(position.X) - creature.PosX);
            long dy = Math.Abs((long)CreatureState.CentreOf(position.Y) - creature.PosY);
            long distance = ((dx * dx) >> 12) + ((dy * dy) >> 12);
            if (distance >= bestDistance) continue;
            if (!Reachable(context, creature, definition, position, scratch)) continue;
            best = room;
            bestDistance = distance;
        }
        return best;
    }

    private static bool Reachable(SimulationContext context, CreatureState creature, CreatureDef definition, TilePos at,
        List<Fixed16Vector> scratch)
    {
        if (creature.Tile == at) return true;
        scratch.Clear();
        var from = new Fixed16Vector(creature.PosX << 4, creature.PosY << 4);
        var to = new Fixed16Vector(CreatureState.CentreOf(at.X) << 4, CreatureState.CentreOf(at.Y) << 4);
        return context.Navigation.Route(from, to,
            WorldSteering.ProfileOf(definition, creature.OwnerPlayerId, MoveOptions.Loose), scratch);
    }

    /// <summary>A room's integer centroid tile when it is the room's, else the room tile nearest it.</summary>
    private static TilePos Position(WorldState world, RoomInstance room)
    {
        int w = world.Width;
        long sumX = 0, sumY = 0;
        foreach (int index in room.TileIndices) { sumX += index % w; sumY += index / w; }
        int cx = (int)(sumX / room.TileIndices.Count), cy = (int)(sumY / room.TileIndices.Count);
        if (room.Contains(cy * w + cx)) return new TilePos(cx, cy);
        int best = room.TileIndices[0];
        long bestDistance = long.MaxValue;
        foreach (int index in room.TileIndices)
        {
            long ddx = index % w - cx, ddy = index / w - cy;
            long d = ddx * ddx + ddy * ddy;
            if (d < bestDistance) { bestDistance = d; best = index; }
        }
        return new TilePos(best % w, best / w);
    }
}
