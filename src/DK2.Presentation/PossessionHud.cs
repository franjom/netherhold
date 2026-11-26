// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;

namespace DK2.Presentation;

public enum PossessionHudIconRole { TopAction, PrimaryAbility, SecondaryAbility, Group }

public sealed record PossessionHudIcon(string Image, int RechargePercent, bool Selected, bool Spell = false)
{
    public PossessionHudIconRole Role { get; init; }
}

public sealed record PossessionHudSnapshot(string Portrait, int HealthPercent, GoldAmount Gold,
    IReadOnlyList<PossessionHudIcon> Actions, IReadOnlyList<string> Followers, string? Overlay = null)
{
    public IReadOnlyList<string> Overlays { get; init; } = Array.Empty<string>();
    public string CarriedGoldIcon { get; init; } = PossessionHud.GoldIcon;
}

/// <summary>The possession panel's draw list and native coordinate arithmetic (possession-hud.md).</summary>
public sealed class PossessionHud
{
    public const string HealthFill = "GUI\\Map\\m-hp";
    public const string GoldIcon = "gui\\rooms\\Treasury";
    public static HudRect HealthBounds => new(664, 22, 280, 100);

    private readonly record struct Metrics(HudScreen Screen, float XScale, float VisibleHeight)
    {
        public float X(float value) => value * XScale;
        public float BottomShift => VisibleHeight - 1920;
        public bool Valid => Screen.Width > 0 && Screen.Height > 0;
    }

    public static float NativeXScale(HudScaleLayout layout) =>
        layout.Height <= 0 ? 1 : layout.Width * 1920f / (layout.Height * 2560f);

    /// <summary>ADR-0821: preserve the native 4:3 content proportions while extra viewport width remains available to the world.</summary>
    public static float ContentXScale(HudScaleLayout layout) => Math.Min(1, NativeXScale(layout));

    public static HudVisibility OrdinaryVisibility(HudVisibility ordinary, bool possessionCameraHeld) =>
        possessionCameraHeld ? new(false, false) : ordinary;

    public static HudVisibility OrdinaryVisibility(HudVisibility ordinary, bool possessionCameraHeld,
        bool possessing, bool firstPersonCameraSettled) =>
        !possessionCameraHeld ? ordinary : new(ordinary.ShowNormalHud && possessing && !firstPersonCameraSettled, false);

    public static HudVisibility OrdinaryVisibility(HudVisibility ordinary, PossessionSession? session) =>
        session is not { CameraHeld: true } ? ordinary : new(ordinary.ShowNormalHud && session.OrdinaryHudDuringTransition, false);

    public static GameShortcutContext ShortcutContext(GameShortcutContext ordinary, bool possessionCameraHeld, bool possessing = true) =>
        ordinary with { Possession = possessionCameraHeld, OrdinaryInput = ordinary.OrdinaryInput && (!possessionCameraHeld || possessing) };

    public static GameShortcutContext ShortcutContext(GameShortcutContext ordinary, bool possessionCameraHeld,
        bool possessing, bool firstPersonCameraSettled) =>
        ShortcutContext(ordinary, possessionCameraHeld, possessing) with
        { OrdinaryInput = ordinary.OrdinaryInput && (!possessionCameraHeld || (possessing && firstPersonCameraSettled)) };

    public static HudVisibility MinimapVisibility(HudVisibility ordinary, bool possessing, bool possessionCameraHeld = false) =>
        possessing ? new(ordinary.ShowNormalHud, false) : OrdinaryVisibility(ordinary, possessionCameraHeld);

    public static HudVisibility MinimapVisibility(HudVisibility ordinary, PossessionSession? session) =>
        session is { Possessing: true } ? new(ordinary.ShowNormalHud, false) : OrdinaryVisibility(ordinary, session);

    public static bool ShowPanel(bool possessing, HudVisibility ordinary) => possessing && ordinary.ShowNormalHud;

    public static bool ShowPanel(bool possessing, HudVisibility ordinary, bool firstPersonCameraSettled) =>
        firstPersonCameraSettled && ShowPanel(possessing, ordinary);

