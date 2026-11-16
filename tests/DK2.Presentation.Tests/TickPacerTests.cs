// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;

namespace DK2.Presentation.Tests;

public sealed class TickPacerTests
{
    private const string TimeManager = "unity/DK2.Unity/ProjectSettings/TimeManager.asset";
    private const string TimestepKey = "Maximum Allowed Timestep:";

    [Fact]
    public void TheFrameClampIsUnitysMaximumTimestep()
    {
        string path = Path.Combine(RepositoryRoot(), TimeManager.Replace('/', Path.DirectorySeparatorChar));
        string? line = File.ReadLines(path).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(TimestepKey, StringComparison.Ordinal));
        Assert.NotNull(line);
        float unity = float.Parse(line[TimestepKey.Length..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.Equal((float)TickPacer.MaximumFrameSeconds, unity);
    }

    [Fact]
    public void AFrameRunsAtMostTwoTicks() => Assert.Equal(2, TickPacer.DefaultMaxTicksPerFrame);

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DK2.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root (no DK2.slnx above the test binaries).");
    }
}
