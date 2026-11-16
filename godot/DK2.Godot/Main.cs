// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Scene root: shows the classic front-end menu over a black screen. The install is DK2_DIR, else the usual
/// install locations (<see cref="Dk2Install"/>); its files are read in place and never copied into the project. The
/// campaign profile and hi-score table are the player's files under <see cref="UserFolders"/>.</summary>
public partial class Main : Node
{
    /// <summary>The page a level that ended hands this scene (Unity's <c>GameSessionRequest.FrontEnd(returnPage)</c>);
    /// taken and cleared by <see cref="_Ready"/>.</summary>
    internal static FrontEndPage s_returnPage;

    public override void _Ready()
    {
        FrontEndPage returnPage = s_returnPage;
        s_returnPage = FrontEndPage.None;
        RenderingServer.SetDefaultClearColor(Colors.Black);
        string? install = Dk2Install.Find();
        if (install is null)
        {
            GD.PushError("DK2.Godot: no Dungeon Keeper 2 install found; set DK2_DIR.");
            return;
        }
        var menu = new ClassicFrontEndMenu { Name = "FrontEndMenu" };
        menu.Open(install, UserFolders.FromArguments(OS.GetCmdlineUserArgs()), returnPage);
        AddChild(menu);
    }
}
