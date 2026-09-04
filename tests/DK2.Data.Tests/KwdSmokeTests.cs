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

using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>
/// Smoke tests against the real DK2 installation. Skipped automatically when no
/// installation is present (CI machines), so the build stays green everywhere.
/// </summary>
public class KwdSmokeTests
{
    private static string? FindInstall() => Dk2Install.Find();

    public static bool InstallPresent => FindInstall() is not null;

    [Fact]
    public void EditorDatabaseFiles_StartWithExpectedRecordType()
    {
        string? dir = FindInstall();
        if (dir is null) return; // skipped: no DK2 install on this machine (e.g. CI)

        // First 4 bytes of each editor KWD are the record-type id (little endian).
        // Verified by direct inspection on 2026-08-29; see docs/research/kwd-container.md.
        (string File, KwdRecordType Expected)[] cases =
        [
            ("Creatures.kwd", KwdRecordType.Creatures),
            ("GlobalVariables.kwd", KwdRecordType.Variables),
            ("Rooms.kwd", KwdRecordType.Rooms),
            ("Terrain.kwd", KwdRecordType.Terrain),
            ("Traps.kwd", KwdRecordType.Traps),
            ("Doors.kwd", KwdRecordType.Doors),
            ("KeeperSpells.kwd", KwdRecordType.KeeperSpells),
            ("CreatureSpells.kwd", KwdRecordType.CreatureSpells),
            ("Objects.kwd", KwdRecordType.Objects),
            ("Shots.kwd", KwdRecordType.Shots),
            ("Effects.kwd", KwdRecordType.Effects),
            ("EffectElements.kwd", KwdRecordType.EffectElements),
        ];

        Span<byte> header = stackalloc byte[4];
        foreach ((string file, KwdRecordType expected) in cases)
        {
            string path = Path.Combine(dir!, "Data", "editor", file);
            Assert.True(File.Exists(path), $"missing: {path}");

            using var fs = File.OpenRead(path);
            fs.ReadExactly(header);
            int typeId = BitConverter.ToInt32(header);

            Assert.Equal(expected, (KwdRecordType)typeId);
        }
    }
}
