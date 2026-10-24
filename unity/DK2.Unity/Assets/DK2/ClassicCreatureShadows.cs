// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Presentation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using NVector3 = System.Numerics.Vector3;

namespace DK2.Unity
{
    public sealed class ClassicCreatureShadows : IDisposable
    {
        private const int Border = 1;
        private const int Supersampling = 8;
        private const int Stride = ClassicMeshShadow.TextureSize + Border * 2;
        private static readonly ProfilerMarker SelectionMarker = new ProfilerMarker("DK2.Shadows.Selection");
        private static readonly ProfilerMarker SilhouetteMarker = new ProfilerMarker("DK2.Shadows.Silhouette");
        private static readonly ProfilerMarker ReceiverMarker = new ProfilerMarker("DK2.Shadows.ReceiverUpload");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int CentreId = Shader.PropertyToID("_CasterCentreRadius");
        private static readonly int CrossId = Shader.PropertyToID("_CrossAxis");
        private static readonly int AlongId = Shader.PropertyToID("_AlongCos");
        private static readonly int SinId = Shader.PropertyToID("_ProjectionSin");
        private static readonly int TransformId = Shader.PropertyToID("_CasterLocalToWorld");

        private sealed class Caster
        {
            public readonly MeshFilter[] Filters;
            public readonly MeshRenderer[] Renderers;
            public float GroundHeight;

            public Caster(GameObject gameObject)
            {
                Filters = gameObject.GetComponentsInChildren<MeshFilter>(true);
                Renderers = new MeshRenderer[Filters.Length];
                for (int i = 0; i < Filters.Length; i++)
                    Renderers[i] = Filters[i].GetComponent<MeshRenderer>();
            }
        }

        private readonly struct Draw
        {
            public readonly Caster Caster;
            public readonly ClassicShadowCaster Bounds;
            public readonly ClassicShadowProjection Projection;
            public Draw(Caster caster, ClassicShadowCaster bounds, ClassicShadowProjection projection)
            { Caster = caster; Bounds = bounds; Projection = projection; }
        }

        private readonly Dictionary<GameObject, Caster> _casters = new Dictionary<GameObject, Caster>();
        private readonly Dictionary<int, NVector3> _torches = new Dictionary<int, NVector3>();
        private readonly Plane[] _frustum = new Plane[6];
        private readonly List<Draw> _draws = new List<Draw>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Color> _colours = new List<Color>();
        private readonly List<int> _indices = new List<int>();
        private readonly NVector3[] _nearest = new NVector3[ClassicMeshShadow.MaximumSources + 1];
        private readonly float[] _distances = new float[ClassicMeshShadow.MaximumSources + 1];
        private readonly int[] _sourceKeys = new int[ClassicMeshShadow.MaximumSources + 1];
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
        private readonly CommandBuffer _commands = new CommandBuffer { name = "DK2 mesh shadow coverage" };
        private readonly GameObject _receiver;
        private readonly MeshRenderer _receiverRenderer;
        private readonly Mesh _receiverMesh;
        private readonly Material _maskMaterial, _reduceMaterial, _receiverMaterial;
        private RenderTexture _highResolution, _atlas;
        private int _grid;
        private bool _disposed;

        public ClassicCreatureShadows(Transform root)
        {
            _maskMaterial = MakeMaterial("ClassicShadowMask");
            _reduceMaterial = MakeMaterial("ClassicShadowReduce");
            _receiverMaterial = MakeMaterial("ClassicShadowReceiver");
            _receiver = new GameObject("Classic creature shadows");
            _receiver.transform.SetParent(root, true);
            _receiverMesh = new Mesh { name = "Classic mesh shadow receivers", indexFormat = IndexFormat.UInt32 };
            _receiverMesh.MarkDynamic();
            _receiver.AddComponent<MeshFilter>().sharedMesh = _receiverMesh;
            _receiverRenderer = _receiver.AddComponent<MeshRenderer>();
            _receiverRenderer.sharedMaterial = _receiverMaterial;
            _receiverRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _receiverRenderer.receiveShadows = false;
            _receiverRenderer.lightProbeUsage = LightProbeUsage.Off;
            _receiverRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _receiverRenderer.enabled = false;
        }

