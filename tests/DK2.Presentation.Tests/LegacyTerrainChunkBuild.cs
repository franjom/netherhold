// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The Unity adapter's per-tile terrain build as it stood at 5afa63b7 (TerrainChunks.Prepare, Build, AddTile,
/// AddRoom, AddHeroFaces, AddBed, AddSet and Add), transcribed line for line before it moved to
/// <see cref="TerrainTileBuilder"/>, so the move is pinned against the code it replaced. UnityEngine's types are
/// replaced by their own float formulas (<see cref="UnityTurn"/>) and ClassicMeshes' material twins by
/// <see cref="Materials"/>, which keeps their caching and their additive and zero-tint short cuts.</summary>
internal sealed class LegacyTerrainChunkBuild
{
    private static readonly Vector2[] QuadrantCentres =
    {
        new(-0.25f, 0.25f), new(0.25f, 0.25f), new(0.25f, -0.25f), new(-0.25f, -0.25f),
    };

    private static readonly UnityTurn[] Turns =
    {
        UnityTurn.Euler(0f), UnityTurn.Euler(90f), UnityTurn.Euler(180f), UnityTurn.Euler(270f),
    };

    private static readonly int[] SideDx = { 0, 1, 0, -1 }, SideDy = { -1, 0, 1, 0 };

    private readonly ITerrainPieceSource _pieces;
    private readonly Materials _classic = new();
    private readonly TerrainLattice? _lattice;
    private readonly int _width;
    private readonly TileView[] _tiles;

    public LegacyTerrainChunkBuild(ITerrainPieceSource pieces, TileView[] tiles, int width, TerrainLattice? lattice)
    {
        _pieces = pieces;
        _tiles = tiles;
        _width = width;
        _lattice = lattice;
    }

    /// <summary>Unity's <c>Quaternion.Euler(0, degrees, 0)</c> and its <c>operator *(Quaternion, Vector3)</c>.</summary>
    internal readonly struct UnityTurn
    {
        private readonly float _y, _w;

        private UnityTurn(float y, float w) { _y = y; _w = w; }

        public static UnityTurn Euler(float degrees)
        {
            float half = degrees * (MathF.PI / 180f) * 0.5f;
            return new UnityTurn(MathF.Sin(half), MathF.Cos(half));
        }

        public Vector3 Apply(Vector3 point)
        {
            float num2 = _y * 2f;
            float num5 = _y * num2;
            float num11 = _w * num2;
            return new Vector3((1f - num5) * point.X + num11 * point.Z, point.Y, -num11 * point.X + (1f - num5) * point.Z);
        }
    }

    /// <summary>A material ClassicMeshes would have made, named by the key it stands for.</summary>
    internal sealed class Material
    {
        public Material(TerrainMaterialKey key) => Key = key;
        public TerrainMaterialKey Key { get; }
    }

    /// <summary>ClassicMeshes' ForTexture, Tagged, TerrainTinted, SightFog and TagOverlay: one material per input,
    /// cached; an additive (Unlit, no _EmissionColor) material is its own tagged and tinted twin.</summary>
    internal sealed class Materials
    {
        private readonly Dictionary<object, Material> _cache = new();

        public Material ForTexture(ClassicMaterialRecipe recipe) =>
            Cached(("base", recipe), () => new Material(new TerrainMaterialKey(recipe, TerrainMaterialVariant.Plain)));

        public Material Tagged(Material material, bool red = false)
        {
            if (material.Key.Recipe.Blend == KmfBlend.Additive)
            {
                return material;
            }

            return Cached(("tag", material, red), () => new Material(material.Key with
            {
                Variant = red ? TerrainMaterialVariant.Red : TerrainMaterialVariant.Tagged,
            }));
        }

        public Material TerrainTinted(Material material, TerrainLightColour tint)
        {
            tint = ClassicTerrainTint.PieceColour(tint);
            if (tint == default || material.Key.Recipe.Blend == KmfBlend.Additive)
            {
                return material;
            }

            return Cached(("tint", material, tint), () => new Material(material.Key with
            {
                Variant = TerrainMaterialVariant.Tinted, Tint = tint,
            }));
        }

        public Material SightFog(Material source, byte alpha) => Cached(("fog", source, alpha), () => new Material(
            source.Key with { Variant = TerrainMaterialVariant.Fog, FogOpacity = alpha }));

