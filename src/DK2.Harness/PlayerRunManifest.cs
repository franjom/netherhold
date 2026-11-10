// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DK2.Simulation.Players;

namespace DK2.Harness;

/// <summary>
/// What a player run executed against, written beside its journal as <see cref="FileName"/>
/// (campaign player plan, task 4): the run id, the scenario file, the install's data (every
/// <c>.kwd</c>/<c>.kld</c> under <c>Data/editor</c> and <c>Data/Paths.WAD</c>, by relative path),
/// every <c>DK2.*.dll</c> beside the harness, and separately the source tree. <see cref="Verify"/>
/// blocks a replay whose run, scenario, data or assemblies differ; a source difference is only
/// diagnostic. Hashes do not prove the assemblies were built from that source: a qualifying attempt
/// also needs task 7's fresh-build receipt. Computed at run start and before replay, never per tick.
/// </summary>
public static class PlayerRunManifest
{
    public const string FileName = "player-run-manifest.json";
    public const int Schema = 1;

    /// <summary>Assemblies a manifest must name; without them it identifies no build.</summary>
    private static readonly string[] Required = ["DK2.Harness", "DK2.Simulation"];

    public sealed record Identity(
        int Schema, string RunId, string ScenarioSha256, string DataSha256, int DataFiles,
        IReadOnlyDictionary<string, string> Assemblies, string SourceSha256, int SourceFiles);

    /// <summary>A replay may proceed only with no blocking difference.</summary>
    public sealed record Verification(IReadOnlyList<string> Blocking, IReadOnlyList<string> Diagnostic)
    {
        public bool MayReplay => Blocking.Count == 0;
    }

    public static Identity Compute(Scenario scenario, string? installDirectory, PlayerRunId runId)
    {
        if (scenario is null) throw new ArgumentNullException(nameof(scenario));
        (string data, int dataFiles) = DataHash(installDirectory);
        (string source, int sourceFiles) = SourceHash(scenario.SourcePath);
        return new Identity(Schema, runId.Value, JournalHeader.Sha256(scenario.SourcePath), data, dataFiles,
            Assemblies(), source, sourceFiles);
    }

    public static void Write(string directory, Scenario scenario, string? installDirectory, PlayerRunId runId)
    {
        Directory.CreateDirectory(directory);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(Compute(scenario, installDirectory, runId), Indented);
        // CreateNew: evidence is never overwritten, and a second writer cannot race the first.
        using var file = new FileStream(Path.Combine(directory, FileName), FileMode.CreateNew, FileAccess.Write);
        file.Write(json);
    }

