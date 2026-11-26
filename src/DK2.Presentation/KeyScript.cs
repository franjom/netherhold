// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;

namespace DK2.Presentation;

/// <summary>One key of a <see cref="KeyScript"/>: on frame <paramref name="Frame"/> the key named
/// <paramref name="Key"/> (the host's own key name) goes down and comes up again.</summary>
public readonly record struct KeyScriptKey(int Frame, string Key);

/// <summary>Scripted key taps for a check run, so a capture drives the keyboard without the OS (as <see cref="PointerScript"/>
/// drives the pointer). Text form: taps separated by <c>;</c>, each <c>frame:Name</c>, frames plain decimals in
/// non-decreasing order (several taps may share a frame), names letters and digits only; the host resolves a name to its key.</summary>
public sealed class KeyScript
{
    private readonly KeyScriptKey[] _keys;

    private KeyScript(KeyScriptKey[] keys) => _keys = keys;

    public IReadOnlyList<KeyScriptKey> Keys => _keys;

    /// <summary>The script from its text, or null when the text is empty or any tap is malformed.</summary>
    public static KeyScript? Parse(string? text)
    {
        if (text is null || text.Trim().Length == 0)
        {
            return null;
        }

        string[] entries = text.Split(';');
        var keys = new KeyScriptKey[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            string[] parts = entries[i].Trim().Split(':');
            if (parts.Length != 2)
            {
                return null;
            }

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int frame))
            {
                return null;
            }

            if (i > 0 && frame < keys[i - 1].Frame)
            {
                return null;
            }

            string name = parts[1];
            if (name.Length == 0)
            {
                return null;
            }

            foreach (char c in name)
            {
                if (c is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
                {
                    return null;
                }
            }

            keys[i] = new KeyScriptKey(frame, name);
        }
        return new KeyScript(keys);
    }

    /// <summary>The index of the first tap on <paramref name="frame"/> or later, from <paramref name="start"/>; a host that
    /// walks its frames in order passes the last answer back, so a frame's lookup allocates nothing.</summary>
    public int FirstAtOrAfter(int frame, int start = 0)
    {
        int i = Math.Max(0, start);
        while (i < _keys.Length && _keys[i].Frame < frame)
        {
            i++;
        }

        return i;
    }
}
