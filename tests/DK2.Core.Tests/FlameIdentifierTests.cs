// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;

namespace DK2.Core.Tests;

/// <summary>Flame — DiaLight's partial recompilation of DK2 v1.70 — publishes
/// no licence, so this project holds no reuse rights to it at all. ADR-0080: a
/// Flame name may be <i>cited</i> in a document, and never appears in the
/// source tree — not as a member, not as a type, not in a comment.
/// </summary>
/// <remarks>
/// It matches a shape, not a list: a denylist would itself be Flame names
/// pasted into the repository. The shapes are Flame's hex-offset field prefix
/// (<c>f</c>, two to four hex digits, underscore) and the serialised-data
/// prefix its save-format work uses. A floor, not a fence: a Flame class name
/// in a comment slips through, and the rule still rests on review.
/// </remarks>
public class FlameIdentifierTests
{
    /// <summary>
    /// The shape of a Flame offset-prefixed member, and Flame's serialised-data
    /// prefix. The first must be preceded by something that is not an
    /// identifier character, so that a project name merely ending in such a
    /// sequence does not trip it; the hex digits are upper-case because that is
    /// how Flame writes them and lower-case would match too much English.
    /// </summary>
    private static readonly Regex FlameShaped = new(
        @"(?<![A-Za-z0-9_])f[0-9A-F]{2,4}_[A-Za-z]|(?<![A-Za-z0-9])sData_",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void NoSourceFile_CarriesAFlameShapedName()
    {
        string root = RepositoryRoot();
        List<string> files = SourceFiles(root).ToList();

        // Same floor as LicenseHeaderTests, for the same reason: a sweep that
        // finds nothing must fail rather than pass.
        Assert.True(files.Count > 100,
            $"Only {files.Count} source files found under src, tests and the Unity " +
            "adapter. The sweep is not looking where the code is.");

        var offenders = new List<string>();
        foreach (string file in files)
        {
            // This file is exempt only where it documents the pattern; it names
            // no Flame identifier itself.
            if (string.Equals(Path.GetFileName(file), "FlameIdentifierTests.cs", StringComparison.Ordinal))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (FlameShaped.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} line(s) carry a Flame-shaped identifier. Describe what Flame " +
            "established in this project's own words instead (ADR-0080):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The pattern recognises what it is meant to and leaves ordinary names
    /// alone. The positive samples are fabricated to the shape; none is a name
    /// Flame uses.
    /// </summary>
    [Theory]
    [InlineData("x.f45_something", true)]
    [InlineData("f2D_firstThing", true)]
    [InlineData("record.sData_anything", true)]
    [InlineData("Half_Width", false)]
    [InlineData("buf12_bytes", false)]
    [InlineData("0x1f_Mask", false)]
    [InlineData("f_underscoreOnly", false)]
    [InlineData("isData_", false)]
    public void TheShape_IsRecognised(string sample, bool expected)
    {
        Assert.Equal(expected, FlameShaped.IsMatch(sample));
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        string[] roots =
        [
            Path.Combine(root, "src"),
            Path.Combine(root, "tests"),
            Path.Combine(root, "unity", "DK2.Unity", "Assets"),
        ];

        string obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        string bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";

        return roots
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(obj, StringComparison.Ordinal) &&
                        !f.Contains(bin, StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DK2.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root (no DK2.slnx above the test binaries).");
    }
}