        private static Material MakeMaterial(string name)
        {
            Shader shader = Resources.Load<Shader>("Shaders/" + name);
            if (shader == null) throw new InvalidOperationException("Missing classic shadow shader: " + name);
            return new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        }

        public void Register(GameObject caster)
        {
            if (caster != null && !_casters.ContainsKey(caster)) _casters.Add(caster, new Caster(caster));
        }

        public void Remove(GameObject caster) => _casters.Remove(caster);

        public void SetGroundHeight(GameObject caster, float groundHeight)
        {
            if (_casters.TryGetValue(caster, out Caster entry)) entry.GroundHeight = groundHeight;
        }

        public void SetTorch(in TorchView torch) =>
            _torches[torch.FloorTileIndex] = ClassicPointLight.FromTorch(torch).Position;

        public void RemoveTorch(int floorTileIndex) => _torches.Remove(floorTileIndex);

        public void Clear()
        {
            _casters.Clear();
            _torches.Clear();
            _draws.Clear();
            _receiverMesh.Clear();
            if (_receiverRenderer != null) _receiverRenderer.enabled = false;
        }

        // Called by the owning scene before this camera renders; snapshot ground data is supplied separately.
        public void Render(Camera camera)
        {
            if (_disposed || camera == null) return;
            _draws.Clear();
            using (SelectionMarker.Auto())
            {
                GeometryUtility.CalculateFrustumPlanes(camera, _frustum);
                foreach (KeyValuePair<GameObject, Caster> item in _casters)
                {
                    if (item.Key == null || !item.Key.activeInHierarchy) continue;
                    if (!TryBounds(item.Value, camera, out Bounds bounds)) continue;
                    if (!GeometryUtility.TestPlanesAABB(_frustum, bounds)) continue;
                    var caster = new ClassicShadowCaster(ToNumerics(bounds.center), bounds.extents.magnitude,
                        item.Value.GroundHeight);
                    int count = SelectSources(caster.Centre);
                    float fade = count > ClassicMeshShadow.MaximumSources
                        ? Mathf.Sqrt(_distances[ClassicMeshShadow.MaximumSources]) : ClassicMeshShadow.MaximumDistance;
                    for (int i = 0; i < Math.Min(count, ClassicMeshShadow.MaximumSources); i++)
                    {
                        var source = new ClassicShadowSource(_nearest[i], fade);
                        if (ClassicMeshShadow.TryProject(caster, source, out ClassicShadowProjection projection))
                            _draws.Add(new Draw(item.Value, caster, projection));
                    }
                }
            }
            _receiverRenderer.enabled = _draws.Count > 0;
            if (_draws.Count == 0) return;
            for (int layer = 0; layer < 32; layer++)
                if ((camera.cullingMask & (1 << layer)) != 0) { _receiver.layer = layer; break; }
            EnsureAtlas(_draws.Count);
            using (SilhouetteMarker.Auto()) DrawCoverage(camera);
            using (ReceiverMarker.Auto()) UploadReceivers();
        }

        private bool TryBounds(Caster caster, Camera camera, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            for (int i = 0; i < caster.Filters.Length; i++)
            {
                if (!VisiblePart(caster, i, camera)) continue;
                caster.Renderers[i].GetSharedMaterials(_materials);
                bool opaque = false;
                for (int m = 0; m < _materials.Count; m++) opaque |= IsOpaque(_materials[m]);
                if (!opaque) continue;
                if (!found) bounds = caster.Renderers[i].bounds;
                else bounds.Encapsulate(caster.Renderers[i].bounds);
                found = true;
            }
            return found && bounds.extents.sqrMagnitude > 0;
        }

        private static bool VisiblePart(Caster caster, int index, Camera camera)
        {
            MeshFilter filter = caster.Filters[index];
            MeshRenderer renderer = caster.Renderers[index];
            return filter != null && filter.sharedMesh != null && renderer != null && renderer.enabled
                && !renderer.forceRenderingOff && filter.gameObject.activeInHierarchy
                && (camera.cullingMask & (1 << filter.gameObject.layer)) != 0;
        }

