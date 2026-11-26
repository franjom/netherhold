// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>One frame's mouse presses and modifier keys, read once: the left
/// and right buttons pressed this frame (<c>Input.GetMouseButtonDown</c> 0 and 1) and Shift, Control and Alt held, either
/// side (<c>Input.GetKey</c>).</summary>
public readonly record struct HudPress(bool Primary, bool Secondary, bool Shift = false, bool Control = false, bool Alt = false)
{
    /// <summary>The held modifiers as the gold withdraw reads them (<see cref="KeeperResourceInteraction.WithdrawAmount"/>).</summary>
    public KeeperResourceModifiers ResourceModifiers =>
        (Shift ? KeeperResourceModifiers.Shift : KeeperResourceModifiers.None)
        | (Control ? KeeperResourceModifiers.Control : KeeperResourceModifiers.None)
        | (Alt ? KeeperResourceModifiers.Alt : KeeperResourceModifiers.None);
}

/// <summary>What <see cref="HudPressRouter"/> asks of the adapter: the panels' hit tests under the pointer, the HUD action
/// (through <see cref="HudActionRouter"/>), and what needs the camera, the session or the save files.</summary>
public interface IHudPressHost
{
    /// <summary>Applies a HUD action as the adapter applies every other (<see cref="HudActionRouter.Apply"/>).</summary>
    void ApplyHudAction(ClassicHudAction action);
    /// <summary>The open full map's left press: the camera moves to the tile under the pointer, before the map closes.</summary>
    void GoToFullMapTile();
    /// <summary>The Game Options pages hold the screen: they poll this frame's keys for a binding.</summary>
    void PollGameOptionsKeyboard();
    /// <summary>The options menu's entry under the pointer, None off its entries.</summary>
    ClassicOptionsAction OptionsActionAtPointer();
    /// <summary>The save list is reread before the Save and Load pages open (<see cref="OptionsHud.RefreshesSaves"/>).</summary>
    void RefreshSaves();
    /// <summary>A confirmed restart, return to the menu or quit (never None).</summary>
    void StartSession(ClassicSessionAction session);
    /// <summary>The creature overview's press under the pointer (<see cref="CreatureOverviewHud.TryPress"/>).</summary>
    bool TryPressOverview(in HudPress press, out CreatureOverviewRequest request);
    /// <summary>The overview's focus: the camera moves to <paramref name="position"/>.</summary>
    void FocusCreature(FpPosition3 position);
    /// <summary>The minimap's press under the pointer (<see cref="MinimapHud.Press"/>, or its corners alone without a
    /// camera), None when it does not act.</summary>
    ClassicHudAction PressMinimap(in HudPress press);
    /// <summary>The resource strip's item under the pointer.</summary>
    KeeperResourceKind ResourceAtPointer();
}

/// <summary>Routes one frame's press to the eligible panels, offered
/// to the HUD in its order, the first taker ending it. The open full map takes every frame (a left press moves the camera,
/// any press closes it); the open options menu takes every frame (the Game Options pages poll the keys, otherwise a left
/// press applies its entry, <see cref="PressOptions"/>); then the creature overview's press (a pickup through
/// <see cref="PickupFlow"/>, or a camera focus), the minimap's, a left press on the gold (a withdraw, then
/// <see cref="HudToolState.ResetAfterGoldWithdraw"/>) and a right press on the heart (its navigation).</summary>
public sealed class HudPressRouter
{
    private readonly ClassicHudControls _controls;
    private readonly HudToolState _tools;
    private readonly CreatureOverview _overview;
    private readonly BottomPanelView _panel;
    private readonly PickupFlow _pickups;
    private readonly PlayerId _viewer;
    private readonly IHudPressHost _host;

    public HudPressRouter(ClassicHudControls controls, HudToolState tools, CreatureOverview overview, BottomPanelView panel,
        PickupFlow pickups, PlayerId viewer, IHudPressHost host)
    {
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _pickups = pickups ?? throw new ArgumentNullException(nameof(pickups));
        _viewer = viewer;
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Offers this frame's <paramref name="press"/> to the HUD; true when the HUD took the frame, so the world gets
    /// nothing. Commands are added to <paramref name="output"/>; <paramref name="milliseconds"/> is the host's wall clock.</summary>
    public bool Route(in HudPress press, WorldState world, GameDatabase database, long milliseconds, List<PlayerCommand> output)
    {
        if (world is null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (_controls.FullMapVisible)
        {
            FullMapPress fullMap = FullMapHud.Press(press.Primary, press.Secondary);
            if (fullMap == FullMapPress.GoToTile)
            {
                _host.GoToFullMapTile();
            }

            _host.ApplyHudAction(FullMapHud.Action(fullMap));
            return true;
        }
        if (_controls.OptionsVisible)
        {
            if (_controls.GameOptionsVisible) { _host.PollGameOptionsKeyboard(); return true; }
            if (press.Primary)
            {
                PressOptions(_host.OptionsActionAtPointer());
            }

            return true;
        }
        if (_host.TryPressOverview(press, out CreatureOverviewRequest request))
        {
            CreatureOverviewAction selected = _overview.Select(world, request.Type, request.Selection, request.Filter);
            // A refused pickup still takes the press (the step-2 audit's open point; the world click skips instead).
            if (selected.Kind == CreatureOverviewActionKind.Pickup)
            {
                _pickups.PickUpFromOverview(world, database, _viewer, selected.Target, _overview, milliseconds, output);
            }
            else if (selected.Kind == CreatureOverviewActionKind.Focus)
            {
                _host.FocusCreature(selected.Position);
            }

            return true;
        }
        ClassicHudAction minimap = _host.PressMinimap(press);
        if (minimap != ClassicHudAction.None) { _host.ApplyHudAction(minimap); return true; }
        KeeperResourceKind resource = _host.ResourceAtPointer();
        if (resource == KeeperResourceKind.Gold && press.Primary)
        {
            output.Add(KeeperResourceInteraction.Withdraw(_viewer, press.ResourceModifiers));
            _tools.ResetAfterGoldWithdraw(_panel);
            return true;
        }
        if (resource == KeeperResourceKind.Heart && press.Secondary)
        {
            _host.ApplyHudAction(ClassicHudAction.NavigateHeart);
            return true;
        }
        return false;
    }

    /// <summary>A left press on the options menu's <paramref name="action"/> (None off its entries): the save list reread
    /// for Save and Load, the action applied (<see cref="OptionsHud.HudAction"/>), then the session a confirmation asks
    /// for taken and handed to the host.</summary>
    public void PressOptions(ClassicOptionsAction action)
    {
        if (OptionsHud.RefreshesSaves(action))
        {
            _host.RefreshSaves();
        }

        _host.ApplyHudAction(OptionsHud.HudAction(action));
        ClassicSessionAction session = _controls.TakeSessionAction();
        if (session != ClassicSessionAction.None)
        {
            _host.StartSession(session);
        }
    }
}
