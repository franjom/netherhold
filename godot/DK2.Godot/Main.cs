// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Bootstrap scene root: proves the engine-neutral assemblies load under Godot's .NET host.</summary>
public partial class Main : Node
{
    public override void _Ready() =>
        GD.Print($"DK2.Godot: {typeof(HudFrame).Assembly.GetName().Name} loaded on Godot {Engine.GetVersionInfo()["string"]}");
}
