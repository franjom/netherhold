// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
namespace DK2.Presentation;
/// <summary>The possession camera's scaled view polygon, occlusion walk and on-map draw mask
/// (possession-warp.md section 8). A host uploads the mask and draws every tile and its contents from it.</summary>
public sealed class PossessionVisibilityField
{
    private readonly int _width, _height;
    private readonly byte[] _mask;
    private readonly bool[] _solid;
    private readonly PossessionVisibility _walk = new();
    private readonly ClassicVisibilityPolygonBuffer _polygon = new();
    private readonly List<TilePos> _tiles = new(1024);
    private TilePos[] _polygonTiles = [];
    public PossessionVisibilityField(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        (_width, _height) = (width, height);
        _mask = new byte[checked(width * height)];
        _solid = new bool[_mask.Length];
    }
    /// <summary>One byte per map tile, row by row: 255 draws it, 0 leaves it out.</summary>
    public ReadOnlySpan<byte> Mask => _mask;
    public bool Contains(TilePos tile) => (uint)tile.X < (uint)_width && (uint)tile.Y < (uint)_height
        && _mask[(tile.Y * _width) + tile.X] != 0;
    /// <summary>Builds from the world's solid terrain. Doors, pillars, liquids and other things are not blockers.</summary>
    public void Build(WorldState world, GameDatabase database, in ClassicCameraPose pose, CameraViewport viewport, float viewDistance)
    {
        if (world.Width != _width || world.Height != _height) throw new ArgumentException("The world must match the draw mask.", nameof(world));
        ReadOnlySpan<TileState> tiles = world.Tiles;
        for (int i = 0; i < _solid.Length; i++) _solid[i] = database.TerrainById(tiles[i].TerrainId).IsSolid;
        Build(pose, viewport, viewDistance, _solid);
    }
    /// <summary>Rebuilds the set from this frame's pose. Only solid terrain blocks; a zero-distance death view draws nothing.</summary>
    public void Build(in ClassicCameraPose pose, CameraViewport viewport, float viewDistance, bool[] solid)
    {
        if (solid.Length != _mask.Length) throw new ArgumentException("The solid map must match the draw mask.", nameof(solid));
        Array.Clear(_mask, 0, _mask.Length);
        if (viewDistance <= 0) return;
        int boundaryCount = ClassicLightVisibility.BuildPolygon(pose, viewport, Math.Min(viewDistance, PossessionFade.PossessionCap),
            _polygon, scaleViewVolume: true);
        int count = ClassicLightVisibility.EnumerateTiles(_polygon.Tiles.AsSpan(0, boundaryCount), ref _polygonTiles);
        _walk.Walk(pose.Position.X, -pose.Position.Z, _width, _height, solid, _polygonTiles.AsSpan(0, count), _tiles);
        foreach (TilePos tile in _tiles)
            if ((uint)tile.X < (uint)_width && (uint)tile.Y < (uint)_height) _mask[(tile.Y * _width) + tile.X] = 255;
    }
}
