// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

public enum FrontEndFontFamily : byte { FrontEnd, InGame }
public readonly record struct FrontEndFont(FrontEndFontFamily Family, int PointSize)
{
    public string FileName => "FONT_" + (Family == FrontEndFontFamily.FrontEnd ? "FRONTEND" : "INGAME") + PointSize + ".bf4";
}
public enum FrontEndVerticalAlignment : byte { Centre, Top }

public static class FrontEndLabelStyles
{
    public static bool IsText(FrontEndDrawKind draw) => draw is FrontEndDrawKind.Label or FrontEndDrawKind.Title
        or FrontEndDrawKind.SmallText or FrontEndDrawKind.MediumText or FrontEndDrawKind.OptionText
        or FrontEndDrawKind.KeyboardText or FrontEndDrawKind.PlainText or FrontEndDrawKind.DialogTitle;
    public static FrontEndFont FontFor(FrontEndDrawKind draw) => draw switch
    {
        FrontEndDrawKind.SmallText => new(FrontEndFontFamily.FrontEnd, 12),
        FrontEndDrawKind.MediumText => new(FrontEndFontFamily.FrontEnd, 14),
        FrontEndDrawKind.OptionText => new(FrontEndFontFamily.InGame, 16),
        FrontEndDrawKind.KeyboardText => new(FrontEndFontFamily.InGame, 18),
        FrontEndDrawKind.DialogTitle => new(FrontEndFontFamily.InGame, 24),
        FrontEndDrawKind.Title => new(FrontEndFontFamily.FrontEnd, 36),
        _ => new(FrontEndFontFamily.FrontEnd, 20),
    };
    public static FrontEndTextAlignment AlignmentFor(FrontEndWidget widget) => widget.Draw switch
    {
        FrontEndDrawKind.Label => widget.Alignment,
        FrontEndDrawKind.Title => FrontEndTextAlignment.Centre,
        _ => (widget.DrawArgument2 & 0xffff) switch
        {
            1 or 3 => FrontEndTextAlignment.Left,
            2 or 4 => FrontEndTextAlignment.Right,
            _ => FrontEndTextAlignment.Centre,
        },
    };
    public static FrontEndVerticalAlignment VerticalFor(FrontEndWidget widget)
        => widget.Draw is not (FrontEndDrawKind.Label or FrontEndDrawKind.Title)
            && (widget.DrawArgument2 & 0xffff) is >= 3 and <= 5
            ? FrontEndVerticalAlignment.Top : FrontEndVerticalAlignment.Centre;
}
