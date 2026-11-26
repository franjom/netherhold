// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Kmf;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Persistent tile state sampled by the world shader through each vertex's state sources.</summary>
public partial class WorldScene
{
    private Image? _terrainStateImage;
    private ImageTexture? _terrainStateTexture;
    private int _terrainStateWidth;
    private bool _terrainStateDirty;
    private TileView[] _terrainStateTiles = [];
    private bool[] _terrainStateKnown = [];
    private static readonly StringName TerrainStateParameter = "terrain_state", TerrainFogParameter = "terrain_fog",
        TerrainOverlayOnlyParameter = "terrain_overlay_only", TerrainOverlayDeferredParameter = "terrain_overlay_deferred";

    private void OpenTerrainState(int width, int height)
    {
        _terrainStateWidth = width;
        _terrainStateTiles = new TileView[width * height];
        _terrainStateKnown = new bool[width * height];
        _terrainStateImage = Image.CreateEmpty(width, height, false, Image.Format.Rgbaf);
        _terrainStateTexture = ImageTexture.CreateFromImage(_terrainStateImage);
        Global("dk2_terrain_state", RenderingServer.GlobalShaderParameterType.Sampler2D, _terrainStateTexture);
        Global("dk2_terrain_grid", RenderingServer.GlobalShaderParameterType.Ivec2, new Vector2I(width, height));
        Global("dk2_tag_tint", RenderingServer.GlobalShaderParameterType.Vec3, ToGodot(TerrainMaterialLook.TagTint));
        Global("dk2_tag_overlay", RenderingServer.GlobalShaderParameterType.Vec3, ToGodot(TerrainMaterialLook.TagOverlayColour));
        _terrainStateDirty = false;
    }

    private bool SetTerrainState(int tileIndex, in TileView tile)
    {
        bool geometryChanged = !_terrainStateKnown[tileIndex] || !TerrainTileState.GeometryEquals(_terrainStateTiles[tileIndex], tile);
        _terrainStateKnown[tileIndex] = true;
        _terrainStateTiles[tileIndex] = tile;
        System.Numerics.Vector4 state = TerrainTileState.Encode(tile);
        _terrainStateImage!.SetPixel(tileIndex % _terrainStateWidth, tileIndex / _terrainStateWidth,
            new Color(state.X, state.Y, state.Z, state.W));
        _terrainStateDirty = true;
        return geometryChanged;
    }

    private void FlushTerrainState()
    {
        if (!_terrainStateDirty) return;
        _terrainStateTexture!.Update(_terrainStateImage!);
        _terrainStateDirty = false;
    }

    private readonly Dictionary<TerrainMaterialKey, ShaderMaterial> _fixedTerrainMaterials = new();
    private ShaderMaterial? _terrainOverlayMaterial;

    private ShaderMaterial FixedTerrainMaterial(in TerrainMaterialKey key)
    {
        if (_fixedTerrainMaterials.TryGetValue(key, out ShaderMaterial? material)) return material;
        var look = TerrainMaterialLook.For(key);
        material = WorldMaterial(look.Texture, look.Blend, look.WritesDepth, look.FullLight, look.Colour, look.AddedLight);
        _fixedTerrainMaterials.Add(key, material);
        return material;
    }

    private void ConfigureTerrainMaterial(ShaderMaterial material, in TerrainMaterialKey key, in TerrainMaterialLook look)
    {
        bool fog = key.Variant == TerrainMaterialVariant.Fog;
        material.SetShaderParameter(TerrainStateParameter, true);
        material.SetShaderParameter(TerrainFogParameter, fog);
        // The AO quad draws first in the transparent pass. A static chunk's overlay surface adds its state overlay
        // after that multiply, before the sorted stream; no overlay triangles enter the stream.
        if (PostOcclusion || look.Blend != KmfBlend.Opaque)
        {
            material.SetShaderParameter(TerrainOverlayDeferredParameter, true);
        }
    }

    private ShaderMaterial TerrainOverlayMaterial()
    {
        if (_terrainOverlayMaterial != null) return _terrainOverlayMaterial;
        _terrainOverlayMaterial = WorldMaterial("", KmfBlend.Additive, false, true,
            System.Numerics.Vector4.One, System.Numerics.Vector3.Zero);
        _terrainOverlayMaterial.SetShaderParameter(TerrainStateParameter, true);
        _terrainOverlayMaterial.SetShaderParameter(TerrainOverlayOnlyParameter, true);
        _terrainOverlayMaterial.RenderPriority = OverlayPriority;
        return _terrainOverlayMaterial;
    }

    private void UploadTerrainOverlayChunk(int chunk)
    {
        MeshInstance3D? instance = _terrainOverlayChunks[chunk];
        if (instance == null && _terrainOverlaySurface.IndexCount == 0) return;
        ArrayMesh mesh = instance?.Mesh as ArrayMesh ?? new ArrayMesh();
        mesh.ClearSurfaces();
        if (_terrainOverlaySurface.IndexCount != 0)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _terrainOverlaySurface.ToArrays(), null, null,
                TerrainStateCustomFormat);
            mesh.SurfaceSetMaterial(0, TerrainOverlayMaterial());
        }
        if (instance != null) return;
        instance = new MeshInstance3D
        {
            Name = "TerrainOverlay" + chunk,
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(instance);
        _terrainOverlayChunks[chunk] = instance;
    }
}
