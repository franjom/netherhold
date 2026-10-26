// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>The tool selected by a local player. These are interface choices, not DK2 behaviour.</summary>
public enum PlayerToolMode : byte
{
    TagTiles,
    UntagTiles,
    BuildRoom,
    SellRoom,
    Hand,
    Slap,
    CreateImp,
}

/// <summary>Edges observed for the primary pointer button during one rendered frame.</summary>
[Flags]
public enum PointerButtonTransition : byte
{
    None = 0,
    Pressed = 1,
    Released = 2,
}

/// <summary>
/// One renderer-independent sample of local input. A host converts its pointer
/// coordinates to a tile and optional fixed-point point, and records button edges;
/// no camera or engine type crosses here.
/// </summary>
public readonly record struct PlayerIntent(
    PlayerId PlayerId,
    PlayerToolMode Tool,
    TilePos? PointerTile,
    PointerButtonTransition PrimaryButton,
    RoomTypeId RoomId = 0,
    PointerButtonTransition SecondaryButton = PointerButtonTransition.None,
    bool PointerOverHud = false,
    FpPosition2? PointerPosition = null);

public readonly record struct ExcavationTarget(TilePos Tile, bool IsTagged);

public enum PlayerInputTargetKind : byte
{
    Creature,
    Object,
    /// <summary>A special's box, which the hand activates and never lifts (specials.md §3).</summary>
    Special,
    /// <summary>A Treasury or Heart tile's store pile, a view decoration over the tile's gold
    /// (<see cref="WorldView.IsStorePile"/>); the hand lifts it by tile.</summary>
    StoreGold,
}

/// <summary>An entity the player's current view permits the pointer to target. <paramref name="Origin"/>
/// is the tile the entity itself is on, for a store pile, which the hand lifts by tile. <paramref name="TypeId"/>
/// is a special's object type; <paramref name="Own"/> marks a creature of the viewing keeper.</summary>
public readonly record struct PlayerInputTarget(
    PlayerInputTargetKind Kind,
    int Id,
    TilePos Tile,
    TilePos Origin = default,
    byte TypeId = 0,
    bool Own = false);

/// <summary>Tool mode 5 (<c>+0x13DB</c>): a special that needs a creature, waiting for the click that
/// names it (specials.md §3). <paramref name="TypeId"/> is DK2's subtype <c>+0x13EA</c>, the object
/// <c>+0x13E8</c>.</summary>
public readonly record struct SpecialTargeting(int ObjectId, byte TypeId)
{
    public const byte KillCreatures = 127, TransferCreature = 140;

    public static bool Needs(byte typeId) => typeId is KillCreatures or TransferCreature;

    /// <summary><c>0x00408D07</c>: Transfer Creature takes an own creature (hover kind 2), Kill
    /// Creatures any other (kind 3).</summary>
    public bool Accepts(in PlayerInputTarget creature) =>
        creature.Kind == PlayerInputTargetKind.Creature && creature.Own == (TypeId == TransferCreature);
}

/// <summary>The small, immutable part of the presented world needed to interpret a click.</summary>
public sealed class PlayerInputSnapshot
{
    private static readonly PlayerInputTarget[] NoTargets = [];

    public PlayerInputSnapshot(bool isHolding, IReadOnlyList<PlayerInputTarget>? targets = null,
        ExcavationTarget? excavation = null)
    {
        IsHolding = isHolding;
        Targets = targets ?? NoTargets;
        Excavation = excavation;
    }

    public bool IsHolding { get; }
    public IReadOnlyList<PlayerInputTarget> Targets { get; }
    public ExcavationTarget? Excavation { get; }

    public PlayerInputSnapshot ForHover(WorldHoverHit hit)
    {
        PlayerInputTargetKind? kind = hit.Kind switch
        {
            WorldHoverKind.Creature => PlayerInputTargetKind.Creature,
            WorldHoverKind.Object => PlayerInputTargetKind.Object,
            _ => null,
        };
        foreach (PlayerInputTarget target in Targets)
            if ((target.Kind == kind || kind == PlayerInputTargetKind.Object
                    && target.Kind is PlayerInputTargetKind.Special or PlayerInputTargetKind.StoreGold)
                && target.Id == hit.EntityId)
                return new PlayerInputSnapshot(IsHolding, [target with { Tile = hit.Tile }], Excavation);
        return new PlayerInputSnapshot(IsHolding, NoTargets, Excavation);
    }

