// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;

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

    /// <summary>The argument after the first <paramref name="key"/>, or null when the key is absent or last.</summary>
    public static string? Value(IReadOnlyList<string> args, string key)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        for (int i = 0; i < args.Count; i++)
            if (args[i] == key) return i + 1 < args.Count ? args[i + 1] : null;
        return null;
    }
}
