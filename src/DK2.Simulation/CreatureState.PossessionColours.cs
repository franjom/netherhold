// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>Transient client notifications, excluded from hashing and snapshots.</summary>
public enum PossessionColourEvent : byte { Damaged, Thawed }

public sealed partial class CreatureState
{
    public event Action<PossessionColourEvent>? PossessionColourChanged;
    internal void NotifyPossessionColour(PossessionColourEvent notification)
    {
        if (Possessed)
        {
            PossessionColourChanged?.Invoke(notification);
        }
    }
}