    /// <summary>
    /// Captures only entities visible through <paramref name="view"/>. Held and
    /// worker-carried things are outside the dungeon and cannot be targeted, and with a
    /// <paramref name="database"/> neither is gold off the keeper's own land
    /// (<see cref="HandRules.MayReachObject"/>).
    /// </summary>
    public static PlayerInputSnapshot Capture(
        WorldView view, WorldState world, PlayerId playerId, GameDatabase? database = null,
        TilePos? pointerTile = null)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (world is null) throw new ArgumentNullException(nameof(world));

        bool isHolding = world.HasPlayer(playerId) && world.Player(playerId).Hand.Count > 0;
        var targets = new List<PlayerInputTarget>();

        foreach (CreatureView creature in view.Creatures(world))
        {
            if (world.CreatureById(creature.Id)?.InHand != false) continue;
            targets.Add(new PlayerInputTarget(PlayerInputTargetKind.Creature, creature.Id, creature.Tile,
                Own: world.CreatureById(creature.Id)!.OwnerPlayerId == playerId));
        }

        foreach (ObjectView instance in view.Objects(world))
        {
            ObjectState? thing = world.ObjectById(instance.Id);
            if (thing is null || thing.InHand || thing.CarriedByCreatureId >= 0) continue;
            if (database is not null && !HandRules.MayReachObject(world, database, thing, playerId)) continue;
            if (database?.ObjectById(thing.ObjectTypeId)?.IsSpecial == true)
            {
                // Hand_Click's object arm (0x00407AD5): a special is activated, only from a tile its
                // keeper owns; its +0x74 predicate (object +0x146 bit 1) is not mirrored.
                if (world.TileAtIndex(thing.TileIndex).OwnerPlayerId == playerId)
                    targets.Add(new PlayerInputTarget(PlayerInputTargetKind.Special, instance.Id, instance.Tile, TypeId: thing.ObjectTypeId));
                continue;
            }
            targets.Add(new PlayerInputTarget(
                PlayerInputTargetKind.Object, instance.Id, instance.Tile));
        }

        // The store piles, drawn from the tiles' gold: liftable from the keeper's own stores only.
        if (database is not null && world.HasPlayer(playerId))
            foreach (ObjectView pile in view.Decorations(world))
                if (WorldView.IsStorePile(pile.Id) && HandRules.MayReachStoreGold(world, pile.Tile, playerId)
                    && HandRules.CanPickUpStoreGold(world, database, pile.Tile, playerId))
                    targets.Add(new PlayerInputTarget(PlayerInputTargetKind.StoreGold, pile.Id, pile.Tile, pile.Tile));

        ExcavationTarget? excavation = null;
        if (pointerTile is TilePos at && world.Contains(at) && database is not null)
        {
            ref TileState tile = ref world.Tile(at);
            bool tagged = tile.IsTaggedBy(playerId);
            if (tagged || (world.FogOfWarEnabled && !tile.IsDiscoveredBy(playerId))
                || database.TerrainById(tile.TerrainId).IsTaggable)
                excavation = new ExcavationTarget(at, tagged);
        }
        return new PlayerInputSnapshot(isHolding, targets, excavation);
    }
}

/// <summary>
/// Turns pointer gestures into the command vocabulary accepted by the simulation.
/// It retains only an unfinished drag; all world-facing choices are supplied in a snapshot.
/// </summary>
public sealed class PlayerInputTranslator
{
    private DragStart? _drag;
    public void CancelPointerGesture() => _drag = null;

    /// <summary>The special waiting for its creature, or null (tool mode 5).</summary>
    public SpecialTargeting? Targeting { get; private set; }

    /// <summary>Hud_CancelSelectedTool (<c>0x004094C0</c>), which every tool selector calls first,
    /// Query and the panel's spells included: a pending special is dropped.</summary>
    public void CancelTool() => Targeting = null;

    /// <summary>The release clamps' limit, the database's <c>RubberBandAreaLimit</c>: the room release
    /// pulls the end toward the anchor (<see cref="PlacementCursor.ClampDrag"/>), the sell release
    /// keeps the low corner (<see cref="PlacementCursor.DragRectangle"/>). Unbounded until set.</summary>
    public int RubberBandLimit { get; set; } = int.MaxValue;

