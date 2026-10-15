// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DK2.Data.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DK2.Unity.Editor
{
    public static class ClassicTagMaterialProbe
    {
        [Serializable] private sealed class Result
        {
            public bool Passed;
            public int Cases;
            public string Error;
        }

        [MenuItem("DK2/Classic look/Verify tag replaces terrain instance colour")]
        public static void Run()
        {
            var result = new Result();
            try { Verify(result); result.Passed = true; }
            catch (Exception error) { result.Error = error.ToString(); }
            string output = Environment.GetEnvironmentVariable("DK2_TAG_PROBE_OUTPUT")
                ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/step5-tag-materials/result.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Debug.Log($"DK2 tag material probe: {(result.Passed ? "PASS" : "FAIL")}; {result.Cases} cases; {output}");
            if (Application.isBatchMode) EditorApplication.Exit(result.Passed ? 0 : 1);
        }

        private static void Verify(Result result)
        {
            Shader shader = Resources.Load<Shader>("Shaders/ClassicVertexLit");
            if (shader == null) throw new InvalidOperationException("Classic vertex lighting shader is missing.");
            var root = new GameObject("Transient tag material probe") { hideFlags = HideFlags.HideAndDontSave };
            var owned = new HashSet<Material>();
            try
            {
                var classic = new ClassicMeshes(null, classicVertexLighting: true);
                var source = new Material(shader);
                owned.Add(source);
                source.mainTexture = Texture2D.whiteTexture;
                source.EnableKeyword("_EMISSION");
                var selfIllumination = new Color(.125f, .25f, .375f, 0);
                source.SetColor("_EmissionColor", selfIllumination);
                var piece = new ClassicMeshes.PieceSource(
                    new[] { Vector3.zero, Vector3.right, Vector3.forward },
                    new[] { Vector3.up, Vector3.up, Vector3.up },
                    new[] { Vector2.zero, Vector2.right, Vector2.up },
                    new[] { new[] { 0, 1, 2 } }, new[] { source });
                foreach (TerrainLightColour tint in new[] { new TerrainLightColour(40, 60, 80), new TerrainLightColour(90, 10, 20) })
                    foreach (bool tagged in new[] { false, true })
                    {
                        var chunks = new TerrainChunks(root.transform, classic, 1, 1);
                        // Exercise the emitted triangle/material boundary, including the room-wall tint argument.
                        typeof(TerrainChunks).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
                            .Invoke(chunks, new object[] { piece, 0, Vector3.zero, 0, 0, tagged, tint });
                        var emitted = (Dictionary<Material, List<int>>)typeof(TerrainChunks)
                            .GetField("_tileByMaterial", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(chunks);
                        foreach (Material material in emitted.Keys) owned.Add(material);
                        if (emitted.Count != (tagged ? 2 : 1))
                            throw new InvalidOperationException("Tag changed the base/overlay material pass count.");
                        Material overlay = tagged ? classic.TagOverlay() : null;
                        foreach (KeyValuePair<Material, List<int>> entry in emitted)
                        {
                            if (entry.Value.Count != 3 || entry.Value[0] != 0 || entry.Value[1] != 1 || entry.Value[2] != 2)
                                throw new InvalidOperationException("Tag changed the piece's emitted triangles.");
                            if (entry.Key == overlay)
                            {
                                if (overlay.GetFloat("_SrcBlend") != (float)BlendMode.One
                                    || overlay.GetFloat("_DstBlend") != (float)BlendMode.One || overlay.GetFloat("_ZWrite") != 0)
                                    throw new InvalidOperationException("Tag overlay lost its additive blend or disabled depth writes.");
                                continue;
                            }
                            Color instance = tagged ? new Color(128f / 255f, 128f / 255f, 200f / 255f, 0)
                                : new Color(tint.Red / 255f, tint.Green / 255f, tint.Blue / 255f, 0);
                            Color expected = selfIllumination + instance;
                            Color actual = entry.Key.GetColor("_EmissionColor");
                            if (Mathf.Abs(expected.r - actual.r) > .0001f || Mathf.Abs(expected.g - actual.g) > .0001f
                                || Mathf.Abs(expected.b - actual.b) > .0001f)
                                throw new InvalidOperationException($"Tagged={tagged}, tint={tint}: expected self illumination + instance {expected}, got {actual}.");
                            if (entry.Key.mainTexture != source.mainTexture || !entry.Key.IsKeywordEnabled("_EMISSION"))
                                throw new InvalidOperationException("Piece lost its texture or self illumination.");
                        }
                        result.Cases++;
                    }
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (Material material in owned) Object.DestroyImmediate(material);
            }
        }
    }
}
