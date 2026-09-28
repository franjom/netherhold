// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.AI;

/// <summary>The keeper's three response slots and event-1 allowance/residual rules.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-threat.md")]
public sealed class KeeperResponseQueue
{
    private readonly KeeperThreatResponse[] _slots = new KeeperThreatResponse[3];
    private int _head;

    public int Count { get; private set; }
    public KeeperThreatResponse? Current => Count == 0 ? null : _slots[_head];

    /// <summary>False leaves the message unhandled. A cancelled reservation is still handled.
    /// Evaluation may reroll imprisonment; the full-queue gate must precede it.</summary>
    public bool TryReceive(Func<KeeperThreatResponse?> evaluate)
    {
        if (Count == _slots.Length) return false;
        KeeperThreatResponse? response = evaluate();
        if (response.HasValue)
        {
            _slots[(_head + Count) % _slots.Length] = response.Value;
            Count++;
        }
        return true;
    }

    /// <summary>Dispatches only the head event. The caller selects a creature anew on each
    /// attempt and supplies the interaction outcome, including any replacement target.</summary>
    public byte Dispatch(byte allowance, Func<KeeperThreatResponse, KeeperResponseAttempt> attempt)
    {
        if (Count == 0 || allowance == 0) return allowance;
        bool withdrawing = _slots[_head].RemainingThreat < 0;
        while (allowance > 0)
        {
            allowance--;
            KeeperThreatResponse response = _slots[_head];
            KeeperResponseAttempt result = attempt(response);
            if (!result.CandidateFound)
            {
                Retire();
                break;
            }
            if (result.Threat is int threat)
            {
                short residual = unchecked((short)(withdrawing
                    ? response.RemainingThreat + threat : response.RemainingThreat - threat));
                response = new KeeperThreatResponse(residual, result.ReplacementSite ?? response.Position);
                _slots[_head] = response;
            }
            if (withdrawing ? response.RemainingThreat >= 0 : response.RemainingThreat <= 0)
            {
                Retire();
                break;
            }
        }
        // 0x004FE0B0 returns AL=0 even when retiring before the allowance is spent.
        return 0;
    }

    private void Retire()
    {
        _slots[_head] = default;
        _head = (_head + 1) % _slots.Length;
        Count--;
    }
}

/// <summary>Null threat means interaction permission failed. Non-null threat is accounted
/// even when pickup/drop fails or the original's owned-target branch performs no move.</summary>
public readonly record struct KeeperResponseAttempt(
    bool CandidateFound, int? Threat, TilePos? ReplacementSite = null);
