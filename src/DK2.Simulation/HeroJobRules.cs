// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a hero does between fights (hero-party-walk.md): state 1 sends a hero-owned
/// creature to 90, which runs its standing <c>GoodJob</c> through the job dispatcher
/// <c>0x00478800</c> on every visit, or sends a follower to 222 behind its leader. The arms
/// built are the five shipped jobs; <see cref="JobType.StealGold"/>'s are in
/// <see cref="StealGoldRules"/> (ADR-0219).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-party-walk.md")]
public static class HeroJobRules
{
    public const int WaitFailedState = 4;
    public const int StandingJobState = 90;
    public const int ToThePointState = 173;
    public const int AtThePointState = 174;
    public const int RoamingState = 175;
    public const int WaitingState = 176;
    public const int HuntingState = 177;
    public const int AtTheHeartState = 178;
    public const int FollowingState = 222;
    public const int NoPointState = 227;

    /// <summary>The retry after a failed job: state 90 sets <c>now + 12</c> and enters 4.</summary>
    public const int RetryTurns = 12;

    /// <summary>A route whose failure may ask a party's Dwarf to tunnel (option bit 0). Ours has no
    /// tunnelling, so the bit changes nothing.</summary>
    public const int TunnelOption = 0x01;

    /// <summary>The nine sub-tile points at <c>0x00698EC8</c>: a 3×3 grid at 682, 2047 and 3412.</summary>
    private static readonly (int X, int Y)[] SubTile =
    [
        (682, 682), (2047, 682), (3412, 682),
        (682, 2047), (2047, 2047), (3412, 2047),
        (682, 3412), (2047, 3412), (3412, 3412),
    ];

    /// <summary>The follower's four trailing steps at <c>0x006984F0</c>, indexed by the leader's facing.</summary>
    private static readonly (int X, int Y)[] Behind = [(0, 1), (-1, 0), (0, -1), (1, 0)];

    /// <summary>State 175's four neighbours at <c>0x006BC350</c>.</summary>
    private static readonly (int X, int Y)[] Neighbours = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    public static bool IsHero(CreatureState creature) => creature.OwnerPlayerId == PlayerIds.Good;

    public static bool IsFollower(CreatureState creature) => (creature.Flags & 0x04) != 0;

    /// <summary><c>0x0050EA20</c>: the player whose creatures carry the greatest sum of the
    /// definition byte <c>+0x787</c> (<see cref="CreatureDef.Unknown_0F4C"/>, <c>0x004BCB20</c>),
    /// first in player order on a tie; every player counts, the heroes' own included. Null when
    /// no sum exceeds zero, where DK2 would read an unset local.</summary>
    public static byte? StrongestPlayer(WorldState world, GameDatabase database)
    {
        byte? best = null;
        int bestSum = 0;
        for (int p = 0; p < world.PlayerCount; p++)
        {
            byte id = world.PlayerAtIndex(p).PlayerId;
            int sum = 0;
            foreach (CreatureState c in world.Creatures)
                if (c.OwnerPlayerId == id && CreatureStateMachine.HasState(c))
                    sum += database.CreatureById(c.CreatureTypeId).Unknown_0F4C;
            if (sum <= bestSum) continue;
            bestSum = sum;
            best = id;
        }
        return best;
    }

    /// <summary>The job's player, or the strongest written back into the triple when it names none
    /// (the arms of jobs 18 and 19). Ours holds the player id where DK2 holds its tag.</summary>
    public static byte? TargetPlayer(WorldState world, GameDatabase database, CreatureState creature)
    {
        if (creature.GoodJobPlayerId != 0) return creature.GoodJobPlayerId;
        if (StrongestPlayer(world, database) is not byte chosen) return null;
        creature.GoodJobPlayerId = chosen;
        return chosen;
    }

    /// <summary>The job dispatcher's arms for the standing job. True when the arm took.</summary>
    public static bool Dispatch(SimulationContext context, string system, CreatureState creature)
    {
        bool took = creature.GoodJob switch
        {
            JobType.Wait => Systems.StateShadow.TryMove(context, system, creature, WaitingState),
            JobType.SendToActionPoint => Systems.StateShadow.TryMove(
                context, system, creature, creature.GoodJobActionPointId != 0 ? ToThePointState : NoPointState),
            JobType.KillCreatures => HuntFirst(context, creature),
            JobType.KillPlayer => ToTheHeart(context, creature, creature.GoodJob),
            JobType.StealGold => StealGoldRules.Arm(context, system, creature),
            _ => false,
        };
        if (took) creature.CurrentJob = creature.GoodJob;
        return took;
    }

