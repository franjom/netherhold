// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DK2.Tools;

/// <summary>Git and repository-layout helpers shared by the dk2tools commands.</summary>
static class Repo
{
    /// <summary>
    /// The repository root, found by walking up for <c>DK2.slnx</c>.
    ///
    /// <para>Every path the commands read is relative to the repository, not
    /// to wherever the tool happened to be launched from.</para>
    /// </summary>
    internal static string? FindRepoRoot()
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DK2.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>One named option's value, or null.</summary>
    internal static string? OptionValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Runs git in the repository and returns stdout, or empty on failure.</summary>
    internal static string RunGit(string root, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using Process? process = Process.Start(psi);
            if (process is null) return string.Empty;
            // Drain stderr concurrently: forty changed files' line-ending warnings fill the pipe
            // and deadlock a reader that waits for stdout first.
            Task<string> errors = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            errors.Wait();
            process.WaitForExit();
            return output;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Working-tree, staged and untracked paths, relative to the root.</summary>
    internal static IEnumerable<string> ChangedFiles(string root, string baseRef)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        void Collect(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length > 0) set.Add(t);
            }
        }

        Collect(RunGit(root, "diff", "--name-only", baseRef));
        Collect(RunGit(root, "diff", "--cached", "--name-only", baseRef));
        Collect(RunGit(root, "ls-files", "--others", "--exclude-standard"));
        return set;
    }

    /// <summary>
    /// The paths a commit would actually carry. Deletions are excluded — there is
    /// nothing left in them to measure.
    /// </summary>
    internal static IEnumerable<string> StagedFiles(string root)
        => RunGit(root, "diff", "--cached", "--name-only", "--diff-filter=d").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

    /// <summary>
    /// One blob's lines, from the index (<c>:path</c>) or a commit (<c>ref:path</c>).
    ///
    /// <para>Git returns LF-terminated text whatever the working tree uses, so the
    /// carriage return and the trailing empty element are stripped here. A stray
    /// <c>\r</c> adds one to every measured line length, which on the question-row
    /// budget is the difference between passing and failing.</para>
    /// </summary>
    internal static string[] GitLines(string root, string spec)
    {
        string text = RunGit(root, "show", spec);
        if (text.Length == 0) return [];
        if (text.EndsWith('\n')) text = text[..^1];
        return text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
    }

    /// <summary>Every tracked path, for the --all backlog report.</summary>
    internal static IEnumerable<string> AllTrackedFiles(string root)
        => RunGit(root, "ls-files").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

    /// <summary>
    /// The 1-based line numbers this change adds to one file.
    ///
    /// <para>A file with no readable diff is treated as entirely new, which is the
    /// safe direction: a checker that stays silent because it could not read the
    /// diff is worse than one that over-reports.</para>
    /// </summary>
    internal static HashSet<int> AddedLineNumbers(string root, string baseRef, string relative, bool staged)
    {
        var added = new HashSet<int>();

        // Staged mode asks only the index. Falling back to the working tree there
        // would reintroduce exactly the leak this mode exists to close.
        string diff = staged
            ? RunGit(root, "diff", "--cached", "-U0", baseRef, "--", relative)
            : RunGit(root, "diff", "-U0", baseRef, "--", relative);
        if (!staged && diff.Trim().Length == 0)
            diff = RunGit(root, "diff", "--cached", "-U0", baseRef, "--", relative);

        if (diff.Trim().Length == 0)
        {
            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            int count = staged ? GitLines(root, $":{relative}").Length
                : File.Exists(full) ? File.ReadAllLines(full).Length
                : 0;
            for (int i = 1; i <= count; i++) added.Add(i);
            return added;
        }

        foreach (string line in diff.Split('\n'))
        {
            Match hunk = Regex.Match(line, @"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@");
            if (!hunk.Success) continue;

            int start = int.Parse(hunk.Groups[1].Value);
            int count = hunk.Groups[2].Success ? int.Parse(hunk.Groups[2].Value) : 1;
            for (int i = 0; i < count; i++) added.Add(start + i);
        }

        return added;
    }
}
