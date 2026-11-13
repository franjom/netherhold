// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DK2.Core;
using DK2.Data;
using DK2.Data.Save;
using DK2.Simulation;

namespace DK2.Tools.Commands;

internal static class SnapshotDiffCommand
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static int Run(string[] args, Func<string, GameDatabase>? loadDatabase = null)
    {
        if (args.Length is not (4 or 6) || args.Length == 6 && args[4] != "--level") return Program.Usage();
        try
        {
            if (File.Exists(args[3])) throw new IOException("The report output already exists.");
            SnapshotInput before = Read(args[1]), after = Read(args[2]);
            string? explicitLevel = args.Length == 6 ? args[5] : null;
            if (explicitLevel is null && (before.Level is null || after.Level is null))
                throw new InvalidDataException("Raw snapshots require --level <campaign-level>.");
            string level = explicitLevel ?? before.Level!;
            if (level.Length is < 1 or > 128 || !level.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                throw new InvalidDataException("Invalid campaign level name.");
            if (new[] { before.Level, after.Level }.Any(l => l is not null && !string.Equals(l, level, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The saved-session levels or --level disagree.");
            GameDatabase database = (loadDatabase ?? LoadDatabase)(level);
            GameSimulation oldSimulation = WorldSnapshot.FromBytes(before.Snapshot, database);
            GameSimulation newSimulation = WorldSnapshot.FromBytes(after.Snapshot, database);
            IReadOnlyList<SnapshotFieldChange> changes = SnapshotComparison.Compare(oldSimulation, newSimulation);
            bool equal = before.Snapshot.AsSpan().SequenceEqual(after.Snapshot);
            string verdict = changes.Count > 0 ? "different" : equal ? "identical" : "different-outside-scope";
            var report = new
            {
                SchemaVersion = 1,
                Level = level,
                Before = Source(before, oldSimulation),
                After = Source(after, newSimulation),
                SnapshotBytesEqual = equal,
                Verdict = verdict,
                Scope = new[] { "snapshot metadata and RNG", "creature fields", "player fields", "tile fields" },
                Limitations = new[]
                {
                    "Decoded fields only; computed properties and world back-references are excluded.",
                    "Other state, including shots, objects, queues, navigation and campaign profile, is outside the field comparison.",
                    "The original database is not embedded in a snapshot; supplied level-data provenance is not verified.",
                    "Values are invariant text; no simulation ticks are executed and no input files are changed.",
                },
                Changes = changes,
            };
            string json = JsonSerializer.Serialize(report, Json);
            using (var stream = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(json);
            Console.WriteLine($"{verdict}: {changes.Count} field differences; report: {args[3]}");
            return changes.Count > 0 ? 1 : equal ? 0 : 3;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SaveIncompatibleException
            or UnauthorizedAccessException or ArgumentException or KeyNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"snapshot-diff: {ex.Message}");
            return 2;
        }
    }

    private static GameDatabase LoadDatabase(string level)
    {
        string install = Dk2Install.Find() ?? throw new InvalidDataException("DK2 installation not found (set DK2_DIR).");
        if (GameDatabaseLoader.LoadCampaignLevel(install, level) is null)
            throw new InvalidDataException($"Campaign level '{level}' is not available in this installation.");
        return GameDatabaseLoader.LoadForCampaignLevel(install, level);
    }

    private sealed record SnapshotInput(string Path, string Format, string? Level, byte[] FileBytes, byte[] Snapshot);

    private static SnapshotInput Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8) throw new InvalidDataException("Truncated snapshot or saved session.");
        string magic = Encoding.ASCII.GetString(bytes, 0, 4);
        if (magic == "DK2S") return new(Path.GetFullPath(path), "DK2S", null, bytes, bytes);
        if (magic != "DK2G") throw new InvalidDataException("Expected our DK2S snapshot or DK2G saved session.");
        SavedSession session = SavedSessionFile.Load(path);
        if (!bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path))) throw new IOException("Saved session changed while it was being read.");
        return new(Path.GetFullPath(path), "DK2G", session.Header.LevelName, bytes, session.Snapshot);
    }

    private static object Source(SnapshotInput input, GameSimulation simulation) => new
    {
        input.Path,
        input.Format,
        Sha256 = System.Convert.ToHexString(SHA256.HashData(input.FileBytes)).ToLowerInvariant(),
        SnapshotSha256 = System.Convert.ToHexString(SHA256.HashData(input.Snapshot)).ToLowerInvariant(),
        Tick = simulation.CurrentTick.Value,
        FormatVersion = GameSimulation.CurrentHashVersion,
    };
}
