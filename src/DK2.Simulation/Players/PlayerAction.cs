// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Players;

/// <summary>An ordinary player action, as a human issues it. The family is closed to this
/// assembly, so no raw slot or arbitrary command can be requested. The host turns an admitted
/// action into the same commands as the human input path.</summary>
public abstract record PlayerAction
{
    private protected PlayerAction() { }

    /// <summary>The action's family, in which capability offers are expressed.</summary>
    public PlayerActionKinds Kind => Family;

    // Only this assembly can override it, so no type elsewhere can derive: a record keeps a
    // protected copy constructor and cannot close itself by constructor access alone.
    private protected abstract PlayerActionKinds Family { get; }

    /// <summary>Tags (or untags) the rectangle between two corners for digging.</summary>
    public sealed record TagArea(TilePos From, TilePos To, bool Untag = false) : PlayerAction
    {
        private protected override PlayerActionKinds Family => PlayerActionKinds.TagArea;
    }

    /// <summary>Builds a room over the rectangle between two corners, as a dragged build does.</summary>
    public sealed record BuildRoom(RoomTypeId Room, TilePos From, TilePos To) : PlayerAction
    {
        private protected override PlayerActionKinds Family => PlayerActionKinds.BuildRoom;
    }

    /// <summary>Presses a bottom-panel button; campaign scripts observe these presses.</summary>
    public sealed record SelectPanel(GuiButtonTarget Button) : PlayerAction
    {
        private protected override PlayerActionKinds Family => PlayerActionKinds.SelectPanel;
    }

    /// <summary>Picks a visible thing up into the hand.</summary>
    public sealed record PickUp(PlayerThingHandle Thing) : PlayerAction
    {
        private protected override PlayerActionKinds Family => PlayerActionKinds.PickUp;
    }

    /// <summary>Drops the hand's top item on a tile.</summary>
    public sealed record DropAt(TilePos Tile) : PlayerAction
    {
        private protected override PlayerActionKinds Family => PlayerActionKinds.DropAt;
    }

    /// <summary>Slaps a visible creature.</summary>
    public sealed record Slap(PlayerThingHandle Thing) : PlayerAction
    {
        private protected override PlayerActionKinds Family => PlayerActionKinds.Slap;
    }

    /// <summary>Activates a visible special's box with the hand.</summary>
    public sealed record ActivateSpecial(PlayerThingHandle Special) : PlayerAction
    {
        private protected override PlayerActionKinds Family => PlayerActionKinds.ActivateSpecial;
    }

    /// <summary>Casts Create Imp on a tile.</summary>
    public sealed record CreateImpAt(TilePos Tile) : PlayerAction
    {
        private protected override PlayerActionKinds Family => PlayerActionKinds.CreateImpAt;
    }

    /// <summary>Skips the level's intro camera path; offered only while it plays.</summary>
    public sealed record SkipIntro : PlayerAction
    {
        private protected override PlayerActionKinds Family => PlayerActionKinds.SkipIntro;
    }
}
