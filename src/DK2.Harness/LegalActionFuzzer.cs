// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Harness;

/// <summary>
/// A random player limited to what a human can do (ADR-0552 Stage C): each decision it picks one
/// action category by weight and builds that command the way the human path does: dragged
/// rectangles for tagging, building and selling, the HUD's spells, doors and traps only while
/// their panel choice is selectable, and the hand on its own creatures. It reads only its
/// <see cref="KeeperView"/>, draws only from its own <see cref="SimRandom"/>, and refuses to emit
/// a command <see cref="HumanCommandPolicy.AllowsInPlay"/> rejects. Narrower than a human: no
/// specials, no picking up objects or store gold, and no tagging under fog (the view lists none
/// of them). A withdrawal puts gold in the hand, which a later drop lands.
/// </summary>
public sealed class LegalActionFuzzer
{
    /// <summary>Bumped when the action menu or weights change; it is part of the controller's name.</summary>
    public const int Version = 1;

    private enum Action { Tag, Untag, Build, Sell, CreateImp, PickUp, Drop, Slap, Cast, Door, Trap, Withdraw }

    private static readonly (Action Action, int Weight)[] Menu =
    [
        (Action.Tag, 6), (Action.Untag, 1), (Action.Build, 4), (Action.Sell, 1), (Action.CreateImp, 2),
        (Action.PickUp, 2), (Action.Drop, 4), (Action.Slap, 2), (Action.Cast, 2), (Action.Door, 1),
        (Action.Trap, 1), (Action.Withdraw, 1),
    ];

    private static readonly int TotalWeight = Menu.Sum(m => m.Weight);
    private const byte CreateImpSpell = 4;
    private const byte HornedReaperSpell = 5, OneShotReaperSpell = 14; // the panel's Reaper button, not a spell choice

    private readonly SimRandom _rng;

    private LegalActionFuzzer(ulong seed, PlayerId player) => _rng = new SimRandom(seed ^ player.Value);

    /// <summary>A controller for <paramref name="player"/>, seeded as the computer keeper is
    /// (seed xor player id), deciding every <paramref name="intervalTicks"/> ticks.</summary>
    public static ScenarioController Create(PlayerId player, ulong seed, int intervalTicks = 4)
    {
        // Player 1 is the heroes and 2 the neutrals; only a keeper has a human's inputs.
        if (!PlayerIds.IsKeeper(player.Value))
            throw new ArgumentException($"Player {player.Value} is not a keeper (keepers are {PlayerIds.FirstKeeper} and up).", nameof(player));
        var fuzzer = new LegalActionFuzzer(seed, player);
        return new ScenarioController(
            $"fuzz-v{Version}.{HumanCommandPolicy.Version}-s{seed}", player, fuzzer.Decide)
        { IntervalTicks = intervalTicks };
    }

