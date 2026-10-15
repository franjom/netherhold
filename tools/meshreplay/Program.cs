using System.Globalization;
using DK2.Pathfinding;

// meshreplay <capture dir> [mode] [v]: replays a dk2move.py capture through our navigation mesh and
// compares it with the original's mesh dumps (navigation-partial-rebuild.md §5). Modes: fresh (a
// whole-map build at each dump), edit (one build, then Retriangulate per logged byte change), edit0
// (the same from the first byte table, tick 1 included), import (DK2's first dump imported slot for
// slot, then edits), step (each interval from DK2's previous dump), case k x y (one interval's region).
string dir = args[0];
string mode = args.Length > 1 ? args[1] : "all";
bool verbose = args.Length > 2 && args[2] == "v";

string prefix = Directory.GetFiles(dir, "*.csv").Single(f => !Path.GetFileName(f).Contains(".nav"));  // the probe's position log; its siblings carry the bytes and the dumps
var nav0Lines = File.ReadAllLines(prefix + ".nav0.txt");
string[] head = nav0Lines[0].Split(' ');
int W = int.Parse(head[0]), H = int.Parse(head[1]);
var bytes0 = new byte[W * H];
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
        bytes0[(y * W) + x] = byte.Parse(nav0Lines[1 + y].Substring(2 * x, 2), NumberStyles.HexNumber);

var changes = File.ReadAllLines(prefix + ".nav.csv").Skip(1).Select(l =>
{
    string[] p = l.Split(',');
    return (Tick: long.Parse(p[1]), X: int.Parse(p[2]), Y: int.Parse(p[3]), Byte: byte.Parse(p[4]));
}).ToList();

var dumps = Directory.GetFiles(dir, Path.GetFileName(prefix) + ".mesh*.txt").OrderBy(f => f).Select(Dump.Load).ToList();
Console.WriteLine($"{W}x{H}, {changes.Count} byte changes over {changes.Select(c => c.Tick).Distinct().Count()} ticks, {dumps.Count} dumps (tick {dumps[0].Tick}..{dumps[^1].Tick})");
{
    int neg = 0, pos = 0, zero = 0;
    foreach (Tri t in dumps[0].Tris)
    {
        var a = dumps[0].Pts[t.V[0]]; var b = dumps[0].Pts[t.V[1]]; var c = dumps[0].Pts[t.V[2]];
        int o = NavMesh.Orient(a.Item1, a.Item2, b.Item1, b.Item2, c.Item1, c.Item2);
        if (o < 0) neg++; else if (o > 0) pos++; else zero++;
    }
    Console.WriteLine($"dump 1 winding in our Orient: {neg} negative (clockwise), {pos} positive, {zero} degenerate");
}

if (mode is "fresh" or "all") Run("fresh");
if (mode is "edit" or "all") Run("edit");
if (mode is "edit0") Run("edit0");
if (mode is "import" or "all") Run("import");
if (mode is "step" or "all") Step();
if (mode == "case") Case(int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));

void Case(int k, int cx, int cy)
{
    // Interval k: import dump k-1, apply its changes one by one, print the region around (cx, cy).
    var bytes = (byte[])bytes0.Clone();
    var grid = new Grid(W, H, bytes);
    int ci = 0;
    while (ci < changes.Count && changes[ci].Tick <= dumps[k - 1].Tick) { var c = changes[ci++]; bytes[(c.Y * W) + c.X] = c.Byte; }
    Dump prev = dumps[k - 1], d = dumps[k];
    Console.WriteLine($"bytes around ({cx},{cy}) before:");
    for (int y = cy - 2; y <= cy + 2; y++) Console.WriteLine("   " + string.Join(" ", Enumerable.Range(cx - 2, 5).Select(x => $"{bytes[(y * W) + x]:X2}")));
    Console.WriteLine("DK2 before (dump " + k + "):");
    PrintRegion(prev, cx, cy);
    NavMesh mesh = Import(prev);
    while (ci < changes.Count && changes[ci].Tick <= d.Tick)
    {
        var c = changes[ci++];
        bytes[(c.Y * W) + c.X] = c.Byte;
        mesh.Retriangulate(grid, c.X, c.Y, c.X + 1, c.Y + 1);
        if (Math.Abs(c.X - cx) <= 3 && Math.Abs(c.Y - cy) <= 3)
        {
            Console.WriteLine($"ours after t{c.Tick} ({c.X},{c.Y})={c.Byte:X2}:");
            PrintRegionOurs(mesh, cx, cy);
        }
    }
    Console.WriteLine("DK2 after (dump " + (k + 1) + "):");
    PrintRegion(d, cx, cy);
}

