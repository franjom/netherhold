// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

public readonly record struct ManaPressureState(
    uint WarningDeadline = 0, int WarningStage = 0,
    bool WarningsSuppressed = false, bool RemovalEnabled = false,
    uint ScanDeadline = 0);

public readonly record struct ManaPressureResult(
    ManaPressureState State, int Warning, bool ScanForRemoval);

/// <summary>One already-owned creature; positions use 4096 units per tile.
/// Idle is the stored original flag, not a derived activity category.</summary>
public readonly record struct ManaRemovalCandidate(
    int Id, byte TypeId, byte Level, bool EvilWorker, bool InHand,
    bool Idle, int PosX, int PosY);

/// <summary>Expiry zeroes health; the later creature cleanup pass owns deletion.</summary>
public enum ManaRemovalAction { None, Cancel, ZeroHealth }

/// <summary>Verified mana-pressure arithmetic, awaiting live lifecycle integration (ADR-0266).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-mana-removal.md")]
public static class ManaPressureRules
{
    /// <summary>Called after a due mana payment, using its net rate before clamping.
    /// Warning is zero or the data1 value for message 52. A scan result advances
    /// its deadline even when the subsequent creature selection fails.</summary>
    public static ManaPressureResult Advance(
        ManaPressureState state, uint now, int mana, int netRate,
        uint evilWorkers, uint minimumWorkers, ushort warningThreshold,
        byte intervalSeconds, uint ticksPerSecond)
    {
        uint interval = unchecked(intervalSeconds * ticksPerSecond);
        int warning = 0;
        if (mana >= warningThreshold)
        {
            state = state with
            {
                WarningDeadline = 0, WarningsSuppressed = false, RemovalEnabled = false
            };
        }
        else if (netRate <= 0 && !state.WarningsSuppressed)
        {
            if (state.WarningDeadline == 0)
                state = state with { WarningStage = 1, WarningDeadline = unchecked(now + interval) };
            else if (state.WarningDeadline <= now)
            {
                warning = state.WarningStage == 4 && evilWorkers <= minimumWorkers
                    ? 3 : state.WarningStage;
                int nextStage = warning + 1;
                state = nextStage < 5
                    ? state with
                    {
                        WarningStage = nextStage,
                        WarningDeadline = unchecked(state.WarningDeadline + interval)
                    }
                    : state with
                    {
                        WarningStage = nextStage, WarningsSuppressed = true, RemovalEnabled = true
                    };
            }
        }

        bool scan = state.RemovalEnabled && state.ScanDeadline < now;
        if (scan) state = state with { ScanDeadline = unchecked(now + interval) };
        return new(state, warning, scan);
    }

    /// <summary>Input order must be the original owned-creature list order.
    /// The unique lowest-level path intentionally allows a non-imp.</summary>
    public static int? SelectForRemoval(
        IReadOnlyList<ManaRemovalCandidate> owned, uint minimumWorkers,
        ushort heartTileX, ushort heartTileY)
    {
        uint workers = 0;
        foreach (var creature in owned)
            if (creature.EvilWorker) workers++;
        if (workers <= minimumWorkers) return null;

        byte lowest = byte.MaxValue;
        int? choice = null;
        bool tied = false;
        foreach (var creature in owned)
        {
            if (creature.InHand) continue;
            if (creature.Level < lowest)
            {
                lowest = creature.Level;
                choice = creature.Id;
                tied = false;
            }
            else if (creature.Level == lowest) tied = true;
        }
        if (choice.HasValue && !tied) return choice;

        foreach (var creature in owned)
            if (!creature.InHand && creature.TypeId == 1 && creature.Idle)
                return creature.Id;

        int centerX = heartTileX * 4096 + 2048;
        int centerY = heartTileY * 4096 + 2048;
        uint farthest = 0;
        choice = null;
        foreach (var creature in owned)
        {
            if (creature.InHand || creature.TypeId != 1) continue;
            long dx = unchecked(creature.PosX - centerX);
            long dy = unchecked(creature.PosY - centerY);
            uint distance = unchecked((uint)(dx * dx >> 12) + (uint)(dy * dy >> 12));
            if (distance <= farthest) continue;
            farthest = distance;
            choice = creature.Id;
        }
        return choice;
    }

    public static ManaRemovalAction Countdown(
        byte typeId, bool possessed, uint started, uint now,
        int mana, ushort impUpkeep, uint ticksPerSecond)
    {
        if (typeId != 1 || possessed || started == 0) return ManaRemovalAction.None;
        if (impUpkeep == 0 || mana >= impUpkeep) return ManaRemovalAction.Cancel;
        return unchecked(started + 5 * ticksPerSecond) <= now
            ? ManaRemovalAction.ZeroHealth : ManaRemovalAction.None;
    }
}
