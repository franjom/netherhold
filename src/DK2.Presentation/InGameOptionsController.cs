// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The native in-level settings family keeps the Options pause and applies edits live.</summary>
public sealed class InGameOptionsController
{
    private readonly ClassicHudControls _hud;
    public InGameOptionsController(ClassicHudControls hud, OptionsPagesController settings)
        => (_hud, Settings) = (hud, settings);
    public OptionsPagesController Settings { get; }
    public InGameOptionsPage Page { get; private set; }
    public void Open()
    {
        if (!_hud.OptionsVisible) return;
        _hud.Apply(ClassicHudAction.OpenGameOptions); Page = InGameOptionsPage.Root;
    }
    public bool Navigate(InGameOptionsPage page)
    {
        bool allowed = Page == InGameOptionsPage.Root && page is InGameOptionsPage.Graphics or InGameOptionsPage.Sound or InGameOptionsPage.Controls
            || Page == InGameOptionsPage.Controls && page == InGameOptionsPage.Keyboard;
        if (!allowed) return false;
        Page = page;
        Settings.Enter(page switch { InGameOptionsPage.Graphics => FrontEndPage.Graphics, InGameOptionsPage.Sound => FrontEndPage.Sound,
            _ => FrontEndPage.Controls }, OptionsApplicationMode.InGame);
        return true;
    }
    public void Back()
    {
        Settings.CancelKeyCapture();
        if (Page == InGameOptionsPage.Keyboard) { Page = InGameOptionsPage.Controls; return; }
        if (Page == InGameOptionsPage.Root) { Hide(); _hud.Apply(ClassicHudAction.BackToOptions); return; }
        Settings.Accept(); Page = InGameOptionsPage.Root;
    }
    public void Continue()
    {
        Hide(); if (_hud.GameOptionsVisible) _hud.Apply(ClassicHudAction.ToggleOptions);
    }
    public void Hide()
    {
        if (Settings.Page != FrontEndPage.None) Settings.Accept();
        Page = InGameOptionsPage.None;
    }
    public bool CanNavigate(InGameOptionsPage page) => page is InGameOptionsPage.Graphics or InGameOptionsPage.Sound or InGameOptionsPage.Controls or InGameOptionsPage.Keyboard;
}
