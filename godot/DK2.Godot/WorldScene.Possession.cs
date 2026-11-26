// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Kmf;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Applies a possession session to the drawn world. The camera takes the session's pose while it holds the view.
/// The latch goes to the
/// view and the lattice and the possessed model is left out before the present (<see cref="LatchPossession"/>), and the
/// roof is drawn after it (<see cref="PresentRoof"/>). Every rule is <see cref="PossessionSession"/>'s,
/// <see cref="PossessionScene"/>'s and <see cref="TerrainCeilingScheduler"/>'s (ADR-0805).</summary>
public partial class WorldScene
{
    public void BuildPossessionForeground(PossessionForeground foreground, int width, int height, long milliseconds, HudFrame frame)
    {
        if (_art != null) foreground.Build(_art, width, height, milliseconds, frame);
    }
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
    /// from it). Null leaves the ordinary camera in control.</summary>
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

    /// <summary>The model hidden until now is shown again, and the frame hides the new one
    /// each time it is drawn (<see cref="ISceneFrameHost.HideCreatureModel"/>).</summary>
    private void HideCreature(int creatureId)
    {
        if (_frame.HiddenCreature.Value == creatureId) return;
        if (_creatures.TryGetValue(_frame.HiddenCreature.Value, out CreatureSlot? shown)) shown.Slot.Node.Visible = true;
        _frame.HiddenCreature = new CreatureId(creatureId);
    }

    /// <summary>After the present: the roof available throughout camera moves to and from possession
    /// (<see cref="PossessionScene.RoofShown(PossessionSession)"/>), its changed chunks built again; the world is read once a turn.</summary>
    public void PresentRoof(WorldState world, GameDatabase database)
    {
        if (!PossessionScene.RoofShown(Possession))
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
    /// which the shaders light from the tile's table without fill.</summary>
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
        float[] anchors = new float[count * 2];
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

        ArrayMesh mesh = instance?.Mesh as ArrayMesh ?? new ArrayMesh();
        mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, TerrainCustomFormat);
        // Use the opaque Ceiling texture, lit as the terrain is.
        _roofMaterial ??= FixedTerrainMaterial(new TerrainMaterialKey(
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
