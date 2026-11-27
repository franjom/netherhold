// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Assets;

namespace DK2.Presentation;

/// <summary>One install model as a terrain chunk copies it: arrays in the renderers' frame (<see cref="RenderMesh"/>),
/// triangles and a material recipe per KMF material.</summary>
public sealed class TerrainPiece
{
    public TerrainPiece(Vector3[] positions, Vector3[] normals, Vector2[] uvs, IReadOnlyList<int[]> triangles,
        ClassicMaterialRecipe[] recipes)
    {
        Positions = positions ?? throw new ArgumentNullException(nameof(positions));
        Normals = normals ?? throw new ArgumentNullException(nameof(normals));
        Uvs = uvs ?? throw new ArgumentNullException(nameof(uvs));
        Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));
        Recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
        if (normals.Length != positions.Length || uvs.Length != positions.Length || recipes.Length != triangles.Count)
        {
            throw new ArgumentException("A piece needs one normal and UV a position and one recipe a triangle list.");
        }
    }

    public Vector3[] Positions { get; }
    public Vector3[] Normals { get; }
    public Vector2[] Uvs { get; }

    /// <summary>Vertex indices, three a triangle, per material.</summary>
    public IReadOnlyList<int[]> Triangles { get; }

    /// <summary>Each triangle list's material.</summary>
    public ClassicMaterialRecipe[] Recipes { get; }

    /// <summary>The model's static mesh with each material's texture alternative <paramref name="textureIndex"/>
    /// (<see cref="ClassicMaterialRecipe.FromKmf"/>): a Decay terrain's face passes its damage stage.</summary>
    public static TerrainPiece FromModel(KmfModel model, int textureIndex)
    {
        var source = RenderMesh.FromModel(model);
        var recipes = new ClassicMaterialRecipe[source.Triangles.Count];
        for (int i = 0; i < recipes.Length; i++)
        {
            recipes[i] = ClassicMaterialRecipe.FromKmf(i < model.Materials.Count ? model.Materials[i] : null, textureIndex);
        }

        return new TerrainPiece([.. source.Positions], [.. source.Normals], [.. source.Uvs], source.Triangles, recipes);
    }
}

/// <summary>Where a terrain build finds its pieces.</summary>
public interface ITerrainPieceSource
{
    /// <summary>The piece for <paramref name="key"/> with texture alternative <paramref name="textureIndex"/> (below 0
    /// the first); null when the install has no model for the key.</summary>
    TerrainPiece? Piece(AssetRef key, int textureIndex = -1);
}

/// <summary>Pieces straight from the install, decoded once per key and texture alternative.</summary>
public sealed class InstallTerrainPieces : ITerrainPieceSource
{
    private readonly InstallArtProvider _art;
    private readonly Dictionary<(AssetRef, int), TerrainPiece?> _pieces = new Dictionary<(AssetRef, int), TerrainPiece?>();

    public InstallTerrainPieces(InstallArtProvider art)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
    }

    public TerrainPiece? Piece(AssetRef key, int textureIndex = -1)
    {
        if (textureIndex < 0)
        {
            textureIndex = 0;
        }

        if (_pieces.TryGetValue((key, textureIndex), out TerrainPiece? cached))
        {
            return cached;
        }

        TerrainPiece? piece = _art.TryResolve(key, out ResolvedAsset asset) && _art.TryLoadModel(asset, out KmfModel model)
            ? TerrainPiece.FromModel(model, textureIndex) : null;
        _pieces[(key, textureIndex)] = piece;
        return piece;
    }

    /// <summary>Forgets every piece.</summary>
    public void Clear()
    {
        _pieces.Clear();
    }
}