    private IReadOnlyList<PlayerCommand> Decide(KeeperView view)
    {
        if (view.Status is PlayerStatus.Conquered or PlayerStatus.Lost) return [];
        // Three tries: a category with nothing to act on gives way to another draw.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (Build(view, Draw()) is not PlayerCommand command) continue;
            if (!HumanCommandPolicy.AllowsInPlay(command))
                throw new InvalidOperationException($"The fuzzer built {command.GetType().Name}, which no human issues in play.");
            return [command];
        }
        return [];
    }

    private Action Draw()
    {
        int roll = _rng.NextInt(TotalWeight);
        foreach ((Action action, int weight) in Menu)
        {
            if (roll < weight) return action;
            roll -= weight;
        }
        return Menu[^1].Action;
    }

    private PlayerCommand? Build(KeeperView view, Action action)
    {
        PlayerId me = view.PlayerId;
        switch (action)
        {
            case Action.Tag:
                // PlayerInputSnapshot.Capture: a tile is a dig target when its terrain is taggable.
                return Pick(Tiles(view, p => view.Terrain(p).IsTaggable && !view.Tile(p).IsTaggedBy(me) && view.TouchesOwnFloor(p)))
                    is TilePos t ? TagTilesCommand.Rect(me, t.X, t.Y, t.X, t.Y) : null;
            case Action.Untag:
                return Pick(Tiles(view, p => view.Tile(p).IsTaggedBy(me)))
                    is TilePos u ? TagTilesCommand.Rect(me, u.X, u.Y, u.X, u.Y, tagged: false) : null;
            case Action.Build:
                return BuildRoom(view);
            case Action.Sell:
                KeeperRoomView[] rooms = view.OwnRooms.Where(r => r.Definition.IsBuildable).ToArray();
                if (rooms.Length == 0) return null;
                KeeperRoomView room = rooms[_rng.NextInt(rooms.Length)];
                TilePos s = view.FromIndex(room.TileIndices[_rng.NextInt(room.TileIndices.Count)]);
                return SellRoomCommand.Rect(me, s.X, s.Y, s.X, s.Y);
            case Action.CreateImp:
                return Selectable(view, AvailabilityKind.Spell, CreateImpSpell) ? new CreateImpCommand(me) : null;
            case Action.PickUp:
                return Pick(view.Creatures.Where(c => c.OwnerPlayerId == me.Value && c.CanPickUp && !c.InHand).ToArray())
                    is KeeperCreatureView pick ? new PickUpCreatureCommand(me, pick.Id) : null;
            case Action.Drop:
                // The human drop always carries the pointer's point; the tile centre stands in for it.
                return view.HandCount > 0 && Pick(Tiles(view, p => view.IsMine(p) && view.IsPassable(p))) is TilePos d
                    ? new DropCommand(me, d, 0, new FpPosition2(new Fp32(CreatureState.CentreOf(d.X)), new Fp32(CreatureState.CentreOf(d.Y))))
                    : null;
            case Action.Slap:
                // The right button slaps only with an empty hand; holding something, it drops.
                if (view.HandCount > 0) return null;
                return Pick(view.Creatures.Where(c => c.OwnerPlayerId == me.Value && c.CanSlap && !c.InHand).ToArray())
                    is KeeperCreatureView slap ? new SlapCreatureCommand(me, slap.Id) : null;
            case Action.Cast:
                return Cast(view);
            case Action.Door:
                // The HUD places neither a door nor a trap until the keeper owns a Workshop.
                if (!OwnsWorkshop(view)) return null;
                return Pick(view.Database.Doors.Values.Where(d => Selectable(view, AvailabilityKind.Door, d.DoorId))
                        .OrderBy(d => d.DoorId).ToArray()) is DoorDef door
                    && Pick(Tiles(view, p => view.IsMine(p) && view.IsPassable(p))) is TilePos dt
                    ? new PlaceDoorCommand(me, dt, door.DoorId)
                    : null;
            case Action.Trap:
                if (!OwnsWorkshop(view)) return null;
                return Pick(view.Database.Traps.Values.Where(t => BottomPanelView.IsOnPanel(t) && Selectable(view, AvailabilityKind.Trap, t.TrapId))
                        .OrderBy(t => t.TrapId).ToArray()) is TrapDef trap
                    && Pick(Tiles(view, p => view.IsMine(p) && view.IsPassable(p))) is TilePos tt
                    ? new PlaceTrapCommand(me, tt, trap.TrapId)
                    : null;
            case Action.Withdraw:
                return view.Gold > 0 ? new WithdrawCounterGoldCommand(me, new GoldAmount(100)) : null;
            default:
                return null;
        }
    }

    /// <summary>A selectable, buildable room dragged over a rectangle of up to 3x3 whose every tile
    /// the view says can take it.</summary>
    private PlayerCommand? BuildRoom(KeeperView view)
    {
        RoomDef[] rooms = view.Database.Rooms.Values
            .Where(r => r.IsBuildable && Selectable(view, AvailabilityKind.Room, r.RoomId))
            .OrderBy(r => r.RoomId).ToArray();
        if (Pick(rooms) is not RoomDef room) return null;
        int w = 1 + _rng.NextInt(3), h = 1 + _rng.NextInt(3);
        if (Pick(Tiles(view, p => Fits(view, room, p, w, h))) is not TilePos at) return null;
        return BuildRoomCommand.Rect(view.PlayerId, (RoomTypeId)room.RoomId, at.X, at.Y, at.X + w - 1, at.Y + h - 1);
    }

    private static bool Fits(KeeperView view, RoomDef room, TilePos at, int w, int h)
    {
        for (int y = at.Y; y < at.Y + h; y++)
            for (int x = at.X; x < at.X + w; x++)
                if (view.CanBuild(new TilePos(x, y), room) != RoomPlacement.Allowed) return false;
        return true;
    }

    /// <summary>The HUD's dispatch: a spell that targets a thing is cast on a creature, any other at
    /// the centre of a tile (<c>DungeonRunner.Hud.cs</c>). Create Imp and the Reaper have their own tools.</summary>
    private PlayerCommand? Cast(KeeperView view)
    {
        KeeperSpellDef[] spells = view.Database.KeeperSpells.Values
            .Where(s => s.KeeperSpellId is not (CreateImpSpell or HornedReaperSpell or OneShotReaperSpell)
                && Selectable(view, AvailabilityKind.Spell, s.KeeperSpellId) && s.ManaCost <= view.Mana)
            .OrderBy(s => s.KeeperSpellId).ToArray();
        if (Pick(spells) is not KeeperSpellDef spell) return null;
        if (KeeperSpellRules.TargetsAThing(spell.TargetSelector))
            // A creature under the pointer: not one in a hand, not a corpse.
            return Pick(view.Creatures.Where(c => !c.InHand && c.Activity != CreatureActivity.Dead).ToArray()) is KeeperCreatureView c
                ? new CastSpellOnCreatureCommand(view.PlayerId, spell.KeeperSpellId, c.Id)
                : null;
        TilePos at = view.FromIndex(_rng.NextInt(view.TileCount));
        return new CastSpellAtPlaceCommand(view.PlayerId, spell.KeeperSpellId, CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y));
    }

    private static bool Selectable(KeeperView view, AvailabilityKind kind, int typeId) =>
        BottomPanelView.IsSelectable(view.Availability(kind, typeId));

    private static bool OwnsWorkshop(KeeperView view) =>
        view.OwnRooms.Any(r => r.Definition.RoomId == (byte)RoomTypeId.Workshop);

    private static TilePos[] Tiles(KeeperView view, Func<TilePos, bool> where)
    {
        var tiles = new List<TilePos>();
        for (int i = 0; i < view.TileCount; i++)
        {
            TilePos p = view.FromIndex(i);
            if (where(p)) tiles.Add(p);
        }
        return tiles.ToArray();
    }

    private T? Pick<T>(T[] items) where T : struct => items.Length == 0 ? null : items[_rng.NextInt(items.Length)];

    private T? Pick<T>(IReadOnlyList<T> items) where T : class =>
        items.Count == 0 ? null : items[_rng.NextInt(items.Count)];
}
