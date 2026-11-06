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
/// The art the first <see cref="ScenePresenter.Present"/> of a world asks for, so it can be decoded
/// ahead on workers while the scene is built (ADR-0686), and the art the first seconds of play ask
/// for after it, decoded in the background (ADR-0688).
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
        AddThings(keys, view, world);
        var torches = new TorchLayout(database);
        foreach (int floor in torches.Update(world))
            if (torches.At(floor) is TorchView torch) Add(keys, torch.Mesh);
        return keys;
    }

    /// <summary>What the first seconds of play ask for that the first frame did not: every clip and
    /// the dead mesh of each creature type present, since creatures change clips within a second of
    /// play, and the things the first tick added. Nothing already in <paramref name="firstFrame"/>.</summary>
    public static IReadOnlyCollection<AssetRef> LaterKeys(GameDatabase database, WorldState world, PlayerId viewer,
        IReadOnlyCollection<AssetRef> firstFrame)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (firstFrame is null) throw new ArgumentNullException(nameof(firstFrame));

        var view = new WorldView(database, viewer);
        var keys = new HashSet<AssetRef>();
        AddThings(keys, view, world);
        // By subject rather than by the mesh key itself, so a creature drawn with a mesh variant
        // still brings its type's clips.
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (CreatureView creature in view.Creatures(world))
            if (!creature.Mesh.IsEmpty) present.Add(creature.Mesh.Subject);
        foreach (CreatureDef creature in database.Creatures.Values)
            if (present.Contains(AssetRef.ForCreature(creature.Name, AssetKind.Mesh).Subject))
                foreach (AssetRef key in AssetCatalogue.ForCreature(creature)) Add(keys, key);
        keys.ExceptWith(firstFrame);
        return keys;
    }

    /// <summary>The creatures, objects, decorations and structures as the presenter reads them.</summary>
    private static void AddThings(HashSet<AssetRef> keys, WorldView view, WorldState world)
    {
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
    }

    private static void Add(HashSet<AssetRef> keys, AssetRef key)
    {
        if (!key.IsEmpty) keys.Add(key);
    }
}
