// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The options menu's confirmed Restart, Return to Menu and Quit (<see cref="HudPressRouter"/>'s session action,
/// mapped by <see cref="GameSessionRequest.ForSessionAction"/> as Unity's runner's <c>StartSession</c> maps it), the level
/// end's prompt (Unity's runner's <c>DrawCampaignResultPrompt</c>) and the <c>--key-path</c> check's key taps.</summary>
public partial class InGameHud
{
    /// <summary>The script frames a handover (a restart or a loaded save) continues from, so the next instance's
    /// <c>--pointer-path</c> and <c>--key-path</c> go on where this one stopped instead of playing again; taken by
    /// <see cref="TakeCarriedScriptFrames"/>.</summary>
    private static (int Pointer, int Key)? s_carriedScriptFrames;

    private BitmapTextItem? _resultPrompt;
    private KeyScript? _keyScript;
    private int _keyFrame = -1, _nextKey;

    /// <summary>Unity's runner's <c>StartSession</c>: Restart reloads this scene for the same level and return page, Return
    /// to Menu opens <c>Main.tscn</c> at the campaign map for a level launched from it and at the main page otherwise, and
    /// Quit quits. A handover already under way takes no second one.</summary>
    void IHudPressHost.StartSession(ClassicSessionAction session)
    {
        if (_context == null || _leaving || s_resume != null) return;
        if (session == ClassicSessionAction.Quit)
        {
            _leaving = true;
            GD.Print("DK2.Godot: the options menu's Quit; quitting.");
            GetTree().Quit();
            return;
        }
        if (GameSessionRequest.ForSessionAction(session, _context) is not GameSessionRequest request) return;
        if (request.Mode == GameSessionMode.FrontEnd)
        {
            LeaveForFrontEnd(request.ReturnPage, "the options menu's Return to Menu");
            return;
        }
        s_resume = request;
        GD.Print("DK2.Godot: the options menu's Restart; reloading the scene for ", request.LevelName, " (return page ",
            request.ReturnPage.ToString(), ").");
        Callable.From(ReloadForResume).CallDeferred();
    }

    /// <summary>Hands the script frames to the instance a reload makes.</summary>
    private void CarryScriptFrames() => s_carriedScriptFrames = (_scriptFrame, _keyFrame);

    /// <summary>The frames a handover carried, once; a fresh start begins at -1.</summary>
    private void TakeCarriedScriptFrames()
    {
        if (s_carriedScriptFrames is (int pointer, int key)) (_scriptFrame, _keyFrame) = (pointer, key);
        s_carriedScriptFrames = null;
    }

    private void OpenResultPrompt(CanvasLayer layer, FontFile? face) =>
        _resultPrompt = new BitmapTextItem(layer.GetCanvas()) { ReplacementFont = face };

    /// <summary>Unity's runner's <c>DrawCampaignResultPrompt</c>: once the result is recorded, the flow's line
    /// (<see cref="CampaignResultFlow.Prompt"/>) in <see cref="SubtitleLayout.ResultPromptBox"/>, over the win screen.</summary>
    private void DrawResultPrompt()
    {
        if (_resultPrompt == null || _resultFlow == null || _simulation == null || _scriptedView == null) return;
        string? text = _resultFlow.ResultRecorded ? _resultFlow.Prompt(_simulation.World, _viewer, _scriptedView.PathPlaying) : null;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        DrawWinScreen(text != null, width, height);
        _resultPrompt.Show(text == null ? null : SubtitleFont(height), text, SubtitleLayout.ResultPromptBox(width, height), 1f);
    }

    private void CloseResultPrompt()
    {
        _resultPrompt?.Dispose();
        _resultPrompt = null;
    }

    /// <summary>The <c>--key-path</c> check (<see cref="KeyScript"/>): each tap is a press and a release of the named Godot
    /// key pushed into the viewport on its frame, so it reaches <see cref="_Input"/> as a real key does and the OS keyboard is
    /// never touched. Frames count from this scene's first, as <c>--pointer-path</c>'s do.</summary>
    private void OpenKeyScript(string[] args)
    {
        string? text = LaunchArguments.Value(args, "--key-path");
        _keyScript = KeyScript.Parse(text);
        if (text != null && _keyScript == null) GD.PushWarning("DK2.Godot: --key-path is malformed (KeyScript.Parse); no key is tapped.");
        if (_keyScript == null) return;
        foreach (KeyScriptKey tap in _keyScript.Keys)
            if (!Enum.TryParse(tap.Key, ignoreCase: false, out Key _))
                GD.PushWarning("DK2.Godot: --key-path names ", tap.Key, ", which is no Godot key; it is skipped.");
    }

    private void PlayKeyScript()
    {
        if (_keyScript == null) return;
        int frame = ++_keyFrame;
        IReadOnlyList<KeyScriptKey> keys = _keyScript.Keys;
        _nextKey = _keyScript.FirstAtOrAfter(frame, _nextKey);
        Viewport viewport = GetViewport();
        for (; _nextKey < keys.Count && keys[_nextKey].Frame == frame; _nextKey++)
        {
            if (!Enum.TryParse(keys[_nextKey].Key, ignoreCase: false, out Key key)) continue;
            viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
            viewport.PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
            GD.Print("DK2.Godot: scripted key ", key.ToString(), " on frame ", frame.ToString(CultureInfo.InvariantCulture), ", tick ",
                _simulation?.World.CurrentTick.Value ?? 0);
        }
    }
}