    public void SelectTool(PlayerId playerId, PlayerToolMode tool)
    {
        // Choosing a tool cancels the pending one first (Hud_CancelSelectedTool 0x004094C0).
        if (tool != PlayerToolMode.Hand) Targeting = null;
        if (_drag is DragStart drag && (drag.PlayerId != playerId || drag.Tool != tool)) _drag = null;
    }

    public InputFrame Translate(in PlayerIntent intent, PlayerInputSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        SelectTool(intent.PlayerId, intent.Tool);
        if (intent.PointerOverHud)
        {
            _drag = null;
            return InputFrame.Empty;
        }

        PlayerCommand? command = null;

        if ((intent.SecondaryButton & PointerButtonTransition.Pressed) != 0 && Targeting is not null)
        {
            // Hand_HandleSecondary 0x00408EE0: with a tool mode set, the right button only cancels it.
            Targeting = null;
            return InputFrame.Empty;
        }

        if ((intent.PrimaryButton & PointerButtonTransition.Pressed) != 0 && Targeting is SpecialTargeting pending)
        {
            // 0x00408D07: a creature of the right kind takes the special (action 88 with its tag) and the
            // hand returns; any other click leaves the mode as it is.
            if (intent.PointerTile is not TilePos at
                || TargetAt(snapshot.Targets, at, PlayerInputTargetKind.Creature) is not PlayerInputTarget creature
                || !pending.Accepts(creature))
                return InputFrame.Empty;
            Targeting = null;
            return InputFrame.Of(new ActivateSpecialCommand(intent.PlayerId, pending.ObjectId, creature.Id));
        }

        if ((intent.SecondaryButton & PointerButtonTransition.Pressed) != 0)
        {
            _drag = null;
            command = SecondaryClick(intent, snapshot);
            return command is null ? InputFrame.Empty : InputFrame.Of(command);
        }

        if ((intent.PrimaryButton & PointerButtonTransition.Pressed) != 0)
        {
            if (IsDragTool(intent.Tool) && intent.PointerTile is TilePos start)
            {
                _drag = new DragStart(intent.PlayerId, intent.Tool, start, intent.RoomId,
                    intent.Tool != PlayerToolMode.UntagTiles);
            }
            else
            {
                _drag = null;
                if (intent.Tool == PlayerToolMode.Hand && intent.PointerTile is TilePos clicked
                    && TargetAt(snapshot.Targets, clicked, PlayerInputTargetKind.Special) is PlayerInputTarget box
                    && TargetAt(snapshot.Targets, clicked, PlayerInputTargetKind.Creature) is null
                    && SpecialTargeting.Needs(box.TypeId))
                {
                    // Hand_Click 0x00407BAE: 127 and 140 enter targeting instead of posting action 88.
                    Targeting = new SpecialTargeting(box.Id, box.TypeId);
                    return InputFrame.Empty;
                }
                command = Click(intent, snapshot);
                if (command is null && intent.Tool == PlayerToolMode.Hand
                    && snapshot.Excavation is ExcavationTarget excavation
                    && intent.PointerTile == excavation.Tile)
                    _drag = new DragStart(intent.PlayerId, intent.Tool, excavation.Tile,
                        intent.RoomId, !excavation.IsTagged);
            }
        }

        if ((intent.PrimaryButton & PointerButtonTransition.Released) != 0)
        {
            if (_drag is DragStart drag &&
                drag.PlayerId == intent.PlayerId &&
                drag.Tool == intent.Tool &&
                intent.PointerTile is TilePos end)
            {
                command = drag.Tool switch
                {
                    PlayerToolMode.BuildRoom => Drag(drag, PlacementCursor.ClampDrag(drag.Tile, end, RubberBandLimit)),
                    PlayerToolMode.SellRoom => SellArea(drag, PlacementCursor.DragRectangle(drag.Tile, end, RubberBandLimit)),
                    _ => Drag(drag, end),
                };
            }

            _drag = null;
        }

        return command is null ? InputFrame.Empty : InputFrame.Of(command);
    }

    /// <summary>Where an unfinished drag with <paramref name="tool"/> began; null with none.</summary>
    public TilePos? DragFrom(PlayerToolMode tool) => _drag is DragStart drag && drag.Tool == tool ? drag.Tile : null;

    public bool? ExcavationPreviewTagged => _drag is DragStart drag
        && drag.Tool is PlayerToolMode.Hand or PlayerToolMode.TagTiles or PlayerToolMode.UntagTiles
            ? drag.Tagged : null;

