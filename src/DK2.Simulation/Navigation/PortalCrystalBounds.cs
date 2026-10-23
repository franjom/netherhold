// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Navigation;

// Explicit clipping correction, not a native collision claim: the horizontal bounds of
// PORTAL0/3/5/8's crystal primitives. See portal-crystal-collision.md.
internal readonly record struct PortalCrystalBounds(Fixed16Vector Min, Fixed16Vector Max)
{
    public Fixed16Vector Centre => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2);
    public int Radius => (Max - Min).Length() / 2 + 256;
    public PortalCrystalBounds Expanded(Fp32 radius)
    {
        int pad = Math.Max(0, radius.Raw) << 4;
        return new(new(Min.X - pad, Min.Y - pad), new(Max.X + pad, Max.Y + pad));
    }

    public bool Contains(Fixed16Vector point) =>
        point.X > Min.X && point.X < Max.X && point.Y > Min.Y && point.Y < Max.Y;

    public bool Allows(Fixed16Vector from, Fixed16Vector to)
    {
        if (from == to) return true;
        if (Contains(from))
        {
            int nearest = Math.Min(Math.Min(from.X - Min.X, Max.X - from.X),
                Math.Min(from.Y - Min.Y, Max.Y - from.Y));
            return (from.X - Min.X == nearest && to.X <= from.X)
                || (Max.X - from.X == nearest && to.X >= from.X)
                || (from.Y - Min.Y == nearest && to.Y <= from.Y)
                || (Max.Y - from.Y == nearest && to.Y >= from.Y);
        }
        long dx = (long)to.X - from.X, dy = (long)to.Y - from.Y;
        long mx = (long)from.X + to.X - Min.X - Max.X, my = (long)from.Y + to.Y - Min.Y - Max.Y;
        long ex = Max.X - Min.X, ey = Max.Y - Min.Y;
        if (Math.Abs(mx) >= ex + Math.Abs(dx) || Math.Abs(my) >= ey + Math.Abs(dy)) return true;
        return Math.Abs(mx * dy - my * dx) >= ex * Math.Abs(dy) + ey * Math.Abs(dx);
    }

    public Fixed16Vector Project(Fixed16Vector from, Fixed16Vector target)
    {
        if (!Contains(target)) return target;
        Fixed16Vector best = new(Min.X - 256, target.Y);
        long distance = Squared(best - target), approach = Squared(best - from);
        Consider(new(Max.X + 256, target.Y));
        Consider(new(target.X, Min.Y - 256));
        Consider(new(target.X, Max.Y + 256));
        return best;
        void Consider(Fixed16Vector point)
        {
            long next = Squared(point - target), nextApproach = Squared(point - from);
            if (next < distance || (next == distance && nextApproach < approach))
            { distance = next; approach = nextApproach; best = point; }
        }
    }

    private static long Squared(Fixed16Vector value) => (long)value.X * value.X + (long)value.Y * value.Y;

    public static List<PortalCrystalBounds> Build(WorldState world, GameDatabase database)
    {
        var result = new List<PortalCrystalBounds>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.Definition.TypeId is not (RoomTypeId.Portal or RoomTypeId.HeroPortal)) continue;
            var tiles = new HashSet<int>(room.TileIndices);
            foreach (int tile in room.TileIndices)
            {
                int x = tile % world.Width, y = tile / world.Width;
                if (x == 0 || y == 0 || x == world.Width - 1 || y == world.Height - 1) continue;
                int cx = !tiles.Contains(tile - 1) ? 0 : tiles.Contains(tile + 1) ? 1 : 2;
                int cy = !tiles.Contains(tile - world.Width) ? 0 : tiles.Contains(tile + world.Width) ? 1 : 2;
                PortalCrystalBounds? local = (cx + 3 * cy) switch
                {
                    0 => new(new(18160, -2851), new(53288, 34250)),
                    3 => new(new(18140, 3892), new(55234, 32768)),
                    5 => new(new(-44421, -47743), new(-16384, -10367)),
                    8 => new(new(-43412, -47947), new(-22642, -19831)),
                    _ => null,
                };
                if (local is not { } bounds) continue;
                var centre = new Fixed16Vector(CreatureState.CentreOf(x) << 4, CreatureState.CentreOf(y) << 4);
                result.Add(new(bounds.Min + centre, bounds.Max + centre));
            }
        }
        return result;
    }
}
