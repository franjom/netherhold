// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The Netherhold launcher: where the tester points the game at their Dungeon Keeper 2 install, picks an
/// optional menu font and the screen mode, before the front end starts. Its rules (the install check, the saved file,
/// the window sizes, the words) are <c>DK2.Presentation</c>'s; this draws them and applies the choice. Its one
/// unprompted motion is the seam's lava run when an install is found (instant under reduced motion).</summary>
public partial class Launcher : Control
{
    private const float SeamWidth = 184f, RunSeconds = .9f;
    private static readonly StringName ProgressName = "progress", PointsName = "points", CountName = "point_count",
        SizeName = "size", PlayLitName = LauncherTheme.PlayLit, PlayWaitingName = LauncherTheme.PlayWaiting;

    /// <summary>Raised by Play with the settings it saved.</summary>
    public event Action<LauncherSettings>? PlayRequested;

    private string[] _args = [];
    private LauncherSettings _saved = LauncherSettings.Default;
    private LineEdit _install = null!, _font = null!;
    private Label _installStatus = null!, _fontStatus = null!;
    private CheckBox _window = null!, _fullScreen = null!, _fullScreenWindow = null!, _startDirectly = null!;
    private OptionButton _sizes = null!;
    private Button _play = null!;
    private ColorRect _seam = null!;
    private ShaderMaterial _seamMaterial = null!;
    private IReadOnlyList<LauncherWindowSize> _sizeList = [];
    private FileDialog? _dialog;
    private bool _valid, _reduceMotion, _seamPlaced, _running, _lit;
    private float _progress;
    /// <summary><c>--launcher-keys</c>: key presses a check injects through Godot's input, at frames counted from the first.</summary>
    private readonly List<(int Frame, Key Key, bool Shift)> _keys = [];
    private int _frame, _nextKey;

    /// <summary>Call before the node enters the tree, with the run's user arguments.</summary>
    public void Open(string[] args)
    {
        _args = args;
        _saved = LauncherSettingsStore.Current(args);
        _reduceMotion = LauncherSystem.ReduceMotion(args);
        string? keys = LaunchArguments.Value(args, "--launcher-keys");
        if (keys == null) return;
        // "frame:Key;..." with Godot's key names, Shift+ for a shifted press (Shift+Tab).
        foreach (string entry in keys.Split(';'))
        {
            string[] parts = entry.Split(':');
            bool shift = parts.Length == 2 && parts[1].StartsWith("Shift+", StringComparison.Ordinal);
            string name = shift ? parts[1].Substring(6) : parts.Length == 2 ? parts[1] : "";
            if (parts.Length == 2 && int.TryParse(parts[0], out int frame) && Enum.TryParse(name, out Key key))
                _keys.Add((frame, key, shift));
            else GD.PushWarning("DK2.Godot: --launcher-keys entry not understood: " + entry);
        }
    }

    public override void _Ready()
    {
        Window root = GetTree().Root;
        int screen = root.CurrentScreen;
        Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
        float scale = LauncherWindowSizes.LauncherScale(DisplayServer.ScreenGetDpi(screen), usable.Size.X, usable.Size.Y);
        LauncherWindowSize size = LauncherWindowSizes.LauncherSize;
        root.ContentScaleFactor = scale;
        root.Title = LauncherDisplay.WindowTitle;
        LauncherDisplay.ShowWindow(root, new Vector2I(Mathf.RoundToInt(size.Width * scale), Mathf.RoundToInt(size.Height * scale)),
            resizable: false);

        Theme = LauncherTheme.Build(scale);
        // Main is a plain Node, so this Control follows the viewport itself rather than by anchors.
        FitViewport();
        GetViewport().SizeChanged += FitViewport;
        var earth = new ColorRect { Color = LauncherTheme.DeepEarth, MouseFilter = MouseFilterEnum.Ignore };
        earth.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(earth);
        BuildSeam();
        BuildContent(usable.Size);

        string? install = _saved.InstallFolder ?? Dk2Install.Find(LaunchArguments.Value(_args, "--dk2Install"), null);
        _install.Text = install ?? "";
        _install.CaretColumn = _install.Text.Length;
        _font.Text = _saved.MenuFont ?? "";
        CheckInstall(fromUser: false);
        CheckFont();
        if (_valid) _play.GrabFocus(); else _install.GrabFocus();
    }

