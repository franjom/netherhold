// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Data.Model;

/// <summary>What DK2's state-transition rule says about one move.</summary>
public enum StateTransition
{
    /// <summary>The original would take the transition.</summary>
    Allowed,

    /// <summary>The original refuses it, and the creature keeps the state it has.</summary>
    Refused,

    /// <summary>The group tags differ, so the original consults three fields of the 32
    /// category records, whose values this project has not read (Q300). Never answered as
    /// <see cref="Refused"/>: an unread rule is not a false one — the precedent is
    /// ADR-0109, which refuses to answer an unsupported trigger condition false.</summary>
    NeedsCategoryTable,
}

/// <summary>DK2's rule for whether a creature may move from one state to another, read from
/// the original's own transition routine (`creature-state-table.md` §2.2).
/// <para>The equal-tags half is decided by the two bytes already in
/// <see cref="CreatureStateTable"/> and is answered here. The differing-tags half needs
/// category-record values that are filled at run time and undumped, so it is reported
/// rather than guessed.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-state-table.md")]
public static class CreatureStateTransitions
{
    /// <summary>The group tag that, paired with <see cref="PassthroughCategory"/>, is
    /// admitted whatever the current category.</summary>
    public const byte PassthroughGroup = 3;

    /// <summary>The category that <see cref="PassthroughGroup"/> admits regardless — the
    /// dying and frozen states. Tested <i>before</i> the refusal and the equality below,
    /// which is the rule that lets a creature die out of anything it was doing.</summary>
    public const CreatureStateCategory PassthroughCategory = CreatureStateCategory.Dying;

    /// <summary>Classify a move from <paramref name="currentStateId"/> to
    /// <paramref name="targetStateId"/>.
    /// <para>The order is the original's and it is load-bearing: the passthrough pair
    /// first, then the refused category, then equality. All three read the <b>target</b>'s
    /// fields; only the last looks at the current state at all.</para></summary>
    public static StateTransition Classify(int currentStateId, int targetStateId)
    {
        ref readonly CreatureStateDef target = ref CreatureStateTable.Get(targetStateId);
        ref readonly CreatureStateDef current = ref CreatureStateTable.Get(currentStateId);

        if (target.Group != current.Group)
        {
            return StateTransition.NeedsCategoryTable;
        }

        if (target.Group == PassthroughGroup && target.Category == PassthroughCategory)
        {
            return StateTransition.Allowed;
        }

        if (target.Category == CreatureStateCategory.Refused)
        {
            return StateTransition.Refused;
        }

        return target.Category == current.Category
            ? StateTransition.Allowed
            : StateTransition.Refused;
    }

    /// <summary>Which state the differing-tags half compares against: a creature standing in
    /// a utility state is judged by the state it <b>saved</b>, not by the movement it is
    /// performing. The substitution is inside the transition rule, which is why a resume
    /// slot is a prerequisite for the rule and not only for resuming (ADR-0135).</summary>
    public static int ResolveComparisonState(int currentStateId, int savedStateId) =>
        CreatureStateTable.Get(currentStateId).IsUtility ? savedStateId : currentStateId;
}
