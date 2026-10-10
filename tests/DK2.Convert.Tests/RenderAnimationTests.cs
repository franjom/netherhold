// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Data;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class RenderAnimationTests
{
    [InstallTheory]
    [InlineData("Imp")]
    [InlineData("Giant")]
    [InlineData("Salamander")]
    public void ShippedWalkClipsKeepTheirOwnUvsAndProduceAnIntermediatePose(string creature)
    {
        using InstallArtProvider provider = InstallArtProvider.Open(Dk2InstallGate.Path,
            GameDatabaseLoader.Load(Dk2InstallGate.Path));
        Assert.True(provider.TryResolve(AssetRef.ForCreature(creature, AssetKind.Animation, "moving_to_job"), out var asset));
        Assert.True(provider.TryLoadModel(asset, out var model));
        var clip = new RenderAnimation(model);
        Assert.True(clip.FrameCount > 1);
        var positions = new Vector3[clip.Mesh.Positions.Count];
        float time = .5f / (clip.FrameCount - 1);
        clip.Sample(time, false, positions);
        RenderMesh next = RenderMesh.FromModel(model, 1);
        bool moves = false;
        for (int i = 0; i < positions.Length; i++)
        {
            Assert.InRange(Vector3.Distance(Vector3.Lerp(clip.Mesh.Positions[i], next.Positions[i], .5f), positions[i]), 0f, 1e-6f);
            Assert.Equal(clip.Mesh.Uvs[i], next.Uvs[i]);
            moves |= positions[i] != clip.Mesh.Positions[i];
        }
        Assert.True(moves);
    }

    [Theory]
    [InlineData(-1f, false, 0f)]
    [InlineData(0f, false, 0f)]
    [InlineData(.25f, false, 2f)]
    [InlineData(.75f, false, 6f)]
    [InlineData(1f, false, 8f)]
    [InlineData(2f, false, 8f)]
    [InlineData(1f, true, 8f)]
    [InlineData(2f, true, 8f)]
    [InlineData(2.25f, true, 2f)]
    public void FractionalTimeInterpolatesAndKeepsTheInclusiveLastFrame(float time, bool loop, float x)
    {
        var clip = new RenderAnimation(Model());
        var positions = new Vector3[clip.Mesh.Positions.Count];
        clip.Sample(time, loop, positions);
        Assert.Equal(new Vector3(x, 3, -2), positions[0]);
    }

    [Fact]
    public void SamplingTwoInstancesDoesNotMutateTheSharedClip()
    {
        var clip = new RenderAnimation(Model());
        var a = new Vector3[1];
        var b = new Vector3[1];
        clip.Sample(.25f, false, a);
        clip.Sample(.75f, false, b);
        Assert.Equal(2f, a[0].X);
        Assert.Equal(6f, b[0].X);
        Assert.Equal(0f, clip.Mesh.Positions[0].X);
    }

    [Fact]
    public void InvalidTimeAndDestinationAreRejected()
    {
        var clip = new RenderAnimation(Model());
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Sample(float.NaN, false, new Vector3[1]));
        Assert.Throws<ArgumentException>(() => clip.Sample(0f, false, []));
    }

    private static KmfModel Model()
    {
        KmfMesh[] frames = Enumerable.Range(0, 3).Select(i => new KmfMesh("clip", Vector3.Zero, 1f,
            [new KmfPrimitive(0, 1f, [new KmfVertex(new Vector3(i * 4, 2, -3), Vector3.UnitZ, Vector2.Zero)],
                [Array.Empty<byte>()])])).ToArray();
        return new KmfModel(1, KmfKind.Animation, [], frames[0], new KmfAnimation(3, frames));
    }
}
