// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

public enum KeeperResourceFlash { None, Mana, Gold }
public enum KeeperGoldColour { Money, AffordableBill, UnaffordableBill }

public readonly record struct KeeperResourceFrame(
    GoldAmount GoldNumber, KeeperGoldColour GoldColour, bool ShowGoldNumber, bool ShowManaNumbers,
    bool ShowHeartAlarm);

/// <summary>Native real-clock flash phases and game-clock alarm/warning windows (hud-resources.md).</summary>
public sealed class KeeperResourceAnimation
{
    private bool _paydayWarningSeen;

    public KeeperResourceFrame Evaluate(KeeperResourceSnapshot snapshot, uint milliseconds,
        KeeperResourceFlash flash = KeeperResourceFlash.None)
    {
        bool blank = ((milliseconds / 250) & 1) != 0;
        bool showGold = flash != KeeperResourceFlash.Gold || !blank;
        // Native countdown comparison is unsigned: an unarmed/passed deadline does not warn.
        long remaining = snapshot.NextPaydayTick.Value - snapshot.Tick.Value;
        bool beforePayday = remaining >= 0
            && remaining < snapshot.PaydayCutoffTicks;
        if (showGold && beforePayday) _paydayWarningSeen = true;
        long sincePayday = snapshot.Tick.Value - snapshot.LastPaydayTick.Value;
        bool afterPayday = _paydayWarningSeen && snapshot.LastPaydayTick.Value != 0
            && sincePayday >= 0 && sincePayday < 10 * SimulationTiming.TicksPerSecond;
        bool bill = (beforePayday || afterPayday) && ((milliseconds / 500) & 1) != 0;
        return new KeeperResourceFrame(bill ? new GoldAmount(-snapshot.WageBill.Value) : snapshot.Gold,
            !bill ? KeeperGoldColour.Money : snapshot.Gold.Value < snapshot.WageBill.Value
                ? KeeperGoldColour.UnaffordableBill : KeeperGoldColour.AffordableBill,
            showGold, flash != KeeperResourceFlash.Mana || !blank,
            snapshot.Tick.Value < snapshot.HeartAlarmUntil.Value
                && (snapshot.Tick.Value / SimulationTiming.OriginalTickPeriodTicks & 1) != 0);
    }
}
