// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Enumerates the trigger types actually present in the shipped level corpus.</summary>
public static class KwdTriggerCorpusAudit
{
    public sealed record Entry(int Tag, byte Type, int Count, IReadOnlyList<string> Levels);

    public static IReadOnlyList<Entry> Run(string installDir)
    {
        string maps = Path.Combine(installDir, "Data", "editor", "maps");
        var distribution = new SortedDictionary<(int Tag, byte Type), (int Count, SortedSet<string> Levels)>();
        foreach (string file in Directory.GetFiles(maps, "*Triggers.kld")
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string level = Path.GetFileName(file)[..^"Triggers.kld".Length];
            foreach (TriggerRecord trigger in KwdTriggers.Parse(KwdReader.ReadFile(file)))
            {
                var key = (trigger.Tag, trigger.Type);
                if (!distribution.TryGetValue(key, out (int Count, SortedSet<string> Levels) entry))
                    entry = (0, new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
                entry.Levels.Add(level);
                distribution[key] = (entry.Count + 1, entry.Levels);
            }
        }

        return distribution.Select(pair => new Entry(
            pair.Key.Tag, pair.Key.Type, pair.Value.Count, pair.Value.Levels.ToArray())).ToArray();
    }
}
