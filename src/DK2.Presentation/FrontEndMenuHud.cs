// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Settings;

namespace DK2.Presentation;

/// <summary>What the front-end menu reads from the install: strings, BF4 fonts and the size of each front-end image.
/// An engine adapter implements it over the game's files; the model never touches a file or a texture.</summary>
public interface IFrontEndMenuSources
{
    /// <summary>A Text.str string by id; empty when the table has none.</summary>
    string Text(uint id);
    BitmapFont Font(FrontEndFont font);
    /// <summary>The pixel size of a front-end image by name, or null when the install has no such picture (a
    /// level's objective shot may be missing, and DK2 skips a picture whose load failed).</summary>
    (int Width, int Height)? ImageSize(string name);
}

/// <summary>DK2's front end is a 640 x 480 screen drawn at one scale, centred: a wider or taller window adds margins.
/// The draw list is in virtual units (1920 high, ADR-0805); the box is 2560 x 1920 of them, which is also the
/// reference grid the widget records are laid out on, so one 640 x 480 unit is <see cref="VirtualPerUnit"/> virtual
/// units. The pointer, hit tests and the model's own rectangles stay in 640 x 480 units, as the controller
/// takes them.</summary>
public static class FrontEndBox
{
    public const int Width = 640, Height = 480;
    /// <summary>Virtual units per 640 x 480 unit: the box's 1920 virtual height over its 480.</summary>
    public const int VirtualPerUnit = 4;

    /// <summary>Screen pixels per 640 x 480 unit: the largest scale at which the whole box fits.</summary>
    public static float Scale(int screenWidth, int screenHeight) => Math.Min(screenWidth / (float)Width, screenHeight / (float)Height);

    /// <summary>The centred box's top-left corner in screen pixels from the top-left (not rounded).</summary>
    public static (float Left, float Top) Origin(int screenWidth, int screenHeight)
    {
        float scale = Scale(screenWidth, screenHeight);
        return ((screenWidth - Width * scale) / 2, (screenHeight - Height * scale) / 2);
    }

    /// <summary>A screen point (pixels from the top-left) in 640 x 480 units, the box centred.</summary>
    public static FrontEndPoint ToUnits(float x, float yFromTop, int screenWidth, int screenHeight)
    {
        float scale = Scale(screenWidth, screenHeight);
        (float left, float top) = Origin(screenWidth, screenHeight);
        return new FrontEndPoint((x - left) / scale, (yFromTop - top) / scale);
    }
}

/// <summary>The engine-free front-end menu (ADR-0805): every page's labels, titles, briefing pictures, dialogs, hi-scores,
/// campaign results, movie list and settings pages, laid out as BF4 labels and drawn back to front into a
/// <see cref="HudFrame"/>. It owns the hover and press state, the scroll bars and the frame-delayed content;
/// <see cref="Model"/> is the shell navigation it drives. Pointer input is in 640 x 480 units
/// (<see cref="FrontEndBox"/>); the draw list is in virtual units, 4 per unit. A renderer draws each
/// <see cref="HudLabel"/> as the shared bitmap or as its own TrueType glyphs in the glyph boxes.</summary>
public sealed partial class FrontEndMenuHud : IDisposable
{
    private static readonly uint[] TitleIds = [ 71, 1476, 1403, 2803, 96, 2802, 145, 73, 93, 2800,
        94, 95, 98, 168, 162, 141, 149, 2729, 1478, 2801, 372, 1675, 140, 0 ];
    private static readonly HudImage LogoArt = "Logo", MovieHighlight = "Mov_Shots/M-Hilight",
        Confirm = "Confirm", ConfirmClick = "Confirm-Click", ConfirmOver = "Confirm-Over",
        Cancel = "Cancel", CancelClick = "Cancel-Click", CancelOver = "Cancel-Over";
    private const byte Dim = 90, Faded = 89;

