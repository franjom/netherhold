// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws a <see cref="GameFailureCopy"/> screen: what failed, the error, the log file and the choices, in the
/// launcher's fonts and palette (<see cref="LauncherTheme"/>), never DK2's art, since the failure may be a missing install.
/// Over a level it dims the last frame drawn; before one it covers the screen. The host acts on <see cref="Chosen"/>.
/// The words and what each choice does are <c>DK2.Presentation</c>'s.</summary>
public partial class FailureScreen : CanvasLayer
{
    /// <summary>Over every canvas the scenes draw, the screen colours (110) and the movies (120) included.</summary>
    public const int CanvasLayerIndex = 125;

    private GameFailureScreen? _screen;
    private bool _overFrame;
    private Control? _root;
    private bool _chosen;

    /// <summary>The choice taken, once.</summary>
    public event Action<GameFailureAction>? Chosen;

    /// <summary>Logs <paramref name="failure"/> and shows its screen under <paramref name="parent"/>;
    /// <paramref name="overFrame"/> keeps the last frame visible behind it.</summary>
    public static FailureScreen Show(Node parent, in GameFailure failure, bool overFrame)
    {
        GameFailureScreen screen = GameFailureCopy.For(failure, LogFile());
        GD.PushError("DK2.Godot: ", screen.Title, ". ", screen.Summary, screen.Detail == null ? "" : " " + screen.Detail);
        var layer = new FailureScreen { Name = "FailureScreen", Layer = CanvasLayerIndex, _screen = screen, _overFrame = overFrame };
        parent.AddChild(layer);
        return layer;
    }

    /// <summary>The log file Godot writes this run (<see cref="GameFailureCopy.LogFile"/>), as an OS path.</summary>
    private static string? LogFile()
    {
        bool logging = ProjectSettings.GetSettingWithOverride("debug/file_logging/enable_file_logging").AsBool();
        string path = ProjectSettings.GetSettingWithOverride("debug/file_logging/log_path").AsString();
        // Godot takes the engine options it consumed (--log-file among them) out of OS.GetCmdlineArgs; the process's own
        // command line still has them.
        string[] engineArgs = [.. OS.GetCmdlineArgs(), .. System.Environment.GetCommandLineArgs()];
        string? file = GameFailureCopy.LogFile(engineArgs, logging,
            string.IsNullOrEmpty(path) ? null : ProjectSettings.GlobalizePath(path));
        return file == null ? null : System.IO.Path.GetFullPath(file);
    }

    public override void _Ready()
    {
        if (_screen == null)
        {
            return;
        }
        // The OS pointer, as the hand cursor and the possession lock may hold it.
        if (Input.MouseMode != Input.MouseModeEnum.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        Vector2 size = GetViewport().GetVisibleRect().Size;
        // Sized for the window's height, as the launcher is for its DPI: 1 at 900 pixels and below.
        float scale = Mathf.Max(1f, size.Y / 900f);
        Scale = new Vector2(scale, scale);
        _root = new Control { Theme = LauncherTheme.Build(scale), Size = size / scale, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_root);
        var shade = new ColorRect
        {
            Color = _overFrame ? LauncherTheme.Fade(LauncherTheme.DeepEarth, .72f) : LauncherTheme.DeepEarth,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(shade);
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(centre);
        var panel = new PanelContainer
        {
            ThemeTypeVariation = LauncherTheme.Stone,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Launcher/LauncherStone.gdshader") },
        };
        centre.AddChild(panel);
        var margin = new MarginContainer();
        foreach (string side in new[] { "margin_left", "margin_right" })
        {
            margin.AddThemeConstantOverride(side, 40);
        }

        foreach (string side in new[] { "margin_top", "margin_bottom" })
        {
            margin.AddThemeConstantOverride(side, 30);
        }

        panel.AddChild(margin);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(Mathf.Min(640f, size.X / scale - 64f), 0) };
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);

        var title = new Label { Text = _screen.Title, ThemeTypeVariation = LauncherTheme.Heading };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", LauncherTheme.HeartRed);
        column.AddChild(title);
        column.AddChild(Paragraph(_screen.Summary, LauncherTheme.Intro));
        if (_screen.Detail != null)
        {
            Label detail = Paragraph(_screen.Detail, LauncherTheme.Status);
            detail.AddThemeColorOverride("font_color", LauncherTheme.KeeperGold);
            column.AddChild(detail);
        }
        column.AddChild(Paragraph(_screen.LogLine, LauncherTheme.Hint));
        if (_screen.LogFile != null)
        {
            // A read-only field, so the path can be selected and copied.
            var path = new LineEdit { Text = _screen.LogFile, Editable = false, ThemeTypeVariation = LauncherTheme.Field };
            path.AddThemeColorOverride("font_uneditable_color", LauncherTheme.Bone);
            column.AddChild(path);
        }

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);
        Button? first = null;
        foreach (GameFailureChoice choice in _screen.Choices)
        {
            GameFailureAction action = choice.Action;
            var button = new Button
            {
                Text = choice.Label + "  (" + choice.Key + ")",
                ThemeTypeVariation = first == null ? LauncherTheme.PlayLit : LauncherTheme.Quiet,
            };
            button.Pressed += () => Choose(action);
            buttons.AddChild(button);
            first ??= button;
        }
        first?.GrabFocus();
    }

    private static Label Paragraph(string text, string variation)
    {
        return new()
        {
            Text = text,
            ThemeTypeVariation = variation,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
    }

    public override void _Input(InputEvent @event)
    {
        if (_screen == null || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        GameFailureKey? pressed = key.Keycode switch
        {
            Key.Enter or Key.KpEnter => GameFailureKey.Confirm,
            Key.Escape => GameFailureKey.Cancel,
            _ => null,
        };
        GetViewport().SetInputAsHandled();
        if (pressed is GameFailureKey which && GameFailureCopy.ForKey(_screen, which) is GameFailureAction action)
        {
            Choose(action);
        }
    }

    private void Choose(GameFailureAction action)
    {
        if (_chosen)
        {
            return;
        }

        _chosen = true;
        Chosen?.Invoke(action);
    }
}
