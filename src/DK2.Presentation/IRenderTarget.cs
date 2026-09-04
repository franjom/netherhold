// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;

namespace DK2.Presentation;

/// <summary>
/// What a renderer has to be able to do, expressed without naming an engine.
///
/// <para><b>Why this interface exists at all.</b> The Unity adapter is the one
/// piece of this project that cannot be compiled or tested here, so the design
/// goal is to make it <i>small and stupid</i>: no diffing, no asset resolution,
/// no coordinate policy, no ordering decisions. All of that is
/// <see cref="ScenePresenter"/>'s job and is tested. What is left for Unity is
/// "make a thing", "move a thing", "forget a thing" — six methods with no logic
/// in them.</para>
///
/// <para>The methods take <see cref="ResolvedAsset"/> rather than a path,
/// because a target must handle both a file and a placeholder and choosing
/// between them is the whole point of the asset layer (ADR-0034). A target that
/// looked only at <see cref="ResolvedAsset.FilePath"/> would draw nothing when
/// no art is installed, which is exactly the case that must keep working.</para>
///
/// <para><b>Ids, not objects.</b> A tile is identified by its index and a
/// creature by the simulation's own creature id, so a target holds a map from id
/// to whatever it drew and nothing else. That is what makes "deleting every
/// GameObject must not change the simulation" (plan §4.7) true by construction:
/// the presenter never receives anything back.</para>
/// </summary>
public interface IRenderTarget
{
    /// <summary>
    /// A tile is being drawn for the first time. Called once per tile before any
    /// <see cref="UpdateTile"/> for it.
    /// </summary>
    void CreateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture);

    /// <summary>
    /// A tile that has already been created looks different now — its terrain
    /// changed, or its owner, its dig mark, its gold or its damage.
    ///
    /// <para>Called for any visible change, including small ones: a wall losing
    /// health a swing at a time produces a call per swing. That is deliberate —
    /// coalescing is a rendering judgement (DK2 shows damage as a handful of
    /// discrete texture states, and how many is UNKNOWN, see
    /// <c>wall-rendering.md</c>) and the presenter refuses to invent the number.
    /// A target that only draws three damage states simply ignores the rest.</para>
    /// </summary>
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
    /// Every push is finished. A target that batches — one combined mesh per
    /// terrain type, say — rebuilds here.
    /// </summary>
    void Flush();
}