    private sealed class Label
    {
        public FrontEndWidget Widget = null!;
        public FrontEndRect Box, Ink;
        public FrontEndLabelGeometry Geometry = null!;
        public HudImage Image;
        /// <summary>The glow at one to four quarters; only a <see cref="FrontEndDrawKind.Label"/> has one.</summary>
        public HudImage[]? Glow;
        public HudRect? ClipRect;
        public FrontEndTextAlignment Alignment;
        public FrontEndVerticalAlignment Vertical;
    }

    private readonly IFrontEndMenuSources _sources;
    private readonly Func<int, float> _hudFit;
    private readonly Func<KeyScanCode, string> _keyName;
    private readonly Dictionary<FrontEndWidgetId, Label> _labels = new();
    private readonly List<FrontEndWidget> _resultWidgets = new();
    private readonly Dictionary<string, (int Width, int Height)> _sizes = new();
    private readonly HashSet<string> _missing = new();
    private readonly List<(HudImage Image, HudRect Rect)> _briefingImages = new();
    private IReadOnlyList<FrontEndWidget> _widgets = Array.Empty<FrontEndWidget>();
    private object? _shownBriefing, _shownResults, _shownScores;
    // The statistics list's scroll bar (widget 180's child, 0x0052A0D0); its position is the first row.
    private FrontEndScrollBar? _resultsBar;
    private FrontEndScrollBar? _movieBar;
    private int _shownMovieRow = -1;
    private int _shownFirstRow;
    private FrontEndPage _shown = (FrontEndPage)ushort.MaxValue;
    private FrontEndPage _pageRectFor = FrontEndPage.None;
    private FrontEndRect _pageRect;

