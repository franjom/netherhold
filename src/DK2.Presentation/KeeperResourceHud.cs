// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Numerics;

namespace DK2.Presentation;

/// <summary>The keeper's resource strip at the top left, engine-free (ADR-0805): the mana and gold panels,
/// the Heart and payday gauges and the counters. DK2 lays it out in whole screen pixels, so it builds from
/// a <see cref="HudScreen"/> and floors as the adapter used to. Elements are a fixed table, back to front.</summary>
public sealed class KeeperResourceHud
{
    // The glyph families: one image per digit, icon or sign after the prefix.
    public const string LargeDigits = "GUI\\Icons\\i-", SmallDigits = "GUI\\Icons\\lo-";
    public const string RateDigits = "GUI\\Icons\\Mana\\s-", MinusSign = "GUI\\Icons\\i--";
    private const int Glyphs = 13;

    private struct Element
    {
        public bool Show;
        public HudImage Image;
        public HudRect Rect;
        public HudColor Tint;
    }

    private readonly Element[] _elements = new Element[4 + 5 + 4 * Glyphs];
    private readonly int[] _backgrounds = new int[4];
    private readonly int _heartFill, _heart, _heartAlarm, _paydayFill, _payday;
    private readonly NumericRow _gold, _mana, _income, _upkeep;
    private readonly KeeperResourceAnimation _animation = new();
    private int _count;
    private bool _visible = true, _allowInput = true;

    public KeeperResourceHud()
    {
        for (int i = 0; i < _backgrounds.Length; i++)
        {
            _backgrounds[i] = Add("GUI\\Windows\\Mana-Bg-0" + (i % 2));
        }
        // Back to front: the gauge ring, its frame, the alarm. The owner's DK2 screenshot shows the
        // riveted frame over the ring; 0x00422F60 and 0x00415000 push layers 3, 2, 1, which fits if a
        // lower layer is in front (inferred; the partial fill's order is unread).
        _heartFill = Add(new HudImage("GUI\\Windows\\heart-hp", HudImageVariant.Gauge));
        _heart = Add("GUI\\Windows\\heart-bg");
        _heartAlarm = Add("GUI\\Windows\\heart-alarm");
        _paydayFill = Add(new HudImage("GUI\\Windows\\payday-Hp", HudImageVariant.Gauge));
        _payday = Add("GUI\\Windows\\payday-BG");
        _gold = new NumericRow(this); _mana = new NumericRow(this);
        _income = new NumericRow(this); _upkeep = new NumericRow(this);
    }

    /// <summary>Every image the strip can draw, for an adapter to decode before the first visible frame
    /// (the counters come after the opening camera path, ADR-0688).</summary>
    public static IEnumerable<HudImage> Images()
    {
        yield return "GUI\\Windows\\Mana-Bg-00";
        yield return "GUI\\Windows\\Mana-Bg-01";
        yield return "GUI\\Windows\\heart-bg";
        yield return "GUI\\Windows\\heart-alarm";
        yield return "GUI\\Windows\\payday-BG";
        yield return new HudImage("GUI\\Windows\\heart-hp", HudImageVariant.Gauge);
        yield return new HudImage("GUI\\Windows\\payday-Hp", HudImageVariant.Gauge);
        foreach (string family in new[] { LargeDigits, SmallDigits, RateDigits })
        {
            for (int digit = 0; digit <= 9; digit++)
            {
                yield return family + digit.ToString(CultureInfo.InvariantCulture);
            }
        }

        yield return MinusSign;
        foreach (string suffix in new[] { "in", "out", "+", "-" })
        {
            yield return RateDigits + suffix;
        }
    }

    public void SetVisibility(HudVisibility visibility)
    {
        _visible = visibility.ShowNormalHud; _allowInput = visibility.AllowOrdinaryInput;
    }

    /// <summary>The strip's lower edge, in local pixels: 68 lines of DK2's 768.</summary>
    public static float Bottom(HudScreen screen) => 68 * screen.Height / 768f;

    /// <summary>The area the strip claims from the world, in virtual units.</summary>
    public static HudRect Bounds(HudScreen screen) =>
        new(0, 0, screen.Virtual(Math.Min(screen.Width, V(screen, 1020))), screen.Virtual(Bottom(screen)));

    public bool ContainsPointer(HudScreen screen, float x, float y) => _visible && _allowInput && Bounds(screen).Contains(x, y);

    public KeeperResourceKind ResourceAt(HudScreen screen, float x, float y) => !_visible || !_allowInput
        ? KeeperResourceKind.None
        : KeeperResourceInteraction.HitTest(new Vector2(x * screen.Scale, y * screen.Scale), screen.Height);

    /// <summary>Writes the strip's draws for this frame into <paramref name="frame"/>; nothing while hidden,
    /// when the animation does not advance either.</summary>
    public void Build(HudScreen screen, KeeperResourceSnapshot snapshot, uint milliseconds, KeeperResourceFlash flash, HudFrame frame)
    {
        if (!_visible || screen.Height <= 0)
        {
            return;
        }

        KeeperResourceFrame animated = _animation.Evaluate(snapshot, milliseconds, flash);
        Layout(screen, snapshot, animated);
        for (int i = 0; i < _count; i++)
        {
            ref Element e = ref _elements[i];
            if (e.Show)
            {
                frame.Add(new HudDraw(HudDrawKind.Quad, e.Image, e.Rect, e.Tint));
            }
        }
    }

