// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;
using DK2.Simulation.Commands;
namespace DK2.Presentation;

public enum ClassicHudAction : byte
{
    None, ToggleGui, ToggleOptions, ToggleFullMap, NavigateHeart, SelectQuery, SelectSell, ZoomIn, ZoomOut, NavigatePortal,
    ShowObjective, CloseObjective,
    RestartLevel, EndGame, ReturnToMenu, QuitGame, Confirm, CancelConfirmation, BackToOptions, OpenSave, OpenLoad, OpenGameOptions, NavigateFight,
}

public enum ClassicOptionsPage : byte { Root, Objective, Exit, Confirmation, Save, Load, GameOptions }
public enum ClassicSessionAction : byte { None, Restart, ReturnToMenu, Quit }

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/minimap-controls.md")]
public sealed class ClassicHudControls
{
    public SoundRequests Sounds { get; } = new();
    public bool GuiVisible { get; private set; } = true;
    public bool OptionsVisible { get; private set; }
    public ClassicOptionsPage OptionsPage { get; private set; }
    public ClassicSessionAction Confirmation { get; private set; }
    private ClassicSessionAction _sessionAction;
    public bool GameOptionsVisible => OptionsVisible && OptionsPage == ClassicOptionsPage.GameOptions;
    public bool ObjectiveVisible => OptionsVisible && OptionsPage == ClassicOptionsPage.Objective;
    /// <summary>Whether the options menu's own page shows (<see cref="OptionsHud"/>): open, and not on the pages other
    /// panels draw (save, load, game options).</summary>
    public bool OptionsMenuVisible => OptionsVisible && OptionsPage != ClassicOptionsPage.Save
        && OptionsPage != ClassicOptionsPage.Load && !GameOptionsVisible;
    /// <summary>The options menu's own page under the HUD's visibility: <see cref="OptionsMenuVisible"/> while the normal
    /// HUD shows.</summary>
    public bool OptionsMenuShown(HudVisibility hud) => hud.ShowNormalHud && OptionsMenuVisible;
    /// <summary>The game options pages (<see cref="GameOptionsHud"/>) under the HUD's visibility: <see cref="GameOptionsVisible"/>
    /// while the normal HUD shows.</summary>
    public bool GameOptionsShown(HudVisibility hud) => hud.ShowNormalHud && GameOptionsVisible;
    /// <summary>A panel that holds the screen is open (the options menu or the full map): the other panels and the world
    /// take no ordinary input.</summary>
    public bool Modal => OptionsVisible || FullMapVisible;
    /// <summary>Whether the named-save browser shows (<see cref="SaveLoadHud"/>): the options menu open on its save or
    /// load page.</summary>
    public bool SaveLoadVisible => OptionsVisible && OptionsPage is ClassicOptionsPage.Save or ClassicOptionsPage.Load;
    /// <summary>The in-game save browser's showing under the HUD's visibility (DungeonRunner.DrawSavePanel): shown while
    /// <see cref="SaveLoadVisible"/> and the normal HUD shows; saving on the save page, where the name is typed in.</summary>
    public (bool Shown, bool Saving) SaveLoadPage(HudVisibility hud) =>
        (hud.ShowNormalHud && SaveLoadVisible, OptionsPage == ClassicOptionsPage.Save);
    /// <summary>The save page's name field takes the keys, so no HUD shortcut fires.</summary>
    public bool EditingText => OptionsVisible && OptionsPage == ClassicOptionsPage.Save;
    /// <summary>No turn runs, and none accrues, while the options menu is open; the bottom panel shows the game paused.</summary>
    public bool TicksPaused => OptionsVisible;

    /// <summary>A panel's visibility under <see cref="Modal"/>: shown as <paramref name="hud"/> says, taking input only
    /// while nothing modal is open.</summary>
    public HudVisibility ModalInput(HudVisibility hud) => new(hud.ShowNormalHud, hud.AllowOrdinaryInput && !Modal);

