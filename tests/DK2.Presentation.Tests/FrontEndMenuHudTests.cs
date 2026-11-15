// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Data.Text;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free front-end menu (ADR-0805): labels as BF4 glyph boxes, hover and press, the label raster and
/// glow, the lists' scroll bars and the frame-delayed content.</summary>
public sealed class FrontEndMenuHudTests
{
    /// <summary>Every printable ASCII glyph as a 2x2 block of full coverage, 3 wide with its spacing, in a 10-pixel line.</summary>
    private static readonly BitmapFont Font = new(10, Enumerable.Range(33, 94)
        .Select(c => new BitmapGlyph((char)c, 2, 2, 0, 0, 3, [15, 15, 15, 15]))
        .Append(new BitmapGlyph(' ', 1, 1, 0, 0, 3, [0])).ToArray());

    private sealed class Sources : IFrontEndMenuSources
    {
        public readonly HashSet<string> Missing = new();
        public string Text(uint id) => "T" + id;
        public BitmapFont Font(FrontEndFont font) => FrontEndMenuHudTests.Font;
        public (int Width, int Height)? ImageSize(string name) => Missing.Contains(name) ? null
            : name switch { "Logo" => (96, 64), "0-V-Scroll_Bar" => (12, 20), "3DFE\\3DSliderButton" => (16, 16), _ => (14, 14) };
    }