void PrintRegion(Dump d, int cx, int cy)
{
    foreach (Tri t in d.Tris)
    {
        var c = t.V.Select(v => d.Pts[v]).ToArray();
        if (!c.Any(p => Math.Abs(p.Item1 - cx) <= 2 && Math.Abs(p.Item2 - cy) <= 2)) continue;
        Console.WriteLine($"   slot {t.Slot,5} byte {t.Nav:X2} bits {t.Bits:X2} v {string.Join(" ", c.Select(p => $"({p.Item1},{p.Item2})"))} n {string.Join(" ", t.N)}");
    }
}

void PrintRegionOurs(NavMesh m, int cx, int cy)
{
    for (int t = 0; t < m.TriangleSlots; t++)
    {
        if (!m.IsTriangleLive(t)) continue;
        var c = Enumerable.Range(0, 3).Select(i => (m.PointX(m.Vertex(t, i)), m.PointY(m.Vertex(t, i)))).ToArray();
        if (!c.Any(p => Math.Abs(p.Item1 - cx) <= 2 && Math.Abs(p.Item2 - cy) <= 2)) continue;
        // Printed in DK2's order (v0, v2, v1) with DK2's neighbour order (n2, n1, n0) for a like-for-like read.
        Console.WriteLine($"   slot {t,5} byte {m.NavByte(t):X2} cons {(m.IsConstraint(t, 2) ? 1 : 0)}{(m.IsConstraint(t, 1) ? 1 : 0)}{(m.IsConstraint(t, 0) ? 1 : 0)} v ({c[0].Item1},{c[0].Item2}) ({c[2].Item1},{c[2].Item2}) ({c[1].Item1},{c[1].Item2}) n {m.Neighbour(t, 2)} {m.Neighbour(t, 1)} {m.Neighbour(t, 0)}");
    }
}

void Run(string how)
{
    var bytes = (byte[])bytes0.Clone();
    var grid = new Grid(W, H, bytes);
    NavMesh? mesh = null;
    int ci = 0;
    long identSum = 0, dkSum = 0, slotSum = 0, pointSum = 0;
    int perfect = 0, worstK = -1, firstBad = -1; double worst = 1;
    var rows = new List<string>();
    if (how == "edit0") mesh = NavMesh.Build(grid);
    for (int k = 0; k < dumps.Count; k++)
    {
        Dump d = dumps[k];
        while (ci < changes.Count && changes[ci].Tick <= d.Tick)
        {
            var c = changes[ci++];
            bytes[(c.Y * W) + c.X] = c.Byte;
            if (mesh != null && how != "fresh") mesh.Retriangulate(grid, c.X, c.Y, c.X + 1, c.Y + 1);
        }
        if (how == "fresh" || (mesh == null && how != "import")) mesh = NavMesh.Build(grid);
        if (how == "import" && mesh == null) mesh = Import(d);

        Compare(mesh!, d, out int ident, out int slotSame, out int ours, out int ptCommon, out int dkPts, out int ourPts);
        double frac = ident / (double)d.Tris.Count;
        identSum += ident; dkSum += d.Tris.Count; slotSum += slotSame; pointSum += ptCommon;
        bool same = ident == d.Tris.Count && ours == d.Tris.Count;
        if (same) perfect++; else if (firstBad < 0) firstBad = k;
        if (frac < worst) { worst = frac; worstK = k; }
        string row = $"  dump {k + 1,3} tick {d.Tick,5}: dk2 {d.Tris.Count} ours {ours} identical {ident} ({frac:P1}) sameSlot {slotSame} points dk2 {dkPts} ours {ourPts} common {ptCommon} ourSlots {mesh!.TriangleSlots} dk2Slots {d.Slots}";
        rows.Add(row);
        if (verbose || k < 2 || ((how == "import" || how == "fresh") && !same && k == firstBad)) Console.WriteLine(row);
        if ((how == "import" || how == "fresh") && !same && k == firstBad) Diagnose(mesh, d, dumps[Math.Max(0, k - 1)], changes.Where(c => c.Tick > dumps[Math.Max(0, k - 1)].Tick && c.Tick <= d.Tick));
    }
    Console.WriteLine($"{how}: {perfect}/{dumps.Count} dumps identical; triangles identical {identSum}/{dkSum} ({identSum / (double)dkSum:P2}), same slot {slotSum}; points common {pointSum}; worst dump {worstK + 1} at {worst:P1}");
    File.WriteAllLines(Path.Combine(dir, $"replay-{how}.txt"), rows);
}

