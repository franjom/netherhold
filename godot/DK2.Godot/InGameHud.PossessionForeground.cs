// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

public partial class InGameHud
{
    private HudCanvas? _foregroundCanvas;
    private readonly HudFrame _foregroundFrame = new HudFrame();

    private void DrawPossessionForeground(long milliseconds)
    {
        if (_sources == null || _world == null || _possession == null)
        {
            return;
        }

        if (_foregroundCanvas == null)
        {
            _foregroundCanvas = new HudCanvas { Name = "PossessionForeground", Load = _sources.Texture, ZIndex = -1 };
            AddChild(_foregroundCanvas);
        }
        _foregroundFrame.Clear();
        Vector2 size = GetViewport().GetVisibleRect().Size;
        _world.BuildPossessionForeground(_possession.Foreground, (int)size.X, (int)size.Y, milliseconds, _foregroundFrame);
        _foregroundCanvas.Present(_foregroundFrame, Vector2.Zero, 1, 1);
    }
}
