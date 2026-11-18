// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
namespace DK2.GodotAdapter;

public partial class InGameHud
{
    private readonly PossessionHighlight _possessionHighlight = new();
    private void DrawPossessionHighlight()
    {
        if (_simulation == null || _world == null || _possession == null) return;
        if (_possessionHighlight.Capture(_simulation.World, _simulation.Database, _viewer, _possession) is { } target)
            _world.DrawHoverOverlay(target);
    }
}
