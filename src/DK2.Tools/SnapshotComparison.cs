// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Tools;

internal sealed record SnapshotFieldChange(string Path, string Kind, string? Before, string? After);

internal static class SnapshotComparison
{
    internal static IReadOnlyList<SnapshotFieldChange> Compare(GameSimulation before, GameSimulation after)
    {
        var changes = new List<SnapshotFieldChange>();
        Fields("snapshot", Metadata(before), Metadata(after));
        WorldState left = before.World, right = after.World;
        var leftCreatures = left.Creatures.ToDictionary(c => c.Id);
        var rightCreatures = right.Creatures.ToDictionary(c => c.Id);
        foreach (int id in leftCreatures.Keys.Concat(rightCreatures.Keys).Distinct().Order())
            Entity($"creatures[{id}]", leftCreatures.GetValueOrDefault(id), rightCreatures.GetValueOrDefault(id));
        var leftPlayers = Enumerable.Range(0, left.PlayerCount).Select(i => left.PlayerAtIndex(i)).ToDictionary(p => p.PlayerId.Value);
        var rightPlayers = Enumerable.Range(0, right.PlayerCount).Select(i => right.PlayerAtIndex(i)).ToDictionary(p => p.PlayerId.Value);
        foreach (byte id in leftPlayers.Keys.Concat(rightPlayers.Keys).Distinct().Order())
            Entity($"players[{id}]", leftPlayers.TryGetValue(id, out PlayerState lp) ? lp : null,
                rightPlayers.TryGetValue(id, out PlayerState rp) ? rp : null);
        foreach (TilePos tile in Positions(left))
            Entity($"tiles[{tile.X},{tile.Y}]", left.Tile(tile), right.Contains(tile) ? right.Tile(tile) : null);
        foreach (TilePos tile in Positions(right))
            if (!left.Contains(tile)) Entity($"tiles[{tile.X},{tile.Y}]", null, right.Tile(tile));
        changes.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
        return changes;

        void Entity(string path, object? oldValue, object? newValue)
        {
            if (oldValue is null && newValue is null) return;
            if (oldValue is null || newValue is null)
                changes.Add(new(path + ".$exists", oldValue is null ? "added" : "removed",
                    oldValue is null ? null : "present", newValue is null ? null : "present"));
            Fields(path, oldValue, newValue);
        }

        void Fields(string path, object? oldValue, object? newValue)
        {
            SortedDictionary<string, string?> oldFields = oldValue is null ? new(StringComparer.Ordinal) : SnapshotFields.Capture(oldValue, path);
            SortedDictionary<string, string?> newFields = newValue is null ? new(StringComparer.Ordinal) : SnapshotFields.Capture(newValue, path);
            foreach (string name in oldFields.Keys.Concat(newFields.Keys).Distinct(StringComparer.Ordinal))
            {
                bool had = oldFields.TryGetValue(name, out string? oldText);
                bool has = newFields.TryGetValue(name, out string? newText);
                if (had && has && oldText == newText) continue;
                changes.Add(new(name, !had ? "added" : !has ? "removed" : "changed", oldText, newText));
            }
        }
    }

    private static IEnumerable<TilePos> Positions(WorldState world)
    {
        for (int index = 0; index < world.TileCount; index++) yield return world.TilePosition(index);
    }

    private static object Metadata(GameSimulation simulation) => new
    {
        Tick = simulation.CurrentTick.Value,
        simulation.World.Width,
        simulation.World.Height,
        simulation.World.FogOfWarEnabled,
        simulation.World.GameMode,
        Rng = Random(simulation.Rng),
    };

    private static SortedDictionary<string, string> Random(SimRandom random)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["Algorithm"] = random.Algorithm.GetType().Name,
        };
        if (random.Algorithm is Dk2Lcg lcg)
            values["Seed"] = lcg.Seed.ToString(CultureInfo.InvariantCulture);
        else if (random.Algorithm is Xoshiro256StarStar)
        {
            (ulong a, ulong b, ulong c, ulong d) = random.SaveState();
            values["S0"] = a.ToString(CultureInfo.InvariantCulture);
            values["S1"] = b.ToString(CultureInfo.InvariantCulture);
            values["S2"] = c.ToString(CultureInfo.InvariantCulture);
            values["S3"] = d.ToString(CultureInfo.InvariantCulture);
        }
        else throw new InvalidDataException("Unsupported snapshot random generator.");
        return values;
    }
}
