// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    public static class ClassicHudScaling
    {
        public static HudScaleLayout Layout(int percent, int width = 0, int height = 0) =>
            new HudScaleLayout(width > 0 ? width : Screen.width, height > 0 ? height : Screen.height, percent);
        public static void Apply(PanelSettings panel, VisualElement root, HudScaleLayout layout, bool bottomDocked, float referenceHeight = 0)
        {
            panel.scale = layout.Factor;
            float height = referenceHeight > 0 ? referenceHeight : layout.Height;
            root.style.top = bottomDocked ? height / layout.Factor - height : 0;
            root.style.height = height;
            root.style.width = layout.LogicalWidth * (referenceHeight > 0 ? referenceHeight / layout.Height : 1);
        }
        public static Vector2 LocalPointer(Vector3 screen, HudScaleLayout layout, bool bottomDocked)
        {
            var p = layout.ToLocalTop(new System.Numerics.Vector2(screen.x, layout.Height - screen.y), bottomDocked);
            return new Vector2(p.X, p.Y);
        }
        public static Vector2 PanelPointer(VisualElement root, Vector3 screen, int height) =>
            RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, height - screen.y));
    }
}
