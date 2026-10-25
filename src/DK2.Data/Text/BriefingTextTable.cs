// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Text;

/// <summary>The distinct briefing-resource table at0x006BC100, not the speech-table filename pattern.</summary>
public static class BriefingTextTable
{
    public static string? FileName(int textTableId) => textTableId switch
    {
        >= 1 and <= 24 => Path.GetFileNameWithoutExtension(LevelTextTable.FileName(textTableId)) + "_BRIEFING.str",
        26 => "LEVELMPD1_BRIEFING.str",
        >= 27 and <= 31 => "LEVELS" + (textTableId - 26) + "_BRIEFING.str",
        >= 35 and <= 40 => "LEVELMPD" + (textTableId - 33) + "_BRIEFING.str",
        _ => null,
    };
}
