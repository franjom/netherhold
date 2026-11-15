// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Data.Model;
using DK2.Data.Save;

namespace DK2.Presentation;

/// <summary>The engine-free named-save browser (ADR-0805) in the original font; its native destinations are
/// Options23/24 in game and front-end menu 8. Laid out in DK2's 1024x768 units; one is 2.5 virtual.</summary>
public sealed class SaveLoadHud
{
    private const float Unit = 2.5f;
    private static readonly HudColor Unselected = HudColor.Grey(200), Unreadable = HudColor.Grey(120);

    private enum Press : byte { None, Select, Submit, Continue, Back }

    private readonly Func<uint, string> _text;
    private readonly Func<int, BitmapFont> _font;
    private readonly List<(HudText Text, HudRect Rect, HudColor Tint, Press Press, int Index)> _lines = new();
    private SaveSlotsView? _view;
    private int _height, _revision = -1, _first;
    private bool _save, _frontEnd, _visible;
    private string _error = "";

    /// <summary>Enter in the name, or a press on the name or the Load entry.</summary>
    public event Action? Submit;
    /// <summary>A press on the Back or Continue line: the options menu's action it applies, the same as that menu's own
    /// Back and Continue Game entries (<see cref="OptionsHud.HudAction"/>; DungeonRunner.InitializeSaves applied them).</summary>
    public event Action<ClassicHudAction>? HudAction;

    /// <param name="text">A string table entry by id.</param>
    /// <param name="font">The legible font for a screen height.</param>
    public SaveLoadHud(Func<uint, string> text, Func<int, BitmapFont> font)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _font = font ?? throw new ArgumentNullException(nameof(font));
    }

    public bool Visible => _visible && _view != null;

    /// <param name="save">Saving: the name is typed in; otherwise the selected save loads.</param>
    /// <param name="frontEnd">The front end's list, which has no title, name or buttons of its own.</param>
    public void Show(SaveSlotsView? view, bool visible, bool save, bool frontEnd = false)
    {
        bool changed = _view != view || _save != save || _frontEnd != frontEnd || visible && !_visible;
        _view = view; _visible = visible; _save = save; _frontEnd = frontEnd;
        if (changed) { _revision = -1; _first = 0; _error = ""; }
    }

    public void Error(string message) { _error = message; _revision = -1; }

    /// <summary>Typed characters for the in-game save name: backspace deletes, Enter submits. A span, so a host can pass
    /// its own reused buffer (a string converts).</summary>
    public void Type(ReadOnlySpan<char> characters)
    {
        if (!Visible || _frontEnd || !_save) return;
        for (int i = 0; i < characters.Length; i++)
        {
            char c = characters[i];
            string name = _view!.Name;
            if (c == '\b') { if (name.Length > 0) _view.SetName(name[..^1]); }
            else if (c == '\n' || c == '\r') Submit?.Invoke();
            else if (!char.IsControl(c)) _view.SetName(name + c);
        }
    }

    /// <summary>The wheel scrolls the list a line a notch, positive up, no further than its last page.</summary>
    public void Wheel(int notches)
    {
        if (!Visible || notches == 0) return;
        int next = Math.Clamp(_first - notches, 0, Math.Max(0, _view!.Entries.Count - Lines));
        if (next != _first) { _first = next; _revision = -1; }
    }

    public void Build(HudScreen screen, HudFrame frame)
    {
        if (!Visible || screen.Height <= 0) return;
        if (_height != screen.Height || _revision != _view!.Revision) Set(screen);
        foreach (var line in _lines) frame.Add(new HudDraw(HudDrawKind.Quad, HudImage.Of(line.Text), line.Rect, line.Tint));
    }

    /// <summary>Whether a line that takes a press is at a point in virtual units.</summary>
    public bool Hits(float x, float y) => Visible && LineAt(x, y) >= 0;

    /// <summary>A primary press on a line; true when one took it.</summary>
    public bool PointerDown(float x, float y, int button)
    {
        if (button != 0 || !Visible) return false;
        int at = LineAt(x, y);
        if (at < 0) return false;
        var line = _lines[at];
        switch (line.Press)
        {
            case Press.Select:
                if (!_view!.Select(line.Index)) break;
                SavedSessionEntry entry = _view.Selected!;
                _error = entry.Readable ? "" : entry.Refusal?.Message ?? "";
                _revision = -1;
                break;
            case Press.Submit: Submit?.Invoke(); break;
            case Press.Continue: HudAction?.Invoke(OptionsHud.HudAction(ClassicOptionsAction.ContinueGame)); break;
            case Press.Back: HudAction?.Invoke(OptionsHud.HudAction(ClassicOptionsAction.Back)); break;
        }
        return true;
    }

    /// <summary>Saves listed at once: 7 in game, 11 in the front end.</summary>
    private int Lines => _frontEnd ? 11 : 7;

    private int LineAt(float x, float y)
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
            if (_lines[i].Press != Press.None && _lines[i].Rect.Contains(x, y)) return i;
        return -1;
    }

    private void Set(HudScreen screen)
    {
        SaveSlotsView view = _view!;
        _height = screen.Height; _revision = view.Revision; _lines.Clear();
        BitmapFont font = _font(screen.Height);
        float x = _frontEnd ? 112 : 8, y = _frontEnd ? 188.8f : 112;
        if (!_frontEnd) Line(screen, font, _text(_save ? 201u : 143u), 8, 72, HudColor.White, Press.None);
        int shown = Math.Min(Lines, view.Entries.Count - _first);
        for (int i = 0; i < shown; i++)
        {
            int index = _first + i;
            SavedSessionEntry entry = view.Entries[index];
            string caption = entry.DisplayName;
            // An unreadable save stays listed so the player can see why it will not load (ADR-0588).
            // English until ADR-0587's catalog exists.
            if (!entry.Readable) caption += "   (cannot be loaded)";
            else if (_frontEnd && entry.Header is { } header) caption += "   " + header.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            HudColor tint = index == view.SelectedIndex ? HudColor.White : entry.Readable ? Unselected : Unreadable;
            Line(screen, font, caption, x, y + i * 32, tint, Press.Select, index);
        }
        if (!_frontEnd)
        {
            if (_save) Line(screen, font, view.Name + "_", 8, 440, HudColor.White, Press.Submit);
            else if (view.Selected != null) Line(screen, font, _text(143), 8, 440, HudColor.White, Press.Submit);
            Line(screen, font, _text(142), 8, 512, HudColor.White, Press.Continue);
            Line(screen, font, _text(20), 512, 512, HudColor.White, Press.Back);
        }
        if (!string.IsNullOrEmpty(_error)) Line(screen, font, _error, x, _frontEnd ? 600 : 480, HudColor.White, Press.None);
    }

    private void Line(HudScreen screen, BitmapFont font, string value, float x, float y, HudColor tint, Press press, int index = -1)
    {
        var text = new HudText(font, value, 0);
        (int width, int height) = HudTextPixels.Measure(text);
        _lines.Add((text, new HudRect(x * Unit, y * Unit, screen.Virtual(width), screen.Virtual(height)), tint, press, index));
    }
}
