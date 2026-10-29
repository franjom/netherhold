// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
namespace DK2.Presentation;

public enum ClassicHudAction : byte
{
    None, ToggleGui, ToggleOptions, ToggleFullMap, NavigateHeart, SelectQuery, SelectSell, ZoomIn, ZoomOut,
    ShowObjective, CloseObjective,
    RestartLevel, EndGame, ReturnToMenu, QuitGame, Confirm, CancelConfirmation, BackToOptions, OpenSave, OpenLoad,
}

public enum ClassicOptionsPage : byte { Root, Objective, Exit, Confirmation, Save, Load }
public enum ClassicSessionAction : byte { None, Restart, ReturnToMenu, Quit }

[Dk2Behavior(Confidence.Confirmed, "docs/research/minimap-controls.md")]
public sealed class ClassicHudControls
{
    public bool GuiVisible { get; private set; } = true;
    public bool OptionsVisible { get; private set; }
    public ClassicOptionsPage OptionsPage { get; private set; }
    public ClassicSessionAction Confirmation { get; private set; }
    private ClassicSessionAction _sessionAction;
    public bool ObjectiveVisible => OptionsVisible && OptionsPage == ClassicOptionsPage.Objective;
    public ClassicSessionAction TakeSessionAction()
    {
        ClassicSessionAction action = _sessionAction;
        _sessionAction = ClassicSessionAction.None;
        return action;
    }
    public bool FullMapVisible { get; private set; }
    public bool InputSuppressed { get; private set; }

    public void SetGuiVisible(bool visible) => GuiVisible = visible;

    [Dk2Behavior(Confidence.Confirmed, "docs/research/hud-visibility.md#result-and-special-player-state-binary-2026-10-24")]
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
            or ClassicHudAction.NavigateHeart or ClassicHudAction.SelectQuery or ClassicHudAction.SelectSell
            or ClassicHudAction.ZoomIn or ClassicHudAction.ZoomOut) return;
        switch (action)
        {
            case ClassicHudAction.ToggleGui: GuiVisible = !GuiVisible; break;
            case ClassicHudAction.ToggleOptions:
                OptionsVisible = !OptionsVisible; OptionsPage = ClassicOptionsPage.Root;
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

[Dk2Behavior(Confidence.Confirmed, "docs/research/minimap-controls.md")]
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
    private static bool In(Vector2 p,float left,float top,float right,float bottom) =>
        p.X >= left && p.X < right && p.Y >= top && p.Y < bottom;
}
