// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Xunit;

namespace DK2.TestSupport;

/// <summary>Owns the optional level-1 idle capture path and its test skip policy.</summary>
public static class Dk2CaptureGate
{
    public const string Level1IdleRelativePath = "reverse-engineering/dk2runs/dk2run6.csv";

    public static string Path => System.IO.Path.Combine(RepositoryRoot(),
        Level1IdleRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public static string? SkipReason
    {
        get
        {
            if (!Dk2InstallGate.Present) return Dk2InstallGate.SkipReason;
            return File.Exists(Path)
                ? null
                : $"Missing {Level1IdleRelativePath}; ask the owner for a copy.";
        }
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

/// <summary><c>[Fact]</c> that skips unless the DK2 install and idle capture exist.</summary>
public sealed class CaptureFactAttribute : FactAttribute
{
    public CaptureFactAttribute() => Skip = Dk2CaptureGate.SkipReason;
}
