// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>A picture 0x0053A580 draws for a briefing section: the level's two objective shots (0x0053AC60)
/// or a panel strip from <c>BG_Tiles\</c>, which 0x00534210 loads into the table at 0x0073FF70 from the
/// names at 0x0066EF84.</summary>
public enum FrontEndBriefingImage : byte
{
    FirstShot, SecondShot, Objective00, Objective01, Objective02, Objective03, Objective04, Load01,
}

/// <summary>One picture at its top-left corner, in 640 × 480 units, drawn at its own size.</summary>
public readonly record struct FrontEndPlacedImage(FrontEndBriefingImage Image, int X, int Y);

/// <summary>0x0053A580's pictures for briefing sections 2 and 3 in each mode (the widget's <c>+0x34</c>):
/// 0 is the briefing page, 1 the statistics page.</summary>
public static class FrontEndBriefingImages
{
    /// <summary>The panel strip's front-end image name; null for the shots, which are per level.</summary>
    public static string? ArtName(FrontEndBriefingImage image) => image switch
    {
        FrontEndBriefingImage.Objective00 => "BG_Tiles/BG-Objective-00",
        FrontEndBriefingImage.Objective01 => "BG_Tiles/BG-Objective-01",
        FrontEndBriefingImage.Objective02 => "BG_Tiles/BG-Objective-02",
        FrontEndBriefingImage.Objective03 => "BG_Tiles/BG-Objective-03",
        FrontEndBriefingImage.Objective04 => "BG_Tiles/BG-Objective-04",
        FrontEndBriefingImage.Load01 => "BG_Tiles/BG-Load-01",
        _ => null,
    };

    /// <summary>Section 2 draws the first shot at (20, 110), then two strips at x 184 (mode 3 instead tiles
    /// BG-Load-01 down x 61). Section 3 draws nothing of its own on a secret or special level (level flags
    /// 0x20 and 0x40, 0x00535E10) except mode 1's strip; otherwise the second shot at (20, 242) and its
    /// mode's strips.</summary>
    public static IEnumerable<FrontEndPlacedImage> For(uint mode, uint section, bool secretOrSpecial)
    {
        if (section == 2)
        {
            yield return new(FrontEndBriefingImage.FirstShot, 20, 110);
            if (mode == 3)
            {
                for (int y = 148; y < 328; y += 30) yield return new(FrontEndBriefingImage.Load01, 61, y);
                yield break;
            }
            yield return new(FrontEndBriefingImage.Objective00, 184, 110);
            yield return new(FrontEndBriefingImage.Objective01, 184, 147);
            yield break;
        }
        if (section != 3) yield break;
        if (secretOrSpecial)
        {
            if (mode == 1) yield return new(FrontEndBriefingImage.Objective03, 184, 242);
            yield break;
        }
        yield return new(FrontEndBriefingImage.SecondShot, 20, 242);
        if (mode == 0)
        {
            yield return new(FrontEndBriefingImage.Objective00, 184, 242);
            yield return new(FrontEndBriefingImage.Objective02, 184, 279);
        }
        else if (mode == 1) yield return new(FrontEndBriefingImage.Objective03, 184, 242);
        else if (mode is 2 or 4) yield return new(FrontEndBriefingImage.Objective04, 0, 242);
    }
}
