// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.AI.Players;

/// <summary>How the campaign player marks rock to dig.</summary>
public enum DigMode
{
    /// <summary>No new marks; standing ones are taken back.</summary>
    Off,

    /// <summary>The frontier, gold seams first, then nearest home (the default).</summary>
    GoldFirst,

    /// <summary>The frontier nearest home, gold or not.</summary>
    Frontier,

    /// <summary>The frontier nearest <see cref="PlayerStrategy.DigTarget"/>.</summary>
    Toward,
}

/// <summary>What the campaign player does about visible enemies.</summary>
public enum FightMode
{
    /// <summary>Lift free fighters and drop them beside the nearest enemy (the default).</summary>
    Engage,

    /// <summary>Answer only an enemy within <see cref="PlayerStrategy.HomeGuardTiles"/> of home;
    /// anything held with no such enemy goes down near home.</summary>
    HoldHome,

    /// <summary>No lifting at all; anything held goes down near home.</summary>
    Off,
}

/// <summary>
/// The values the campaign player's routines read (campaign bot stage 3 plan, Task 1). Test policy for
/// this controller, not a claim about DK2's own keepers. The defaults are the controller's behaviour
/// before strategies existed; <see cref="CampaignPlayer.Apply"/> changes them during a run.
/// </summary>
public sealed record PlayerStrategy
{
    /// <summary>The harness's default bound on a decision's advance (<c>--player-interval-ticks</c> takes
    /// 1 to 40); a strategy keeps within it whatever the run's own bound.</summary>
    public const int MaxAdvanceTicks = 40;

    /// <summary>Rooms to keep, in the order they are built.</summary>
    public IReadOnlyList<RoomTypeId> Rooms { get; init; } = [RoomTypeId.Treasury, RoomTypeId.Lair, RoomTypeId.Hatchery];

    /// <summary>A room is built as a square of this side.</summary>
    public int RoomSide { get; init; } = 3;

    public DigMode Dig { get; init; } = DigMode.GoldFirst;

    /// <summary>The tile <see cref="DigMode.Toward"/> digs toward; null for every other mode.</summary>
    public TilePos? DigTarget { get; init; }

    /// <summary>At most this many of the player's own dig marks stand at once.</summary>
    public int MaxOutstandingTags { get; init; } = 8;

    public FightMode Fight { get; init; } = FightMode.Engage;

    /// <summary>Under <see cref="FightMode.HoldHome"/>, how near home (tiles) an enemy must be to be answered.</summary>
    public int HomeGuardTiles { get; init; } = 8;

    /// <summary>Activate offered specials (within the options' allowed types).</summary>
    public bool UseSpecials { get; init; } = true;

    /// <summary>Ticks between ordinary decisions, 1 to <see cref="MaxAdvanceTicks"/>.</summary>
    public int AdvanceTicks { get; init; } = 4;

    /// <summary>Every problem with this strategy, empty when it may be applied. With the map's size,
    /// a dig target outside it is one.</summary>
    public IReadOnlyList<string> Validate(int? mapWidth = null, int? mapHeight = null)
    {
        var problems = new List<string>();
        if (Rooms is null) problems.Add("Rooms is null");
        else if (Rooms.Distinct().Count() != Rooms.Count) problems.Add("Rooms names a room twice");
        if (RoomSide <= 0) problems.Add($"RoomSide {RoomSide} is not positive");
        if (MaxOutstandingTags <= 0) problems.Add($"MaxOutstandingTags {MaxOutstandingTags} is not positive");
        if (HomeGuardTiles < 0) problems.Add($"HomeGuardTiles {HomeGuardTiles} is negative");
        if (AdvanceTicks is < 1 or > MaxAdvanceTicks) problems.Add($"AdvanceTicks {AdvanceTicks} is outside 1 to {MaxAdvanceTicks}");
        if (Dig == DigMode.Toward && DigTarget is null) problems.Add("Dig Toward needs a DigTarget");
        if (Dig != DigMode.Toward && DigTarget is not null) problems.Add($"DigTarget is set but Dig is {Dig}");
        if (DigTarget is TilePos target && mapWidth is int width && mapHeight is int height
            && (target.X < 0 || target.Y < 0 || target.X >= width || target.Y >= height))
            problems.Add($"DigTarget ({target.X},{target.Y}) is outside the {width}x{height} map");
        return problems;
    }
}
