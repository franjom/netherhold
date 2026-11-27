// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

public enum InGameOptionsPage : byte { None, Root = 25, Graphics, Sound, AdvancedSound, Controls, Keyboard, UserCameras, UserCameraEdit }
public enum InGameOptionsWidgetKind { Label, Navigate, Continue, Back, Slider, Value, KeyboardList, Unavailable }
public sealed record InGameOptionsWidget(int NativeIndex, uint TextId, FrontEndRect Bounds,
    InGameOptionsWidgetKind Kind, InGameOptionsPage Destination, FrontEndWidgetId Setting, bool IsTitle = false);

/// <summary>Native pages25..32. Rectangles use the existing1024x768 Options reference, including menu top180.</summary>
public static class ClassicGameOptionsLayout
{
    public static IReadOnlyDictionary<InGameOptionsPage, IReadOnlyList<InGameOptionsWidget>> Pages { get; } =
        new Dictionary<InGameOptionsPage, IReadOnlyList<InGameOptionsWidget>>
        {
            [InGameOptionsPage.Root] = new InGameOptionsWidget[]
            {
                new InGameOptionsWidget(0, 97u, new FrontEndRect(8f, 72f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), true),
                new InGameOptionsWidget(1, 96u, new FrontEndRect(8f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.Graphics, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(2, 95u, new FrontEndRect(512f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.Sound, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(3, 98u, new FrontEndRect(8f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.Controls, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(4, 1485u, new FrontEndRect(512f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameras, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(5, 142u, new FrontEndRect(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(6, 20u, new FrontEndRect(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
            },
            [InGameOptionsPage.Graphics] = new InGameOptionsWidget[]
            {
                new InGameOptionsWidget(0, 96u, new FrontEndRect(8f, 72f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), true),
                new InGameOptionsWidget(1, 135u, new FrontEndRect(512f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(2, 1u, new FrontEndRect(584f, 168f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new FrontEndWidgetId(126), false),
                new InGameOptionsWidget(3, 1444u, new FrontEndRect(8f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(4, 0u, new FrontEndRect(64f, 168f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(116), false),
                new InGameOptionsWidget(5, 1450u, new FrontEndRect(8f, 200f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(6, 0u, new FrontEndRect(64f, 232f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(118), false),
                new InGameOptionsWidget(7, 1445u, new FrontEndRect(512f, 200f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(8, 0u, new FrontEndRect(584f, 232f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new FrontEndWidgetId(128), false),
                new InGameOptionsWidget(9, 142u, new FrontEndRect(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(10, 96u, new FrontEndRect(8f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(11, 0u, new FrontEndRect(64f, 296f, 496f, 28.8f), InGameOptionsWidgetKind.Unavailable, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(12, 20u, new FrontEndRect(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Root, new FrontEndWidgetId(0), false),
            },
            [InGameOptionsPage.Sound] = new InGameOptionsWidget[]
            {
                new InGameOptionsWidget(0, 95u, new FrontEndRect(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), true),
                new InGameOptionsWidget(1, 1457u, new FrontEndRect(512f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(2, 0u, new FrontEndRect(584f, 200f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new FrontEndWidgetId(141), false),
                new InGameOptionsWidget(3, 0u, new FrontEndRect(568f, 168f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(678), false),
                new InGameOptionsWidget(4, 1459u, new FrontEndRect(512f, 232f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(5, 0u, new FrontEndRect(584f, 296f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new FrontEndWidgetId(143), false),
                new InGameOptionsWidget(6, 0u, new FrontEndRect(568f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(676), false),
                new InGameOptionsWidget(7, 1458u, new FrontEndRect(8f, 232f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(8, 0u, new FrontEndRect(80f, 296f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new FrontEndWidgetId(144), false),
                new InGameOptionsWidget(9, 0u, new FrontEndRect(64f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(677), false),
                new InGameOptionsWidget(10, 1460u, new FrontEndRect(8f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(11, 0u, new FrontEndRect(80f, 200f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new FrontEndWidgetId(139), false),
                new InGameOptionsWidget(12, 1464u, new FrontEndRect(8f, 328f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(13, 0u, new FrontEndRect(64f, 360f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(133), false),
                new InGameOptionsWidget(14, 1674u, new FrontEndRect(8f, 392f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(15, 0u, new FrontEndRect(64f, 424f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(135), false),
                new InGameOptionsWidget(16, 1472u, new FrontEndRect(512f, 392f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(17, 0u, new FrontEndRect(568f, 424f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(674), false),
                new InGameOptionsWidget(18, 1679u, new FrontEndRect(512f, 328f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(19, 0u, new FrontEndRect(568f, 360f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(137), false),
                new InGameOptionsWidget(20, 142u, new FrontEndRect(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(21, 20u, new FrontEndRect(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Root, new FrontEndWidgetId(0), false),
            },
            [InGameOptionsPage.AdvancedSound] = new InGameOptionsWidget[]
            {
                new InGameOptionsWidget(0, 1500u, new FrontEndRect(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), true),
                new InGameOptionsWidget(1, 20u, new FrontEndRect(8f, 512f, 1008f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Sound, new FrontEndWidgetId(0), false),
            },
            [InGameOptionsPage.Controls] = new InGameOptionsWidget[]
            {
                new InGameOptionsWidget(0, 98u, new FrontEndRect(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), true),
                new InGameOptionsWidget(1, 1469u, new FrontEndRect(8f, 136f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(2, 25u, new FrontEndRect(96f, 168f, 360f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new FrontEndWidgetId(154), false),
                new InGameOptionsWidget(3, 1468u, new FrontEndRect(8f, 200f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(4, 25u, new FrontEndRect(96f, 232f, 360f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new FrontEndWidgetId(158), false),
                new InGameOptionsWidget(5, 115u, new FrontEndRect(8f, 264f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(6, 50u, new FrontEndRect(96f, 296f, 360f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new FrontEndWidgetId(156), false),
                new InGameOptionsWidget(7, 2840u, new FrontEndRect(8f, 328f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(8, 0u, new FrontEndRect(64f, 360f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new FrontEndWidgetId(152), false),
                new InGameOptionsWidget(9, 1670u, new FrontEndRect(512f, 328f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(10, 0u, new FrontEndRect(568f, 360f, 496f, 28.8f), InGameOptionsWidgetKind.Unavailable, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(11, 1470u, new FrontEndRect(8f, 392f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(12, 0u, new FrontEndRect(64f, 424f, 496f, 70.4f), InGameOptionsWidgetKind.Unavailable, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(13, 1680u, new FrontEndRect(512f, 392f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(14, 0u, new FrontEndRect(568f, 424f, 496f, 28.8f), InGameOptionsWidgetKind.Unavailable, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(15, 117u, new FrontEndRect(8f, 464f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.Keyboard, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(16, 142u, new FrontEndRect(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(17, 20u, new FrontEndRect(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Root, new FrontEndWidgetId(0), false),
            },
            [InGameOptionsPage.Keyboard] = new InGameOptionsWidget[]
            {
                new InGameOptionsWidget(0, 117u, new FrontEndRect(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), true),
                new InGameOptionsWidget(1, 0u, new FrontEndRect(8f, 136f, 744f, 243.2f), InGameOptionsWidgetKind.KeyboardList, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(2, 142u, new FrontEndRect(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(3, 20u, new FrontEndRect(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Controls, new FrontEndWidgetId(0), false),
            },
            [InGameOptionsPage.UserCameras] = new InGameOptionsWidget[]
            {
                new InGameOptionsWidget(0, 1485u, new FrontEndRect(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), true),
                new InGameOptionsWidget(1, 1486u, new FrontEndRect(8f, 136f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameraEdit, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(2, 1487u, new FrontEndRect(8f, 200f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameraEdit, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(3, 1488u, new FrontEndRect(8f, 264f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameraEdit, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(4, 142u, new FrontEndRect(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(5, 20u, new FrontEndRect(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Root, new FrontEndWidgetId(0), false),
            },
            [InGameOptionsPage.UserCameraEdit] = new InGameOptionsWidget[]
            {
                new InGameOptionsWidget(0, 1490u, new FrontEndRect(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), true),
                new InGameOptionsWidget(1, 1686u, new FrontEndRect(8f, 136f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(2, 1489u, new FrontEndRect(8f, 464f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameras, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(3, 142u, new FrontEndRect(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new FrontEndWidgetId(0), false),
                new InGameOptionsWidget(4, 20u, new FrontEndRect(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.UserCameras, new FrontEndWidgetId(0), false),
            },
        };
    public static IReadOnlyList<InGameOptionsWidget> ForPage(InGameOptionsPage page)
    {
        return Pages[page];
    }
}
