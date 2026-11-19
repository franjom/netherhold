// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using UnityEngine;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace DK2.Unity.Editor
{
    public static class ClassicLightningProbe
    {
        public static void Verify()
        {
            var root = new GameObject("Lightning parity probe");
            try
            {
                var controls = new[] { new LightningControlPoint(1, new NVector(1, 2, 3)),
                    new LightningControlPoint(8, new NVector(3, 1, 2)),
                    new LightningControlPoint(12, new NVector(-1, 2, 1)),
                    new LightningControlPoint(17, new NVector(2, 3, 4)) };
                int compared = 0;
                for (int pose = 0; pose < 3; pose++)
                {
                    root.transform.SetPositionAndRotation(new Vector3(4.25f, 5.5f, -7.75f),
                        Quaternion.Euler(pose * 30, pose * 17, pose * 7));
                    for (uint seed = 1; seed <= 64; seed++)
                    {
                        var effects = new RoomConstructionParticles(new GameDatabase(new TerrainDef[0],
                            new RoomDef[0], new CreatureDef[0], new GlobalVariables()), 1, () => default);
                        var draws = new LightningDrawList(effects, seed, new NativeCamera(root.transform));
                        var bolts = new LightningBolts(seed);
                        var pairs = new LightningCracklePairs(new Dk2Lcg(seed));
                        for (int frame = 0; frame < 8; frame++)
                        {
                            var expected = new List<Vector3>();
                            foreach (LightningSegment segment in pairs.Choose(controls))
                            {
                                Vector3 a = root.transform.InverseTransformPoint(Render(segment.Start));
                                Vector3 b = root.transform.InverseTransformPoint(Render(segment.End));
                                foreach (LightningPath path in bolts.Build(new NVector(a.x, a.y, a.z),
                                    new NVector(b.x, b.y, b.z), LightningAmplitude.Creature))
                                    foreach (NVector vertex in LightningRibbon.Build(path.Nodes).Vertices)
                                        expected.Add(root.transform.TransformPoint(new Vector3(vertex.X, vertex.Y, vertex.Z)));
                            }
                            draws.BeginFrame(Matrix(root.transform.worldToLocalMatrix), Matrix(root.transform.localToWorldMatrix));
                            draws.DrawCrackle(controls);
                            if (expected.Count != draws.Vertices.Count) throw new InvalidOperationException("Lightning vertex count changed.");
                            for (int i = 0; i < expected.Count; i++)
                            {
                                NVector actual = draws.Vertices[i]; Vector3 old = expected[i];
                                if (Bits(old.x) != Bits(actual.X) || Bits(old.y) != Bits(actual.Y) || Bits(old.z) != Bits(actual.Z))
                                    throw new InvalidOperationException("Lightning bits differ: pose " + pose + ", seed " + seed
                                        + ", frame " + frame + ", vertex " + i + ": " + old.ToString("R") + " / " + actual);
                                compared++;
                            }
                        }
                    }
                }
                Debug.Log("Lightning parity: " + compared + " native Transform vertices bit-identical; texture-only colour unchanged.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
        private sealed class NativeCamera : ILightningCameraTransform
        {
            private readonly Transform _camera;
            public NativeCamera(Transform camera) { _camera = camera; }
            public NVector ToCamera(NVector p)
            {
                Vector3 v = _camera.InverseTransformPoint(new Vector3(p.X, p.Y, p.Z));
                return new NVector(v.x, v.y, v.z);
            }
            public NVector ToWorld(NVector p)
            {
                Vector3 v = _camera.TransformPoint(new Vector3(p.X, p.Y, p.Z));
                return new NVector(v.x, v.y, v.z);
            }
        }
        private static Vector3 Render(NVector p) => new Vector3(p.X, p.Z, -p.Y);
        private static NMatrix Matrix(Matrix4x4 m) => new NMatrix(
            m.m00, m.m10, m.m20, m.m30, m.m01, m.m11, m.m21, m.m31,
            m.m02, m.m12, m.m22, m.m32, m.m03, m.m13, m.m23, m.m33);
    }
}
