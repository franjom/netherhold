// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>What a worker is doing, by DK2's own task-kind numbers.</summary>
/// <remarks>
/// <para>DK2 stores this as the byte <c>ImpJobData.LastTaskType</c>, written by each
/// task-accepting routine when it takes the task; these values were read from those writes.
/// docs/research/imp-task-priority.md §3 has all sixteen; these are the eight built here.</para>
/// <para>The values are load-bearing in one place: <see cref="CreatureState.DroppedTask"/> is
/// compared against <see cref="CreatureState.LastTaskKind"/> as raw bytes, as DK2 does. Gaps are
/// kinds not built here: 9/10 (fetch a crate for a trap or door) and 11 (unidentified).</para>
/// </remarks>
public enum ImpTaskKind : byte
{
    /// <summary>No task. DK2 writes a literal zero and tests against it.</summary>
    None = 0,

    /// <summary>
    /// Dig or mine a tile the keeper marked — DK2's kinds <b>1 and 2</b>, always
    /// raised and taken as a pair; one value here because nothing distinguishes them.
    /// </summary>
    Excavate = 1,

    /// <summary>Claim a floor tile — DK2's kind <b>3</b>.</summary>
    Claim = 3,

    /// <summary>
    /// Reinforce a wall — DK2's kind <b>6</b>, which shares a handler with kind
    /// 16 ("Fix"). Same pairing as <see cref="Excavate"/>.
    /// </summary>
    Reinforce = 6,

    /// <summary>Pick gold up off the floor — DK2's kind <b>7</b>.</summary>
    FetchGold = 7,

    /// <summary>Carry a loose object to its room — DK2's kind <b>8</b>.</summary>
    CarryObject = 8,

    /// <summary>Carry a corpse to a Graveyard — DK2's kind <b>12</b>.</summary>
    CarryCorpse = 12,

    /// <summary>
    /// Carry an unconscious creature — one of the keeper's own to a Lair, an
    /// enemy's to a Prison. DK2's kind <b>13</b>, both branches.
    /// </summary>
    CarryUnconscious = 13,

    /// <summary>
    /// Claim the tile just dug out — DK2's kind <b>14</b>, highest priority.
    ///
    /// <para>DK2 never stores 14: kind 14's handler raises a kind 3 at the tile
    /// and takes that, so the stored byte is 3. This project keeps the value only
    /// because <c>JobPriority</c> distinguishes it; <see cref="CreatureState.LastTaskKind"/>
    /// records <see cref="Claim"/> for both, as the original does.</para>
    /// </summary>
    ClaimFreshlyDug = 14,
}
