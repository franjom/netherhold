// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Scene root: shows the classic front-end menu over a black screen. The install is DK2_DIR, else the usual
/// install locations (<see cref="Dk2Install"/>); its files are read in place and never copied into the project.</summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Colors.Black);
        string? install = Dk2Install.Find();
        if (install is null)
        {
            GD.PushError("DK2.Godot: no Dungeon Keeper 2 install found; set DK2_DIR.");
            return;
        }
        var menu = new ClassicFrontEndMenu { Name = "FrontEndMenu" };
        menu.Open(install);
        AddChild(menu);
    }
}
