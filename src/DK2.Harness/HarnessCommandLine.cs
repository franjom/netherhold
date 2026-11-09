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
        CampaignPlayerSpec? campaign = null;
        if (Value(args, "--campaign-player") is string who)
        {
            if (!byte.TryParse(who, out byte id) || !PlayerIds.IsKeeper(id) || fuzz?.Player.Value == id) return null;
            if (Int(args, "--player-max-ticks", 14400) is not int maxTicks
                || Int(args, "--player-wall-seconds", 600) is not int wall
                || Int(args, "--player-interval-ticks", 4) is not int interval || interval > 40
                || Int(args, "--player-max-actions", 4096) is not int actions
                || Int(args, "--player-stall-ticks", 1200) is not int stall
                || Int(args, "--player-retries", 3) is not int retries
                || NonNegative(args, "--player-finale-ticks", 400) is not int finale
                || Int(args, "--player-output-mib", 64) is not int output || output < 2 || output > 1024 * 1024) return null;
            string? receipt = Value(args, "--player-receipt");
            if (Array.IndexOf(args, "--player-receipt") >= 0
                && (receipt is null || receipt.Length == 0 || receipt.StartsWith("--", StringComparison.Ordinal))) return null;
            campaign = new CampaignPlayerSpec(new PlayerId(id), maxTicks, wall, interval, actions, stall, retries, finale, output,
                receipt is null ? null : Path.GetFullPath(receipt));
        }
        else if (Array.Exists(args, a => a.StartsWith("--player-", StringComparison.Ordinal))) return null;
        // A misspelt limit must not silently fall back to its default.
        if (Array.Exists(args, a => a.StartsWith("--player-", StringComparison.Ordinal)
                                    && a != "--player-run-id" && Array.IndexOf(PlayerFlags, a) < 0)) return null;
        return new RunTemplate(fuzz, Array.IndexOf(args, "--coverage") >= 0, progress, snapshot) { Player = campaign };
    }

    /// <summary>The player-run flags that pass from <c>watch</c> to each run.</summary>
    public static readonly string[] PlayerFlags =
    [
        "--campaign-player", "--player-max-ticks", "--player-wall-seconds", "--player-interval-ticks",
        "--player-max-actions", "--player-stall-ticks", "--player-retries", "--player-finale-ticks", "--player-output-mib",
        "--player-receipt",
    ];

    /// <summary>
    /// One job per scenario, passing the run options through. With <c>--fuzz-seeds n</c>, one job
    /// per fuzz seed 1..n, replacing any seed in <c>--fuzz</c>; without it, <c>--fuzz</c> passes
    /// unchanged, so a watched run plays the game <c>run</c> would. Null when the options are
    /// malformed or <c>--fuzz-seeds</c> comes without <c>--fuzz</c>.
    /// </summary>
    public static List<WatchJob>? WatchJobs(IReadOnlyList<string> files, string[] args, int timeoutSeconds = 600)
    {
        if (RunOptions(args) is not { } template) return null;
        // Each watched run names its own folder; one run id for many runs would collide.
        if (Value(args, "--player-run-id") is not null) return null;
        string[] wall = [];
        if (template.Player is { } player)
        {
            // The run must stop itself, writing its verdict, before the watchdog kills it.
            if (Value(args, "--player-wall-seconds") is not null && player.WallSeconds >= timeoutSeconds) return null;
            if (Value(args, "--player-wall-seconds") is null)
                wall = ["--player-wall-seconds", Math.Max(1, timeoutSeconds - 30).ToString(System.Globalization.CultureInfo.InvariantCulture)];
        }
        string? fuzz = Value(args, "--fuzz");
        int? seeds = null;
        if (Value(args, "--fuzz-seeds") is not null)
        {
            if (fuzz is null || Int(args, "--fuzz-seeds", 1) is not int n) return null;
            seeds = n;
        }

        var common = new List<string>();
        if (Array.IndexOf(args, "--coverage") >= 0) common.Add("--coverage");
        foreach (string flag in (string[])["--progress", "--snapshot-check", .. PlayerFlags])
            if (Value(args, flag) is string v)
                // A child may run from another folder: it gets the receipt's absolute path.
                common.AddRange([flag, flag == "--player-receipt" ? template.Player!.Receipt! : v]);
        common.AddRange(wall);

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

    /// <summary>The fallback when the option is absent; null when present but not a whole number of at least 0.</summary>
    public static int? NonNegative(string[] args, string name, int fallback)
    {
        if (Array.IndexOf(args, name) < 0) return fallback;
        return Value(args, name) is string value && int.TryParse(value, out int parsed) && parsed >= 0 ? parsed : null;
    }

    /// <summary>The fallback when the option is absent; null when present but not a positive whole number.</summary>
    public static int? Int(string[] args, string name, int fallback)
    {
        if (Array.IndexOf(args, name) < 0) return fallback;
        return Value(args, name) is string value && int.TryParse(value, out int parsed) && parsed > 0 ? parsed : null;
    }
}
