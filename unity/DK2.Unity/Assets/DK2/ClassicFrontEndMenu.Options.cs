// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Linq;
using DK2.Data.Settings;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    public sealed partial class ClassicFrontEndMenu
    {
        private GameSettings _shownOptions;
        private GameAction? _shownCapturing;
        private int _keysFirst, _shownKeysFirst;
        private FrontEndScrollBar _keysBar;
        private bool _optionHeld;
        private ushort? _dragSlider;
        private void UpdateOptionsPointer(FrontEndPointer pointer)
        {
            OptionsPagesController options = Model.Options;
            if (options == null || Model.Page != options.Page) return;
            if (options.CapturingAction != null)
                foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
                    if ((int)key < (int)KeyCode.Mouse0 && Input.GetKeyDown(key))
                        options.CaptureKey(ClassicKeyBindings.ScanCode(key), ClassicKeyBindings.Modifiers);
            bool press = pointer.PrimaryHeld && !_optionHeld; _optionHeld = pointer.PrimaryHeld;
            var point = new FrontEndPoint(pointer.X, pointer.Y);
            if (Model.Page == FrontEndPage.Controls)
            {
                FrontEndWidget list = Model.Widgets.First(w => w.Id.Value == 150);
                FrontEndRect box = LabelBox(list);
                if (box.Contains(point))
                {
                    int wheel = Mathf.RoundToInt(Input.mouseScrollDelta.y);
                    for (int i = 0; i < Math.Abs(wheel); i++) _keysBar.Bar.Step(-Math.Sign(wheel));
                    _keysFirst = _keysBar.Bar.Position;
                    if (press)
                    {
                        int row = _keysFirst + Mathf.Clamp((int)((point.Y - box.Y) / (box.Height / 5)), 0, 4);
                        options.BeginKeyCapture(KeyboardSetup.Actions[row].Action);
                    }
                }
                _keysBar.Update(point, pointer.PrimaryHeld, Time.unscaledTimeAsDouble); _keysBar.Frame(_keysBar.HitTest(point));
                _keysFirst = _keysBar.Bar.Position;
            }
            if (!pointer.PrimaryHeld) { _dragSlider = null; return; }
            foreach (FrontEndWidget widget in Model.Widgets.Concat(_resultWidgets))
            {
                ushort id = widget.Id.Value;
                if (!options.Supports(id) || options.Range(id).Maximum <= 1 || id == 133) continue;
                FrontEndRect box = LabelBox(widget);
                if (_dragSlider != id && (!press || !box.Inflate(5).Contains(point))) continue;
                _dragSlider = id;
                (int lo, int hi) = options.Range(id);
                float t = Mathf.Clamp01((point.X - box.X) / box.Width);
                options.SetWidget(id, Mathf.RoundToInt(lo + t * (hi - lo)));
            }
        }
        private void RebuildOptionLabels()
        {
            _shownOptions = Model.Options?.Pending; _shownKeysFirst = _keysFirst;
            _shownCapturing = Model.Options?.CapturingAction;
            if (Model.Options == null || Model.Page != Model.Options.Page) return;
            foreach (FrontEndWidget widget in Model.Widgets)
            {
                ushort id = widget.Id.Value;
                string text = id == 133 ? _assets.Text(new uint[] { 0, 1443, 281, 1442 }[Model.Options.Pending.SoundQuality])
                    : id == 135 ? _assets.Text(Model.Options.Pending.Headphones ? 1465u : 111u)
                    : id == 676 || id == 677 || id == 678 ? _assets.Text(Model.Options.Value(id) == 0 ? 110u : 109u) : null;
                if (text == null) continue;
                AddLabel(widget, text, FrontEndLabelStyles.FontFor(FrontEndDrawKind.OptionText),
                    FrontEndTextAlignment.Left, FrontEndVerticalAlignment.Centre, false);
            }
            if (Model.Page == FrontEndPage.Controls)
            {
                FrontEndWidget template = Model.Widgets.First(w => w.Id.Value == 150);
                if (_keysBar == null) { _keysBar = FrontEndScrollBar.Beside(template.DrawRect, 0); _keysBar.Bar.Arm(KeyboardSetup.Actions.Count - 5); }
                for (int i = 0; i < 5 && _keysFirst + i < KeyboardSetup.Actions.Count; i++)
                {
                    KeyboardAction row = KeyboardSetup.Actions[_keysFirst + i];
                    GameKeyBinding binding = Model.Options.Pending.Binding(row.Action);
                    string[] text = { _assets.Text(row.TextId), binding.Modifiers == KeyModifiers.None ? "" : binding.Modifiers.ToString(),
                        Model.Options.CapturingAction == row.Action ? _assets.Text(1475)
                            : binding.ScanCode == 0 ? "-" : ClassicKeyBindings.Key(binding.ScanCode).ToString() };
                    FrontEndRect raw = template.DrawRect;
                    float[] start = { 0, 306 * 4, 454 * 4, raw.Width };
                    for (int column = 0; column < 3; column++)
                    {
                        var widget = template with { Id = new FrontEndWidgetId((ushort)(800 + column * 5 + i)),
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
                FrontEndWidget template = Model.Widgets.First(w => w.Id.Value == 125);
                var scale = template with { Id = new FrontEndWidgetId(900), TextId = 0,
                    DrawRect = new FrontEndRect(1616, 1300, 680, 48), Draw = FrontEndDrawKind.None };
                _resultWidgets.Add(scale);
                var caption = template with { Id = new FrontEndWidgetId(901), TextId = 0,
                    DrawRect = new FrontEndRect(80, 1260, 1300, 100), Draw = FrontEndDrawKind.PlainText };
                _resultWidgets.Add(caption);
                var fitting = ClassicHudScaling.Layout(Model.Options.Pending.UiScale);
                AddLabel(caption, $"HUD scale {Model.Options.Pending.UiScale}% (fits {Mathf.RoundToInt(fitting.EffectivePercent)}%)",
                    new FrontEndFont(FrontEndFontFamily.FrontEnd, 14), FrontEndTextAlignment.Left, FrontEndVerticalAlignment.Centre, true);
                var unavailable = template with { Id = new FrontEndWidgetId(991), TextId = 0,
                    DrawRect = new FrontEndRect(80, 1430, 2250, 80), Draw = FrontEndDrawKind.PlainText };
                _resultWidgets.Add(unavailable);
                AddLabel(unavailable, "Water, environment mapping and shadows are unavailable.",
                    new FrontEndFont(FrontEndFontFamily.FrontEnd, 14), FrontEndTextAlignment.Left, FrontEndVerticalAlignment.Centre, true);
                var note = template with { Id = new FrontEndWidgetId(990), TextId = 0,
                    DrawRect = new FrontEndRect(80, 1540, 1900, 100), Draw = FrontEndDrawKind.PlainText };
                _resultWidgets.Add(note);
                AddLabel(note, "Unity controls display mode and hardware acceleration.",
                    new FrontEndFont(FrontEndFontFamily.FrontEnd, 14), FrontEndTextAlignment.Left, FrontEndVerticalAlignment.Centre, true);
            }
        }
        private void DrawOptions(MeshGenerationContext context, float sx, float sy)
        {
            OptionsPagesController options = Model.Options;
            if (options == null || Model.Page != options.Page) return;
            if (Model.Page == FrontEndPage.Controls && _keysBar != null) DrawFrontEndBar(context, _keysBar, sx, sy);
            foreach (FrontEndWidget widget in Model.Widgets.Concat(_resultWidgets))
            {
                ushort id = widget.Id.Value;
                bool unavailable = id == 116 || id == 118 || id == 128;
                if (!options.Supports(id) && !unavailable) continue;
                Color tint = unavailable ? new Color(1, 1, 1, .35f) : Color.white;
                FrontEndRect box = LabelBox(widget);
                (int lo, int hi) = options.Range(id);
                if (hi > 1 && id != 133)
                {
                    string art = Model.Page == FrontEndPage.Graphics ? "3DFE\\3DGraphicsHSlider"
                        : Model.Page == FrontEndPage.Sound ? "3DFE\\3DSoundHSlider" : "3DFE\\3DControlHSlider";
                    Quad(context, _assets.Image(art), box, tint, sx, sy);
                    Texture2D thumb = _assets.Image("3DFE\\3DSliderButton");
                    float x = box.X + (box.Width - thumb.width) * (options.Value(id) - lo) / (hi - lo);
                    Quad(context, thumb, new FrontEndRect(x, box.Y + (box.Height - thumb.height) / 2, thumb.width, thumb.height), tint, sx, sy);
                }
                else if ((id != 133 && id != 135 && id < 676) || id == 674)
                {
                    string art = options.Value(id) != 0 ? "3DFE\\3DsmlButtonOn" : "3DFE\\3DsmlButtonOff";
                    Quad(context, _assets.Image(art), box, tint, sx, sy);
                }
            }
        }
    }
}
