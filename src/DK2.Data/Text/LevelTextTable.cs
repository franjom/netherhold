// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Text;

/// <summary>
/// The string table a level's <c>TextTableId</c> names. It holds that level's scripted
/// Mentor lines (trigger action 24) and their subtitles.
///
/// <c>0x00553780</c> loads it at level start from the level's text-table byte. It takes
/// the resource id from the 60-entry table at <c>0x006BC010</c> and puts the strings
/// where <c>0x00553A90</c> resolves the subtitle text. Entries 1-34 are the resources
/// 0x420-0x441 in order. Entries 35-40 all repeat 0x439. Entry 0 and 41-59 are empty,
/// and with them no table loads.
/// </summary>
public static class LevelTextTable
{
    // residx.dat's names for resources 0x420-0x441, in table order.
    private static readonly string[] Names =
    [
        "LEVEL1.str", "LEVEL2.str", "LEVEL3.str", "LEVEL4.str", "LEVEL5.str",
        "LEVEL6A.str", "LEVEL6B.str", "LEVEL7.str", "LEVEL8.str", "LEVEL9.str",
        "LEVEL10.str", "LEVEL11A.str", "LEVEL11B.str", "LEVEL11C.str", "LEVEL12.str",
        "LEVEL13.str", "LEVEL14.str", "LEVEL15A.str", "LEVEL15B.str", "LEVEL16.str",
        "LEVEL17.str", "LEVEL18.str", "LEVEL19.str", "LEVEL20.str", "MULTIPLAYER.str",
        "MYPETDUNGEON.str", "SECRET1.str", "SECRET2.str", "SECRET3.str", "SECRET4.str",
        "SECRET5.str", "DEMO1.str", "DEMO2.str", "DEMO3.str",
    ];

    /// <summary>The file under <c>Data/Text/&lt;language&gt;</c>, or null when the id loads none.</summary>
    public static string? FileName(int textTableId) => textTableId switch
    {
        >= 1 and <= 34 => Names[textTableId - 1],
        >= 35 and <= 40 => "MYPETDUNGEON.str",
        _ => null,
    };
}