        public Material TagOverlay(bool red = false) =>
            Cached(("overlay", red), () => new Material(TerrainMaterialKey.Overlay(red)));

        private Material Cached(object key, Func<Material> make)
        {
            if (!_cache.TryGetValue(key, out Material? material))
            {
                _cache[key] = material = make();
            }

            return material;
        }
    }

    internal sealed class PieceSource
    {
        public PieceSource(TerrainPiece piece, Material[] materials) { Piece = piece; Materials = materials; }
        public TerrainPiece Piece { get; }
        public Vector3[] Positions => Piece.Positions;
        public Vector3[] Normals => Piece.Normals;
        public Vector2[] Uvs => Piece.Uvs;
        public IReadOnlyList<int[]> Triangles => Piece.Triangles;
        public Material[] Materials { get; }
    }

    internal sealed class TileGeometry
    {
        public readonly List<PlacedPiece> Pieces = new();
        public readonly List<TriangleGroup> Groups = new();
        public Material[] Materials = new Material[4];
        public int MaterialCount;
        public Vector2 LightAnchor;
        public int LatticeX, LatticeY;
        public Vector3[] Positions = new Vector3[256], Normals = new Vector3[256];
        public Vector2[] Uvs = new Vector2[256];
        public int VertexCount;
        public List<int>[] Triangles = new List<int>[4];
        public int[] PieceStarts = new int[8];

        public int SlotOf(Material material)
        {
            for (int i = 0; i < MaterialCount; i++)
            {
                if (ReferenceEquals(Materials[i], material))
                {
                    return i;
                }
            }

            if (MaterialCount == Materials.Length)
            {
                Array.Resize(ref Materials, MaterialCount * 2);
                Array.Resize(ref Triangles, MaterialCount * 2);
            }
            Materials[MaterialCount] = material;
            Triangles[MaterialCount] ??= new List<int>();
            return MaterialCount++;
        }
    }

    internal struct PlacedPiece
    {
        public PieceSource Piece;
        public int Rotation;
        public Vector3 At;
        public bool InvertNormals;
    }

    internal struct TriangleGroup
    {
        public int Piece, Group, Slot;
    }

    private static Vector3 Ground(float x, float y, float height = 0f) => new(x, height, -y);

    private PieceSource? Piece(AssetRef key, int textureIndex = -1)
    {
        TerrainPiece? piece = _pieces.Piece(key, textureIndex);
        if (piece == null)
        {
            return null;
        }

        var materials = new Material[piece.Triangles.Count];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = _classic.ForTexture(piece.Recipes[i]);
        }

