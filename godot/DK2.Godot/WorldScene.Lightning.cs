// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using DK2.Art;
using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>Shared lightning ribbons drawn as additive quad instances among the blended runs and items.</summary>
public partial class WorldScene
{
    private LightningDrawList? _lightning;
    private WorldView? _lightningView;
    private readonly List<AnimationPoint> _lightningLocalPoints = new List<AnimationPoint>(64);
    private readonly List<LightningControlPoint> _lightningPoints = new List<LightningControlPoint>(64);
    private ItemBatch? _lightningItems;
    private ShaderMaterial? _lightningMaterial;
    private Rid _lightningMesh;
    private Transform3D _lightningCameraPose;
    private bool _lightningCameraValid;
    private long _lightningTicks, _lightningLateBytes, _lightningWarmUpBytes;
    private int _lightningFrames, _lightningMaxQuads, _lightningCrackleQuads;

    private void OpenLightning(RoomConstructionParticles effects, WorldView view, uint seed)
    {
        _lightning = new LightningDrawList(effects, seed);
        _lightningView = view;
    }

    private void BeginLightning()
    {
        SetLightningCamera(beginFrame: true);
    }

    private void SetLightningCamera(bool beginFrame)
    {
        if (_lightning == null)
        {
            return;
        }

        if (_camera == null)
        {
            _lightningCameraValid = false;
            if (beginFrame)
            {
                _lightning.BeginFrame(NMatrix4x4.Identity, NMatrix4x4.Identity, hasCamera: false);
            }
            else
            {
                _lightning.UpdateCameraMatrices(NMatrix4x4.Identity, NMatrix4x4.Identity, hasCamera: false);
            }

            return;
        }
        Transform3D camera = _camera.GlobalTransform;
        if (!beginFrame && _lightningCameraValid && camera == _lightningCameraPose)
        {
            return;
        }

        _lightningCameraPose = camera;
        _lightningCameraValid = true;
        NVector3 right = RightHandedFrame.Direction(new NVector3(camera.Basis.X.X, camera.Basis.X.Y, camera.Basis.X.Z));
        NVector3 up = RightHandedFrame.Direction(new NVector3(camera.Basis.Y.X, camera.Basis.Y.Y, camera.Basis.Y.Z));
        NVector3 forward = RightHandedFrame.Direction(new NVector3(-camera.Basis.Z.X, -camera.Basis.Z.Y, -camera.Basis.Z.Z));
        NVector3 position = RightHandedFrame.Point(new NVector3(camera.Origin.X, camera.Origin.Y, camera.Origin.Z));
        var cameraToWorld = new NMatrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0, position.X, position.Y, position.Z, 1);
        bool hasCamera = NMatrix4x4.Invert(cameraToWorld, out NMatrix4x4 worldToCamera);
        if (beginFrame)
        {
            _lightning.BeginFrame(worldToCamera, cameraToWorld, hasCamera);
        }
        else
        {
            _lightning.UpdateCameraMatrices(worldToCamera, cameraToWorld, hasCamera);
        }
    }

    private void DrawCreatureLightning(CreatureId creature, GameTick tick)
    {
        if (_lightning == null || _drawn?.Slot.Animation?.Player is not { } player
            || !_lightning.NeedsCreatureCrackle(creature, tick, player.InstallationRevision))
        {
            return;
        }

        long started = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        int before = _lightning.Vertices.Count;
        player.CollectLightningPoints(_lightningLocalPoints);
        _lightningPoints.Clear();
        foreach (AnimationPoint point in _lightningLocalPoints)
        {
            _lightningPoints.Add(LightningControlPoint.FromRenderWorld(point.Id, _drawn.Slot.ToRender(point.Local)));
        }

        _lightning.DrawCrackle(_lightningPoints);
        _lightningCrackleQuads += (_lightning.Vertices.Count - before) / 4;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (_lightningFrames < DrawWarmUp)
        {
            _lightningWarmUpBytes += bytes;
        }
        else
        {
            _lightningLateBytes += bytes;
        }

        _lightningTicks += Stopwatch.GetTimestamp() - started;
    }

    private void DrawLightning()
    {
        if (_lightning == null || _effectSimulation == null || _lightningView == null)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        SetLightningCamera(beginFrame: false);
        _lightning.DrawShots(_effectSimulation.World, _effectSimulation.Database, _lightningView, _lightFraction);
        _lightning.DrawEffects();
        IReadOnlyList<NVector3> vertices = _lightning.Vertices;
        int quads = vertices.Count / 4;
        if (quads != 0)
        {
            _lightningItems ??= MakeLightningItems();
            for (int i = 0; i < vertices.Count; i += 4)
            {
                Vector3 a = ToGodot(RightHandedFrame.Point(vertices[i]));
                Vector3 b = ToGodot(RightHandedFrame.Point(vertices[i + 1]));
                Vector3 c = ToGodot(RightHandedFrame.Point(vertices[i + 2]));
                Vector3 d = ToGodot(RightHandedFrame.Point(vertices[i + 3]));
                Vector3 low = a.Min(b).Min(c).Min(d), high = a.Max(b).Max(c).Max(d);
                Vector3 centre = (low + high) * .5f;
                float radius = Mathf.Sqrt(Mathf.Max(Mathf.Max(centre.DistanceSquaredTo(a), centre.DistanceSquaredTo(b)),
                    Mathf.Max(centre.DistanceSquaredTo(c), centre.DistanceSquaredTo(d))));
                // The shader reads these columns as positions, rather than transforming a parallelogram. All four
                // ribbon corners survive unchanged, and the shared item path supplies the actual culling bounds.
                AddItem(_lightningItems, centre, radius, new Transform3D(new Basis(b, c, d), a), Colors.White, Colors.White);
                if (PreviewPose.HasValue)
                {
                    SwapPreviewItems();
                    AddItem(_lightningItems, centre, radius, new Transform3D(new Basis(b, c, d), a), Colors.White, Colors.White);
                    SwapPreviewItems();
                }
            }
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (_lightningFrames++ < DrawWarmUp)
        {
            _lightningWarmUpBytes += bytes;
        }
        else
        {
            _lightningLateBytes += bytes;
        }

        _lightningTicks += Stopwatch.GetTimestamp() - started;
        _lightningMaxQuads = Math.Max(_lightningMaxQuads, quads);
    }

    private ItemBatch MakeLightningItems()
    {
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        // Z is a corner selector into the instance's four columns; UV matches LightningRibbon's file-row convention.
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(0, 0, 3), new Vector3(0, 1, 0), new Vector3(1, 0, 1), new Vector3(1, 1, 2) };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 2, 1, 3 };
        _lightningMesh = RenderingServer.MeshCreate();
        RenderingServer.MeshAddSurfaceFromArrays(_lightningMesh, RenderingServer.PrimitiveType.Triangles, arrays);
        _lightningMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Lightning.gdshader") };
        _lightningMaterial.SetShaderParameter("lightning_texture", TextureFor("Lightning"));
        return CreateItemBatch(_lightningMesh, _lightningMaterial.GetRid(), BlendedItemBlend.Additive);
    }

    private void AddLightningCounters(List<KeyValuePair<string, double>> into)
    {
        Add(into, "lightning.ms", Ms(_lightningTicks) / Math.Max(1, _lightningFrames));
        Add(into, "lightning.lateBytes", _lightningLateBytes);
        Add(into, "lightning.maxQuads", _lightningMaxQuads);
        Add(into, "lightning.crackleQuads", _lightningCrackleQuads);
    }

    private void FreeLightning()
    {
        GD.Print("DK2.Godot lightning: ", _lightningFrames, " frames, up to ", _lightningMaxQuads, " quads; ",
            Milliseconds(_lightningTicks / Math.Max(1, _lightningFrames)), " ms/frame; ", _lightningWarmUpBytes,
            " B in warm-up and ", _lightningLateBytes, " B after; ", _lightningCrackleQuads, " crackle quads");
        _lightningMaterial?.Dispose();
        if (_lightningMesh.IsValid)
        {
            RenderingServer.FreeRid(_lightningMesh);
        }

        _lightningMesh = default;
        _lightningItems = null;
        _lightning = null;
    }
}
