// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Harness;

/// <summary>The <c>run</c> and <c>watch</c> options a command line asks for (ADR-0552 Stage C).</summary>
public static class HarnessCommandLine
{
    /// <summary>The run options, or null when one is malformed.</summary>
    public static RunTemplate? RunOptions(string[] args)
    {
        FuzzSpec? fuzz = null;
        if (Value(args, "--fuzz") is string spec)
        {
            string[] parts = spec.Split(':');
            if (parts.Length > 3 || !byte.TryParse(parts[0], out byte player)) return null;
            ulong? seed = null;
            int interval = 4;
            if (parts.Length > 1)
            {
                if (!ulong.TryParse(parts[1], out ulong s)) return null;
                seed = s;
            }
            if (parts.Length > 2 && (!int.TryParse(parts[2], out interval) || interval < 1)) return null;
            fuzz = new FuzzSpec(new PlayerId(player), seed, interval);
        }
        if (Int(args, "--progress", fuzz is null ? 0 : 40) is not int progress) return null;
        if (Int(args, "--snapshot-check", 0) is not int snapshot) return null;
        return new RunTemplate(fuzz, Array.IndexOf(args, "--coverage") >= 0, progress, snapshot);
    }

    /// <summary>
    /// One job per scenario, passing the run options through. With <c>--fuzz-seeds n</c>, one job
    /// per fuzz seed 1..n, replacing any seed in <c>--fuzz</c>; without it, <c>--fuzz</c> passes
    /// unchanged, so a watched run plays the game <c>run</c> would. Null when the options are
    /// malformed or <c>--fuzz-seeds</c> comes without <c>--fuzz</c>.
    /// </summary>
    public static List<WatchJob>? WatchJobs(IReadOnlyList<string> files, string[] args)
    {
        if (RunOptions(args) is null) return null;
        string? fuzz = Value(args, "--fuzz");
        int? seeds = null;
        if (Value(args, "--fuzz-seeds") is not null)
        {
            if (fuzz is null || Int(args, "--fuzz-seeds", 1) is not int n) return null;
            seeds = n;
        }

        var common = new List<string>();
        if (Array.IndexOf(args, "--coverage") >= 0) common.Add("--coverage");
        foreach (string flag in (string[])["--progress", "--snapshot-check"])
            if (Value(args, flag) is string v) common.AddRange([flag, v]);

        var jobs = new List<WatchJob>();
        foreach (string file in files)
        {
            if (fuzz is null) jobs.Add(new WatchJob(file, common, ""));
            else if (seeds is null) jobs.Add(new WatchJob(file, [.. common, "--fuzz", fuzz], $"fuzz {fuzz}"));
            else
            {
                string[] parts = fuzz.Split(':');
                for (int seed = 1; seed <= seeds; seed++)
                {
                    string spec = parts.Length > 2 ? $"{parts[0]}:{seed}:{parts[2]}" : $"{parts[0]}:{seed}";
                    jobs.Add(new WatchJob(file, [.. common, "--fuzz", spec], $"fuzz seed {seed}"));
                }
            }
        }
        return jobs;
    }

    public static string? Value(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>The fallback when the option is absent; null when present but not a positive whole number.</summary>
    public static int? Int(string[] args, string name, int fallback) =>
        Value(args, name) is not string value ? fallback
        : int.TryParse(value, out int parsed) && parsed > 0 ? parsed : null;
}