    private void FitViewport() => Size = GetViewport().GetVisibleRect().Size;

    public override void _ExitTree() => GetViewport().SizeChanged -= FitViewport;

    private void BuildSeam()
    {
        _seamMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Launcher/LauncherSeam.gdshader") };
        _seamMaterial.SetShaderParameter("earth", LauncherTheme.DeepEarth);
        _seamMaterial.SetShaderParameter("lava", LauncherTheme.Lava);
        _seam = new ColorRect { Material = _seamMaterial, MouseFilter = MouseFilterEnum.Ignore };
        _seam.SetAnchorsPreset(LayoutPreset.RightWide);
        _seam.OffsetLeft = -SeamWidth;
        AddChild(_seam);
    }

    private void BuildContent(Vector2I screen)
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.OffsetRight = -SeamWidth;
        margin.AddThemeConstantOverride("margin_left", 64);
        margin.AddThemeConstantOverride("margin_top", 40);
        margin.AddThemeConstantOverride("margin_bottom", 34);
        AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        margin.AddChild(column);

        column.AddChild(new Label { Text = LauncherCopy.Title, ThemeTypeVariation = LauncherTheme.Title });
        column.AddChild(new Label
        {
            Text = LauncherCopy.Intro, ThemeTypeVariation = LauncherTheme.Intro, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(470, 0), SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        });
        column.AddChild(Gap(34));

        VBoxContainer installBody = Section(column, "1", LauncherCopy.InstallHeading);
        _install = Field(installBody, out Button browseInstall);
        _install.PlaceholderText = "";
        _install.TextChanged += _ => CheckInstall(fromUser: true);
        browseInstall.Pressed += BrowseInstall;
        _installStatus = StatusLine(installBody);
        column.AddChild(Gap(24));

        VBoxContainer fontBody = Section(column, "2", LauncherCopy.FontHeading);
        _font = Field(fontBody, out Button browseFont);
        _font.PlaceholderText = LauncherCopy.FontPlaceholder;
        _font.TextChanged += _ => CheckFont();
        browseFont.Pressed += BrowseFont;
        _fontStatus = StatusLine(fontBody);
        column.AddChild(Gap(24));

        VBoxContainer screenBody = Section(column, "3", LauncherCopy.ScreenHeading);
        var modes = new ButtonGroup();
        var windowRow = new HBoxContainer();
        windowRow.AddThemeConstantOverride("separation", 16);
        screenBody.AddChild(windowRow);
        _window = Radio(windowRow, LauncherCopy.Window, modes);
        _sizes = new OptionButton { FitToLongestItem = true, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        (_sizeList, int selected) = LauncherWindowSizes.Options(_saved.WindowSize, screen.X, screen.Y);
        for (int i = 0; i < _sizeList.Count; i++) _sizes.AddItem(_sizeList[i].ToString(), i);
        _sizes.Select(selected);
        windowRow.AddChild(_sizes);
        _fullScreen = Radio(screenBody, LauncherCopy.FullScreen, modes);
        _fullScreenWindow = Radio(screenBody, LauncherCopy.FullScreenWindow, modes);
        (_saved.Screen switch
        {
            LauncherScreenMode.FullScreen => _fullScreen,
            LauncherScreenMode.FullScreenWindow => _fullScreenWindow,
            _ => _window,
        }).ButtonPressed = true;
        _sizes.Disabled = !_window.ButtonPressed;
        _window.Toggled += on => _sizes.Disabled = !on;

        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        bottom.AddThemeConstantOverride("separation", 10);
        column.AddChild(bottom);
        var startColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        startColumn.AddThemeConstantOverride("separation", 0);
        bottom.AddChild(startColumn);
        _startDirectly = new CheckBox { Text = LauncherCopy.StartDirectly, ButtonPressed = _saved.StartDirectly,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        startColumn.AddChild(_startDirectly);
        var hint = new Label { Text = LauncherCopy.ShiftHint, ThemeTypeVariation = LauncherTheme.Hint };
        hint.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginLeft = 34 });
        startColumn.AddChild(hint);
        var quit = new Button { Text = LauncherCopy.Quit, ThemeTypeVariation = LauncherTheme.Quiet, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        quit.Pressed += Quit;
        bottom.AddChild(quit);
        _play = new Button { Text = LauncherCopy.Play, ThemeTypeVariation = PlayWaitingName,
            CustomMinimumSize = new Vector2(132, 48), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _play.AddThemeFontSizeOverride("font_size", 21);
        _play.Pressed += TryPlay;
        bottom.AddChild(_play);
    }

    private static Control Gap(float height) => new() { CustomMinimumSize = new Vector2(0, height), MouseFilter = MouseFilterEnum.Ignore };

    /// <summary>A numbered section: the number in its own column, the heading and <paramref name="column"/>'s body beside it.</summary>
    private static VBoxContainer Section(VBoxContainer column, string number, string heading)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        column.AddChild(row);
        row.AddChild(new Label { Text = number, ThemeTypeVariation = LauncherTheme.Number, CustomMinimumSize = new Vector2(34, 0),
            SizeFlagsVertical = SizeFlags.ShrinkBegin });
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        row.AddChild(body);
        body.AddChild(new Label { Text = heading, ThemeTypeVariation = LauncherTheme.Heading });
        return body;
    }

