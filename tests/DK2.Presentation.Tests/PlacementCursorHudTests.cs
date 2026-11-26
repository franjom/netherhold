// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary><see cref="PlacementCursorHud"/> against a copy of the Unity <c>ClassicPlacementOverlay.Draw</c> it came from
/// (plan step 3e): the same images, in the same order, at the same pixels and tints.</summary>
public sealed class PlacementCursorHudTests
{
    private readonly record struct Shown(string Name, float X, float Y, float Width, float Height, HudColor Tint);

    private static readonly Vector2 Pointer = new(700.5f, 410.25f);

    public static TheoryData<int, int, PlacementIcon, int, bool, bool, byte, bool, HandCursorKind> Cases() => new()
    {
        { 1920, 1080, PlacementIcon.PlaceRoom, -250, false, true, 0, false, HandCursorKind.Point },
        { 1920, 1080, PlacementIcon.PlaceRoom, -250, true, false, 0, false, HandCursorKind.Point },
        { 1920, 1080, PlacementIcon.Sell, 125, false, true, 0, false, HandCursorKind.Point },
        { 1920, 1080, PlacementIcon.PlaceDoor, -12345678, false, false, 0, true, HandCursorKind.SmallPoint },
        { 1920, 1080, PlacementIcon.PlaceTrap, 0, true, true, 0, false, HandCursorKind.Dig },
        { 1920, 1080, PlacementIcon.Query, 0, true, true, 0, true, HandCursorKind.SmallPoint },
        { 1920, 1080, PlacementIcon.Kill, -5, false, true, 0, false, HandCursorKind.Default },
        { 1920, 1080, PlacementIcon.Transfer, 0, false, true, 0, false, HandCursorKind.Point },
        { 1920, 1080, PlacementIcon.Spell, -300, true, true, 3, false, HandCursorKind.SpellPoint },
        { 1920, 1080, PlacementIcon.Spell, -300, false, true, 9, false, HandCursorKind.SpellPoint },
        { 600, 480, PlacementIcon.PlaceRoom, -1000, false, true, 0, false, HandCursorKind.Point },
        { 640, 480, PlacementIcon.PlaceRoom, -1000, false, true, 0, false, HandCursorKind.Point },
        { 639, 480, PlacementIcon.PlaceRoom, -1000, false, true, 0, false, HandCursorKind.Point },
        { 2560, 1440, PlacementIcon.Sell, 9, false, true, 0, false, HandCursorKind.Point },
        { 1920, 1080, PlacementIcon.None, -250, true, true, 0, false, HandCursorKind.Point },
        { 1920, 1080, PlacementIcon.Held, -250, true, true, 0, false, HandCursorKind.HoldThing },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ToolCursorsAreUnitys(int width, int height, PlacementIcon icon, int price, bool noGo, bool affordable,
        byte spell, bool overHud, HandCursorKind hand)
    {
        var feedback = new PlacementFeedback(null, false, price, icon, noGo, affordable, spell);
        AssertSame(width, height, feedback, overHud, null, hand);
    }

    [Theory]
    [InlineData(false, false, false, 1)]
    [InlineData(false, true, false, 3)]
    [InlineData(true, false, false, 2)]
    [InlineData(true, true, true, 9)]
    [InlineData(true, false, true, 4)]
    public void HeldIconsAreUnitys(bool noGo, bool dispose, bool firstIsObject, int count)
    {
        HeldItemStack held = Held(count, firstIsObject);
        var feedback = new PlacementFeedback(null, false, -40, PlacementIcon.Held, noGo, true, 0, dispose);
        AssertSame(1920, 1080, feedback, false, held, HandCursorKind.HoldThing);
        AssertSame(1280, 720, feedback, true, held, HandCursorKind.DropThing);
    }

    [Fact]
    public void ThePriceIsTheLastSevenDigitsAndItsRefundColourAssumesTheSameByteOrder()
    {
        var hud = new PlacementCursorHud();
        var frame = new HudFrame();
        hud.Build(frame, new PlacementFeedback(null, false, -123456789, PlacementIcon.PlaceRoom, false, false), Pointer, 1920, 1080, false);
        string[] names = frame.Draws.ToArray().Select(d => d.Image.Path!).ToArray();
        Assert.Equal(new[] { @"GUI\Cursors\I-Place_Room", @"GUI\Icons\i-3", @"GUI\Icons\i-4", @"GUI\Icons\i-5", @"GUI\Icons\i-6",
            @"GUI\Icons\i-7", @"GUI\Icons\i-8", @"GUI\Icons\i-9" }, names);
        Assert.Equal(new HudColor(255, 0, 0), frame.Draws[1].Tint);
        Assert.Equal(new HudColor(255, 128, 128), PlacementCursorHud.PriceColour(-1, true));
        Assert.Equal(new HudColor(128, 255, 255), PlacementCursorHud.PriceColour(1, false));
    }

    [Fact]
    public void ABuildAllocatesNothingOnceWarm()
    {
        var hud = new PlacementCursorHud();
        var frame = new HudFrame();
        var feedback = new PlacementFeedback(null, false, -2500, PlacementIcon.PlaceRoom, true, true);
        hud.Build(frame, feedback, Pointer, 1920, 1080, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++)
        {
            frame.Clear();
            hud.Build(frame, feedback, Pointer, 1920, 1080, false);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ImagesListEveryFixedName()
    {
        HudImage[] images = PlacementCursorHud.Images().ToArray();
        Assert.Contains(new HudImage(PlacementCursorHud.NoGoIconName), images);
        Assert.Contains(new HudImage(@"GUI\Icons\lo-0"), images);
        Assert.Contains(new HudImage(@"GUI\Cursors\I-Transfer"), images);
        Assert.Contains(new HudImage(HeldItemStack.DisposeIconName), images);
    }

    private static void AssertSame(int width, int height, PlacementFeedback feedback, bool overHud, HeldItemStack? held,
        HandCursorKind hand)
    {
        var hud = new PlacementCursorHud { SpellIconName = SpellName, CreatureIconName = CreatureName, ObjectIconName = ObjectName };
        var frame = new HudFrame();
        hud.Build(frame, feedback, Pointer, width, height, overHud, held, hand);
        List<Shown> expected = LegacyDraw(width, height, feedback, Pointer, overHud, held, hand);
        float scale = new HudScreen(width, height).Scale;
        Assert.Equal(expected.Count, frame.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            HudDraw draw = frame.Draws[i];
            Assert.Equal(HudDrawKind.Quad, draw.Kind);
            Assert.Equal(expected[i].Name, draw.Image.Path);
            Assert.Equal(expected[i].Tint, draw.Tint);
            Assert.Equal(expected[i].X, draw.Rect.X * scale, 3);
            Assert.Equal(expected[i].Y, draw.Rect.Y * scale, 3);
            Assert.Equal(expected[i].Width, draw.Rect.Width * scale, 3);
            Assert.Equal(expected[i].Height, draw.Rect.Height * scale, 3);
        }
    }

    private static string? SpellName(byte id) => id == 3 ? @"GUI\Spells\possess" : null;
    private static string? CreatureName(byte id) => id switch { 7 => null, 9 => "", _ => "creature-" + id };
    private static string? ObjectName(byte id) => "object-" + id;

    private static HeldItemStack Held(int count, bool firstIsObject)
    {
        var stack = new HeldItemStack();
        var icons = new List<HeldIcon>();
        for (int i = 0; i < count; i++)
            icons.Add(new HeldIcon((i == 0 && firstIsObject) || i == 3 ? HeldIconKind.Object : HeldIconKind.Creature, (byte)(i + 5)));
        // The stack fills itself from a world's hand; these tests set its icons directly.
        var list = (List<HeldIcon>)typeof(HeldItemStack)
            .GetField("_icons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(stack)!;
        list.AddRange(icons.Take(HeldItemStack.MaxIcons));
        typeof(HeldItemStack).GetProperty(nameof(HeldItemStack.More))!.SetValue(stack, count > HeldItemStack.MaxIcons);
        return stack;
    }

    /// <summary>Unity's <c>ClassicPlacementOverlay.Draw</c> before the move: its images in child order (icon, held 0,
    /// I-Dismiss, held 1..7, the overflow icon, No Go, digits), each one shown, at its pixel place.</summary>
    private static List<Shown> LegacyDraw(int screenWidth, int screenHeight, PlacementFeedback feedback, Vector2 pointer,
        bool overHud, HeldItemStack? held, HandCursorKind handKind)
    {
        Shown? icon = null, dispose = null, more = null, noGo = null;
        var heldShown = new Shown?[HeldItemStack.MaxIcons];
        var digits = new List<Shown>();
        float width = screenWidth, height = screenHeight;
        Vector2 anchor = HandCursor.OverlayAnchor(handKind, pointer, screenHeight, overHud);
        float x = anchor.X, y = anchor.Y;
        float scale = height / 1200f, size = 32 * scale;
        Shown Place(string name, float px, float py, float w, float h, HudColor tint) => new(name, px, py, w, h, tint);
        if (feedback.Icon == PlacementIcon.Held && held != null && held.Icons.Count > 0)
        {
            for (int i = 0; i < held.Icons.Count; i++)
            {
                HeldIcon h = held.Icons[i];
                string? name = h.Kind == HeldIconKind.Creature ? CreatureName(h.TypeId) : ObjectName(h.TypeId);
                if (string.IsNullOrEmpty(name)) continue;
                (int ox, int oy) = HeldItemStack.Offset(i);
                heldShown[i] = Place(name, x + ox * scale, y + oy * scale, size, size, HudColor.White);
            }
            if (held.More)
            {
                (int ox, int oy) = HeldItemStack.Offset(HeldItemStack.MaxIcons);
                more = Place(HeldItemStack.MoreIconName, x + ox * scale, y + oy * scale, size, size, HudColor.White);
            }
            if (feedback.Dispose && held.Icons[0].Kind == HeldIconKind.Creature)
                dispose = Place(HeldItemStack.DisposeIconName, x - 2 * scale, y, size, size, HudColor.White);
            if (feedback.NoGo && !feedback.Dispose)
                noGo = Place(@"GUI\Cursors\I-No_Go", x - 2 * scale, y + 2 * scale, size, size, HudColor.White);
        }
        else
        {
            string? iconName = feedback.Icon switch
            {
                PlacementIcon.PlaceRoom => "GUI\\Cursors\\I-Place_Room",
                PlacementIcon.Sell => "GUI\\Cursors\\I-Sell",
                PlacementIcon.PlaceDoor => "GUI\\Cursors\\I-Place_Door",
                PlacementIcon.PlaceTrap => "GUI\\Cursors\\I-Place_Trap",
                PlacementIcon.Query => "GUI\\Cursors\\I-Query",
                PlacementIcon.Kill => "GUI\\Cursors\\I-Kill",
                PlacementIcon.Transfer => "GUI\\Cursors\\I-Transfer",
                PlacementIcon.Spell => string.IsNullOrEmpty(SpellName(feedback.SpellId)) ? null : SpellName(feedback.SpellId),
                _ => null,
            };
            if (iconName != null)
            {
                icon = Place(iconName, x, y, size, size, HudColor.White);
                float shift = feedback.Icon == PlacementIcon.Spell ? 2 * scale : 0;
                if (feedback.NoGo) noGo = Place("GUI\\Cursors\\I-No_Go", x - shift, y + shift, size, size, HudColor.White);
                if (feedback.Price != 0 && feedback.Icon != PlacementIcon.Spell)
                {
                    string family = width >= 640 ? "GUI\\Icons\\i-" : "GUI\\Icons\\lo-";
                    long magnitude = Math.Abs((long)feedback.Price);
                    int count = 0;
                    for (long rest = magnitude; count == 0 || rest > 0; rest /= 10) count++;
                    count = Math.Min(count, 7);
                    string[] names = new string[count];
                    long value = magnitude;
                    for (int i = count - 1; i >= 0; i--, value /= 10) names[i] = family + (char)('0' + (int)(value % 10));
                    HudColor colour = feedback.Price > 0 ? new HudColor(128, 255, 255, 255)
                        : feedback.Affordable ? new HudColor(255, 128, 128, 255) : new HudColor(255, 0, 0, 255);
                    float glyph = 48 * height / 1920f, advance = 40 * height / 1920f;
                    for (int i = 0; i < count; i++)
                        digits.Add(Place(names[i], x + 30 * scale + i * advance, y + 10 * scale, glyph, glyph, colour));
                }
            }
        }
        var shown = new List<Shown>();
        void Add(Shown? s) { if (s is Shown v) shown.Add(v); }
        Add(icon); Add(heldShown[0]); Add(dispose);
        for (int i = 1; i < heldShown.Length; i++) Add(heldShown[i]);
        Add(more); Add(noGo);
        shown.AddRange(digits);
        return shown;
    }
}