    /// <summary>State 176: until the deadline it stands (action 43); then it walks to one of the
    /// nine points of its own tile and waits 50 to 84 turns more.</summary>
    public static bool Wait(SimulationContext context, CreatureState creature)
    {
        long now = context.Tick.Value;
        if (now <= creature.StateDeadlineTick) return true;
        (int ox, int oy) = SubTile[context.Rng.NextInt(SubTile.Length)];
        creature.StateDeadlineTick = now + 50 + context.Rng.NextInt(35);
        int x = (creature.PosX & ~0xFFF) | ox;
        int y = (creature.PosY & ~0xFFF) | oy;
        return Walk(context, creature, x, y, WaitingState, Navigation.MoveOptions.Loose);
    }

    /// <summary>State 173 (<c>0x00471EB0</c>): four tries at a random tile of the action point's
    /// rectangle — upper bounds excluded — that the creature may stand on and that is not a room or
    /// wall of its own keeper; the first that routes wins, resuming 174.</summary>
    public static bool ToThePoint(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        byte id = unchecked((byte)creature.GoodJobActionPointId);
        if (id == 0 || PointById(world, id) is not { } point) return false;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            int spanX = point.EndX - point.StartX;
            int spanY = point.EndY - point.StartY;
            int y = spanY != 0 ? point.StartY + context.Rng.NextInt(Math.Abs(spanY)) * Math.Sign(spanY) : point.StartY;
            int x = spanX != 0 ? point.StartX + context.Rng.NextInt(Math.Abs(spanX)) * Math.Sign(spanX) : point.StartX;
            if (!world.Contains(x, y) || !MayStand(world, database, definition, creature, point, x, y)) continue;
            if (Walk(context, creature, CreatureState.CentreOf(x), CreatureState.CentreOf(y), AtThePointState, TunnelOption))
                return true;
        }
        return false;
    }

    /// <summary>State 174: the point's next waypoint becomes the job's value and its wait timer
    /// the roaming deadline; then 175.</summary>
    public static bool AtThePoint(SimulationContext context, string system, CreatureState creature)
    {
        byte id = unchecked((byte)creature.GoodJobActionPointId);
        if (id == 0 || PointById(context.World, id) is not { } point) return false;
        creature.GoodJobActionPointId = point.NextWaypointId;
        creature.WaypointDeadlineTick = context.Tick.Value + point.WaitTimerTicks;
        return Systems.StateShadow.TryMove(context, system, creature, RoamingState);
    }

    /// <summary>State 175: past the deadline, on to the next point (173); before it, a walk to a
    /// random point of a free neighbouring tile, starting from a random one of the four.</summary>
    public static bool Roam(SimulationContext context, string system, CreatureState creature)
    {
        if (context.Tick.Value > creature.WaypointDeadlineTick)
            return Systems.StateShadow.TryMove(context, system, creature, ToThePointState);
        var grid = new WorldPathGrid(context.Database, context.World, context.Database.CreatureById(creature.CreatureTypeId));
        int start = context.Rng.NextInt(4);
        TilePos at = creature.Tile;
        for (int k = 0; k < 4; k++)
        {
            (int dx, int dy) = Neighbours[(start + k) & 3];
            int tx = at.X + dx, ty = at.Y + dy;
            if (!grid.IsWalkable(tx, ty)) continue;
            (int ox, int oy) = SubTile[context.Rng.NextInt(SubTile.Length)];
            if (Walk(context, creature, (tx << 12) | ox, (ty << 12) | oy, RoamingState, Navigation.MoveOptions.Plain))
                return true;
        }
        return false;
    }

    /// <summary>Job 18's arm (<c>0x00462310</c>): the first creature of the target player that is
    /// reachable (<c>0x00471630</c>) — not the nearest. If that is the hero itself, or its route
    /// (<c>0x00471890</c>, options <c>0x41</c>) fails, the arm fails; it does not try the next.</summary>
    /// <remarks>Ours walks the creatures in spawn order, where DK2 walks the player's own list
    /// (<c>player+6</c>), whose order is not read; reachability is the combat system's route test.</remarks>
    private static bool HuntFirst(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        if (TargetPlayer(world, context.Database, creature) is not byte player) return false;
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        var scratch = new List<Steering.Fixed16Vector>();
        foreach (CreatureState prey in world.Creatures)
        {
            if (prey.OwnerPlayerId != player || !CreatureStateMachine.HasState(prey)) continue;
            if (!Combat.CombatTargetRules.CanReach(context.Navigation, creature, definition, prey.PosX, prey.PosY, int.MaxValue, scratch))
                continue;
            return prey.Id != creature.Id && Walk(context, creature, prey.PosX, prey.PosY, HuntingState, 0x41);
        }
        return false;
    }

    /// <summary>State 177 (<c>0x00462560</c>): a random creature of the target player, walked to
    /// again; when it has none, or the draw is the hero, the player's heart, resuming 178.</summary>
    public static bool Hunt(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        byte player = creature.GoodJobPlayerId;
        var theirs = new List<CreatureState>();
        foreach (CreatureState c in world.Creatures)
            if (c.OwnerPlayerId == player && CreatureStateMachine.HasState(c)) theirs.Add(c);
        if (theirs.Count > 0)
        {
            CreatureState prey = theirs[context.Rng.NextInt(theirs.Count)];
            if (prey.Id != creature.Id
                && Walk(context, creature, prey.PosX, prey.PosY, HuntingState, Navigation.MoveOptions.Plain))
                return true;
        }
        return HeartPositionWalk(context, creature, player);
    }

    /// <summary>Job 19's arm (<c>0x00462490</c>): the target player's heart, resuming 178, whose
    /// handler returns 0 — so the hero idles there and runs the job again.</summary>
    private static bool ToTheHeart(SimulationContext context, CreatureState creature, JobType _)
    {
        if (TargetPlayer(context.World, context.Database, creature) is not byte player) return false;
        return HeartWalk(context, creature, player, TunnelOption);
    }

    /// <summary>Job 19's walk (<c>0x004624F0</c>–<c>0x00462544</c>): the room on the player's
    /// <c>DungeonHeartPosition</c> tile, none failing, then a point in it from
    /// <c>0x004E4D90(room, &amp;point, 0, 0)</c>. That draws a random tile along the room's ring and
    /// steps on while its cell test <c>0x00454110</c> rejects; the test accepts every tile of a
    /// heart (type 5), so the point is the random tile. Ours takes the ring in tile-index order and
    /// accepts any room's tile.</summary>
    private static bool HeartWalk(SimulationContext context, CreatureState creature, byte player, int options)
    {
        WorldState world = context.World;
        int position = Combat.HeartSites.PositionOf(world, context.Database, player);
        if (position < 0 || RoomIndex.RoomAt(world, context.Database, position) is not { } room) return false;
        int tile = room.TileIndices[context.Rng.NextInt(room.TileCount)];
        int w = world.Width;
        return Walk(context, creature, CreatureState.CentreOf(tile % w), CreatureState.CentreOf(tile / w), AtTheHeartState, options);
    }

    /// <summary>State 177's fallback (<c>0x004625F7</c>): straight to the centre of the player's
    /// <c>DungeonHeartPosition</c> tile, options 0, no room asked.</summary>
    private static bool HeartPositionWalk(SimulationContext context, CreatureState creature, byte player)
    {
        int position = Combat.HeartSites.PositionOf(context.World, context.Database, player);
        if (position < 0) return false;
        int w = context.World.Width;
        return Walk(context, creature, CreatureState.CentreOf(position % w), CreatureState.CentreOf(position / w), AtTheHeartState, Navigation.MoveOptions.Plain);
    }

    /// <summary>State 222 (<c>0x00461F00</c>): <c>max(PositionInParty &gt;&gt; 1, 1)</c> free tiles
    /// behind the leader's facing, turning a quarter on a blocked step and giving up after five
    /// turns, then a random sub-tile point there. Always succeeds, routed or not.</summary>
    public static bool Follow(SimulationContext context, CreatureState creature)
    {
        if (context.World.CreatureById(creature.LeaderId) is not { } leader) return true;
        (int x, int y) = FollowTile(context.World, context.Database, creature, leader);
        (int ox, int oy) = SubTile[context.Rng.NextInt(SubTile.Length)];
        Walk(context, creature, (x << 12) | ox, (y << 12) | oy, FollowingState, Navigation.MoveOptions.Loose);
        return true;
    }

    /// <summary>The trailing tile. The facing test is DK2's own: <c>h &gt; 0x700 &amp;&amp; h &lt; 0x100</c>
    /// can never hold, so direction 0 is never chosen and a leader facing near 0 is trailed from
    /// direction 3.</summary>
    public static (int X, int Y) FollowTile(WorldState world, GameDatabase database, CreatureState me, CreatureState leader)
    {
        int x, y;
        if (leader.Dk2StateId == Systems.StateShadow.TravelToPoint)
        {
            x = leader.Body.PathTargetPos.X >> 16;
            y = leader.Body.PathTargetPos.Y >> 16;
        }
        else
        {
            x = leader.PosX >> 12;
            y = leader.PosY >> 12;
        }
        return StepBehind(world, database, me, x, y, leader.Heading, Math.Max(me.PositionInParty >> 1, 1));
    }

    /// <summary>The trailing search 222 and 223 share: <paramref name="depth"/> free tiles behind a
    /// facing from (<paramref name="x"/>, <paramref name="y"/>), a quarter turn on a blocked step,
    /// five turns giving up.</summary>
    public static (int X, int Y) StepBehind(WorldState world, GameDatabase database, CreatureState me, int x, int y, int heading, int depth)
    {
        int h = heading & 0x7FF;
        int dir = h is > 0x100 and < 0x300 ? 1 : h is > 0x300 and < 0x500 ? 2 : 3;
        var grid = new WorldPathGrid(database, world, database.CreatureById(me.CreatureTypeId));
        int turns = 0;
        while (depth != 0 && turns != 5)
        {
            x += Behind[dir].X;
            y += Behind[dir].Y;
            if (grid.IsWalkable(x, y))
            {
                depth--;
                turns = 0;
                continue;
            }
            x -= Behind[dir].X;
            y -= Behind[dir].Y;
            dir = (dir - 1) & 3;
            turns++;
        }
        return (x, y);
    }

    private static bool MayStand(
        WorldState world, GameDatabase database, CreatureDef definition, CreatureState creature,
        ActionPointState point, int x, int y)
    {
        ref TileState tile = ref world.Tile(x, y);
        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        if ((point.Flags & 0x40) != 0 && terrain.IsSolid) return false;
        if (terrain.IsRoom && tile.OwnerPlayerId == creature.OwnerPlayerId) return false;
        if (terrain.IsImpenetrable && tile.OwnerPlayerId == creature.OwnerPlayerId) return false;
        if (terrain.IsWater && definition.Flags.HasFlag(CreatureFlags.CanWalkOnWater)) return true;
        if (terrain.IsLava && definition.Flags.HasFlag(CreatureFlags.CanWalkOnLava)) return true;
        return !terrain.IsImpenetrable;
    }

    private static ActionPointState? PointById(WorldState world, byte id)
    {
        foreach (ActionPointState p in world.ActionPoints)
            if (p.Id == id) return p;
        return null;
    }

    /// <summary>One of the nine sub-tile offsets (<c>0x00698EC8</c>).</summary>
    public static (int X, int Y) RandomSubTile(SimulationContext context) => SubTile[context.Rng.NextInt(SubTile.Length)];

    /// <summary>DK2's <c>0x00471890(point, resume, options)</c>: route, then state 6 resuming; with
    /// option bit 0 a failed route falls back to tunnelling (<see cref="TunnelRules"/>).</summary>
    private static bool Walk(SimulationContext context, CreatureState creature, int x, int y, int resume, int options) =>
        TunnelRules.RouteOrTunnel(context, "Hero", creature, x, y, Systems.StateShadow.TravelToPoint, resume, options, x >> 12, y >> 12);
}
