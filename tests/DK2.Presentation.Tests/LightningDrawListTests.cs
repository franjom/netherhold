// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class LightningDrawListTests
{
    [Fact]
    public void CrackleMatchesTheOldUnityCameraSpaceAlgorithmAcrossSeedsAndFrames()
    {
        LightningControlPoint[] controls = [new LightningControlPoint(1, new Vector3(1, 2, 3)), new LightningControlPoint(8, new Vector3(3, 1, 2)),
            new LightningControlPoint(12, new Vector3(-1, 2, 1)), new LightningControlPoint(2001, new Vector3(9, 9, 9)), new LightningControlPoint(17, new Vector3(2, 3, 4))];
        for (uint seed = 1; seed <= 64; seed++)
        {
            var draws = new LightningDrawList(Effects(), seed);
            var bolts = new LightningBolts(seed);
            var pairs = new LightningCracklePairs(new Dk2Lcg(seed));
            Matrix4x4 camera = Matrix4x4.CreateRotationX(.7f) * Matrix4x4.CreateRotationY(.2f)
                * Matrix4x4.CreateTranslation(4, 5, -7);
            Matrix4x4.Invert(camera, out Matrix4x4 view);
            for (int frame = 0; frame < 8; frame++)
            {
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();
                var indices = new List<int>();
                foreach (LightningSegment segment in pairs.Choose(controls))
                {
                    Vector3 a = Transform(Render(segment.Start), view), b = Transform(Render(segment.End), view);
                    foreach (LightningPath path in bolts.Build(a, b, LightningAmplitude.Creature))
                    {
                        var ribbon = LightningRibbon.Build(path.Nodes);
                        int offset = vertices.Count;
                        foreach (Vector3 vertex in ribbon.Vertices)
                        {
                            vertices.Add(Transform(vertex, camera));
                        }

                        uvs.AddRange(ribbon.Uvs);
                        foreach (int index in ribbon.Triangles)
                        {
                            indices.Add(offset + index);
                        }
                    }
                }
                draws.BeginFrame(view, camera);
                draws.DrawCrackle(controls);
                AssertBits(vertices, draws.Vertices);
                Assert.Equal(uvs, draws.Uvs);
                Assert.Equal(indices, draws.Triangles);
                draws.BeginFrame(view, camera);
                Assert.Empty(draws.Vertices);
                Assert.Empty(draws.Uvs);
                Assert.Empty(draws.Triangles);
            }
        }
    }

    [Fact]
    public void ClipRevisionIsObservedEvenBeforeTheCreatureIsElectrified()
    {
        RoomConstructionParticles effects = Effects();
        var draws = new LightningDrawList(effects, 1);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        Assert.False(draws.NeedsCreatureCrackle(new CreatureId(5), new GameTick(10), 2));
        effects.Emit(new SimEvent(new GameTick(10), "status", "lightning", "") { ElectrifiedCreature = new CreatureId(5) });
        Assert.False(draws.NeedsCreatureCrackle(new CreatureId(5), new GameTick(11), 2));
        Assert.True(draws.NeedsCreatureCrackle(new CreatureId(5), new GameTick(11), 3));
        Assert.True(draws.NeedsCreatureCrackle(new CreatureId(5), new GameTick(11), 3));
        Assert.False(draws.NeedsCreatureCrackle(new CreatureId(5), new GameTick(12), 3));
    }

    private static RoomConstructionParticles Effects()
    {
        return new RoomConstructionParticles(new GameDatabase([], [], [], new GlobalVariables()), 1, () => default);
    }

    private static Vector3 Render(Vector3 p)
    {
        return new Vector3(p.X, p.Z, -p.Y);
    }

    private static Vector3 Transform(Vector3 p, Matrix4x4 m)
    {
        return new Vector3(
            m.M11 * p.X + m.M21 * p.Y + m.M31 * p.Z + m.M41,
            m.M12 * p.X + m.M22 * p.Y + m.M32 * p.Z + m.M42,
            m.M13 * p.X + m.M23 * p.Y + m.M33 * p.Z + m.M43);
    }

    private static void AssertBits(IReadOnlyList<Vector3> expected, IReadOnlyList<Vector3> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(expected[i].X), BitConverter.SingleToInt32Bits(actual[i].X));
            Assert.Equal(BitConverter.SingleToInt32Bits(expected[i].Y), BitConverter.SingleToInt32Bits(actual[i].Y));
            Assert.Equal(BitConverter.SingleToInt32Bits(expected[i].Z), BitConverter.SingleToInt32Bits(actual[i].Z));
        }
    }
}