    /// <summary>On the Main page: the intro path is one frame, so it has already finished.</summary>
    private static FrontEndSession Session()
    {
        var session = new FrontEndSession(path => Enumerable.Range(0, path == FrontEndPath.Intro ? 1 : 30)
            .Select(i => new KcsFrame(i, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000)).ToArray());
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.Zero);
        return session;
    }

    private static FrontEndMenuHud Menu(FrontEndSession? session = null, Sources? sources = null,
        CampaignMapController? campaign = null, OptionsPagesController? options = null) =>
        new(session ?? Session(), sources ?? new Sources(), percent => percent, scan => "K" + scan.Value, campaign, options);

    private static HudDraw[] Draws(FrontEndMenuHud hud)
    {
        var frame = new HudFrame();
        hud.Build(frame);
        return frame.Draws.ToArray();
    }

    private static HudRect Virtual(FrontEndRect r) => new(r.X * 4, r.Y * 4, r.Width * 4, r.Height * 4);
    private static FrontEndPointer Centre(FrontEndRect r, bool held = false) => new(r.X + r.Width / 2, r.Y + r.Height / 2, held);

    private static HudDraw LabelDraw(HudDraw[] draws, FrontEndMenuHud hud, ushort widget) =>
        draws.Single(d => d.Image.Label is not null && d.Image.Variant == HudImageVariant.None && ReferenceEquals(d.Image.Label, hud.LabelOf(new(widget))));

    [Fact]
    public void AMainPageLaysItsLabelsOutAsBf4BoxesInDk2sColoursAndDimsTheOnesItCannotTake()
    {
        using FrontEndMenuHud hud = Menu();
        hud.Sync();
        Assert.Equal(FrontEndPage.Main, hud.ShownPage);
        HudDraw[] draws = Draws(hud);

        HudLabel single = hud.LabelOf(new(1))!;
        Assert.Same(Font, single.Font);
        Assert.Equal(FrontEndLabelGeometry.Measure(Font, "T71").Glyphs.Count, single.Geometry.Glyphs.Count);
        HudDraw label = LabelDraw(draws, hud, 1);
        Assert.Equal(Virtual(hud.LabelInkBounds(new(1))), label.Rect);   // the ink box, 4 virtual units per 640 x 480 unit
        Assert.Equal(new HudColor(191, 191, 191, 255), label.Tint);       // a label not under the pointer
        Assert.Null(label.Clip);

        FrontEndWidget disabled = hud.Model.Widgets.First(w => w.Draw == FrontEndDrawKind.Label && !hud.Model.IsEnabled(w) && hud.LabelOf(w.Id) is not null);
        Assert.Equal(new HudColor(191, 191, 191, 90), LabelDraw(draws, hud, disabled.Id.Value).Tint);
        Assert.DoesNotContain(draws, d => d.Image.Variant == HudImageVariant.Glow);
    }

    [Fact]
    public void AHoverLightsTheLabelAndStartsItsGlowWhichFadesInQuartersAfterThePointerLeaves()
    {
        using FrontEndMenuHud hud = Menu();
        hud.Sync();
        FrontEndRect ink = hud.LabelInkBounds(new(1));
        hud.Advance(Centre(ink), 0, 0);
        HudDraw[] draws = Draws(hud);
        Assert.Equal(new HudColor(255, 255, 255, 255), LabelDraw(draws, hud, 1).Tint);
        HudDraw glow = Assert.Single(draws, d => d.Image.Variant == HudImageVariant.Glow);
        Assert.Same(hud.LabelOf(new(1)), glow.Image.Label);
        Assert.Equal(4, glow.Image.Pieces);
        Assert.Equal(Virtual(ink.Inflate(HudLabel.GlowPadding)), glow.Rect);   // under the label, grown by the glow's padding
        Assert.True(Array.IndexOf(draws, glow) < Array.IndexOf(draws, LabelDraw(draws, hud, 1)));

        var away = new FrontEndPointer(-100, -100, false);
        hud.Advance(away, 0, 0);
        Assert.Equal(4, Assert.Single(Draws(hud), d => d.Image.Variant == HudImageVariant.Glow).Image.Pieces);
        for (int i = 0; i < 3; i++) hud.Advance(away, 0, 0);
        Assert.Equal(3, Assert.Single(Draws(hud), d => d.Image.Variant == HudImageVariant.Glow).Image.Pieces);
        for (int i = 0; i < 2; i++) hud.Advance(away, 0, 0);
        Assert.Equal(1, Assert.Single(Draws(hud), d => d.Image.Variant == HudImageVariant.Glow).Image.Pieces);
        hud.Advance(away, 0, 0);
        Assert.DoesNotContain(Draws(hud), d => d.Image.Variant == HudImageVariant.Glow);
        Assert.Equal(new HudColor(191, 191, 191, 255), LabelDraw(Draws(hud), hud, 1).Tint);
    }

    [Fact]
    public void APressNavigatesAndTheNewPagesLabelsAreLaidOutBeforeTheNextFrame()
    {
        using FrontEndMenuHud hud = Menu();
        hud.Sync();
        hud.Advance(Centre(hud.LabelInkBounds(new(1)), true), 0, 0);
        Assert.Equal(FrontEndPage.SinglePlayer, hud.Model.Page);
        Assert.Equal(FrontEndPage.Main, hud.ShownPage);   // not yet: the page's labels are laid out by the next Sync
        HudDraw[] draws = Draws(hud);                    // Build syncs
        Assert.Equal(FrontEndPage.SinglePlayer, hud.ShownPage);
        Assert.Contains(draws, d => d.Image.Label is not null);
        Assert.Null(hud.LabelOf(new(1)));                // Main's widget 1 is gone
    }

    [Fact]
    public void ADialogShowsOnlyItsTitleBodyAndButtonsAndTheButtonArtFollowsHoverAndPress()
    {
        using FrontEndMenuHud hud = Menu();
        hud.Sync();
        hud.Model.Activate(new(5));   // Quit
        HudDraw[] draws = Draws(hud);
        Assert.Equal(FrontEndPage.Dialog, hud.ShownPage);
        Assert.NotNull(hud.LabelOf(new(684))); Assert.NotNull(hud.LabelOf(new(686)));
        Assert.Equal(2, draws.Count(d => d.Image.Label is not null));
        FrontEndWidget accept = hud.Model.Widgets.Single(w => w.Id.Value == 667);
        FrontEndRect box = accept.ReferenceRect(FrontEndPages.Get(FrontEndPage.Dialog).DrawRect);
        Assert.Equal(new HudDraw(HudDrawKind.Quad, "Confirm", Virtual(box), HudColor.White), draws.Single(d => d.Image.Path == "Confirm"));

        hud.Advance(Centre(box), 0, 0);
        Assert.Contains(Draws(hud), d => d.Image.Path == "Confirm-Over");
        hud.Advance(Centre(box, true), 0, 0);
        Assert.Contains(Draws(hud), d => d.Image.Path == "Confirm-Click");
        Assert.DoesNotContain(Draws(hud), d => d.Image.Path == "Confirm");
    }

    [Fact]
    public void ALabelIsRasterizedFromItsGlyphBoxesKeepingTheStrongerInkWhereBoxesOverlap()
    {
        // 'A' is [3, 15] wide 2 at pen 0; 'B' is [5, 15] reaching back 2 from pen 3, so it starts under A's last pixel.
        var font = new BitmapFont(4, [new BitmapGlyph('A', 2, 1, 0, 0, 3, [15, 3]), new BitmapGlyph('B', 2, 1, -2, 0, 3, [5, 15])]);
        FrontEndLabelGeometry geometry = FrontEndLabelGeometry.Measure(font, "AB");
        var label = new HudLabel(font, geometry);
        RgbaImage plain = HudLabelPixels.Render(label);
        Assert.Equal((3, 1), (plain.Width, plain.Height));
        Assert.Equal(new byte[] { 255, 85, 255 }, new[] { plain.Pixels[3], plain.Pixels[7], plain.Pixels[11] });
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(new byte[] { 255, 255, 255 }, plain.Pixels.Skip(i * 4).Take(3)));

        RgbaImage padded = HudLabelPixels.Render(label, HudLabel.GlowPadding);
        Assert.Equal((3 + 32, 1 + 32), (padded.Width, padded.Height));
        Assert.Equal(255, padded.Pixels[((16 * padded.Width) + 16) * 4 + 3]);
        Assert.Equal(0, padded.Pixels[3]);
    }

    [Fact]
    public void AGlowFrameIsTheNativeGlowAtItsShareOfFullStrength()
    {
        FrontEndLabelGeometry geometry = FrontEndLabelGeometry.Measure(Font, "TEST");
        RgbaImage padded = HudLabelPixels.Render(new HudLabel(Font, geometry), HudLabel.GlowPadding);
        RgbaImage full = FrontEndLabelGlow.Create(padded);
        Assert.Equal(full.Pixels, HudLabelPixels.GlowFrame(padded, 4).Pixels);
        RgbaImage half = HudLabelPixels.GlowFrame(padded, 2);
        Assert.Equal((padded.Width, padded.Height), (half.Width, half.Height));
        for (int i = 0; i < full.Pixels.Length; i += 4)
        {
            Assert.Equal((byte)(full.Pixels[i + 3] * 2 / 4), half.Pixels[i + 3]);
            Assert.Equal(new byte[] { 255, 168, 0 }, half.Pixels.Skip(i).Take(3));
        }
        Assert.Contains(full.Pixels.Where((_, i) => i % 4 == 3), a => a > 0);

        RgbaImage art = new(2, 3, new byte[2 * 3 * 4]);
        RgbaImage artPadded = HudLabelPixels.Pad(art, 6);
        Assert.Equal((14, 15), (artPadded.Width, artPadded.Height));
    }

    [Fact]
    public void ALabelImageComparesByItsFontAndGeometryReferenceSoAFrameStaysCheapToCompare()
    {
        FrontEndLabelGeometry geometry = FrontEndLabelGeometry.Measure(Font, "AB");
        var label = new HudLabel(Font, geometry);
        Assert.Equal(HudImage.Of(label), HudImage.Of(new HudLabel(Font, geometry)));
        Assert.NotEqual(HudImage.Of(label), HudImage.Of(new HudLabel(Font, FrontEndLabelGeometry.Measure(Font, "AB"))));
        Assert.NotEqual(HudImage.Of(label), HudImage.Of(label, 2));
        Assert.NotEqual(HudImage.Of(label, 2), HudImage.Of(label, 3));
        Assert.True(HudImage.Of(label).IsGenerated);
        Assert.False(HudImage.Of(label).IsNone);
        Assert.Equal(HudImageVariant.Glow, HudImage.Glowing("art", 2).Variant);

        using FrontEndMenuHud hud = Menu();
        var first = new HudFrame(); var second = new HudFrame();
        hud.Build(first); hud.Build(second);
        Assert.True(first.SameAs(second));
        hud.Advance(Centre(hud.LabelInkBounds(new(1))), 0, 0);
        hud.Build(second);
        Assert.False(first.SameAs(second));
    }

    [Fact]
    public void BuildingAnUnchangedPageAllocatesNothing()
    {
        using FrontEndMenuHud hud = Menu();
        var frame = new HudFrame();
        hud.Sync();
        hud.Advance(Centre(hud.LabelInkBounds(new(1))), 0, 0);
        for (int i = 0; i < 4; i++) { frame.Clear(); hud.Build(frame); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) { frame.Clear(); hud.Build(frame); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void TheMovieListsBarDrawsItsArtAndThumbHighlightsUnderThePointerAndAPressScrollsTheRows()
    {
        FrontEndSession session = Session();
        using FrontEndMenuHud hud = Menu(session);
        hud.Model.SetMovieGallery(new FrontEndMovieGallery(new CampaignProfile(), _ => true, id => "S" + id, id => "X" + id));
        Assert.True(session.TryShowPage(FrontEndPage.Movies));
        hud.Advance(new FrontEndPointer(-100, -100, false), 0, 0);   // the bar is armed by the page's first update
        HudDraw[] draws = Draws(hud);
        FrontEndScrollBar bar = new(FrontEndPages.Get(FrontEndPage.Movies).Widgets.First(w => w.Id.Value == 52).DrawRect);
        bar.Bar.Arm(17);
        // Up arrow, track and down arrow at the bar's origin plus the glow padding, the thumb cut from art row 8.
        const int pad = FrontEndScrollBar.GlowPadding;
        FrontEndPoint origin = bar.ArtOrigin(0);
        Assert.Contains(draws, d => d.Image.Path == FrontEndScrollBar.UpArrowArt && d.Rect == Virtual(new(origin.X + pad, origin.Y + pad, 14, 14)));
        HudDraw thumb = Assert.Single(draws, d => d.Image.Path == FrontEndScrollBar.ThumbArt);
        FrontEndRect box = bar.ThumbRect();
        Assert.Equal(Virtual(new(box.X, box.Y, Math.Min(box.Width, 12), Math.Min(box.Height, 20 - FrontEndScrollBar.ThumbArtTop))), thumb.Rect);
        Assert.Equal(new HudRect(0, 8 / 20f, Math.Min(box.Width, 12) / 12f, Math.Min(box.Height, 12f) / 20f), thumb.Uv);
        Assert.DoesNotContain(draws, d => d.Image.Variant == HudImageVariant.Glow);
        Assert.Equal("X77", Text(hud, 59));   // the first row's title

        // Hovering the down arrow lights its glow; pressing it scrolls one row, and the titles follow one frame later.
        FrontEndRect screen = bar.Screen;
        var arrow = new FrontEndPointer(screen.X + screen.Width / 2, screen.Y + screen.Height - 2, false);
        hud.Advance(arrow, 0, 0);
        Assert.Contains(Draws(hud), d => d.Image.Variant == HudImageVariant.Glow && d.Image.Path == FrontEndScrollBar.DownArrowArt && d.Image.Pieces == 4);
        hud.Advance(arrow with { PrimaryHeld = true }, 0, 1);
        Assert.Equal(1, hud.Model.Movies!.FirstRow);
        Assert.Equal("X77", Text(hud, 59, sync: false));
        Assert.Equal(hud.Model.Movies!.Entries[1].Title, Text(hud, 59));
    }

    /// <summary>The text a label shows: its glyph characters in order.</summary>
    private static string Text(FrontEndMenuHud hud, ushort widget, bool sync = true)
    {
        if (sync) hud.Sync();
        HudLabel? label = hud.LabelOf(new(widget));
        return label is null ? "" : string.Concat(label.Geometry.Glyphs.Select(g => g.Character));
    }

    [Fact]
    public void ContentThatArrivesAfterItsPageIsLaidOutOnTheNextSync()
    {
        FrontEndSession session = Session();
        using FrontEndMenuHud hud = Menu(session);
        Assert.True(session.TryShowPage(FrontEndPage.Movies));
        hud.Sync();
        Assert.Null(hud.LabelOf(new(59)));   // no gallery yet
        hud.Model.SetMovieGallery(new FrontEndMovieGallery(new CampaignProfile(), _ => true, id => "S" + id, id => "X" + id));
        Assert.Null(hud.LabelOf(new(59)));   // not until the next update
        hud.Sync();
        Assert.Equal("X77", Text(hud, 59));
        hud.Model.Movies!.ScrollTo(3);
        Assert.Equal("X77", Text(hud, 59, sync: false));
        Assert.Equal(hud.Model.Movies!.Entries[3].Title, Text(hud, 59));
    }

    [Fact]
    public void ABriefingsPicturesSitUnderItsTextAndAMissingOneIsSkipped()
    {
        var session = new FrontEndSession(_ => new[] { new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000) });
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromSeconds(1));
        var map = new CampaignMapController(session, new CampaignProfile(), false,
            slot => new CampaignBriefing(slot, "A realm", "A description", "Keep the Heart alive.", ["Dig"]));
        var sources = new Sources();
        sources.Missing.Add(FrontEndBriefingImages.ShotArtName(new CampaignSlot(1), CampaignObjectiveShot.Second));
        using FrontEndMenuHud hud = Menu(session, sources, map);
        map.Continue(); session.Update(TimeSpan.FromSeconds(2));
        map.Select(new CampaignSlot(1)); session.Update(TimeSpan.FromSeconds(3));
        Assert.Equal(FrontEndPage.Briefing, session.Page);
        Assert.NotNull(map.Briefing);

        HudDraw[] draws = Draws(hud);
        string first = FrontEndBriefingImages.ShotArtName(new CampaignSlot(1), CampaignObjectiveShot.First);
        string second = FrontEndBriefingImages.ShotArtName(new CampaignSlot(1), CampaignObjectiveShot.Second);
        Assert.Equal(new HudDraw(HudDrawKind.Quad, first, Virtual(new(20, 110, 14, 14)), HudColor.White), draws.First());   // pictures first
        Assert.DoesNotContain(draws, d => d.Image.Path == second);
        Assert.Contains(draws, d => d.Image.Path == "BG_Tiles/BG-Objective-00");
        Assert.True(Array.FindIndex(draws, d => d.Image.Label is not null) > Array.FindIndex(draws, d => d.Image.Path == first));
        // The heading is a clipped label in the title colour.
        Assert.Contains(draws, d => d.Image.Label is not null && d.Tint == new HudColor(255, 176, 51, 255) && d.Clip is not null);
    }

    [Fact]
    public void TheControlsPageShowsFiveKeyRowsAWheelScrollsThemAndAPressedRowWaitsForAKey()
    {
        FrontEndSession session = Session();
        var options = new OptionsPagesController(new GameSettings());
        using FrontEndMenuHud hud = Menu(session, options: options);
        Assert.True(session.TryShowPage(FrontEndPage.Controls));
        hud.Sync();
        FrontEndWidget list = hud.Model.Widgets.Single(w => w.Id.Value == 150);
        FrontEndRect box = list.ReferenceRect(FrontEndPages.Get(FrontEndPage.Controls).DrawRect);
        Assert.Equal("T" + KeyboardSetup.Actions[0].TextId, Text(hud, 800));
        Assert.Equal("T" + KeyboardSetup.Actions[4].TextId, Text(hud, 804));

        // Scroll to the first row whose action a key reaches: the others cannot be bound.
        int built = Enumerable.Range(1, KeyboardSetup.Actions.Count - 1).First(i => KeyboardSetup.IsBuilt(KeyboardSetup.Actions[i].Action));
        var inside = new FrontEndPointer(box.X + 4, box.Y + 4, false);
        hud.Advance(inside, -built, 0);   // one notch down per row
        Assert.Equal("T" + KeyboardSetup.Actions[0].TextId, Text(hud, 800, sync: false));
        Assert.Equal("T" + KeyboardSetup.Actions[built].TextId, Text(hud, 800));

        hud.Advance(inside with { PrimaryHeld = true }, 0, 1);
        Assert.Equal(KeyboardSetup.Actions[built].Action, options.CapturingAction);
        Assert.Equal("T1475", Text(hud, 810));   // the key column asks for a key
        options.CancelKeyCapture();
        Assert.NotEqual("T1475", Text(hud, 810));
    }

    [Fact]
    public void ASliderFollowsAPressOnItsThumbAndTheGraphicsCaptionNamesTheFittingScale()
    {
        FrontEndSession session = Session();
        var options = new OptionsPagesController(new GameSettings());
        using FrontEndMenuHud hud = Menu(session, options: options);
        Assert.True(session.TryShowPage(FrontEndPage.Graphics));
        HudDraw[] draws = Draws(hud);
        Assert.Equal("HUDscale100%(fits100%)", Text(hud, 901).Replace(" ", ""));
        FrontEndWidget slider = hud.Model.Widgets.First(w => options.Supports(w.Id.Value) && options.Range(w.Id.Value).Maximum > 1 && w.Id.Value != 133);
        FrontEndRect track = slider.ReferenceRect(FrontEndPages.Get(FrontEndPage.Graphics).DrawRect);
        Assert.Contains(draws, d => d.Image.Path == "3DFE\\3DGraphicsHSlider" && d.Rect == Virtual(track));
        (int lo, int hi) = options.Range(slider.Id.Value);

        var far = new FrontEndPointer(track.X + track.Width - 1, track.Y + track.Height / 2, false);
        FrontEndRect thumbRect = SliderThumb.Rect(track, 16, 16, options.Value(slider.Id.Value), lo, hi);
        hud.Advance(Centre(thumbRect, true), 0, 1);           // a press on the thumb grabs it
        hud.Advance(far with { PrimaryHeld = true }, 0, 2);   // and a drag follows the pointer
        Assert.Equal(hi, options.Value(slider.Id.Value));
        hud.Advance(far, 0, 3);
        Assert.Contains(Draws(hud), d => d.Image.Path == "3DFE\\3DSliderButton" && d.Rect.X == (track.X + track.Width - 16) * 4);
    }

    private sealed class InstallSources : IFrontEndMenuSources
    {
        private readonly string _text = Path.Combine(Dk2InstallGate.Path, "Data", "Text", "Default");
        private readonly Dk2StringTable _strings;
        private readonly Dictionary<string, BitmapFont> _fonts = new();
        public InstallSources() => _strings = Dk2StringTable.Open(Path.Combine(_text, "Text.str"));
        public string Text(uint id) => _strings.TryResolve(id, out string? text) ? text : "";
        public BitmapFont Font(FrontEndFont font) =>
            _fonts.TryGetValue(font.FileName, out BitmapFont? known) ? known : _fonts[font.FileName] = Bf4FontReader.Open(Path.Combine(_text, font.FileName));
        public (int Width, int Height)? ImageSize(string name) => (14, 14);
    }

    /// <summary>The install's own BF4 fonts and strings through the label primitive: every label's raster covers its ink
    /// box with the font's coverage, and a glow frame reaches past it.</summary>
    [InstallFact]
    public void EveryLabelOfTheInstallsMainPageRastersToItsInkBoxAndGlowsBeyondIt()
    {
        using FrontEndMenuHud real = new(Session(), new InstallSources(), percent => percent, scan => "K" + scan.Value);
        real.Sync();
        HudDraw[] draws = Draws(real);
        HudDraw[] labels = draws.Where(d => d.Image.Label is not null && d.Image.Variant == HudImageVariant.None).ToArray();
        Assert.NotEmpty(labels);
        foreach (HudDraw draw in labels)
        {
            HudLabel label = draw.Image.Label!;
            RgbaImage raster = HudLabelPixels.Render(label);
            Assert.Equal(Math.Max(1, (int)label.Geometry.InkBounds.Width), raster.Width);
            Assert.Equal(Math.Max(1, (int)label.Geometry.InkBounds.Height), raster.Height);
            Assert.Contains(raster.Pixels.Where((_, i) => i % 4 == 3), a => a == 255);
            Assert.Equal(label.Geometry.InkBounds.Width * 4, draw.Rect.Width);
            RgbaImage glow = HudLabelPixels.GlowFrame(HudLabelPixels.Render(label, HudLabel.GlowPadding), 4);
            Assert.Equal((raster.Width + 32, raster.Height + 32), (glow.Width, glow.Height));
            Assert.Equal(0, glow.Pixels[3]);   // the far corner stays clear of the halo
        }
    }

    [Theory]
    [InlineData(1024, 768, 1.6f, 0f, 0f)]
    [InlineData(1600, 900, 1.875f, 200f, 0f)]
    [InlineData(800, 800, 1.25f, 0f, 100f)]
    public void TheBoxIsCentredAtTheLargestScaleThatFits(int width, int height, float scale, float left, float top)
    {
        Assert.Equal(scale, FrontEndBox.Scale(width, height));
        FrontEndPoint corner = FrontEndBox.ToUnits(left, top, width, height);
        Assert.Equal((0f, 0f), (corner.X, corner.Y));
        FrontEndPoint far = FrontEndBox.ToUnits(left + 640 * scale, top + 480 * scale, width, height);
        Assert.Equal(640f, far.X, 2); Assert.Equal(480f, far.Y, 2);
    }
}
