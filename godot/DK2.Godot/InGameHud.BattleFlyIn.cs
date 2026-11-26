// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.


using DK2.Presentation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

public partial class InGameHud
{
    private const int BattleFlyInLayer = 111;
    private readonly Random _battleFlyInRandom = new();
    private BattleFlyIn? _battleFlyIn;
    private ClassicTabletCanvas? _battleCanvas;
    private NumericsVector3[] _battlePoints = [];
    private int _flyInEntries;

    private void OpenBattleFlyIn(TabletMesh? mesh)
    {
        _battlePoints = mesh?.Points ?? [];
        _battleFlyIn = new BattleFlyIn(_battlePoints.Length);
        if (mesh == null || _world == null)
        {
            return;
        }

        var layer = new CanvasLayer { Name = "BattleFlyInLayer", Layer = BattleFlyInLayer };
        AddChild(layer);
        _battleCanvas = new ClassicTabletCanvas(layer.GetCanvas(), mesh, _world.TabletTexture);
    }

    private void DrawBattleFlyIn(HudVisibility visibility, bool collapsed, long now)
    {
        if (_battleFlyIn == null || _battleTab == null || _battles == null || _scriptedView == null)
        {
            return;
        }

        int entries = _battles.ViewerBattleEntries;
        if (!_battleFlyIn.Running && entries == _flyInEntries)
        {
            _battleCanvas?.Hide();
            return;
        }

        Vector2 size = GetViewport().GetVisibleRect().Size;
        var screen = new HudScreen((int)size.X, (int)size.Y);
        if (entries != _flyInEntries)
        {
            _flyInEntries = entries;
            Vector2 target = _battleTab.BattleCentre(collapsed);
            _battleFlyIn.Start(now, screen, new NumericsVector2(target.X, target.Y));
            PlayBattleFlyInSounds(_battleFlyIn);
        }

        if (!_battleFlyIn.BeginFrame(now, visibility.ShowNormalHud, _scriptedView.LetterboxBar > 0,
            _hudControls.OptionsVisible, _battlePoints, _battleFlyInRandom))
        {
            _battleCanvas?.Hide();
            return;
        }

        _battleCanvas?.Show(_battleFlyIn.Tablet, BattleFlyIn.FightItem, 0, 0, screen.Height);
        _battleFlyIn.EndFrame(now);
        PlayBattleFlyInSounds(_battleFlyIn);
    }

    private void PlayBattleFlyInSounds(BattleFlyIn flyIn)
    {
        if (_mixer != null)
        {
            _mixer.Play(flyIn.Sounds);
        }
        else
        {
            flyIn.Sounds.Clear();
        }
    }

    private void CloseBattleFlyIn()
    {
        _battleCanvas?.Dispose();
        _battleCanvas = null;
        _battleFlyIn = null;
        _battlePoints = [];
    }
}
