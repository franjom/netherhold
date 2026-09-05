// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Assets;
using DK2.Core;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>
    /// Turns the presenter's pushes into GameObjects. The whole Unity-specific
    /// half of the renderer.
    ///
    /// <para><b>There is deliberately almost no judgement in here.</b>
    /// <see cref="ScenePresenter"/> already decided what changed, in what order,
    /// which asset each thing needs and whether this is a create or an update;
    /// the asset layer already decided whether the answer is a file or a
    /// placeholder. What is left is arithmetic and <c>Instantiate</c>. That was
    /// the point of pushing the logic down (ADR-0036): this file is the one part
    /// of the project that cannot be covered by the test suite, so it holds as
    /// little as possible.</para>
    ///
    /// <para><b>Coordinates.</b> The simulation's grid lies on Unity's XZ plane
    /// with Y up. A tile <c>(x, y)</c> is centred at
    /// <c>(x + 0.5, 0, y + 0.5)</c>, and a creature's <see cref="Fp32"/> position
    /// divides by <see cref="Fp32.One"/> — <b>the conversion happens here and
    /// nowhere else</b> (ADR-0001). Spawning centres a creature in its tile, so
    /// the two conventions already agree.</para>
    /// </summary>
    public sealed class DungeonScene : IRenderTarget
    {
        private readonly Transform _root;
        private readonly Dictionary<int, GameObject> _tiles = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _furniture = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _creatures = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _objects = new Dictionary<int, GameObject>();
        private readonly Dictionary<uint, Material> _materials = new Dictionary<uint, Material>();
        private readonly int _worldWidth;

        private bool _warnedAboutFiles;

        /// <summary>
        /// How many tiles and creatures exist as objects. For the on-screen
        /// diagnostics, and for noticing a leak.
        /// </summary>
        public int TileObjects => _tiles.Count;
        public int CreatureObjects => _creatures.Count;
        public int ObjectObjects => _objects.Count;

        /// <summary>
        /// Set when a provider handed back a real file. Nothing loads one yet
        /// (see <see cref="Instantiate"/>), so this is how the runner can say so
        /// on screen rather than silently drawing a box.
        /// </summary>
        public bool SawFileBackedAssets { get; private set; }

        public DungeonScene(Transform root, int worldWidth)
        {
            _root = root;
            _worldWidth = worldWidth;
        }

        public void CreateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture)
        {
            GameObject go = Instantiate(ground, $"tile {tile.Position.X},{tile.Position.Y}");
            if (go != null)
            {
                Place(go, tile.Position.X, tile.Position.Y, ground.Look);
                _tiles[tileIndex] = go;
            }

            ApplyFurniture(tileIndex, tile, furniture);
        }

        public void UpdateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture)
        {
            // The terrain may have become something else entirely — a dug wall
            // becomes floor — so the ground object is rebuilt rather than
            // recoloured. Tiles change rarely (the presenter guarantees only
            // changed ones arrive), so this costs nothing in practice and avoids
            // a second, subtly different code path.
            if (_tiles.TryGetValue(tileIndex, out GameObject old) && old != null)
            {
                Object.Destroy(old);
                _tiles.Remove(tileIndex);
            }

            CreateTile(tileIndex, tile, ground, furniture);
        }

        public void CreateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
        {
            GameObject go = Instantiate(mesh, $"creature {creatureId}");
            if (go == null) return;

            _creatures[creatureId] = go;
            PositionCreature(go, creature, mesh.Look);
        }

        public void UpdateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
        {
            if (!_creatures.TryGetValue(creatureId, out GameObject go) || go == null)
            {
                CreateCreature(creatureId, creature, mesh, animation);
                return;
            }

            PositionCreature(go, creature, mesh.Look);

            // A creature carrying gold reads as carrying it. Standing in for the
            // real thing, which is an object an imp holds — and objects are M6.
            SetColour(go, creature.GoldHeld > 0
                ? new Color(0.95f, 0.82f, 0.25f)
                : ColourOf(mesh.Look));
        }

        public void RemoveCreature(int creatureId)
        {
            if (_creatures.TryGetValue(creatureId, out GameObject go) && go != null)
                Object.Destroy(go);

            _creatures.Remove(creatureId);
        }

        public void CreateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
        {
            GameObject go = Instantiate(mesh, $"object {objectId}");
            if (go == null) return;

            _objects[objectId] = go;
            PositionObject(go, instance, mesh.Look);
        }

        public void UpdateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
        {
            if (!_objects.TryGetValue(objectId, out GameObject go) || go == null)
            {
                CreateObject(objectId, instance, mesh);
                return;
            }

            PositionObject(go, instance, mesh.Look);
        }

        public void RemoveObject(int objectId)
        {
            if (_objects.TryGetValue(objectId, out GameObject go) && go != null)
                Object.Destroy(go);

            _objects.Remove(objectId);
        }

        /// <summary>
        /// Nothing to do: this target draws one object per thing rather than
        /// batching. A combined-mesh target would rebuild here, which is why the
        /// method exists on the interface at all.
        /// </summary>
        public void Flush()
        {
        }

        /// <summary>Destroys everything. Pair with <see cref="ScenePresenter.Resync"/>.</summary>
        public void Clear()
        {
            foreach (GameObject go in _tiles.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _furniture.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _creatures.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _objects.Values) if (go != null) Object.Destroy(go);

            _tiles.Clear();
            _furniture.Clear();
            _creatures.Clear();
            _objects.Clear();
        }

        private void ApplyFurniture(int tileIndex, in TileView tile, in ResolvedAsset furniture)
        {
            if (_furniture.TryGetValue(tileIndex, out GameObject existing) && existing != null)
            {
                Object.Destroy(existing);
                _furniture.Remove(tileIndex);
            }

            if (furniture.Reference.IsEmpty) return;

            GameObject go = Instantiate(furniture, $"room {tile.Position.X},{tile.Position.Y}");
            if (go == null) return;

            // Room furniture stands on the floor rather than replacing it, so it
            // is deliberately smaller than a tile and sits above the ground.
            go.transform.localScale = new Vector3(0.6f, 0.35f, 0.6f);
            go.transform.position = new Vector3(tile.Position.X + 0.5f, 0.2f, tile.Position.Y + 0.5f);
            _furniture[tileIndex] = go;
        }

        /// <summary>
        /// Makes an object for a resolved asset.
        ///
        /// <para><b>File-backed assets are not loaded, and that is the honest
        /// state of the project rather than a shortcut.</b> A
        /// <see cref="ResolvedAsset.FilePath"/> points at converter output —
        /// glTF, PNG, OGG — and **no converter exists yet** (plan §7.1), so
        /// nothing has ever produced one of those files. Writing a runtime glTF
        /// loader now would be writing an importer for input that does not
        /// exist. When the converter lands, this is the method that gains a
        /// branch; until then a file resolves to the same primitive a
        /// placeholder does, and <see cref="SawFileBackedAssets"/> records that
        /// it happened so the runner can say so on screen.</para>
        /// </summary>
        private GameObject Instantiate(in ResolvedAsset asset, string name)
        {
            if (asset.Reference.IsEmpty) return null;

            if (!asset.IsPlaceholder)
            {
                SawFileBackedAssets = true;
                if (!_warnedAboutFiles)
                {
                    _warnedAboutFiles = true;
                    Debug.LogWarning(
                        $"DK2: '{asset.Reference}' resolved to a file ({asset.FilePath}) but no " +
                        "loader exists yet — drawing a placeholder. See DungeonScene.Instantiate.");
                }
            }

            PlaceholderShape shape = asset.IsPlaceholder
                ? asset.Look.Shape
                : PlaceholderProvider.ShapeFor(asset.Reference);

            if (shape == PlaceholderShape.None) return null;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_root, worldPositionStays: false);

            // Colliders on thousands of tiles cost a great deal and buy nothing
            // until something needs picking. Removed rather than never added,
            // because CreatePrimitive is the cheapest way to a cube with a mesh.
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            SetColour(go, ColourOf(asset.IsPlaceholder
                ? asset.Look
                : PlaceholderProvider.LookFor(asset.Reference)));

            return go;
        }

        private void Place(GameObject go, int x, int y, in PlaceholderLook look)
        {
            float height = look.HeightPermille / 1000f;
            if (height <= 0f) height = 0.04f;

            go.transform.localScale = new Vector3(1f, height, 1f);

            // Sit on the ground plane rather than centred on it, so a slab is
            // flush and a block's base is level with it.
            go.transform.position = new Vector3(x + 0.5f, height / 2f, y + 0.5f);
        }

        private void PositionCreature(GameObject go, in CreatureView creature, in PlaceholderLook look)
        {
            float height = look.HeightPermille / 1000f;
            if (height <= 0f) height = 0.5f;

            go.transform.localScale = new Vector3(0.4f, height, 0.4f);
            go.transform.position = new Vector3(
                creature.PosX / (float)Fp32.One,
                height / 2f,
                creature.PosY / (float)Fp32.One);
        }

        /// <summary>
        /// An object sits at the centre of its tile, small and low.
        ///
        /// <para>The centre because that is all the simulation knows: an
        /// <see cref="ObjectView"/> carries a tile and not a position, and DK2's
        /// own sub-tile placement is recorded but not modelled
        /// (gold-on-the-floor.md section 7). Small and low so a bed reads as
        /// furniture on a floor rather than as another creature standing on
        /// it.</para>
        /// </summary>
        private void PositionObject(GameObject go, in ObjectView instance, in PlaceholderLook look)
        {
            float height = look.HeightPermille / 1000f;
            if (height <= 0f) height = 0.25f;
            height *= 0.5f;

            go.transform.localScale = new Vector3(0.5f, height, 0.5f);
            go.transform.position = new Vector3(
                instance.Tile.X + 0.5f, height / 2f, instance.Tile.Y + 0.5f);
        }

        private static Color ColourOf(in PlaceholderLook look) =>
            new Color(look.Red / 255f, look.Green / 255f, look.Blue / 255f);

        /// <summary>
        /// Sets a renderer's colour, sharing one material per colour.
        ///
        /// <para>Sharing matters: a level is thousands of objects and a material
        /// per object would mean thousands of draw calls and thousands of
        /// allocations. Colours come from a hash of the asset key, so the number
        /// of distinct ones is small and bounded by the number of asset
        /// types.</para>
        /// </summary>
        private void SetColour(GameObject go, Color colour)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            uint key = ((uint)(colour.r * 255) << 16) | ((uint)(colour.g * 255) << 8) | (uint)(colour.b * 255);

            if (!_materials.TryGetValue(key, out Material material) || material == null)
            {
                material = new Material(renderer.sharedMaterial) { color = colour };
                _materials[key] = material;
            }

            renderer.sharedMaterial = material;
        }
    }
}
