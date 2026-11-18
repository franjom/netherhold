// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class PossessionHudIndicatorsTests
{
    private sealed class Art : IBottomPanelArt
    {
        public string? Icon(BottomPanelChoiceKey key) => "room";
        public string Text(uint id) => "";
    }
    private static readonly BitmapFont Font = new(10, [new BitmapGlyph('A', 2, 2, 0, 0, 3, [15, 15, 15, 15])]);

    [Fact]
    public void AllEnabledIndicatorsStackUpwardWithNativeArtAndValues()
    {
        var frame = new HudFrame();
        var hud = new PossessionHudIndicators(new Art(), _ => Font, player => "AA");
        var shown = new HudIndicatorSnapshot(true, 10, true, 123, true, 7, 3, true, 4, 2);
        hud.Build(new HudScaleLayout(1920, 1080, 100), shown, frame);
        HudDraw[] draws = frame.Draws.ToArray();
        Assert.Contains(draws, d => d.Image.Path == "GUI\\Windows\\timer-bg" && d.Rect.Y == 1332);
        Assert.Contains(draws, d => d.Image.Path == "GUI\\Windows\\score-bg" && d.Rect.Y == 1204);
        Assert.Contains(draws, d => d.Image.Path == "GUI\\Windows\\kingofhill-bg" && d.Rect.Y == 1076);
        Assert.Contains(draws, d => d.Image.Path == "GUI\\Windows\\kills-bg" && d.Rect.Y == 948);
        Assert.Contains(draws, d => d.Image.Path == "GUI\\Windows\\slaps-bg" && d.Rect.Y == 820);
        Assert.Contains(draws, d => d.Image.Path == "GUI\\Icons\\next_room" && d.Rect.Y == 692);
        Assert.Contains(draws, d => d.Image.Path == "room");
        Assert.Contains(draws, d => d.Image.Path == "GUI\\Icons\\i-7");
        Assert.Contains(draws, d => d.Image.Text != null);
        Assert.Equal(20 * 1080f / 1920, draws[0].Rect.X * new HudScreen(1920, 1080).Scale, 3);
    }

    [Fact]
    public void NoIndicatorsDrawsNothingAndScaledIndicatorsStayVisible()
    {
        var hud = new PossessionHudIndicators(new Art(), _ => Font, player => "AA");
        var frame = new HudFrame();
        var layout = new HudScaleLayout(1920, 1080, 150);
        hud.Build(layout, default, frame);
        Assert.Empty(frame.Draws.ToArray());
        hud.Build(layout, new HudIndicatorSnapshot(true, 10, true, 123, true, 7, 3, true, 4, 2), frame);
        Assert.All(frame.Draws.ToArray(), d => Assert.InRange(d.Rect.Y + d.Rect.Height, 0, 1920 / layout.Factor));
    }
}