        private static bool IsOpaque(Material material) => material != null
            && (material.HasProperty(SurfaceId) ? material.GetFloat(SurfaceId) == 0
                : material.renderQueue < (int)RenderQueue.Transparent);

        private int SelectSources(NVector3 centre)
        {
            int count = 0;
            foreach (KeyValuePair<int, NVector3> torch in _torches)
            {
                float squared = NVector3.DistanceSquared(centre, torch.Value);
                if (squared >= ClassicMeshShadow.MaximumDistance * ClassicMeshShadow.MaximumDistance) continue;
                int slot = 0;
                while (slot < count && (_distances[slot] < squared
                    || (_distances[slot] == squared && _sourceKeys[slot] < torch.Key))) slot++;
                if (slot >= _nearest.Length) continue;
                for (int j = Math.Min(count, _nearest.Length - 1); j > slot; j--)
                {
                    _distances[j] = _distances[j - 1];
                    _nearest[j] = _nearest[j - 1];
                    _sourceKeys[j] = _sourceKeys[j - 1];
                }
                _distances[slot] = squared;
                _nearest[slot] = torch.Value;
                _sourceKeys[slot] = torch.Key;
                count = Math.Min(count + 1, _nearest.Length);
            }
            return count;
        }

        private void EnsureAtlas(int count)
        {
            if (_atlas != null && count <= _grid * _grid) return;
            int grid = Math.Max(1, _grid);
            while (grid * grid < count) grid *= 2;
            int size = checked(grid * Stride);
            if (size * Supersampling > SystemInfo.maxTextureSize)
                throw new InvalidOperationException("Classic shadow atlas exceeds this GPU's maximum texture dimensions.");
            ReleaseAtlas();
            _grid = grid;
            _highResolution = MakeTarget(size * Supersampling, "Classic shadow supersamples", FilterMode.Point);
            _atlas = MakeTarget(size, "Classic 32px shadow coverage", FilterMode.Bilinear);
            _reduceMaterial.SetTexture("_MainTex", _highResolution);
            _receiverMaterial.SetTexture("_MainTex", _atlas);
        }

        private static RenderTexture MakeTarget(int size, string name, FilterMode filtering)
        {
            var target = new RenderTexture(size, size, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                name = name, filterMode = filtering, wrapMode = TextureWrapMode.Clamp,
                useMipMap = false, autoGenerateMips = false, antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!target.Create())
            {
                DestroyOwned(target);
                throw new InvalidOperationException("Could not allocate classic shadow coverage target.");
            }
            return target;
        }

        private void DrawCoverage(Camera camera)
        {
            _commands.Clear();
            _commands.BeginSample("DK2.Shadows.CoverageGPU");
            _commands.SetRenderTarget(_highResolution);
            _commands.ClearRenderTarget(false, true, Color.clear);
            for (int index = 0; index < _draws.Count; index++)
            {
                Draw draw = _draws[index];
                ClassicShadowProjection projection = draw.Projection;
                _commands.SetViewport(new Rect((index % _grid * Stride + Border) * Supersampling,
                    (index / _grid * Stride + Border) * Supersampling,
                    ClassicMeshShadow.TextureSize * Supersampling, ClassicMeshShadow.TextureSize * Supersampling));
                _properties.Clear();
                NVector3 centre = draw.Bounds.Centre;
                _properties.SetVector(CentreId, new Vector4(centre.X, centre.Y, centre.Z, draw.Bounds.Radius));
                _properties.SetVector(CrossId, ToUnity(projection.CrossAxis));
                NVector3 along = projection.AlongAxis;
                _properties.SetVector(AlongId, new Vector4(along.X, along.Y, along.Z, Mathf.Cos(projection.AngleRadians)));
                _properties.SetFloat(SinId, Mathf.Sin(projection.AngleRadians));
                for (int part = 0; part < draw.Caster.Filters.Length; part++)
                {
                    if (!VisiblePart(draw.Caster, part, camera)) continue;
                    MeshFilter filter = draw.Caster.Filters[part];
                    Mesh mesh = filter.sharedMesh;
                    Matrix4x4 transform = filter.transform.localToWorldMatrix;
                    _properties.SetMatrix(TransformId, transform);
                    draw.Caster.Renderers[part].GetSharedMaterials(_materials);
                    for (int submesh = 0; submesh < Math.Min(mesh.subMeshCount, _materials.Count); submesh++)
                        if (IsOpaque(_materials[submesh]))
                            _commands.DrawMesh(mesh, transform, _maskMaterial, submesh, 0, _properties);
                }
            }
            _commands.SetRenderTarget(_atlas);
            _commands.SetViewport(new Rect(0, 0, _atlas.width, _atlas.height));
            _commands.DrawProcedural(Matrix4x4.identity, _reduceMaterial, 0, MeshTopology.Triangles, 3);
            _commands.EndSample("DK2.Shadows.CoverageGPU");
            RenderTexture previous = RenderTexture.active;
            try { Graphics.ExecuteCommandBuffer(_commands); }
            finally { RenderTexture.active = previous; }
        }

