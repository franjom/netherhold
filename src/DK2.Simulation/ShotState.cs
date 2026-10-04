// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>A shot in flight — DK2's shot thing (docs/research/shots.md §1), made by a creature's
/// spell cast (combat-spells.md §7) or by another shot it chained from. Positions are 1/4096
/// tile; the velocity is position units a tick, so the definition's <c>Speed</c> is the distance
/// covered each tick. Height is not modelled: every shot flies flat (Q398).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/shots.md")]
public sealed class ShotState
{
    public int Id;
    public byte ShotTypeId;
    public byte OwnerPlayerId;
    /// <summary>The position; a property so that every write keeps <see cref="TileLink"/> current. Each
    /// axis write relinks on its own, so a diagonal crossing takes two link orders (<see cref="WorldState.Relink(CreatureState)"/>).</summary>
    public int PosX
    {
        get => _posX;
        set
        {
            if (_posX == value) return;
            _posX = value;
            LinkWorld?.Relink(this);
        }
    }

    public int PosY
    {
        get => _posY;
        set
        {
            if (_posY == value) return;
            _posY = value;
            LinkWorld?.Relink(this);
        }
    }

    private int _posX, _posY;

    /// <summary>The world whose tile chain holds this shot, set while it is in that world's list.</summary>
    internal WorldState? LinkWorld;

    /// <summary>The mixed-class tile chain link (<see cref="ThingTileLink"/>): DK2 constructs a shot on
    /// layer 0 (<c>0x004AACF3</c>), so a possessed melee pick can meet it.</summary>
    public ThingTileLink TileLink { get; internal set; } = ThingTileLink.None;

    public int VelX;
    public int VelY;

    /// <summary>The definition's <c>Health</c>: a lifetime in ticks when its flags count it down, a
    /// number of hits otherwise (shots.md §4).</summary>
    public int Health;
    public int Age;

    /// <summary>The two data words the caster's spell wrote over the definition's defaults —
    /// contact damage and an area radius in tiles for the shipped creature shots.</summary>
    public int Data1;
    public int Data2;

    public int CasterId;
    public byte TargetKind;
    public int TargetId;
    public byte TargetPlayerId;
    public int TargetTileIndex = -1;

    /// <summary>The aim, in position units — the target's centre with the caster's lead; a shot
    /// chained from this one is placed here.</summary>
    public int TargetX;
    public int TargetY;

    /// <summary>DK2's <c>+0x14D</c>: the next collision check hits <see cref="TargetId"/> if its tag is
    /// live, whatever the overlap, and clears this word (shots.md §4a). Set by a keeper's cast at a
    /// thing (<c>0x004BBF90</c>) and by the instant handler.</summary>
    public bool ForcedCollision;

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(ShotTypeId);
        hash.Add(OwnerPlayerId);
        hash.Add(PosX);
        hash.Add(PosY);
        TileLink.HashInto(ref hash);
        hash.Add(VelX);
        hash.Add(VelY);
        hash.Add(Health);
        hash.Add(Age);
        hash.Add(Data1);
        hash.Add(Data2);
        hash.Add(CasterId);
        hash.Add(TargetKind);
        hash.Add(TargetId);
        hash.Add(TargetPlayerId);
        hash.Add(TargetTileIndex);
        hash.Add(TargetX);
        hash.Add(TargetY);
        hash.Add(ForcedCollision);
    }
}
