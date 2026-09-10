// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Xunit;

namespace DK2.TestSupport;

/// <summary>Owns the optional live-capture paths and their test skip policy. Captures are
/// evidence, not code: they live under the gitignored <c>reverse-engineering/</c>, so every
/// test that reads one skips rather than fails where they are absent.</summary>
public static class Dk2CaptureGate
{
    public const string Level1IdleRelativePath = "reverse-engineering/dk2runs/dk2run6.csv";
    public const string Level20IdleRelativePath = "reverse-engineering/dk2runs/dk2run7.csv";

    public static string Path => PathFor(Level1IdleRelativePath);

    public static string? SkipReason => SkipReasonFor(Level1IdleRelativePath);

    public static string PathFor(string relativePath) => System.IO.Path.Combine(
        RepositoryRoot(), relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public static string? SkipReasonFor(string relativePath)
    {
        if (!Dk2InstallGate.Present) return Dk2InstallGate.SkipReason;
        return File.Exists(PathFor(relativePath))
            ? null
            : $"Missing {relativePath}; ask the owner for a copy.";
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "DK2.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root (no DK2.slnx above the test binaries).");
    }
}

/// <summary><c>[Fact]</c> that skips unless the DK2 install and the named idle capture exist.
/// Defaults to the level-1 capture, which is the one most tests read.</summary>
public sealed class CaptureFactAttribute : FactAttribute
{
    public CaptureFactAttribute(
        string relativePath = Dk2CaptureGate.Level1IdleRelativePath)
        => Skip = Dk2CaptureGate.SkipReasonFor(relativePath);
}
