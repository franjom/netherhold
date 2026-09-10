// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;

namespace DK2.Simulation;

/// <summary>Compares trigger events without assuming that two turn origins coincide.</summary>
public static class TriggerCaptureComparison
{
    public static TriggerCaptureComparisonResult Compare(
        string oursPath, string capturePath, string? anchor = null)
    {
        if (oursPath is null) throw new ArgumentNullException(nameof(oursPath));
        if (capturePath is null) throw new ArgumentNullException(nameof(capturePath));

        using var ours = File.OpenText(oursPath);
        using var capture = File.OpenText(capturePath);
        return Compare(ours, oursPath, capture, capturePath, anchor);
    }

    public static TriggerCaptureComparisonResult Compare(
        TextReader ours, string oursName, TextReader capture, string captureName,
        string? anchor = null)
    {
        if (ours is null) throw new ArgumentNullException(nameof(ours));
        if (capture is null) throw new ArgumentNullException(nameof(capture));

        string[] columns = TriggerTurnCsvEmitter.Header.Split(',');
        CaptureData oursData = Read(ours, oursName, columns);
        CaptureData captureData = Read(capture, captureName, columns);
        string[] eventIds = columns.Skip(2).Take(TriggerState.TimerCount + TriggerState.FlagCount).ToArray();

        string[] inBoth = eventIds.Where(id => oursData.Events.ContainsKey(id)
            && captureData.Events.ContainsKey(id)).ToArray();
        List<string> captureOnly = eventIds.Where(id => !oursData.Events.ContainsKey(id)
            && captureData.Events.ContainsKey(id)).ToList();
        List<string> oursOnly = eventIds.Where(id => oursData.Events.ContainsKey(id)
            && !captureData.Events.ContainsKey(id)).ToList();

        string? anchorId = SelectAnchor(anchor, oursData, captureData);
        string? alignmentRefusal = null;
        long? oursOrigin = null;
        long? captureOrigin = null;
        var aligned = new List<TriggerAlignedEvent>();
        List<string> oursBeyond = [];
        List<string> captureBeyond = [];
        var stampSeries = new List<TriggerStampSeries>();

        if (anchorId is null)
        {
            alignmentRefusal = anchor is null
                ? "no timer is stamped on both sides"
                : $"anchor {anchor} is not stamped on both sides";
        }
        else
        {
            oursOrigin = oursData.Events[anchorId].FirstValue;
            captureOrigin = captureData.Events[anchorId].FirstValue;
            foreach (string id in inBoth.Where(id => id != anchorId))
            {
                EventObservation a = oursData.Events[id];
                EventObservation b = captureData.Events[id];
                long oursTurn = a.FirstTurn - oursOrigin.Value;
                long captureTurn = b.FirstTurn - captureOrigin.Value;
                long? residual = a.FirstTurnIsCeiling || b.FirstTurnIsCeiling
                    ? null
                    : captureTurn - oursTurn;
                aligned.Add(new TriggerAlignedEvent(
                    id, oursTurn, captureTurn, residual,
                    a.FirstTurnIsCeiling, b.FirstTurnIsCeiling));
            }

            oursBeyond = Extract(oursOnly, oursData, oursOrigin.Value,
                captureOrigin.Value, captureData.LastTurn);
            captureBeyond = Extract(captureOnly, captureData, captureOrigin.Value,
                oursOrigin.Value, oursData.LastTurn);

            for (byte timer = 0; timer < TriggerState.TimerCount; timer++)
            {
                string id = $"t{timer}";
                if (!oursData.Stamps.TryGetValue(id, out List<long>? mine)
                    || !captureData.Stamps.TryGetValue(id, out List<long>? theirs)) continue;
                // A stamp is a tick, so the run that ended first cannot speak to one written
                // after it stopped. Comparing the untruncated series makes a short capture
                // read as a disagreement, which is ADR-0118's mistake in a second place.
                long[] a = Within(mine, oursOrigin.Value, captureOrigin.Value, captureData.LastTurn);
                long[] b = Within(theirs, captureOrigin.Value, oursOrigin.Value, oursData.LastTurn);
                bool truncated = a.Length != mine.Count || b.Length != theirs.Count;
                stampSeries.Add(new TriggerStampSeries(id, a, b, a.SequenceEqual(b), truncated));
            }
        }

        return new TriggerCaptureComparisonResult(
            inBoth, captureOnly, oursOnly, oursBeyond, captureBeyond,
            anchorId, oursOrigin, captureOrigin, alignmentRefusal, aligned, stampSeries);
    }

