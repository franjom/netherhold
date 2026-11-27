// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

/// <summary>The tool icon, the No Go icon over it and the price beside the hand, or in Hand mode the held-item icons with
/// I-Dismiss or No Go over them (0x0040CAE0, 0x0040CFE0, docs/research/placement-cursor.md section 3), as a
/// <see cref="HudFrame"/> in screen pixels over 1920-high virtual units. DK2 draws held icon 0,
/// I-Dismiss inside its draw (0x0040D58B..0x0040D5AF), then held icons
/// 1..7, the overflow icon (0x0040D1F1..0x0040D27C), No Go over them (0x0040CF1E); then the price digits.</summary>
[DK2.Core.Dk2Behavior(DK2.Core.Confidence.StronglyInferred, "docs/research/placement-cursor.md")]
public sealed class PlacementCursorHud
{
    /// <summary>How many of the price's last decimal digits are drawn.</summary>
    public const int MaxDigits = 7;
    public const string NoGoIconName = @"GUI\Cursors\I-No_Go";
    private static readonly string[] WideDigits = Digits(@"GUI\Icons\i-"), NarrowDigits = Digits(@"GUI\Icons\lo-");
    private readonly int[] _digits = new int[MaxDigits];

    /// <summary>A keeper spell's GUI icon resource by spell id: the record's first art resource, which 0x0040D310 installs
    /// from the runtime spell (KeeperSpells file +0x20, the bottom panel's icon). Null or an unknown id draws no spell icon.</summary>
    public Func<byte, string?>? SpellIconName { get; set; }

    /// <summary>A creature type's held-item icon: Creatures.kwd Icon1Resource (file +0xC44), the compact definition's art
    /// +0x510 that 0x0040D450 installs. Null or empty draws none.</summary>
    public Func<byte, string?>? CreatureIconName { get; set; }

    /// <summary>An object type's held-item icon: Objects.kwd GuiIconResource (file +0x74), the compact definition's first
    /// art that 0x0040D450 installs. Null or empty draws none.</summary>
    public Func<byte, string?>? ObjectIconName { get; set; }

    /// <summary>Every fixed image the cursor can draw, for a renderer to decode ahead of the first frame (ADR-0688).</summary>
    public static IEnumerable<HudImage> Images()
    {
        yield return NoGoIconName;
        yield return HeldItemStack.MoreIconName;
        yield return HeldItemStack.DisposeIconName;
        foreach (PlacementIcon icon in Enum.GetValues(typeof(PlacementIcon)))
        {
            if (IconName(icon) is string name)
            {
                yield return name;
            }
        }

        foreach (string digit in WideDigits)
        {
            yield return digit;
        }

        foreach (string digit in NarrowDigits)
        {
            yield return digit;
        }
    }

    /// <summary>The tool's cursor icon, or null for <see cref="PlacementIcon.Spell"/> (its own art), <see cref="PlacementIcon.Held"/>
    /// and <see cref="PlacementIcon.None"/>.</summary>
    public static string? IconName(PlacementIcon icon)
    {
        return icon switch
        {
            PlacementIcon.PlaceRoom => @"GUI\Cursors\I-Place_Room",
            PlacementIcon.Sell => @"GUI\Cursors\I-Sell",
            PlacementIcon.PlaceDoor => @"GUI\Cursors\I-Place_Door",
            PlacementIcon.PlaceTrap => @"GUI\Cursors\I-Place_Trap",
            PlacementIcon.Query => @"GUI\Cursors\I-Query",
            PlacementIcon.Kill => @"GUI\Cursors\I-Kill",
            PlacementIcon.Transfer => @"GUI\Cursors\I-Transfer",
            _ => null,
        };
    }

    /// <summary>The price's digit colour: light red for an affordable cost, red for one that is not, and cyan for a refund
    /// (HYPOTHESIS: 0xFFFFFF80 read assuming the costs' byte order, placement-cursor.md section 3).</summary>
    public static HudColor PriceColour(int price, bool affordable)
    {
        return price > 0 ? new HudColor(128, 255, 255) : affordable ? new HudColor(255, 128, 128) : new HudColor(255, 0, 0);
    }

    /// <summary>Writes the cursor's draws for one frame into <paramref name="frame"/> (not cleared here).</summary>
    /// <param name="pointer">The pointer in screen pixels from the top left.</param>
    /// <param name="overHud">Moves the icon left by 40 reference units over the GUI (<see cref="HandCursor.OverlayAnchor"/>).</param>
    /// <param name="held">The hand's icons, read when <paramref name="feedback"/> shows <see cref="PlacementIcon.Held"/>.</param>
    public void Build(HudFrame frame, in PlacementFeedback feedback, Vector2 pointer, int screenWidth, int screenHeight,
        bool overHud, HeldItemStack? held = null, HandCursorKind handKind = HandCursorKind.Point)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        var screen = new HudScreen(screenWidth, screenHeight);
        float width = screenWidth, height = screenHeight;
        Vector2 anchor = HandCursor.OverlayAnchor(handKind, pointer, screenHeight, overHud);
        float x = anchor.X, y = anchor.Y;
        // The draw passes 0x20 x 0x20 and the +30/+10 price offset in pixels whose screen scale is unread; the 1/1200 scale
        // and both offsets are taken at the owner's 1200-line photos (2026-10-23), HYPOTHESIS.
        float scale = height / 1200f, size = 32 * scale;
        if (feedback.Icon == PlacementIcon.Held && held != null && held.Icons.Count > 0)
        {
            BuildHeld(frame, screen, held, feedback.Dispose, x, y, size, scale);
            // 0x0040CFE0 leaves the No Go anchor at icon 0 moved by (-2, +2); bit 0x04 hides it.
            if (feedback.NoGo && !feedback.Dispose)
            {
                Quad(frame, screen, NoGoIconName, x - 2 * scale, y + 2 * scale, size, HudColor.White);
            }

            return;
        }
        string? iconName = feedback.Icon == PlacementIcon.Spell ? SpellIcon(feedback.SpellId) : IconName(feedback.Icon);
        if (iconName == null)
        {
            return;
        }

