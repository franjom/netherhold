// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;
using Xunit.Abstractions;
using static DK2.Oracle.Tests.OriginalCpu;

namespace DK2.Oracle.Tests;

/// <summary>
/// The steering layer on the whole-process dump of Level 4 seed 1 at tick 500 (the recorder's
/// <c>--dump-ticks</c>, <see cref="CapturedState.LoadWithDump"/>): DKII.EXE's own functions run
/// on the game's memory, ours on the same values.
/// </summary>
public class CapturedSteeringTests(ITestOutputHelper log)
{
    private const string Capture = "reverse-engineering/dk2runs/level4-dump/level4.dk2cap";
    private const string Dump = "reverse-engineering/dk2runs/level4-dump/level4.dk2cap.500.mem.z";
    private const uint BodyPos = 0x84, BodyVel = 0x8C, BodyRadius = 0xDC, BodyWindow = 0xB4, BodyFlags = 0x10C;

    /// <summary><c>Avoid_Walls</c> (<c>0x004D35C0</c>, stdcall (out*, body*, unused)) against
    /// <see cref="AvoidWallsRules.Compute"/>, both on DK2's own navigation bytes, so this holds the
    /// behaviour and not our grid (<see cref="OurNavigationBytes_AreDk2s_WithTheHeartAndDoorStamps"/> holds that). First every
    /// live creature's body and mover as the dump has them; then synthetic bodies written into one
    /// of them across the same map, each with a random mover (owner number, the three door bits,
    /// water and lava), since every live creature at tick 500 walks on water. The steering globals
    /// are written per call as the route request sets them (navigation-mesh-and-search.md §4);
    /// the water flag <c>0x006EC9E4</c>, which the per-tick setter <c>0x004D5A40</c> leaves at the
    /// last request's value, is given the case's own, as our profile has it.</summary>
    [CapturedFact(Dump)]
    public void AvoidWalls_OnTheDump_MatchesTheOriginal()
    {
        CapturedState state = CapturedState.LoadWithDump(Capture, 500, Dump);
        Assert.NotNull(state.NavBytes);
        var grid = new GridWorld(state.Width, state.Height, state.NavBytes!);
        CapturedCreature[] live = [.. state.Creatures.Where(c => c.Body is not null && c.Mover is not null)];
        Assert.NotEmpty(live);

        var cases = new List<(CapturedCreature Owner, CapturedMover Mover, SteeringBody Body, bool Real)>();
        foreach (CapturedCreature c in live) cases.Add((c, c.Mover!, BodyOf(c.Body!), true));
        cases.AddRange(Synthetic(state, live[0]));

        IReadOnlyList<Outcome> original = Run([.. cases.Select(x => Request(x.Owner, x.Mover, x.Body, x.Real))], state.MemoryImage);
        var mismatches = new List<string>();
        var reached = new Dictionary<string, int>
        {
            ["nothing"] = 0, ["axis margin"] = 0, ["corner reflection"] = 0, ["stuck nudge"] = 0,
            ["a water wall"] = 0, ["a door wall"] = 0,
        };
        for (int i = 0; i < cases.Count; i++)
        {
            (CapturedCreature owner, CapturedMover mover, SteeringBody body, bool real) = cases[i];
            string at = $"{(real ? "creature" : "synthetic on")} {owner.Slot}: pos ({body.Pos.X}, {body.Pos.Y}) vel ({body.Vel.X}, {body.Vel.Y}) r {body.Radius} stuck {body.EscapeStuckMode} point {body.SubpathIndex}";
            if (original[i].Fault is { } fault) { mismatches.Add($"{at}: original fault ({fault})"); continue; }
            var theirs = new Fixed16Vector((int)original[i].Reads[0], (int)original[i].Reads[1]);
            Fixed16Vector ours = AvoidWallsRules.Compute(body, Profile(mover), grid);
            reached[Arm(body, Profile(mover), grid, theirs)]++;
            if (WallKind(body, Profile(mover), grid, theirs) is { } wall) reached[wall]++;
            if (theirs != ours) mismatches.Add($"{at}: original ({theirs.X}, {theirs.Y}), ours ({ours.X}, {ours.Y})");
        }
        log.WriteLine($"{live.Length} real bodies, {cases.Count - live.Length} synthetic; arms (by our probes on DK2's bytes): " +
                      string.Join(", ", reached.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {cases.Count} differ; first:\n" + string.Join("\n", mismatches.Take(15)));
        Assert.True(reached.Values.All(n => n > 0), "an arm no case reaches: " +
            string.Join(", ", reached.Where(kv => kv.Value == 0).Select(kv => kv.Key)));
    }

    /// <summary>Our navigation bytes (<see cref="WorldSteering"/> over our world rebuilt from the
    /// level, run through its first door update, and given the tick's tiles and DK2's door states)
    /// against DK2's grid at the same tick, every tile. Two bytes outlive their writers: the Heart
    /// builder's 3 on its chain tile 12, the centre (24,54) (Q601), and the 0 the hero door's
    /// removal leaves on (24,9)'s water after the owner test kills it on that first update (Q600).
    /// Both writers are read; that they are what happened here is inferred from the bytes.</summary>
    [CapturedFact(Dump)]
    public void OurNavigationBytes_AreDk2s_WithTheHeartAndDoorStamps()
    {
        CapturedState state = CapturedState.LoadWithDump(Capture, 500, Dump);
        (GameDatabase database, WorldState world) = CapturedState.Level("Level4");
        new GameSimulation(database, world, 1, [new DoorHealthSystem(), new DoorRemovalSystem()]).Run(1);
        Assert.Equal([new NavStamp(54 * world.Width + 24, 14, NavBytes.Lava), new NavStamp(9 * world.Width + 24, 4, NavBytes.Floor)],
            world.NavStamps);
        state.ApplyTiles(world);
        IReadOnlyList<string> doors = state.ApplyDoors(world);
        var steering = new WorldSteering(database, world);
        steering.Refresh();
        var differ = new List<string>();
        for (int y = 0; y < state.Height; y++)
        for (int x = 0; x < state.Width; x++)
        {
            int theirs = state.NavBytes![y * state.Width + x], ours = steering.NavByte(x, y);
            if (theirs != ours) differ.Add($"({x},{y}) ours {ours}, DK2 {theirs} (terrain {state.Terrain(x, y)}, owner {state.Owner(x, y)})");
        }
        Assert.Empty(doors);
        Assert.Empty(differ);
    }

    private static Request Request(CapturedCreature owner, CapturedMover m, SteeringBody body, bool real)
    {
        var writes = new List<(uint, uint)>
        {
            (0x006EC9D0, (uint)m.Number), (0x006EC9D4, (m.Flags3E6 >> 17) & 1), (0x006EC9D8, (m.Flags3E6 >> 18) & 1),
            (0x006EC9E0, (m.Flags3E6 >> 19) & 1), (0x006EC9DC, (m.Def686 >> 5) & 1), (0x006EC9E4, (m.Def686 >> 4) & 1),
        };
        if (!real)
        {
            // Every field the function reads, so no case sees an earlier one's: position, velocity,
            // the radius word, the window point the index names, and the index and stuck bytes.
            uint b = owner.Body!.Ptr;
            writes.AddRange([
                (b + BodyPos, (uint)body.Pos.X), (b + BodyPos + 4, (uint)body.Pos.Y),
                (b + BodyVel, (uint)body.Vel.X), (b + BodyVel + 4, (uint)body.Vel.Y),
                (b + BodyRadius, (uint)(ushort)body.Radius),
                (b + BodyWindow + 8 * (uint)body.SubpathIndex, (uint)body.CurrentPoint.X),
                (b + BodyWindow + 8 * (uint)body.SubpathIndex + 4, (uint)body.CurrentPoint.Y),
                (b + BodyFlags, (uint)((body.SubpathIndex << 3) << 16 | (body.EscapeStuckMode ? 2 : 0) << 24)),
            ]);
        }
        return new Request(0x004D35C0, [Movable, owner.Body!.Ptr, 0], [.. writes], [Movable, Movable + 4]) { Scratchy = true };
    }

    private static SteeringBody BodyOf(CapturedBody b)
    {
        var body = new SteeringBody
        {
            Pos = new Fixed16Vector(b.Pos[0], b.Pos[1]),
            Vel = new Fixed16Vector(b.Vel[0], b.Vel[1]),
            Radius = b.Radius,
            EscapeStuckMode = (b.B10F & 2) != 0,
            SubpathIndex = (b.B10E >> 3) & 7,
        };
        for (int i = 0; i < SteeringBody.WindowSize; i++) body.Subpath[i] = new Fixed16Vector(b.Window[i][0], b.Window[i][1]);
        return body;
    }

    private static MoverProfile Profile(CapturedMover m) =>
        new(CanWalkOnWater: ((m.Def686 >> 4) & 1) != 0, CanWalkOnLava: ((m.Def686 >> 5) & 1) != 0, Owner: (byte)m.Number,
            RespectDoors: ((m.Flags3E6 >> 19) & 1) != 0, PassOwnLockedDoors: ((m.Flags3E6 >> 17) & 1) != 0,
            EntersPortalHole: ((m.Flags3E6 >> 18) & 1) != 0);

    /// <summary>Bodies on every open tile of the map, a tenth beside its doors: positions anywhere in
    /// the tile, headings all round at speeds to two tiles a tick, the shipped radius or another,
    /// a quarter of them stuck with a route point near by.</summary>
    private static IEnumerable<(CapturedCreature, CapturedMover, SteeringBody, bool)> Synthetic(CapturedState state, CapturedCreature host)
    {
        var spread = new Random(20261015);
        int[] open = [.. Enumerable.Range(0, state.Width * state.Height).Where(i => state.NavBytes![i] != NavBytes.Solid)];
        // A tenth beside a door, which only four tiles of this map hold.
        int[] byDoors = [.. open.Where(i => new[] { -1, 1, -state.Width, state.Width }
            .Any(d => i + d >= 0 && i + d < state.NavBytes!.Length && (state.NavBytes[i + d] & 0xF0) != 0))];
        for (int i = 0; i < 20000; i++)
        {
            int tile = i % 10 == 0 && byDoors.Length > 0 ? byDoors[spread.Next(byDoors.Length)] : open[spread.Next(open.Length)];
            var pos = new Fixed16Vector(((tile % state.Width) << 16) + spread.Next(0, 0x10000), ((tile / state.Width) << 16) + spread.Next(0, 0x10000));
            int speed = spread.Next(6) == 0 ? 0 : 1 << spread.Next(8, 18);
            double turn = spread.NextDouble() * 2 * Math.PI;
            var body = new SteeringBody
            {
                Pos = pos,
                Vel = new Fixed16Vector((int)(Math.Cos(turn) * speed), (int)(Math.Sin(turn) * speed)),
                // Not 0: the corner reflection divides by the radius (0x004D1EC0), a fault in both.
                Radius = spread.Next(3) == 0 ? spread.Next(1, 0x8000) : host.Body!.Radius,
                EscapeStuckMode = spread.Next(4) == 0,
                SubpathIndex = spread.Next(0, SteeringBody.WindowSize),
            };
            body.Subpath[body.SubpathIndex] = pos + new Fixed16Vector(spread.Next(-0x30000, 0x30000), spread.Next(-0x30000, 0x30000));
            var mover = new CapturedMover(spread.Next(1, 8), (uint)spread.Next(0, 8) << 17, (uint)spread.Next(0, 4) << 4);
            yield return (host, mover, body, false);
        }
    }

    /// <summary>Which arm a case is in, judged by our probes on DK2's bytes and the original's
    /// answer.</summary>
    private static string Arm(SteeringBody body, MoverProfile mover, ISteeringWorld world, Fixed16Vector theirs)
    {
        if (body.EscapeStuckMode) return "stuck nudge";
        if (theirs == Fixed16Vector.Zero) return "nothing";
        (int tx, int ty) = body.Pos.Tile();
        byte own = world.NavByte(tx, ty);
        int sx = body.Vel.X < 0 ? -1 : 1, sy = body.Vel.Y < 0 ? -1 : 1;
        bool Open(int x, int y) => MeshRouter.TransitionCost(world.NavByte(x, y), own, mover) != 0;
        return !Open(tx + sx, ty + sy) && Open(tx, ty + sy) && Open(tx + sx, ty) ? "corner reflection" : "axis margin";
    }

    /// <summary>Whether the original's force came from a probe this mover may not enter only
    /// because of what it is: water or lava it cannot walk, or a door it may not pass.</summary>
    private static string? WallKind(SteeringBody body, MoverProfile mover, ISteeringWorld world, Fixed16Vector theirs)
    {
        if (body.EscapeStuckMode || theirs == Fixed16Vector.Zero) return null;
        (int tx, int ty) = body.Pos.Tile();
        byte own = world.NavByte(tx, ty);
        int sx = body.Vel.X < 0 ? -1 : 1, sy = body.Vel.Y < 0 ? -1 : 1;
        foreach ((int x, int y) in new[] { (tx, ty + sy), (tx + sx, ty + sy), (tx + sx, ty) })
        {
            byte b = world.NavByte(x, y);
            if (b == NavBytes.Solid || MeshRouter.TransitionCost(b, own, mover) != 0) continue;
            if ((b & 0xF0) != 0) return "a door wall";
            if ((b & 3) is 1 or 3) return "a water wall";
        }
        return null;
    }

    /// <summary>DK2's grid as an <see cref="ISteeringWorld"/>; <c>Avoid_Walls</c> asks only for
    /// bytes, and off the map the original's probe answers solid (8).</summary>
    private sealed class GridWorld(int width, int height, int[] bytes) : ISteeringWorld
    {
        public byte NavByte(int x, int y) =>
            x < 0 || y < 0 || x >= width || y >= height ? NavBytes.Solid : (byte)bytes[y * width + x];

        public bool CanMove(Fixed16Vector from, Fixed16Vector to) => throw new NotSupportedException();
        public Fixed16Vector ConstrainTarget(Fixed16Vector from, Fixed16Vector target) => throw new NotSupportedException();
        public void CollectObstacles(Fixed16Vector centre, int range, List<SteeringObstacle> into) => throw new NotSupportedException();
        public int CrowdWeight(Fixed16Vector point, int span) => throw new NotSupportedException();
        public bool Route(Fixed16Vector from, Fixed16Vector to, MoverProfile mover, List<Fixed16Vector> points) => throw new NotSupportedException();
        public void CollectNeighbours(Fixed16Vector centre, int range, List<SteeringNeighbour> into) => throw new NotSupportedException();
        public SteeringBody? RelatedBody(SteeringMover mover, out int reachTiles) => throw new NotSupportedException();
    }
}
