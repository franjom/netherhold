// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

public class PlayerDecisionTests
{
    [Fact]
    public void DecisionValidatesAdvanceAndOwnsItsActions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerDecision.Wait(0, "none"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerDecision.Wait(-4, "none"));
        Assert.Throws<ArgumentException>(() => PlayerDecision.Wait(1, new string('x', PlayerDecision.MaxReasonLength + 1)));
        Assert.Equal(PlayerDecision.MaxReasonLength, PlayerDecision.Wait(1, new string('x', PlayerDecision.MaxReasonLength)).Reason.Length);
        Assert.Throws<ArgumentException>(() => new PlayerDecision([null!], 1, "null action"));
        Assert.Throws<ArgumentNullException>(() => new PlayerDecision([], 1, null!));

        var actions = new List<PlayerAction> { new PlayerAction.DropAt(new TilePos(1, 1)) };
        var decision = new PlayerDecision(actions, 4, "drop");
        actions.Clear();

        Assert.Single(decision.Actions);
        Assert.False(decision.Actions is PlayerAction[]);
        Assert.Empty(PlayerDecision.Wait(1, "idle").Actions);
    }
}
