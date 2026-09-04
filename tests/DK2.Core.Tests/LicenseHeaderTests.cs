// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace DK2.Core.Tests;

/// <summary>
/// The MIT notice is written down in three places, for three different
/// consumers, and nothing but this test connects them:
///
/// <list type="bullet">
/// <item><c>LICENSE</c> — the licence itself, for humans and for GitHub's
/// repository sidebar.</item>
/// <item><c>license.txt</c> — a FreeMarker header template, for IDEs that stamp
/// a notice into newly created files. This is the form OpenKeeper uses, and the
/// reason this repository has a lower-case <c>license.txt</c> next to an
/// upper-case <c>LICENSE</c> at all.</item>
/// <item><c>.editorconfig</c> — <c>file_header_template</c>, which is the copy
/// that is actually enforced: Roslyn's IDE0073 checks it against every source
/// file, and <c>Directory.Build.props</c> turns that into a build error.</item>
/// </list>
///
/// <para><b>Three copies of one sentence with no check between them is the
/// shape this project keeps getting caught by.</b> Only the
/// <c>.editorconfig</c> copy is read by a machine; the other two could say
/// anything at all — a different licence, a different copyright holder — and
/// every build and every test would still pass. The drift would surface the
/// first time somebody read <c>LICENSE</c> for a reason that mattered, which is
/// the worst possible time to discover it.</para>
///
/// <para>So: edit one of the three, and this fails. That is the whole
/// point.</para>
/// </summary>
public class LicenseHeaderTests
{
    /// <summary>
    /// The template Roslyn enforces says the same thing as the template an IDE
    /// would stamp.
    /// </summary>
    [Fact]
    public void EditorConfigHeaderTemplate_MatchesLicenseTxt()
    {
        Assert.Equal(NoticeFromLicenseTxt(), NoticeFromEditorConfig());
    }

    /// <summary>
    /// ...and both say the same thing as the licence the repository is under.
    /// </summary>
    [Fact]
    public void LicenseTxtNotice_MatchesTheLicenceItself()
    {
        // LICENSE opens with an "MIT License" title line that a per-file header
        // does not carry, so the notice proper starts at the copyright line.
        string[] licence = File.ReadAllLines(Path.Combine(RepositoryRoot(), "LICENSE"));
        int start = Array.FindIndex(
            licence, l => l.StartsWith("Copyright (c)", StringComparison.Ordinal));

        Assert.True(start >= 0, "LICENSE contains no 'Copyright (c)' line.");
        Assert.Equal(
            string.Join("\n", licence[start..]).TrimEnd(),
            NoticeFromLicenseTxt());
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
    /// Reads <c>file_header_template</c> back out of <c>.editorconfig</c> and
    /// undoes the one escape Roslyn's reader applies to it.
    /// </summary>
    private static string NoticeFromEditorConfig()
    {
        const string key = "file_header_template = ";

        string line = File
            .ReadAllLines(Path.Combine(RepositoryRoot(), ".editorconfig"))
            .FirstOrDefault(l => l.StartsWith(key, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                ".editorconfig declares no file_header_template, so nothing is " +
                "enforcing the licence header on source files.");

        // The value is one physical line because that is the only form the
        // .editorconfig reader accepts; Roslyn unescapes \n itself.
        return line[key.Length..].Replace(@"\n", "\n").TrimEnd();
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