void Step()
{
    // Bytes at each dump's tick. The byte log and the mesh dumps come from two threads, so a
    // change logged at tick T may already be in the dump at T or only in the next one: each
    // interval is tried with the changes through T - 1, T and T + 2, and the best taken.
    var bytes = (byte[])bytes0.Clone();
    int applied0 = 0;
    while (applied0 < changes.Count && changes[applied0].Tick <= dumps[0].Tick) { var c = changes[applied0++]; bytes[(c.Y * W) + c.X] = c.Byte; }
    int exact = 0, geometryOnly = 0, seeded = 0, unexplained = 0;
    long identSum = 0, dkSum = 0, slotSum = 0;
    var rows = new List<string>();
    int ci = applied0;
    for (int k = 1; k < dumps.Count; k++)
    {
        Dump prev = dumps[k - 1], d = dumps[k];
        int from = ci;
        string best = ""; int bestIdent = -1, bestSlot = 0, bestOurs = 0, bestGeom = 0; long bestLimit = 0; List<(long Tick, int X, int Y, byte Byte)> bestApplied = [];
        foreach (long limit in new[] { d.Tick - 1, d.Tick, d.Tick + 2 })
        {
            var b = (byte[])bytes.Clone();
            var grid = new Grid(W, H, b);
            NavMesh mesh = Import(prev, 0);
            var applied = new List<(long Tick, int X, int Y, byte Byte)>();
            int j = from;
            while (j < changes.Count && changes[j].Tick <= limit)
            {
                var c = changes[j++];
                b[(c.Y * W) + c.X] = c.Byte;
                applied.Add(c);
                mesh.Retriangulate(grid, c.X, c.Y, c.X + 1, c.Y + 1);
            }
            Compare(mesh, d, out int ident, out int slotSame, out int ours, out _, out _, out _);
            int geom = GeometryMatch(mesh, d);
            if (ident > bestIdent) { bestIdent = ident; bestSlot = slotSame; bestOurs = ours; bestGeom = geom; bestLimit = limit; bestApplied = applied; }
        }
        // Advance the byte state through this dump's tick for the next interval.
        while (ci < changes.Count && changes[ci].Tick <= bestLimit) { var c = changes[ci++]; bytes[(c.Y * W) + c.X] = c.Byte; }

        bool same = bestIdent == d.Tris.Count && bestOurs == d.Tris.Count;
        bool geomSame = bestGeom == d.Tris.Count && bestOurs == d.Tris.Count;
        identSum += bestIdent; dkSum += d.Tris.Count; slotSum += bestSlot;
        string note = "";
        if (same) exact++;
        else if (geomSame)
        {
            geometryOnly++;
            // Rebuild our mesh for this interval and check each byte difference against the log.
            var b = (byte[])bytes0.Clone();
            for (int i = 0; i < from; i++) b[(changes[i].Y * W) + changes[i].X] = changes[i].Byte;
            var grid = new Grid(W, H, b);
            NavMesh mesh = Import(prev, 0);
            foreach (var c in bestApplied) { b[(c.Y * W) + c.X] = c.Byte; mesh.Retriangulate(grid, c.X, c.Y, c.X + 1, c.Y + 1); }
            var ourByte = new Dictionary<string, byte>();
            for (int t = 0; t < mesh.TriangleSlots; t++) if (mesh.IsTriangleLive(t)) { string kk = Key(mesh, t); ourByte[kk.Substring(kk.IndexOf(':') + 1)] = mesh.NavByte(t); }
            int lateLogged = 0, other = 0; var detail = new List<string>();
            foreach (Tri tri in d.Tris)
            {
                string kk = d.Key(tri); string g = kk.Substring(kk.IndexOf(':') + 1);
                if (!ourByte.TryGetValue(g, out byte ob) || ob == tri.Nav) continue;
                // The tile under the triangle's centroid, and the next logged byte for it after the dump's tick.
                var pts = tri.V.Select(v => d.Pts[v]).ToArray();
                int cx = (int)Math.Floor((pts[0].Item1 + pts[1].Item1 + pts[2].Item1) / 3.0), cy = (int)Math.Floor((pts[0].Item2 + pts[1].Item2 + pts[2].Item2) / 3.0);
                var later = changes.FirstOrDefault(c => c.X == cx && c.Y == cy && c.Tick > bestLimit && c.Tick <= d.Tick + 8);
                var earlier = changes.LastOrDefault(c => c.X == cx && c.Y == cy && c.Tick <= bestLimit);
                if (later.Tick != 0 && later.Byte == tri.Nav) lateLogged++;
                else if (earlier.Tick != 0 && earlier.Byte == tri.Nav && ob != tri.Nav) { other++; detail.Add($"({cx},{cy}) dk2 {tri.Nav:X2} ours {ob:X2} log {earlier.Byte:X2}@{earlier.Tick}"); }
                else { other++; detail.Add($"({cx},{cy}) dk2 {tri.Nav:X2} ours {ob:X2} no log"); }
            }
            note = $"  BYTE-ONLY: {lateLogged} logged later, {other} other" + (detail.Count > 0 ? " [" + string.Join("; ", detail) + "]" : "");
        }
        else
        {
            // Try other starts for the locate cache: every live triangle of the previous dump
            // within four tiles of a changed tile, seeded into all sixteen cells.
            var seeds = prev.Tris.Where(t => t.V.Any(v => bestApplied.Any(c => Math.Abs(prev.Pts[v].Item1 - c.X) <= 4 && Math.Abs(prev.Pts[v].Item2 - c.Y) <= 4))).Select(t => t.Slot).ToList();
            int hits = 0; int firstHit = -1;
            foreach (int seed in seeds)
            {
                var b = (byte[])bytes0.Clone();
                // Rebuild the byte state before this interval.
                for (int i = 0; i < from; i++) b[(changes[i].Y * W) + changes[i].X] = changes[i].Byte;
                var grid = new Grid(W, H, b);
                NavMesh mesh = Import(prev, seed);
                foreach (var c in bestApplied) { b[(c.Y * W) + c.X] = c.Byte; mesh.Retriangulate(grid, c.X, c.Y, c.X + 1, c.Y + 1); }
                Compare(mesh, d, out int ident, out _, out int ours, out _, out _, out _);
                int geom = GeometryMatch(mesh, d);
                if ((ident == d.Tris.Count || geom == d.Tris.Count) && ours == d.Tris.Count) { hits++; if (firstHit < 0) firstHit = seed; }
            }
            if (hits > 0) { seeded++; note = $"  SEEDED: {hits}/{seeds.Count} nearby starts reproduce it (e.g. slot {firstHit})"; }
            else
            {
                // Seed before one change at a time, the others from the default: the cache the
                // original held before a later change was left by route queries ours cannot see.
                int hits2 = 0; string where = "";
                for (int j = 1; j < bestApplied.Count && hits2 == 0; j++)
                {
                    foreach (int seed in seeds)
                    {
                        var b = (byte[])bytes0.Clone();
                        for (int i = 0; i < from; i++) b[(changes[i].Y * W) + changes[i].X] = changes[i].Byte;
                        var grid = new Grid(W, H, b);
                        NavMesh mesh = Import(prev, 0);
                        for (int i = 0; i < bestApplied.Count; i++)
                        {
                            var c = bestApplied[i];
                            if (i == j) mesh = Reseed(mesh, seed);
                            b[(c.Y * W) + c.X] = c.Byte;
                            mesh.Retriangulate(grid, c.X, c.Y, c.X + 1, c.Y + 1);
                        }
                        Compare(mesh, d, out int ident, out _, out int ours, out _, out _, out _);
                        int geom = GeometryMatch(mesh, d);
                        if ((ident == d.Tris.Count || geom == d.Tris.Count) && ours == d.Tris.Count) { hits2++; where = $"before change {j + 1} ({bestApplied[j].X},{bestApplied[j].Y}) with slot {seed}"; break; }
                    }
                }
                if (hits2 > 0) { seeded++; note = $"  SEEDED {where}"; }
                else if (TryOrders(prev, d, bestApplied, from, out string order)) { seeded++; note = $"  ORDER: {order}"; }
                else
                {
                    unexplained++; note = $"  UNEXPLAINED ({seeds.Count} starts tried, before each change)";
                    var b = (byte[])bytes0.Clone();
                    for (int i = 0; i < from; i++) b[(changes[i].Y * W) + changes[i].X] = changes[i].Byte;
                    var grid = new Grid(W, H, b);
                    NavMesh mesh = Import(prev, 0);
                    foreach (var c in bestApplied) { b[(c.Y * W) + c.X] = c.Byte; mesh.Retriangulate(grid, c.X, c.Y, c.X + 1, c.Y + 1); }
                    Console.WriteLine($"  step {k,3}:");
                    Diagnose(mesh, d, prev, bestApplied);
                }
            }
        }
        string row = $"  step {k,3} tick {prev.Tick,5}->{d.Tick,5}: {bestApplied.Count} changes (through {bestLimit}), dk2 {d.Tris.Count} ours {bestOurs} identical {bestIdent} geometry {bestGeom} sameSlot {bestSlot}{note}";
        rows.Add(row);
        if (!same) Console.WriteLine(row);
    }
    Console.WriteLine($"step: {exact}/{dumps.Count - 1} intervals exact, {geometryOnly} byte-only (sampling), {seeded} exact from another cached start, {unexplained} unexplained; triangles identical {identSum}/{dkSum} ({identSum / (double)dkSum:P2}), same slot {slotSum}/{dkSum}");
    File.WriteAllLines(Path.Combine(dir, "replay-step.txt"), rows);
}


