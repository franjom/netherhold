// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

public partial class InGameHud
{
    private readonly PossessionHud _possessionHud = new();
    private readonly KeeperResourceHud _possessionResources = new();
    private readonly HudFrame _possessionFrame = new(), _possessionResourceFrame = new(), _possessionFilterFrame = new();
    private readonly HudFrame _possessionCreatureFrame = new();
    private PossessionHudView? _possessionHudView;
    private PossessionHudIndicators? _possessionIndicators;
    private HudCanvas? _possessionCanvas;
    private HudCanvas? _possessionFilterCanvas;
    private HudCanvas? _possessionCreatureCanvas;

    private void OpenPossessionHud(string install, CampaignLevelData level)
    {
        if (_sources == null)
        {
            return;
        }

        _possessionHudView = new(level.Database, new InstallPossessionHudArt(install));
        if (_art != null)
        {
            Func<byte, string> names = PlayerNames.From(level.Players);
            _possessionIndicators = new(_art, _art.Font, player => names(player.Value));
        }
        _possessionCanvas = new HudCanvas { Name = "PossessionHud", Load = _sources.Texture, Visible = false };
        _possessionFilterCanvas = new HudCanvas { Name = "PossessionFilters", Load = _sources.Texture, Visible = false };
        _possessionCreatureCanvas = new HudCanvas { Name = "PossessionCreaturePanel", Load = _sources.Texture, Visible = false };
        foreach (HudImage image in PossessionHud.Images())
        {
            _possessionCanvas.Preload(image);
        }

        AddChild(_possessionCanvas);
        AddChild(_possessionFilterCanvas);
        AddChild(_possessionCreatureCanvas);
        if (_minimap != null)
        {
            MoveChild(_possessionFilterCanvas, _minimap.GetIndex());
        }

        MoveChild(_possessionCreatureCanvas, _possessionFilterCanvas.GetIndex() + 1);
    }

    private void DrawPossessionHud(long milliseconds)
    {
        if (_possessionCanvas == null || _possessionHudView == null || _simulation == null || _possession == null)
        {
            return;
        }

        PossessionHudSnapshot? snapshot = PossessionHud.ShowPanel(_possession.Possessing,
            _scriptedView!.PlayedHud(_hudControls, ClassicCameraMode.Dungeon), _possession.SettledPossessionCamera)
            ? _possessionHudView.Capture(_simulation.World, _viewer, _possession.Controls,
                firstPersonCameraSettled: _possession.SettledPossessionCamera) : null;
        _possessionCanvas.Visible = snapshot != null;
        if (_possessionFilterCanvas != null)
        {
            _possessionFilterCanvas.Visible = snapshot != null;
        }

        if (_possessionCreatureCanvas != null)
        {
            _possessionCreatureCanvas.Visible = snapshot != null;
        }

        if (snapshot == null)
        {
            return;
        }

        Vector2 size = GetViewport().GetVisibleRect().Size;
        var layout = new HudScaleLayout((int)size.X, (int)size.Y, _settings.UiScale);
        HudScreen screen = layout.Screen;
        _possessionFrame.Clear();
        _possessionHud.BuildChrome(layout, snapshot, _possessionFrame);
        _possessionCreatureFrame.Clear();
        _possessionHud.BuildCreaturePanel(layout, snapshot, _possessionCreatureFrame);
        _possessionFilterFrame.Clear();
        _possessionHud.BuildFilters(layout, snapshot, _possessionFilterFrame);
        _possessionFilterCanvas?.Present(_possessionFilterFrame, Vector2.Zero, screen.Scale, layout.Factor);
        _possessionResourceFrame.Clear();
        _possessionResources.Build(screen, _resourceFacts, unchecked((uint)milliseconds), KeeperResourceFlash.None, _possessionResourceFrame);
        PossessionHud.AppendResourceCorner(layout, _possessionResourceFrame, _possessionCreatureFrame);
        _possessionCreatureCanvas?.Present(_possessionCreatureFrame, Vector2.Zero, screen.Scale, layout.Factor);
        _possessionIndicators?.Build(layout,
            HudIndicatorSnapshot.Capture(_simulation.World, _simulation.Database.Level, _viewer), _possessionFrame);
        _possessionCanvas.Present(_possessionFrame, Vector2.Zero, screen.Scale, layout.Factor);
    }
}
