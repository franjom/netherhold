// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Text;

namespace DK2.Presentation;

/// <summary>The string tables the message tabs read, shared by every engine adapter: a message's text (Speech.str, the
/// level's table, Text.str) and the level's briefing lines for the objective panel. A table that is missing or fails to
/// read is left out, and its strings resolve to null.</summary>
public sealed class MessageTextTables
{
    private readonly Dk2StringTable? _speech, _level, _briefing;
    private readonly Dk2StringTable _text;

    /// <param name="textTableId">The level's text table (its definition's <c>TextTableId</c>; 0 for none).</param>
    /// <param name="warn">Told, once per table, why a table that exists could not be read.</param>
    public MessageTextTables(InstallHudSources sources, int textTableId, Action<string>? warn = null)
    {
        if (sources is null) throw new ArgumentNullException(nameof(sources));
        _text = sources.Strings;
        string directory = sources.TextDirectory;
        _speech = Open(Path.Combine(directory, "Speech.str"), warn);
        string? levelFile = LevelTextTable.FileName(textTableId);
        _level = levelFile == null ? null : Open(Path.Combine(directory, levelFile), warn);
        _briefing = levelFile == null ? null
            : Open(Path.Combine(directory, Path.GetFileNameWithoutExtension(levelFile) + "_BRIEFING.str"), warn);
    }

    /// <summary>A message string reference's text, or null (<see cref="Resolve(int, Dk2StringTable?, Dk2StringTable?, Dk2StringTable?)"/>).</summary>
    public string? Resolve(int reference) => Resolve(reference, _speech, _text, _level);

    /// <summary>A line of the level's briefing table, or null.</summary>
    public string? Briefing(int line) => _briefing != null && _briefing.TryResolve((uint)line, out string text) ? text : null;

    /// <summary>A message string reference (0x004C51D0): below -10000 Speech.str entry -10000 - ref,
    /// -128..-1 the level's table entry -1 - ref, a positive one Text.str.</summary>
    public static string? Resolve(int reference, Dk2StringTable? speech, Dk2StringTable? text, Dk2StringTable? level)
    {
        Dk2StringTable? table = reference < -10000 ? speech : reference < 0 ? level : reference > 0 ? text : null;
        uint id = reference < -10000 ? (uint)(-10000 - reference) : reference < 0 ? (uint)(-1 - reference) : (uint)reference;
        return table != null && table.TryResolve(id, out string value) ? value : null;
    }

    private static Dk2StringTable? Open(string path, Action<string>? warn)
    {
        try { return File.Exists(path) ? Dk2StringTable.Open(path) : null; }
        catch (Exception ex) { warn?.Invoke($"{Path.GetFileName(path)} unavailable: {ex.Message}"); return null; }
    }
}
