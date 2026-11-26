// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws the Mentor's subtitle over the letterbox and the hand cursor while a camera path plays. <see cref="ScriptedViewDirector.Subtitle"/> supplies the timed line.</summary>
public partial class InGameHud
{
    private const int SubtitleLayer = 103;
    private BitmapTextItem? _subtitle;
    private FontFile? _subtitleFace;
    private BitmapFont? _subtitleFont;
    private int _subtitleFontHeight = -1;

    private void OpenSubtitles()
    {
        var layer = new CanvasLayer { Name = "SubtitleLayer", Layer = SubtitleLayer };
        AddChild(layer);
        _subtitleFace = ClassicFrontEndMenuAssets.LoadReplacementFont();
        _subtitle = new BitmapTextItem(layer.GetCanvas()) { ReplacementFont = _subtitleFace };
        OpenResultPrompt(layer, _subtitleFace);
    }

    private void DrawSubtitles()
    {
        if (_subtitle == null || _scriptedView == null)
        {
            return;
        }

        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        string? text = _scriptedView.Subtitle;
        _subtitle.Show(text == null ? null : SubtitleFont(height), text, SubtitleLayout.SpeechBox(width, height), 1f);
    }

    /// <summary>The bitmap font for the screen height, read once a height; null, with one warning, when the install lacks it.</summary>
    private BitmapFont? SubtitleFont(int height)
    {
        if (height == _subtitleFontHeight || _sources == null)
        {
            return _subtitleFont;
        }

        _subtitleFontHeight = height;
        try
        {
            _subtitleFont = _sources.Font(InterfaceFonts.LegibleFileName(height));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _subtitleFont = null;
            GD.PushWarning("DK2.Godot: subtitle font unavailable: " + ex.Message);
        }
        return _subtitleFont;
    }

    private void CloseSubtitles()
    {
        _subtitle?.Dispose();
        _subtitle = null;
        CloseResultPrompt();
        _subtitleFace?.Dispose();
        _subtitleFace = null;
    }
}
