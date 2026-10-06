// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>State 186's controller fields: the stand point <c>+0xA7/+0xAB</c> and dig point
/// <c>+0xAF/+0xB3</c> (positions), the target tile <c>+0xB7/+0xB9</c>, the phase <c>+0xBB</c>, the
/// wall-follow exit <c>+0xBF/+0xC1</c> and hit point <c>+0xC3/+0xC5</c> (tiles), the facing
/// <c>+0xC7</c>, the reach flag <c>+0xCB</c>, the hand <c>+0xCF</c> and the resume <c>+0xD3</c>;
/// ours adds the end of the dig swing it is waiting on.</summary>
public struct TunnelRecord
{
    public int StandX, StandY, DigX, DigY, TargetX, TargetY, Phase, ExitX, ExitY, HitX, HitY, Facing, Resume;
    public bool Reach, Hand;
    public long ActionUntilTick;
}

/// <summary>A hero worker digging its own way (tunnelling.md, ADR-0222): the route helpers'
/// option-bit-0 fallback, the tunnel request, state 186 with its planner and stand point, and the
/// leader's 223 and the worker's 224.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/tunnelling.md")]
public static class TunnelRules
{
    public const int TunnellingState = 186;
    public const int AdvancingState = 223;
    public const int HandBackState = 224;

    /// <summary>How far from its stand point a tunneller may be and still dig (<c>0x555</c>, a third of a tile).</summary>
    public const int StandRange = 0x555;

    /// <summary>The wall-follow's reach past an obstacle before it gives up (<c>0x50</c>).</summary>
    public const int ExitSearchLimit = 0x50;

    /// <summary><c>0x0069A070</c>: north, east, south, west.</summary>
    private static readonly (int X, int Y)[] Dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>The route helpers <c>0x00471890</c> and <c>0x00471A70</c>: route and travel, or with
    /// option bit 0 fall back to a tunnel toward <paramref name="tunnelX"/>, <paramref name="tunnelY"/>.</summary>
    public static bool RouteOrTunnel(
        SimulationContext context, string system, CreatureState creature, int x, int y,
        int travelState, int resume, Navigation.MoveOptions options, int tunnelX, int tunnelY)
    {
        if (context.World.Contains(new TilePos(x / Fp32.One, y / Fp32.One))
            && !IsSolid(context, x >> 12, y >> 12)
            && context.Navigation.TryRouteTo(creature, x, y, options))
        {
            CreatureStateMachine.BeginTravel(context.Hooks, creature, travelState, resume);
            return true;
        }
        if (((int)options & 0x01) == 0) return false;
        return Fallback(context, system, creature, tunnelX, tunnelY, resume);
    }

    /// <summary>The failure path (<c>0x0047193A</c>–<c>0x00471A56</c>): a hero worker tunnels itself;
    /// a party leader lends the errand to the first worker follower whose request is taken, which
    /// resumes 224, and walks behind it in 223.</summary>
    public static bool Fallback(SimulationContext context, string system, CreatureState creature, int tunnelX, int tunnelY, int resume)
    {
        if (IsWorker(context, creature) && HeroJobRules.IsHero(creature)
            && Start(context, system, creature, tunnelX, tunnelY, resume))
            return true;
        if (creature.PartyId == HeroPartyRules.NoParty || (creature.Flags & 0x02) == 0 || creature.PositionInParty == 0)
            return false;
        foreach (CreatureState c in context.World.Creatures)
        {
            if (c.OwnerPlayerId != creature.OwnerPlayerId || c.PartyId != creature.PartyId) continue;
            if (!HeroJobRules.IsFollower(c) || !IsWorker(context, c)) continue;
            if (!Start(context, system, c, tunnelX, tunnelY, HandBackState)) continue;
            creature.LeaderId = c.Id;
            StateShadow.Move(context, system, creature, AdvancingState);
            return true;
        }
        return false;
    }

