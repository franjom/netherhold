// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Players;
using GuiButtonTarget = DK2.Simulation.Commands.GuiButtonTarget;

namespace DK2.Presentation;

/// <summary>
/// One capture: the <see cref="Observation"/> a controller receives, and the host-only map from its
/// handles back to the admitted input targets. Only the observation reaches a controller.
/// </summary>
public sealed class PlayerObservationCapture
{
    private readonly PlayerInputTarget[] _targets;

    internal PlayerObservationCapture(PlayerObservation observation, PlayerInputTarget[] targets)
    {
        Observation = observation;
        _targets = targets;
    }

    public PlayerObservation Observation { get; }

    /// <summary>Resolves a handle this capture issued; a handle from another revision, or past the
    /// end of its thing list, resolves to nothing.</summary>
    public bool TryResolve(PlayerThingHandle handle, out PlayerInputTarget target)
    {
        if (handle.Revision == Observation.Stamp.Revision && (uint)handle.Ordinal < (uint)_targets.Length)
        {
            target = _targets[handle.Ordinal];
            return true;
        }
        target = default;
        return false;
    }

    /// <summary>The first creature target on <paramref name="tile"/>, as the hand's drop names the
    /// creature under the pointer (<c>PlayerInputTranslator</c>'s TargetAt).</summary>
    public bool TryFindCreatureAt(TilePos tile, out PlayerInputTarget creature)
    {
        foreach (PlayerInputTarget target in _targets)
            if (target.Kind == PlayerInputTargetKind.Creature && target.Tile == tile)
            {
                creature = target;
                return true;
            }
        creature = default;
        return false;
    }
}

/// <summary>
/// Projects a completed world onto what one player may know (campaign player plan, task 2). It reads
/// through the player's own view, never a privileged one redacted afterwards: tiles through the
/// <see cref="WorldView"/> fog gate, things through <see cref="PlayerInputSnapshot.Capture"/>'s
/// admitted targets, prices and availability through <see cref="BottomPanelView"/>, and only the
/// player's own messages. Capture reads the world and writes nothing to it. One builder serves one
/// run's world (notice ordinals and view caches carry across captures). Not thread-safe: the host
/// calls it serially at completed-tick boundaries. <see cref="CoverageLimits"/> lists what it omits.
/// </summary>
public sealed class PlayerObservationBuilder
{
    private readonly GameDatabase _database;
    private readonly PlayerId _player;
    private readonly WorldView _view;
    private readonly BottomPanelView _panel;
    private readonly Dictionary<uint, (GameTick Seen, int Ordinal)> _noticesSeen = [];
    private readonly List<uint> _noticesGone = [];
    private int _lastNoticeOrdinal;
    private readonly int[] _roomTiles = new int[256];
    private readonly Dictionary<int, PlayerThingHandle> _creatureHandles = [];
    private readonly List<PlayerRoomSummary> _rooms = [];
    private readonly List<PlayerCreatureSummary> _creatures = [];
    private readonly List<PlayerVisibleThing> _things = [];
    private readonly List<PlayerNotice> _notices = [];
    private readonly List<PlayerRoomOffer> _roomOffers = [];
    private readonly List<GuiButtonTarget> _panels = [];
    private PlayerTile[] _tiles = [];

