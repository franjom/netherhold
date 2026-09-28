// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.AI;

/// <summary>Arithmetic and selection shared by the keeper's two threat readers.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-threat.md")]
public static class ThreatEvaluator
{
    public static int CreatureThreat(ushort baseThreat, byte level, short levelPercent) =>
        level == 1 ? baseThreat : baseThreat * levelPercent / 100;

    /// <summary>Raw 12-bit fixed point; preserves the original's overflowing radius square.</summary>
    public static bool WithinRadius(int x, int y, int centreX, int centreY, int radius)
    {
        long dx = (long)x - centreX, dy = (long)y - centreY;
        uint distance = unchecked((uint)(dx * dx >> 12) + (uint)(dy * dy >> 12));
        uint limit = unchecked((uint)((radius * radius) >> 12));
        return distance < limit;
    }

    public static int LocalStrength(IEnumerable<KeeperThreatSource> sources,
        byte playerId, int centreX, int centreY, int radius, bool includeTraps)
    {
        int sum = 0;
        foreach (KeeperThreatSource source in sources)
        {
            if (source.OwnerPlayerId != playerId || source.MapLayer == 2) continue;
            if (source.IsTrap && (!includeTraps || source.TrapState == 2)) continue;
            if (!WithinRadius(source.X, source.Y, centreX, centreY, radius)) continue;
            sum = unchecked(sum + source.Threat);
        }
        return sum;
    }

    public static KeeperThreatAssessment Assess(IReadOnlyList<KeeperThreatSource> sources,
        IEnumerable<byte> playerOrder, Func<byte, bool> allied, TilePos target,
        bool roomMessage, bool trapsStartFights)
    {
        int enemy = 0, friendly = 0;
        byte? attacker = null;
        int x = target.X * Fp32.One + Fp32.One / 2;
        int y = target.Y * Fp32.One + Fp32.One / 2;
        foreach (byte player in playerOrder)
        {
            bool friend = allied(player);
            int radius = roomMessage ? 5 : friend ? 4 : 10;
            int strength = LocalStrength(sources, player, x, y, radius * Fp32.One,
                !roomMessage && trapsStartFights);
            if (friend) friendly = unchecked(friendly + strength);
            else
            {
                enemy = unchecked(enemy + strength);
                if (strength != 0) attacker = player;
            }
        }
        return new KeeperThreatAssessment(enemy, friendly, attacker);
    }

    /// <summary>The caller supplies the audited ownership, bounds and navigation predicate.</summary>
    public static TilePos? DropSite(TilePos target, bool roomMessage, Func<TilePos, bool> eligible)
    {
        int bestScore = 0x19000;
        TilePos? best = null;
        for (int y = target.Y - 5; y <= target.Y + 5; y++)
        for (int x = target.X - 5; x <= target.X + 5; x++)
        {
            int dx = target.X - x, dy = target.Y - y;
            int score = unchecked(dx * dx + (roomMessage ? y * target.Y * dy : dy * dy));
            var tile = new TilePos(x, y);
            if (score >= bestScore || !eligible(tile)) continue;
            best = tile;
            bestScore = score;
        }
        // The original checks the chosen coordinates after the search, not each candidate.
        return best is TilePos p && p.X != 0 && p.Y != 0 ? best : null;
    }

    /// <summary>Null cancels a reservation; zero is a real reinforcement event.</summary>
    public static KeeperThreatResponse? Response(TilePos target, TilePos? dropSite,
        int enemyStrength, int allyStrength, ushort superiorityPercent,
        bool removeIfCannotReinforce, bool roomMessage)
    {
        int deficit = unchecked(superiorityPercent * enemyStrength) / 100 - allyStrength;
        TilePos position = dropSite ?? target;
        if (dropSite.HasValue && deficit >= 0)
            return new KeeperThreatResponse(unchecked((short)deficit), position);
        if (!dropSite.HasValue && removeIfCannotReinforce && allyStrength >= 0 && deficit > 0)
            return new KeeperThreatResponse(unchecked((short)-allyStrength), position);
        return roomMessage ? new KeeperThreatResponse(0, position) : null;
    }
}

/// <summary>Copied threat inputs; map layer 2 means off-map, not low health.</summary>
public readonly record struct KeeperThreatSource(
    byte OwnerPlayerId, int X, int Y, int Threat, int MapLayer,
    bool IsTrap = false, byte TrapState = 0);

public readonly record struct KeeperThreatResponse(short RemainingThreat, TilePos Position);

public readonly record struct KeeperThreatAssessment(int Enemy, int Allied, byte? AttackingPlayer);
