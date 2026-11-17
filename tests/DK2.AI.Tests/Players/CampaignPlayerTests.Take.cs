// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>Rooms to take (R66, Level 5 plan step 1, 2026-11-17): a Prison, Portal or Torture Chamber the map holds is dug
/// toward first, through ground another player owns, until open floor joins it to own floor.</summary>
public partial class CampaignPlayerTests
{
    /// <summary>Own floor round the heart; a hero Prison behind a rock wall and hero floor to the east; open rock south.</summary>
    private static readonly string[] HeroPrison =
    [
        "RRRRRRRRRRRR",
        "R.....#hhppR",
        "R.....#hhppR",
        "R..H..#hhppR",
        "R.....#hhppR",
        "R#####RRRRRR",
        "RRRRRRRRRRRR",
    ];

    private static IReadOnlyList<TilePos> TakeMarks(PlayerDecision decision) =>
        [.. decision.Actions.OfType<PlayerAction.TagArea>().Select(t => t.From)];

    [Fact]
    public void AHeroPrisonInViewIsDugTowardThroughHeldBackGround()
    {
        PlayerDecision decision = Player().Decide(Observe(map: HeroPrison, rooms: []));
        IReadOnlyList<TilePos> marks = TakeMarks(decision);
        // The wall beside hero floor is held back from the ordinary frontier; the Prison makes it the first mark.
        Assert.Equal(new TilePos(6, 3), marks[0]);
        Assert.Contains("toward the room to take at (9,3)", decision.Thoughts.Single(t => t.Routine == "dig").Note);

        // Without rooms to take the wall stays standing while open rock remains.
        CampaignPlayer plain = Player().Also(p => p.Apply(p.Strategy with { TakeRooms = [] }));
        Assert.DoesNotContain(TakeMarks(plain.Decide(Observe(map: HeroPrison, rooms: []))), t => t.X == 6);
    }

    [Fact]
    public void APrisonJoinedToOwnFloorByOpenGroundIsLeftToTheImps()
    {
        string[] joined = [.. HeroPrison.Select(r => r.Replace('#', '.'))];
        joined[5] = "R#####RRRRRR";
        PlayerDecision decision = Player().Decide(Observe(map: joined, rooms: []));
        Assert.DoesNotContain("toward the room to take", decision.Thoughts.Single(t => t.Routine == "dig").Note);
    }

    [Fact]
    public void APlaybooksRoomToTakeIsDugTowardBeforeItIsSeen()
    {
        string[] unseen =
        [
            "RRRRRRRRRRRR",
            "R.....??????",
            "R.....??????",
            "R..H..??????",
            "R.....??????",
            "R#####??????",
            "RRRRRRRRRRRR",
        ];
        CampaignPlayer player = Player().Also(p => p.Apply(p.Strategy with { TakeAt = [new TilePos(10, 1)] }));
        PlayerDecision decision = player.Decide(Observe(map: unseen, rooms: []));
        Assert.Equal(new TilePos(6, 1), TakeMarks(decision)[0]);
        Assert.Contains("toward the room to take at (10,1)", decision.Thoughts.Single(t => t.Routine == "dig").Note);
    }
}