    /// <summary>The feasibility test's fallback (<c>0x00471740</c> with bit 0, <c>0x00485B50</c>): a
    /// hero worker, or a leader with a worker follower, could tunnel there.</summary>
    public static bool CouldTunnel(SimulationContext context, CreatureState creature)
    {
        if (IsWorker(context, creature) && HeroJobRules.IsHero(creature)) return true;
        if (creature.PartyId == HeroPartyRules.NoParty || (creature.Flags & 0x02) == 0 || creature.PositionInParty == 0)
            return false;
        foreach (CreatureState c in context.World.Creatures)
            if (c.OwnerPlayerId == creature.OwnerPlayerId && c.PartyId == creature.PartyId
                && HeroJobRules.IsFollower(c) && IsWorker(context, c))
                return true;
        return false;
    }

    /// <summary>The tunnel request (<c>0x00485B80</c>, <c>0x00485AB0</c>): 186 must be taken; the
    /// target tile, dig and stand points at the creature, the resume, phase 0.</summary>
    public static bool Start(SimulationContext context, string system, CreatureState creature, int tileX, int tileY, int resume)
    {
        if (!StateShadow.TryMove(context, system, creature, TunnellingState)) return false;
        creature.StopMoving();
        creature.Tunnel = new TunnelRecord
        {
            TargetX = tileX, TargetY = tileY,
            DigX = creature.PosX, DigY = creature.PosY,
            StandX = creature.PosX, StandY = creature.PosY,
            Resume = resume,
        };
        context.Emit(system, "tunnel_started",
            $"creature={creature.Id} at=({creature.Tile.X},{creature.Tile.Y}) target=({tileX},{tileY}) resume={resume}");
        return true;
    }

    /// <summary>State 186 (<c>0x00485C10</c>). False goes to idle.</summary>
    public static bool Tunnel(SimulationContext context, string system, CreatureState creature)
    {
        ref TunnelRecord t = ref creature.Tunnel;
        if (context.Tick.Value < t.ActionUntilTick) return true;
        int dx = t.DigX >> 12, dy = t.DigY >> 12;

        if (!IsSolid(context, dx, dy))
        {
            if (Walk(context, creature, Centre(t.TargetX), Centre(t.TargetY), t.Resume, 0)) return true;
            if (NextDig(context, creature))
            {
                if (Stand(context, creature) && Walk(context, creature, t.StandX, t.StandY, TunnellingState, 0)) return true;
                StateShadow.Move(context, system, creature, HeroJobRules.WaitFailedState);
                return false;
            }
            // Without the reach flag DK2 sets 86, wandering, which is not built: ours idles.
            return t.Reach && Walk(context, creature, Centre(t.TargetX), Centre(t.TargetY), t.Resume, 0);
        }

        if (Math.Abs(t.StandX - creature.PosX) > StandRange || Math.Abs(t.StandY - creature.PosY) > StandRange)
            return Stand(context, creature) && Walk(context, creature, t.StandX, t.StandY, TunnellingState, 0);

        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        int heading = creature.Heading;
        int left = Steering.Bearing.TurnToward(ref heading, Steering.Bearing.Of(t.DigX - creature.PosX, t.DigY - creature.PosY), definition.TurnRate);
        creature.Heading = heading;
        if (left != 0) return true;

        return Dig(context, system, creature, dx, dy);
    }

    /// <summary>State 223 (<c>0x00462130</c>): behind the tunneller, one free tile past its facing,
    /// then a random sub-tile point, resuming 223. A gone tunneller clears the word and idles.</summary>
    public static bool Advance(SimulationContext context, CreatureState leader)
    {
        if (context.World.CreatureById(leader.LeaderId) is not { } worker || CreatureDeathRules.IsCorpse(worker))
        {
            leader.LeaderId = -1;
            return false;
        }
        (int x, int y) = HeroJobRules.StepBehind(context.World, context.Database, leader,
            worker.PosX >> 12, worker.PosY >> 12, worker.Heading, 1);
        (int ox, int oy) = HeroJobRules.RandomSubTile(context);
        Walk(context, leader, (x << 12) | ox, (y << 12) | oy, AdvancingState, Navigation.MoveOptions.Loose);
        return true;
    }

