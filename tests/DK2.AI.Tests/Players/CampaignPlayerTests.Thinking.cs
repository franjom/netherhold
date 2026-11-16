// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>Each decision carries the player's own account: every routine's verdict and the numbers it read
/// (economy and assault plan, T0). Diagnostics only: the actions do not depend on them.</summary>
public partial class CampaignPlayerTests
{
    [Fact]
    public void EveryRoutineThatRanLeavesAVerdictInOrder()
    {
        PlayerDecision decision = Player().Decide(Observe());
        Assert.Equal(["hand", "room", "trap", "imp", "assault", "work", "pit", "scout", "special", "dig"], decision.Thoughts.Select(t => t.Routine));
        PlayerThought room = decision.Thoughts.Single(t => t.Routine == "room");
        Assert.True(room.Acted);
        Assert.Contains("build Treasury 3x3 at", room.Note);
        Assert.False(decision.Thoughts.Single(t => t.Routine == "hand").Acted);
        Assert.Equal("no attack to answer", decision.Thoughts.Single(t => t.Routine == "hand").Note);
    }

    [Fact]
    public void ARoutineThatWaitsSaysWhy()
    {
        PlayerDecision decision = Player().Decide(Observe(gold: 449));
        PlayerThought room = decision.Thoughts.Single(t => t.Routine == "room");
        Assert.False(room.Acted);
        Assert.Equal("saving gold for Treasury, Lair (gold 449)", room.Note);
        PlayerThought dig = decision.Thoughts.Single(t => t.Routine == "dig");
        Assert.True(dig.Acted);
        Assert.StartsWith("mark ", dig.Note);
        Assert.Contains("(1 gold)", dig.Note);
    }

    [Fact]
    public void BeliefsCarryTheNumbersTheDecisionReadAndDoNotChangeTheActions()
    {
        PlayerDecision decision = Player().Decide(Observe(gold: 1234));
        Assert.Contains(new PlayerBelief("gold", 1234), decision.Beliefs);
        Assert.Contains(decision.Beliefs, b => b.Name == "fighters");
        Assert.Equal(Player().Decide(Observe(gold: 1234)).Actions, decision.Actions);
    }

    [Fact]
    public void ADecisionsAccountIsACopyTheNextDecisionCannotChange()
    {
        CampaignPlayer player = Player();
        PlayerDecision first = player.Decide(Observe(1));
        string[] before = [.. first.Thoughts.Select(t => t.Note)];
        player.Decide(Observe(2, gold: 0));
        Assert.Equal(before, first.Thoughts.Select(t => t.Note));
    }
}
