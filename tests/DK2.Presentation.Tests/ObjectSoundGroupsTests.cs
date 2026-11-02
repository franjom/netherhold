// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class ObjectSoundGroupsTests
{
    /// <summary>Which sound groups a shipped category holds with at least one sample, from its <c>SFX.map</c> (none for a category
    /// with no map).</summary>
    private static bool Holds(string category, uint group)
    {
        string file = Path.Combine(Dk2InstallGate.Path, "Data", "Sound", "Sfx", "Global", category.ToLowerInvariant() + "SFX.map");
        if (!File.Exists(file)) return false;
        return SfxMap.Read(File.ReadAllBytes(file)).Groups.Any(g => g.Id.Value == group && g.Items.Any(i => i.Sounds.Count > 0));
    }

    /// <summary><see cref="ObjectSoundRules"/> asks only the categories whose map has the group: for every category a shipped
    /// object names, its table must say what the map says, or a silent request is made or an audible one is not.</summary>
    [InstallFact]
    public void ObjectSoundGroupsAreTheShippedOnes()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        string[] categories = db.Objects.Values.Select(o => o.SoundCategory).Where(c => c.Length > 0).Distinct().ToArray();

        Assert.Contains("OBJECT_CHICKEN", categories);
        foreach (string category in categories)
            foreach (ThingSoundEvent sound in new[] { ThingSoundEvent.ObjectMade, ThingSoundEvent.ObjectMadeSecond, ThingSoundEvent.ObjectRemoved })
                Assert.True(Holds(category, (uint)sound) == ObjectSoundRules.HasGroup(category, sound), $"{category} {(uint)sound:X}");
        Assert.True(Holds("OBJECT_CHICKEN", 0xF4) && Holds("TRAP_BOULDER", 0xF4) && Holds("OBJECT_BED", 0xF1) && Holds("OBJECT_RAT", 0xF2));
    }
}
