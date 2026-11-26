// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;

namespace DK2.Presentation;

public sealed partial class FrontEndMenuHud
{
    private static readonly HudImage GraphicsSlider = "3DFE\\3DGraphicsHSlider", SoundSlider = "3DFE\\3DSoundHSlider",
        ControlSlider = "3DFE\\3DControlHSlider", SliderButton = "3DFE\\3DSliderButton",
        ButtonOn = "3DFE\\3DsmlButtonOn", ButtonOff = "3DFE\\3DsmlButtonOff";
    private const string SliderButtonArt = "3DFE\\3DSliderButton";

    private GameSettings? _shownOptions;
    private GameAction? _shownCapturing;
    private int _keysFirst, _shownKeysFirst;
    private FrontEndScrollBar? _keysBar;
    private bool _optionHeld;
    private ushort? _dragSlider;

    private FrontEndWidget? WidgetById(ushort id)
    {
        foreach (FrontEndWidget widget in _widgets)
        {
            if (widget.Id.Value == id)
            {
                return widget;
            }
        }

        return null;
    }

    /// <summary>The settings pages' input: the key list's wheel, rows and bar, and a slider's grab and drag. Pressing a
    /// key to bind it is the adapter's, since the keyboard is the engine's.</summary>
    private void UpdateOptionsPointer(FrontEndPointer pointer, int wheel, double now)
    {
        OptionsPagesController? options = Model.Options;
        if (options == null || Model.Page != options.Page)
        {
            return;
        }

        bool press = pointer.PrimaryHeld && !_optionHeld; _optionHeld = pointer.PrimaryHeld;
        var point = new FrontEndPoint(pointer.X, pointer.Y);
        if (Model.Page == FrontEndPage.Controls && _keysBar != null)
        {
            FrontEndRect box = LabelBox(WidgetById(150)!);
            if (box.Contains(point))
            {
                for (int i = 0; i < Math.Abs(wheel); i++)
                {
                    _keysBar.Bar.Step(-Math.Sign(wheel));
                }

                _keysFirst = _keysBar.Bar.Position;
                if (press)
                {
                    int row = _keysFirst + Math.Clamp((int)((point.Y - box.Y) / (box.Height / 5)), 0, 4);
                    options.BeginKeyCapture(KeyboardSetup.Actions[row].Action);
                }
            }
            _keysBar.Update(point, pointer.PrimaryHeld, now); _keysBar.Frame(_keysBar.HitTest(point));
            _keysFirst = _keysBar.Bar.Position;
        }
        if (!pointer.PrimaryHeld) { _dragSlider = null; options.EndSliderDrag(); return; }
        for (int i = 0; i < _widgets.Count + _resultWidgets.Count; i++)
        {
            FrontEndWidget widget = i < _widgets.Count ? _widgets[i] : _resultWidgets[i - _widgets.Count];
            ushort id = widget.Id.Value;
            if (!options.Supports(id) || options.Range(id).Maximum <= 1 || id == 133)
            {
                continue;
            }

            FrontEndRect box = LabelBox(widget);
            if (_dragSlider != id && (!press || !box.Inflate(5).Contains(point)))
            {
                continue;
            }

            (int lo, int hi) = options.Range(id);
            // Only a press on the thumb grabs it (state 3) and starts the test sound; the track and arrows are other states, silent.
            if (_dragSlider != id)
            {
                (int thumbWidth, int thumbHeight) = RequireSize(SliderButtonArt);
                if (SliderThumb.Rect(box, thumbWidth, thumbHeight, options.Value(id), lo, hi).Contains(point))
                {
                    options.BeginSliderDrag(id);
                }
            }
            _dragSlider = id;
            float t = Math.Clamp((point.X - box.X) / box.Width, 0f, 1f);
            options.SetWidget(id, (int)Math.Round(lo + t * (hi - lo)));
        }
    }