        Quad(frame, screen, iconName, x, y, size, HudColor.White);
        // 0x0040D310 draws the spell's art at the same anchor and size, then moves the No Go anchor by (-2, +2) unscaled
        // pixels, scaled here as the price is (HYPOTHESIS); it draws no price.
        float shift = feedback.Icon == PlacementIcon.Spell ? 2 * scale : 0;
        if (feedback.NoGo)
        {
            Quad(frame, screen, NoGoIconName, x - shift, y + shift, size, HudColor.White);
        }

        if (feedback.Price == 0 || feedback.Icon == PlacementIcon.Spell)
        {
            return;
        }
        // 0x00415650 mode 0 (48 square, 40 advance, i- digits from 640 wide), on screen as our gold counter scales it
        // (H/1920): fitted, not read.
        string[] family = width >= 640 ? WideDigits : NarrowDigits;
        int count = PriceDigits(Math.Abs((long)feedback.Price));
        HudColor colour = PriceColour(feedback.Price, feedback.Affordable);
        float glyph = 48 * height / 1920f, advance = 40 * height / 1920f;
        for (int i = 0; i < count; i++)
        {
            frame.Add(new HudDraw(HudDrawKind.Quad, family[_digits[i]],
                Rect(screen, x + 30 * scale + i * advance, y + 10 * scale, glyph), colour));
        }
    }

    private string? SpellIcon(byte spellId)
    {
        string? name = SpellIconName?.Invoke(spellId);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>0x0040CFE0: icon i at the anchor plus <see cref="HeldItemStack.Offset"/>(i) (unscaled pixels, the 0x006CE910
    /// table), each 0x20 x 0x20; the overflow icon past eight; and 0x0040D450's I-Dismiss over icon 0, 2 pixels left, when it is
    /// a creature and bit 0x04 is set.</summary>
    private void BuildHeld(HudFrame frame, HudScreen screen, HeldItemStack held, bool dispose, float x, float y, float size, float scale)
    {
        bool bin = dispose && held.Icons[0].Kind == HeldIconKind.Creature;
        for (int i = 0; i < held.Icons.Count; i++)
        {
            HeldIcon icon = held.Icons[i];
            string? name = icon.Kind == HeldIconKind.Creature ? CreatureIconName?.Invoke(icon.TypeId) : ObjectIconName?.Invoke(icon.TypeId);
            if (!string.IsNullOrEmpty(name))
            {
                (int ox, int oy) = HeldItemStack.Offset(i);
                Quad(frame, screen, name, x + ox * scale, y + oy * scale, size, HudColor.White);
            }
            if (i == 0 && bin)
            {
                Quad(frame, screen, HeldItemStack.DisposeIconName, x - 2 * scale, y, size, HudColor.White);
            }
        }
        if (!held.More)
        {
            return;
        }

        (int mx, int my) = HeldItemStack.Offset(HeldItemStack.MaxIcons);
        Quad(frame, screen, HeldItemStack.MoreIconName, x + mx * scale, y + my * scale, size, HudColor.White);
    }

    /// <summary>The price's last <see cref="MaxDigits"/> decimal digits into <see cref="_digits"/>, most significant first, as
    /// the formatted number's tail was; returns how many.</summary>
    private int PriceDigits(long magnitude)
    {
        int count = 0;
        for (long rest = magnitude; count == 0 || rest > 0; rest /= 10)
        {
            count++;
        }

        count = Math.Min(count, MaxDigits);
        long value = magnitude;
        for (int i = count - 1; i >= 0; i--, value /= 10)
        {
            _digits[i] = (int)(value % 10);
        }

        return count;
    }

    private static void Quad(HudFrame frame, HudScreen screen, string image, float x, float y, float size, HudColor tint)
    {
        frame.Add(new HudDraw(HudDrawKind.Quad, image, Rect(screen, x, y, size), tint));
    }

    private static HudRect Rect(HudScreen screen, float x, float y, float size)
    {
        return new(screen.Virtual(x), screen.Virtual(y), screen.Virtual(size), screen.Virtual(size));
    }

    private static string[] Digits(string family)
    {
        string[] names = new string[10];
        for (int i = 0; i < 10; i++)
        {
            names[i] = family + (char)('0' + i);
        }

        return names;
    }
}