    /// <summary>The mana/Heart corner remains; creature gold replaces the keeper's gold/payday corner.</summary>
    public static void AppendResourceCorner(HudFrame resourceFrame, HudFrame frame) =>
        AppendResourceCorner(resourceFrame, frame, 1);

    public static void AppendResourceCorner(HudScaleLayout layout, HudFrame resourceFrame, HudFrame frame) =>
        AppendResourceCorner(resourceFrame, frame, ContentXScale(layout));

    private static void AppendResourceCorner(HudFrame resourceFrame, HudFrame frame, float xScale)
    {
        foreach (HudDraw draw in resourceFrame.Draws)
        {
            if (draw.Rect.X + draw.Rect.Width <= 520)
            {
                frame.Add(draw with { Rect = draw.Rect with { X = draw.Rect.X * xScale, Width = draw.Rect.Width * xScale } });
            }
        }
    }

    public static IEnumerable<HudImage> Images()
    {
        foreach (string path in new[] { HealthFill, GoldIcon, "GUI\\Map\\m-hp_bg", "GUI\\Map\\m-h-tone-bg", "GUI\\Map\\m-h-bg",
            "GUI\\icons\\selected-creature", "GUI\\icons\\selected-spell", "GUI\\icons\\selected_group",
            "GUI\\Windows\\Bar-00", "GUI\\Filters\\F-Sniper", "GUI\\Filters\\F-Frozen",
            "GUI\\Filters\\F-Knight", "GUI\\Filters\\F-Black_Knight", "GUI\\Filters\\F-Guard", "GUI\\Filters\\F-FireFly" })
        {
            yield return path;
        }

        for (int i = 0; i < 10; i++)
        {
            yield return KeeperResourceHud.LargeDigits + i.ToString(CultureInfo.InvariantCulture);
        }
    }

    public void Build(HudScreen screen, PossessionHudSnapshot? snapshot, HudFrame frame) =>
        Build(new Metrics(screen, Math.Min(1, screen.VirtualWidth / 2560), 1920), snapshot, frame);

    public void Build(HudScaleLayout layout, PossessionHudSnapshot? snapshot, HudFrame frame) =>
        Build(Of(layout), snapshot, frame);

    public void BuildFilters(HudScaleLayout layout, PossessionHudSnapshot? snapshot, HudFrame frame) =>
        Filters(Of(layout), snapshot, frame);

    public void BuildCreaturePanel(HudScaleLayout layout, PossessionHudSnapshot? snapshot, HudFrame frame) =>
        CreaturePanel(Of(layout), snapshot, frame);

    public void BuildChrome(HudScaleLayout layout, PossessionHudSnapshot? snapshot, HudFrame frame) =>
        Chrome(Of(layout), snapshot, frame);

    private static Metrics Of(HudScaleLayout layout) => new(layout.Screen, ContentXScale(layout), 1920 / layout.Factor);

    private static void Build(Metrics metrics, PossessionHudSnapshot? snapshot, HudFrame frame)
    {
        Filters(metrics, snapshot, frame);
        CreaturePanel(metrics, snapshot, frame);
        Chrome(metrics, snapshot, frame);
    }

    private static void Filters(Metrics m, PossessionHudSnapshot? snapshot, HudFrame frame)
    {
        if (snapshot == null || !m.Valid)
        {
            return;
        }

        foreach (string overlay in snapshot.Overlays)
        {
            Quad(frame, overlay, new(0, 0, m.Screen.VirtualWidth, m.VisibleHeight));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Overlay))
        {
            Quad(frame, snapshot.Overlay!, new(0, 0, m.Screen.VirtualWidth, m.VisibleHeight));
        }

