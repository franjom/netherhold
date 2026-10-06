// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;

namespace DK2.Tools.Commands;

internal static class CommandHelpers
{
    internal static string ResolveData(string arg) =>
        File.Exists(arg) ? arg : Path.Combine(Dk2Install.Require(), "Data", arg);

    internal static string ResolveText(string arg) =>
        File.Exists(arg)
            ? arg
            : Path.Combine(Dk2Install.Require(), "Data", "Text", "Default", arg);

    internal static string EscapeForOneLine(string value) =>
        value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    internal static string ResolveEditor(string arg)
    {
        if (File.Exists(arg)) return arg;
        string root = Dk2Install.Require();
        string editor = Path.Combine(root, "Data", "editor", arg);
        if (File.Exists(editor)) return editor;
        return Path.Combine(root, "Data", "editor", "maps", arg);
    }

    internal static string RequireConvertedOutput(string argument)
    {
        string path = Path.GetFullPath(argument);
        string allowed = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "converted-assets"));
        string allowedPrefix = allowed.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (path.Equals(allowed, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
            return path;
        throw new ArgumentException(
            $"Conversion output must be converted-assets/ or one of its children, not '{argument}'.");
    }
}