    public static Verification Verify(string directory, Scenario scenario, string? installDirectory, PlayerRunId runId)
    {
        string path = Path.Combine(directory, FileName);
        Identity? recorded;
        try { recorded = JsonSerializer.Deserialize<Identity>(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Blocked($"incomplete evidence: {FileName} could not be read ({ex.GetType().Name}: {ex.Message})");
        }
        if (recorded?.Assemblies is null || recorded.RunId is null) return Blocked($"incomplete evidence: {FileName} is empty or malformed");
        if (recorded.Schema != Schema) return Blocked($"incomplete evidence: manifest schema {recorded.Schema}, this build reads {Schema}");

        Identity current;
        try { current = Compute(scenario, installDirectory, runId); }
        catch (DirectoryNotFoundException ex) { return Blocked($"incomplete evidence: {ex.Message}"); }
        var blocking = new List<string>();
        if (recorded.RunId != current.RunId) blocking.Add($"the manifest is run '{recorded.RunId}', not '{current.RunId}'");
        if (recorded.ScenarioSha256 != current.ScenarioSha256)
            blocking.Add($"scenario differs: recorded {recorded.ScenarioSha256}, now {current.ScenarioSha256}");
        if (recorded.DataSha256 != current.DataSha256)
            blocking.Add($"installed data differs: recorded {recorded.DataSha256} over {recorded.DataFiles} files, now {current.DataSha256} over {current.DataFiles}");
        foreach (string name in Required)
            if (!recorded.Assemblies.ContainsKey(name)) blocking.Add($"incomplete evidence: the manifest names no {name}");
        foreach (string name in recorded.Assemblies.Keys.Union(current.Assemblies.Keys).Order(StringComparer.Ordinal))
            if (!recorded.Assemblies.TryGetValue(name, out string? was) || !current.Assemblies.TryGetValue(name, out string? now) || was != now)
                blocking.Add($"assembly {name} differs from the recorded run");
        List<string> diagnostic = recorded.SourceSha256 == current.SourceSha256 ? []
            : [$"the source tree differs from the recorded run's ({recorded.SourceFiles} files then, {current.SourceFiles} now)"];
        return new Verification(blocking, diagnostic);
    }

    private static Verification Blocked(string why) => new([why], []);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The install's data: <c>Data/editor</c>'s KWD and KLD files and <c>Data/Paths.WAD</c>
    /// (the camera paths' frame counts). "none" with no install; a named install without
    /// <c>Data/editor</c> is an error, not an empty identity.</summary>
    private static (string Sha256, int Files) DataHash(string? install)
    {
        if (install is null) return ("none", 0);
        string data = Path.Combine(install, "Data");
        string editor = Path.Combine(data, "editor");
        if (!Directory.Exists(editor)) throw new DirectoryNotFoundException($"'{editor}' does not exist; '{install}' is not a DK2 installation.");
        List<string> files = Files(editor, ".kwd", ".kld").Select(f => "editor/" + f).ToList();
        if (File.Exists(Path.Combine(data, "Paths.WAD"))) files.Add("Paths.WAD");
        return TreeHash(data, files);
    }

    /// <summary>The repository's <c>src/</c> sources plus the root build files that change how they
    /// compile, found from the scenario's directory upward; "none" outside a repository.</summary>
    private static (string Sha256, int Files) SourceHash(string scenarioPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(scenarioPath.Length > 0 ? scenarioPath : ".")) ?? ".");
        for (; dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "DK2.slnx"))) continue;
            string src = Path.Combine(dir.FullName, "src");
            List<string> files = Directory.Exists(src) ? Files(src, ".cs", ".csproj", ".props").Select(f => "src/" + f).ToList() : [];
            foreach (string root in new[] { "DK2.slnx", "Directory.Build.props", "global.json" })
                if (File.Exists(Path.Combine(dir.FullName, root))) files.Add(root);
            return TreeHash(dir.FullName, files);
        }
        return ("none", 0);
    }

    /// <summary>Files under <paramref name="root"/> with one of the extensions, any case, as relative
    /// '/' paths; build output folders are skipped.</summary>
    private static IEnumerable<string> Files(string root, params string[] extensions) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                        && !("/" + f).Contains("/bin/", StringComparison.Ordinal) && !("/" + f).Contains("/obj/", StringComparison.Ordinal));

    /// <summary>One SHA-256 over (relative path, file hash) lines in ordinal path order.</summary>
    private static (string Sha256, int Files) TreeHash(string root, List<string> relativePaths)
    {
        relativePaths.Sort(StringComparer.Ordinal);
        var lines = new StringBuilder();
        foreach (string relative in relativePaths)
            lines.Append(relative).Append('\t').Append(StreamHash(Path.Combine(root, relative))).Append('\n');
        return (Hex(SHA256.HashData(Encoding.UTF8.GetBytes(lines.ToString()))), relativePaths.Count);
    }

    /// <summary>Every <c>DK2.*.dll</c> in the harness's own directory but test assemblies: the set the
    /// process can load, the same whichever of them a given run happened to touch first.</summary>
    private static IReadOnlyDictionary<string, string> Assemblies()
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string? directory = Path.GetDirectoryName(typeof(PlayerRunManifest).Assembly.Location);
        if (string.IsNullOrEmpty(directory)) return hashes;
        foreach (string path in Directory.EnumerateFiles(directory, "DK2.*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!name.EndsWith(".Tests", StringComparison.Ordinal) && name != "DK2.TestSupport") hashes[name] = StreamHash(path);
        }
        return hashes;
    }

    private static string StreamHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Hex(SHA256.HashData(stream));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
