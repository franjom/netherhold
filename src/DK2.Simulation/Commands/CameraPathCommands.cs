// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>Escape or Space while a scripted camera path plays: the key handler's branch at
/// <c>0x00405362</c> ends the path (<c>0x0044D8F0</c>) and clears the speech queue
/// (camera-paths.md section 6).</summary>
public sealed record SkipCameraPathCommand(PlayerId PlayerId) : PlayerCommand(PlayerId);