        private void UploadReceivers()
        {
            _vertices.Clear(); _uvs.Clear(); _colours.Clear(); _indices.Clear();
            float inverseSize = 1f / (_grid * Stride);
            Matrix4x4 worldToLocal = _receiver.transform.worldToLocalMatrix;
            for (int index = 0; index < _draws.Count; index++)
            {
                ClassicShadowProjection projection = _draws[index].Projection;
                Vector3 centre = ToUnity(projection.Centre);
                Vector3 cross = ToUnity(projection.CrossAxis) * projection.HalfWidth;
                Vector3 along = ToUnity(projection.AlongAxis) * projection.HalfLength;
                int start = _vertices.Count;
                _vertices.Add(worldToLocal.MultiplyPoint3x4(centre - cross - along));
                _vertices.Add(worldToLocal.MultiplyPoint3x4(centre + cross - along));
                _vertices.Add(worldToLocal.MultiplyPoint3x4(centre + cross + along));
                _vertices.Add(worldToLocal.MultiplyPoint3x4(centre - cross + along));
                float x = (index % _grid * Stride + Border) * inverseSize;
                float y = (index / _grid * Stride + Border) * inverseSize;
                float width = ClassicMeshShadow.TextureSize * inverseSize;
                _uvs.Add(new Vector2(x, y)); _uvs.Add(new Vector2(x + width, y));
                _uvs.Add(new Vector2(x + width, y + width)); _uvs.Add(new Vector2(x, y + width));
                var colour = new Color(projection.Strength, projection.Strength, projection.Strength, 1);
                for (int corner = 0; corner < 4; corner++) _colours.Add(colour);
                _indices.Add(start); _indices.Add(start + 1); _indices.Add(start + 2);
                _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 3);
            }
            _receiverMesh.Clear();
            _receiverMesh.SetVertices(_vertices);
            _receiverMesh.SetUVs(0, _uvs);
            _receiverMesh.SetColors(_colours);
            _receiverMesh.SetTriangles(_indices, 0, true);
        }

        private static NVector3 ToNumerics(Vector3 value) => new NVector3(value.x, value.y, value.z);
        private static Vector3 ToUnity(NVector3 value) => new Vector3(value.X, value.Y, value.Z);

        private void ReleaseAtlas()
        {
            if (_highResolution != null) { _highResolution.Release(); DestroyOwned(_highResolution); }
            if (_atlas != null) { _atlas.Release(); DestroyOwned(_atlas); }
            _highResolution = null; _atlas = null;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_receiverRenderer != null) _receiverRenderer.enabled = false;
            _casters.Clear(); _torches.Clear(); _draws.Clear();
            _commands.Dispose();
            ReleaseAtlas();
            DestroyOwned(_receiverMesh);
            DestroyOwned(_receiver);
            DestroyOwned(_maskMaterial); DestroyOwned(_reduceMaterial); DestroyOwned(_receiverMaterial);
        }
    }
}
