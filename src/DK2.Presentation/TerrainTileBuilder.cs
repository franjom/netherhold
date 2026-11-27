// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Assets;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The classic terrain of one tile: plain <see cref="TerrainPieces"/>, a room's floor and
/// <see cref="RoomWalls"/>, a hero wall's faces and a liquid's bed, in the renderers' frame.</summary>
/// <remarks>
/// A piece is turned a quarter per step about its origin, clockwise from above (side N 0 to E 1, as DK2's side table
/// runs), then moved to the tile's centre and, for a quarter, its quadrant's. Heights are the models' own: floor 1, walls
/// 1 to 2 (ADR-0411). A <see cref="TerrainLattice"/> then bends each vertex as DK2 does, keeping the straight normals
/// (terrain-lattice.md §1). <see cref="Prepare"/> reads the tiles and runs on the world's thread; <see cref="Build"/> is
/// arrays and arithmetic only, so any thread may run it (ADR-0696). Neither allocates once a tile's buffers have grown.
/// </remarks>
public sealed class TerrainTileBuilder
{
    // Quadrant centres NW, NE, SE, SW in renderer X (east) and Z (north).
    private static readonly Vector2[] QuadrantCentres =
    [
        new(-0.25f, 0.25f), new(0.25f, 0.25f), new(0.25f, -0.25f), new(-0.25f, -0.25f),
    ];

    private static readonly int[] SideDx = [0, 1, 0, -1], SideDy = [-1, 0, 1, 0];

    private readonly ITerrainPieceSource _pieces;
    private readonly TerrainLattice? _lattice;
    private readonly int _width, _height;
    private readonly bool _stateTexture;

    /// <param name="lattice">Bends the terrain as DK2 does; null draws it straight.</param>
    public TerrainTileBuilder(ITerrainPieceSource pieces, int width, int height, TerrainLattice? lattice = null)
        : this(pieces, width, height, lattice, false) { }

    public TerrainTileBuilder(ITerrainPieceSource pieces, int width, int height, TerrainLattice? lattice, bool stateTexture)
    {
        _stateTexture = stateTexture;
        _pieces = pieces ?? throw new ArgumentNullException(nameof(pieces));
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        _width = width;
        _height = height;
        _lattice = lattice;
    }

    /// <summary>Whether a tile draws terrain here at all.</summary>
    public static bool Draws(in TileView tile)
    {
        return (!tile.Pieces.IsEmpty && tile.PieceKeys != null) || tile.RoomKeys != null || tile.BedKeys != null
        || !tile.SightFogOverlay.IsEmpty;
    }

    /// <summary>Phase one of tile (<paramref name="x"/>, <paramref name="y"/>): its pieces placed and each triangle
    /// group's <see cref="TerrainMaterialKey"/> chosen, in the tile's first-use order, and the lattice cells the bend
    /// reads built. <paramref name="tiles"/> is the map, row by row; the neighbours' tag and tint reach a room's walls.</summary>
    public void Prepare(int x, int y, TileView[] tiles, TerrainTileGeometry geometry)
    {
        if (tiles is null)
        {
            throw new ArgumentNullException(nameof(tiles));
        }

        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        ref readonly TileView tile = ref tiles[(y * _width) + x];
        geometry.Reset(x, y, _width, tile.RedHighlighted);
        geometry.StateTexture = _stateTexture;
        geometry.DefaultStateSource = new Vector2(y * _width + x);
        AddTile(geometry, tiles, x, y, in tile);
        if (!tile.SightFogOverlay.IsEmpty)
        {
            Add(geometry, _pieces.Piece(tile.SightFogOverlay), 0, RenderWorld.Ground(x + .5f, y + .5f),
                fogOpacity: tile.SightFogOpacity, stateSource: new Vector2(-1));
        }

        _lattice?.Prepare(x, y);
    }