    /// <summary>State 224 (<c>0x004620F0</c>), the worker's: its leader to 1, then itself to 222.</summary>
    public static bool HandBack(SimulationContext context, string system, CreatureState worker)
    {
        if (context.World.CreatureById(worker.LeaderId) is { } leader)
        {
            leader.StopMoving();
            StateShadow.Move(context, system, leader, CreatureStateMachine.Idle);
        }
        StateShadow.Move(context, system, worker, HeroJobRules.FollowingState);
        return true;
    }

    /// <summary>The swing: action 1, or 2 on a tile with a <c>GoldValue</c>, aimed at the dig tile. Ours
    /// queues one application of imp work and waits a work period.</summary>
    private static bool Dig(SimulationContext context, string system, CreatureState creature, int x, int y)
    {
        WorldState world = context.World;
        int index = (y * world.Width) + x;
        ref TileState tile = ref world.TileAtIndex(index);
        TileWorkKind kind = context.Database.TerrainById(tile.TerrainId).GoldValue != 0 ? TileWorkKind.MineGold : TileWorkKind.Dig;
        if (kind == TileWorkKind.Dig
            ? !TileWorkRules.TryResolveDigTask(context.Database, tile, kind, creature.OwnerPlayerId, out _)
            : !TileWorkRules.Applies(world, context.Database, tile, kind, creature.OwnerPlayerId))
            return false;
        world.QueueWork(new TileWorkOrder(index, kind, creature.OwnerPlayerId, creature.Id));
        creature.Tunnel.ActionUntilTick = context.Tick.Value + SimulationTiming.WorkApplicationPeriodTicks;
        return true;
    }

    /// <summary><c>0x00485EF0</c>: the next dig tile (tunnelling.md §4). True with one; false at the
    /// target, when the target is reachable (reach flag set), or when the wall-follow fails.</summary>
    private static bool NextDig(SimulationContext context, CreatureState creature)
    {
        ref TunnelRecord t = ref creature.Tunnel;
        byte me = creature.OwnerPlayerId;
        t.Reach = true;

        if (t.Phase == 0)
        {
            int x = t.DigX >> 12, y = t.DigY >> 12;
            bool asked = false;
            int stepX;
            bool blocked;
            while (true)
            {
                t.DigX = Centre(x);
                t.DigY = Centre(y);
                if (x == t.TargetX && y == t.TargetY) return false;
                if (Math.Abs(y - t.TargetY) < Math.Abs(x - t.TargetX))
                {
                    stepX = t.TargetX < x ? -1 : 1;
                    x += stepX;
                }
                else
                {
                    stepX = 0;
                    y += t.TargetY < y ? -1 : 1;
                }
                blocked = IsSolid(context, x, y);
                if (blocked || Terrain(context, x, y).IsLava) break;
                if (!asked)
                {
                    if (Reachable(context, creature, Centre(t.TargetX), Centre(t.TargetY))) return false;
                    asked = true;
                }
            }

            if (IsDiggable(context, x, y) && Owner(context, x, y) != me)
            {
                t.DigX = Centre(x);
                t.DigY = Centre(y);
                return true;
            }

            int hx = t.DigX >> 12, hy = t.DigY >> 12;
            // DK2's own facing: a y step reads the blocked bit, not its sign.
            t.Facing = stepX >= 1 ? 1 : blocked ? 2 : stepX < 0 ? 3 : 0;
            t.Phase = 1;

            int ex = hx, ey = hy;
            for (int n = 1; ; n++)
            {
                if (Math.Abs(ey - t.TargetY) < Math.Abs(ex - t.TargetX)) ex += t.TargetX < ex ? -1 : 1;
                else ey += t.TargetY < ey ? -1 : 1;
                TerrainDef f = Terrain(context, ex, ey);
                bool impenetrable = f.IsSolid && f.IsImpenetrable;
                bool own = f.IsSolid && Owner(context, ex, ey) == me;
                if (!impenetrable && !own && !f.IsLava) break;
                if (n > ExitSearchLimit) return false;
            }
            t.ExitX = ex;
            t.ExitY = ey;
            t.HitX = hx;
            t.HitY = hy;
            t.Hand = Hand(context, creature, hx, hy, ex, ey, t.Facing);
            t.Facing = (t.Facing + (t.Hand ? 1 : -1)) & 3;
        }
        if (t.Phase != 1) return true;

        int dir = t.Facing;
        int leftward = (dir - 1) & 3, rightward = (dir + 1) & 3;
        int px = t.DigX >> 12, py = t.DigY >> 12;
        int laps = 4;
        do
        {
            while (true)
            {
                if (px == t.ExitX && py == t.ExitY)
                {
                    t.DigX = Centre(t.ExitX);
                    t.DigY = Centre(t.ExitY);
                    t.Phase = 0;
                    return true;
                }
                int d = t.Hand ? leftward : rightward;
                int cx = px + Dirs[d].X, cy = py + Dirs[d].Y;
                if (IsDiggable(context, cx, cy) && Owner(context, cx, cy) != me && !Terrain(context, cx, cy).IsLava)
                {
                    t.DigX = Centre(cx);
                    t.DigY = Centre(cy);
                    t.Facing = d;
                    return true;
                }
                if (Terrain(context, cx, cy).IsSolid || Terrain(context, cx, cy).IsLava) break;
                px = cx;
                py = cy;
                leftward = (d - 1) & 3;
                rightward = (d + 1) & 3;
                dir = d;
                if (px == t.HitX && py == t.HitY)
                {
                    t.Reach = false;
                    return false;
                }
            }

            bool walk = false;
            for (int k = 0; k < 4; k++)
            {
                int cx = px + Dirs[dir].X, cy = py + Dirs[dir].Y;
                if (IsDiggable(context, cx, cy) && Owner(context, cx, cy) != me && !Terrain(context, cx, cy).IsLava) break;
                if (!Terrain(context, cx, cy).IsSolid && !Terrain(context, cx, cy).IsLava) { walk = true; break; }
                dir = (dir + (t.Hand ? 1 : -1)) & 3;
            }
            if (!walk)
            {
                t.DigX = Centre(px + Dirs[dir].X);
                t.DigY = Centre(py + Dirs[dir].Y);
                t.Facing = dir;
                return true;
            }
            px += Dirs[dir].X;
            py += Dirs[dir].Y;
            leftward = (dir - 1) & 3;
            rightward = (dir + 1) & 3;
        }
        while (px != t.HitX || py != t.HitY || --laps != 0);

        t.Reach = false;
        return false;
    }

