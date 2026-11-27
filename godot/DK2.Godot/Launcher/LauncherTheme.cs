// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Godot;

namespace DK2.GodotAdapter;

/// <summary>The launcher's look, built in code: the project's own palette and fonts (no DK2 art or lettering, since the
/// install may be unknown), hewn-stone fields, a gold focus outline on every control, and radio, check and dropdown
/// marks drawn from distance fields at the window's scale.</summary>
public static class LauncherTheme
{
    public static readonly Color DeepEarth = new Color("1e1714"), HewnStone = new Color("3a322d"), Bone = new Color("e6dcc8"),
        KeeperGold = new Color("d4a537"), HeartRed = new Color("e0574f"), Lava = new Color("ff7a1a");

    public const string Title = "LauncherTitle", Intro = "LauncherIntro", Heading = "LauncherHeading",
        Number = "LauncherNumber", Status = "LauncherStatus", Hint = "LauncherHint", Stone = "LauncherStone",
        Field = "LauncherField", Quiet = "LauncherQuiet", PlayWaiting = "LauncherPlayWaiting", PlayLit = "LauncherPlayLit";

    private static readonly Color StoneEdge = new Color("554a43"), StoneHover = new Color("463d37"), StonePressed = new Color("2f2824"),
        StoneDim = new Color("2b2420");

