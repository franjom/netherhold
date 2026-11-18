// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The performance harness's last process callback of the frame (priority <see cref="int.MaxValue"/>):
/// its stamp, against <see cref="PerformanceReport"/>'s first, is the frame's process time. Added only with the
/// harness.</summary>
public partial class PerformanceReportTail : Node
{
    /// <summary>The stamp of this frame's last process callback (<see cref="Stopwatch.GetTimestamp"/>).</summary>
    public long Stamp { get; private set; }

    public override void _Process(double delta) => Stamp = Stopwatch.GetTimestamp();
}