    /// <summary><c>0x004868D0</c>: two walkers round the obstacle from the hit point, one on each
    /// hand; true when the first reaches the exit first. Four returns of the first to the start give
    /// false.</summary>
    private static bool Hand(SimulationContext context, CreatureState creature, int sx, int sy, int ex, int ey, int dir)
    {
        int a = (dir + 1) & 3, aSide = (a - 1) & 3;
        int b = (dir - 1) & 3, bSide = (b + 1) & 3;
        int ax = sx, ay = sy, bx = sx, by = sy, laps = 0;
        while (true)
        {
            if (ax == sx && ay == sy && ++laps == 4) return false;
            if (ax == ex && ay == ey) return true;

            if (Passable(context, creature, ax + Dirs[aSide].X, ay + Dirs[aSide].Y))
            {
                ax += Dirs[aSide].X;
                ay += Dirs[aSide].Y;
                a = (a - 1) & 3;
                aSide = (a - 1) & 3;
            }
            else
            {
                for (int k = 0; k < 4; k++)
                {
                    if (Passable(context, creature, ax + Dirs[a].X, ay + Dirs[a].Y)) { ax += Dirs[a].X; ay += Dirs[a].Y; break; }
                    a = (a + 1) & 3;
                    aSide = (a - 1) & 3;
                }
            }

            if (bx == ex && by == ey) return false;
            if (Passable(context, creature, bx + Dirs[bSide].X, by + Dirs[bSide].Y))
            {
                bx += Dirs[bSide].X;
                by += Dirs[bSide].Y;
                b = (b + 1) & 3;
                bSide = (b + 1) & 3;
            }
            else
            {
                for (int k = 0; k < 4; k++)
                {
                    if (Passable(context, creature, bx + Dirs[b].X, by + Dirs[b].Y)) { bx += Dirs[b].X; by += Dirs[b].Y; break; }
                    b = (b - 1) & 3;
                    bSide = (b + 1) & 3;
                }
            }
        }
    }

