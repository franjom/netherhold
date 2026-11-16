// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>One liquid's surface in the classic look (room-floors-and-liquids.md section 6): each tile of that liquid a
/// 3x3 vertex grid in the renderers' frame, waved as DK2's sine mode waves it. The buffers are the surface's own and
/// are refilled in place, so a wave allocates nothing; <see cref="VertexCount"/> and <see cref="IndexCount"/> of them
/// are in use.</summary>
/// <remarks>Water rests at 0.9, lava at 0.85 (<c>0x005A2570</c>, <c>0x005A2BE0</c>), moved by a sine along the row plus
/// one along the column: water rises by it, lava sinks. Chosen or not built: Sine Wave on, Translucent Water off (SOLID
/// textures), opaque, water's first frame only, one texture repeat a tile, DK2's frame counter at 30 a second, normals
/// from the faces (DK2's are analytic), no lattice bend, and no fog gate of its own (<c>0x0043D187</c>).</remarks>
public sealed class LiquidSurfaceWave
{
    public const float WaterRest = 0.9f, WaterAmplitude = 0.03f;
    public const float LavaRest = 0.85f, LavaAmplitude = 0.04f;
    public const float FramesPerSecond = 30f;
    public const int VerticesPerTile = 9, IndicesPerTile = 24;
    public const string WaterTexture = "WaterSOLID0", LavaTexture = "Lava0SOLID";

    private readonly int _width;
    private int[] _tiles = new int[16];
    private Vector3[] _positions = new Vector3[16 * VerticesPerTile], _normals = new Vector3[16 * VerticesPerTile];
    private Vector2[] _uvs = new Vector2[16 * VerticesPerTile], _lightAnchors = new Vector2[16 * VerticesPerTile];
    private int[] _triangles = new int[16 * IndicesPerTile];

    public LiquidSurfaceWave(LiquidSurface kind, int width)
    {
        if (kind == LiquidSurface.None) throw new ArgumentOutOfRangeException(nameof(kind));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Kind = kind;
        _width = width;
    }

    public LiquidSurface Kind { get; }

    /// <summary>The engine texture the surface draws with, opaque.</summary>
    public string Texture => Kind == LiquidSurface.Water ? WaterTexture : LavaTexture;

    public int TileCount { get; private set; }
    public int VertexCount => TileCount * VerticesPerTile;
    public int IndexCount => TileCount * IndicesPerTile;

    /// <summary>Bumped by every <see cref="SetTiles"/>: the tiles, the UVs, the light anchors and the triangles are
    /// new, where a <see cref="Wave"/> rewrites only positions and normals.</summary>
    public int Version { get; private set; }

    public Vector3[] Positions => _positions;
    public Vector3[] Normals => _normals;

    /// <summary>Half a texture a vertex step; V falls to the south, as the terrain pieces' V.</summary>
    public Vector2[] Uvs => _uvs;

    /// <summary>The light table each vertex reads: its tile's index and 1, the terrain kind.</summary>
    public Vector2[] LightAnchors => _lightAnchors;

    /// <summary>Clockwise from above, two triangles a quad: north-west, north-east, south-east; then south-west.</summary>
    public int[] Triangles => _triangles;

    /// <summary>The tile index of each surface tile, in index order.</summary>
    public ReadOnlySpan<int> Tiles => new(_tiles, 0, TileCount);

    /// <summary>DK2's frame counter at <paramref name="seconds"/>.</summary>
    public static int Frame(float seconds) => (int)(seconds * FramesPerSecond);

    /// <summary>The sine of one axis at vertex step <paramref name="vertex"/> (0 to 2) of tile coordinate
    /// <paramref name="tile"/>: water's ((tile*0x10000 + vertex*0x8000 + 4096*frame) &amp; 0x1FFFF)*pi/65536, lava's
    /// ((vertex*32 - tile*64 + frame) &amp; 127)*pi/64; two tiles a period.</summary>
    public static float Phase(LiquidSurface kind, int tile, int vertex, int frame) =>
        kind == LiquidSurface.Water
            ? (float)Math.Sin((((tile * 0x10000) + (vertex * 0x8000) + (4096 * frame)) & 0x1FFFF) * (float)Math.PI / 65536f)
            : (float)Math.Sin(((((vertex * 32) - (tile * 64)) + frame) & 127) * (float)Math.PI / 64f);

    /// <summary>The surface's height above the floor at a vertex whose row and column phases are given: DK2's z (up is
    /// -z) is -0.9 - 0.03*s for water and -0.85 + 0.04*s for lava.</summary>
    public static float Height(LiquidSurface kind, float row, float column) =>
        kind == LiquidSurface.Water
            ? WaterRest + (WaterAmplitude * (row + column))
            : LavaRest - (LavaAmplitude * (row + column));

