// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Core;
using DK2.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using NumericsVector3 = System.Numerics.Vector3;

namespace DK2.Unity.Editor
{
    public static class ClassicLightingProbe
    {
        [Serializable]
        private sealed class Result
        {
            public bool Passed;
            public int Cases;
            public float MaximumError;
            public string Error;
        }

        [MenuItem("DK2/Classic look/Verify native lighting on GPU")]
        public static void Run()
        {
            var result = new Result();
            try
            {
                Verify(result);
                result.Passed = true;
            }
            catch (Exception error) { result.Error = error.ToString(); }
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/step5-lighting-probe.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Debug.Log($"DK2 lighting probe: {(result.Passed ? "PASS" : "FAIL")}; {result.Cases} cases; {output}");
            if (Application.isBatchMode) EditorApplication.Exit(result.Passed ? 0 : 1);
        }

        private static void Verify(Result result)
        {
            Shader kernel = Resources.Load<Shader>("Shaders/ClassicLightingProbe");
            Shader lit = Resources.Load<Shader>("Shaders/ClassicVertexLit");
            if (kernel == null || lit == null) throw new InvalidOperationException("Classic lighting shaders are missing.");
            if (!kernel.isSupported || !lit.isSupported) throw new InvalidOperationException("Classic lighting shaders are unsupported.");
            var material = new Material(kernel);
            var litMaterial = new Material(lit);
            var target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                target.Create();
                foreach (float distance in new[] { 0.1f, 0.5f, 1f, 1.02f, 2f, 3f })
                    PointCase(result, material, target, readback, distance, -NumericsVector3.UnitX, 128);
                PointCase(result, material, target, readback, 1f, NumericsVector3.UnitX, 128);
                PointCase(result, material, target, readback, 1f, -NumericsVector3.UnitX, 64);
                PointCase(result, material, target, readback, 1f, -NumericsVector3.UnitX, 255);
                material.SetFloat("_ProbeMode", 1);
                material.SetVector("_ProbeVertex", new Vector4(2f, 0.5f, 0.25f, 0));
                Check(result, Draw(material, target, readback), new Color(1f, 127f / 255f, 63f / 255f, 1f));
                material.SetFloat("_ProbeMode", 2);
                Check(result, Draw(material, target, readback), new Color(0.25f, 63.5f / 255f, 63f / 255f, 1f));

                MethodInfo compile = typeof(ShaderUtil).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "CompilePass" && m.GetParameters().Length == 3
                        && m.GetParameters()[0].ParameterType == typeof(Material)
                        && m.GetParameters()[1].ParameterType == typeof(int)
                        && m.GetParameters()[2].ParameterType == typeof(bool));
                if (compile == null) throw new NotSupportedException("Installed ShaderUtil has no supported CompilePass overload.");
                compile.Invoke(null, new object[] { litMaterial, 0, true });
                litMaterial.EnableKeyword("_ADDITIONAL_LIGHTS");
                litMaterial.EnableKeyword("_CLUSTER_LIGHT_LOOP");
                compile.Invoke(null, new object[] { litMaterial, 0, true });
                string[] errors = ShaderUtil.GetShaderMessages(lit).Concat(ShaderUtil.GetShaderMessages(kernel))
                    .Where(m => m.severity.ToString() == "Error").Select(m => m.message).ToArray();
                if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors));
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(material); Object.DestroyImmediate(litMaterial);
                Object.DestroyImmediate(target); Object.DestroyImmediate(readback);
            }
        }

        private static void PointCase(Result result, Material material, RenderTexture target, Texture2D readback,
            float distance, NumericsVector3 normal, byte intensity)
        {
            ClassicPointLight light = ClassicPointLight.FromTorch(new TorchView(0, new TilePos(0, 0),
                0, 0, 0, 0, 255, 128, 0, intensity, 8192, default));
            NumericsVector3 vertex = new NumericsVector3(distance, 0, 0);
            NumericsVector3 expected = light.ContributionAt(vertex, normal);
            material.SetFloat("_ProbeMode", 0);
            material.SetVector("_ProbeVertex", new Vector4(vertex.X, vertex.Y, vertex.Z, 0));
            material.SetVector("_ProbeNormal", new Vector4(normal.X, normal.Y, normal.Z, 0));
            material.SetVector("_ProbeLightPositionRadius", new Vector4(0, 0, 0, light.Radius * light.Radius));
            material.SetVector("_ProbeLightColourIntensity", new Vector4(light.Colour.X, light.Colour.Y, light.Colour.Z, light.Intensity));
            Check(result, Draw(material, target, readback), new Color(expected.X, expected.Y, expected.Z, 1));
        }

        private static Color Draw(Material material, RenderTexture target, Texture2D readback)
        {
            Graphics.Blit(Texture2D.blackTexture, target, material);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
            readback.Apply();
            return readback.GetPixel(2, 2);
        }

        private static void Check(Result result, Color actual, Color expected)
        {
            float error = Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Abs(actual.g - expected.g),
                Mathf.Abs(actual.b - expected.b), Mathf.Abs(actual.a - expected.a));
            result.MaximumError = Mathf.Max(result.MaximumError, error);
            if (float.IsNaN(error) || error > 0.0001f)
                throw new InvalidOperationException($"GPU case {result.Cases}: expected {expected}, got {actual}, error {error}.");
            result.Cases++;
        }
    }
}