bool TryOrders(Dump prev, Dump d, List<(long Tick, int X, int Y, byte Byte)> applied, int from, out string order)
{
    order = "";
    var groups = applied.GroupBy(c => c.Tick).Select(g => g.ToList()).ToList();
    if (groups.All(g => g.Count == 1)) return false;
    // Every combination of per-tick permutations, capped.
    var perTick = groups.Select(g => Permutations(g).ToList()).ToList();
    long total = 1; foreach (var p in perTick) { total *= p.Count; if (total > 5000) return false; }
    var idx = new int[perTick.Count];
    while (true)
    {
        var seq = new List<(long Tick, int X, int Y, byte Byte)>();
        for (int g = 0; g < perTick.Count; g++) seq.AddRange(perTick[g][idx[g]]);
        var b = (byte[])bytes0.Clone();
        for (int i = 0; i < from; i++) b[(changes[i].Y * W) + changes[i].X] = changes[i].Byte;
        var grid = new Grid(W, H, b);
        NavMesh mesh = Import(prev, 0);
        foreach (var c in seq) { b[(c.Y * W) + c.X] = c.Byte; mesh.Retriangulate(grid, c.X, c.Y, c.X + 1, c.Y + 1); }
        Compare(mesh, d, out int ident, out _, out int ours, out _, out _, out _);
        int geom = GeometryMatch(mesh, d);
        if ((ident == d.Tris.Count || geom == d.Tris.Count) && ours == d.Tris.Count)
        {
            order = string.Join(" ", seq.Select(c => $"t{c.Tick}({c.X},{c.Y})"));
            return true;
        }
        int k = 0;
        while (k < idx.Length && ++idx[k] == perTick[k].Count) { idx[k] = 0; k++; }
        if (k == idx.Length) return false;
    }
}