    /// <summary>Stamps the other run was still running to record, origin-relative. The bound is
    /// strict because a sampled probe needs a further read to show a stamp written on its last
    /// tick.</summary>
    private static long[] Within(
        List<long> stamps, long origin, long otherOrigin, long otherLastTurn) =>
        [.. stamps.Select(v => v - origin).Where(v => v + otherOrigin < otherLastTurn)];

    /// <summary>Moves the one-sided events the other run stopped too early to see out of
    /// <paramref name="only"/>. A run that ends at turn 1218 says nothing about an event due
    /// at 1724, so counting that as a divergence reports a disagreement where there was no
    /// observation — which is how a Level 20 idle capture that matched in all 150 columns
    /// first read as "event set differed".</summary>
    private static List<string> Extract(
        List<string> only, CaptureData side, long origin, long otherOrigin, long otherLastTurn)
    {
        var beyond = new List<string>();
        foreach (string id in only)
        {
            EventObservation e = side.Events[id];
            if (e.FirstTurnIsCeiling) continue;
            if (e.FirstTurn - origin + otherOrigin > otherLastTurn) beyond.Add(id);
        }
        only.RemoveAll(beyond.Contains);
        return beyond;
    }

    private static CaptureData Read(TextReader reader, string name, string[] columns)
    {
        string? header = reader.ReadLine();
        if (header != TriggerTurnCsvEmitter.Header)
            throw new InvalidDataException($"'{name}' does not have the live trigger-probe header.");

        var events = new Dictionary<string, EventObservation>();
        var stamps = new Dictionary<string, List<long>>();
        bool firstDataRow = true;
        long lastTurn = long.MinValue;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            string[] cells = line.Split(',');
            if (cells.Length != columns.Length)
                throw new InvalidDataException(
                    $"'{name}' has a row of {cells.Length} columns; expected {columns.Length}.");
            if (!long.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long turn))
                throw new InvalidDataException($"'{name}' has an invalid tick '{cells[1]}'.");
            lastTurn = turn;

            int eventEnd = 2 + TriggerState.TimerCount + TriggerState.FlagCount;
            for (int col = 2; col < eventEnd; col++)
            {
                string cell = cells[col];
                if (!long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                    throw new InvalidDataException(
                        $"'{name}' has an invalid value '{cell}' in column {columns[col]}.");
                if (value != 0 && !events.ContainsKey(columns[col]))
                    events.Add(columns[col], new EventObservation(turn, value, firstDataRow));

                // A timer slot holds the tick the timer was started, so every distinct value
                // it takes is a tick the game (or we) recorded at the moment of acting. Unlike
                // the turn a change was noticed, that survives being read late.
                if (value != 0 && col < 2 + TriggerState.TimerCount)
                {
                    List<long> seen = stamps.TryGetValue(columns[col], out List<long>? list)
                        ? list
                        : stamps[columns[col]] = [];
                    if (seen.Count == 0 || seen[^1] != value) seen.Add(value);
                }
            }
            firstDataRow = false;
        }

        if (lastTurn == long.MinValue)
            throw new InvalidDataException($"'{name}' has a header but no data rows.");

