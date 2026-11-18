// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The level, seed and install a scripted run names on its command line (the tester package,
/// docs/PERFORMANCE.md). Each engine has its own prefix: Unity's player takes <c>-level</c>, Godot's user arguments
/// <c>--level</c>. An argument absent, or a seed that is not a plain decimal, leaves the host's own value.</summary>
public sealed record LaunchArguments(string? Level, ulong? Seed, string? Install)
{
    public static LaunchArguments Parse(IReadOnlyList<string> args, string prefix)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        string? seedText = Value(args, prefix + "seed");
        ulong? seed = seedText != null && ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value)
            ? value : null;
        return new LaunchArguments(Value(args, prefix + "level"), seed, Value(args, prefix + "dk2Install"));
    }

    /// <summary>The tile a <c>glide-to x,y</c> argument names (a check that shows a camera glide), or null when it is
    /// absent or not two plain decimals.</summary>
    public static TilePos? GlideTarget(IReadOnlyList<string> args, string prefix)
    {
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        string? text = Value(args, prefix + "glide-to");
        int comma = text?.IndexOf(',') ?? -1;
        if (text is null || comma < 0) return null;
        return int.TryParse(text.Substring(0, comma), NumberStyles.None, CultureInfo.InvariantCulture, out int x)
            && int.TryParse(text.Substring(comma + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int y)
            ? new TilePos(x, y) : null;
    }

    /// <summary>True when a <c>possess-first-creature</c> argument is present (a check that shows the first-person
    /// view): the host possesses the viewer's first creature once the level is up.</summary>
    public static bool PossessFirstCreature(IReadOnlyList<string> args, string prefix)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        string key = prefix + "possess-first-creature";
        for (int i = 0; i < args.Count; i++)
            if (args[i] == key) return true;
        return false;
    }

    /// <summary>The scripted pointer a <c>pointer-path</c> argument names (<see cref="PointerScript"/>, a check that drives a
    /// capture without the OS pointer), or null when it is absent or malformed.</summary>
    public static PointerScript? PointerPath(IReadOnlyList<string> args, string prefix)
    {
        if (prefix is null) throw new ArgumentNullException(nameof(prefix));
        return ParsePointerPath(Value(args, prefix + "pointer-path"));
    }

    /// <summary>A <see cref="PointerScript"/> from its text, or null when the text is empty or any key is malformed. Keys
    /// are separated by <c>;</c>, each <c>frame:x,y</c> with an optional <c>:buttons</c> of <c>L</c> (primary down),
    /// <c>l</c> (primary up), <c>R</c> and <c>r</c> (secondary), each at most once; frames are plain decimals, strictly
    /// increasing; x and y are finite invariant-culture decimals.</summary>
    public static PointerScript? ParsePointerPath(string? text)
    {
        if (text is null || text.Trim().Length == 0) return null;
        string[] entries = text.Split(';');
        var keys = new PointerScriptKey[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            string[] parts = entries[i].Trim().Split(':');
            if (parts.Length is < 2 or > 3) return null;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int frame)) return null;
            if (i > 0 && frame <= keys[i - 1].Frame) return null;
            string[] xy = parts[1].Split(',');
            if (xy.Length != 2 || !Coordinate(xy[0], out float x) || !Coordinate(xy[1], out float y)) return null;
            PointerScriptButtons buttons = PointerScriptButtons.None;
            if (parts.Length == 3)
            {
                if (parts[2].Length == 0) return null;
                foreach (char c in parts[2])
                {
                    PointerScriptButtons button = c switch
                    {
                        'L' => PointerScriptButtons.PrimaryDown,
                        'l' => PointerScriptButtons.PrimaryUp,
                        'R' => PointerScriptButtons.SecondaryDown,
                        'r' => PointerScriptButtons.SecondaryUp,
                        _ => PointerScriptButtons.None,
                    };
                    if (button == PointerScriptButtons.None || (buttons & button) != 0) return null;
                    buttons |= button;
                }
            }
            keys[i] = new PointerScriptKey(frame, x, y, buttons);
        }
        return new PointerScript(keys);
    }

    private static bool Coordinate(string text, out float value) =>
        float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    /// <summary>The argument after the first <paramref name="key"/>, or null when the key is absent or last.</summary>
    public static string? Value(IReadOnlyList<string> args, string key)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        for (int i = 0; i < args.Count; i++)
            if (args[i] == key) return i + 1 < args.Count ? args[i + 1] : null;
        return null;
    }
}
