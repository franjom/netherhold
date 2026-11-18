// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Kmf;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>What a possession changes in the drawn world (plan step 3m), as Unity's runner and <c>DungeonScene</c> apply
/// it: the camera takes the session's pose while it holds the view (<see cref="PossessionPose"/>), the latch goes to the
/// view and the lattice and the possessed model is left out before the present (<see cref="LatchPossession"/>), and the
/// roof is drawn after it (<see cref="PresentRoof"/>). Every rule is <see cref="PossessionSession"/>'s,
/// <see cref="PossessionScene"/>'s and <see cref="TerrainCeilingScheduler"/>'s (ADR-0805).</summary>
public partial class WorldScene
{
    private TerrainCeilingScheduler? _roof;
    // The near plane the camera was last placed with: the ordinary one, or the possession cameras' own.
    private float _near = NearPlane;
    private readonly TerrainCeilingGeometry _roofGeometry = new();
    private Node3D? _roofRoot;
    private MeshInstance3D?[] _roofChunks = [];
    private ShaderMaterial? _roofMaterial;

    /// <summary>The possession whose picture, hidden model and roof this scene draws; null draws none.</summary>
    public PossessionSession? Possession { get; set; }

    /// <summary>The session's pose while the camera is the possession's (the first-person view and the timed moves to and
    /// from it), as Unity's <c>ApplyClassicCamera</c> asks first; null leaves the ordinary camera's.</summary>
    private ClassicCameraPose? PossessionPose(CameraViewport viewport) =>
        Possession is { CameraHeld: true } session && session.TryCamera(ClassicCamera, viewport, out ClassicCameraPose pose)
            ? pose : null;

    /// <summary>Before the present: the possession cameras' latch (<see cref="PossessionScene.Latch"/>), and the creature
    /// whose model the frame leaves out (<see cref="PossessionSession.HiddenCreatureId"/>).</summary>
    public void LatchPossession(WorldState world, PlayerId viewer)
    {
        if (_lightView == null) return;
        PossessionScene.Latch(world, viewer, _lightView, _lattice, _terrain);
        HideCreature(Possession?.HiddenCreatureId ?? 0);
    }

    /// <summary>Unity's <c>HideCreature</c>: the model hidden until now is shown again, and the frame hides the new one
    /// each time it is drawn (<see cref="ISceneFrameHost.HideCreatureModel"/>).</summary>
    private void HideCreature(int creatureId)
    {
        if (_frame.HiddenCreature.Value == creatureId) return;
        if (_creatures.TryGetValue(_frame.HiddenCreature.Value, out CreatureSlot? shown)) shown.Slot.Node.Visible = true;
        _frame.HiddenCreature = new CreatureId(creatureId);
    }

    /// <summary>After the present, which the roof reads: the roof shown exactly while a model is hidden
    /// (<see cref="PossessionScene.RoofShown"/>), its changed chunks built again; the world is read once a turn.</summary>
    public void PresentRoof(WorldState world, GameDatabase database)
    {
        if (!PossessionScene.RoofShown(Possession?.HiddenCreatureId ?? 0))
        {
            if (_roofRoot is { Visible: true }) _roofRoot.Visible = false;
            return;
        }
        if (_roof == null || _roofRoot == null)
        {
            _roof = new TerrainCeilingScheduler(world.Width, world.Height, _lattice);
            _roofChunks = new MeshInstance3D?[_roof.ChunkCount];
            _roofRoot = new Node3D { Name = "Roof" };
            AddChild(_roofRoot);
        }
        if (_roof.Refresh(world, database))
            for (int chunk = 0; chunk < _roofChunks.Length; chunk++)
                if (_roof.TryBuild(chunk, _roofGeometry)) UploadRoof(chunk, _roofRoot);
        if (!_roofRoot.Visible) _roofRoot.Visible = true;
    }

    /// <summary>The chunk the scheduler just built, as a terrain chunk is uploaded: through the frame change with the
    /// triangle order kept, each vertex carrying its tile and the roof's anchor kind (<see cref="TerrainCustomFormat"/>),
    /// which the lit shaders light from the tile's table with none of the fill. Unity's <c>TerrainCeilingChunks</c>.</summary>
    private void UploadRoof(int chunk, Node3D root)
    {
        MeshInstance3D? instance = _roofChunks[chunk];
        TerrainCeilingGeometry geometry = _roofGeometry;
        int count = geometry.Positions.Count;
        if (count == 0)
        {
            if (instance != null) instance.Visible = false;
            return;
        }
        var positions = new Vector3[count];
        var normals = new Vector3[count];
        var uvs = new Vector2[count];
        var anchors = new float[count * 2];
        for (int i = 0; i < count; i++)
        {
            positions[i] = ToGodot(RightHandedFrame.Point(geometry.Positions[i]));
            normals[i] = ToGodot(RightHandedFrame.Normal(geometry.Normals[i]));
            uvs[i] = new Vector2(geometry.Uvs[i].X, geometry.Uvs[i].Y);
            anchors[i * 2] = geometry.LightAnchors[i].X;
            anchors[(i * 2) + 1] = geometry.LightAnchors[i].Y;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Custom0] = anchors;
        arrays[(int)Mesh.ArrayType.Index] = geometry.Triangles.ToArray();

        var mesh = instance?.Mesh as ArrayMesh ?? new ArrayMesh();
        mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, TerrainCustomFormat);
        // Unity's ClassicMeshes.ForTexture: the one Ceiling texture, opaque, lit as the terrain is.
        _roofMaterial ??= MaterialFor(new TerrainMaterialKey(
            new ClassicMaterialRecipe(TerrainCeiling.TextureName, KmfBlend.Opaque, 0f), TerrainMaterialVariant.Plain));
        mesh.SurfaceSetMaterial(0, _roofMaterial);
        if (instance == null)
        {
            instance = new MeshInstance3D { Name = "RoofChunk" + chunk, Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            root.AddChild(instance);
            _roofChunks[chunk] = instance;
        }
        else if (!instance.Visible) instance.Visible = true;
    }
}
