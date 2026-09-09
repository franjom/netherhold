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
        string[] captureOnly = eventIds.Where(id => !oursData.Events.ContainsKey(id)
            && captureData.Events.ContainsKey(id)).ToArray();
        string[] oursOnly = eventIds.Where(id => oursData.Events.ContainsKey(id)
            && !captureData.Events.ContainsKey(id)).ToArray();

        string? anchorId = SelectAnchor(anchor, oursData, captureData);
        string? alignmentRefusal = null;
        long? oursOrigin = null;
        long? captureOrigin = null;
        var aligned = new List<TriggerAlignedEvent>();

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
        }

        return new TriggerCaptureComparisonResult(
            inBoth, captureOnly, oursOnly, anchorId, oursOrigin, captureOrigin,
            alignmentRefusal, aligned);
    }

    private static CaptureData Read(TextReader reader, string name, string[] columns)
    {
        string? header = reader.ReadLine();
        if (header != TriggerTurnCsvEmitter.Header)
            throw new InvalidDataException($"'{name}' does not have the live trigger-probe header.");

        var events = new Dictionary<string, EventObservation>();
        bool firstDataRow = true;
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

            int eventEnd = 2 + TriggerState.TimerCount + TriggerState.FlagCount;
            for (int col = 2; col < eventEnd; col++)
            {
                string cell = cells[col];
                if (!long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                    throw new InvalidDataException(
                        $"'{name}' has an invalid value '{cell}' in column {columns[col]}.");
                if (value != 0 && !events.ContainsKey(columns[col]))
                    events.Add(columns[col], new EventObservation(turn, value, firstDataRow));
            }
            firstDataRow = false;
        }

        return new CaptureData(events);
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

    private sealed record CaptureData(Dictionary<string, EventObservation> Events);
    private sealed record EventObservation(long FirstTurn, long FirstValue, bool FirstTurnIsCeiling);
}

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
        string? anchorId,
        long? oursOrigin,
        long? captureOrigin,
        string? alignmentRefusal,
        IReadOnlyList<TriggerAlignedEvent> alignedEvents)
    {
        InBoth = inBoth;
        CaptureOnly = captureOnly;
        OursOnly = oursOnly;
        AnchorId = anchorId;
        OursOrigin = oursOrigin;
        CaptureOrigin = captureOrigin;
        AlignmentRefusal = alignmentRefusal;
        AlignedEvents = alignedEvents;
    }

    public IReadOnlyList<string> InBoth { get; }
    public IReadOnlyList<string> CaptureOnly { get; }
    public IReadOnlyList<string> OursOnly { get; }
    public bool EventSetsMatch => CaptureOnly.Count == 0 && OursOnly.Count == 0;
    public string? AnchorId { get; }
    public long? OursOrigin { get; }
    public long? CaptureOrigin { get; }
    public string? AlignmentRefusal { get; }
    public IReadOnlyList<TriggerAlignedEvent> AlignedEvents { get; }
    public long? LargestAbsoluteResidual => AlignedEvents
        .Select(e => e.Residual.HasValue ? Math.Abs(e.Residual.Value) : (long?)null)
        .Max();
}
