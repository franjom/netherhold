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

namespace DK2.Data;

/// <summary>
/// Locates the local Dungeon Keeper 2 installation used as reference material.
///
/// Resolution order:
/// <list type="number">
/// <item>the <c>DK2_DIR</c> environment variable, which is authoritative — if
/// it is set, no other location is considered, so a run can be pointed at a
/// specific install (or at nothing at all) without surprises;</item>
/// <item>otherwise, known default paths.</item>
/// </list>
///
/// Returning null is a normal outcome, not an error: CI has no installation and
/// the install-dependent tests skip. Nothing in the shipped game may require
/// this type to succeed.
/// </summary>
public static class Dk2Install
{
    /// <summary>
    /// Where the reference install lives on the development machine
    /// (docs/HANDOFF.md). Overridden by DK2_DIR anywhere else.
    /// </summary>
    private static readonly string[] DefaultCandidates =
    [
        @"E:\GogGames\Dungeon Keeper 2",
    ];

    public static string? Find()
    {
        string? env = Environment.GetEnvironmentVariable("DK2_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return IsValid(env) ? env : null;

        foreach (string candidate in DefaultCandidates)
            if (IsValid(candidate)) return candidate;

        return null;
    }

    public static string Require() =>
        Find() ?? throw new InvalidOperationException(
            "Dungeon Keeper 2 installation not found. Set the DK2_DIR environment " +
            "variable to the game directory (the one containing DKII.EXE).");

    private static bool IsValid(string? dir) =>
        !string.IsNullOrEmpty(dir)
        && File.Exists(Path.Combine(dir, "DKII.EXE"))
        && Directory.Exists(Path.Combine(dir, "Data"));
}
