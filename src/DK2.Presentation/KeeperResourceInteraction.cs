// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

public enum KeeperResourceKind { None, Heart, Mana, Payday, Gold }
[Flags]
public enum KeeperResourceModifiers { None = 0, Shift = 1, Control = 2, Alt = 4 }

public static class KeeperResourceInteraction
{
    public static KeeperResourceKind HitTest(Vector2 fromTopLeft, float viewportHeight)
    {
        if (viewportHeight <= 0) return KeeperResourceKind.None;
        Vector2 point = fromTopLeft * (1920f / viewportHeight);
        if (Inside(point, 28, 48, 100, 100)) return KeeperResourceKind.Heart;
        if (Inside(point, 140, 20, 360, 88)) return KeeperResourceKind.Mana;
        if (Inside(point, 540, 48, 100, 100)) return KeeperResourceKind.Payday;
        if (Inside(point, 680, 20, 400, 88)) return KeeperResourceKind.Gold;
        return KeeperResourceKind.None;
    }

    public static int TooltipTextId(KeeperResourceKind kind) => kind switch
    {
        KeeperResourceKind.Heart => 2139, KeeperResourceKind.Mana => 2133,
        KeeperResourceKind.Payday => 2143, KeeperResourceKind.Gold => 2141, _ => 0,
    };

    public static GoldAmount WithdrawAmount(KeeperResourceModifiers modifiers) => new(modifiers switch
    {
        KeeperResourceModifiers.Shift => 1000, KeeperResourceModifiers.Control => 10000, _ => 100,
    });

    public static WithdrawCounterGoldCommand Withdraw(PlayerId player, KeeperResourceModifiers modifiers)
        => new(player, WithdrawAmount(modifiers));

    public static HoverTextSnapshot ResourceHover(KeeperResourceSnapshot snapshot, KeeperResourceKind kind,
        Func<uint, string> resolve)
    {
        int id = TooltipTextId(kind);
        if (id == 0) return HoverTextSnapshot.Empty;
        string description = resolve((uint)(id + 1));
        if (kind == KeeperResourceKind.Heart)
        {
            description = snapshot.HeartTile is null || snapshot.HeartHealth.Maximum <= 0 ? ""
                : WorldHoverText.Substitute(description, new Dictionary<int, string>
                {
                    [19] = unchecked(snapshot.HeartHealth.Value * 100 / snapshot.HeartHealth.Maximum)
                        .ToString(CultureInfo.InvariantCulture),
                });
        }
        else if (kind == KeeperResourceKind.Payday)
            description = WorldHoverText.Substitute(description, new Dictionary<int, string>
            { [20] = snapshot.WageBill.Value.ToString(CultureInfo.InvariantCulture) });
        return new HoverTextSnapshot(description, resolve((uint)id),
            new WorldHoverHit(WorldHoverKind.Widget, default, id));
    }

    private static bool Inside(Vector2 point, int x, int y, int width, int height)
        => point.X >= x && point.Y >= y && point.X < x + width && point.Y < y + height;
}
