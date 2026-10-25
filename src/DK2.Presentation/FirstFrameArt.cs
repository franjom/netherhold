// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>
/// The art keys a world's first presentation asks for, so a renderer can decode them ahead of it.
/// Creatures, objects, structures and torches are read from a fresh view, as the presenter reads
/// them; terrain is <see cref="WorldView.TerrainArtKeys"/>, a superset of what is drawn.
/// </summary>
public static class FirstFrameArt
{
    public static IReadOnlyCollection<AssetRef> Keys(GameDatabase database, WorldState world, PlayerId viewer,
        IReadOnlyList<FrontEndProp>? frontEndScenery = null)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (world is null) throw new ArgumentNullException(nameof(world));

        var view = new WorldView(database, viewer);
        if (frontEndScenery is not null) view.FrontEndScenery = frontEndScenery;
        var keys = new HashSet<AssetRef>();
        if (frontEndScenery is not null)
            foreach (FrontEndProp prop in frontEndScenery)
                if (prop.MarkerSlot.HasValue) Add(keys, prop.View.Mesh);
        foreach (AssetRef key in view.TerrainArtKeys(world)) Add(keys, key);

        foreach (CreatureView creature in view.Creatures(world))
        {
            Add(keys, creature.Mesh);
            Add(keys, creature.Animation);
        }
        foreach (ObjectView instance in view.Objects(world)) Add(keys, instance.Mesh);
        foreach (ObjectView decoration in view.Decorations(world)) Add(keys, decoration.Mesh);
        foreach (StructureView structure in view.Structures(world))
        {
            Add(keys, structure.Mesh);
            Add(keys, structure.LockMesh);
        }
        var torches = new TorchLayout(database);
        foreach (int floor in torches.Update(world))
            if (torches.At(floor) is TorchView torch) Add(keys, torch.Mesh);
        return keys;
    }

    private static void Add(HashSet<AssetRef> keys, AssetRef key)
    {
        if (!key.IsEmpty) keys.Add(key);
    }
}