        Quad(frame, "GUI\\Windows\\Bar-00", new(0, m.VisibleHeight - 128, m.Screen.VirtualWidth, 88));
    }

    private static void CreaturePanel(Metrics m, PossessionHudSnapshot? snapshot, HudFrame frame)
    {
        if (snapshot == null || !m.Valid)
        {
            return;
        }

        HudQuads.Tiled(frame, "GUI\\Windows\\Bar-00", new(0, 20, m.Screen.VirtualWidth, 88), m.X(512), 88, HudColor.White);
        float square = m.X(128), delta = 140 - square;
        Quad(frame, snapshot.Portrait, new(m.X(520), 8, square, square));
        var healthArea = new HudRect(m.X(656), 8, m.X(292), square);
        Quad(frame, "GUI\\Map\\m-hp_bg", healthArea);
        float health = Math.Clamp(snapshot.HealthPercent, 0, 100) / 100f;
        if (health > 0)
        {
            var fill = new HudRect(m.X(664), 8 + delta / 2 + m.X(8),
                (m.X(292) - delta) * health, square - delta - 2 * m.X(8));
            frame.Add(new(HudDrawKind.Quad, HealthFill, fill, HudColor.White, Uv: new HudRect(0, 0, health, 1)));
        }
        Quad(frame, "GUI\\Map\\m-h-tone-bg", healthArea);
        Quad(frame, snapshot.CarriedGoldIcon, new(m.X(956), 8, square, square));
        Quad(frame, "GUI\\Map\\m-h-bg", new(m.X(1084), 8, 496 - square, square));
        Digits(frame, snapshot.Gold.Value, m, 956, 8 + square / 3);
        int column = 0;
        foreach (PossessionHudIcon icon in snapshot.Actions)
        {
            if (Role(icon) != PossessionHudIconRole.TopAction)
            {
                continue;
            }

            Icon(frame, icon, new(m.X(956) + 496 - 40 + m.X(column++ * 136), 8, square, square));
        }
    }

    private static void Chrome(Metrics m, PossessionHudSnapshot? snapshot, HudFrame frame)
    {
        if (snapshot == null || !m.Valid)
        {
            return;
        }

        foreach (PossessionHudIcon icon in snapshot.Actions)
        {
            PossessionHudIconRole role = Role(icon);
            if (role == PossessionHudIconRole.TopAction)
            {
                continue;
            }

            int row = role == PossessionHudIconRole.PrimaryAbility ? 0 : role == PossessionHudIconRole.SecondaryAbility ? 1 : 2;
            var rect = new HudRect(m.X(620) - 140, 1460 + row * m.X(128) + m.BottomShift,
                m.X(128), m.X(role == PossessionHudIconRole.Group ? 184 : 128));
            Icon(frame, icon, rect);
        }
        int column = 0;
        foreach (string follower in snapshot.Followers)
        {
            Quad(frame, follower, new(m.X(628 + column++ * 136), m.X(1624) + 12 + m.BottomShift, m.X(128), 264));
        }
    }

    private static PossessionHudIconRole Role(PossessionHudIcon icon) =>
        icon.Role == PossessionHudIconRole.TopAction && icon.Image.EndsWith("group-mode", StringComparison.OrdinalIgnoreCase)
            ? PossessionHudIconRole.Group : icon.Role;

    private static void Icon(HudFrame frame, PossessionHudIcon icon, HudRect rect)
    {
        if (!string.IsNullOrWhiteSpace(icon.Image))
        {
            int ready = Math.Clamp(icon.RechargePercent, 0, 100);
            Quad(frame, icon.Image, rect, ready == 100 ? HudColor.White : HudColor.Grey(102));
            if (ready is > 0 and < 100)
            {
                frame.Add(new(HudDrawKind.Sector, icon.Image, rect, HudColor.White, Percent: ready));
            }
        }
        if (icon.Selected)
        {
            Quad(frame, Role(icon) == PossessionHudIconRole.Group ? "GUI\\icons\\selected_group"
                : icon.Spell ? "GUI\\icons\\selected-spell" : "GUI\\icons\\selected-creature", rect);
        }
    }

    private static void Quad(HudFrame frame, HudImage image, HudRect rect, HudColor? tint = null)
    {
        if (!image.IsNone)
        {
            frame.Add(new(HudDrawKind.Quad, image, rect, tint ?? HudColor.White));
        }
    }

    private static void Digits(HudFrame frame, int value, Metrics m, int nativeX, float y)
    {
        string text = Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
        int start = Math.Max(0, 10 - text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            Quad(frame, KeeperResourceHud.LargeDigits + text[i], new(m.X(nativeX + (start + i) * 40), y, m.X(48), m.X(48)));
        }
    }
}