    public static Theme Build(float scale)
    {
        FontFile regular = GD.Load<FontFile>("res://Launcher/AlegreyaSans-Regular.ttf");
        FontFile medium = GD.Load<FontFile>("res://Launcher/AlegreyaSans-Medium.ttf");
        FontFile blackletter = GD.Load<FontFile>("res://Launcher/UnifrakturMaguntia-Book.ttf");
        var theme = new Theme { DefaultFont = regular, DefaultFontSize = 18 };

        Variation(theme, Title, "Label");
        theme.SetFont("font", Title, blackletter);
        theme.SetFontSize("font_size", Title, 66);
        theme.SetColor("font_color", Title, Bone);
        theme.SetColor("font_color", "Label", Bone);
        Variation(theme, Intro, "Label");
        theme.SetFontSize("font_size", Intro, 19);
        theme.SetColor("font_color", Intro, Fade(Bone, .78f));
        Variation(theme, Heading, "Label");
        theme.SetFont("font", Heading, medium);
        theme.SetFontSize("font_size", Heading, 22);
        Variation(theme, Number, "Label");
        theme.SetFont("font", Number, medium);
        theme.SetFontSize("font_size", Number, 22);
        theme.SetColor("font_color", Number, KeeperGold);
        Variation(theme, Status, "Label");
        theme.SetFontSize("font_size", Status, 16);
        Variation(theme, Hint, "Label");
        theme.SetFontSize("font_size", Hint, 15);
        theme.SetColor("font_color", Hint, Fade(Bone, .58f));

        StyleBoxFlat focus = Box(Colors.Transparent, KeeperGold, 2, 4);
        focus.DrawCenter = false;
        focus.SetExpandMarginAll(3);

        Variation(theme, Stone, "PanelContainer");
        StyleBoxFlat stone = Box(HewnStone, StoneEdge, 1, 3);
        Margins(stone, 0, 0);
        theme.SetStylebox("panel", Stone, stone);

        Variation(theme, Field, "LineEdit");
        var fieldBox = new StyleBoxEmpty();
        Margins(fieldBox, 12, 8);
        theme.SetStylebox("normal", Field, fieldBox);
        theme.SetStylebox("read_only", Field, fieldBox);
        theme.SetStylebox("focus", Field, focus);
        theme.SetColor("font_color", Field, Bone);
        theme.SetColor("font_placeholder_color", Field, Fade(Bone, .45f));
        theme.SetColor("caret_color", Field, KeeperGold);
        theme.SetColor("selection_color", Field, Fade(KeeperGold, .35f));
        theme.SetColor("font_selected_color", Field, Bone);

        ButtonStyles(theme, "Button", Box(HewnStone, StoneEdge, 1, 3), Box(StoneHover, StoneEdge, 1, 3),
            Box(StonePressed, StoneEdge, 1, 3), Box(StoneDim, StoneDim, 1, 3), focus, Bone, Fade(Bone, .35f));
        theme.SetColor("font_color", "Button", Bone);
        Variation(theme, Quiet, "Button");
        var quiet = new StyleBoxEmpty();
        Margins(quiet, 18, 9);
        ButtonStyles(theme, Quiet, quiet, Box(StoneHover, StoneHover, 0, 3), Box(StonePressed, StonePressed, 0, 3), quiet, focus,
            Fade(Bone, .82f), Fade(Bone, .35f));
        Variation(theme, PlayWaiting, "Button");
        ButtonStyles(theme, PlayWaiting, Box(StoneDim, StoneEdge, 1, 3), Box(StoneHover, StoneEdge, 1, 3),
            Box(StonePressed, StoneEdge, 1, 3), Box(StoneDim, new Color("3a312c"), 1, 3), focus, Bone, Fade(Bone, .32f));
        theme.SetFont("font", PlayWaiting, medium);
        Variation(theme, PlayLit, "Button");
        StyleBoxFlat lit = Box(KeeperGold, new Color("f0c860"), 1, 3);
        lit.ShadowColor = Fade(Lava, .28f);
        lit.ShadowSize = 10;
        StyleBoxFlat litHover = Box(new Color("e0b444"), new Color("f6d77a"), 1, 3);
        litHover.ShadowColor = lit.ShadowColor;
        litHover.ShadowSize = 12;
        ButtonStyles(theme, PlayLit, lit, litHover, Box(new Color("b88d27"), new Color("f0c860"), 1, 3), lit, focus,
            DeepEarth, DeepEarth);
        theme.SetFont("font", PlayLit, medium);

        const string type = "CheckBox";
        var plain = new StyleBoxEmpty();
        Margins(plain, 4, 5);
        ButtonStyles(theme, type, plain, plain, plain, plain, focus, Bone, Fade(Bone, .35f));
        theme.SetConstant("h_separation", type, 10);
        theme.SetIcon("radio_unchecked", type, Mark(20, scale, Fade(Bone, .7f), RadioOff));
        theme.SetIcon("radio_checked", type, Mark(20, scale, KeeperGold, RadioOn));
        theme.SetIcon("radio_unchecked_disabled", type, Mark(20, scale, Fade(Bone, .3f), RadioOff));
        theme.SetIcon("radio_checked_disabled", type, Mark(20, scale, Fade(KeeperGold, .4f), RadioOn));
        theme.SetIcon("unchecked", type, Mark(20, scale, Fade(Bone, .7f), BoxOff));
        theme.SetIcon("checked", type, Mark(20, scale, KeeperGold, BoxOn));
        theme.SetIcon("unchecked_disabled", type, Mark(20, scale, Fade(Bone, .3f), BoxOff));
        theme.SetIcon("checked_disabled", type, Mark(20, scale, Fade(KeeperGold, .4f), BoxOn));

        ButtonStyles(theme, "OptionButton", Box(HewnStone, StoneEdge, 1, 3), Box(StoneHover, StoneEdge, 1, 3),
            Box(StonePressed, StoneEdge, 1, 3), Box(StoneDim, StoneDim, 1, 3), focus, Bone, Fade(Bone, .35f));
        theme.SetIcon("arrow", "OptionButton", Mark(14, scale, Fade(Bone, .8f), Chevron));
        theme.SetConstant("arrow_margin", "OptionButton", 10);
        theme.SetStylebox("panel", "PopupMenu", Box(HewnStone, StoneEdge, 1, 3));
        theme.SetStylebox("hover", "PopupMenu", Box(KeeperGold, KeeperGold, 0, 2));
        theme.SetColor("font_color", "PopupMenu", Bone);
        theme.SetColor("font_hover_color", "PopupMenu", DeepEarth);
        theme.SetIcon("radio_checked", "PopupMenu", Mark(14, scale, KeeperGold, Dot));
        theme.SetIcon("radio_unchecked", "PopupMenu", Mark(14, scale, Colors.Transparent, Dot));
        theme.SetConstant("v_separation", "PopupMenu", 8);
        return theme;
    }

    public static Color Fade(Color colour, float alpha)
    {
        return new Color(colour.R, colour.G, colour.B, alpha);
    }

    private static void Variation(Theme theme, string name, string baseType)
    {
        theme.SetTypeVariation(name, baseType);
    }

