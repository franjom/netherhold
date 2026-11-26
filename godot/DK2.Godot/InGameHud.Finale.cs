// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.


using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

public partial class InGameHud
{
    private WinScreen? _winScreen;
    private ClassicTabletCanvas? _rewardCanvas;
    private BitmapTextItem? _rewardText;

    private void OpenWinScreen(string install, LevelDef? level)
    {
        if (level == null || _world == null || _scriptedView == null || _resultFlow == null) return;

        var layer = new CanvasLayer { Name = "WinScreenLayer", Layer = SubtitleLayer - 1 };
        AddChild(layer);
        TabletMesh? mesh = _world.LoadTabletMesh();
        if (mesh != null) _rewardCanvas = new ClassicTabletCanvas(layer.GetCanvas(), mesh, _world.TabletTexture);
        _rewardText = new BitmapTextItem(layer.GetCanvas()) { ReplacementFont = _subtitleFace };

        string path = Path.Combine(LevelSubtitles.TextFolder(install), "Speech.str");
        Dk2StringTable? speech = null;
        try
        {
            if (File.Exists(path)) speech = Dk2StringTable.Open(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            GD.PushWarning("DK2.Godot: reward text unavailable: " + ex.Message);
        }

        _winScreen = new WinScreen(WinScreen.RewardsOf(level),
            id => speech != null && speech.TryResolve(id, out string text) ? text : null, mesh?.Points);
        _winScreen.SpeechRequested += QueueRewardSpeech;
        _resultFlow.WinScreen = _winScreen;
        _scriptedView.WinScreenRequested += StartWinScreen;
    }

    private void QueueRewardSpeech(uint id) => _mixer?.QueueSpeech(SoundCategory.Mentor, new SoundId(id + 1));

    private void StartWinScreen()
    {
        long now = (long)(_elapsed * 1000);
        _mixer?.StopSpeech(now);
        _winScreen?.Start(now);
    }

    private void DrawWinScreen(bool visible, int width, int height)
    {
        if (_rewardText == null) return;
        string? text = null;
        if (visible && _winScreen?.Current is RewardItem item)
        {
            _rewardCanvas?.Show(_winScreen.Tablet, item.Id, width * RewardTablet.CentreX, height * RewardTablet.CentreY, height);
            if (_mixer?.SpeechEnabled != true) text = _winScreen.Text;
        }
        else
        {
            _rewardCanvas?.Hide();
        }
        _rewardText.Show(text == null ? null : SubtitleFont(height), text, SubtitleLayout.WinScreenBox(width, height), 1f);
    }

    private void CloseWinScreen()
    {
        if (_scriptedView != null) _scriptedView.WinScreenRequested -= StartWinScreen;
        if (_winScreen != null) _winScreen.SpeechRequested -= QueueRewardSpeech;
        _rewardCanvas?.Dispose();
        _rewardCanvas = null;
        _rewardText?.Dispose();
        _rewardText = null;
        _winScreen = null;
    }
}