static IEnumerable<List<T>> Permutations<T>(List<T> items)
{
    if (items.Count <= 1) { yield return items; yield break; }
    for (int i = 0; i < items.Count; i++)
    {
        var rest = items.Where((_, j) => j != i).ToList();
        foreach (var p in Permutations(rest)) yield return new List<T> { items[i] }.Concat(p).ToList();
    }
}

int GeometryMatch(NavMesh mesh, Dump d)
{
    var ours = new HashSet<string>();
    for (int t = 0; t < mesh.TriangleSlots; t++) if (mesh.IsTriangleLive(t)) ours.Add(Key(mesh, t).Substring(Key(mesh, t).IndexOf(':') + 1));
    int n = 0;
    foreach (Tri tri in d.Tris) { string k = d.Key(tri); if (ours.Contains(k.Substring(k.IndexOf(':') + 1))) n++; }
    return n;
}

void Diagnose(NavMesh mesh, Dump d, Dump prev, IEnumerable<(long Tick, int X, int Y, byte Byte)> applied)
{
    Console.WriteLine("    changes: " + string.Join(" ", applied.Select(c => $"t{c.Tick}({c.X},{c.Y})={c.Byte:X2}")));
    var ours = new Dictionary<string, int>();
    for (int t = 0; t < mesh.TriangleSlots; t++) if (mesh.IsTriangleLive(t)) ours[Key(mesh, t)] = t;
    var dk = new Dictionary<string, Tri>();
    foreach (Tri t in d.Tris) dk[d.Key(t)] = t;
    var prevKeys = new HashSet<string>(prev.Tris.Select(prev.Key));
    var onlyDk = dk.Keys.Where(k => !ours.ContainsKey(k)).ToList();
    var onlyOurs = ours.Keys.Where(k => !dk.ContainsKey(k)).ToList();
    Console.WriteLine($"    only in DK2 ({onlyDk.Count}): " + string.Join(" | ", onlyDk.Take(8).Select(k => $"{k} slot {dk[k].Slot}{(prevKeys.Contains(k) ? " (unchanged)" : " (new)")}")));
    Console.WriteLine($"    only in ours ({onlyOurs.Count}): " + string.Join(" | ", onlyOurs.Take(8).Select(k => $"{k} slot {ours[k]}{(prevKeys.Contains(k) ? " (kept)" : " (new)")}")));
    // Slot-level differences among identical triangles.
    int slotDiff = 0; var examples = new List<string>();
    foreach (var (k, t) in ours)
        if (dk.TryGetValue(k, out Tri? tri) && tri.Slot != t) { slotDiff++; if (examples.Count < 6) examples.Add($"{k}: dk2 {tri.Slot} ours {t}"); }
    Console.WriteLine($"    same triangle, other slot: {slotDiff}: " + string.Join(" | ", examples));
}

