// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>What a worker is doing, by DK2's own task-kind numbers.</summary>
/// <remarks>
/// <para>DK2 stores this as the byte <c>ImpJobData.LastTaskType</c>, written by each
/// task-accepting routine when it takes the task; these values were read from those writes.
/// docs/research/imp-task-priority.md §3 identifies the kinds; these are the ten built here.</para>
/// <para>The values are load-bearing in one place: <see cref="CreatureState.DroppedTask"/> is
/// compared against <see cref="CreatureState.LastTaskKind"/> as raw bytes, as DK2 does. Not built:
/// kind 11, a marked Secret Door (<c>0x0046CB80</c>), and 16, fix a wall; 4 and 5 are unidentified.</para>
/// </remarks>
public enum ImpTaskKind : byte
{
    /// <summary>No task. DK2 writes a literal zero and tests against it.</summary>
    None = 0,

    /// <summary>
    /// Dig a tile the keeper marked — DK2's kind <b>1</b>. Its whole-map walk takes marked
    /// seams too, so a search for this kind finds either; a job it takes on a seam is
    /// recorded as <see cref="Mine"/>.
    /// </summary>
    Excavate = 1,

    /// <summary>
    /// Mine a marked seam — DK2's kind <b>2</b>, which its walk writes when it took a seam.
    /// Searched by the same walk as <see cref="Excavate"/>; what differs is that the next
    /// search measures seams from the seam just mined (<c>imp-task-priority.md</c> §4).
    /// </summary>
    Mine = 2,

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

    /// <summary>Carry a crate to a placed trap and build it — DK2's kind <b>9</b>, states 34 and
    /// 35 (ADR-0214).</summary>
    BuildTrap = 9,

    /// <summary>Carry a crate to a placed door and build it — DK2's kind <b>10</b>, states 36 and
    /// 37 (ADR-0214).</summary>
    BuildDoor = 10,

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
