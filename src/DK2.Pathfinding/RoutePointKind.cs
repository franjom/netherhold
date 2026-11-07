// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Pathfinding;

/// <summary>The points returned for movement or a plain route query.</summary>
public enum RoutePointKind : byte
{
    GateMidpoints,
    FunnelCorners,
}
