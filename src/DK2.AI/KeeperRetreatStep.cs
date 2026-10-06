// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The keeper's retreat of the wounded (<c>0x004FF260</c>), run at step 7 after the Call to
/// Arms removal test while allowance remains: every own creature the branches select is lifted and
/// put down in a random own Lair, or at the heart without one, one allowance each.</summary>
/// <remarks>
/// <para>Built: the Combat Pit fighter's half-health and skill-level branches, the fighting
/// creature's threshold branch, the dropped-counter guard, the other creature's threshold branch
/// with its six exclusions, the Heart requirement and the allowance accounting.</para>
/// <para>Deviates: the pick-up and drop are commands executed a tick later, so the drop is issued
/// only when the pick-up is predicted from the captured permission, the allowance is spent as
/// DK2's is either way, and a creature predicted prepared leaves the tick's fighters. The Lair is
/// drawn with the keeper's own RNG; its drop tile is the bounding-box middle (ADR-0284).</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-threat.md")]
public sealed class KeeperRetreatStep
{
    /// <summary>A creature put down at most this many turns ago is left to the Pit branches once
    /// the game turn is past the same number (<c>0x004FF2FA</c>'s guard, as <c>0x004FE189</c>).</summary>
    public const int DroppedGraceTurns = KeeperThreatResponder.DroppedGraceTurns;

    private readonly KeeperView _view;
    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly long _turn;

    public KeeperRetreatStep(KeeperView view, KeeperAIParameters parameters, SimRandom rng, List<PlayerCommand> commands)
    {
        _view = view;
        _p = parameters;
        _rng = rng;
        _commands = commands;
        // DK2's first game turn is 1; the observation's tick is the turn about to run.
        _turn = view.Tick.Value + 1;
    }

    /// <summary>The creatures this tick's pick-up commands are predicted to prepare: in DK2 they sit
    /// in state 75 or 78 for the rest of the tick, outside every category the later steps walk,
    /// whether or not the hand then takes them.</summary>
    public HashSet<int> Prepared { get; } = new();

    /// <summary>Walks the owned list while allowance remains. A selected creature costs one allowance
    /// once an own Heart room exists, whether or not the hand predicate admits it; without a Heart
    /// nothing is attempted and nothing is spent.</summary>
    public int Run(int allowance)
    {
        foreach (KeeperCreatureView creature in Owned(_view.PlayerId))
        {
            if (allowance <= 0) break;
            if (!Selected(creature)) continue;
            // 0x004C6460(5): a random own Heart room; the drop is at the heart position, not its centre.
            if (_view.OwnRoomOfType((byte)RoomTypeId.DungeonHeart) is null) continue;
            if (creature.PickupPermitted)
            {
                // 0x0048E6A0 passed: 0x004BFBD0 prepares it, 0x004BC500 lifts it unless the hand
                // refuses (CanPickUp), and 0x004BCAE0's answer decides the drop. A refused lift
                // still spends the allowance and still leaves the creature prepared.
                _commands.Add(new PickUpCreatureCommand(_view.PlayerId, creature.Id));
                Prepared.Add(creature.Id);
                if (creature.CanPickUp) _commands.Add(new DropCommand(_view.PlayerId, Destination()));
            }
            allowance--;
        }
        return allowance;
    }

    /// <summary>The branches in DK2's order. A Pit fighter below half its level-scaled health is
    /// taken outright; then anyone below <c>BARetreatHealth</c> percent who fights through a walk;
    /// then, unless put down within the grace once the level is past it, another creature below
    /// the percentage that is not in a Lair or Combat category, not in this hand, not landing
    /// (76), not stunned and not attached to a room; a Pit fighter otherwise goes by level.</summary>
    public bool Selected(KeeperCreatureView creature)
    {
        int max = creature.MaxHealth;
        if (creature.CombatPitFighter && creature.Health < (int)((uint)max >> 1)) return true;

        // DK2 multiplies the byte by the unsigned maximum and divides unsigned; the health is signed.
        int threshold = (int)(((uint)_p.BARetreatHealth * (uint)max) / 100);
        bool wounded = creature.Health < threshold;
        CreatureStateCategory category = CreatureStateTable.Get(creature.ResolvedStateId).Category;
        if (wounded && category == CreatureStateCategory.Combat) return true;

        if (creature.LastDroppedCounter <= DroppedGraceTurns && _turn > DroppedGraceTurns)
            return creature.CombatPitFighter && SkilledEnough(creature);
        if (!creature.CombatPitFighter)
            return wounded && category != CreatureStateCategory.Lair
                && !creature.InMyHand && creature.StateId != DropLandingRules.LandingState
                && !KeeperThreatResponder.IsStunned(creature.StateId) && !creature.AttachedToRoom;
        return SkilledEnough(creature);
    }

    /// <summary>The Pit fighter's level is at least <c>IACreatureSkillLevel</c>, the four-bit field.</summary>
    private bool SkilledEnough(KeeperCreatureView creature) => creature.Level >= (_p.IACreatureSkillLevel & 0xF);

    /// <summary><c>0x004C6460(2)</c>: a random own Lair's stored position (<c>+0x27</c>, read at
    /// <c>0x004FF260</c>); none, and the heart position.</summary>
    private TilePos Destination()
    {
        // 0x004C6460(2) walks the player's list newest first, as RandomRoomOfType does.
        KeeperRoomView? lair = _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Lair, _rng);
        return lair is null ? _view.Heart!.Value : _view.PositionOf(lair);
    }

    /// <summary>An owner's creature list in DK2's order, held ones included; a corpse is a different thing there.</summary>
    private IEnumerable<KeeperCreatureView> Owned(PlayerId player) =>
        _view.Creatures.Where(c => c.OwnerPlayerId == player && c.OwnershipRank >= 0 && c.Activity != CreatureActivity.Dead)
            .OrderBy(c => c.OwnershipRank);
}
