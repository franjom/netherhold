// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.


using DK2.Art;
using DK2.Art.Kmf;
using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

public partial class WorldScene
{
    private readonly ClassicShadowSourceSelector _creatureShadowSources = new ClassicShadowSourceSelector();
    private readonly List<CreatureShadowReceiver> _creatureShadowReceivers = new List<CreatureShadowReceiver>();
    private NVector2[] _shadowVertices = [];
    private Shader? _creatureShadowShader;
    private QuadMesh? _creatureShadowQuad;
    private bool _creatureShadows;
    private int _shownCreatureShadows;

    internal void SetCreatureShadows(bool enabled)
    {
        if (_creatureShadows == enabled)
        {
            return;
        }

        _creatureShadows = enabled;
        if (!enabled)
        {
            HideUnusedCreatureShadows(0);
        }
    }

    private void DrawCreatureShadows()
    {
        if (!_creatureShadows || _lightWorld == null || _lightView == null || View == null)
        {
            return;
        }

        int count = 0;
        foreach ((int id, CreatureSlot creature) in _creatures)
        {
            ModelSlot slot = creature.Slot;
            if (!slot.Node.Visible)
            {
                continue;
            }

            IReadOnlyList<NVector3> vertices = slot.ShadowPositions(out PickBox bounds);
            if (PlacedFrustum.Excludes(bounds))
            {
                continue;
            }

            _creatureShadowSources.Begin(bounds.Centre);
            foreach ((int key, NVector3 position) in _shadowTorches)
            {
                _creatureShadowSources.Offer(key, position);
            }

            if (_creatureShadowSources.Count == 0 || _lightWorld.CreatureById(id) is not CreatureState state)
            {
                continue;
            }

            var point = new FpPosition3(new Fp32(state.PosX), new Fp32(state.PosY), default);
            float ground = _lightView.GroundHeightAt(_lightWorld, point).Raw / (float)Fp32.One;
            var caster = new ClassicShadowCaster(bounds.Centre, bounds.Extents.Length(), ground);
            for (int i = 0; i < _creatureShadowSources.Count; i++)
            {
                var source = new ClassicShadowSource(_creatureShadowSources.Source(i), _creatureShadowSources.FadeDistance);
                if (!ClassicMeshShadow.TryProject(caster, source, out ClassicShadowProjection projection))
                {
                    continue;
                }

                CreatureShadowReceiver receiver = CreatureShadowAt(count++);
                RasterizeCreatureShadow(receiver.Samples, slot, vertices, caster, projection);
                receiver.Show(projection);
            }
        }

        HideUnusedCreatureShadows(count);
    }

    private void RasterizeCreatureShadow(Span<byte> coverage, ModelSlot slot, IReadOnlyList<NVector3> vertices,
        in ClassicShadowCaster caster, in ClassicShadowProjection projection)
    {
        coverage.Clear();
        if (vertices.Count > _shadowVertices.Length)
        {
            Array.Resize(ref _shadowVertices, vertices.Count);
        }

        const float half = ClassicMeshShadow.TextureSize / 2f;
        float scale = half / caster.Radius;
        NVector3 cross = projection.CrossAxis * scale;
        NVector3 along = (projection.AlongAxis * MathF.Cos(projection.AngleRadians)
            + NVector3.UnitY * MathF.Sin(projection.AngleRadians)) * scale;
        for (int i = 0; i < vertices.Count; i++)
        {
            NVector3 relative = slot.ToRender(vertices[i]) - caster.Centre;
            _shadowVertices[i] = new NVector2(half + NVector3.Dot(relative, cross), half - NVector3.Dot(relative, along));
        }

        RenderMesh mesh = slot.ShadowMesh;
        for (int surface = 0; surface < mesh.Triangles.Count; surface++)
        {
            if (mesh.Blends[surface] != KmfBlend.Opaque)
            {
                continue;
            }

            int[] triangles = mesh.Triangles[surface];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                ClassicShadowCoverage.AddTriangle(coverage, _shadowVertices[triangles[i]], _shadowVertices[triangles[i + 1]],
                    _shadowVertices[triangles[i + 2]]);
            }
        }
        ClassicShadowCoverage.Resolve(coverage);
    }

    private CreatureShadowReceiver CreatureShadowAt(int index)
    {
        if (index < _creatureShadowReceivers.Count)
        {
            return _creatureShadowReceivers[index];
        }

        _creatureShadowShader ??= GD.Load<Shader>("res://Shaders/CreatureShadow.gdshader");
        _creatureShadowQuad ??= new QuadMesh { Size = Vector2.One };
        var receiver = new CreatureShadowReceiver(_creatureShadowShader, _creatureShadowQuad);
        _creatureShadowReceivers.Add(receiver);
        AddChild(receiver.Node);
        return receiver;
    }

    private void HideUnusedCreatureShadows(int count)
    {
        for (int i = count; i < _shownCreatureShadows; i++)
        {
            _creatureShadowReceivers[i].Node.Hide();
        }

        _shownCreatureShadows = count;
    }

    private void FreeCreatureShadows()
    {
        foreach (CreatureShadowReceiver receiver in _creatureShadowReceivers)
        {
            receiver.Dispose();
        }

        _creatureShadowReceivers.Clear();
        _creatureShadowQuad?.Dispose();
        _creatureShadowShader?.Dispose();
    }

    private sealed class CreatureShadowReceiver : IDisposable
    {
        private const int Size = ClassicMeshShadow.TextureSize;
        private const float GroundBias = .005f;
        private static readonly StringName SilhouetteParameter = "silhouette";
        private static readonly StringName StrengthParameter = "strength";

        private readonly Image _image;
        private readonly ImageTexture _texture;
        private readonly ShaderMaterial _material;
        public readonly byte[] Samples = new byte[Size * Size];
        public MeshInstance3D Node { get; }

        public CreatureShadowReceiver(Shader shader, QuadMesh quad)
        {
            _image = Image.CreateFromData(Size, Size, false, Image.Format.R8, Samples);
            _texture = ImageTexture.CreateFromImage(_image);
            _material = new ShaderMaterial { Shader = shader, RenderPriority = -10 };
            _material.SetShaderParameter(SilhouetteParameter, _texture);
            Node = new MeshInstance3D { Mesh = quad, MaterialOverride = _material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        }

        public void Show(in ClassicShadowProjection projection)
        {
            _image.SetData(Size, Size, false, Image.Format.R8, Samples);
            _texture.Update(_image);
            Vector3 x = ToGodot(RightHandedFrame.Point(projection.CrossAxis * (2 * projection.HalfWidth)));
            Vector3 y = ToGodot(RightHandedFrame.Point(projection.AlongAxis * (2 * projection.HalfLength)));
            Node.Transform = new Transform3D(new Basis(x, y, Vector3.Up), ToGodot(RightHandedFrame.Point(projection.Centre)) + Vector3.Up * GroundBias);
            Node.SetInstanceShaderParameter(StrengthParameter, projection.Strength);
            Node.Show();
        }

        public void Dispose()
        {
            Node.QueueFree();
            _material.Dispose();
            _texture.Dispose();
            _image.Dispose();
        }
    }
}
