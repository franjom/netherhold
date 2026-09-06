// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>
/// What a worker is doing, by <b>DK2's own task-kind numbers</b>.
///
/// <para>The original keeps this as a byte its debug dump calls
/// <c>ImpJobData.LastTaskType</c>, and every one of its task-accepting routines
/// writes its own number into it the moment it takes the task — which is how
/// the numbers below were read rather than assigned
/// (docs/research/imp-task-priority.md has the full table of sixteen; these are
/// the eight this project builds).</para>
///
/// <para><b>The values are the original's and are load-bearing in exactly one
/// place</b>: <see cref="CreatureState.DroppedTask"/> is compared against
/// <see cref="CreatureState.LastTaskKind"/>, and DK2 compares the raw bytes. A
/// gap in the numbering is a kind this project has not built, not a mistake —
/// 9/10 (fetch a crate for a trap or door) and 11 (unidentified) are the ones
/// missing.</para>
/// </summary>
public enum ImpTaskKind : byte
{
    /// <summary>No task. DK2 writes a literal zero and tests against it.</summary>
    None = 0,

    /// <summary>
    /// Dig or mine a tile the keeper marked — DK2's kinds <b>1 and 2</b>, which
    /// its own chain always raises and takes as a pair. One byte here because
    /// nothing this project does distinguishes them.
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
    /// Claim the tile just dug out — DK2's kind <b>14</b>, first of everything.
    ///
    /// <para><b>DK2 never stores this one.</b> Kind 14's handler raises a kind 3
    /// at the tile and takes <i>that</i>, so the byte it writes is 3 and a
    /// worker that took the fast lane is indistinguishable afterwards from one
    /// that claimed the ordinary way. This project keeps the distinction only
    /// because its <c>JobPriority</c> does; <see cref="CreatureState.LastTaskKind"/>
    /// records <see cref="Claim"/> for both, exactly as the original does.</para>
    /// </summary>
    ClaimFreshlyDug = 14,
}
