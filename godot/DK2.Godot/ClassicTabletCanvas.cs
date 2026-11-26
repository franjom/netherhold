// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.


using DK2.Art.Kmf;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws the tablet with runtime textures owned by the world.</summary>
internal sealed class ClassicTabletCanvas : IDisposable
{
    private readonly TabletDrawList _draw;
    private readonly Dictionary<string, Rid> _textures = new(StringComparer.Ordinal);
    private readonly TriangleBuffer _triangles;
    private readonly Rid _sparkle;
    private readonly Rid _item;
    private bool _visible;

    public ClassicTabletCanvas(Rid canvas, TabletMesh mesh, Func<string, Texture2D?> texture)
    {
        _draw = new TabletDrawList(mesh);
        int capacity = 0;
        foreach (KmfPrimitive primitive in mesh.Primitives)
        {
            capacity = Math.Max(capacity, primitive.IndicesByDetailLevel[0].Count);
            foreach (string name in mesh.Model.Materials[(int)primitive.MaterialIndex].TextureNames)
            {
                if (!_textures.ContainsKey(name))
                {
                    _textures.Add(name, texture(name)?.GetRid() ?? default);
                }
            }
        }
        _triangles = new TriangleBuffer(capacity);
        _sparkle = texture("TabletSparkle")?.GetRid() ?? default;
        _item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_item, canvas);
        RenderingServer.CanvasItemSetDefaultTextureFilter(_item, RenderingServer.CanvasItemTextureFilter.Linear);
    }

    public void Show(RewardTablet tablet, byte itemId, float x, float y, int height)
    {
        _draw.Build(tablet, itemId, x, y, height);
        RenderingServer.CanvasItemClear(_item);
        _visible = true;
        foreach (TabletBatch batch in _draw.Batches)
        {
            if (batch.Count == 0 || !_textures.TryGetValue(batch.Texture, out Rid texture) || !texture.IsValid)
            {
                continue;
            }

            ReadOnlySpan<TabletVertex> vertices = _draw.Vertices.Slice(batch.First, batch.Count);
            for (int i = 0; i < vertices.Length; i++)
            {
                TabletVertex vertex = vertices[i];
                _triangles.Points[i] = new Vector2(vertex.X, vertex.Y);
                _triangles.Uvs[i] = new Vector2(vertex.U, vertex.V);
            }
            RenderingServer.CanvasItemAddTriangleArray(_item, _triangles.Indices.AsSpan(0, batch.Count),
                _triangles.Points.AsSpan(0, batch.Count), [],
                _triangles.Uvs.AsSpan(0, batch.Count), [], [], texture, -1);
        }

        if (!_sparkle.IsValid)
        {
            return;
        }

        foreach (TabletSparkleQuad sparkle in _draw.Sparkles)
        {
            float side = sparkle.Half * 2;
            var rect = new Rect2(sparkle.X - sparkle.Half, sparkle.Y - sparkle.Half, side, side);
            RenderingServer.CanvasItemAddTextureRect(_item, rect, _sparkle);
        }
    }

    public void Hide()
    {
        if (!_visible)
        {
            return;
        }

        RenderingServer.CanvasItemClear(_item);
        _visible = false;
    }

    public void Dispose() => RenderingServer.FreeRid(_item);

    private sealed class TriangleBuffer
    {
        public readonly int[] Indices;
        public readonly Vector2[] Points, Uvs;

        public TriangleBuffer(int count)
        {
            Indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                Indices[i] = i;
            }

            Points = new Vector2[count];
            Uvs = new Vector2[count];
        }
    }
}
