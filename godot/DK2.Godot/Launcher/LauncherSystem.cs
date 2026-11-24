// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Runtime.InteropServices;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>What the launcher asks the operating system that Godot does not expose: whether Shift is down at start
/// (Godot's <see cref="Input"/> knows only the events it has received, and there are none yet) and whether the player
/// turned animations off (Windows' "Animation effects"). Elsewhere Shift reads Godot's state and motion stays on.</summary>
public static class LauncherSystem
{
    private const int VkShift = 0x10;
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public static bool ShiftHeld()
    {
        if (OperatingSystem.IsWindows())
            try { return (GetAsyncKeyState(VkShift) & 0x8000) != 0; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return Input.IsKeyPressed(Key.Shift);
    }

    /// <summary>True when <c>--reduce-motion</c> is given or the system asks for less animation.</summary>
    public static bool ReduceMotion(IReadOnlyList<string> args)
    {
        foreach (string arg in args)
            if (arg == "--reduce-motion") return true;
        if (!OperatingSystem.IsWindows()) return false;
        try { return SystemParametersInfoW(SpiGetClientAreaAnimation, 0, out int on, 0) && on == 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, out int value, uint update);
}
