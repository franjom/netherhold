// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation.Commands;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Unity glue for <see cref="BottomPanelHud"/> (ADR-0805): one UI Toolkit panel holding a
    /// <see cref="HudCanvas"/>, pointer events forwarded in virtual units. The panel's DK2 behaviour is the model's.</summary>
    public sealed class ClassicBottomPanel : IDisposable, IPointerGestureHolder
    {
        private const float Tall = 1920f;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly HudCanvas _canvas;
        private readonly IClassicBottomPanelAssets _assets;
        private readonly BottomPanelHud _hud;
        private readonly HudFrame _frame = new HudFrame();
        private Vector2Int _viewport;
        private int _pointer = -1;

        public ClassicBottomPanel(Transform parent, IClassicBottomPanelAssets assets, BottomPanelView view)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _hud = new BottomPanelHud(assets, view ?? throw new ArgumentNullException(nameof(view)));
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 12;
            _owner = new GameObject("Keeper bottom panel");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _canvas = new HudCanvas(assets.Image, (x, y) => _hud.HasPointerCapture || _hud.HitAction(x, y) >= 0);
            _root.Add(_canvas);
            _canvas.RegisterCallback<PointerDownEvent>(PointerDown);
            _canvas.RegisterCallback<PointerUpEvent>(PointerUp);
            _canvas.RegisterCallback<PointerMoveEvent>(evt => Move(evt.localPosition));
            _canvas.RegisterCallback<PointerEnterEvent>(evt => Move(evt.localPosition));
            _canvas.RegisterCallback<PointerLeaveEvent>(_ => _hud.PointerLeave());
            _canvas.RegisterCallback<PointerCancelEvent>(_ => CancelPointerGesture());
            _canvas.RegisterCallback<PointerCaptureOutEvent>(_ => { _pointer = -1; _hud.CaptureLost(); });
            _canvas.RegisterCallback<WheelEvent>(Wheel);
        }

        public event Action<BottomPanelSelection> Selected { add => _hud.Selected += value; remove => _hud.Selected -= value; }
        public event Action<BottomPanelChoice> SpecialChosen { add => _hud.SpecialChosen += value; remove => _hud.SpecialChosen -= value; }
        public event Action<RoomTypeId> NavigateRoom { add => _hud.NavigateRoom += value; remove => _hud.NavigateRoom -= value; }
        public event Action<byte> NavigateDoor { add => _hud.NavigateDoor += value; remove => _hud.NavigateDoor -= value; }
        public event Action<byte> NavigateTrap { add => _hud.NavigateTrap += value; remove => _hud.NavigateTrap -= value; }
        public event Action<GuiButtonTarget> GuiButton { add => _hud.GuiButton += value; remove => _hud.GuiButton -= value; }
        public event Action<bool> CollapsedChanged { add => _hud.CollapsedChanged += value; remove => _hud.CollapsedChanged -= value; }

        public bool GamePaused { get => _hud.GamePaused; set => _hud.GamePaused = value; }
        public HudFlashTarget? FlashTarget { get => _hud.FlashTarget; set => _hud.FlashTarget = value; }
        public SoundRequests Sounds => _hud.Sounds;
        public bool Collapsed => _hud.Collapsed;
        public BottomPanelOpenSet OpenPanel => _hud.OpenPanel;
        public HoverTextSnapshot HoverText => _hud.HoverText;
        public PanelSettings Panel => _settings;
        public bool HasPointerCapture => _hud.HasPointerCapture;
        public int UiScale { get; set; } = 100;
        public BottomPanelRect? PanelArea(BottomPanelCategory category) => _hud.PanelArea(category);
        public void SelectCategory(BottomPanelCategory category) { _hud.SelectCategory(category); SyncCapture(); }
        /// <summary>The panel as a level starts (<see cref="BottomPanelHud.OpenAtLevelStart"/>).</summary>
        public void OpenAtLevelStart() { _hud.OpenAtLevelStart(); SyncCapture(); }

        private HudScaleLayout ScaleLayout => ClassicHudScaling.Layout(UiScale, _viewport.x, _viewport.y);
        private int Height => _viewport.y > 0 ? _viewport.y : Screen.height;

        public void SetVisibility(HudVisibility visibility)
        {
            _hud.SetVisibility(visibility); SyncCapture();
            _root.style.display = visibility.ShowNormalHud ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public bool ContainsPointer(Vector3 screenPosition)
        {
            Vector2 local = ClassicHudScaling.LocalPointer(screenPosition, ScaleLayout, true) * (Tall / Height);
            return _hud.ContainsPointer(local.x, local.y);
        }

        public void CancelPointerGesture() { _hud.CancelPointerGesture(); SyncCapture(); }

        public void Draw(Vector2Int viewport = default)
        {
            _viewport = viewport;
            HudScaleLayout layout = ScaleLayout;
            ClassicHudScaling.Apply(_settings, _root, layout, true);
            float scale = Height / Tall;
            _frame.Clear();
            _hud.Build(Mathf.RoundToInt(layout.LogicalWidth) / scale, (long)(Time.unscaledTimeAsDouble * 1000), _frame);
            _canvas.Present(_frame, scale, layout.Factor);
            _hud.Repick();
        }

        /// <summary>The strip's area in panel-local pixels.</summary>
        public Rect Bounds => Local(_hud.Bounds);
        public Rect ReaperBounds => Local(_hud.ReaperBounds);
        private Rect Local(HudRect r)
        {
            float s = Height / Tall;
            return new Rect(r.X * s, r.Y * s, r.Width * s, r.Height * s);
        }

        private void PointerDown(PointerDownEvent evt)
        {
            Vector2 at = _canvas.ToVirtual(evt.localPosition);
            if (!_hud.PointerDown(at.x, at.y, evt.button)) return;
            _pointer = evt.pointerId; _canvas.CapturePointer(evt.pointerId); evt.StopPropagation();
        }

        private void PointerUp(PointerUpEvent evt)
        {
            if (_pointer != evt.pointerId) return;
            Vector2 at = _canvas.ToVirtual(evt.localPosition);
            try
            {
                if (_hud.PointerUp(at.x, at.y, evt.button, evt.shiftKey, evt.ctrlKey, evt.altKey)) evt.StopPropagation();
            }
            finally
            {
                // Released even when a click handler throws, so Unity never holds a capture the model dropped.
                SyncCapture();
            }
            // Hover follows a release away from the pressed widget at once, not at the next move.
            _hud.PointerMove(at.x, at.y);
        }

        private void Move(Vector3 local)
        {
            Vector2 at = _canvas.ToVirtual(local);
            _hud.PointerMove(at.x, at.y);
        }

        private void Wheel(WheelEvent evt)
        {
            Vector2 at = _canvas.ToVirtual(evt.localMousePosition);
            if (_hud.Wheel(at.x, at.y, evt.delta.y)) evt.StopPropagation();
        }

        /// <summary>Releases Unity's capture once the model has let go of the press.</summary>
        private void SyncCapture()
        {
            if (_hud.HasPointerCapture || _pointer < 0) return;
            int pointer = _pointer; _pointer = -1;
            if (_canvas.HasPointerCapture(pointer)) _canvas.ReleasePointer(pointer);
        }

        public void Dispose()
        {
            CancelPointerGesture();
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_settings); UnityEngine.Object.Destroy(_owner);
            if (_assets is IDisposable disposable) disposable.Dispose();
        }
    }
}
