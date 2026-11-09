// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Security.Cryptography;

namespace DK2.Harness;

/// <summary>
/// A fresh-build receipt (campaign player plan, task 7) as <c>tools/build_receipt.py</c> writes it:
/// <c>output.sha256</c> lists the harness output a clean Release build produced and
/// <c>source.sha256</c> the repository files it was built from, one <c>sha256sum</c> line each
/// (<c>&lt;hex&gt;  &lt;path&gt;</c> or <c>&lt;hex&gt; *&lt;path&gt;</c>); <c>head.txt</c> names the commit.
/// <see cref="Verify"/> checks that the running harness is that build and the tree that source,
/// which is what lets a run claim acceptance.
/// </summary>
public static class BuildReceipt
{
    public const string OutputFile = "output.sha256", SourceFile = "source.sha256", HeadFile = "head.txt";

    /// <summary>Build inputs that change the binaries: a file of these kinds under <c>src/</c> that the
    /// receipt does not list means the tree is not the receipted source.</summary>
    private static readonly string[] BuildInputs = [".cs", ".csproj", ".props", ".targets"];

    public const string StatusFile = "status.txt", PatchHashFile = "tracked.patch.sha256";

    /// <summary>What a check of a receipt found: no problems means the harness and tree are the receipt's.
    /// <paramref name="Dirty"/> is true when the receipt's <c>status.txt</c> lists uncommitted changes, which
    /// <paramref name="PatchSha256"/> (the tracked patch's hash) and the source list then identify.</summary>
    public sealed record Verification(string Directory, string? Head, IReadOnlyList<string> Problems, bool Dirty = false, string? PatchSha256 = null)
    {
        public bool Matches => Problems.Count == 0;
    }

    /// <summary>Checks the receipt in <paramref name="receiptDirectory"/> against <paramref name="harnessDirectory"/>
    /// (every listed output matches, and every file there but a <c>.pdb</c> is listed, the probing files
    /// <c>deps.json</c> and <c>runtimeconfig.json</c> included) and the files under
    /// <paramref name="repositoryRoot"/> (every listed source file matches, and no unlisted build input lies
    /// under <c>src/</c>).</summary>
    public static Verification Verify(string receiptDirectory, string harnessDirectory, string? repositoryRoot)
    {
        var problems = new List<string>();
        string? head = ReadOptional(Path.Combine(receiptDirectory, HeadFile));
        string? status = ReadOptional(Path.Combine(receiptDirectory, StatusFile));
        string? patch = ReadOptional(Path.Combine(receiptDirectory, PatchHashFile))?.Split(' ', 2)[0];

        Dictionary<string, string>? output = Read(Path.Combine(receiptDirectory, OutputFile), problems);
        if (output is not null)
        {
            foreach ((string name, string hash) in output)
                Compare(Path.Combine(harnessDirectory, name), hash, $"output {name}", problems);
            foreach (string file in Directory.EnumerateFiles(harnessDirectory))
            {
                string name = Path.GetFileName(file);
                if (!name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && !output.ContainsKey(name))
                    problems.Add($"output {name} is beside the harness but not in the receipt");
            }
        }

        Dictionary<string, string>? source = Read(Path.Combine(receiptDirectory, SourceFile), problems);
        if (source is not null)
        {
            if (repositoryRoot is null) problems.Add("no repository (DK2.slnx) above the harness to check the source against");
            else
            {
                foreach ((string name, string hash) in source)
                    Compare(Path.Combine(repositoryRoot, name), hash, $"source {name}", problems);
                string src = Path.Combine(repositoryRoot, "src");
                if (Directory.Exists(src))
                    foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
                        // src/<project>/bin and src/<project>/obj are build output; any deeper folder of
                        // that name is still compiled by the SDK's default globs.
                        string[] parts = relative.Split('/');
                        bool buildOutput = parts.Length > 3 && (parts[2] == "bin" || parts[2] == "obj");
                        if (!buildOutput &&Array.IndexOf(BuildInputs, Path.GetExtension(file).ToLowerInvariant()) >= 0
                            && !source.ContainsKey(relative))
                            problems.Add($"source {relative} is a build input the receipt does not list");
                    }
            }
        }
        return new Verification(receiptDirectory, head, problems, Dirty: !string.IsNullOrWhiteSpace(status), PatchSha256: patch);
    }

    /// <summary>Loaded <c>DK2.*</c> assemblies that did not come from <paramref name="harnessDirectory"/>:
    /// the receipt vouches for the files there, so one loaded from elsewhere is not the receipt's build.</summary>
    public static IReadOnlyList<string> LoadedElsewhere(string harnessDirectory)
    {
        string root = Path.GetFullPath(harnessDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var problems = new List<string>();
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string? name = assembly.GetName().Name;
            if (name is null || !name.StartsWith("DK2.", StringComparison.Ordinal) || assembly.IsDynamic) continue;
            string? folder = string.IsNullOrEmpty(assembly.Location) ? null : Path.GetDirectoryName(Path.GetFullPath(assembly.Location));
            if (!string.Equals(folder, root, StringComparison.OrdinalIgnoreCase))
                problems.Add($"loaded {name} comes from {(folder ?? "no file")}, not the receipt's harness folder");
        }
        return problems;
    }

    private static string? ReadOptional(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : null;

    /// <summary>The repository root above <paramref name="start"/> (the folder holding DK2.slnx), or null.</summary>
    public static string? RepositoryRootAbove(string start)
    {
        for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DK2.slnx"))) return dir.FullName;
        return null;
    }

    private static Dictionary<string, string>? Read(string path, List<string> problems)
    {
        if (!File.Exists(path))
        {
            problems.Add($"{Path.GetFileName(path)} is missing from the receipt");
            return null;
        }
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        int number = 0;
        foreach (string line in File.ReadLines(path))
        {
            number++;
            if (line.Length == 0) continue;
            // sha256sum: 64 hex digits, a space, then a space (text) or '*' (binary), then the path.
            if (line.Length < 67 || line[64] != ' ' || (line[65] != ' ' && line[65] != '*')
                || !line[..64].All(char.IsAsciiHexDigit))
            {
                problems.Add($"{Path.GetFileName(path)}:{number} is not a sha256sum line");
                continue;
            }
            string name = line[66..].Replace('\\', '/');
            if (Path.IsPathRooted(name) || name.Split('/').Contains(".."))
            {
                problems.Add($"{Path.GetFileName(path)}:{number} names a path outside the tree: {name}");
                continue;
            }
            if (!entries.TryAdd(name, line[..64].ToLowerInvariant()))
                problems.Add($"{Path.GetFileName(path)}:{number} lists {name} twice");
        }
        if (entries.Count == 0) problems.Add($"{Path.GetFileName(path)} lists nothing");
        return entries;
    }

    private static void Compare(string path, string expected, string what, List<string> problems)
    {
        if (!File.Exists(path))
        {
            problems.Add($"{what} is missing");
            return;
        }
        using FileStream file = File.OpenRead(path);
        string actual = Convert.ToHexStringLower(SHA256.HashData(file));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what} differs from the receipt"));
    }
}
