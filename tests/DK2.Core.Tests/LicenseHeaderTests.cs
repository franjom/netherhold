// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;

namespace DK2.Core.Tests;

/// <summary>Connects the licence's two written forms, which nothing else does:
/// <c>LICENSE</c> (the GNU GPL version 3, verbatim, for humans and GitHub's
/// sidebar) and <c>license.txt</c> (the three-line per-file notice, as the
/// FreeMarker header template a stamping IDE reads — OpenKeeper's shape, and
/// why a lower-case <c>license.txt</c> sits beside an upper-case
/// <c>LICENSE</c>). The notice carries the whole grant in one token, so a wrong
/// or drifted SPDX identifier leaves no prose to fall back on. These tests are
/// also the only enforcement of the notice on source files: IDE0073 with
/// <c>file_header_template</c> is deliberately unused — it is blind to the
/// Unity adapter and truncates a template at the first <c>;</c> in silence
/// (<c>.editorconfig</c> records why).
/// </summary>
public class LicenseHeaderTests
{
    /// <summary>
    /// Every source file starts with the notice — what makes <c>LICENSE</c>
    /// apply to the code rather than merely sit beside it. Covers the three
    /// files under <c>unity/DK2.Unity/Assets</c>, which no project in
    /// <c>DK2.slnx</c> compiles — only <c>unity/DK2.Unity.Compile</c> does, and
    /// that needs an installed Unity editor — so a build-time analyser is
    /// structurally blind to them.
    /// </summary>
    [Fact]
    public void EverySourceFile_StartsWithTheNotice()
    {
        string root = RepositoryRoot();
        string expected = string.Join("\n", NoticeFromLicenseTxt()
            .Split('\n')
            .Select(l => l.Length == 0 ? "//" : "// " + l));

        List<string> files = SourceFiles(root).ToList();

        // A sweep that finds nothing would pass, which would make this test a
        // decoration rather than a check. The floor sits deliberately far below
        // the real file count: it catches a broken search, and is not a count.
        Assert.True(files.Count > 100,
            $"Only {files.Count} source files found under src, tests and the Unity " +
            "adapter. The sweep is not looking where the code is.");

        List<string> offenders = files
            .Where(f => !File.ReadAllText(f).Replace("\r\n", "\n")
                             .StartsWith(expected + "\n\n", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} of {files.Count} source files do not open with the " +
            "notice from license.txt:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The notice grants GPL version 3 <i>or later</i>, under a copyright line,
    /// and says where the full text is. The "or later" half is the part worth
    /// asserting: <c>GPL-3.0-only</c>, or a bare <c>GPL-3.0</c>, is a narrower
    /// grant than this project made, and a one-word edit nothing else notices.
    /// </summary>
    [Fact]
    public void TheNotice_DeclaresGpl3OrLater()
    {
        string[] notice = NoticeFromLicenseTxt().Split('\n');

        Assert.StartsWith("Copyright (C) ", notice[0]);
        Assert.Equal("GPL-3.0-or-later", SpdxIdentifier());
        Assert.Contains(notice, l => l.Contains("LICENSE", StringComparison.Ordinal));
    }

    /// <summary>
    /// ...and the licence file it points at is really the version the notice
    /// claims. Catches <c>LICENSE</c> being swapped for a different GPL — v2,
    /// say — while every source file goes on declaring
    /// <c>GPL-3.0-or-later</c>. Under a short notice that mismatch is
    /// invisible: the files no longer quote the licence, they only name it.
    /// </summary>
    [Fact]
    public void TheLicenceFile_IsTheVersionTheNoticeClaims()
    {
        string spdx = SpdxIdentifier();

        Match parsed = Regex.Match(spdx, @"^GPL-(?<major>\d+)\.\d+-or-later$");
        Assert.True(parsed.Success,
            $"The notice's SPDX identifier '{spdx}' is not a GPL-<version>-or-later " +
            "identifier, so there is no version to check LICENSE against.");

        string licence = File.ReadAllText(Path.Combine(RepositoryRoot(), "LICENSE"));

        Assert.Contains("GNU GENERAL PUBLIC LICENSE", licence);
        Assert.Contains($"Version {parsed.Groups["major"].Value},", licence);
    }

    /// <summary>
    /// The <c>SPDX-License-Identifier</c> the notice declares, asserting there
    /// is exactly one — two would leave it ambiguous which one governs.
    /// </summary>
    private static string SpdxIdentifier()
    {
        const string key = "SPDX-License-Identifier:";

        string[] declared = NoticeFromLicenseTxt()
            .Split('\n')
            .Where(l => l.StartsWith(key, StringComparison.Ordinal))
            .ToArray();

        Assert.Single(declared);
        return declared[0][key.Length..].Trim();
    }

    /// <summary>
    /// Renders <c>license.txt</c> the way a header-stamping IDE would: drop the
    /// FreeMarker scaffolding that opens and closes the comment block, and strip
    /// the per-line comment prefix, leaving the notice itself.
    /// </summary>
    private static string NoticeFromLicenseTxt()
    {
        const string prefix = "${licensePrefix}";

        var notice = new List<string>();
        foreach (string line in File.ReadAllLines(Path.Combine(RepositoryRoot(), "license.txt")))
        {
            // <#if licenseFirst??> / ${licenseFirst} / </#if> and their closing
            // counterparts: the IDE's business, not part of the notice.
            if (line.StartsWith("<#", StringComparison.Ordinal) ||
                line.StartsWith("</#", StringComparison.Ordinal) ||
                line is "${licenseFirst}" or "${licenseLast}")
            {
                continue;
            }

            // Anything else is either a prefixed text line, or the
            // ${licensePrefix?replace(...)} form, which is how the template
            // writes a blank line without leaving trailing whitespace behind.
            notice.Add(line.StartsWith(prefix, StringComparison.Ordinal)
                ? line[prefix.Length..]
                : string.Empty);
        }

        return string.Join("\n", notice).TrimEnd();
    }

    /// <summary>
    /// Every first-party C# file in the repository. <c>obj</c> and <c>bin</c>
    /// are excluded because the SDK writes generated sources there
    /// (<c>*.GlobalUsings.g.cs</c> and friends), which are not ours to licence;
    /// Unity's <c>Library/PackageCache</c> is outside these roots for the same
    /// reason, and is gitignored besides.
    /// </summary>
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
