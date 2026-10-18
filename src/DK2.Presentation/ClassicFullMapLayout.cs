// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using System.Numerics;
namespace DK2.Presentation;

[Dk2Behavior(Confidence.Confirmed, "docs/research/minimap-controls.md")]
public readonly record struct ClassicFullMapLayout(Vector2 Origin, Vector2 Size, int MapWidth, int MapHeight)
{
    public static ClassicFullMapLayout Fit(int screenWidth,int screenHeight,int mapWidth,int mapHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0 || mapWidth <= 0 || mapHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(screenWidth));
        int left = screenWidth * 41 / 640, top = screenHeight * 33 / 480;
        int width = screenWidth * 425 / 640 - left, height = screenHeight * 417 / 480 - top;
        int fittedWidth = width, fittedHeight = height;
        if ((float)mapWidth / mapHeight <= (float)width / height) fittedWidth = (int)((float)height * mapWidth / mapHeight);
        else fittedHeight = (int)((float)width * mapHeight / mapWidth);
        return new(new(left + (width-fittedWidth)/2, top + (height-fittedHeight)/2),new(fittedWidth,fittedHeight),mapWidth,mapHeight);
    }
    public TilePos ResolveTile(Vector2 screenPoint) => new(
        (int)((screenPoint.X-Origin.X)*MapWidth/Size.X), (int)((screenPoint.Y-Origin.Y)*MapHeight/Size.Y));
}
