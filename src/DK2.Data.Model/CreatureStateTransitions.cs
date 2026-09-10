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
}

/// <summary>DK2's rule for whether a creature may move from one state to another, read whole
/// from the original's own transition routine (`creature-state-table.md` §2.2,
/// `state-categories.md`).
/// <para>Both halves are answered. The equal-tags half needs only the two bytes in
/// <see cref="CreatureStateTable"/>; the differing-tags half needs
/// <see cref="CreatureStateCategoryTable"/>, which is why this was partial until Q300 was
/// closed.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-categories.md")]
public static class CreatureStateTransitions
{
    /// <summary>The group tag that, paired with <see cref="PassthroughCategory"/>, is
    /// admitted whatever the current category.</summary>
    public const byte PassthroughGroup = 3;

    /// <summary>The category that <see cref="PassthroughGroup"/> admits regardless of the
    /// current category — the dying and frozen states. Tested before the refusal and the
    /// equality, but <b>inside</b> the equal-tags branch.</summary>
    public const CreatureStateCategory PassthroughCategory = CreatureStateCategory.Dying;

    /// <summary>Classify a move from <paramref name="currentStateId"/> to
    /// <paramref name="targetStateId"/>.
    /// <para><paramref name="utilityNewStateId"/> is the state object's
    /// <c>UtilityNewState</c>. It is consulted only when the group tags differ and the
    /// creature is in a movement state, and then it <b>replaces</b> the current state for
    /// the rest of the test — a creature mid-movement is judged by where the movement is
    /// going, not by the movement.</para></summary>
    public static StateTransition Classify(int currentStateId, int targetStateId, int utilityNewStateId)
    {
        ref readonly CreatureStateDef target = ref CreatureStateTable.Get(targetStateId);
        ref readonly CreatureStateDef current = ref CreatureStateTable.Get(currentStateId);

        if (target.Group == current.Group)
        {
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

        // Tags differ. A creature standing in a movement state is judged by the state that
        // movement is heading for, and only then against the target's entry mask.
        int fromId = current.IsUtility ? utilityNewStateId : currentStateId;
        CreatureStateCategory from = CreatureStateTable.Get(fromId).Category;

        if (from == target.Category)
        {
            return StateTransition.Allowed;
        }

        return CreatureStateCategoryTable.Get(target.Category)
            .AdmitsFrom(CreatureStateCategoryTable.Get(from))
            ? StateTransition.Allowed
            : StateTransition.Refused;
    }

    /// <summary>Which state the differing-tags half compares against. Exposed because the
    /// substitution is part of the rule, not an implementation detail: it is why a resume
    /// slot is a prerequisite for the gate and not only for resuming (ADR-0135).</summary>
    public static int ResolveComparisonState(int currentStateId, int utilityNewStateId) =>
        CreatureStateTable.Get(currentStateId).IsUtility ? utilityNewStateId : currentStateId;
}
