// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// One effect generator: the THINGS records in file order, then the one the level initialiser adds
/// (<see cref="EffectGeneratorRules"/>). Each turn's update is <c>0x004B1B60</c> (ADR-0648).
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-parties-and-effect-generators.md")]
public sealed partial class EffectGeneratorState
{
    public const int Unknown001ELength = 6;

    public int X { get; init; }
    public int Y { get; init; }
    public int Unknown_0008 { get; init; }
    public int Unknown_000C { get; init; }
    public ushort Unknown_0010 { get; init; }
    public ushort Unknown_0012 { get; init; }
    public required IReadOnlyList<ushort> EffectIds { get; init; }
    public byte Frequency { get; init; }
    public byte GeneratorId { get; init; }
    public required ReadOnlyMemory<byte> Unknown_001E
    {
        get => _unknown001E;
        init => _unknown001E = value.Length == Unknown001ELength
            ? value
            : throw new ArgumentException(
                $"An effect generator's trailing unknown is {Unknown001ELength} bytes, not {value.Length}.",
                nameof(Unknown_001E));
    }

    private readonly ReadOnlyMemory<byte> _unknown001E;

    /// <summary>The position, <c>+0x1A/+0x1E/+0x22</c>: the spawn rectangle's corner.</summary>
    public int PosX { get; init; }
    public int PosY { get; init; }
    public int PosZ { get; init; }

    /// <summary>The turn after which the next effect is due, <c>+0x2E</c>; zero at creation.</summary>
    public uint NextDueTurn;

    /// <summary>The fields the THINGS loader <c>0x00521380</c> reads (Q259): position, the spawn
    /// rectangle's width and height (<c>Unknown_0010</c>, <c>Unknown_0012</c>), the four effect ids,
    /// the frequency and the id. Body <c>+0x08</c>, <c>+0x0C</c> and the six bytes at <c>+0x1E</c>
    /// are never read, so they are kept for the round trip and not hashed.</summary>
    public void HashInto(ref Hash64 hash)
    {
        hash.Add(X); hash.Add(Y);
        hash.Add(Unknown_0010); hash.Add(Unknown_0012);
        hash.Add(EffectIds.Count);
        foreach (ushort effectId in EffectIds) hash.Add(effectId);
        hash.Add(Frequency); hash.Add(GeneratorId);
        hash.Add(PosX); hash.Add(PosY); hash.Add(PosZ);
        hash.Add(NextDueTurn);
    }
}
