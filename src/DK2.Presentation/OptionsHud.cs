// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The engine-free in-game options menu (ADR-0805): a page of <see cref="ClassicOptionsLayout"/> text
/// entries under its title, over the paused level. The layout is in DK2's 1024x768 units; one is 2.5 virtual.</summary>
public sealed class OptionsHud
{
    /// <summary>Virtual units per 1024x768 unit.</summary>
    private const float Unit = 2.5f;
    /// <summary>An entry's hit height: 38.4 units of 768.</summary>
    private const float EntryHeight = 38.4f * Unit;

    private readonly Func<uint, string> _text;
    private readonly Func<int, BitmapFont> _font;
    private readonly bool[] _supported = new bool[Enum.GetValues(typeof(ClassicOptionsAction)).Cast<ClassicOptionsAction>().Max(a => (int)a) + 1];
    private readonly List<(HudText Text, HudRect Rect)> _labels = new();
    private ClassicOptionsPage _page;
    private ClassicSessionAction _confirmation;
    private int _height;
    private bool _visible;

    /// <param name="text">A string table entry by id.</param>
    /// <param name="font">The legible font for a screen height.</param>
    public OptionsHud(Func<uint, string> text, Func<int, BitmapFont> font)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _font = font ?? throw new ArgumentNullException(nameof(font));
        foreach (ClassicOptionsAction action in new[] { ClassicOptionsAction.ContinueGame, ClassicOptionsAction.LevelObjective,
            ClassicOptionsAction.Back, ClassicOptionsAction.EndGame, ClassicOptionsAction.RestartLevel, ClassicOptionsAction.ReturnToMenu,
            ClassicOptionsAction.QuitGame, ClassicOptionsAction.Confirm, ClassicOptionsAction.Cancel })
            SetSupported(action, true);
    }

    private IReadOnlyList<ClassicOptionsEntry> Entries => ClassicOptionsLayout.ForPage(_page);

    public bool Visible => _visible;

    /// <summary>An unsupported action's entry is greyed and takes no press.</summary>
    public void SetSupported(ClassicOptionsAction action, bool supported)
    { if (action != ClassicOptionsAction.None) _supported[(int)action] = supported; }

    /// <summary>Whether an entry with this action takes a press; never for <see cref="ClassicOptionsAction.None"/>.</summary>
    public bool Supported(ClassicOptionsAction action) => action != ClassicOptionsAction.None && _supported[(int)action];

    public void SetVisible(bool visible) => _visible = visible;

    /// <summary>The <see cref="ClassicHudControls"/> action a press on an entry applies; none for a title or nothing.
    /// Save and Load open their pages; the host also refreshes its save list for them (<see cref="RefreshesSaves"/>).
    /// The save browser's Back and Continue lines (<see cref="SaveLoadHud"/>) apply the <see cref="ClassicOptionsAction.Back"/>
    /// and <see cref="ClassicOptionsAction.ContinueGame"/> entries' actions.</summary>
    public static ClassicHudAction HudAction(ClassicOptionsAction action) => action switch
    {
        ClassicOptionsAction.ContinueGame => ClassicHudAction.ToggleOptions,
        ClassicOptionsAction.GameOptions => ClassicHudAction.OpenGameOptions,
        ClassicOptionsAction.LevelObjective => ClassicHudAction.ShowObjective,
        ClassicOptionsAction.Back => ClassicHudAction.BackToOptions,
        ClassicOptionsAction.RestartLevel => ClassicHudAction.RestartLevel,
        ClassicOptionsAction.EndGame => ClassicHudAction.EndGame,
        ClassicOptionsAction.ReturnToMenu => ClassicHudAction.ReturnToMenu,
        ClassicOptionsAction.QuitGame => ClassicHudAction.QuitGame,
        ClassicOptionsAction.Confirm => ClassicHudAction.Confirm,
        ClassicOptionsAction.Cancel => ClassicHudAction.CancelConfirmation,
        ClassicOptionsAction.Save => ClassicHudAction.OpenSave,
        ClassicOptionsAction.Load => ClassicHudAction.OpenLoad,
        _ => ClassicHudAction.None,
    };

    /// <summary>Whether a press on an entry rereads the save list (<see cref="SaveSlotsView.Refresh"/>) before its page
    /// opens: Save and Load.</summary>
    public static bool RefreshesSaves(ClassicOptionsAction action) => action is ClassicOptionsAction.Save or ClassicOptionsAction.Load;

    public void SetPage(ClassicOptionsPage page, ClassicSessionAction confirmation)
    {
        if (_page == page && _confirmation == confirmation) return;
        _page = page; _confirmation = confirmation; _height = 0;
    }

    /// <summary>The supported entry under a point in virtual units.</summary>
    public ClassicOptionsAction ActionAt(float x, float y)
    {
        if (!_visible) return ClassicOptionsAction.None;
        IReadOnlyList<ClassicOptionsEntry> entries = Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            ClassicOptionsEntry entry = entries[i];
            if (_supported[(int)entry.Action] && new HudRect(entry.X * Unit, entry.Y * Unit, entry.Width * Unit, EntryHeight).Contains(x, y))
                return entry.Action;
        }
        return ClassicOptionsAction.None;
    }

    /// <param name="pointer">The pointer in virtual units: the entry under it is lit.</param>
    public void Build(HudScreen screen, (float X, float Y) pointer, HudFrame frame)
    {
        if (!_visible || screen.Height <= 0) return;
        if (_height != screen.Height) Set(screen);
        ClassicOptionsAction hover = ActionAt(pointer.X, pointer.Y);
        int first = _page == ClassicOptionsPage.Objective ? 0 : 1;
        if (first == 1) frame.Add(new HudDraw(HudDrawKind.Quad, HudImage.Of(_labels[0].Text), _labels[0].Rect, HudColor.White));
        for (int i = 0; i < Entries.Count; i++)
        {
            ClassicOptionsAction action = Entries[i].Action;
            byte level = action == ClassicOptionsAction.None ? (byte)200 : !_supported[(int)action] ? (byte)102 : hover == action ? (byte)255 : (byte)200;
            (HudText text, HudRect rect) = _labels[first + i];
            frame.Add(new HudDraw(HudDrawKind.Quad, HudImage.Of(text), rect, HudColor.Grey(level)));
        }
    }

    /// <summary>Sets the title (none on the objective page) and the entries, each at its text's own size.</summary>
    private void Set(HudScreen screen)
    {
        _height = screen.Height; _labels.Clear();
        BitmapFont font = _font(screen.Height);
        if (_page != ClassicOptionsPage.Objective) Label(screen, font, ClassicOptionsLayout.TitleFor(_page, _confirmation), 8, 72);
        foreach (ClassicOptionsEntry entry in Entries) Label(screen, font, entry.TextId, entry.X, entry.Y);
    }

    private void Label(HudScreen screen, BitmapFont font, uint id, float x, float y)
    {
        var text = new HudText(font, _text(id), 0);
        (int width, int height) = HudTextPixels.Measure(text);
        _labels.Add((text, new HudRect(x * Unit, y * Unit, screen.Virtual(width), screen.Virtual(height))));
    }
}