    /// <summary>Takes every tile of <paramref name="kinds"/> (a surface kind per tile index) that is this liquid, lays
    /// their grids out and waves them at frame 0.</summary>
    public void SetTiles(ReadOnlySpan<LiquidSurface> kinds)
    {
        int count = 0;
        for (int i = 0; i < kinds.Length; i++)
            if (kinds[i] == Kind) count++;
        if (_tiles.Length < count)
        {
            int size = Math.Max(count, _tiles.Length * 2);
            _tiles = new int[size];
            _positions = new Vector3[size * VerticesPerTile];
            _normals = new Vector3[size * VerticesPerTile];
            _uvs = new Vector2[size * VerticesPerTile];
            _lightAnchors = new Vector2[size * VerticesPerTile];
            _triangles = new int[size * IndicesPerTile];
        }
        TileCount = 0;
        for (int i = 0; i < kinds.Length; i++)
            if (kinds[i] == Kind) _tiles[TileCount++] = i;

        for (int k = 0; k < TileCount; k++)
        {
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                {
                    int vertex = (k * VerticesPerTile) + (j * 3) + i;
                    _uvs[vertex] = new Vector2(i * 0.5f, 1f - (j * 0.5f));
                    _lightAnchors[vertex] = new Vector2(_tiles[k], 1);
                }
            int n = k * IndicesPerTile;
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    int a = (k * VerticesPerTile) + (j * 3) + i, b = a + 1, c = a + 3, d = a + 4;
                    _triangles[n++] = a; _triangles[n++] = b; _triangles[n++] = d;
                    _triangles[n++] = a; _triangles[n++] = d; _triangles[n++] = c;
                }
        }
        Version++;
        Wave(0);
    }

    /// <summary>Moves every vertex to its height at DK2 frame <paramref name="frame"/> and sets each normal to the
    /// normalised sum of the unit normals of the triangles that share it.</summary>
    public void Wave(int frame)
    {
        for (int k = 0; k < TileCount; k++)
        {
            int x = _tiles[k] % _width, y = _tiles[k] / _width, first = k * VerticesPerTile;
            for (int j = 0; j < 3; j++)
            {
                float row = Phase(Kind, y, j, frame);
                for (int i = 0; i < 3; i++)
                    _positions[first + (j * 3) + i] = RenderWorld.Ground(x + (i * 0.5f), y + (j * 0.5f),
                        Height(Kind, row, Phase(Kind, x, i, frame)));
            }
            for (int v = 0; v < VerticesPerTile; v++) _normals[first + v] = Vector3.Zero;
            for (int t = k * IndicesPerTile; t < (k + 1) * IndicesPerTile; t += 3)
            {
                int a = _triangles[t], b = _triangles[t + 1], c = _triangles[t + 2];
                Vector3 face = Vector3.Normalize(Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]));
                _normals[a] += face;
                _normals[b] += face;
                _normals[c] += face;
            }
            for (int v = 0; v < VerticesPerTile; v++) _normals[first + v] = Vector3.Normalize(_normals[first + v]);
        }
    }
}

/// <summary>The water and lava surfaces of a map, from the pushed tiles' <see cref="TileView.Surface"/>: a layer whose
/// tiles changed is laid out again at the next <see cref="Animate"/>, and both are waved.</summary>
public sealed class LiquidSurfaceField
{
    private readonly LiquidSurface[] _kinds;
    private bool _dirty;

    public LiquidSurfaceField(int width, int height)
    {
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Water = new LiquidSurfaceWave(LiquidSurface.Water, width);
        Lava = new LiquidSurfaceWave(LiquidSurface.Lava, width);
        _kinds = new LiquidSurface[width * height];
    }

    public LiquidSurfaceWave Water { get; }
    public LiquidSurfaceWave Lava { get; }

    public void Set(int tileIndex, in TileView tile)
    {
        if (_kinds[tileIndex] == tile.Surface) return;
        _kinds[tileIndex] = tile.Surface;
        _dirty = true;
    }

    /// <summary>Lays both layers out again if a tile changed, then waves them at DK2's frame for
    /// <paramref name="seconds"/>.</summary>
    public void Animate(float seconds)
    {
        if (_dirty)
        {
            Water.SetTiles(_kinds);
            Lava.SetTiles(_kinds);
            _dirty = false;
        }
        int frame = LiquidSurfaceWave.Frame(seconds);
        Water.Wave(frame);
        Lava.Wave(frame);
    }

    public void Clear()
    {
        Array.Clear(_kinds, 0, _kinds.Length);
        Water.SetTiles(_kinds);
        Lava.SetTiles(_kinds);
        _dirty = false;
    }
}