        return new CaptureData(events, stamps, lastTurn);
    }

    private static string? SelectAnchor(
        string? requested, CaptureData ours, CaptureData capture)
    {
        if (requested is not null)
        {
            if (requested.Length < 2 || requested[0] != 't'
                || !byte.TryParse(requested.AsSpan(1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out byte timer)
                || timer >= TriggerState.TimerCount)
                throw new ArgumentException(
                    $"anchor must be t0..t{TriggerState.TimerCount - 1}, not '{requested}'.",
                    nameof(requested));
            return ours.Events.ContainsKey(requested) && capture.Events.ContainsKey(requested)
                ? requested
                : null;
        }

        for (byte timer = 0; timer < TriggerState.TimerCount; timer++)
        {
            string id = $"t{timer}";
            if (ours.Events.ContainsKey(id) && capture.Events.ContainsKey(id)) return id;
        }
        return null;
    }

    private sealed record CaptureData(
        Dictionary<string, EventObservation> Events,
        Dictionary<string, List<long>> Stamps,
        long LastTurn);
    private sealed record EventObservation(long FirstTurn, long FirstValue, bool FirstTurnIsCeiling);
}

/// <summary>One timer's stored stamps on each side, relative to the anchor's origin. A stamp
/// is the tick the game recorded when it started the timer, so it is exact however late the
/// probe read it — the only quantity a live capture yields with no sampling in the path.</summary>
public sealed record TriggerStampSeries(
    string Id, IReadOnlyList<long> Ours, IReadOnlyList<long> Capture, bool Agrees,
    bool Truncated);

public sealed record TriggerAlignedEvent(
    string Id,
    long OursTurn,
    long CaptureTurn,
    long? Residual,
    bool OursTurnIsCeiling,
    bool CaptureTurnIsCeiling);

public sealed class TriggerCaptureComparisonResult
{
    internal TriggerCaptureComparisonResult(
        IReadOnlyList<string> inBoth,
        IReadOnlyList<string> captureOnly,
        IReadOnlyList<string> oursOnly,
        IReadOnlyList<string> oursBeyondCapture,
        IReadOnlyList<string> captureBeyondOurs,
        string? anchorId,
        long? oursOrigin,
        long? captureOrigin,
        string? alignmentRefusal,
        IReadOnlyList<TriggerAlignedEvent> alignedEvents,
        IReadOnlyList<TriggerStampSeries> stampSeries)
    {
        StampSeries = stampSeries;
        InBoth = inBoth;
        CaptureOnly = captureOnly;
        OursOnly = oursOnly;
        OursBeyondCapture = oursBeyondCapture;
        CaptureBeyondOurs = captureBeyondOurs;
        AnchorId = anchorId;
        OursOrigin = oursOrigin;
        CaptureOrigin = captureOrigin;
        AlignmentRefusal = alignmentRefusal;
        AlignedEvents = alignedEvents;
    }

    public IReadOnlyList<string> InBoth { get; }
    public IReadOnlyList<string> CaptureOnly { get; }
    public IReadOnlyList<string> OursOnly { get; }

    /// <summary>Our events due after the capture stopped, and the capture's due after our run
    /// ended. Neither side disagrees about these; neither side looked.</summary>
    public IReadOnlyList<string> OursBeyondCapture { get; }
    public IReadOnlyList<string> CaptureBeyondOurs { get; }

    public bool EventSetsMatch => CaptureOnly.Count == 0 && OursOnly.Count == 0;
    public string? AnchorId { get; }
    public long? OursOrigin { get; }
    public long? CaptureOrigin { get; }
    public string? AlignmentRefusal { get; }
    public IReadOnlyList<TriggerAlignedEvent> AlignedEvents { get; }

    /// <summary>Per-timer stored stamps, the exact half of the comparison.</summary>
    public IReadOnlyList<TriggerStampSeries> StampSeries { get; }

    /// <summary>True when every timer stamped on both sides holds the same stored values.
    /// This is the claim a sampled residual cannot make.</summary>
    public bool StampsAgree => StampSeries.All(s => s.Agrees);
    public long? LargestAbsoluteResidual => AlignedEvents
        .Select(e => e.Residual.HasValue ? Math.Abs(e.Residual.Value) : (long?)null)
        .Max();
}