void Compare(NavMesh mesh, Dump d, out int ident, out int slotSame, out int oursCount, out int ptCommon, out int dkPtCount, out int ourPtCount)
{
    var ours = new Dictionary<string, int>();
    for (int t = 0; t < mesh.TriangleSlots; t++)
    {
        if (!mesh.IsTriangleLive(t)) continue;
        ours[Key(mesh, t)] = t;
    }
    ident = 0; slotSame = 0;
    foreach (Tri tri in d.Tris)
    {
        if (ours.TryGetValue(d.Key(tri), out int t))
        {
            ident++;
            if (t == tri.Slot) slotSame++;
        }
    }
    oursCount = ours.Count;
    var ourPts = new HashSet<(int, int)>();
    for (int p = 0; p < mesh.PointSlots; p++) if (mesh.IsPointLive(p)) ourPts.Add((mesh.PointX(p), mesh.PointY(p)));
    var dkPts = new HashSet<(int, int)>();
    foreach (Tri tri in d.Tris) foreach (int v in tri.V) dkPts.Add(d.Pts[v]);
    ptCommon = dkPts.Count(p => ourPts.Contains(p));
    dkPtCount = dkPts.Count; ourPtCount = ourPts.Count;
}

NavMesh Import(Dump d, int seed = 0)
{
    // Our storage is DK2's with the two corners after a given one swapped (NavMesh.Edit.cs): DK2
    // (v0, v1, v2) is ours (v0, v2, v1); DK2's edges 0, 1, 2 are ours 2, 1, 0, and so its neighbour
    // words and constraint bits (+0xD bits 3..5).
    using var ms = new MemoryStream();
    var w = new BinaryWriter(ms);
    int minX = d.Pts.Take(4).Min(p => p.Item1), minY = d.Pts.Take(4).Min(p => p.Item2);
    int maxX = d.Pts.Take(4).Max(p => p.Item1), maxY = d.Pts.Take(4).Max(p => p.Item2);
    w.Write(minX); w.Write(minY); w.Write(maxX); w.Write(maxY);
    int np = d.Pts.Count;
    var ptTri = new int[np];
    Array.Fill(ptTri, -1);
    foreach (Tri t in d.Tris) foreach (int v in t.V) ptTri[v] = t.Slot;
    w.Write(np);
    for (int p = 0; p < np; p++)
    {
        w.Write(d.Pts[p].Item1); w.Write(d.Pts[p].Item2); w.Write(ptTri[p] < 0); w.Write(Math.Max(0, ptTri[p]));
    }
    var deadPts = Enumerable.Range(0, np).Where(p => ptTri[p] < 0).OrderByDescending(p => p).ToList();
    w.Write(deadPts.Count); foreach (int p in deadPts) w.Write(p);
    var bySlot = d.Tris.ToDictionary(t => t.Slot);
    w.Write(d.Slots);
    for (int t = 0; t < d.Slots; t++)
    {
        if (bySlot.TryGetValue(t, out Tri? tri))
        {
            w.Write(tri.V[0]); w.Write(tri.V[2]); w.Write(tri.V[1]);
            w.Write(tri.N[2]); w.Write(tri.N[1]); w.Write(tri.N[0]);
            w.Write(tri.Nav);
            int bits = 0;
            for (int k = 0; k < 3; k++) if ((tri.Bits & (1 << (3 + (2 - k)))) != 0) bits |= 1 << k;
            w.Write((byte)bits);
        }
        else
        {
            w.Write(0); w.Write(0); w.Write(0); w.Write(-1); w.Write(-1); w.Write(-1); w.Write(NavBytes.Dead); w.Write((byte)0);
        }
    }
    var deadTris = Enumerable.Range(0, d.Slots).Where(t => !bySlot.ContainsKey(t)).OrderByDescending(t => t).ToList();
    w.Write(deadTris.Count); foreach (int t in deadTris) w.Write(t);
    for (int i = 0; i < 16; i++) w.Write(seed); // the locate cache: unknown at the dump; the reset value, or a trial start
    ms.Position = 0;
    return NavMesh.Read(new BinaryReader(ms));
}