    public PlayerObservationBuilder(GameDatabase database, PlayerId player)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _player = player;
        _view = new WorldView(database, player);
        _panel = new BottomPanelView(database, player, (_, name) => name);
    }

    public PlayerId Player => _player;

    /// <summary>What the observation does not yet carry; a run reports these as unchecked coverage.
    /// Phase Intro covers any scripted camera path, mid-level introductions included.</summary>
    public static IReadOnlyList<string> CoverageLimits { get; } =
    [
        "doors, traps and spells other than Create Imp are not offered",
        "specials that name a creature (127, 140) and stored specials are not offered",
        "tag previews and Sight of Evil are not applied to the observing view",
        "message text and payloads are not carried, only kind and order",
    ];

    public PlayerObservationCapture Capture(WorldState completedWorld, PlayerStamp stamp, IReadOnlyList<PlayerReceipt> receipts)
    {
        if (completedWorld is null) throw new ArgumentNullException(nameof(completedWorld));
        if (receipts is null) throw new ArgumentNullException(nameof(receipts));
        if (!stamp.IsValid) throw new ArgumentException("A capture needs a valid stamp.", nameof(stamp));
        if (stamp.Player != _player) throw new ArgumentException("The stamp names another player.", nameof(stamp));
        if (!completedWorld.HasPlayer(_player)) throw new ArgumentException("The world has no such player.", nameof(completedWorld));

        WorldState world = completedWorld;
        ref readonly PlayerState keeper = ref world.Player(_player);
        PlayerResult result = (PlayerResult)keeper.Status;
        PlayerPhase phase = result != PlayerResult.Playing ? PlayerPhase.Ended
            : CameraPathRules.Playing(keeper, world.CurrentTick) ? PlayerPhase.Intro : PlayerPhase.Playing;

        PlayerTileGrid grid = CaptureTiles(world);
        PlayerInputTarget[] targets = CaptureThings(world, stamp.Revision);
        CaptureCreatures(world);
        CaptureNotices(world, stamp.Tick);
        PlayerOffers offers = CaptureOffers(world, phase, keeper.Hand.Count);

        var observation = new PlayerObservation
        {
            Stamp = stamp,
            Phase = phase,
            Result = result,
            Gold = new GoldAmount(GoldStore.Total(world, _player)),
            Mana = new ManaAmount(keeper.Mana),
            HandCount = keeper.Hand.Count,
            Tiles = grid,
            Rooms = _rooms,
            Creatures = _creatures,
            Things = _things,
            Notices = _notices,
            Receipts = receipts,
            Offers = offers,
        };
        return new PlayerObservationCapture(observation, targets);
    }

    private PlayerTileGrid CaptureTiles(WorldState world)
    {
        int count = world.Width * world.Height;
        if (_tiles.Length != count) _tiles = new PlayerTile[count];
        Array.Clear(_roomTiles, 0, _roomTiles.Length);
        for (int y = 0, i = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++, i++)
            {
                PlayerTile tile = _view.PlayerTileAt(world, x, y);
                _tiles[i] = tile;
                if (tile.Room != default && tile.Owner == _player) _roomTiles[(byte)tile.Room]++;
            }

        _rooms.Clear();
        for (int room = 1; room < _roomTiles.Length; room++)
            if (_roomTiles[room] > 0) _rooms.Add(new PlayerRoomSummary((RoomTypeId)room, _roomTiles[room]));
        return new PlayerTileGrid(world.Width, world.Height, _tiles);
    }

    private PlayerInputTarget[] CaptureThings(WorldState world, PlayerRevision revision)
    {
        IReadOnlyList<PlayerInputTarget> admitted = PlayerInputSnapshot.Capture(_view, world, _player, _database).Targets;
        var targets = new PlayerInputTarget[admitted.Count];
        _things.Clear();
        _creatureHandles.Clear();
        for (int i = 0; i < admitted.Count; i++)
        {
            PlayerInputTarget target = admitted[i];
            targets[i] = target;
            var handle = new PlayerThingHandle(revision, i);
            _things.Add(Visible(world, target, handle));
            if (target.Kind == PlayerInputTargetKind.Creature) _creatureHandles[target.Id] = handle;
        }
        return targets;
    }

    private PlayerVisibleThing Visible(WorldState world, in PlayerInputTarget target, PlayerThingHandle handle)
    {
        // Each offer is the hand rule the engine applies to that thing, read from the viewer's own
        // state and the public definitions; a thing the hand cannot act on is listed with none.
        switch (target.Kind)
        {
            case PlayerInputTargetKind.Creature:
                CreatureState creature = world.CreatureById(target.Id)!;
                PlayerThingActions creatureActions = PlayerThingActions.None;
                if (HandRules.CanPickUp(world, _database, creature, _player)) creatureActions |= PlayerThingActions.PickUp;
                if (HandRules.CanSlap(_database, creature, _player)) creatureActions |= PlayerThingActions.Slap;
                return new PlayerVisibleThing(handle, PlayerThingKind.Creature, target.Tile, creature.OwnerPlayerId,
                    creatureActions, creatureType: creature.CreatureTypeId);
            case PlayerInputTargetKind.Object:
                ObjectState thing = world.ObjectById(target.Id)!;
                return new PlayerVisibleThing(handle, PlayerThingKind.Object, target.Tile, thing.OwnerPlayerId,
                    HandRules.CanPickUpObject(world, _database, thing, _player) ? PlayerThingActions.PickUp : PlayerThingActions.None,
                    objectType: thing.ObjectTypeId);
            case PlayerInputTargetKind.Special:
                // Specials that need a creature named (127, 140) enter targeting mode, which this
                // action family cannot express; they are listed but not offered.
                return new PlayerVisibleThing(handle, PlayerThingKind.Special, target.Tile, world.ObjectById(target.Id)!.OwnerPlayerId,
                    SpecialTargeting.Needs(target.TypeId) ? PlayerThingActions.None : PlayerThingActions.Activate,
                    objectType: target.TypeId);
            case PlayerInputTargetKind.StoreGold:
                return new PlayerVisibleThing(handle, PlayerThingKind.StoreGold, target.Tile, _player,
                    HandRules.CanPickUpStoreGold(world, _database, target.Origin, _player) ? PlayerThingActions.PickUp : PlayerThingActions.None);
            case PlayerInputTargetKind.Door:
                return new PlayerVisibleThing(handle, PlayerThingKind.Door, target.Tile, default, PlayerThingActions.None);
            default:
                throw new InvalidOperationException($"No observation for input target kind {target.Kind}.");
        }
    }

    private void CaptureCreatures(WorldState world)
    {
        _creatures.Clear();
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.OwnerPlayerId != _player || creature.Activity == CreatureActivity.Dead) continue;
            // Every own creature's place is the viewer's to know: the creature panel's Focus
            // (0x00417F60) jumps the camera to any not held or joining, prisoners included.
            TilePos? tile = creature.InHand ? null : creature.Tile;
            _creatures.Add(new PlayerCreatureSummary(creature.CreatureTypeId, tile, creature.InHand,
                _creatureHandles.TryGetValue(creature.Id, out PlayerThingHandle handle) ? handle : null));
        }
    }

    private void CaptureNotices(WorldState world, GameTick tick)
    {
        _notices.Clear();
        IReadOnlyList<PlayerMessage> entries = world.Messages.Entries;
        foreach (PlayerMessage message in entries)
        {
            if (message.PlayerId != _player) continue;
            if (!_noticesSeen.TryGetValue(message.Id, out (GameTick Seen, int Ordinal) seen))
                _noticesSeen[message.Id] = seen = (tick, ++_lastNoticeOrdinal);
            _notices.Add(new PlayerNotice(seen.Seen, seen.Ordinal, message.Kind));
        }
        // Forget messages the queue dropped; queue ids are never reused, so none can return.
        if (_noticesSeen.Count == _notices.Count) return;
        _noticesGone.Clear();
        foreach (uint id in _noticesSeen.Keys)
            if (!Contains(entries, id)) _noticesGone.Add(id);
        foreach (uint id in _noticesGone) _noticesSeen.Remove(id);

        static bool Contains(IReadOnlyList<PlayerMessage> entries, uint id)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i].Id == id) return true;
            return false;
        }
    }

    private PlayerOffers CaptureOffers(WorldState world, PlayerPhase phase, int handCount)
    {
        if (phase == PlayerPhase.Ended) return PlayerOffers.None;
        if (phase == PlayerPhase.Intro) return new PlayerOffers { Actions = PlayerActionKinds.SkipIntro };

        _panel.Capture(world);
        _roomOffers.Clear();
        _panels.Clear();
        ManaAmount? createImp = null;
        foreach (BottomPanelChoice choice in _panel.Choices)
        {
            if (!choice.Visible || !choice.Selectable) continue;
            if (choice.Kind == BottomPanelChoiceKind.Room)
            {
                var room = (RoomTypeId)choice.Key.Value;
                _roomOffers.Add(new PlayerRoomOffer(room, new GoldAmount(choice.Cost), choice.Affordable));
                _panels.Add(GuiButtonTarget.Room(room));
            }
            else if (choice.Kind == BottomPanelChoiceKind.Spell)
            {
                _panels.Add(GuiButtonTarget.Spell(choice.Key.Value));
                if (choice.Key.Value == BottomPanelView.CreateImpSpellId) createImp = new ManaAmount(choice.Cost);
            }
        }

        PlayerActionKinds actions = PlayerActionKinds.TagArea;
        if (_roomOffers.Count > 0) actions |= PlayerActionKinds.BuildRoom;
        if (_panels.Count > 0) actions |= PlayerActionKinds.SelectPanel;
        if (handCount > 0) actions |= PlayerActionKinds.DropAt;
        if (createImp is not null) actions |= PlayerActionKinds.CreateImpAt;
        foreach (PlayerVisibleThing thing in _things)
        {
            if ((thing.Actions & PlayerThingActions.PickUp) != 0) actions |= PlayerActionKinds.PickUp;
            if ((thing.Actions & PlayerThingActions.Slap) != 0) actions |= PlayerActionKinds.Slap;
            if ((thing.Actions & PlayerThingActions.Activate) != 0) actions |= PlayerActionKinds.ActivateSpecial;
        }
        return new PlayerOffers { Actions = actions, Rooms = _roomOffers, Panels = _panels, CreateImpCost = createImp };
    }
}
