// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Puts the game's window into the launcher's screen mode: a window of the chosen size centred on its screen,
/// which the player may resize (the HUD and the world lay out from the viewport's size every frame), exclusive full
/// screen, or Godot's borderless full-screen window (the project's own start mode).</summary>
public static class LauncherDisplay
{
    public const string WindowTitle = LauncherCopy.Title;

    public static void Apply(Window root, LauncherSettings settings)
    {
        root.ContentScaleFactor = 1f;
        root.Title = WindowTitle;
        switch (settings.Screen)
        {
            case LauncherScreenMode.FullScreen:
                root.Mode = Window.ModeEnum.ExclusiveFullscreen;
                break;
            case LauncherScreenMode.FullScreenWindow:
                root.Mode = Window.ModeEnum.Fullscreen;
                break;
            default:
                Rect2I screen = DisplayServer.ScreenGetUsableRect(root.CurrentScreen);
                (IReadOnlyList<LauncherWindowSize> sizes, int selected) =
                    LauncherWindowSizes.Options(settings.WindowSize, screen.Size.X, screen.Size.Y);
                ShowWindow(root, new Vector2I(sizes[selected].Width, sizes[selected].Height), resizable: true);
                break;
        }
        Callable.From(() => GD.Print($"DK2.Godot launcher: {settings.Screen}, window {root.Size.X}x{root.Size.Y}")).CallDeferred();
    }

    /// <summary>A decorated window of <paramref name="size"/>, centred on its screen's usable area.</summary>
    public static void ShowWindow(Window root, Vector2I size, bool resizable)
    {
        root.Mode = Window.ModeEnum.Windowed;
        root.Borderless = false;
        root.Unresizable = !resizable;
        root.Size = size;
        root.MoveToCenter();
    }
}
