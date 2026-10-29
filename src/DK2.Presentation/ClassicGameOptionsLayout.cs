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
                new(0, 97u, new(8f, 72f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), true),
                new(1, 96u, new(8f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.Graphics, new(0), false),
                new(2, 95u, new(512f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.Sound, new(0), false),
                new(3, 98u, new(8f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.Controls, new(0), false),
                new(4, 1485u, new(512f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameras, new(0), false),
                new(5, 142u, new(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new(0), false),
                new(6, 20u, new(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.None, new(0), false),
            },
            [InGameOptionsPage.Graphics] = new InGameOptionsWidget[]
            {
                new(0, 96u, new(8f, 72f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), true),
                new(1, 135u, new(512f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(2, 1u, new(584f, 168f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new(126), false),
                new(3, 1444u, new(8f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(4, 0u, new(64f, 168f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(116), false),
                new(5, 1450u, new(8f, 200f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(6, 0u, new(64f, 232f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(118), false),
                new(7, 1445u, new(512f, 200f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(8, 0u, new(584f, 232f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new(128), false),
                new(9, 142u, new(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new(0), false),
                new(10, 96u, new(8f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(11, 0u, new(64f, 296f, 496f, 28.8f), InGameOptionsWidgetKind.Unavailable, InGameOptionsPage.None, new(0), false),
                new(12, 20u, new(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Root, new(0), false),
            },
            [InGameOptionsPage.Sound] = new InGameOptionsWidget[]
            {
                new(0, 95u, new(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), true),
                new(1, 1457u, new(512f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(2, 0u, new(584f, 200f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new(141), false),
                new(3, 0u, new(568f, 168f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(678), false),
                new(4, 1459u, new(512f, 232f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(5, 0u, new(584f, 296f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new(143), false),
                new(6, 0u, new(568f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(676), false),
                new(7, 1458u, new(8f, 232f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(8, 0u, new(80f, 296f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new(144), false),
                new(9, 0u, new(64f, 264f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(677), false),
                new(10, 1460u, new(8f, 136f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(11, 0u, new(80f, 200f, 376f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new(139), false),
                new(12, 1464u, new(8f, 328f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(13, 0u, new(64f, 360f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(133), false),
                new(14, 1674u, new(8f, 392f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(15, 0u, new(64f, 424f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(135), false),
                new(16, 1472u, new(512f, 392f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(17, 0u, new(568f, 424f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(674), false),
                new(18, 1679u, new(512f, 328f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(19, 0u, new(568f, 360f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(137), false),
                new(20, 142u, new(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new(0), false),
                new(21, 20u, new(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Root, new(0), false),
            },
            [InGameOptionsPage.AdvancedSound] = new InGameOptionsWidget[]
            {
                new(0, 1500u, new(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), true),
                new(1, 20u, new(8f, 512f, 1008f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Sound, new(0), false),
            },
            [InGameOptionsPage.Controls] = new InGameOptionsWidget[]
            {
                new(0, 98u, new(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), true),
                new(1, 1469u, new(8f, 136f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(2, 25u, new(96f, 168f, 360f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new(154), false),
                new(3, 1468u, new(8f, 200f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(4, 25u, new(96f, 232f, 360f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new(158), false),
                new(5, 115u, new(8f, 264f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(6, 50u, new(96f, 296f, 360f, 25.6f), InGameOptionsWidgetKind.Slider, InGameOptionsPage.None, new(156), false),
                new(7, 2840u, new(8f, 328f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(8, 0u, new(64f, 360f, 496f, 28.8f), InGameOptionsWidgetKind.Value, InGameOptionsPage.None, new(152), false),
                new(9, 1670u, new(512f, 328f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(10, 0u, new(568f, 360f, 496f, 28.8f), InGameOptionsWidgetKind.Unavailable, InGameOptionsPage.None, new(0), false),
                new(11, 1470u, new(8f, 392f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(12, 0u, new(64f, 424f, 496f, 70.4f), InGameOptionsWidgetKind.Unavailable, InGameOptionsPage.None, new(0), false),
                new(13, 1680u, new(512f, 392f, 496f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(14, 0u, new(568f, 424f, 496f, 28.8f), InGameOptionsWidgetKind.Unavailable, InGameOptionsPage.None, new(0), false),
                new(15, 117u, new(8f, 464f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.Keyboard, new(0), false),
                new(16, 142u, new(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new(0), false),
                new(17, 20u, new(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Root, new(0), false),
            },
            [InGameOptionsPage.Keyboard] = new InGameOptionsWidget[]
            {
                new(0, 117u, new(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), true),
                new(1, 0u, new(8f, 136f, 744f, 243.2f), InGameOptionsWidgetKind.KeyboardList, InGameOptionsPage.None, new(0), false),
                new(2, 142u, new(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new(0), false),
                new(3, 20u, new(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Controls, new(0), false),
            },
            [InGameOptionsPage.UserCameras] = new InGameOptionsWidget[]
            {
                new(0, 1485u, new(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), true),
                new(1, 1486u, new(8f, 136f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameraEdit, new(0), false),
                new(2, 1487u, new(8f, 200f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameraEdit, new(0), false),
                new(3, 1488u, new(8f, 264f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameraEdit, new(0), false),
                new(4, 142u, new(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new(0), false),
                new(5, 20u, new(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.Root, new(0), false),
            },
            [InGameOptionsPage.UserCameraEdit] = new InGameOptionsWidget[]
            {
                new(0, 1490u, new(8f, 72f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), true),
                new(1, 1686u, new(8f, 136f, 1008f, 28.8f), InGameOptionsWidgetKind.Label, InGameOptionsPage.None, new(0), false),
                new(2, 1489u, new(8f, 464f, 1008f, 28.8f), InGameOptionsWidgetKind.Navigate, InGameOptionsPage.UserCameras, new(0), false),
                new(3, 142u, new(8f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Continue, InGameOptionsPage.None, new(0), false),
                new(4, 20u, new(512f, 512f, 496f, 28.8f), InGameOptionsWidgetKind.Back, InGameOptionsPage.UserCameras, new(0), false),
            },
        };
    public static IReadOnlyList<InGameOptionsWidget> ForPage(InGameOptionsPage page) => Pages[page];
}