    /// <summary>Phase one for <paramref name="piece"/> alone at the origin of tile (0, 0), neither red nor fogged,
    /// for a material probe.</summary>
    public void PrepareSinglePiece(TerrainTileGeometry geometry, TerrainPiece piece, bool tagged, TerrainLightColour tint)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        geometry.Reset(0, 0, _width, red: false);
        geometry.StateTexture = _stateTexture;
        geometry.DefaultStateSource = Vector2.Zero;
        Add(geometry, piece, 0, Vector3.Zero, tagged, tint);
    }

    /// <summary>Phase two: the vertices turned, placed and bent, and the triangles gathered per key slot, numbered
    /// from the tile's first vertex. The lattice cells it reads were built by <see cref="Prepare"/>.</summary>
    public void Build(TerrainTileGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        geometry.VertexCount = 0;
        for (int s = 0; s < geometry.KeyCount; s++)
        {
            geometry.Triangles[s].Count = 0;
        }

        List<PlacedPiece> pieces = geometry.Pieces;
        if (geometry.PieceStarts.Length < pieces.Count)
        {
            geometry.PieceStarts = new int[pieces.Count * 2];
        }

        int x = geometry.X, y = geometry.Y;
        for (int p = 0; p < pieces.Count; p++)
        {
            PlacedPiece placed = pieces[p];
            TerrainPiece piece = placed.Piece;
            int start = geometry.VertexCount;
            geometry.PieceStarts[p] = start;
            int needed = start + piece.Positions.Length;
            geometry.EnsureVertices(needed);
            for (int i = 0; i < piece.Positions.Length; i++)
            {
                Vector3 v = Turn(piece.Positions[i], placed.Rotation) + placed.At;
                if (_lattice != null)
                {
                    // DK2's frame is (x, -z, -y) of this one (RenderWorld.Ground, RenderMesh).
                    Vector3 bent = _lattice.Deform(new Vector3(v.X, -v.Z, -v.Y), x, y);
                    v = new Vector3(bent.X, -bent.Z, -bent.Y);
                }
                geometry.Positions[start + i] = v;
                // Ordinary tops carry native drawflag0x80 (static-mesh-lighting.md section1).
                Vector3 normal = placed.InvertNormals ? -piece.Normals[i] : piece.Normals[i];
                geometry.Normals[start + i] = Turn(normal, placed.Rotation);
                geometry.Uvs[start + i] = piece.Uvs[i];
                geometry.StateSources[start + i] = placed.StateSource;
            }
            geometry.VertexCount = needed;
        }
        List<TriangleGroup> groups = geometry.Groups;
        for (int g = 0; g < groups.Count; g++)
        {
            TriangleGroup group = groups[g];
            int start = geometry.PieceStarts[group.Piece];
            TerrainIndexList list = geometry.Triangles[group.Slot];
            int[] source = pieces[group.Piece].Piece.Triangles[group.Group];
            for (int i = 0; i < source.Length; i++)
            {
                list.Add(start + source[i]);
            }
        }
    }

    /// <summary>A quarter turn per step about the vertical, clockwise from above: north (0, 0, 1) to east (1, 0, 0).</summary>
    internal static Vector3 Turn(Vector3 v, int rotation)
    {
        return (rotation & 3) switch
        {
            0 => v,
            1 => new Vector3(v.Z, v.Y, -v.X),
            2 => new Vector3(-v.X, v.Y, -v.Z),
            _ => new Vector3(-v.Z, v.Y, v.X),
        };
    }

    private void AddTile(TerrainTileGeometry geometry, TileView[] tiles, int x, int y, in TileView tile)
    {
        if (tile.BedKeys != null)
        {
            geometry.DefaultStateSource = new Vector2(-1);
            AddSet(geometry, tile.Bed, null, tile.BedKeys, Centre(x, y));
            geometry.DefaultStateSource = new Vector2(y * _width + x);
        }
        if (tile.RoomKeys != null)
        {
            AddRoom(geometry, tiles, x, y, tile);
            return;
        }

        TerrainPieces pieces = tile.Pieces;
        TerrainPieceKeys? keys = tile.PieceKeys;
        if (keys is null)
        {
            return;
        }

        Vector3 centre = Centre(x, y);

        // A Reinforced Wall's face takes the texture of its damage stage (Brick_DECAY3 to Brick,
        // tile.FaceTexture; Q617). A tagged tile tints every piece it draws (tag-highlight.md).
        bool tagged = tile.Tagged;
        for (int side = 0; side < 4; side++)
        {
            if (pieces.HasFace(side))
            {
                Add(geometry, _pieces.Piece(keys.Side, tile.FaceTexture), side, centre, tagged, tile.AmbientTint);
            }
        }

        if (pieces.HasTop)
        {
            Add(geometry, _pieces.Piece(keys.Top), 0, centre, tagged, tile.AmbientTint, invertLightingNormals: true);
        }

        if (pieces.HasFloor)
        {
            Add(geometry, _pieces.Piece(keys.Floor), 0, centre, tagged, tile.AmbientTint);
        }

        AddHeroFaces(geometry, tile, keys, centre, tagged);
        if (pieces.Quarters == QuarterKind.None)
        {
            return;
        }

        for (int q = 0; q < 4; q++)
        {
            Add(geometry, _pieces.Piece(pieces.Quarters == QuarterKind.HeroLair
                    ? keys.HeroLairQuarter(pieces.QuarterPiece(q))
                    : keys.Quarter(pieces.QuarterSet, pieces.QuarterPiece(q))),
                TerrainPieces.QuarterRotation(q), Quadrant(centre, q), tagged, tile.AmbientTint,
                invertLightingNormals: pieces.Quarters == QuarterKind.Top);
        }
    }

    /// <summary>A room tile: its floor's quarters and whole piece, then each side's wall, whole at the centre or as two
    /// halves in the side's two quadrants, all turned to the side.</summary>
    private void AddRoom(TerrainTileGeometry geometry, TileView[] tiles, int x, int y, in TileView tile)
    {
        RoomPieceKeys keys = tile.RoomKeys!;
        geometry.DefaultStateSource = new Vector2(-1, y * _width + x);
        Vector3 centre = Centre(x, y);
        AddSet(geometry, tile.RoomFloor, keys, null, centre, tile.AmbientTint, tile.QuarterAmbientTint);
        if (!tile.ExtraRoomPiece.IsEmpty)
        {
            Add(geometry, _pieces.Piece(tile.ExtraRoomPiece), 0, centre, tint: tile.AmbientTint);
        }

        // A wall facing a tagged tile is tinted with it (0x00444F10's per-piece flag, which 0x0043C310 reads).
        RoomWalls walls = tile.Walls;
        for (int side = 0; side < 4; side++)
        {
            bool tagged = FacesTag(tiles, x, y, side);
            int nx = x + SideDx[side], ny = y + SideDy[side];
            int neighbour = nx >= 0 && ny >= 0 && nx < _width && ny < _height ? ny * _width + nx : -1;
            Vector2 source = new(neighbour, walls.IsWhole(side) && walls.WholePiece(side) < 7 ? -1 : neighbour);
            // Special gate walls have a separate native builder; its tint is not traced here.
            TerrainLightColour tint = walls.IsWhole(side) && walls.WholePiece(side) < 7 ? default : NeighbourTint(tiles, x, y, side);
            if (walls.IsWhole(side))
            {
                Add(geometry, _pieces.Piece(keys.Piece(walls.WholePiece(side))), walls.WholeRotation(side), centre, tagged, tint, stateSource: source);
            }
            else if (walls.HasWall(side))
            {
                Add(geometry, _pieces.Piece(keys.Piece(walls.HalfAPiece(side))), side, Quadrant(centre, side), tagged, tint, stateSource: source);
                Add(geometry, _pieces.Piece(keys.Piece(walls.HalfBPiece(side))), side, Quadrant(centre, (side + 1) & 3), tagged, tint, stateSource: source);
            }
        }
    }

    /// <summary>A hero-owned wall's faces, carried in <see cref="TileView.Walls"/>: a whole piece at the centre or two
    /// halves in the side's two quadrants, turned to the side, with the room scheme's halves 4 and 5 swapped
    /// (0x0043E860).</summary>
    private void AddHeroFaces(TerrainTileGeometry geometry, in TileView tile, TerrainPieceKeys keys, Vector3 centre, bool tagged)
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
                Add(geometry, _pieces.Piece(keys.HeroFace(faces.WholePiece(side))), side, centre, tagged, tile.AmbientTint);
            }
            else if (faces.HasWall(side))
            {
                Add(geometry, _pieces.Piece(keys.HeroFace(SwapHeroHalf(faces.HalfAPiece(side)))), side,
                    Quadrant(centre, side), tagged, tile.AmbientTint);
                Add(geometry, _pieces.Piece(keys.HeroFace(SwapHeroHalf(faces.HalfBPiece(side)))), side,
                    Quadrant(centre, (side + 1) & 3), tagged, tile.AmbientTint);
            }
        }
    }

    internal static int SwapHeroHalf(int piece)
    {
        return piece == 4 ? 5 : piece == 5 ? 4 : piece;
    }

    /// <summary>A piece list's quarters and whole piece, from a room's pieces or a liquid's or bridge's bed. A liquid's
    /// surface is not drawn here.</summary>
    private void AddSet(TerrainTileGeometry geometry, PieceSet set, RoomPieceKeys? room, TerrainPieceKeys? bed, Vector3 centre,
        TerrainLightColour wholeTint = default, TerrainLightColour quarterTint = default)
    {
        if (set.HasQuarters)
        {
            for (int q = 0; q < 4; q++)
            {
                if (set.HasQuarter(q))
                {
                    Add(geometry, _pieces.Piece(Key(room, bed, set.QuarterPiece(q))), set.QuarterRotation(q),
                        Quadrant(centre, q), tint: quarterTint,
                        stateSource: room != null ? new Vector2(-1, -2 - geometry.Y * _width - geometry.X) : new Vector2(-1));
                }
            }
        }

        if (set.HasWhole)
        {
            Add(geometry, _pieces.Piece(Key(room, bed, set.WholePiece)), set.WholeRotation, centre, tint: wholeTint);
        }
    }

    private bool FacesTag(TileView[] tiles, int x, int y, int side)
    {
        int nx = x + SideDx[side], ny = y + SideDy[side];
        return nx >= 0 && ny >= 0 && nx < _width && ny < _height && tiles[(ny * _width) + nx].Tagged;
    }

    private TerrainLightColour NeighbourTint(TileView[] tiles, int x, int y, int side)
    {
        int nx = x + SideDx[side], ny = y + SideDy[side];
        return nx >= 0 && ny >= 0 && nx < _width && ny < _height ? tiles[(ny * _width) + nx].AmbientTint : default;
    }

    private static AssetRef Key(RoomPieceKeys? room, TerrainPieceKeys? bed, int k)
    {
        return room != null ? room.Piece(k) : bed!.Bed(k);
    }

    private static Vector3 Centre(int x, int y)
    {
        return RenderWorld.Ground(x + 0.5f, y + 0.5f);
    }

    private static Vector3 Quadrant(Vector3 centre, int quadrant)
    {
        return centre + new Vector3(QuadrantCentres[quadrant].X, 0f, QuadrantCentres[quadrant].Y);
    }

    /// <summary>Places one piece and keys each of its triangle groups (<see cref="TerrainMaterialKey.For"/>); a tagged
    /// or red piece draws a second time, additively (tag-highlight.md).</summary>
    private static void Add(TerrainTileGeometry geometry, TerrainPiece? piece, int rotation, Vector3 at, bool tagged = false,
        TerrainLightColour tint = default, byte? fogOpacity = null, bool invertLightingNormals = false, Vector2? stateSource = null)
    {
        if (piece == null)
        {
            return;
        }

        bool red = geometry.Red;
        int placed = geometry.Pieces.Count;
        geometry.Pieces.Add(new PlacedPiece(piece, rotation, at, invertLightingNormals, stateSource ?? geometry.DefaultStateSource));
        for (int m = 0; m < piece.Triangles.Count; m++)
        {
            var key = TerrainMaterialKey.For(piece.Recipes[m],
                geometry.StateTexture ? false : red, geometry.StateTexture ? false : tagged,
                geometry.StateTexture ? default : tint, fogOpacity);
            geometry.Groups.Add(new TriangleGroup(placed, m, geometry.SlotOf(key)));
            if (!geometry.StateTexture && (red || tagged))
            {
                geometry.Groups.Add(new TriangleGroup(placed, m, geometry.SlotOf(TerrainMaterialKey.Overlay(red))));
            }
        }
    }

    internal readonly record struct PlacedPiece(TerrainPiece Piece, int Rotation, Vector3 At, bool InvertNormals, Vector2 StateSource);

    internal readonly record struct TriangleGroup(int Piece, int Group, int Slot);
}
