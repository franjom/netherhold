// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;
/// <summary>Action 30: toggles native creature flag bit 11 and requests effect 201.</summary>
public sealed record TogglePossessionFormCommand(PlayerId PlayerId) : PlayerCommand(PlayerId);
/// <summary>Action 31: toggles native creature flag bit 13 (the possession prayer).</summary>
public sealed record TogglePossessionPrayerCommand(PlayerId PlayerId) : PlayerCommand(PlayerId);
/// <summary>Pick Lock's prison exception (action136); outside a prison the native action36 is inert.</summary>
public sealed record OpenPossessedPrisonCommand(PlayerId PlayerId, TilePos PrisonAt) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(PrisonAt.X);
        hash.Add(PrisonAt.Y);
    }
}
/// <summary>Primary kind9's action137: a target/source creature pair and threat amount.</summary>
public sealed record FrightenPossessedCreatureCommand(PlayerId PlayerId, CreatureId Target, CreatureId Source, FearAmount Amount) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(Target.Value);
        hash.Add(Source.Value);
        hash.Add(Amount.Value);
    }
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed record LeavePossessionCommand(PlayerId PlayerId) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash) => base.HashInto(ref hash);
}

/// <summary>Action 26 (<c>0x00514830</c>): the possessed creature strikes (<paramref name="Melee"/>)
/// or uses <paramref name="Slot"/>, looking along <paramref name="Aim"/>. The simulation picks the target.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed record ActPossessedCreatureCommand(PlayerId PlayerId, PossessionAim Aim, bool Melee, PossessedSlot Slot) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        Aim.HashInto(ref hash);
        hash.Add(Melee);
        hash.Add(Slot.Value);
    }
}

/// <summary>Action 38 (<c>0x00516E40</c>): the possessed creature's followers are let go. The
/// input routine <c>0x0040FFB0</c> posts it (key binding 13, HYPOTHESIS); no packet decoder here builds it.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-arms-and-inputs.md")]
public sealed record DismissFollowersCommand(PlayerId PlayerId) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash) => base.HashInto(ref hash);
}

/// <summary>Command 0x1D (<c>0x00517270</c>): the possessed creature disguises itself as the owner
/// of the tile named, or ends the disguise (<see cref="PossessionRules.ToggleSneak"/>).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-arms-and-inputs.md")]
public sealed record ToggleSneakCommand(PlayerId PlayerId, int TileX, int TileY) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(TileX);
        hash.Add(TileY);
    }
}

/// <summary>Command 0x1B (<c>0x005163C0</c>): hypnotise the enemy looked at, or release the one held.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-arms-and-inputs.md")]
public sealed record HypnotiseCommand(PlayerId PlayerId, PossessionAim Aim) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        Aim.HashInto(ref hash);
    }
}

/// <summary>Game action 0x22 (<c>0x00517310</c>), sent by the possession movement step
/// <c>0x0040E910</c> when the creep key is pressed or released: Creeping on or off.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-client.md")]
public sealed record SetCreepingCommand(PlayerId PlayerId, bool On) : PlayerCommand(PlayerId)
{
}

/// <summary>Command 39 (<c>0x005171D0</c>), sent by the input routine <c>0x0040FDF0</c> with the new
/// value: SniperMode on or off.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-arms-and-inputs.md")]
public sealed record SetSniperModeCommand(PlayerId PlayerId, bool On) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add(On);
    }
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed record MovePossessedCreatureCommand(PlayerId PlayerId, PossessionPose Pose) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        Pose.HashInto(ref hash);
    }
}
