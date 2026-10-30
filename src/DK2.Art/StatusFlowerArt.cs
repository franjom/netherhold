// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;

namespace DK2.Art;

public static class StatusFlowerArt
{
    public static readonly AssetRef Dollar = AssetRef.Create(AssetRef.UiCategory, "casino_dollar", AssetKind.Mesh);
    public static IReadOnlyList<ArtEntry> Entries { get; } = new[]
    {
        new ArtEntry(Dollar, ArtUse.Model, new Dk2ArtResource("Dollar", 6)
            { FrameCount = 30, FramesPerSecond = 30 }, "casino winner flower"),
    };
}