    /// <summary><c>0x00486680</c>: the dig tile's near x side, near y side, far x side, far y side; the
    /// first clear and reachable one (options 2) is the stand point.</summary>
    private static bool Stand(SimulationContext context, CreatureState creature)
    {
        ref TunnelRecord t = ref creature.Tunnel;
        int dx = t.DigX >> 12, dy = t.DigY >> 12;
        int sx = dx < (creature.PosX >> 12) ? -1 : 1;
        int sy = dy < (creature.PosY >> 12) ? -1 : 1;
        ReadOnlySpan<(int X, int Y)> sides = [(dx - sx, dy), (dx, dy - sy), (dx + sx, dy), (dx, dy + sy)];
        foreach ((int x, int y) in sides)
        {
            if (IsSolid(context, x, y) || !Reachable(context, creature, Centre(x), Centre(y))) continue;
            t.StandX = Centre(x);
            t.StandY = Centre(y);
            return true;
        }
        return false;
    }

    private static bool Walk(SimulationContext context, CreatureState creature, int x, int y, int resume, Navigation.MoveOptions options)
    {
        if (!context.World.Contains(new TilePos(x >> 12, y >> 12)) || IsSolid(context, x >> 12, y >> 12)) return false;
        if (!context.Navigation.TryRouteTo(creature, x, y, options)) return false;
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, resume);
        return true;
    }

    /// <summary><c>0x00471630</c>, the route test.</summary>
    private static bool Reachable(SimulationContext context, CreatureState creature, int x, int y) =>
        Combat.CombatTargetRules.CanReach(context.Navigation, creature, context.Database.CreatureById(creature.CreatureTypeId),
            x, y, int.MaxValue, []);

    /// <summary>World slot <c>+0x208</c>, bit 3 of the route map (<c>0x00509200</c>): ours, the terrain's
    /// <c>Solid</c>. Off the map counts as solid and impenetrable.</summary>
    private static bool IsSolid(SimulationContext context, int x, int y) => Terrain(context, x, y).IsSolid;

    /// <summary><c>0x00453D10</c>: solid and not impenetrable.</summary>
    private static bool IsDiggable(SimulationContext context, int x, int y)
    {
        TerrainDef f = Terrain(context, x, y);
        return f.IsSolid && !f.IsImpenetrable && context.World.Contains(new TilePos(x, y));
    }

    /// <summary>The hand walk's step test: not impenetrable rock, not Lava, and not a full-health wall
    /// of the walker's own player (<c>0x00453CC0</c>).</summary>
    private static bool Passable(SimulationContext context, CreatureState creature, int x, int y)
    {
        if (!context.World.Contains(new TilePos(x, y))) return false;
        TerrainDef f = Terrain(context, x, y);
        if ((f.IsSolid && f.IsImpenetrable) || f.IsLava) return false;
        ref TileState tile = ref context.World.Tile(x, y);
        bool fullWall = tile.OwnerPlayerId != PlayerIds.Neutral && f.IsSolid && f.GoldValue == 0 && tile.Health == f.MaxHealth;
        return !fullWall || tile.OwnerPlayerId != creature.OwnerPlayerId;
    }

    private static byte Owner(SimulationContext context, int x, int y) =>
        context.World.Contains(new TilePos(x, y)) ? context.World.Tile(x, y).OwnerPlayerId : (byte)0;

    private static TerrainDef Terrain(SimulationContext context, int x, int y)
    {
        if (!context.World.Contains(new TilePos(x, y))) return BorderTerrain(context);
        return context.Database.TerrainById(context.World.Tile(x, y).TerrainId);
    }

    /// <summary>Off the map: any impenetrable solid terrain, so it is never dug or walked.</summary>
    private static TerrainDef BorderTerrain(SimulationContext context)
    {
        foreach (TerrainDef f in context.Database.Terrain.Values)
            if (f.IsSolid && f.IsImpenetrable) return f;
        throw new InvalidOperationException("No impenetrable terrain in the database.");
    }

    private static bool IsWorker(SimulationContext context, CreatureState creature) =>
        context.Database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker);

    private static int Centre(int tile) => CreatureState.CentreOf(tile);
}
