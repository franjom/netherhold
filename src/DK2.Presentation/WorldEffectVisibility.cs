// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
namespace DK2.Presentation;

/// <summary>The shared effect-request filter: explored tile, non-solid navigation and local Heart intro ownership.</summary>
public static class WorldEffectVisibility
{
    public static bool Allows(bool explored, bool solid, bool viewerOnly, PlayerId owner, PlayerId viewer)
    {
        return explored && !solid && (!viewerOnly || owner == viewer);
    }

    public static bool Effect(WorldState world, WorldView view, INavByteGrid navigation, PlayerId viewer, in ViewEffectRequest request)
    {
        return Allows(view.CanShowTile(world, request.Tile), !world.Contains(request.Tile)
            || NavBytes.IsSolid(navigation.NavByte(request.Tile.X, request.Tile.Y)),
            HeartIntroRules.IsViewerOnly(request.EffectId.Value), request.Owner, viewer);
    }

    public static bool Construction(WorldState world, WorldView view, in RoomConstructionEffect request)
    {
        return view.CanShowTile(world, request.Tile);
    }

    public static bool Shot(WorldState world, WorldView view, ShotState shot)
    {
        return view.CanShowTile(world, new TilePos(shot.PosX >> 12, shot.PosY >> 12));
    }
}
