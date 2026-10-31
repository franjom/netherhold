// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using DK2.Data.Model;
using DK2.Art.Textures;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Front-end overlay over the native station view, with bitmap-matched replacement glyphs.</summary>
    public sealed partial class ClassicFrontEndMenu : IDisposable
    {
        private static readonly uint[] TitleIds = { 71, 1476, 1403, 2803, 96, 2802, 145, 73, 93, 2800,
            94, 95, 98, 168, 162, 141, 149, 2729, 1478, 2801, 372, 1675, 140, 0 };
        private sealed class LabelData
        {
            public FrontEndWidget Widget;
            public FrontEndLabelGeometry Geometry;
            public string Text;
            public Texture2D Bitmap;
            public Texture2D[] Glow;
            public CharacterInfo[] FontGlyphs;
            public bool UsesFont;
            public bool Clip;
            public FrontEndTextAlignment Alignment;
            public FrontEndVerticalAlignment Vertical;
        }
        private sealed class Canvas : VisualElement
        {
            public Canvas(Action<MeshGenerationContext> draw) { generateVisualContent += draw; pickingMode = PickingMode.Ignore; }
        }
        private readonly IClassicFrontEndMenuAssets _assets;
        private readonly GameObject _owner;
        private readonly PanelSettings _panel;
        private readonly Canvas _canvas;
        private readonly Dictionary<FrontEndWidgetId, LabelData> _labels = new Dictionary<FrontEndWidgetId, LabelData>();
        private readonly List<FrontEndWidget> _resultWidgets = new List<FrontEndWidget>();
        private object _shownBriefing, _shownResults, _shownScores;
        // The statistics list's scroll bar (widget 180's child, 0x0052A0D0); its position is the first row.
        private FrontEndScrollBar _resultsBar;
        private FrontEndScrollBar _movieBar;
        private int _shownMovieRow = -1;
        private int _shownFirstRow;
        private int ResultsFirstRow => _resultsBar?.Bar.Position ?? 0;
        // Glow frames of the scroll bar's art (0x0054FD20), by art name.
        private readonly Dictionary<string, Texture2D[]> _scrollGlow = new Dictionary<string, Texture2D[]>();
        // Briefing pictures that are not in the install; DK2 skips a picture whose load failed.
        private readonly HashSet<(CampaignSlot, FrontEndBriefingImage)> _missingImages = new HashSet<(CampaignSlot, FrontEndBriefingImage)>();
        private FrontEndPage _shown = (FrontEndPage)ushort.MaxValue;
        private bool _fontDirty = true, _useReplacement = true;
        private RenderTexture _fontAtlas;
        private Material _fontMaterial;
        private const int FontResolution = 96;
        public PanelSettings Panel => _panel;
        public FrontEndMenuController Model { get; }
        public bool UseReplacementFont
        {
            get => _useReplacement;
            set { if (_useReplacement != value) { _useReplacement = value; _fontDirty = true; } }
        }
        public bool UsingReplacementFont => _labels.Values.Any(label => label.UsesFont);

        public ClassicFrontEndMenu(Transform parent, FrontEndSession session, IClassicFrontEndMenuAssets assets, CampaignMapController campaign = null, OptionsPagesController options = null)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            Model = new FrontEndMenuController(session, HitBounds, campaign, options);
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panel.referenceResolution = new Vector2Int(640, 480);
            _panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panel.match = Screen.width * 3 >= Screen.height * 4 ? 1 : 0;
            _panel.sortingOrder = 20;
            _owner = new GameObject("Front-end menu"); _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _panel;
            VisualElement root = document.rootVisualElement; root.pickingMode = PickingMode.Ignore;
            root.style.width = Length.Percent(100); root.style.height = Length.Percent(100);
            // DK2's menus are a 640x480 screen. Keep that box at one scale, centred, so a wide screen adds
            // margins (the front-end camera keeps the 4:3 vertical view, so the box still sits on the 3D scene).
            _canvas = new Canvas(Draw);
            _canvas.style.position = Position.Absolute; _canvas.style.width = 640; _canvas.style.height = 480;
            _canvas.style.left = Length.Percent(50); _canvas.style.top = Length.Percent(50);
            _canvas.style.marginLeft = -320; _canvas.style.marginTop = -240;
            root.Add(_canvas);
            Font.textureRebuilt += FontRebuilt;
        }

        public FrontEndRect HitBounds(FrontEndWidget widget)
        {
            FrontEndRect rect = LabelBox(widget);
            var hit = new FrontEndRect(rect.X + widget.HitRect.X * .25f, rect.Y + widget.HitRect.Y * .25f,
                widget.HitRect.Width * .25f, widget.HitRect.Height * .25f);
            return _labels.TryGetValue(widget.Id, out LabelData label) && widget.Draw == FrontEndDrawKind.Label
                ? label.Geometry.PlaceCanvas(rect, label.Alignment, label.Vertical).Inflate(8).Intersect(hit) : hit;
        }
        public void SetVisible(bool visible) => _owner.SetActive(visible);
        public FrontEndRect LabelInkBounds(FrontEndWidgetId id)
        {
            LabelData label = _labels[id];
            return label.Geometry.PlaceInk(LabelBox(label.Widget), label.Alignment, label.Vertical);
        }
        private FrontEndRect LabelBox(FrontEndWidget widget)
        {
            FrontEndRect rect = widget.ReferenceRect(FrontEndPages.Get(Model.Page).DrawRect);
            return widget.Draw == FrontEndDrawKind.Title ? new FrontEndRect(rect.X, rect.Y, rect.Width, 70) : rect;
        }
        public void Refresh(bool pollInput = true)
        {
            if (_shown != Model.Page || ContentChanged) RebuildLabels();
            if (_fontDirty) PrepareFont();
            int width = _panel.targetTexture != null ? _panel.targetTexture.width : Screen.width;
            int height = _panel.targetTexture != null ? _panel.targetTexture.height : Screen.height;
            // Match whichever side keeps the whole 640x480 box on screen.
            if (width > 0 && height > 0) _panel.match = width * 3 >= height * 4 ? 1 : 0;
            if (pollInput)
            {
                Vector3 mouse = Input.mousePosition;
                if (width > 0 && height > 0)
                {
                    float scale = Mathf.Min(width / 640f, height / 480f);
                    float left = (width - 640 * scale) / 2, top = (height - 480 * scale) / 2;
                    var pointer = new FrontEndPointer((mouse.x - left) / scale, (height - mouse.y - top) / scale, Input.GetMouseButton(0));
                    UpdateOptionsPointer(pointer);
                    Model.UpdatePointer(pointer);
                    UpdateResultsBar(pointer);
                    if (Model.Page == FrontEndPage.Movies && Model.Movies != null)
                    {
                        if (_movieBar == null)
                        {
                            _movieBar = new FrontEndScrollBar(FrontEndPages.Get(FrontEndPage.Movies).Widgets.First(w => w.Id.Value == 52).DrawRect);
                            _movieBar.Bar.Arm(17);
                        }
                        var moviePointer = new FrontEndPoint(pointer.X, pointer.Y);
                        _movieBar.Update(moviePointer, pointer.PrimaryHeld, Time.unscaledTimeAsDouble);
                        _movieBar.Frame(_movieBar.HitTest(moviePointer));
                        Model.Movies.ScrollTo(_movieBar.Bar.Position);
                    }
                }
                Model.AdvanceFrame();
            }
            if (_shown != Model.Page || ContentChanged) { RebuildLabels(); PrepareFont(); }
            _canvas.MarkDirtyRepaint();
        }
        // The briefing and the statistics arrive a frame after their page appears (0x0053C3C0).
        private bool ContentChanged => !ReferenceEquals(_shownOptions, Model.Options?.Pending) || _shownKeysFirst != _keysFirst
            || _shownCapturing != Model.Options?.CapturingAction || !ReferenceEquals(_shownBriefing, Model.Campaign?.Briefing)
            || !ReferenceEquals(_shownResults, Model.Campaign?.Results)
            || Model.Page == FrontEndPage.HighScores && !ReferenceEquals(_shownScores, Model.Campaign?.HiScores)
            || _shownFirstRow != ResultsFirstRow
            || Model.Page == FrontEndPage.Movies && _shownMovieRow != Model.Movies?.FirstRow;
        /// <summary>One GUI update of page 26's scroll bar: its input, then its hover glow (0x005507E0, 0x00551BA0).</summary>
        private void UpdateResultsBar(FrontEndPointer pointer)
        {
            if (Model.Page != FrontEndPage.CampaignResults || _resultsBar == null) return;
            var point = new FrontEndPoint(pointer.X, pointer.Y);
            _resultsBar.Update(point, pointer.PrimaryHeld, Time.unscaledTimeAsDouble);
            _resultsBar.Frame(_resultsBar.HitTest(point));
        }
        private void RebuildLabels()
        {
            foreach (LabelData label in _labels.Values) DestroyLabel(label);
            _labels.Clear(); _shown = Model.Page;
            if (!ReferenceEquals(_shownResults, Model.Campaign?.Results)) _resultsBar = null;
            FrontEndWidget list = Model.Widgets.FirstOrDefault(w => w.Draw == FrontEndDrawKind.StatisticsRows);
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
            foreach (FrontEndWidget widget in Model.Widgets)
            {
                if (Model.Page == FrontEndPage.Graphics && Model.Options != null
                    && (widget.Id.Value >= 119 && widget.Id.Value <= 124 || widget.Id.Value == 679 || widget.Id.Value == 680)) continue;
                uint textId = widget.TextId;
                string text = null;
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
                    if (widget.Id.Value == 684) textId = Model.Dialog.TitleTextId;
                    else if (widget.Id.Value == 686) textId = Model.Dialog.BodyTextId;
                    else if (widget.Id.Value == 666) textId = Model.Dialog.SecondaryTextId;
                    else continue;
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
                else if (!FrontEndLabelStyles.IsText(widget.Draw)) continue;
                if (text == null && textId == 0) continue;
                AddLabel(widget, text ?? _assets.Text(textId), fontSpec, alignment, vertical, clip);
            }
            _resultWidgets.Clear();
            if (Model.Page == FrontEndPage.CampaignResults && Model.Campaign?.Results is CampaignResults results)
                AddResultLabels(Model.Widgets.FirstOrDefault(w => w.Draw == FrontEndDrawKind.StatisticsRows), results);
            if (Model.Page == FrontEndPage.HighScores && Model.Campaign != null)
                foreach (FrontEndWidget row in Model.Widgets.Where(w => w.Draw == FrontEndDrawKind.HiScoreRow))
                    AddHiScoreLabels(row, Model.Campaign);
            RebuildOptionLabels();
            _fontDirty = true;
        }
        private void AddLabel(FrontEndWidget widget, string text, FrontEndFont fontSpec, FrontEndTextAlignment alignment,
            FrontEndVerticalAlignment vertical, bool clip)
        {
            BitmapFont font = _assets.Font(fontSpec);
            FrontEndLabelGeometry geometry = FrontEndLabelGeometry.Measure(font, text, clip ? (int)LabelBox(widget).Width : 0);
            if (widget.Draw == FrontEndDrawKind.Title && geometry.Advance > 629)
            { font = _assets.Font(new FrontEndFont(FrontEndFontFamily.FrontEnd, 28)); geometry = FrontEndLabelGeometry.Measure(font, text); }
            _labels.Add(widget.Id, new LabelData { Widget = widget, Text = text, Geometry = geometry, Bitmap = Bitmap(font, geometry),
                Glow = widget.Draw == FrontEndDrawKind.Label ? Glow(font, geometry) : null,
                Alignment = alignment, Vertical = vertical, Clip = clip });
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
                Cell(Column(50, 160), _assets.Text(80), header, FrontEndTextAlignment.Centre);
                Cell(Column(150, 260), _assets.Text(82), header, FrontEndTextAlignment.Centre);
                Cell(Column(260, 400), _assets.Text(2042), header, FrontEndTextAlignment.Centre);
                Cell(Column(400, 639), _assets.Text(83), header, FrontEndTextAlignment.Centre);
                return;
            }
            int index = (int)row.DrawArgument1;
            if (index >= campaign.HiScores.Count) return;
            HiScoreEntry entry = campaign.HiScores[index];
            uint colour = ColourIndex(index == campaign.EntryRow ? FrontEndTextColour.Red
                : index == 0 ? FrontEndTextColour.Label : FrontEndTextColour.Grey);
            Cell(Column(80, 150), (index + 1).ToString(), colour, FrontEndTextAlignment.Centre);
            Cell(Column(150, 270), entry.EvilRating.ToString(), colour, FrontEndTextAlignment.Centre);
            Cell(Column(270, 410), entry.LevelLabel, colour, FrontEndTextAlignment.Centre);
            string name = index == campaign.EntryRow ? entry.Name + "_" : entry.Name;
            if (name.Length > 0) Cell(Column(410, 639), name, colour, FrontEndTextAlignment.Left);
        }
        /// <summary>Page 26: 0x0053CAF0's summary at its fixed boxes, then 0x0053D270's rows in the list
        /// widget, clipped to it.</summary>
        private void AddResultLabels(FrontEndWidget list, CampaignResults results)
        {
            foreach (CampaignResultText text in results.Summary()) ResultCell(text, false);
            if (list == null) return;
            foreach (CampaignResultText text in results.Rows(list.DrawRect, ResultsFirstRow)) ResultCell(text, true);
        }
        private void ResultCell(CampaignResultText text, bool clip) =>
            Cell(text.Rect, text.Text ?? _assets.Text(text.TextId), ColourIndex(text.Colour), text.Alignment, FrontEndVerticalAlignment.Top, clip);
        private static uint ColourIndex(FrontEndTextColour colour) => colour switch
        {
            FrontEndTextColour.Value => 4,
            FrontEndTextColour.Grey => 3,
            FrontEndTextColour.Rank => 5,
            FrontEndTextColour.Red => 6,
            _ => 0,
        };
        private void FontRebuilt(Font font) { if (font == _assets.ReplacementFont) _fontDirty = true; }
        private void PrepareFont()
        {
            Font font = _useReplacement ? _assets.ReplacementFont : null;
            if (font != null) font.RequestCharactersInTexture(string.Concat(_labels.Values.Select(label => label.Text)), FontResolution, FontStyle.Normal);
            if (font != null)
            {
                Texture source = font.material.mainTexture;
                if (_fontAtlas == null || _fontAtlas.width != source.width || _fontAtlas.height != source.height)
                {
                    if (_fontAtlas != null) { _fontAtlas.Release(); UnityEngine.Object.Destroy(_fontAtlas); }
                    _fontAtlas = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                    _fontAtlas.Create();
                }
                if (_fontMaterial == null) _fontMaterial = new Material(Resources.Load<Shader>("DK2FrontEndFont"));
                Graphics.Blit(source, _fontAtlas, _fontMaterial);
            }
            foreach (LabelData label in _labels.Values)
            {
                label.UsesFont = font != null;
                if (font == null) continue;
                label.FontGlyphs = new CharacterInfo[label.Geometry.Glyphs.Count];
                for (int i = 0; i < label.FontGlyphs.Length; i++)
                    if (!font.GetCharacterInfo(label.Geometry.Glyphs[i].Character, out label.FontGlyphs[i], FontResolution, FontStyle.Normal))
                    { label.UsesFont = false; break; }
            }
            _fontDirty = false;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (_shown == FrontEndPage.None) return;
            float sx = _canvas.contentRect.width / 640, sy = _canvas.contentRect.height / 480;
            if (sx <= 0 || sy <= 0) return;
            // The briefing sections come before the page's text widgets, so their pictures lie beneath.
            if (Model.Campaign?.Briefing is CampaignBriefing briefing)
                foreach (FrontEndWidget section in Model.Widgets.Where(w => w.Draw == FrontEndDrawKind.BriefingSection))
                    foreach (FrontEndPlacedImage placed in FrontEndBriefingImages.For(section.DrawArgument1, section.DrawArgument2, briefing.IsSecretOrSpecial))
                        if (BriefingImage(briefing, placed.Image) is Texture2D image)
                            Quad(context, image, new FrontEndRect(placed.X, placed.Y, image.width, image.height), Color.white, sx, sy);
            foreach (FrontEndWidget widget in Model.Widgets.Concat(_resultWidgets))
            {
                FrontEndWidgetState state = Model.State(widget);
                FrontEndRect rect = LabelBox(widget);
                if (_labels.TryGetValue(widget.Id, out LabelData label))
                {
                    bool decoration = widget.Draw != FrontEndDrawKind.Label && widget.Primary.Kind == FrontEndActionKind.None;
                    bool unsupportedKey = widget.Id.Value >= 800 && widget.Id.Value < 815
                        && !KeyboardSetup.IsBuilt(KeyboardSetup.Actions[_keysFirst + (widget.Id.Value - 800) % 5].Action);
                    bool unsupportedGraphics = Model.Page == FrontEndPage.Graphics && (widget.Id.Value == 115 || widget.Id.Value == 117 || widget.Id.Value == 127);
                    byte alpha = unsupportedKey || unsupportedGraphics || widget.Id.Value == 901 ? (byte)90 : state.Enabled || decoration ? (byte)255 : (byte)90;
                    Color32 tint = LabelColour(widget, state.Hovered, alpha);
                    FrontEndRect ink = label.Geometry.PlaceInk(rect, label.Alignment, label.Vertical);
                    if (state.GlowFrame >= 0 && label.Glow != null)
                        Quad(context, label.Glow[state.GlowFrame], ink.Inflate(16), Color.white, sx, sy);
                    DrawLabel(context, label, ink, tint, sx, sy);
                }
                else if (widget.Draw == FrontEndDrawKind.Logo)
                {
                    Texture2D logo = _assets.Image("Logo");
                    Quad(context, logo, new FrontEndRect(rect.X, rect.Y, logo.width, logo.height), Color.white, sx, sy);
                }
                else if (Model.Page == FrontEndPage.Movies && Model.Movies != null && widget.Id.Value >= 53 && widget.Id.Value <= 57)
                {
                    MovieGalleryEntry row = Model.Movies.Entries[Model.Movies.FirstRow + widget.Id.Value - 53];
                    Quad(context, _assets.Image(row.Thumbnail), rect, Color.white, sx, sy);
                    if (state.Hovered) Quad(context, _assets.Image("Mov_Shots/M-Hilight"), rect, Color.white, sx, sy);
                }
                else if (widget.Draw == FrontEndDrawKind.Accept || widget.Draw == FrontEndDrawKind.Cancel)
                {
                    string name = widget.Draw == FrontEndDrawKind.Accept ? "Confirm" : "Cancel";
                    if (state.Pressed) name += "-Click";
                    else if (state.Hovered) name += "-Over";
                    Quad(context, _assets.Image(name), rect, state.Enabled ? Color.white : new Color(1, 1, 1, .35f), sx, sy);
                }
            }
            if (Model.Page == FrontEndPage.CampaignResults && _resultsBar != null) DrawResultsBar(context, sx, sy);
            if (Model.Page == FrontEndPage.Movies && _movieBar != null) DrawResultsBar(context, sx, sy, _movieBar);
            DrawOptions(context, sx, sy);
        }
        private Texture2D BriefingImage(CampaignBriefing briefing, FrontEndBriefingImage image)
        {
            if (_missingImages.Contains((briefing.Slot, image))) return null;
            try
            {
                return image switch
                {
                    FrontEndBriefingImage.FirstShot => _assets.ObjectiveShot(briefing.Slot, CampaignObjectiveShot.First),
                    FrontEndBriefingImage.SecondShot => _assets.ObjectiveShot(briefing.Slot, CampaignObjectiveShot.Second),
                    _ => _assets.Image(FrontEndBriefingImages.ArtName(image)),
                };
            }
            catch (System.IO.FileNotFoundException) { _missingImages.Add((briefing.Slot, image)); return null; }
        }
        /// <summary>0x00551BA0: each part's glow frame, then the up arrow, track and down arrow over them, then
        /// the thumb cut from its art's row 8, as wide as the bar (clipped to the art).</summary>
        private void DrawResultsBar(MeshGenerationContext context, float sx, float sy, FrontEndScrollBar bar = null) =>
            DrawFrontEndBar(context, bar ?? _resultsBar, sx, sy);
        private void DrawFrontEndBar(MeshGenerationContext context, FrontEndScrollBar bar, float sx, float sy)
        {
            bar = bar ?? _resultsBar;
            string[] art = { FrontEndScrollBar.UpArrowArt, FrontEndScrollBar.DownArrowArt, FrontEndScrollBar.TrackArt };
            for (int part = 0; part < art.Length; part++)
            {
                int frame = bar.GlowFrame(part);
                if (frame < 0) continue;
                Texture2D glow = ScrollGlow(art[part])[frame];
                FrontEndPoint origin = bar.ArtOrigin(part);
                Quad(context, glow, new FrontEndRect(origin.X, origin.Y, glow.width, glow.height), Color.white, sx, sy);
            }
            const int pad = FrontEndScrollBar.GlowPadding;
            for (int part = 0; part < art.Length; part++)
            {
                Texture2D image = _assets.Image(art[part]);
                FrontEndPoint origin = bar.ArtOrigin(part);
                Quad(context, image, new FrontEndRect(origin.X + pad, origin.Y + pad, image.width, image.height), Color.white, sx, sy);
            }
            Texture2D thumb = _assets.Image(FrontEndScrollBar.ThumbArt);
            FrontEndRect box = bar.ThumbRect();
            float width = Mathf.Min(box.Width, thumb.width);
            float height = Mathf.Min(box.Height, thumb.height - FrontEndScrollBar.ThumbArtTop);
            float right = width / thumb.width, top = 1 - (float)FrontEndScrollBar.ThumbArtTop / thumb.height;
            float bottom = top - height / thumb.height;
            Quad(context, thumb, new FrontEndRect(box.X, box.Y, width, height), Color.white, sx, sy,
                new Vector2(0, top), new Vector2(right, top), new Vector2(right, bottom), new Vector2(0, bottom));
        }
        /// <summary>0x0054FD20's frames 0–3: the art padded by 6 pixels a side, its glow (0x005524F0) at ¼ to full.</summary>
        private Texture2D[] ScrollGlow(string name)
        {
            if (_scrollGlow.TryGetValue(name, out Texture2D[] frames)) return frames;
            Texture2D image = _assets.Image(name);
            const int pad = FrontEndScrollBar.GlowPadding;
            int width = image.width + 2 * pad, height = image.height + 2 * pad;
            Color32[] colours = image.GetPixels32();
            var padded = new byte[width * height * 4];
            for (int y = 0; y < image.height; y++)
                for (int x = 0; x < image.width; x++)
                {
                    Color32 colour = colours[(image.height - 1 - y) * image.width + x];
                    int at = ((y + pad) * width + x + pad) * 4;
                    padded[at] = colour.r; padded[at + 1] = colour.g; padded[at + 2] = colour.b; padded[at + 3] = colour.a;
                }
            RgbaImage full = FrontEndLabelGlow.Create(new RgbaImage(width, height, padded));
            frames = new Texture2D[4];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                byte[] pixels = (byte[])full.Pixels.Clone();
                for (int at = 3; at < pixels.Length; at += 4) pixels[at] = (byte)(pixels[at] * (frame + 1) / 4);
                frames[frame] = MinimapFrameVisual.Decode(new RgbaImage(width, height, pixels));
            }
            _scrollGlow.Add(name, frames);
            return frames;
        }
        private static Color32 LabelColour(FrontEndWidget widget, bool hovered, byte alpha)
        {
            if (widget.Draw == FrontEndDrawKind.Title || widget.Draw == FrontEndDrawKind.BriefingSection && widget.DrawArgument2 == 1) return new Color32(255, 176, 51, alpha);
            if (widget.Draw == FrontEndDrawKind.Label)
                return hovered ? new Color32(255, 255, 255, alpha) : new Color32(191, 191, 191, alpha);
            switch (widget.DrawArgument2 >> 16)
            {
                case 1: return new Color32(253, 242, 167, alpha);
                case 2: return new Color32(255, 176, 51, alpha);
                case 3: return new Color32(191, 191, 191, alpha);
                case 4: return new Color32(255, 168, 0, alpha);
                case 5: return new Color32(255, 84, 0, alpha);
                case 6: return new Color32(255, 0, 0, alpha);
                default: return new Color32(255, 255, 255, alpha);
            }
        }
        private void DrawLabel(MeshGenerationContext context, LabelData label, FrontEndRect ink, Color32 tint, float sx, float sy)
        {
            if (!label.UsesFont)
            { LabelQuad(context, label, label.Bitmap, ink, tint, sx, sy, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), Vector2.zero); return; }
            Texture atlas = _fontAtlas;
            for (int i = 0; i < label.Geometry.Glyphs.Count; i++)
            {
                FrontEndGlyph glyph = label.Geometry.Glyphs[i]; CharacterInfo info = label.FontGlyphs[i];
                var rect = new FrontEndRect(ink.X + glyph.InkBounds.X - label.Geometry.InkBounds.X,
                    ink.Y + glyph.InkBounds.Y - label.Geometry.InkBounds.Y, glyph.InkBounds.Width, glyph.InkBounds.Height);
                LabelQuad(context, label, atlas, rect, tint, sx, sy, info.uvTopLeft, info.uvTopRight, info.uvBottomRight, info.uvBottomLeft);
            }
        }
        private void LabelQuad(MeshGenerationContext context, LabelData label, Texture texture, FrontEndRect rect,
            Color32 tint, float sx, float sy, Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl)
        {
            if (!label.Clip) { Quad(context, texture, rect, tint, sx, sy, tl, tr, br, bl); return; }
            FrontEndRect clipped = rect.Intersect(LabelBox(label.Widget));
            if (clipped.Width <= 0 || clipped.Height <= 0) return;
            float x0 = (clipped.X - rect.X) / rect.Width, x1 = (clipped.X + clipped.Width - rect.X) / rect.Width;
            float y0 = (clipped.Y - rect.Y) / rect.Height, y1 = (clipped.Y + clipped.Height - rect.Y) / rect.Height;
            Vector2 Uv(float x, float y) => Vector2.Lerp(Vector2.Lerp(tl, tr, x), Vector2.Lerp(bl, br, x), y);
            Quad(context, texture, clipped, tint, sx, sy, Uv(x0, y0), Uv(x1, y0), Uv(x1, y1), Uv(x0, y1));
        }
        private static void Quad(MeshGenerationContext context, Texture texture, FrontEndRect rect, Color32 tint, float sx, float sy)
            => Quad(context, texture, rect, tint, sx, sy, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), Vector2.zero);
        private static void Quad(MeshGenerationContext context, Texture texture, FrontEndRect rect, Color32 tint, float sx, float sy,
            Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl)
        {
            if (texture == null || rect.Width <= 0 || rect.Height <= 0) return;
            MeshWriteData mesh = context.Allocate(4, 6, texture);
            Vertex Write(float x, float y, Vector2 source) => new Vertex { position = new Vector3(x * sx, y * sy, Vertex.nearZ),
                tint = tint, uv = source };
            mesh.SetNextVertex(Write(rect.X, rect.Y, tl)); mesh.SetNextVertex(Write(rect.X + rect.Width, rect.Y, tr));
            mesh.SetNextVertex(Write(rect.X + rect.Width, rect.Y + rect.Height, br)); mesh.SetNextVertex(Write(rect.X, rect.Y + rect.Height, bl));
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
            mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
        }
        private static RgbaImage Raster(BitmapFont font, FrontEndLabelGeometry geometry, int padding)
        {
            int width = Math.Max(1, (int)geometry.InkBounds.Width) + 2 * padding;
            int height = Math.Max(1, (int)geometry.InkBounds.Height) + 2 * padding;
            var pixels = new byte[width * height * 4];
            foreach (FrontEndGlyph item in geometry.Glyphs)
            {
                BitmapGlyph glyph = font.Find(item.Character);
                for (int y = 0; y < item.InkBounds.Height; y++) for (int x = 0; x < item.InkBounds.Width; x++)
                {
                    int targetX = (int)(item.InkBounds.X - geometry.InkBounds.X) + x + padding;
                    int targetY = (int)(item.InkBounds.Y - geometry.InkBounds.Y) + y + padding;
                    byte alpha = (byte)(glyph.Pixels[((int)item.SourceInkBounds.Y + y) * glyph.Width + (int)item.SourceInkBounds.X + x] * 17);
                    int at = (targetY * width + targetX) * 4;
                    // Ink boxes overlap (a j reaches back under the letter before it): keep the stronger ink.
                    if (alpha <= pixels[at + 3]) continue;
                    pixels[at] = 255; pixels[at + 1] = 255; pixels[at + 2] = 255; pixels[at + 3] = alpha;
                }
            }
            return new RgbaImage(width, height, pixels);
        }
        private static Texture2D Bitmap(BitmapFont font, FrontEndLabelGeometry geometry) => MinimapFrameVisual.Decode(Raster(font, geometry, 0));
        private static Texture2D[] Glow(BitmapFont font, FrontEndLabelGeometry geometry)
        {
            RgbaImage full = FrontEndLabelGlow.Create(Raster(font, geometry, 16));
            var frames = new Texture2D[4];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                byte[] pixels = (byte[])full.Pixels.Clone();
                for (int at = 3; at < pixels.Length; at += 4) pixels[at] = (byte)(pixels[at] * (frame + 1) / 4);
                frames[frame] = MinimapFrameVisual.Decode(new RgbaImage(full.Width, full.Height, pixels));
            }
            return frames;
        }
        private static void DestroyLabel(LabelData label)
        {
            UnityEngine.Object.Destroy(label.Bitmap);
            if (label.Glow != null) foreach (Texture2D glow in label.Glow) UnityEngine.Object.Destroy(glow);
        }
        public void Dispose()
        {
            Font.textureRebuilt -= FontRebuilt;
            Model.Dispose();
            foreach (LabelData label in _labels.Values) DestroyLabel(label);
            foreach (Texture2D[] frames in _scrollGlow.Values) foreach (Texture2D frame in frames) UnityEngine.Object.Destroy(frame);
            if (_fontAtlas != null) { _fontAtlas.Release(); UnityEngine.Object.Destroy(_fontAtlas); }
            if (_fontMaterial != null) UnityEngine.Object.Destroy(_fontMaterial);
            _assets.Dispose(); UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_panel);
        }
    }
}