static string Key(NavMesh m, int t)
{
    var c = new (int, int)[3];
    for (int i = 0; i < 3; i++) { int p = m.Vertex(t, i); c[i] = (m.PointX(p), m.PointY(p)); }
    Array.Sort(c);
    return $"{m.NavByte(t)}:{c[0]};{c[1]};{c[2]}";
}

static NavMesh Reseed(NavMesh m, int seed)
{
    // The locate cache is the last sixteen ints of the save: rewrite them and read the mesh back.
    using var ms = new MemoryStream();
    m.Write(new BinaryWriter(ms));
    byte[] data = ms.ToArray();
    for (int i = 0; i < 16; i++) BitConverter.GetBytes(seed).CopyTo(data, data.Length - 64 + (4 * i));
    return NavMesh.Read(new BinaryReader(new MemoryStream(data)));
}

sealed record Tri(int Slot, int[] V, int[] N, byte Nav, byte Bits);

sealed class Dump
{
    public long Tick;
    public int Slots;
    public List<(int, int)> Pts = [];
    public List<Tri> Tris = [];

    public static Dump Load(string path)
    {
        var d = new Dump();
        foreach (string line in File.ReadLines(path))
        {
            string[] p = line.Split(' ');
            if (p[0] == "wall") { d.Tick = long.Parse(p[3]); d.Slots = int.Parse(p[5]); }
            else if (p[0] == "P") d.Pts.Add((int.Parse(p[2]), int.Parse(p[3])));
            else if (p[0] == "T")
                d.Tris.Add(new Tri(int.Parse(p[1]), [int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4])],
                    [int.Parse(p[5]), int.Parse(p[6]), int.Parse(p[7])],
                    byte.Parse(p[8], NumberStyles.HexNumber), byte.Parse(p[9], NumberStyles.HexNumber)));
        }
        return d;
    }

    public string Key(Tri t)
    {
        var c = new (int, int)[3];
        for (int i = 0; i < 3; i++) c[i] = Pts[t.V[i]];
        Array.Sort(c);
        return $"{t.Nav}:{c[0]};{c[1]};{c[2]}";
    }
}

sealed class Grid(int width, int height, byte[] bytes) : INavByteGrid
{
    public int Width => width;
    public int Height => height;
    public byte NavByte(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? NavBytes.Solid : bytes[(y * width) + x];
}