        return new PieceSource(piece, materials);
    }

    public void Prepare(int x, int y, in TileView tile, TileGeometry geometry)
    {
        geometry.Pieces.Clear();
        geometry.Groups.Clear();
        geometry.MaterialCount = 0;
        geometry.LightAnchor = new Vector2(y * _width + x, 1);
        geometry.LatticeX = x;
        geometry.LatticeY = y;
        AddTile(geometry, x, y, tile);
        if (!tile.SightFogOverlay.IsEmpty)
        {
            Add(geometry, Piece(tile.SightFogOverlay), 0, Ground(x + .5f, y + .5f),
                x, y, fogOpacity: tile.SightFogOpacity);
        }

        _lattice?.Prepare(x, y);
    }

    public void Build(TileGeometry geometry)
    {
        geometry.VertexCount = 0;
        for (int s = 0; s < geometry.MaterialCount; s++)
        {
            geometry.Triangles[s].Clear();
        }

        if (geometry.PieceStarts.Length < geometry.Pieces.Count)
        {
            geometry.PieceStarts = new int[geometry.Pieces.Count * 2];
        }

        int x = geometry.LatticeX, y = geometry.LatticeY;
        for (int p = 0; p < geometry.Pieces.Count; p++)
        {
            PlacedPiece placed = geometry.Pieces[p];
            PieceSource piece = placed.Piece;
            UnityTurn turn = Turns[placed.Rotation & 3];
            int start = geometry.VertexCount;
            geometry.PieceStarts[p] = start;
            int needed = start + piece.Positions.Length;
            if (geometry.Positions.Length < needed)
            {
                int size = Math.Max(needed, geometry.Positions.Length * 2);
                Array.Resize(ref geometry.Positions, size);
                Array.Resize(ref geometry.Normals, size);
                Array.Resize(ref geometry.Uvs, size);
            }
            for (int i = 0; i < piece.Positions.Length; i++)
            {
                Vector3 v = turn.Apply(piece.Positions[i]) + placed.At;
                if (_lattice != null)
                {
                    Vector3 bent = _lattice.Deform(new Vector3(v.X, -v.Z, -v.Y), x, y);
                    v = new Vector3(bent.X, -bent.Z, -bent.Y);
                }
                geometry.Positions[start + i] = v;
                Vector3 normal = placed.InvertNormals ? -piece.Normals[i] : piece.Normals[i];
                geometry.Normals[start + i] = turn.Apply(normal);
                geometry.Uvs[start + i] = piece.Uvs[i];
            }
            geometry.VertexCount = needed;
        }
        for (int g = 0; g < geometry.Groups.Count; g++)
        {
            TriangleGroup group = geometry.Groups[g];
            int start = geometry.PieceStarts[group.Piece];
            List<int> list = geometry.Triangles[group.Slot];
            foreach (int index in geometry.Pieces[group.Piece].Piece.Triangles[group.Group])
            {
                list.Add(start + index);
            }
        }
    }

    private void AddTile(TileGeometry geometry, int x, int y, in TileView tile)
    {
        if (tile.BedKeys != null)
        {
            AddBed(geometry, x, y, tile);
        }

        if (tile.RoomKeys != null)
        {
            AddRoom(geometry, x, y, tile);
            return;
        }

        TerrainPieces pieces = tile.Pieces;
        TerrainPieceKeys keys = tile.PieceKeys!;
        Vector3 centre = Ground(x + 0.5f, y + 0.5f);

        bool tagged = tile.Tagged;
        for (int side = 0; side < 4; side++)
        {
            if (pieces.HasFace(side))
            {
                Add(geometry, Piece(keys.Side, tile.FaceTexture), side, centre, x, y, tagged, tile.AmbientTint);
            }
        }

        if (pieces.HasTop)
        {
            Add(geometry, Piece(keys.Top), 0, centre, x, y, tagged, tile.AmbientTint,
            invertLightingNormals: true);
        }

        if (pieces.HasFloor)
        {
            Add(geometry, Piece(keys.Floor), 0, centre, x, y, tagged, tile.AmbientTint);
        }

        AddHeroFaces(geometry, x, y, tile, keys, centre, tagged);
        if (pieces.Quarters == QuarterKind.None)
        {
            return;
        }

        for (int q = 0; q < 4; q++)
        {
            Vector2 offset = QuadrantCentres[q];
            Add(geometry, Piece(keys.Quarter(pieces.QuarterSet, pieces.QuarterPiece(q))),
                TerrainPieces.QuarterRotation(q), centre + new Vector3(offset.X, 0f, offset.Y), x, y, tagged, tile.AmbientTint,
                invertLightingNormals: pieces.Quarters == QuarterKind.Top);
        }
    }

    private void AddRoom(TileGeometry geometry, int x, int y, in TileView tile)
    {
        RoomPieceKeys keys = tile.RoomKeys!;
        Vector3 centre = Ground(x + 0.5f, y + 0.5f);
        AddSet(geometry, tile.RoomFloor, keys, null, centre, x, y, tile.AmbientTint, tile.QuarterAmbientTint);
        if (!tile.ExtraRoomPiece.IsEmpty)
        {
            Add(geometry, Piece(tile.ExtraRoomPiece), 0, centre, x, y, tint: tile.AmbientTint);
        }

        RoomWalls walls = tile.Walls;
        for (int side = 0; side < 4; side++)
        {
            bool tagged = FacesTag(x, y, side);
            TerrainLightColour tint = walls.IsWhole(side) && walls.WholePiece(side) < 7
                ? default : NeighbourTint(x, y, side);
            if (walls.IsWhole(side))
            {
                Add(geometry, Piece(keys.Piece(walls.WholePiece(side))), walls.WholeRotation(side), centre, x, y, tagged, tint);
            }
            else if (walls.HasWall(side))
            {
                Add(geometry, Piece(keys.Piece(walls.HalfAPiece(side))), side, Quadrant(centre, side), x, y, tagged, tint);
                Add(geometry, Piece(keys.Piece(walls.HalfBPiece(side))), side, Quadrant(centre, (side + 1) & 3), x, y,
                    tagged, tint);
            }
        }
    }

    private void AddHeroFaces(TileGeometry geometry, int x, int y, in TileView tile, TerrainPieceKeys keys, Vector3 centre, bool tagged)
    {
        RoomWalls faces = tile.Walls;
        if (faces.IsEmpty)
        {
            return;
        }

        for (int side = 0; side < 4; side++)
        {
            if (faces.IsWhole(side))
            {
                Add(geometry, Piece(keys.HeroFace(faces.WholePiece(side))), side, centre, x, y, tagged, tile.AmbientTint);
            }
            else if (faces.HasWall(side))
            {
                Add(geometry, Piece(keys.HeroFace(SwapHeroHalf(faces.HalfAPiece(side)))), side,
                    Quadrant(centre, side), x, y, tagged, tile.AmbientTint);
                Add(geometry, Piece(keys.HeroFace(SwapHeroHalf(faces.HalfBPiece(side)))), side,
                    Quadrant(centre, (side + 1) & 3), x, y, tagged, tile.AmbientTint);
            }
        }
    }

    private static int SwapHeroHalf(int piece) => piece == 4 ? 5 : piece == 5 ? 4 : piece;

    private void AddBed(TileGeometry geometry, int x, int y, in TileView tile)
    {
        TerrainPieceKeys keys = tile.BedKeys!;
        Vector3 centre = Ground(x + 0.5f, y + 0.5f);
        AddSet(geometry, tile.Bed, null, keys, centre, x, y);
    }

    private void AddSet(TileGeometry geometry, PieceSet set, RoomPieceKeys? room, TerrainPieceKeys? bed, Vector3 centre, int x, int y,
        TerrainLightColour wholeTint = default, TerrainLightColour quarterTint = default)
    {
        if (set.HasQuarters)
        {
            for (int q = 0; q < 4; q++)
            {
                if (set.HasQuarter(q))
                {
                    Add(geometry, Piece(Key(room, bed, set.QuarterPiece(q))), set.QuarterRotation(q),
                        Quadrant(centre, q), x, y, tint: quarterTint);
                }
            }
        }

        if (set.HasWhole)
        {
            Add(geometry, Piece(Key(room, bed, set.WholePiece)), set.WholeRotation, centre, x, y,
            tint: wholeTint);
        }
    }

    private bool FacesTag(int x, int y, int side)
    {
        int nx = x + SideDx[side], ny = y + SideDy[side];
        return nx >= 0 && ny >= 0 && nx < _width && ny < _tiles.Length / _width && _tiles[(ny * _width) + nx].Tagged;
    }

    private TerrainLightColour NeighbourTint(int x, int y, int side)
    {
        int nx = x + SideDx[side], ny = y + SideDy[side];
        return nx >= 0 && ny >= 0 && nx < _width && ny < _tiles.Length / _width
            ? _tiles[ny * _width + nx].AmbientTint : default;
    }

    private static AssetRef Key(RoomPieceKeys? room, TerrainPieceKeys? bed, int k) =>
        room != null ? room.Piece(k) : bed!.Bed(k);

    private static Vector3 Quadrant(Vector3 centre, int quadrant) =>
        centre + new Vector3(QuadrantCentres[quadrant].X, 0f, QuadrantCentres[quadrant].Y);

    private void Add(TileGeometry geometry, PieceSource? piece, int rotation, Vector3 at, int x, int y, bool tagged = false,
        TerrainLightColour tint = default, byte? fogOpacity = null, bool invertLightingNormals = false)
    {
        if (piece == null)
        {
            return;
        }

        bool red = _tiles[y * _width + x].RedHighlighted;
        int placed = geometry.Pieces.Count;
        geometry.Pieces.Add(new PlacedPiece { Piece = piece, Rotation = rotation, At = at, InvertNormals = invertLightingNormals });
        for (int m = 0; m < piece.Triangles.Count; m++)
        {
            Material material = fogOpacity is byte opacity ? _classic.SightFog(piece.Materials[m], opacity)
                : red ? _classic.Tagged(piece.Materials[m], red: true)
                : tagged ? _classic.Tagged(piece.Materials[m])
                : _classic.TerrainTinted(piece.Materials[m], tint);
            geometry.Groups.Add(new TriangleGroup { Piece = placed, Group = m, Slot = geometry.SlotOf(material) });
            if (red || tagged)
            {
                geometry.Groups.Add(new TriangleGroup { Piece = placed, Group = m, Slot = geometry.SlotOf(_classic.TagOverlay(red)) });
            }
        }
    }
}
