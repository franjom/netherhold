// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text;

namespace DK2.Presentation;

/// <summary>What a performance run samples once per frame (the harness, migration plan step 3o). The adapter fills
/// each from its engine; <see cref="FrameRecording.MetricName"/> is the report's key.</summary>
public enum FrameMetric
{
    /// <summary>Wall time from one frame's start to the next, in milliseconds.</summary>
    FrameMs,
    /// <summary>The scene's process callbacks, first to last, in milliseconds.</summary>
    ProcessMs,
    /// <summary>The renderer's CPU time for the viewport, in milliseconds.</summary>
    RenderCpuMs,
    /// <summary>The GPU's time for the viewport, in milliseconds; zero when the driver gives none. The engine returns the
    /// latest the GPU has finished, about two frames behind the frame it is filed with.</summary>
    RenderGpuMs,
    /// <summary>The rendering server's frame setup on the CPU, in milliseconds.</summary>
    SetupCpuMs,
    DrawCalls,
    Objects,
    Primitives,
    VideoMemoryMiB,
    TextureMemoryMiB,
    BufferMemoryMiB,
    /// <summary>Managed bytes the main thread allocated during the frame (all threads: the report's counters).</summary>
    AllocatedBytes,
    Gen0Collections,
    Gen1Collections,
    Gen2Collections,
    PipelineCompilations,
}

/// <summary>Which frames a statistic covers: all, those whose drawn camera did not move, those whose did.</summary>
public enum FramePhase
{
    All,
    Still,
    Moving,
}

/// <summary>A metric's distribution over one phase: nearest-rank percentiles (the smallest sample with at least that
/// share of the samples at or below it). With no samples every value is zero.</summary>
public readonly record struct FrameStatistics(int Count, double Mean, double P50, double P95, double P99, double Max);

/// <summary>A performance run's per-frame samples in one preallocated buffer, so recording allocates nothing (the
/// harness, migration plan step 3o), and their summary and report. Frames past the capacity are counted and dropped.</summary>
public sealed class FrameRecording
{
    /// <summary>The 60 FPS floor's frame budget (docs/PERFORMANCE.md): a frame over it, not at it, is a slow frame.</summary>
    public const double BudgetMilliseconds = 1000.0 / 60;

    public static readonly int MetricCount = Enum.GetValues(typeof(FrameMetric)).Length;

    private static readonly string[] Names =
    [
        "frameMs", "processMs", "renderCpuMs", "renderGpuMs", "setupCpuMs", "drawCalls", "objects", "primitives",
        "videoMemoryMiB", "textureMemoryMiB", "bufferMemoryMiB", "allocatedBytes", "gen0Collections", "gen1Collections", "gen2Collections", "pipelineCompilations",
    ];

    private readonly double[] _values;
    private readonly bool[] _moving;
    private double[]? _scratch;

    public FrameRecording(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _values = new double[capacity * MetricCount];
        _moving = new bool[capacity];
    }

    public int Capacity => _moving.Length;
    public int Count { get; private set; }
    public int Dropped { get; private set; }

    public static string MetricName(FrameMetric metric) => Names[(int)metric];

    public static string PhaseName(FramePhase phase) => phase switch
    {
        FramePhase.Still => "still",
        FramePhase.Moving => "moving",
        _ => "all",
    };

    /// <summary>The next frame's row, indexed by <see cref="FrameMetric"/>, for the caller to fill; empty once full (the
    /// frame is counted in <see cref="Dropped"/>). Allocation-free.</summary>
    public Span<double> Add(bool cameraMoved)
    {
        if (Count == Capacity)
        {
            Dropped++;
            return Span<double>.Empty;
        }
        _moving[Count] = cameraMoved;
        Span<double> row = _values.AsSpan(Count * MetricCount, MetricCount);
        row.Clear();
        Count++;
        return row;
    }

    public double Value(int frame, FrameMetric metric) => _values[frame * MetricCount + (int)metric];

    public bool Moving(int frame) => _moving[frame];

    public FrameStatistics Statistics(FramePhase phase, FrameMetric metric)
    {
        double[] scratch = _scratch ??= new double[Capacity];
        int n = 0;
        double sum = 0;
        for (int i = 0; i < Count; i++)
        {
            if (!InPhase(i, phase))
            {
                continue;
            }

            double value = Value(i, metric);
            scratch[n++] = value;
            sum += value;
        }
        if (n == 0)
        {
            return default;
        }

        Array.Sort(scratch, 0, n);
        return new FrameStatistics(n, sum / n, Rank(scratch, n, 50), Rank(scratch, n, 95), Rank(scratch, n, 99), scratch[n - 1]);
    }

    /// <summary>The phase's frames over <see cref="BudgetMilliseconds"/>.</summary>
    public int SlowFrames(FramePhase phase)
    {
        int slow = 0;
        for (int i = 0; i < Count; i++)
        {
            if (InPhase(i, phase) && Value(i, FrameMetric.FrameMs) > BudgetMilliseconds)
            {
                slow++;
            }
        }

        return slow;
    }