    /// <summary>The context the HUD shortcuts are read in (<see cref="GameBindingInput.HudShortcut"/>).</summary>
    /// <param name="camera">The camera's visibility (the scripted view's), before this state's own gates.</param>
    /// <param name="allowOptionsKey">False while a scripted camera path plays, which holds the options key back.</param>
    /// <param name="capturingKey">A key binding is being captured on the controls page.</param>
    public GameShortcutContext ShortcutContext(HudVisibility camera, bool allowOptionsKey, bool capturingKey) => new()
    {
        OrdinaryInput = camera.AllowOrdinaryInput && !InputSuppressed,
        AllowOptionsKey = allowOptionsKey,
        OptionsVisible = OptionsVisible,
        FullMapVisible = FullMapVisible,
        EditingText = EditingText,
        CapturingKey = capturingKey,
    };
    public ClassicSessionAction TakeSessionAction()
    {
        ClassicSessionAction action = _sessionAction;
        _sessionAction = ClassicSessionAction.None;
        return action;
    }
    public bool FullMapVisible { get; private set; }
    /// <summary>The full map under the camera's visibility (the scripted view's, before this state's own gates, which
    /// hide the normal HUD under the map): <see cref="FullMapVisible"/> while the camera shows the normal HUD.</summary>
    public bool FullMapShown(HudVisibility camera) => camera.ShowNormalHud && FullMapVisible;
    public bool InputSuppressed { get; private set; }

    /// <summary>Whether a host acts on a HUD action at all (DungeonRunner.ApplyHudAction's gate): not None, input not
    /// suppressed, and while <see cref="Modal"/> none of the camera's and the minimap's own actions (navigation, the query
    /// and sell corners, zoom), which the host applies itself rather than through <see cref="Apply"/>.</summary>
    public bool Accepts(ClassicHudAction action) =>
        action != ClassicHudAction.None && !InputSuppressed
        && !(Modal && action is ClassicHudAction.NavigateHeart or ClassicHudAction.NavigatePortal or ClassicHudAction.NavigateFight
            or ClassicHudAction.SelectQuery or ClassicHudAction.SelectSell or ClassicHudAction.ZoomIn or ClassicHudAction.ZoomOut);

    public void SetGuiVisible(bool visible) => GuiVisible = visible;

    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/hud-visibility.md#result-and-special-player-state-binary-2026-10-24")]
    public void UpdateResult(HudResultState result, CameraModeId camera = default)
    {
        if (camera != CameraModeId.ScriptedPath) InputSuppressed = false;
        if (result.HasResult && !result.Continued)
        {
            InputSuppressed = true;
            GuiVisible = false;
        }
        if (result.RestoreRequested && result.Continued)
        {
            InputSuppressed = false;
            GuiVisible = true;
        }
    }

