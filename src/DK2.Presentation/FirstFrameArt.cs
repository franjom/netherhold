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

    /// <summary>What the first frames of play ask for that the first frame did not, to decode in the
    /// background after the first tick rather than on request in a frame (ADR-0688): every clip and
    /// the dead mesh of each creature type present, since creatures change clips within a second of
    /// play; and the objects, decorations and structures the first tick added. Nothing already in
    /// <paramref name="firstFrame"/>.</summary>
    public static IReadOnlyCollection<AssetRef> LaterKeys(GameDatabase database, WorldState world, PlayerId viewer,
        IReadOnlyCollection<AssetRef> firstFrame)
    {
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (firstFrame is null) throw new ArgumentNullException(nameof(firstFrame));

        var view = new WorldView(database, viewer);
        var keys = new HashSet<AssetRef>();
        // Present types by key prefix (creature.<name>.), so a creature drawn with a mesh variant
        // still brings its type's clips.
        var presentTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (CreatureView creature in view.Creatures(world))
        {
            Add(keys, creature.Mesh);
            Add(keys, creature.Animation);
            if (!creature.Mesh.IsEmpty) presentTypes.Add(TypePrefix(creature.Mesh));
        }
        foreach (CreatureDef creature in database.Creatures.Values)
        {
            if (!presentTypes.Contains(TypePrefix(AssetRef.ForCreature(creature.Name, AssetKind.Mesh)))) continue;
            Add(keys, AssetRef.ForCreature(creature.Name, AssetKind.Mesh, "dead"));
            foreach (string animation in AssetCatalogue.CreatureAnimations)
                Add(keys, AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation));
            foreach (string animation in AssetCatalogue.CreatureActionAnimations)
                Add(keys, AssetRef.ForCreature(creature.Name, AssetKind.Animation, animation));
        }
        foreach (ObjectView instance in view.Objects(world)) Add(keys, instance.Mesh);
        foreach (ObjectView decoration in view.Decorations(world)) Add(keys, decoration.Mesh);
        foreach (StructureView structure in view.Structures(world))
        {
            Add(keys, structure.Mesh);
            Add(keys, structure.LockMesh);
        }
        keys.ExceptWith(firstFrame);
        return keys;
    }

    /// <summary>The <c>category.subject.</c> prefix of a key, which every asset of that subject shares.</summary>
    public static string TypePrefix(AssetRef key)
    {
        string text = key.Key;
        int first = text.IndexOf('.');
        int second = first < 0 ? -1 : text.IndexOf('.', first + 1);
        return second < 0 ? text + "." : text.Substring(0, second + 1);
    }

    private static void Add(HashSet<AssetRef> keys, AssetRef key)
    {
        if (!key.IsEmpty) keys.Add(key);
    }
}