    private void Layout(HudScreen screen, KeeperResourceSnapshot snapshot, KeeperResourceFrame frame)
    {
        int dx = V(screen, 12), y = V(screen, 12), height = V(screen, 124) - y;
        int manaWidth = V(screen, 224), goldLeft = V(screen, 516), goldWidth = V(screen, 740) - goldLeft;
        Place(screen, _backgrounds[0], 4 * dx, y, manaWidth, height);
        Place(screen, _backgrounds[1], 4 * dx + manaWidth, y, manaWidth, height);
        Place(screen, _backgrounds[2], goldLeft + 4 * dx, y, goldWidth, height);
        Place(screen, _backgrounds[3], goldLeft + 4 * dx + goldWidth, y, goldWidth, height);
        int gauge = V(screen, 160);
        Place(screen, _heart, 0, 3, gauge, gauge);
        Place(screen, _heartAlarm, 0, 3, gauge, gauge);
        _elements[_heartAlarm].Show = frame.ShowHeartAlarm;
        Place(screen, _heartFill, 0, 3, gauge, gauge);
        _elements[_heartFill].Image = new HudImage("GUI\\Windows\\heart-hp", HudImageVariant.Gauge, Gauge: snapshot.HeartHealth);
        Place(screen, _payday, goldLeft, y, gauge, gauge);
        Place(screen, _paydayFill, goldLeft, y, gauge, gauge);
        _elements[_paydayFill].Image = new HudImage("GUI\\Windows\\payday-Hp", HudImageVariant.Gauge, Gauge: snapshot.Payday);
        string family = screen.Width >= 640 ? LargeDigits : SmallDigits;
        _mana.Draw(screen, snapshot.Mana.Value, family, V(screen, 172), V(screen, 32), V(screen, 48), V(screen, 40), 7,
            new HudColor(0, 252, 156), frame.ShowManaNumbers);
        HudColor gold = frame.GoldColour == KeeperGoldColour.Money ? new HudColor(238, 182, 0)
            : frame.GoldColour == KeeperGoldColour.AffordableBill ? new HudColor(0, 178, 235)
            : new HudColor(255, 75, 75);
        _gold.Draw(screen, frame.GoldNumber.Value, family, V(screen, 680), V(screen, 40), V(screen, 48), V(screen, 40), 7, gold,
            frame.ShowGoldNumber);
        // The rates show on a screen wider than 600 pixels, and only while the mana numbers do.
        bool rates = screen.Width > 600;
        if (rates)
        {
            _income.Draw(screen, snapshot.Income.Value, RateDigits, V(screen, 184), V(screen, 88), V(screen, 64), V(screen, 16), 0,
                new HudColor(50, 255, 50), frame.ShowManaNumbers, "in", "+");
            _upkeep.Draw(screen, snapshot.Upkeep.Value, RateDigits, V(screen, 360), V(screen, 88), V(screen, 64), V(screen, 16), 0,
                new HudColor(255, 50, 50), frame.ShowManaNumbers, "out", "-");
        }
        else { _income.Hide(); _upkeep.Hide(); }
    }

    /// <summary>A native virtual coordinate in whole local pixels, floored.</summary>
    private static int V(HudScreen screen, int coordinate) => (int)Math.Floor(coordinate * screen.Height / 1920f);

    private void Place(HudScreen screen, int element, float x, float y, float width, float height)
    {
        ref Element e = ref _elements[element];
        e.Rect = new HudRect(screen.Virtual(x), screen.Virtual(y), screen.Virtual(width), screen.Virtual(height));
    }

    private int Add(HudImage image)
    {
        _elements[_count] = new Element { Show = true, Image = image, Tint = HudColor.White };
        return _count++;
    }

    /// <summary>A right-aligned number of glyph images, with an optional icon and sign in front (the rates).</summary>
    private sealed class NumericRow
    {
        private readonly KeeperResourceHud _hud;
        private readonly int[] _glyphs = new int[Glyphs];
        private int _value;
        private string? _digits, _family;

        public NumericRow(KeeperResourceHud hud)
        {
            _hud = hud;
            for (int i = 0; i < _glyphs.Length; i++)
            {
                _glyphs[i] = hud.Add(default);
            }
        }

        public void Hide()
        {
            foreach (int glyph in _glyphs)
            {
                _hud._elements[glyph].Show = false;
            }
        }

        public void Draw(HudScreen screen, int value, string family, float x, float y, float size, float advance, int slots,
            HudColor color, bool visible, string? icon = null, string? sign = null)
        {
            // A steady value builds no string: glyph images are rebuilt only when the value or family changes.
            bool changed = _digits == null || value != _value || family != _family;
            if (changed) { _value = value; _family = family; _digits = value.ToString(CultureInfo.InvariantCulture); }
            string digits = _digits!;
            int prefix = icon == null ? 0 : 2;
            x += Math.Max(0, slots - digits.Length) * advance;
            for (int i = 0; i < _glyphs.Length; i++)
            {
                ref Element glyph = ref _hud._elements[_glyphs[i]];
                bool used = i < prefix + digits.Length;
                glyph.Show = used && visible;
                if (!used)
                {
                    continue;
                }

                if (changed)
                {
                    string suffix = i == 0 && prefix != 0 ? icon! : i == 1 && prefix != 0 ? sign! : digits[i - prefix].ToString();
                    glyph.Image = suffix == "-" && prefix == 0 ? MinusSign : family + suffix;
                }
                glyph.Tint = color;
                _hud.Place(screen, _glyphs[i], x + i * advance, y, size, size);
            }
        }
    }
}
