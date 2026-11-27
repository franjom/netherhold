// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Text;

namespace DK2.Presentation;

/// <summary>The level's own string table by line index: the subtitle text of a scripted Mentor line (<c>0x00553780</c> loads
/// it from the level's text-table byte, <see cref="LevelTextTable"/>). A level whose id names no table, or whose table is
/// missing or unreadable, has no subtitles.</summary>
public sealed class LevelSubtitles
{
    private readonly Dk2StringTable? _table;

    private LevelSubtitles(Dk2StringTable? table)
    {
        _table = table;
    }

    /// <summary>The install's default-language text folder, where the level tables, Speech.str and the fonts are.</summary>
    public static string TextFolder(string install)
    {
        return Path.Combine(install, "Data", "Text", "Default");
    }

    /// <param name="warn">Told why a table that exists could not be read.</param>
    public static LevelSubtitles Open(string install, int textTableId, Action<string>? warn = null)
    {
        string? file = LevelTextTable.FileName(textTableId);
        string? path = file == null ? null : Path.Combine(TextFolder(install), file);
        Dk2StringTable? table = null;
        try
        {
            if (path != null && File.Exists(path))
            {
                table = Dk2StringTable.Open(path);
            }
        }
        catch (Exception ex)   // any failure leaves the level without subtitles
        {
            warn?.Invoke("subtitles unavailable: " + ex.Message);
        }
        return new LevelSubtitles(table);
    }

    /// <summary>Line <paramref name="line"/>'s text, or null.</summary>
    public string? Line(int line)
    {
        return _table != null && line >= 0 && line < _table.Entries.Count ? _table.Entries[line].Value : null;
    }
}
