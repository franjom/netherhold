// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>What one held-item icon shows: a creature type's GUI icon (Creatures.kwd Icon1Resource,
/// file <c>+0xC44</c>) or an object type's (Objects.kwd GuiIconResource, file <c>+0x74</c>).</summary>
public enum HeldIconKind : byte { Creature, Object }

/// <summary>One held-item icon: its kind and the creature or object type id that names its art.</summary>
public readonly record struct HeldIcon(HeldIconKind Kind, byte TypeId);

/// <summary>The Hand's held-item icons beside the pointer (<c>0x0040CFE0</c>, each drawn by
/// <c>0x0040D450</c>; docs/research/placement-cursor.md §3): the hand's things from the top down, at
/// most <see cref="MaxIcons"/>, in a 4×2 grid of 32-pixel steps from the tool-icon anchor, and past
/// that <c>gui\Creatures\more_creatures</c> at (128, 32). A buffer the caller refreshes per frame.</summary>
public sealed class HeldItemStack
{
    /// <summary>The icons <c>0x0040CFE0</c> draws before it shows the overflow icon.</summary>
    public const int MaxIcons = 8;

    /// <summary>The overflow icon, interface art catalogue entry 70 (<c>+0x12B6 + 0x762</c>).</summary>
    public const string MoreIconName = @"gui\Creatures\more_creatures";

    /// <summary><c>gui\cursors\I-Dismiss</c>, icon 1 of the cursor table <c>0x0068F978</c>: the red bin.</summary>
    public const string DisposeIconName = @"GUI\Cursors\I-Dismiss";

    private readonly List<HeldIcon> _icons = new(MaxIcons);

    /// <summary>The icons, the top of the hand first.</summary>
    public IReadOnlyList<HeldIcon> Icons => _icons;

    /// <summary>Whether the hand holds more than <see cref="MaxIcons"/> things.</summary>
    public bool More { get; private set; }

    /// <summary>Icon <paramref name="index"/>'s offset from the anchor in DK2's unscaled pixels, from the
    /// table <c>0x0040AA40</c> fills at <c>0x006CE910</c>; <see cref="MaxIcons"/> is the overflow icon's.</summary>
    public static (int X, int Y) Offset(int index) =>
        index >= MaxIcons ? (128, 32) : (32 * (index % 4), 32 * (index / 4));

    /// <summary>Reads <paramref name="player"/>'s hand. DK2 walks the hand from its last slot down
    /// (slot <c>+0x274</c>) after the drops still waiting for the simulation (interface list
    /// <c>+0x142</c>), which ours does not have. A dead body would take a slot and draw nothing; our
    /// hand holds none.</summary>
    public void Refresh(WorldState world, PlayerId player)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        _icons.Clear();
        List<HandItem> hand = world.Player(player).Hand;
        More = hand.Count > MaxIcons;
        for (int i = hand.Count - 1; i >= 0 && _icons.Count < MaxIcons; i--)
        {
            HandItem item = hand[i];
            if (item.IsObject)
            {
                if (world.ObjectById(item.Id) is ObjectState thing) _icons.Add(new HeldIcon(HeldIconKind.Object, thing.ObjectTypeId));
            }
            else if (world.CreatureById(item.Id) is CreatureState creature)
                _icons.Add(new HeldIcon(HeldIconKind.Creature, creature.CreatureTypeId));
        }
    }

    /// <summary>Whether the bin shows: our click disposes of the top item here, and DK2's bit 0x04 rule
    /// agrees. The click's rule is <see cref="HandRules.CreatureLands"/>' <c>disposed</c>
    /// (<see cref="DisposalRules.Disposes"/>, never a prisoner, the Heart not ruined), so the cursor
    /// never promises a disposal the click does not make. DK2's rule (<c>0x0040AE80</c>) only narrows
    /// it: the creature is the player's own, and an IsWorker+IsEvil creature is disposed over the
    /// player's own Heart centre (tile code <c>0x40</c>), any other over its own Portal centre (code 4).
    /// Where the two disagree the cursor follows the click: a non-evil worker over its own Portal
    /// centre gets DK2's bin but our No Go, since <see cref="DisposalRules"/> keys the Heart on
    /// IsWorker alone and our landing guard refuses that drop. Whether DK2 keeps cell <c>0x40</c>, and
    /// so its bin, once the Heart is ruined is unread; ours shows none.</summary>
    public static bool Disposes(WorldState world, GameDatabase database, PlayerId player, HandItem item, TilePos at)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (item.IsObject || world.CreatureById(item.Id) is not CreatureState creature) return false;
        if (creature.OwnerPlayerId != player.Value) return false;
        HandRules.CreatureLands(world, database, player, creature, at, out bool disposed);
        if (!disposed) return false;
        CreatureFlags flags = database.CreatureById(creature.CreatureTypeId).Flags;
        bool evilWorker = (flags & (CreatureFlags.IsWorker | CreatureFlags.IsEvil)) == (CreatureFlags.IsWorker | CreatureFlags.IsEvil);
        return evilWorker
            ? DisposalRules.IsHeartCentreTile(world, database, at, player)
            : PortalRules.IsHoleTile(world, database, at, player);
    }
}
