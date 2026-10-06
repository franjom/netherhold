// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// One authored member of a hero party: the template action 14 raises a creature from
/// (<see cref="HeroPartyRules"/>), not a creature placed in the level (ADR-0108). Every
/// parsed field is retained verbatim.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-parties-and-effect-generators.md")]
public sealed class HeroPartyMemberState
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public ushort GoldHeld { get; init; }
    public byte Level { get; init; }
    public byte Flags { get; init; }
    public int ObjectiveTargetActionPointId { get; init; }
    public int InitialHealth { get; init; }
    public ushort TriggerId { get; init; }
    public byte ObjectiveTargetPlayerId { get; init; }
    public byte Objective { get; init; }
    public byte CreatureId { get; init; }
    /// <summary>The two bytes once stored as an unknown tail: copied by the spawn to the
    /// creature's <c>WanderRadius</c> (<c>+0x418</c>) and <c>PartyId</c> (<c>+0x417</c>). Every
    /// shipped member carries its own party's id and a zero radius (Q258).</summary>
    public byte WanderRadius { get; init; }
    public byte PartyId { get; init; }
    public byte Flags2 { get; init; }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(X); hash.Add(Y); hash.Add(Z); hash.Add(GoldHeld);
        hash.Add(Level); hash.Add(Flags); hash.Add(ObjectiveTargetActionPointId);
        hash.Add(InitialHealth); hash.Add(TriggerId); hash.Add(ObjectiveTargetPlayerId);
        hash.Add(Objective); hash.Add(CreatureId);
        hash.Add(WanderRadius); hash.Add(PartyId);
        hash.Add(Flags2);
    }
}

/// <summary>
/// One hero party projected verbatim in THINGS-file order. It is authored
/// state only: parties do not spawn, and their members are not placed creatures.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-parties-and-effect-generators.md")]
public sealed class HeroPartyState
{
    public required string Name { get; init; }
    public ushort TriggerId { get; init; }
    public byte PartyId { get; init; }
    public int Unknown_0023 { get; init; }
    public int Unknown_0027 { get; init; }
    public required IReadOnlyList<HeroPartyMemberState> Members { get; init; }

    /// <summary>How often action 14 has raised this party — DK2's byte at
    /// <c>world+0x70F0+PartyId</c>, added to a random party's member levels on each raising.</summary>
    public byte SpawnCount { get; set; }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Name); hash.Add(TriggerId); hash.Add(PartyId);
        hash.Add(Unknown_0023); hash.Add(Unknown_0027); hash.Add(SpawnCount);
        hash.Add(Members.Count);
        foreach (HeroPartyMemberState member in Members) member.HashInto(ref hash);
    }
}
