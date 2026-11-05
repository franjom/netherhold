// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The sound categories a level decodes while its loading screen is up (<see cref="SoundClipDecoder.Prefetch"/>):
/// its own speech, the categories of the terrain and rooms on its map (ambience emitters, the heart's beat, room work), and
/// those of the creatures, objects, doors and traps it starts with: about 55 MB of samples for Level6a. What a level gains
/// later (a new creature's voice) and the mentor's 1,500 lines (233 MB decoded) are decoded on first use, off the frame's
/// thread. Music and the ambience bed are not here: they stream segments of 2-11 MB decoded (the bed's category alone is
/// 68 MB), each decoded one ahead of need.</summary>
public static class LevelSoundPrefetch
{
    public static IReadOnlyCollection<SoundCategory> Categories(GameDatabase database, WorldState world, SoundCategory levelSpeech)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = new List<SoundCategory>();
        void Add(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name) && names.Add(name!)) categories.Add(new SoundCategory(name!.ToLowerInvariant()));
        }
        // The runner falls back to the mentor's category when there is no level record; it is 1,500 lines and not prefetched.
        if (levelSpeech != SoundCategory.Mentor) Add(levelSpeech.Name);
        // Only the terrain on the map: every terrain and room in the database decodes 140 MB and takes seconds.
        var terrains = new HashSet<byte>();
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                if (terrains.Add(world.Tile(x, y).TerrainId) && database.Terrain.TryGetValue(world.Tile(x, y).TerrainId, out TerrainDef? terrain))
                    Add(terrain.SoundCategory);
        foreach (CreatureState creature in world.Creatures)
            if (database.Creatures.TryGetValue(creature.CreatureTypeId, out CreatureDef? definition)) Add(definition.SoundCategory);
        foreach (ObjectState thing in world.Objects) Add(database.ObjectById(thing.ObjectTypeId)?.SoundCategory);
        foreach (DoorState door in world.Doors)
            if (database.Doors.TryGetValue(door.DoorId, out DoorDef? definition)) Add(definition.SoundCategory);
        foreach (TrapState trap in world.Traps)
            if (database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition)) Add(definition.SoundCategory);
        return categories;
    }
}
