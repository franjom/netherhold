// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Front-end overlay over the native station view: <see cref="FrontEndMenuHud"/> (ADR-0805) decides every
    /// label, colour, hover and scroll bar, and a <see cref="HudCanvas"/> draws its frame, as the shared bitmap or with the
    /// replacement TrueType font in the same glyph boxes. This class creates the panel, converts the pointer to
    /// 640 x 480 units and forwards the keyboard.</summary>
    public sealed partial class ClassicFrontEndMenu : IDisposable
    {
        private readonly IClassicFrontEndMenuAssets _assets;
        private readonly FrontEndMenuHud _hud;
        private readonly GameObject _owner;
        private readonly PanelSettings _panel;
        private readonly HudCanvas _canvas;
        private readonly HudFrame _frame = new HudFrame();
        private bool _useReplacement = true;
        public PanelSettings Panel => _panel;
        public FrontEndMenuController Model => _hud.Model;
        public bool UseReplacementFont
        {
            get => _useReplacement;
            set { _useReplacement = value; _canvas.ReplacementFont = value ? _assets.ReplacementFont : null; }
        }
        public bool UsingReplacementFont => _canvas.UsingReplacementFont;

        public ClassicFrontEndMenu(Transform parent, FrontEndSession session, IClassicFrontEndMenuAssets assets, CampaignMapController campaign = null, OptionsPagesController options = null)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _hud = new FrontEndMenuHud(session, assets, percent => ClassicHudScaling.Layout(percent).EffectivePercent,
                ClassicKeyNames.Name, campaign, options);
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panel.referenceResolution = new Vector2Int(FrontEndBox.Width, FrontEndBox.Height);
            _panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panel.match = Screen.width * 3 >= Screen.height * 4 ? 1 : 0;
            _panel.sortingOrder = 20;
            _owner = new GameObject("Front-end menu"); _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _panel;
            VisualElement root = document.rootVisualElement; root.pickingMode = PickingMode.Ignore;
            root.style.width = Length.Percent(100); root.style.height = Length.Percent(100);
            // DK2's menus are a 640x480 screen. Keep that box at one scale, centred, so a wide screen adds
            // margins (the front-end camera keeps the 4:3 vertical view, so the box still sits on the 3D scene).
            // The canvas is the box itself: one local unit is one 640x480 unit, 4 virtual units of the draw list.
            _canvas = new HudCanvas(assets.Load, null);
            _canvas.style.width = FrontEndBox.Width; _canvas.style.height = FrontEndBox.Height;
            _canvas.style.left = Length.Percent(50); _canvas.style.top = Length.Percent(50);
            _canvas.style.marginLeft = -FrontEndBox.Width / 2; _canvas.style.marginTop = -FrontEndBox.Height / 2;
            root.Add(_canvas);
            UseReplacementFont = true;
        }

        public FrontEndRect HitBounds(FrontEndWidget widget) => _hud.HitBounds(widget);
        public FrontEndRect LabelInkBounds(FrontEndWidgetId id) => _hud.LabelInkBounds(id);
        public void SetVisible(bool visible)
        {
            _owner.SetActive(visible);
            // A re-enabled document starts with an empty mesh; the unchanged frame has to be drawn again.
            if (visible) _canvas.Invalidate();
        }

        public void Refresh(bool pollInput = true)
        {
            _hud.Sync();
            int width = _panel.targetTexture != null ? _panel.targetTexture.width : Screen.width;
            int height = _panel.targetTexture != null ? _panel.targetTexture.height : Screen.height;
            bool sized = width > 0 && height > 0;
            // Match whichever side keeps the whole 640x480 box on screen.
            if (sized) _panel.match = width * 3 >= height * 4 ? 1 : 0;
            if (pollInput)
            {
                FrontEndPointer? pointer = null;
                int wheel = 0;
                if (sized)
                {
                    PollKeyCapture();
                    Vector3 mouse = Input.mousePosition;
                    FrontEndPoint at = FrontEndBox.ToUnits(mouse.x, height - mouse.y, width, height);
                    pointer = new FrontEndPointer(at.X, at.Y, Input.GetMouseButton(0));
                    wheel = Mathf.RoundToInt(Input.mouseScrollDelta.y);
                }
                _hud.Advance(pointer, wheel, Time.unscaledTimeAsDouble);
            }
            _hud.Sync();
            if (!sized) return;
            _frame.Clear();
            _hud.Build(_frame);
            _canvas.Present(_frame, 1f / FrontEndBox.VirtualPerUnit, FrontEndBox.Scale(width, height));
        }

        public void Dispose()
        {
            _hud.Dispose();
            _canvas.Dispose();
            _assets.Dispose(); UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_panel);
        }
    }
}