    private static void ButtonStyles(Theme theme, string type, StyleBox normal, StyleBox hover, StyleBox pressed,
        StyleBox disabled, StyleBox focus, Color text, Color disabledText)
    {
        theme.SetStylebox("normal", type, normal);
        theme.SetStylebox("hover", type, hover);
        theme.SetStylebox("pressed", type, pressed);
        theme.SetStylebox("hover_pressed", type, pressed);
        theme.SetStylebox("disabled", type, disabled);
        theme.SetStylebox("focus", type, focus);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color",
                     "font_hover_pressed_color" })
        {
            theme.SetColor(state, type, text);
        }

        theme.SetColor("font_disabled_color", type, disabledText);
    }

    private static StyleBoxFlat Box(Color fill, Color border, int borderWidth, int radius)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border, AntiAliasing = true };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        Margins(box, 18, 9);
        return box;
    }

    private static void Margins(StyleBox box, float horizontal, float vertical)
    {
        box.ContentMarginLeft = box.ContentMarginRight = horizontal;
        box.ContentMarginTop = box.ContentMarginBottom = vertical;
    }

    /// <summary>A mark <paramref name="size"/> units square, rasterized at <paramref name="scale"/> pixels a unit from a
    /// signed distance in units (coverage 1 inside, anti-aliased over one pixel) and shown at the unit size.</summary>
    private static ImageTexture Mark(int size, float scale, Color colour, Func<Vector2, float, (float Distance, float Cut)> shape)
    {
        int pixels = Mathf.CeilToInt(size * scale);
        var image = Image.CreateEmpty(pixels, pixels, false, Image.Format.Rgba8);
        for (int y = 0; y < pixels; y++)
        {
            for (int x = 0; x < pixels; x++)
            {
                var p = new Vector2((x + .5f) / scale, (y + .5f) / scale);
                (float d, float cut) = shape(p, size);
                float cover = Mathf.Clamp(.5f - d * scale, 0f, 1f) * (1f - Mathf.Clamp(.5f - cut * scale, 0f, 1f));
                image.SetPixel(x, y, new Color(colour.R, colour.G, colour.B, colour.A * cover));
            }
        }

        var texture = ImageTexture.CreateFromImage(image);
        texture.SetSizeOverride(new Vector2I(size, size));
        return texture;
    }

    private static (float, float) RadioOff(Vector2 p, float size)
    {
        return (Mathf.Abs(p.DistanceTo(Centre(size)) - 7.5f) - .8f, 1e9f);
    }

    private static (float, float) RadioOn(Vector2 p, float size)
    {
        float r = p.DistanceTo(Centre(size));
        return (Mathf.Min(Mathf.Abs(r - 7.5f) - .8f, r - 4f), 1e9f);
    }

    private static (float, float) Dot(Vector2 p, float size)
    {
        return (p.DistanceTo(Centre(size)) - 3.5f, 1e9f);
    }

    private static (float, float) BoxOff(Vector2 p, float size)
    {
        return (Mathf.Abs(RoundedBox(p, size, 7.5f, 2.5f)) - .8f, 1e9f);
    }

    /// <summary>A filled box with the tick cut out of it.</summary>
    private static (float, float) BoxOn(Vector2 p, float size)
    {
        float tick = Mathf.Min(Segment(p, new Vector2(5.5f, 10.3f), new Vector2(8.7f, 13.5f)),
            Segment(p, new Vector2(8.7f, 13.5f), new Vector2(14.6f, 6.8f))) - 1.1f;
        return (RoundedBox(p, size, 8f, 2.5f), tick);
    }

    private static (float, float) Chevron(Vector2 p, float size)
    {
        return (Mathf.Min(Segment(p, new Vector2(3.5f, 5.5f), new Vector2(7f, 9f)), Segment(p, new Vector2(7f, 9f), new Vector2(10.5f, 5.5f))) - .8f,
            1e9f);
    }

    private static Vector2 Centre(float size)
    {
        return new Vector2(size * .5f, size * .5f);
    }

    private static float RoundedBox(Vector2 p, float size, float half, float radius)
    {
        Vector2 q = (p - Centre(size)).Abs() - new Vector2(half - radius, half - radius);
        return new Vector2(Mathf.Max(q.X, 0f), Mathf.Max(q.Y, 0f)).Length() + Mathf.Min(Mathf.Max(q.X, q.Y), 0f) - radius;
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }
}
