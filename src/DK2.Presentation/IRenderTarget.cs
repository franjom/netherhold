// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;

namespace DK2.Presentation;

/// <summary>What a renderer has to be able to do, without naming an engine.</summary>
/// <remarks>
/// <para>Nine methods with no logic in them (ADR-0036). Diffing, asset resolution,
/// coordinate policy and push order are all <see cref="ScenePresenter"/>'s and are
/// tested here; the adapter cannot be.</para>
/// <para>Arguments are <see cref="ResolvedAsset"/>, not a path: a target must draw
/// both a file and a placeholder, so one reading only
/// <see cref="ResolvedAsset.FilePath"/> draws nothing with no art (ADR-0034).</para>
/// <para>Things are named by id — tile index, simulation creature id, object id —
/// and nothing is returned, so deleting every GameObject cannot change the
/// simulation (plan §4.7).</para>
/// </remarks>
public interface IRenderTarget
{
    /// <summary>
    /// A tile is being drawn for the first time. Called once per tile before any
    /// <see cref="UpdateTile"/> for it.
    /// </summary>
    void CreateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture);

    /// <summary>
    /// A created tile looks different now — terrain, owner, dig mark, gold or damage.
    /// </summary>
    /// <remarks>
    /// Called for every visible change, so a wall losing health a swing at a time
    /// produces one call per swing; coalescing is the target's judgement. How many
    /// discrete damage states DK2 draws is UNKNOWN (<c>docs/research/wall-rendering.md</c>),
    /// so the presenter does not pick a number.
    /// </remarks>
    void UpdateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture);

    /// <summary>A creature has appeared.</summary>
    void CreateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation);

    /// <summary>
    /// A creature has moved, or changed what it is doing, or picked gold up.
    /// Position is in <see cref="DK2.Core.Fp32"/> tile units — <b>the target
    /// converts</b> (ADR-0001).
    /// </summary>
    void UpdateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation);

    /// <summary>A creature is gone. The target should forget whatever it drew for it.</summary>
    void RemoveCreature(int creatureId);

    /// <summary>
    /// An object has appeared: a bed dropped by a creature claiming a lair tile,
    /// a coop placed by a hatchery, an egg, a pile of gold.
    /// </summary>
    void CreateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh);

    /// <summary>
    /// An object changed: a gold pile grew or shrank, or a bed changed hands.
    /// Not implemented: object movement — <see cref="ObjectView"/> carries no
    /// sub-tile position, so an object that walks in DK2 stands still here.
    /// </summary>
    void UpdateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh);

    /// <summary>An object is gone: a bed sold, an egg hatched, a pile emptied.</summary>
    void RemoveObject(int objectId);

    /// <summary>
    /// Every push is finished. A target that batches — one combined mesh per
    /// terrain type, say — rebuilds here.
    /// </summary>
    void Flush();
}
