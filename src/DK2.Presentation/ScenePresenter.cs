// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Assets;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Pushes what changed in the world to an <see cref="IRenderTarget"/>, and nothing that
/// did not. It owns what counts as a visible change, the push order, which asset key each thing
/// needs, and when to create versus update versus remove; the Unity adapter turns a
/// <see cref="ResolvedAsset"/> into a <c>GameObject</c> and no more.
/// <para><b>Diff rather than redraw</b>: a campaign level is 61x61, so 3,721 tiles — iterating
/// that per frame is nothing, instantiating it is not, so the presenter remembers what it last
/// pushed per tile and pushes again only when the visible part differs. <see cref="TextRenderer"/>
/// redraws everything by design, and having both proves the boundary assumes neither.</para>
/// <para><b>Order is part of the contract</b>: tiles in row order, creatures in the simulation's
/// own order — spawn order, and behaviour (ADR-0010). <b>Never reads back from the target</b>, so
/// the plan's "deleting every GameObject must not change the simulation" (§4.7) holds by
/// construction; <see cref="Resync"/> refills a target that did throw everything away.</para></summary>
public sealed class ScenePresenter
{
    /// <summary>
    /// The part of a tile a renderer can see. Compared field by field to decide
    /// whether to push.
    ///
    /// <para>Deliberately not the whole <see cref="TileView"/>: position never
    /// changes, so including it would cost a comparison per tile per frame for
    /// nothing. Everything here is something a player could point at.</para>
    /// </summary>
    private readonly struct TileVisual : IEquatable<TileVisual>
    {
        public TileVisual(in TileView tile)
        {
            Ground = tile.Ground;
            Furniture = tile.Furniture;
            OwnerPlayerId = tile.OwnerPlayerId;
            Tagged = tile.Tagged;
            GoldHeld = tile.GoldHeld;
            HealthPermille = tile.HealthPermille;
            Walls = tile.Walls;
        }

        public AssetRef Ground { get; }
        public AssetRef Furniture { get; }
        public byte OwnerPlayerId { get; }
        public bool Tagged { get; }
        public int GoldHeld { get; }
        public int HealthPermille { get; }
        public RoomWalls Walls { get; }

        public bool Equals(TileVisual other) =>
            Ground == other.Ground
            && Furniture == other.Furniture
            && OwnerPlayerId == other.OwnerPlayerId
            && Tagged == other.Tagged
            && GoldHeld == other.GoldHeld
            && HealthPermille == other.HealthPermille
            && Walls == other.Walls;

        public override bool Equals(object? obj) => obj is TileVisual other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            Ground, Furniture, OwnerPlayerId, Tagged, GoldHeld, HealthPermille, Walls);
    }

    /// <summary>The part of a creature a renderer can see.</summary>
    private readonly struct CreatureVisual : IEquatable<CreatureVisual>
    {
        public CreatureVisual(in CreatureView creature)
        {
            Mesh = creature.Mesh;
            Animation = creature.Animation;
            PosX = creature.PosX;
            PosY = creature.PosY;
            OwnerPlayerId = creature.OwnerPlayerId;
            GoldHeld = creature.GoldHeld;
        }

        public AssetRef Mesh { get; }
        public AssetRef Animation { get; }
        public int PosX { get; }
        public int PosY { get; }
        public byte OwnerPlayerId { get; }
        public int GoldHeld { get; }

        public bool Equals(CreatureVisual other) =>
            Mesh == other.Mesh
            && Animation == other.Animation
            && PosX == other.PosX
            && PosY == other.PosY
            && OwnerPlayerId == other.OwnerPlayerId
            && GoldHeld == other.GoldHeld;

        public override bool Equals(object? obj) => obj is CreatureVisual other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            Mesh, Animation, PosX, PosY, OwnerPlayerId, GoldHeld);
    }

    /// <summary>The part of an object a renderer can see.</summary>
    private readonly struct ObjectVisual : IEquatable<ObjectVisual>
    {
        public ObjectVisual(in ObjectView instance)
        {
            Mesh = instance.Mesh;
            Tile = instance.Tile;
            OwnerPlayerId = instance.OwnerPlayerId;
            GoldHeld = instance.GoldHeld;
        }

        public AssetRef Mesh { get; }
        public TilePos Tile { get; }
        public byte OwnerPlayerId { get; }
        public int GoldHeld { get; }

        public bool Equals(ObjectVisual other) =>
            Mesh == other.Mesh
            && Tile.Equals(other.Tile)
            && OwnerPlayerId == other.OwnerPlayerId
            && GoldHeld == other.GoldHeld;

        public override bool Equals(object? obj) => obj is ObjectVisual other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Mesh, Tile, OwnerPlayerId, GoldHeld);
    }

    private readonly WorldView _view;
    private readonly IAssetProvider _assets;
    private readonly IRenderTarget _target;
    private readonly TorchLayout? _torches;
    private readonly HashSet<int> _torchesDrawn = [];
    private bool _redrawTorches;

    private TileVisual[] _lastTiles = [];
    private bool[] _tileCreated = [];
    private readonly Dictionary<int, CreatureVisual> _lastCreatures = [];
    private readonly List<int> _goneCreatures = [];
    private readonly HashSet<int> _seenCreatures = [];
    private readonly Dictionary<int, ObjectVisual> _lastObjects = [];
    private readonly List<int> _goneObjects = [];
    private readonly HashSet<int> _seenObjects = [];

    public ScenePresenter(WorldView view, IAssetProvider assets, IRenderTarget target)
        : this(view, assets, target, null)
    {
    }

    /// <summary>With <paramref name="torches"/>, the wall torches are pushed too (ADR-0390).</summary>
    public ScenePresenter(WorldView view, IAssetProvider assets, IRenderTarget target, TorchLayout? torches)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _torches = torches;
    }

    /// <summary>How many tiles were pushed by the most recent <see cref="Present"/>.</summary>
    public int TilesPushed { get; private set; }

    /// <summary>How many creatures were pushed by the most recent <see cref="Present"/>.</summary>
    public int CreaturesPushed { get; private set; }

    /// <summary>How many objects were pushed by the most recent <see cref="Present"/>.</summary>
    public int ObjectsPushed { get; private set; }

    /// <summary>How many torches were pushed by the most recent <see cref="Present"/>.</summary>
    public int TorchesPushed { get; private set; }

    /// <summary>
    /// Forgets everything, so the next <see cref="Present"/> creates the whole
    /// scene again. For a target that discarded what it drew — a Unity scene
    /// reload, a camera moving to a different level.
    /// </summary>
    public void Resync()
    {
        _lastTiles = [];
        _tileCreated = [];
        _lastCreatures.Clear();
        _lastObjects.Clear();
        _torchesDrawn.Clear();
        _redrawTorches = true;
    }

    /// <summary>
    /// Pushes the current state of the world to the target.
    ///
    /// <para>Safe to call every frame or every tick; the first call creates
    /// everything and later calls push only differences. A world whose
    /// dimensions changed is treated as a different world and resynced, because
    /// a tile index means nothing across a resize.</para>
    /// </summary>
    public void Present(WorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        TilesPushed = 0;
        CreaturesPushed = 0;
        ObjectsPushed = 0;
        TorchesPushed = 0;

        if (_lastTiles.Length != world.TileCount)
        {
            _lastTiles = new TileVisual[world.TileCount];
            _tileCreated = new bool[world.TileCount];
        }

        PresentTiles(world);
        PresentTorches(world);

        // Objects before creatures, because a creature stands on an object and
        // the text renderer draws them in that precedence -- one order, decided
        // here, so two renderers cannot disagree about what occludes what.
        PresentObjects(world);
        PresentCreatures(world);

        _target.Flush();
    }

    private void PresentTiles(WorldState world)
    {
        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                int index = (y * world.Width) + x;
                TileView tile = _view.TileAt(world, x, y);
                var visual = new TileVisual(tile);

                if (_tileCreated[index] && _lastTiles[index].Equals(visual)) continue;

                ResolvedAsset ground = Resolve(tile.Ground);
                ResolvedAsset furniture = Resolve(tile.Furniture);

                if (_tileCreated[index])
                    _target.UpdateTile(index, tile, ground, furniture);
                else
                    _target.CreateTile(index, tile, ground, furniture);

                _lastTiles[index] = visual;
                _tileCreated[index] = true;
                TilesPushed++;
            }
        }
    }

    /// <summary>The torches after the tiles they hang on, in floor-tile order.</summary>
    private void PresentTorches(WorldState world)
    {
        if (_torches is null) return;
        IReadOnlyList<int> changed = _torches.Update(world);
        if (_redrawTorches)
        {
            _redrawTorches = false;
            var all = new List<int>();
            for (int i = 0; i < world.TileCount; i++)
                if (_torches.At(i) is not null || _torchesDrawn.Contains(i)) all.Add(i);
            changed = all;
        }

        foreach (int index in changed)
        {
            if (_torches.At(index) is TorchView torch)
            {
                ResolvedAsset mesh = Resolve(torch.Mesh);
                if (_torchesDrawn.Add(index)) _target.CreateTorch(index, torch, mesh);
                else _target.UpdateTorch(index, torch, mesh);
            }
            else if (_torchesDrawn.Remove(index))
            {
                _target.RemoveTorch(index);
            }
            else
            {
                continue;
            }

            TorchesPushed++;
        }
    }

    private void PresentCreatures(WorldState world)
    {
        _seenCreatures.Clear();

        foreach (CreatureView creature in _view.Creatures(world))
        {
            _seenCreatures.Add(creature.Id);
            var visual = new CreatureVisual(creature);

            bool known = _lastCreatures.TryGetValue(creature.Id, out CreatureVisual last);
            if (known && last.Equals(visual)) continue;

            ResolvedAsset mesh = Resolve(creature.Mesh);
            ResolvedAsset animation = Resolve(creature.Animation);

            if (known)
                _target.UpdateCreature(creature.Id, creature, mesh, animation);
            else
                _target.CreateCreature(creature.Id, creature, mesh, animation);

            _lastCreatures[creature.Id] = visual;
            CreaturesPushed++;
        }

        // Anything we knew about and did not see has died or been removed.
        // Collected first rather than removed mid-enumeration, and in ascending
        // id order so the call sequence is reproducible.
        _goneCreatures.Clear();
        foreach (int id in _lastCreatures.Keys)
            if (!_seenCreatures.Contains(id)) _goneCreatures.Add(id);

        _goneCreatures.Sort();

        foreach (int id in _goneCreatures)
        {
            _target.RemoveCreature(id);
            _lastCreatures.Remove(id);
        }
    }

    private void PresentObjects(WorldState world)
    {
        _seenObjects.Clear();

        foreach (ObjectView instance in _view.Objects(world))
        {
            _seenObjects.Add(instance.Id);
            var visual = new ObjectVisual(instance);

            bool known = _lastObjects.TryGetValue(instance.Id, out ObjectVisual last);
            if (known && last.Equals(visual)) continue;

            ResolvedAsset mesh = Resolve(instance.Mesh);

            if (known)
                _target.UpdateObject(instance.Id, instance, mesh);
            else
                _target.CreateObject(instance.Id, instance, mesh);

            _lastObjects[instance.Id] = visual;
            ObjectsPushed++;
        }

        // Same rule as creatures: anything we knew about and did not see is
        // gone, collected first and removed in ascending id order so the call
        // sequence is reproducible.
        _goneObjects.Clear();
        foreach (int id in _lastObjects.Keys)
            if (!_seenObjects.Contains(id)) _goneObjects.Add(id);

        _goneObjects.Sort();

        foreach (int id in _goneObjects)
        {
            _target.RemoveObject(id);
            _lastObjects.Remove(id);
        }
    }

    /// <summary>
    /// Resolves a key, tolerating the empty one.
    ///
    /// <para>An empty key is normal — a tile that is not a room has no
    /// furniture — so it resolves to nothing rather than throwing. Everything
    /// else must resolve, because the chain ends in a total provider; a miss
    /// there means the chain was built without a placeholder, which is a
    /// configuration bug and not a missing file.</para>
    /// </summary>
    private ResolvedAsset Resolve(AssetRef reference)
    {
        if (reference.IsEmpty) return default;

        if (_assets.TryResolve(reference, out ResolvedAsset asset)) return asset;

        throw new InvalidOperationException(
            $"nothing resolved '{reference}'. With a placeholder in the chain this cannot " +
            "happen, so the chain was built without one.");
    }
}