    private void RebuildOptionLabels()
    {
        _shownOptions = Model.Options?.Pending; _shownKeysFirst = _keysFirst;
        _shownCapturing = Model.Options?.CapturingAction;
        if (Model.Options == null || Model.Page != Model.Options.Page)
        {
            return;
        }

        foreach (FrontEndWidget widget in _widgets)
        {
            ushort id = widget.Id.Value;
            string? text = id == 133 ? _sources.Text(new uint[] { 0, 1443, 281, 1442 }[Model.Options.Pending.SoundQuality])
                : id == 135 ? _sources.Text(Model.Options.Pending.Headphones ? 1465u : 111u)
                : id == 676 || id == 677 || id == 678 ? _sources.Text(Model.Options.Value(id) == 0 ? 110u : 109u) : null;
            if (text == null)
            {
                continue;
            }

            AddLabel(widget, text, FrontEndLabelStyles.FontFor(FrontEndDrawKind.OptionText),
                FrontEndTextAlignment.Left, FrontEndVerticalAlignment.Centre, false);
        }
        if (Model.Page == FrontEndPage.Controls)
        {
            FrontEndWidget template = WidgetById(150)!;
            if (_keysBar == null) { _keysBar = FrontEndScrollBar.Beside(template.DrawRect, 0); _keysBar.Bar.Arm(KeyboardSetup.Actions.Count - 5); }
            for (int i = 0; i < 5 && _keysFirst + i < KeyboardSetup.Actions.Count; i++)
            {
                KeyboardAction row = KeyboardSetup.Actions[_keysFirst + i];
                GameKeyBinding binding = Model.Options.Pending.Binding(row.Action);
                string[] text = [ _sources.Text(row.TextId), binding.Modifiers == KeyModifiers.None ? "" : binding.Modifiers.ToString(),
                    Model.Options.CapturingAction == row.Action ? _sources.Text(1475)
                        : binding.ScanCode == 0 ? "-" : _keyName(binding.ScanCode) ];
                FrontEndRect raw = template.DrawRect;
                float[] start = [0, 306 * 4, 454 * 4, raw.Width];
                for (int column = 0; column < 3; column++)
                {
                    FrontEndWidget widget = template with { Id = new FrontEndWidgetId((ushort)(800 + column * 5 + i)),
                        Draw = FrontEndDrawKind.PlainText, TextId = 0, Primary = new FrontEndAction(FrontEndActionKind.None, default),
                        DrawRect = new FrontEndRect(raw.X + start[column], raw.Y + i * raw.Height / 5,
                            start[column + 1] - start[column], raw.Height / 5) };
                    _resultWidgets.Add(widget);
                    AddLabel(widget, text[column], new FrontEndFont(FrontEndFontFamily.FrontEnd, 14),
                        FrontEndTextAlignment.Left, FrontEndVerticalAlignment.Centre, true);
                }
            }
        }

        if (Model.Page == FrontEndPage.Graphics)
        {
            FrontEndWidget template = WidgetById(125)!;
            FrontEndWidget scale = template with { Id = new FrontEndWidgetId(900), TextId = 0,
                DrawRect = new FrontEndRect(1616, 1300, 680, 48), Draw = FrontEndDrawKind.None };
            _resultWidgets.Add(scale);
            FrontEndWidget caption = template with { Id = new FrontEndWidgetId(901), TextId = 0,
                DrawRect = new FrontEndRect(80, 1260, 1300, 100), Draw = FrontEndDrawKind.PlainText };
            _resultWidgets.Add(caption);
            float fitting = _hudFit(Model.Options.Pending.UiScale);
            AddLabel(caption, $"HUD scale {Model.Options.Pending.UiScale}% (fits {(int)Math.Round(fitting)}%)",
                new FrontEndFont(FrontEndFontFamily.FrontEnd, 14), FrontEndTextAlignment.Left, FrontEndVerticalAlignment.Centre, true);
            FrontEndWidget unavailable = template with { Id = new FrontEndWidgetId(991), TextId = 0,
                DrawRect = new FrontEndRect(80, 1430, 2250, 80), Draw = FrontEndDrawKind.PlainText };
            _resultWidgets.Add(unavailable);
            AddLabel(unavailable, "Water, environment mapping and shadows are unavailable.",
                new FrontEndFont(FrontEndFontFamily.FrontEnd, 14), FrontEndTextAlignment.Left, FrontEndVerticalAlignment.Centre, true);
            FrontEndWidget note = template with { Id = new FrontEndWidgetId(990), TextId = 0,
                DrawRect = new FrontEndRect(80, 1540, 1900, 100), Draw = FrontEndDrawKind.PlainText };
            _resultWidgets.Add(note);
            AddLabel(note, "Set the display mode in the launcher.",
                new FrontEndFont(FrontEndFontFamily.FrontEnd, 14), FrontEndTextAlignment.Left, FrontEndVerticalAlignment.Centre, true);
        }
    }

    private void DrawOptions(HudFrame frame)
    {
        OptionsPagesController? options = Model.Options;
        if (options == null || Model.Page != options.Page)
        {
            return;
        }

        if (Model.Page == FrontEndPage.Controls && _keysBar != null)
        {
            DrawBar(frame, _keysBar);
        }

        for (int i = 0; i < _widgets.Count + _resultWidgets.Count; i++)
        {
            FrontEndWidget widget = i < _widgets.Count ? _widgets[i] : _resultWidgets[i - _widgets.Count];
            ushort id = widget.Id.Value;
            bool unavailable = id == 116 || id == 118 || id == 128;
            if (!options.Supports(id) && !unavailable)
            {
                continue;
            }

            HudColor tint = unavailable ? new HudColor(255, 255, 255, Faded) : HudColor.White;
            FrontEndRect box = LabelBox(widget);
            (int lo, int hi) = options.Range(id);
            if (hi > 1 && id != 133)
            {
                HudImage art = Model.Page == FrontEndPage.Graphics ? GraphicsSlider
                    : Model.Page == FrontEndPage.Sound ? SoundSlider : ControlSlider;
                frame.Add(new HudDraw(HudDrawKind.Quad, art, Virtual(box), tint));
                (int width, int height) = RequireSize(SliderButtonArt);
                float x = box.X + (box.Width - width) * (options.Value(id) - lo) / (hi - lo);
                frame.Add(new HudDraw(HudDrawKind.Quad, SliderButton, Virtual(new FrontEndRect(x, box.Y + (box.Height - height) / 2, width, height)), tint));
            }
            else if ((id != 133 && id != 135 && id < 676) || id == 674)
            {
                frame.Add(new HudDraw(HudDrawKind.Quad, options.Value(id) != 0 ? ButtonOn : ButtonOff, Virtual(box), tint));
            }
        }
    }
}