    private static bool IsDragTool(PlayerToolMode tool) =>
        tool is PlayerToolMode.TagTiles or PlayerToolMode.UntagTiles or
            PlayerToolMode.BuildRoom or PlayerToolMode.SellRoom;

    private static PlayerCommand? Drag(in DragStart drag, TilePos end) => drag.Tool switch
    {
        PlayerToolMode.Hand =>
            TagTilesCommand.Rect(drag.PlayerId, drag.Tile.X, drag.Tile.Y, end.X, end.Y, drag.Tagged),
        PlayerToolMode.TagTiles =>
            TagTilesCommand.Rect(drag.PlayerId, drag.Tile.X, drag.Tile.Y, end.X, end.Y),
        PlayerToolMode.UntagTiles =>
            TagTilesCommand.Rect(drag.PlayerId, drag.Tile.X, drag.Tile.Y, end.X, end.Y, tagged: false),
        PlayerToolMode.BuildRoom =>
            BuildRoomCommand.Rect(drag.PlayerId, drag.RoomId, drag.Tile.X, drag.Tile.Y, end.X, end.Y),
        PlayerToolMode.SellRoom =>
            SellRoomCommand.Rect(drag.PlayerId, drag.Tile.X, drag.Tile.Y, end.X, end.Y),
        _ => null,
    };

    private static PlayerCommand SellArea(in DragStart drag, TileRectangle area) =>
        SellRoomCommand.Rect(drag.PlayerId, area.X0, area.Y0, area.X1 - 1, area.Y1 - 1);

    private static PlayerCommand? Click(in PlayerIntent intent, PlayerInputSnapshot snapshot)
    {
        if (intent.Tool == PlayerToolMode.CreateImp)
            return new CreateImpCommand(intent.PlayerId);

        if (intent.PointerTile is not TilePos tile) return null;

        if (intent.Tool == PlayerToolMode.Hand)
        {
            PlayerInputTarget? creature = TargetAt(
                snapshot.Targets, tile, PlayerInputTargetKind.Creature);
            if (creature is PlayerInputTarget c)
                return new PickUpCreatureCommand(intent.PlayerId, c.Id);

            if (TargetAt(snapshot.Targets, tile, PlayerInputTargetKind.Special) is PlayerInputTarget special)
                return new ActivateSpecialCommand(intent.PlayerId, special.Id);

            PlayerInputTarget? instance = TargetAt(
                snapshot.Targets, tile, PlayerInputTargetKind.Object);
            if (instance is PlayerInputTarget o) return new PickUpObjectCommand(intent.PlayerId, o.Id);
            return TargetAt(snapshot.Targets, tile, PlayerInputTargetKind.StoreGold) is PlayerInputTarget g
                ? new PickUpStoreGoldCommand(intent.PlayerId, g.Origin)
                : null;
        }

        if (intent.Tool == PlayerToolMode.Slap)
        {
            PlayerInputTarget? creature = TargetAt(
                snapshot.Targets, tile, PlayerInputTargetKind.Creature);
            return creature is PlayerInputTarget c
                ? new SlapCreatureCommand(intent.PlayerId, c.Id)
                : null;
        }

        return null;
    }

    private static PlayerCommand? SecondaryClick(in PlayerIntent intent, PlayerInputSnapshot snapshot)
    {
        if (intent.Tool != PlayerToolMode.Hand || intent.PointerTile is not TilePos tile) return null;
        PlayerInputTarget? creature = TargetAt(snapshot.Targets, tile, PlayerInputTargetKind.Creature);
        if (snapshot.IsHolding)
            return new DropCommand(intent.PlayerId, tile, creature?.Id ?? 0, intent.PointerPosition);
        return creature is PlayerInputTarget c ? new SlapCreatureCommand(intent.PlayerId, c.Id) : null;
    }

    private static PlayerInputTarget? TargetAt(
        IReadOnlyList<PlayerInputTarget> targets,
        TilePos tile,
        PlayerInputTargetKind kind)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            PlayerInputTarget target = targets[i];
            if (target.Kind == kind && target.Tile == tile) return target;
        }

        return null;
    }

    private readonly record struct DragStart(
        PlayerId PlayerId,
        PlayerToolMode Tool,
        TilePos Tile,
        RoomTypeId RoomId,
        bool Tagged);
}
