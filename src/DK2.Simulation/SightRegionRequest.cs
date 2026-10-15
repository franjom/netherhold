// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Process 25's local presentation command; it never changes discovery bits.</summary>
public readonly record struct SightRegionRequest(
    PlayerId Viewer, TilePos Position, ushort DurationTicks, ushort RadiusTiles);
