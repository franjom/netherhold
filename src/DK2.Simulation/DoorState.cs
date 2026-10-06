// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// One door projected from a placed THING. Authored bytes remain verbatim; the
/// construction predicate is the one runtime value currently derived from them.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/doors-and-traps.md")]
public sealed class DoorState
{
    /// <summary>The door's id, given by <see cref="WorldState.AddDoor"/> from 1 and never reused,
    /// so a blueprint, a crate's order and a forced target name it across a removal. Ours: DK2's
    /// id is a slot in the one thing table every class shares.</summary>
    public int Id { get; internal set; }

    public int X { get; init; }
    public int Y { get; init; }
    public int Unknown_0008 { get; init; }
    public ushort TriggerId { get; init; }
    public byte DoorId { get; init; }

    /// <summary>The mixed-class tile chain link (<see cref="ThingTileLink"/>), given once by
    /// <see cref="WorldState.AddDoor"/>: DK2 constructs a door on layer 0 (<c>0x004A50BF</c>).</summary>
    public ThingTileLink TileLink { get; internal set; } = ThingTileLink.None;

    public byte PlayerId { get; init; }
    public byte Flags { get; init; }
    public bool UnderConstruction { get; set; }

    /// <summary>DK2's <c>+0x111</c>: 0 opens for every allied creature, 1 for none of the
    /// workers, 2 for workers only, 3 for nobody. The loader seeds 3 from authored flag bit 0,
    /// and the owner's lock command toggles it between 0 and 3 — modes 1 and 2 have their arms
    /// in the update but no caller in v1.70 (doors-and-traps.md).</summary>
    public byte LockMode { get; set; }

    /// <summary>DK2's <c>+0x115</c>: ticks left on a picked lock, counted down one per update
    /// (<c>0x004A56A1</c>) and, while it is not zero, <b>forcing the door open</b> ahead of every
    /// other rule including lock mode 3 (<c>0x004A5D52</c>).</summary>
    /// <remarks><b>Nothing sets it.</b> The one writer is <c>0x004A6DF0</c>, reached from the
    /// effect handler <c>0x00474F80</c> that a creature ability dispatches through a descriptor
    /// table; which ability owns that descriptor is UNKNOWN, and abilities are not built, so a
    /// lock here is never picked. The duration it writes is the product of two globals in
    /// uninitialised data, <c>[0x006F28D3] * [0x006F2590]</c>, so its value is unread as
    /// well.</remarks>
    public int LockPickTicks { get; set; }

    /// <summary>DK2's <c>+0x10D</c>, recomputed every tick by <see cref="Systems.DoorSystem"/>:
    /// while open the tile's navigation byte is plain floor for everyone.</summary>
    public bool Open { get; set; }

    /// <summary>0, 1 or 2, fixed when the door is made
    /// (<see cref="BlueprintPlacementRules.Orientation"/>): which pair of neighbours are its
    /// walls: 0 north and south, otherwise west and east. The opening probes and the support
    /// check read it (door-blocking.md).</summary>
    public byte Orientation { get; init; }

    /// <summary>DK2's common health <c>+0x40</c>, seeded from the definition's <c>Health</c>
    /// (<c>def+0x91</c>) by the constructor <c>0x004A5070</c>. At 0 or below the door is dead
    /// (<c>0x004B5C30</c>) and removed the same turn (door-blocking.md).</summary>
    public int Health { get; set; }

    /// <summary>DK2's <c>+0x11A</c>, the recovery deadline: 0 when made, advanced one game second
    /// each time the update passes it while the definition recovers (door-blocking.md).</summary>
    public long RecoveryDeadline { get; set; }

    /// <summary>How long <see cref="Unknown_0011"/> is. The snapshot writes those bytes
    /// without a length and reads back exactly this many, so a door carrying any other
    /// number would desync the stream and silently corrupt everything after it.</summary>
    public const int Unknown0011Length = 3;

    /// <summary>Bytes 17..19 of the record, kept together because nothing yet says they
    /// are separate fields (Q252).</summary>
    public required ReadOnlyMemory<byte> Unknown_0011
    {
        get => _unknown0011;
        init => _unknown0011 = value.Length == Unknown0011Length
            ? value
            : throw new ArgumentException(
                $"A door's trailing unknown is {Unknown0011Length} bytes, not {value.Length}.",
                nameof(Unknown_0011));
    }

    private readonly ReadOnlyMemory<byte> _unknown0011;

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(X);
        hash.Add(Y);
        hash.Add(Unknown_0008);
        hash.Add(TriggerId);
        hash.Add(DoorId);
        TileLink.HashInto(ref hash);
        hash.Add(PlayerId);
        hash.Add(Flags);
        hash.Add(UnderConstruction);
        hash.Add(LockMode);
        hash.Add(LockPickTicks);
        hash.Add(Open);
        hash.Add(Orientation);
        hash.Add(Health);
        hash.Add(RecoveryDeadline);
        foreach (byte value in Unknown_0011.Span) hash.Add(value);
    }
}
