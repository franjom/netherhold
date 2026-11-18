// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using System.Globalization;
using DK2.Core;

namespace DK2.Presentation;

public sealed record PossessionHudIcon(string Image, int RechargePercent, bool Selected, bool Spell = false);
public sealed record PossessionHudSnapshot(string Portrait, int HealthPercent, GoldAmount Gold,
    IReadOnlyList<PossessionHudIcon> Actions, IReadOnlyList<string> Followers, string? Overlay = null)
{
    public IReadOnlyList<string> Overlays { get; init; } = Array.Empty<string>();
}

/// <summary>The possession panel's draw list (possession-hud.md). Ordering, art and recharge come
/// from 0x00420D10; the top strip's adapted spacing is a documented remake layout choice.</summary>
public sealed class PossessionHud
{
    public const string HealthFill = "GUI\\Map\\m-hp";
    public static HudRect HealthBounds => new(656, 16, 292, 112);

    public static HudVisibility OrdinaryVisibility(HudVisibility ordinary, bool possessionCameraHeld) =>
        possessionCameraHeld ? new(false, false) : ordinary;

    /// <summary>The existing mana/Heart corner is also present in first person. The keeper's
    /// gold/payday corner is replaced by the creature panel.</summary>
    public static void AppendResourceCorner(HudFrame resourceFrame, HudFrame frame)
    {
        foreach (HudDraw draw in resourceFrame.Draws)
            if (draw.Rect.X + draw.Rect.Width <= 520) frame.Add(draw);
    }

    public static IEnumerable<HudImage> Images()
    {
        foreach (string path in new[] { HealthFill, "GUI\\Map\\m-hp_bg", "GUI\\Map\\m-h-tone-bg", "GUI\\Map\\m-h-bg",
            "GUI\\icons\\selected-creature", "GUI\\icons\\selected-spell", "GUI\\icons\\selected_group",
            "GUI\\Windows\\Bar-00", "GUI\\Filters\\F-Sniper", "GUI\\Filters\\F-Frozen",
            "GUI\\Filters\\F-Knight", "GUI\\Filters\\F-Black_Knight", "GUI\\Filters\\F-Guard", "GUI\\Filters\\F-FireFly" }) yield return path;
        for (int i = 0; i < 10; i++) yield return KeeperResourceHud.LargeDigits + i.ToString(CultureInfo.InvariantCulture);
    }

    public void Build(HudScreen screen, PossessionHudSnapshot? snapshot, HudFrame frame)
        => Build(screen, 1920, snapshot, frame);
    public void Build(HudScaleLayout layout, PossessionHudSnapshot? snapshot, HudFrame frame)
        => Build(layout.Screen, 1920 / layout.Factor, snapshot, frame);
    public void BuildFilters(HudScaleLayout layout, PossessionHudSnapshot? snapshot, HudFrame frame)
        => Filters(layout.Screen, 1920 / layout.Factor, snapshot, frame);
    public void BuildCreaturePanel(HudScaleLayout layout, PossessionHudSnapshot? snapshot, HudFrame frame)
        => CreaturePanel(layout.Screen, snapshot, frame);
    public void BuildChrome(HudScaleLayout layout, PossessionHudSnapshot? snapshot, HudFrame frame)
        => Chrome(layout.Screen, 1920 / layout.Factor, snapshot, frame);
    private void Build(HudScreen screen, float visibleHeight, PossessionHudSnapshot? snapshot, HudFrame frame)
    {
        CreaturePanel(screen, snapshot, frame);
        Filters(screen, visibleHeight, snapshot, frame);
        Chrome(screen, visibleHeight, snapshot, frame);
    }
    private static void Filters(HudScreen screen, float visibleHeight, PossessionHudSnapshot? snapshot, HudFrame frame)
    {
        if (snapshot == null || screen.Width <= 0 || screen.Height <= 0) return;
        foreach (string overlay in snapshot.Overlays) Quad(frame, overlay, new(0, 0, screen.VirtualWidth, visibleHeight));
        if (!string.IsNullOrWhiteSpace(snapshot.Overlay)) Quad(frame, snapshot.Overlay!, new(0, 0, screen.VirtualWidth, visibleHeight));
    }
    private static void CreaturePanel(HudScreen screen, PossessionHudSnapshot? snapshot, HudFrame frame)
    {
        if (snapshot == null || screen.Width <= 0 || screen.Height <= 0) return;
        Quad(frame, "GUI\\Windows\\Bar-00", new(0, 0, screen.VirtualWidth, 144));
        Quad(frame, snapshot.Portrait, new(520, 8, 128, 128));
        Quad(frame, "GUI\\Map\\m-hp_bg", new(656, 8, 292, 128));
        Quad(frame, "GUI\\Map\\m-h-tone-bg", new(656, 8, 292, 128));
        float health = Math.Clamp(snapshot.HealthPercent, 0, 100) / 100f;
        if (health > 0)
            frame.Add(new(HudDrawKind.Quad, HealthFill, HealthBounds with { Width = HealthBounds.Width * health },
                HudColor.White, Uv: new HudRect(0, 0, health, 1)));
        Quad(frame, "GUI\\Map\\m-h-bg", new(956, 8, 444, 128));
        Digits(frame, snapshot.Gold.Value, new(1084, 48, 308, 80));
        for (int i = 0; i < snapshot.Actions.Count; i++)
        {
            PossessionHudIcon icon = snapshot.Actions[i];
            HudRect rect = new(1408 + i * 136, 8, 128, 128);
            if (rect.X + rect.Width > screen.VirtualWidth) break;
            if (icon.Selected) Quad(frame, icon.Image.EndsWith("group-mode", StringComparison.OrdinalIgnoreCase)
                ? "GUI\\icons\\selected_group" : icon.Spell ? "GUI\\icons\\selected-spell" : "GUI\\icons\\selected-creature", rect);
            if (string.IsNullOrWhiteSpace(icon.Image)) continue;
            int ready = Math.Clamp(icon.RechargePercent, 0, 100);
            Quad(frame, icon.Image, rect, ready == 100 ? HudColor.White : HudColor.Grey(102));
            if (ready is > 0 and < 100) frame.Add(new(HudDrawKind.Sector, icon.Image, rect, HudColor.White, Percent: ready));
        }
    }
    private static void Chrome(HudScreen screen, float visibleHeight, PossessionHudSnapshot? snapshot, HudFrame frame)
    {
        if (snapshot == null || screen.Width <= 0 || screen.Height <= 0) return;
        int column = 0;
        foreach (string follower in snapshot.Followers)
        {
            HudRect rect = new(20 + column * 136, visibleHeight - 460, 128, 264);
            if (rect.X + rect.Width > screen.VirtualWidth) break;
            Quad(frame, follower, rect);
            column++;
        }
        Quad(frame, "GUI\\Windows\\Bar-00", new(0, visibleHeight - 128, screen.VirtualWidth, 88));
    }

    private static void Quad(HudFrame frame, HudImage image, HudRect rect, HudColor? tint = null)
    {
        if (!image.IsNone) frame.Add(new(HudDrawKind.Quad, image, rect, tint ?? HudColor.White));
    }

    private static void Digits(HudFrame frame, int value, HudRect area)
    {
        string text = Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
        const int advance = 40, size = 48;
        int start = Math.Max(0, 7 - text.Length);
        for (int i = 0; i < text.Length; i++)
            Quad(frame, KeeperResourceHud.LargeDigits + text[i], new(area.X + (start + i) * advance, area.Y, size, size));
    }
}