    public void Apply(ClassicHudAction action)
    {
        if (InputSuppressed) return;
        if (FullMapVisible && action != ClassicHudAction.ToggleFullMap) return;
        if (OptionsVisible && action is ClassicHudAction.ToggleGui or ClassicHudAction.ToggleFullMap
            or ClassicHudAction.NavigateHeart or ClassicHudAction.NavigatePortal or ClassicHudAction.NavigateFight or ClassicHudAction.SelectQuery or ClassicHudAction.SelectSell
            or ClassicHudAction.ZoomIn or ClassicHudAction.ZoomOut) return;
        switch (action)
        {
            case ClassicHudAction.ToggleGui: GuiVisible = !GuiVisible; break;
            case ClassicHudAction.ToggleOptions:
                OptionsVisible = !OptionsVisible; GuiVisible = true;
                Sounds.Add(new SoundRequest(SoundCategory.None, new SoundId(OptionsVisible ? 0x139u : 0x13au)));
                OptionsPage = ClassicOptionsPage.Root;
                Confirmation = ClassicSessionAction.None; GuiVisible = true; break;
            case ClassicHudAction.ShowObjective:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Root) OptionsPage = ClassicOptionsPage.Objective;
                break;
            case ClassicHudAction.CloseObjective:
                if (ObjectiveVisible) OptionsPage = ClassicOptionsPage.Root;
                break;
            case ClassicHudAction.BackToOptions:
                if (OptionsVisible) { OptionsPage = ClassicOptionsPage.Root; Confirmation = ClassicSessionAction.None; }
                break;
            case ClassicHudAction.EndGame:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Root) OptionsPage = ClassicOptionsPage.Exit;
                break;
            case ClassicHudAction.OpenGameOptions:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Root) OptionsPage = ClassicOptionsPage.GameOptions;
                break;
            case ClassicHudAction.OpenSave:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Root) OptionsPage = ClassicOptionsPage.Save;
                break;
            case ClassicHudAction.OpenLoad:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Root) OptionsPage = ClassicOptionsPage.Load;
                break;
            case ClassicHudAction.RestartLevel:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Root) Confirm(ClassicSessionAction.Restart);
                break;
            case ClassicHudAction.ReturnToMenu:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Exit) Confirm(ClassicSessionAction.ReturnToMenu);
                break;
            case ClassicHudAction.QuitGame:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Exit) Confirm(ClassicSessionAction.Quit);
                break;
            case ClassicHudAction.Confirm:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Confirmation) _sessionAction = Confirmation;
                break;
            case ClassicHudAction.CancelConfirmation:
                if (OptionsVisible && OptionsPage == ClassicOptionsPage.Confirmation)
                {
                    OptionsPage = Confirmation == ClassicSessionAction.Restart ? ClassicOptionsPage.Root : ClassicOptionsPage.Exit;
                    Confirmation = ClassicSessionAction.None;
                }
                break;
            case ClassicHudAction.ToggleFullMap: FullMapVisible = !FullMapVisible; break;
        }
    }
    private void Confirm(ClassicSessionAction action)
    {
        Confirmation = action;
        OptionsPage = ClassicOptionsPage.Confirmation;
    }
    public HudVisibility Visibility(HudVisibility camera) =>
        new(camera.ShowNormalHud && GuiVisible && !FullMapVisible, camera.AllowOrdinaryInput && !InputSuppressed);
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/minimap-controls.md")]
public static class ClassicHudControlLayout
{
    public static ClassicHudAction ActionAt(Vector2 point, bool secondary)
    {
        if (MinimapControlLayout.IsMagnifier(point)) return secondary ? ClassicHudAction.ZoomOut : ClassicHudAction.ZoomIn;
        if (secondary) return ClassicHudAction.None;
        if (In(point, 8, 584, 40, 616)) return ClassicHudAction.SelectQuery;
        if (In(point, 155.2f, 584, 187.2f, 616)) return ClassicHudAction.SelectSell;
        if (In(point, 8, 728, 40, 760)) return ClassicHudAction.ToggleOptions;
        return ClassicHudAction.None;
    }
    /// <summary>The GUI button (game action 0x72, ADR-0670) a minimap corner's action posts: the magnifier's either click,
    /// the query corner and the sell corner; false for every other action.</summary>
    public static bool TryGuiButton(ClassicHudAction action, out GuiButtonTarget target)
    {
        switch (action)
        {
            case ClassicHudAction.ZoomIn: case ClassicHudAction.ZoomOut: target = GuiButtonTarget.MinimapZoom; return true;
            case ClassicHudAction.SelectQuery: target = GuiButtonTarget.Query; return true;
            case ClassicHudAction.SelectSell: target = GuiButtonTarget.Sell; return true;
            default: target = default; return false;
        }
    }
    private static bool In(Vector2 p,float left,float top,float right,float bottom) =>
        p.X >= left && p.X < right && p.Y >= top && p.Y < bottom;
}
