// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The scripted view's HUD rules moved out of Unity's runner: <see cref="ScriptedViewDirector.PlayedHud"/> and
/// <see cref="ScriptedViewDirector.PanelFlash"/>.</summary>
public sealed class ScriptedViewHudTests
{
    private static readonly PlayerId Viewer = new(3);

    private static SimEvent Request(ScriptedViewRequest request)
    {
        return new(default, "t", "scripted_view", "") { ScriptedView = request };
    }

    private static SimEvent Flash(HudFlashTarget target)
    {
        return Request(new ScriptedViewRequest(ScriptedViewKind.FlashButton,
        PlayerIds.Unowned, 0, default, false, FlashButton: new HudFlashRequest(target, 0, true)));
    }

    [Fact]
    public void ThePlayedHudIsTheScriptedViewsUnderTheHudsOwnGates()
    {
        var director = new ScriptedViewDirector(Viewer, _ => null, _ => null);
        var controls = new ClassicHudControls();
        director.Update(0, 0);
        Assert.Equal(new HudVisibility(true, true), director.PlayedHud(controls, ClassicCameraMode.Dungeon));

        controls.SetGuiVisible(false);
        Assert.Equal(new HudVisibility(false, true), director.PlayedHud(controls, ClassicCameraMode.Dungeon));
        controls.SetGuiVisible(true);

        director.Emit(Request(new ScriptedViewRequest(ScriptedViewKind.Letterbox, Viewer, 0, default, true)));
        director.Update(100, 0);
        Assert.Equal(new HudVisibility(false, false), director.PlayedHud(controls, ClassicCameraMode.Dungeon));
    }

    [Fact]
    public void ThePanelFlashIsDroppedOnceItsItemIsInUse()
    {
        var director = new ScriptedViewDirector(Viewer, _ => null, _ => null);
        var tools = new HudToolState();
        var creatures = new HudFlashTarget(0xF800);
        director.Emit(Flash(creatures));
        director.Update(0, 0);

        Assert.Equal(creatures, director.PanelFlash(tools, BottomPanelOpenSet.None));
        Assert.Equal(creatures, director.ActiveFlashTarget);
        Assert.Null(director.PanelFlash(tools, BottomPanelOpenSet.Of(BottomPanelCategory.Creatures)));
        Assert.Null(director.ActiveFlashTarget);
    }

    /// <summary>A flash target packs its category in the low byte and its button (the item) in the high byte.</summary>
    private static HudFlashTarget Item(byte category, byte button)
    {
        return new((ushort)(category | button << 8));
    }

    private static void Choose(HudToolState tools, BottomPanelChoiceKind kind, byte value, PlayerToolMode tool, RoomTypeId room)
    {
        tools.SelectPanelChoice(new BottomPanelSelection(new BottomPanelChoiceKey(kind, value), tool, room),
            new PlayerInputTranslator(), query: null);
    }

    [Fact]
    public void ARoomFlashEndsOnlyWithTheBuildToolOnThatRoom()
    {
        var director = new ScriptedViewDirector(Viewer, _ => null, _ => null);
        var tools = new HudToolState();
        HudFlashTarget lair = Item(1, (byte)RoomTypeId.Lair);
        director.Emit(Flash(lair));
        director.Update(0, 0);

        // The same number as a spell choice, and another room with the build tool, leave it lit.
        Choose(tools, BottomPanelChoiceKind.Spell, (byte)RoomTypeId.Lair, PlayerToolMode.Hand, RoomTypeId.Treasury);
        Assert.Equal(lair, director.PanelFlash(tools, BottomPanelOpenSet.None));
        Choose(tools, BottomPanelChoiceKind.Room, (byte)RoomTypeId.Treasury, PlayerToolMode.BuildRoom, RoomTypeId.Treasury);
        Assert.Equal(lair, director.PanelFlash(tools, BottomPanelOpenSet.None));

        Choose(tools, BottomPanelChoiceKind.Room, (byte)RoomTypeId.Lair, PlayerToolMode.BuildRoom, RoomTypeId.Lair);
        Assert.Null(director.PanelFlash(tools, BottomPanelOpenSet.None));
    }

    [Theory]
    [InlineData(5, BottomPanelChoiceKind.Spell)]
    [InlineData(3, BottomPanelChoiceKind.Door)]
    [InlineData(4, BottomPanelChoiceKind.Trap)]
    public void ASpellDoorOrTrapFlashEndsOnlyWhenThatItemIsTheChoice(byte category, BottomPanelChoiceKind kind)
    {
        var director = new ScriptedViewDirector(Viewer, _ => null, _ => null);
        var tools = new HudToolState();
        HudFlashTarget flash = Item(category, 6);
        director.Emit(Flash(flash));
        director.Update(0, 0);

        // The room with that number on the build tool, another kind with that number, and another item leave it lit.
        Choose(tools, BottomPanelChoiceKind.Room, 6, PlayerToolMode.BuildRoom, (RoomTypeId)6);
        Assert.Equal(flash, director.PanelFlash(tools, BottomPanelOpenSet.None));
        Choose(tools, kind == BottomPanelChoiceKind.Spell ? BottomPanelChoiceKind.Door : BottomPanelChoiceKind.Spell, 6,
            PlayerToolMode.Hand, RoomTypeId.Treasury);
        Assert.Equal(flash, director.PanelFlash(tools, BottomPanelOpenSet.None));
        Choose(tools, kind, 7, PlayerToolMode.Hand, RoomTypeId.Treasury);
        Assert.Equal(flash, director.PanelFlash(tools, BottomPanelOpenSet.None));

        Choose(tools, kind, 6, PlayerToolMode.Hand, RoomTypeId.Treasury);
        Assert.Null(director.PanelFlash(tools, BottomPanelOpenSet.None));
    }

    [Fact]
    public void TheSellFlashEndsWithTheSellToolAndAResourceFlashStays()
    {
        var director = new ScriptedViewDirector(Viewer, _ => null, _ => null);
        var tools = new HudToolState();
        director.Emit(Flash(new HudFlashTarget(0xFF00)));
        director.Update(0, 0);
        Assert.NotNull(director.PanelFlash(tools, BottomPanelOpenSet.None));
        tools.StartWith(DungeonToolMode.SellRoom, RoomTypeId.Treasury);
        Assert.Null(director.PanelFlash(tools, BottomPanelOpenSet.None));

        director.Emit(Flash(HudFlashTarget.Mana));
        director.Update(1, 0);
        Assert.Equal(HudFlashTarget.Mana, director.PanelFlash(tools, BottomPanelOpenSet.Of(BottomPanelCategory.Creatures)));
        Assert.Equal(KeeperResourceFlash.Mana, director.ResourceFlash);
    }
}
