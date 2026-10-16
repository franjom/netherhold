// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

internal static class ClassicKeeperPalette
{
    public static (byte Red, byte Green, byte Blue) Colour(PlayerId owner) => owner.Value switch
    {
        0 => (0, 0, 0), 1 => (191, 213, 224), 2 => (255, 255, 255),
        3 => (230, 50, 50), 4 => (50, 78, 230), 5 => (50, 230, 51),
        6 => (226, 230, 50), 7 => (230, 50, 212), _ => (191, 213, 224)
    };
}