    /// <param name="sources">Strings, fonts and image sizes.</param>
    /// <param name="hudFit">The HUD scale the window fits for a requested percent, for the graphics page's caption.</param>
    /// <param name="keyName">A key's name for the controls page.</param>
    public FrontEndMenuHud(FrontEndSession session, IFrontEndMenuSources sources, Func<int, float> hudFit, Func<KeyScanCode, string> keyName,
        CampaignMapController? campaign = null, OptionsPagesController? options = null)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _hudFit = hudFit ?? throw new ArgumentNullException(nameof(hudFit));
        _keyName = keyName ?? throw new ArgumentNullException(nameof(keyName));
        Model = new FrontEndMenuController(session, HitBounds, campaign, options);
    }

    public FrontEndMenuController Model { get; }

    /// <summary>The page whose labels are laid out now.</summary>
    public FrontEndPage ShownPage => _shown;

    /// <summary>Whether any label is laid out; false before the first page appears.</summary>
    public bool HasLabels => _labels.Count != 0;

    /// <summary>A widget's pointer region in 640 x 480 units: its hit rectangle, and for a label also only its
    /// text's canvas grown by 8.</summary>
    public FrontEndRect HitBounds(FrontEndWidget widget)
    {
        FrontEndRect rect = LabelBox(widget);
        var hit = new FrontEndRect(rect.X + widget.HitRect.X * .25f, rect.Y + widget.HitRect.Y * .25f,
            widget.HitRect.Width * .25f, widget.HitRect.Height * .25f);
        return _labels.TryGetValue(widget.Id, out Label? label) && widget.Draw == FrontEndDrawKind.Label
            ? label.Geometry.PlaceCanvas(rect, label.Alignment, label.Vertical).Inflate(8).Intersect(hit) : hit;
    }

    /// <summary>Where a label's glyphs are inked, in 640 x 480 units.</summary>
    public FrontEndRect LabelInkBounds(FrontEndWidgetId id) => _labels[id].Ink;

    /// <summary>The label a widget shows, for a renderer-side check; null when it has none.</summary>
    public HudLabel? LabelOf(FrontEndWidgetId id) => _labels.TryGetValue(id, out Label? label) ? label.Image.Label : null;

    private FrontEndRect PageRect()
    {
        if (_pageRectFor != Model.Page)
        {
            _pageRect = FrontEndPages.Find(Model.Page)?.DrawRect ?? default;
            _pageRectFor = Model.Page;
        }
        return _pageRect;
    }

    private FrontEndRect LabelBox(FrontEndWidget widget)
    {
        FrontEndRect rect = widget.ReferenceRect(PageRect());
        return widget.Draw == FrontEndDrawKind.Title ? new FrontEndRect(rect.X, rect.Y, rect.Width, 70) : rect;
    }

    private (int Width, int Height)? Size(string name)
    {
        if (_sizes.TryGetValue(name, out (int, int) known))
        {
            return known;
        }

        if (_missing.Contains(name))
        {
            return null;
        }

        (int, int)? size = _sources.ImageSize(name);
        if (size is (int, int) found)
        {
            _sizes.Add(name, found);
        }
        else
        {
            _missing.Add(name);
        }

        return size;
    }

    private (int Width, int Height) RequireSize(string name) =>
        Size(name) ?? throw new FileNotFoundException("Front-end image " + name + " is not in the install.", name);

    /// <summary>The briefing and the statistics arrive a frame after their page appears (0x0053C3C0).</summary>
    private bool ContentChanged => !ReferenceEquals(_shownOptions, Model.Options?.Pending) || _shownKeysFirst != _keysFirst
        || _shownCapturing != Model.Options?.CapturingAction || !ReferenceEquals(_shownBriefing, Model.Campaign?.Briefing)
        || !ReferenceEquals(_shownResults, Model.Campaign?.Results)
        || Model.Page == FrontEndPage.HighScores && !ReferenceEquals(_shownScores, Model.Campaign?.HiScores)
        || _shownFirstRow != ResultsFirstRow
        || Model.Page == FrontEndPage.Movies && _shownMovieRow != Model.Movies?.FirstRow;

    private int ResultsFirstRow => _resultsBar?.Bar.Position ?? 0;

    /// <summary>Lays the labels out again when the page changed or its content arrived. Cheap otherwise; call it
    /// before reading input and again before <see cref="Build"/>.</summary>
    public void Sync()
    {
        if (_shown != Model.Page || ContentChanged)
        {
            RebuildLabels();
        }
    }

    /// <summary>One front-end update: the pointer, the lists' scroll bars and the hover fades.
    /// <paramref name="pointer"/> is null when the window has no size; the hover fades still advance.</summary>
    /// <param name="wheel">Mouse wheel notches since the last update, positive up.</param>
    /// <param name="now">Seconds, for a held scroll arrow's repeat.</param>
    public void Advance(FrontEndPointer? pointer, int wheel, double now)
    {
        // The pointer reads the page's widgets: lay a newly shown page out first.
        Sync();
        if (pointer is FrontEndPointer at)
        {
            UpdateOptionsPointer(at, wheel, now);
            Model.UpdatePointer(at);
            UpdateResultsBar(at, now);
            if (Model.Page == FrontEndPage.Movies && Model.Movies != null)
            {
                _movieBar ??= ArmedMovieBar();
                var point = new FrontEndPoint(at.X, at.Y);
                _movieBar.Update(point, at.PrimaryHeld, now);
                _movieBar.Frame(_movieBar.HitTest(point));
                Model.Movies.ScrollTo(_movieBar.Bar.Position);
            }
        }
        Model.AdvanceFrame();
    }

    private static FrontEndScrollBar ArmedMovieBar()
    {
        var bar = new FrontEndScrollBar(FrontEndPages.Get(FrontEndPage.Movies).Widgets.First(w => w.Id.Value == 52).DrawRect);
        bar.Bar.Arm(17);
        return bar;
    }

    /// <summary>One GUI update of page 26's scroll bar: its input, then its hover glow (0x005507E0, 0x00551BA0).</summary>
    private void UpdateResultsBar(FrontEndPointer pointer, double now)
    {
        if (Model.Page != FrontEndPage.CampaignResults || _resultsBar == null)
        {
            return;
        }

        var point = new FrontEndPoint(pointer.X, pointer.Y);
        _resultsBar.Update(point, pointer.PrimaryHeld, now);
        _resultsBar.Frame(_resultsBar.HitTest(point));
    }

    private void RebuildLabels()
    {
        _labels.Clear(); _shown = Model.Page;
        _widgets = Model.Widgets;
        if (!ReferenceEquals(_shownResults, Model.Campaign?.Results))
        {
            _resultsBar = null;
        }

        FrontEndWidget? list = FirstOfKind(FrontEndDrawKind.StatisticsRows);
        if (_resultsBar == null && list != null && Model.Campaign?.Results != null)
        {
            // 0x0053D810 arms the bar once per game: range 0..9, at the top.
            _resultsBar = FrontEndScrollBar.Beside(list.DrawRect, CampaignResults.ScrollBarGap,
                CampaignResults.ScrollBarWidth, CampaignResults.ScrollBarHeight);
            _resultsBar.Bar.Arm(CampaignResults.ScrollMaximum);
        }
        _shownBriefing = Model.Campaign?.Briefing; _shownResults = Model.Campaign?.Results; _shownScores = Model.Campaign?.HiScores;
        _shownFirstRow = ResultsFirstRow;
        _shownMovieRow = Model.Movies?.FirstRow ?? -1;
        foreach (FrontEndWidget widget in _widgets)
        {
            if (Model.Page == FrontEndPage.Graphics && Model.Options != null
                && (widget.Id.Value >= 119 && widget.Id.Value <= 124 || widget.Id.Value == 679 || widget.Id.Value == 680))
            {
                continue;
            }

            uint textId = widget.TextId;
            string? text = null;
            bool clip = false;
            FrontEndTextAlignment alignment = FrontEndLabelStyles.AlignmentFor(widget);
            FrontEndVerticalAlignment vertical = FrontEndLabelStyles.VerticalFor(widget);
            FrontEndFont fontSpec = FrontEndLabelStyles.FontFor(widget.Draw);
            if (widget.Draw == FrontEndDrawKind.Title)
            { textId = widget.TextId < TitleIds.Length ? TitleIds[widget.TextId] : 0; }
            else if (Model.Page == FrontEndPage.Movies && Model.Movies != null && widget.Id.Value >= 59 && widget.Id.Value <= 63)
            {
                text = Model.Movies.Entries[Model.Movies.FirstRow + widget.Id.Value - 59].Title;
                fontSpec = new FrontEndFont(FrontEndFontFamily.FrontEnd, 14);
                alignment = FrontEndTextAlignment.Left;
            }
            else if (Model.Dialog != null)
            {
                if (widget.Id.Value == 684)
                {
                    textId = Model.Dialog.TitleTextId;
                }
                else if (widget.Id.Value == 686)
                {
                    textId = Model.Dialog.BodyTextId;
                }
                else if (widget.Id.Value == 666)
                {
                    textId = Model.Dialog.SecondaryTextId;
                }
                else
                {
                    continue;
                }
            }
            else if (widget.Draw == FrontEndDrawKind.BriefingSection && Model.Campaign?.Briefing is CampaignBriefing briefing)
            {
                bool heading = widget.DrawArgument2 == 1;
                // 0x0053A580 lays the objectives' text out only in mode 0 (the briefing page); in
                // the other modes (page 26 passes 1) sections 2 and 3 draw only their images. Section 3 draws
                // no text on a secret or special level.
                text = widget.DrawArgument2 switch
                {
                    2 or 3 when widget.DrawArgument1 != 0 => null,
                    3 when briefing.IsSecretOrSpecial => null,
                    1 => briefing.Heading,
                    2 => briefing.MainObjective,
                    3 => string.Join("\n\n", briefing.SubObjectives.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => "- " + s)),
                    _ => "",
                };
                fontSpec = new FrontEndFont(heading ? FrontEndFontFamily.InGame : FrontEndFontFamily.FrontEnd, heading ? 24 : 14);
                alignment = heading ? FrontEndTextAlignment.Centre : FrontEndTextAlignment.Left;
                vertical = heading ? FrontEndVerticalAlignment.Centre : FrontEndVerticalAlignment.Top;
                clip = true;
            }
            else if (!FrontEndLabelStyles.IsText(widget.Draw))
            {
                continue;
            }

            if (text == null && textId == 0)
            {
                continue;
            }

            AddLabel(widget, text ?? _sources.Text(textId), fontSpec, alignment, vertical, clip);
        }
        _resultWidgets.Clear();
        if (Model.Page == FrontEndPage.CampaignResults && Model.Campaign?.Results is CampaignResults results)
        {
            AddResultLabels(FirstOfKind(FrontEndDrawKind.StatisticsRows), results);
        }

        if (Model.Page == FrontEndPage.HighScores && Model.Campaign != null)
        {
            foreach (FrontEndWidget row in _widgets.Where(w => w.Draw == FrontEndDrawKind.HiScoreRow))
            {
                AddHiScoreLabels(row, Model.Campaign);
            }
        }

        RebuildOptionLabels();
        RebuildBriefingImages();
    }

    private FrontEndWidget? FirstOfKind(FrontEndDrawKind kind)
    {
        foreach (FrontEndWidget widget in _widgets)
        {
            if (widget.Draw == kind)
            {
                return widget;
            }
        }

        return null;
    }

    /// <summary>The briefing sections come before the page's text widgets, so their pictures lie beneath. A picture
    /// that is not in the install is skipped, as DK2 skips one whose load failed.</summary>
    private void RebuildBriefingImages()
    {
        _briefingImages.Clear();
        if (Model.Campaign?.Briefing is not CampaignBriefing briefing)
        {
            return;
        }

        foreach (FrontEndWidget section in _widgets)
        {
            if (section.Draw != FrontEndDrawKind.BriefingSection)
            {
                continue;
            }

            foreach (FrontEndPlacedImage placed in FrontEndBriefingImages.For(section.DrawArgument1, section.DrawArgument2, briefing.IsSecretOrSpecial))
            {
                string name = placed.Image switch
                {
                    FrontEndBriefingImage.FirstShot => FrontEndBriefingImages.ShotArtName(briefing.Slot, CampaignObjectiveShot.First),
                    FrontEndBriefingImage.SecondShot => FrontEndBriefingImages.ShotArtName(briefing.Slot, CampaignObjectiveShot.Second),
                    _ => FrontEndBriefingImages.ArtName(placed.Image)!,
                };
                if (Size(name) is not (int width, int height))
                {
                    continue;
                }

                _briefingImages.Add((name, Virtual(new FrontEndRect(placed.X, placed.Y, width, height))));
            }
        }
    }

    private void AddLabel(FrontEndWidget widget, string text, FrontEndFont fontSpec, FrontEndTextAlignment alignment,
        FrontEndVerticalAlignment vertical, bool clip)
    {
        BitmapFont font = _sources.Font(fontSpec);
        FrontEndRect box = LabelBox(widget);
        var geometry = FrontEndLabelGeometry.Measure(font, text, clip ? (int)box.Width : 0);
        if (widget.Draw == FrontEndDrawKind.Title && geometry.Advance > 629)
        { font = _sources.Font(new FrontEndFont(FrontEndFontFamily.FrontEnd, 28)); geometry = FrontEndLabelGeometry.Measure(font, text); }
        var shape = new HudLabel(font, geometry);
        var label = new Label
        {
            Widget = widget, Box = box, Geometry = geometry, Alignment = alignment, Vertical = vertical,
            Ink = geometry.PlaceInk(box, alignment, vertical), Image = HudImage.Of(shape),
            ClipRect = clip ? Virtual(box) : null,
        };
        if (widget.Draw == FrontEndDrawKind.Label)
        {
            label.Glow = new HudImage[4];
            for (int frame = 0; frame < 4; frame++)
            {
                label.Glow[frame] = HudImage.Of(shape, (byte)(frame + 1));
            }
        }
        _labels.Add(widget.Id, label);
    }

    /// <summary>A synthetic text widget for the statistics and hi-score pages; the colour is a
    /// LabelColour index.</summary>
    private void Cell(FrontEndRect rect, string text, uint colour, FrontEndTextAlignment alignment,
        FrontEndVerticalAlignment vertical = FrontEndVerticalAlignment.Centre, bool clip = false)
    {
        var widget = new FrontEndWidget(new FrontEndWidgetId((ushort)(0xF000 + _resultWidgets.Count)), 7, rect, default, 0, default,
            default, FrontEndPage.None, false, 0, FrontEndDrawKind.MediumText, 0, colour << 16, 0, 0, 0);
        _resultWidgets.Add(widget);
        AddLabel(widget, text, new FrontEndFont(FrontEndFontFamily.FrontEnd, 14), alignment, vertical, clip);
    }

    /// <summary>Page 18's header and rows (0x0053DBD0): rank, score, level label and name in DK2's
    /// columns (640-unit x ranges); the entry row shows a caret. The header is (255, 168, 0), the
    /// entry row red, the top row the label colour (+0x3031E, white) and the rest grey.</summary>
    private void AddHiScoreLabels(FrontEndWidget row, CampaignMapController campaign)
    {
        FrontEndRect band = row.DrawRect;
        FrontEndRect Column(float from, float to) => new FrontEndRect(from * 4, band.Y, (to - from) * 4, band.Height);
        if (row.DrawArgument1 == 10)
        {
            uint header = ColourIndex(FrontEndTextColour.Value);
            Cell(Column(50, 160), _sources.Text(80), header, FrontEndTextAlignment.Centre);
            Cell(Column(150, 260), _sources.Text(82), header, FrontEndTextAlignment.Centre);
            Cell(Column(260, 400), _sources.Text(2042), header, FrontEndTextAlignment.Centre);
            Cell(Column(400, 639), _sources.Text(83), header, FrontEndTextAlignment.Centre);
            return;
        }
        int index = (int)row.DrawArgument1;
        if (index >= campaign.HiScores.Count)
        {
            return;
        }

        HiScoreEntry entry = campaign.HiScores[index];
        uint colour = ColourIndex(index == campaign.EntryRow ? FrontEndTextColour.Red
            : index == 0 ? FrontEndTextColour.Label : FrontEndTextColour.Grey);
        Cell(Column(80, 150), (index + 1).ToString(), colour, FrontEndTextAlignment.Centre);
        Cell(Column(150, 270), entry.EvilRating.ToString(), colour, FrontEndTextAlignment.Centre);
        Cell(Column(270, 410), entry.LevelLabel, colour, FrontEndTextAlignment.Centre);
        string name = index == campaign.EntryRow ? entry.Name + "_" : entry.Name;
        if (name.Length > 0)
        {
            Cell(Column(410, 639), name, colour, FrontEndTextAlignment.Left);
        }
    }

    /// <summary>Page 26: 0x0053CAF0's summary at its fixed boxes, then 0x0053D270's rows in the list
    /// widget, clipped to it.</summary>
    private void AddResultLabels(FrontEndWidget? list, CampaignResults results)
    {
        foreach (CampaignResultText text in results.Summary())
        {
            ResultCell(text, false);
        }

        if (list == null)
        {
            return;
        }

        foreach (CampaignResultText text in results.Rows(list.DrawRect, ResultsFirstRow))
        {
            ResultCell(text, true);
        }
    }

    private void ResultCell(CampaignResultText text, bool clip) =>
        Cell(text.Rect, text.Text ?? _sources.Text(text.TextId), ColourIndex(text.Colour), text.Alignment, FrontEndVerticalAlignment.Top, clip);

    private static uint ColourIndex(FrontEndTextColour colour) => colour switch
    {
        FrontEndTextColour.Value => 4,
        FrontEndTextColour.Grey => 3,
        FrontEndTextColour.Rank => 5,
        FrontEndTextColour.Red => 6,
        _ => 0,
    };

    /// <summary>Writes the page into <paramref name="frame"/>, back to front: briefing pictures, every widget's label,
    /// glow and art, the lists' scroll bars, then the settings pages' sliders and toggles. Allocates nothing once
    /// the labels are laid out.</summary>
    public void Build(HudFrame frame)
    {
        Sync();
        if (_shown == FrontEndPage.None)
        {
            return;
        }

        foreach ((HudImage image, HudRect rect) in _briefingImages)
        {
            frame.Add(new HudDraw(HudDrawKind.Quad, image, rect, HudColor.White));
        }

        for (int i = 0; i < _widgets.Count; i++)
        {
            DrawWidget(frame, _widgets[i]);
        }

        for (int i = 0; i < _resultWidgets.Count; i++)
        {
            DrawWidget(frame, _resultWidgets[i]);
        }

        if (Model.Page == FrontEndPage.CampaignResults && _resultsBar != null)
        {
            DrawBar(frame, _resultsBar);
        }

        if (Model.Page == FrontEndPage.Movies && _movieBar != null)
        {
            DrawBar(frame, _movieBar);
        }

        DrawOptions(frame);
    }

    private void DrawWidget(HudFrame frame, FrontEndWidget widget)
    {
        FrontEndWidgetState state = Model.State(widget);
        FrontEndRect rect = LabelBox(widget);
        if (_labels.TryGetValue(widget.Id, out Label? label))
        {
            bool decoration = widget.Draw != FrontEndDrawKind.Label && widget.Primary.Kind == FrontEndActionKind.None;
            bool unsupportedKey = widget.Id.Value >= 800 && widget.Id.Value < 815
                && !KeyboardSetup.IsBuilt(KeyboardSetup.Actions[_keysFirst + (widget.Id.Value - 800) % 5].Action);
            bool unsupportedGraphics = Model.Page == FrontEndPage.Graphics && (widget.Id.Value == 115 || widget.Id.Value == 117 || widget.Id.Value == 127);
            byte alpha = unsupportedKey || unsupportedGraphics || widget.Id.Value == 901 ? Dim : state.Enabled || decoration ? (byte)255 : Dim;
            if (state.GlowFrame >= 0 && label.Glow != null)
            {
                frame.Add(new HudDraw(HudDrawKind.Quad, label.Glow[state.GlowFrame], Virtual(label.Ink.Inflate(HudLabel.GlowPadding)), HudColor.White));
            }

            frame.Add(new HudDraw(HudDrawKind.Quad, label.Image, Virtual(label.Ink), LabelColour(widget, state.Hovered, alpha), Clip: label.ClipRect));
        }
        else if (widget.Draw == FrontEndDrawKind.Logo)
        {
            (int width, int height) = RequireSize("Logo");
            frame.Add(new HudDraw(HudDrawKind.Quad, LogoArt, Virtual(new FrontEndRect(rect.X, rect.Y, width, height)), HudColor.White));
        }
        else if (Model.Page == FrontEndPage.Movies && Model.Movies != null && widget.Id.Value >= 53 && widget.Id.Value <= 57)
        {
            MovieGalleryEntry row = Model.Movies.Entries[Model.Movies.FirstRow + widget.Id.Value - 53];
            frame.Add(new HudDraw(HudDrawKind.Quad, row.Thumbnail, Virtual(rect), HudColor.White));
            if (state.Hovered)
            {
                frame.Add(new HudDraw(HudDrawKind.Quad, MovieHighlight, Virtual(rect), HudColor.White));
            }
        }
        else if (widget.Draw == FrontEndDrawKind.Accept || widget.Draw == FrontEndDrawKind.Cancel)
        {
            bool accept = widget.Draw == FrontEndDrawKind.Accept;
            HudImage art = state.Pressed ? accept ? ConfirmClick : CancelClick
                : state.Hovered ? accept ? ConfirmOver : CancelOver : accept ? Confirm : Cancel;
            frame.Add(new HudDraw(HudDrawKind.Quad, art, Virtual(rect), state.Enabled ? HudColor.White : new HudColor(255, 255, 255, Faded)));
        }
    }

    /// <summary>0x00551BA0: each part's glow frame, then the up arrow, track and down arrow over them, then
    /// the thumb cut from its art's row 8, as wide as the bar (clipped to the art).</summary>
    private void DrawBar(HudFrame frame, FrontEndScrollBar bar)
    {
        string[] art = BarArt;
        for (int part = 0; part < art.Length; part++)
        {
            int glow = bar.GlowFrame(part);
            if (glow < 0)
            {
                continue;
            }

            (int width, int height) = RequireSize(art[part]);
            FrontEndPoint origin = bar.ArtOrigin(part);
            const int pad = FrontEndScrollBar.GlowPadding;
            frame.Add(new HudDraw(HudDrawKind.Quad, HudImage.Glowing(art[part], (byte)(glow + 1)),
                Virtual(new FrontEndRect(origin.X, origin.Y, width + 2 * pad, height + 2 * pad)), HudColor.White));
        }
        for (int part = 0; part < art.Length; part++)
        {
            (int width, int height) = RequireSize(art[part]);
            FrontEndPoint origin = bar.ArtOrigin(part);
            frame.Add(new HudDraw(HudDrawKind.Quad, art[part],
                Virtual(new FrontEndRect(origin.X + FrontEndScrollBar.GlowPadding, origin.Y + FrontEndScrollBar.GlowPadding, width, height)), HudColor.White));
        }
        (int thumbWidth, int thumbHeight) = RequireSize(FrontEndScrollBar.ThumbArt);
        FrontEndRect box = bar.ThumbRect();
        float shownWidth = Math.Min(box.Width, thumbWidth);
        float shownHeight = Math.Min(box.Height, thumbHeight - FrontEndScrollBar.ThumbArtTop);
        var uv = new HudRect(0, (float)FrontEndScrollBar.ThumbArtTop / thumbHeight, shownWidth / thumbWidth, shownHeight / thumbHeight);
        frame.Add(new HudDraw(HudDrawKind.Quad, FrontEndScrollBar.ThumbArt, Virtual(new FrontEndRect(box.X, box.Y, shownWidth, shownHeight)),
            HudColor.White, Uv: uv));
    }

    private static readonly string[] BarArt = [FrontEndScrollBar.UpArrowArt, FrontEndScrollBar.DownArrowArt, FrontEndScrollBar.TrackArt];

    private static HudColor LabelColour(FrontEndWidget widget, bool hovered, byte alpha)
    {
        if (widget.Draw == FrontEndDrawKind.Title || widget.Draw == FrontEndDrawKind.BriefingSection && widget.DrawArgument2 == 1)
        {
            return new HudColor(255, 176, 51, alpha);
        }

        if (widget.Draw == FrontEndDrawKind.Label)
        {
            return hovered ? new HudColor(255, 255, 255, alpha) : new HudColor(191, 191, 191, alpha);
        }

        switch (widget.DrawArgument2 >> 16)
        {
            case 1: return new HudColor(253, 242, 167, alpha);
            case 2: return new HudColor(255, 176, 51, alpha);
            case 3: return new HudColor(191, 191, 191, alpha);
            case 4: return new HudColor(255, 168, 0, alpha);
            case 5: return new HudColor(255, 84, 0, alpha);
            case 6: return new HudColor(255, 0, 0, alpha);
            default: return new HudColor(255, 255, 255, alpha);
        }
    }

    /// <summary>A 640 x 480 rectangle in virtual units.</summary>
    private static HudRect Virtual(FrontEndRect r) =>
        new(r.X * FrontEndBox.VirtualPerUnit, r.Y * FrontEndBox.VirtualPerUnit, r.Width * FrontEndBox.VirtualPerUnit, r.Height * FrontEndBox.VirtualPerUnit);

    public void Dispose() => Model.Dispose();
}