    private static LineEdit Field(VBoxContainer body, out Button browse)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        body.AddChild(row);
        var stone = new PanelContainer
        {
            ThemeTypeVariation = LauncherTheme.Stone, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Launcher/LauncherStone.gdshader") },
        };
        row.AddChild(stone);
        var field = new LineEdit { ThemeTypeVariation = LauncherTheme.Field, CaretBlink = true };
        stone.AddChild(field);
        browse = new Button { Text = LauncherCopy.Browse };
        row.AddChild(browse);
        return field;
    }

    private static Label StatusLine(VBoxContainer body)
    {
        var label = new Label { ThemeTypeVariation = LauncherTheme.Status, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        body.AddChild(label);
        return label;
    }

    private static CheckBox Radio(Container parent, string text, ButtonGroup group)
    {
        var radio = new CheckBox { Text = text, ButtonGroup = group, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        parent.AddChild(radio);
        return radio;
    }

    private void CheckInstall(bool fromUser)
    {
        Dk2InstallCheck check = Dk2Install.Check(LauncherCopy.CleanPath(_install.Text));
        ShowMessage(_installStatus, LauncherCopy.Install(check));
        bool valid = check == Dk2InstallCheck.Found;
        if (valid == _valid && fromUser) return;
        _valid = valid;
        _play.Disabled = !valid;
        _play.FocusMode = valid ? FocusModeEnum.All : FocusModeEnum.None;
        if (valid) Run(_reduceMotion ? 1f : 0f);
        else Run(0f, running: false);
    }

    private void CheckFont() =>
        ShowMessage(_fontStatus, LauncherCopy.Font(LauncherCopy.CheckFont(LauncherCopy.CleanPath(_font.Text), File.Exists)));

    private static void ShowMessage(Label label, LauncherMessage message)
    {
        label.Text = message.Text;
        label.AddThemeColorOverride("font_color", message.Tone switch
        {
            LauncherTone.Ready => LauncherTheme.KeeperGold,
            LauncherTone.Problem => LauncherTheme.HeartRed,
            _ => LauncherTheme.Fade(LauncherTheme.Bone, .7f),
        });
    }

    /// <summary>Sets the lava's front; below 1 with <paramref name="running"/> it runs on each frame until it reaches Play.</summary>
    private void Run(float progress, bool running = true)
    {
        _progress = progress;
        _seamMaterial.SetShaderParameter(ProgressName, _progress);
        bool lit = _valid && _progress >= 1f;
        if (lit != _lit)
            _play.ThemeTypeVariation = (_lit = lit) ? PlayLitName : PlayWaitingName;
        _running = running && _valid && _progress < 1f;
        SetProcess(_running || !_seamPlaced || _nextKey < _keys.Count);
    }

    public override void _Process(double delta)
    {
        if (!_seamPlaced) PlaceSeam();
        for (; _nextKey < _keys.Count && _keys[_nextKey].Frame <= _frame; _nextKey++)
            foreach (bool pressed in new[] { true, false })
                Input.ParseInputEvent(new InputEventKey
                    { Keycode = _keys[_nextKey].Key, ShiftPressed = _keys[_nextKey].Shift, Pressed = pressed });
        _frame++;
        if (_running) Run(Mathf.Min(1f, _progress + (float)delta / RunSeconds));
        else SetProcess(!_seamPlaced || _nextKey < _keys.Count);
    }

    /// <summary>The crack's points, in the seam's pixels: down the column from the top in jagged steps, then left into
    /// the Play button's edge. Placed once the layout has settled.</summary>
    private void PlaceSeam()
    {
        Vector2 size = _seam.Size;
        Rect2 play = _play.GetGlobalRect();
        if (size.Y <= 0 || play.Size.Y <= 0) return;
        _seamPlaced = true;
        float endY = play.GetCenter().Y - _seam.GlobalPosition.Y;
        const int down = 13;
        var points = new Vector2[16];
        uint state = 0x9E3779B9u;
        for (int i = 0; i < down; i++)
        {
            state = state * 1664525u + 1013904223u;
            float jitter = ((state >> 8) & 0xFFFF) / 65535f - .5f;
            float swing = (i & 1) == 0 ? -1f : 1f;
            float x = size.X * .55f + swing * 11f + jitter * 40f;
            float y = Mathf.Lerp(-6f, endY - 70f, i / (float)(down - 1));
            points[i] = new Vector2(x, y);
        }
        points[down] = new Vector2(size.X * .30f, endY - 22f);
        points[down + 1] = new Vector2(size.X * .12f, endY - 4f);
        points[down + 2] = new Vector2(-2f, endY);
        _seamMaterial.SetShaderParameter(PointsName, points);
        _seamMaterial.SetShaderParameter(CountName, down + 3);
        _seamMaterial.SetShaderParameter(SizeName, size);
    }

    /// <summary>Enter is Play wherever the focus is, once Play is enabled (Space still presses the focused control); Esc is
    /// Quit, also from a field (which would otherwise take it). An open dropdown or Godot's own file dialog keeps both.</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || _dialog?.Visible == true || _sizes.GetPopup().Visible)
            return;
        if (key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Quit();
        }
        else if (key.Keycode is Key.Enter or Key.KpEnter && _valid)
        {
            GetViewport().SetInputAsHandled();
            TryPlay();
        }
    }

    private void TryPlay()
    {
        if (!_valid) return;
        LauncherWindowSize size = _sizeList[Math.Max(0, _sizes.Selected)];
        var settings = new LauncherSettings
        {
            InstallFolder = LauncherCopy.CleanPath(_install.Text), MenuFont = LauncherCopy.CleanPath(_font.Text),
            Screen = _fullScreen.ButtonPressed ? LauncherScreenMode.FullScreen
                : _fullScreenWindow.ButtonPressed ? LauncherScreenMode.FullScreenWindow : LauncherScreenMode.Window,
            WindowSize = size, StartDirectly = _startDirectly.ButtonPressed,
        };
        LauncherSettingsStore.Save(_args, settings);
        PlayRequested?.Invoke(settings);
    }

    private void Quit() => GetTree().Quit();

    private void BrowseInstall() => Browse(FileDialog.FileModeEnum.OpenDir, LauncherCopy.ChooseInstall, _install.Text, [],
        path => { _install.Text = path; CheckInstall(fromUser: true); _install.GrabFocus(); });

    private void BrowseFont() => Browse(FileDialog.FileModeEnum.OpenFile, LauncherCopy.ChooseFont, _font.Text,
        ["*.ttf, *.otf ; Fonts"], path => { _font.Text = path; CheckFont(); _font.GrabFocus(); });

    /// <summary>The system's own file dialog where Godot has one, else Godot's, starting at the field's folder.</summary>
    private void Browse(FileDialog.FileModeEnum mode, string title, string current, string[] filters, Action<string> chosen)
    {
        _dialog?.QueueFree();
        _dialog = new FileDialog
        {
            FileMode = mode, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, Title = title,
            Filters = filters, Size = new Vector2I(820, 520),
        };
        string? start = LauncherCopy.CleanPath(current);
        if (start != null)
        {
            string folder = mode == FileDialog.FileModeEnum.OpenDir ? start : System.IO.Path.GetDirectoryName(start) ?? "";
            if (Directory.Exists(folder)) _dialog.CurrentDir = folder;
        }
        if (mode == FileDialog.FileModeEnum.OpenDir) _dialog.DirSelected += dir => chosen(dir);
        else _dialog.FileSelected += file => chosen(file);
        AddChild(_dialog);
        _dialog.PopupCentered();
    }
}
