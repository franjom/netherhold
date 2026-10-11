// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Numerics;
using DK2.Art;
using DK2.Assets;
using DK2.Data;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run -c Release --project tools/classic-animation-probe -- <DK2 install>");
    return 2;
}
var db = GameDatabaseLoader.Load(args[0]);
using InstallArtProvider provider = InstallArtProvider.Open(args[0], db);
foreach (string name in new[] { "Imp", "Giant", "Salamander", "Mistress" })
{
    AssetRef key = AssetRef.ForCreature(name, AssetKind.Animation, "moving_to_job");
    if (!provider.TryResolve(key, out ResolvedAsset asset)
        || !provider.TryLoadModel(asset, out var model) || model.Animation is not { IsComplete: true }) continue;
    var clip = new RenderAnimation(model);
    provider.TryGetResource(asset, out Dk2ArtResource resource);
    Console.WriteLine($"{name}: authored movement scale {resource.MovementScale}/4096");
    const int crowd = 200, iterations = 300;
    Vector3[][] instances = Enumerable.Range(0, crowd).Select(_ => new Vector3[clip.Mesh.Positions.Count]).ToArray();
    for (int t = 0; t < 100; t++)
        foreach (Vector3[] positions in instances) clip.Sample(t / 59f, true, positions);
    var timer = new Stopwatch();
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    timer.Start();
    for (int t = 0; t < iterations; t++)
        for (int c = 0; c < crowd; c++) clip.Sample((t + c * .1f) / 59f, true, instances[c]);
    timer.Stop();
    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    Console.WriteLine($"{name}: {clip.FrameCount} frames, {clip.Mesh.Positions.Count} vertices; added sample storage {clip.PositionBytes} bytes; crowd {crowd}, sampler only {timer.Elapsed.TotalMilliseconds / iterations:F3} ms/render; allocations {allocated} bytes/{iterations} renders");
}
return 0;
