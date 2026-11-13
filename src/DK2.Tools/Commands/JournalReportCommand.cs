// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DK2.Simulation;

namespace DK2.Tools.Commands;

internal static class JournalReportCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4) return Program.Usage();
        try
        {
            byte[] journalBytes = File.ReadAllBytes(args[1]);
            byte[] hashBytes = File.ReadAllBytes(args[2]);
            InputJournal journal = InputJournal.Read(new MemoryStream(journalBytes));
            JournalEvidenceValidation.Check(journalBytes, journal);
            SortedDictionary<long, ulong> replay = ReadHashes(hashBytes);
            ValidateJournal(journal);
            var comparison = new Comparison(journal, replay);
            string html = Render(journal, replay, comparison, args[1], args[2], journalBytes, hashBytes);
            using (var output = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                writer.Write(html);
            Console.WriteLine($"{comparison.Verdict}; report: {args[3]}");
            return comparison.ExitCode;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"journal-report: {ex.Message}");
            return 2;
        }
    }

    private static SortedDictionary<long, ulong> ReadHashes(byte[] bytes)
    {
        var result = new SortedDictionary<long, ulong>();
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true));
        int lineNumber = 0;
        long previous = -1;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            string[] fields = line.Split('\t');
            if (fields.Length != 2
                || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long tick)
                || tick <= previous || fields[1].Length is < 1 or > 16
                || !ulong.TryParse(fields[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong hash))
                throw new InvalidDataException($"Invalid hashes.tsv row {lineNumber}: expected increasing nonnegative tick, tab, hexadecimal hash.");
            result.Add(tick, hash);
            previous = tick;
        }
        return result;
    }

    private static void ValidateJournal(InputJournal journal)
    {
        if (journal.TicksRun is < 0 or long.MaxValue
            || journal.Hashes.Keys.Any(t => t < 0 || t == long.MaxValue || journal.TicksRun is { } end && t > end)
            || journal.FrameTicks.Any(t => t < 0 || t == long.MaxValue
                || journal.TicksRun is { } end && (t > end || t == end && journal.EndReason == "completed")))
            throw new InvalidDataException("Journal contains an invalid tick or a hash beyond its end record.");
    }

    private sealed class Comparison
    {
        public int Compared { get; }
        public int MissingReplay { get; }
        public int ReplayOnly { get; }
        public long MissingRecorded { get; }
        public long? FirstDivergence { get; }
        public long Focus { get; }
        public int ExitCode { get; }
        public string Verdict { get; }

        public Comparison(InputJournal journal, SortedDictionary<long, ulong> replay)
        {
            long? firstMissing = null;
            foreach ((long tick, ulong hash) in journal.Hashes.OrderBy(kv => kv.Key))
            {
                if (!replay.TryGetValue(tick, out ulong actual))
                {
                    MissingReplay++;
                    firstMissing ??= tick;
                    continue;
                }
                Compared++;
                if (hash != actual) FirstDivergence ??= tick;
            }
            ReplayOnly = replay.Keys.Count(t => !journal.Hashes.ContainsKey(t));
            long last = journal.TicksRun ?? journal.LastHashedTick;
            MissingRecorded = last < 0 ? 0 : last + 1 - journal.Hashes.Count;
            bool incomplete = journal.Truncated || journal.Hashes.Count == 0 || MissingRecorded > 0
                || MissingReplay > 0 || ReplayOnly > 0;
            ExitCode = FirstDivergence is not null ? 1 : incomplete ? 3 : 0;
            Verdict = FirstDivergence is { } different ? $"First divergent hash: tick {different}"
                : incomplete ? "Incomplete comparison" : "All supplied hashes match";
            Focus = FirstDivergence ?? firstMissing ?? Math.Max(0, last);
        }
    }

    private static string Render(InputJournal journal, SortedDictionary<long, ulong> replay, Comparison comparison,
        string journalPath, string hashPath, byte[] journalBytes, byte[] hashBytes)
    {
        var html = new StringBuilder();
        html.AppendLine("""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
            <title>Replay diagnostic report</title>
            <style>
            body{font:16px/1.5 system-ui,sans-serif;max-width:1100px;margin:2rem auto;padding:0 1rem;color:#20252b;background:#fafafa}
            table{border-collapse:collapse;width:100%;margin:1rem 0}th,td{border:1px solid #c7ccd1;padding:.45rem;text-align:left;vertical-align:top}
            th{background:#e8edf2}code,pre{font-family:ui-monospace,monospace;overflow-wrap:anywhere;white-space:pre-wrap}
            .different{background:#ffe4e1}.missing{background:#fff0cd}small{color:#4b5563}td{overflow-wrap:anywhere}
            </style></head><body><h1>Replay diagnostic report</h1>
            """);
        html.AppendLine($"<h2>{Encode(comparison.Verdict)}</h2>");
        html.AppendLine($"<p>Compared hashes: {comparison.Compared}<br>Missing replay hashes: {comparison.MissingReplay}<br>"
            + $"Replay hashes without a recorded hash: {comparison.ReplayOnly}<br>Missing recorded hashes: {comparison.MissingRecorded}</p>");
        if (journal.Truncated) html.AppendLine("<p class=\"missing\">Journal is truncated: no end record. Only surviving records are compared.</p>");
        if (journal.Hashes.Count == 0) html.AppendLine("<p class=\"missing\">No recorded hashes: there is no state evidence to compare.</p>");
        html.AppendLine("<p>Provenance is not verified: the supplied hashes.tsv has no build or scenario metadata. "
            + "Matching hashes describe these supplied records only; they do not establish DK2 parity or successful gameplay.</p>");
        html.AppendLine("<h2>Inputs and recorded context</h2><table><tr><th>Field</th><th>Value</th></tr>");
        Row("Journal", Path.GetFullPath(journalPath));
        Row("Journal SHA-256", System.Convert.ToHexString(SHA256.HashData(journalBytes)).ToLowerInvariant());
        Row("Replay hashes", Path.GetFullPath(hashPath));
        Row("Replay hashes SHA-256", System.Convert.ToHexString(SHA256.HashData(hashBytes)).ToLowerInvariant());
        Row("Hash version (journal)", journal.HashVersion.ToString(CultureInfo.InvariantCulture));
        Row("Recorded end tick", journal.TicksRun?.ToString(CultureInfo.InvariantCulture) ?? "unknown");
        Row("Recorded end reason", journal.EndReason ?? "unknown");
        foreach ((string key, string value) in journal.Header.OrderBy(kv => kv.Key, StringComparer.Ordinal)) Row(key, value);
        html.AppendLine("</table>");

        long start = Math.Max(0, comparison.Focus - 3);
        long end = comparison.Focus > long.MaxValue - 3 ? long.MaxValue : comparison.Focus + 3;
        html.AppendLine($"<h2>Hash context around tick {comparison.Focus}</h2>"
            + "<p>Hash tick T is the state after T ticks. Input tick T is applied by the next tick and can first affect hash T+1.</p>"
            + "<table><tr><th>Hash tick</th><th>Recorded</th><th>Replayed</th><th>Status</th></tr>");
        foreach (long tick in journal.Hashes.Keys.Concat(replay.Keys).Where(t => t >= start && t <= end).Distinct().Order())
        {
            bool recorded = journal.Hashes.TryGetValue(tick, out ulong expected);
            bool replayed = replay.TryGetValue(tick, out ulong actual);
            string status = !recorded ? "no recorded hash" : !replayed ? "no replay hash" : expected == actual ? "match" : "different";
            string css = !recorded || !replayed ? "missing" : expected != actual ? "different" : "";
            html.AppendLine($"<tr class=\"{css}\"><td>{tick}</td><td><code>{(recorded ? expected.ToString("x16") : "missing")}</code></td>"
                + $"<td><code>{(replayed ? actual.ToString("x16") : "missing")}</code></td><td>{status}</td></tr>");
        }
        html.AppendLine("</table><h2>Nearby recorded commands</h2><p>Commands retain their journal order within each input tick.</p>");
        bool commands = false;
        foreach (long tick in journal.FrameTicks.Where(t => t >= Math.Max(0, start - 1) && t <= end))
        {
            commands = true;
            string effect = journal.TicksRun == tick && journal.EndReason != "completed"
                ? "attempted; no completed tick"
                : $"can affect hash tick {tick + 1}";
            html.AppendLine($"<h3>Input tick {tick} ({effect})</h3><ol>");
            foreach (PlayerCommand command in journal.FrameAt(tick).All)
                html.AppendLine($"<li><code>{Encode(command.ToString())}</code></li>");
            html.AppendLine("</ol>");
        }
        if (!commands) html.AppendLine("<p>No recorded commands in this window.</p>");
        html.AppendLine("<p><small>This report compares existing evidence offline. It neither runs a replay nor reconstructs world state or subsystem causes.</small></p></body></html>");
        return html.ToString();

        void Row(string key, string value) => html.AppendLine($"<tr><th>{Encode(key)}</th><td><code>{Encode(value)}</code></td></tr>");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