    /// <summary>The longest run of consecutive slow frames, whatever their phase.</summary>
    public int LongestSlowRun()
    {
        int longest = 0, run = 0;
        for (int i = 0; i < Count; i++)
        {
            run = Value(i, FrameMetric.FrameMs) > BudgetMilliseconds ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        return longest;
    }

    /// <summary>A table for the log: per phase, each metric's mean, p50, p95, p99 and max.</summary>
    public string Table()
    {
        var text = new StringBuilder();
        text.Append(FormattableString.Invariant($"{Count} frames ({Dropped} dropped), longest slow run {LongestSlowRun()}"));
        foreach (FramePhase phase in new[] { FramePhase.All, FramePhase.Still, FramePhase.Moving })
        {
            FrameStatistics frames = Statistics(phase, FrameMetric.FrameMs);
            text.AppendLine();
            text.Append(FormattableString.Invariant(
                $"  {PhaseName(phase)}: {frames.Count} frames, {SlowFrames(phase)} over {BudgetMilliseconds:0.00} ms"));
            if (frames.Count == 0)
            {
                continue;
            }

            text.AppendLine();
            text.Append("    metric                          mean          p50          p95          p99          max");
            for (int m = 0; m < MetricCount; m++)
            {
                FrameStatistics s = Statistics(phase, (FrameMetric)m);
                text.AppendLine();
                text.Append(FormattableString.Invariant(
                    $"    {Names[m],-24} {s.Mean,12:0.###} {s.P50,12:0.###} {s.P95,12:0.###} {s.P99,12:0.###} {s.Max,12:0.###}"));
            }
        }
        return text.ToString();
    }

    /// <summary>The machine-readable report (schema <c>dk2-godot-perf/1</c>): the run's <paramref name="info"/> strings,
    /// its <paramref name="arguments"/>, the adapter's step <paramref name="counters"/>, then per phase each metric's
    /// statistics and the slow frames.</summary>
    public string Json(IReadOnlyList<KeyValuePair<string, string>> info, IReadOnlyList<string> arguments,
        IReadOnlyList<KeyValuePair<string, double>> counters, int warmUpFrames)
    {
        var json = new StringBuilder();
        json.Append("{\n  \"schema\": \"dk2-godot-perf/1\",\n  \"info\": {");
        for (int i = 0; i < info.Count; i++)
        {
            json.Append(i == 0 ? "\n    " : ",\n    ").Append(Quote(info[i].Key)).Append(": ").Append(Quote(info[i].Value));
        }

        json.Append("\n  },\n  \"arguments\": [");
        for (int i = 0; i < arguments.Count; i++)
        {
            json.Append(i == 0 ? "" : ", ").Append(Quote(arguments[i]));
        }

        json.Append("],\n  \"frames\": {\"warmUp\": ").Append(warmUpFrames.ToString(CultureInfo.InvariantCulture))
            .Append(", \"measured\": ").Append(Count.ToString(CultureInfo.InvariantCulture))
            .Append(", \"dropped\": ").Append(Dropped.ToString(CultureInfo.InvariantCulture))
            .Append(", \"longestSlowRun\": ").Append(LongestSlowRun().ToString(CultureInfo.InvariantCulture))
            .Append(", \"budgetMs\": ").Append(Number(BudgetMilliseconds)).Append("},\n  \"counters\": {");
        for (int i = 0; i < counters.Count; i++)
        {
            json.Append(i == 0 ? "\n    " : ",\n    ").Append(Quote(counters[i].Key)).Append(": ").Append(Number(counters[i].Value));
        }

        json.Append("\n  },\n  \"phases\": {");
        foreach (FramePhase phase in new[] { FramePhase.All, FramePhase.Still, FramePhase.Moving })
        {
            json.Append(phase == FramePhase.All ? "\n    " : ",\n    ").Append(Quote(PhaseName(phase))).Append(": {\"frames\": ")
                .Append(Statistics(phase, FrameMetric.FrameMs).Count.ToString(CultureInfo.InvariantCulture))
                .Append(", \"slowFrames\": ").Append(SlowFrames(phase).ToString(CultureInfo.InvariantCulture));
            for (int m = 0; m < MetricCount; m++)
            {
                FrameStatistics s = Statistics(phase, (FrameMetric)m);
                json.Append(",\n      ").Append(Quote(Names[m])).Append(": {\"mean\": ").Append(Number(s.Mean))
                    .Append(", \"p50\": ").Append(Number(s.P50)).Append(", \"p95\": ").Append(Number(s.P95))
                    .Append(", \"p99\": ").Append(Number(s.P99)).Append(", \"max\": ").Append(Number(s.Max)).Append('}');
            }
            json.Append('}');
        }
        json.Append("\n  }\n}\n");
        return json.ToString();
    }

    private bool InPhase(int frame, FramePhase phase) =>
        phase == FramePhase.All || _moving[frame] == (phase == FramePhase.Moving);

    private static double Rank(double[] sorted, int count, int percent)
    {
        int index = (int)Math.Ceiling(percent / 100.0 * count) - 1;
        return sorted[Math.Min(count - 1, Math.Max(0, index))];
    }

    private static string Number(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? "null" : value.ToString("R", CultureInfo.InvariantCulture);

    private static string Quote(string text)
    {
        StringBuilder quoted = new StringBuilder(text.Length + 2).Append('"');
        foreach (char c in text)
        {
            if (c == '"' || c == '\\')
            {
                quoted.Append('\\').Append(c);
            }
            else if (c < ' ')
            {
                quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                quoted.Append(c);
            }
        }
        return quoted.Append('"').ToString();
    }
}
