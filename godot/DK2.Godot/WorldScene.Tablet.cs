// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.


using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

public partial class WorldScene
{
    internal TabletMesh? LoadTabletMesh()
    {
        return _art != null && _art.TryResolveEntry("Tablet", out ResolvedAsset asset)
        && _art.TryLoadModel(asset, out KmfModel model) && model.Mesh != null ? new TabletMesh(model) : null;
    }

    internal Texture2D? TabletTexture(string name)
    {
        Texture2D texture = TextureFor(name);
        return texture == _white ? null : texture;
    }
}
