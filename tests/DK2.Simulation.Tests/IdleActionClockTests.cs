// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>Idle actions 43 and 44 and the install's coin flip (animation-clock.md, ADR-0515). The
/// lengths are the Level 4 capture's: 24 turns for slot 20, 25 for slot 31, 19 when the install
/// swaps in IMP-Idle4; 7 of the 14 rests in turns 1-1100 were swapped.</summary>
public sealed class IdleActionClockTests
{
    private const uint ImpIdleFlags = 0x20401;
    private const uint ImpDigFlags = 0x28011;

    private static CreatureAnimationTiming Clip(ushort frames, uint flags) => new()
    {
        ResourceType = 6, Flags = (CreatureAnimationFlags)flags, FrameCount = frames, FramesPerSecond = 30,
    };

    /// <summary>The Imp's shipped idle art: slots 20 and 31, and IMP-Idle4 at +0x1083.</summary>
    internal static GameDatabase WithImpIdleArt(GameDatabase source)
    {
        return new GameDatabase(source.Terrain.Values, source.Rooms.Values,
            source.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.ImpCreatureId ? ImpWithArt(c) : c),
            source.Variables, objects: source.Objects.Values, keeperSpells: source.KeeperSpells.Values);
    }

    private static CreatureDef ImpWithArt(CreatureDef imp)
    {
        var art = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        art[5] = Clip(15, ImpDigFlags) with { Flags = (CreatureAnimationFlags)ImpDigFlags, EndSegmentFrames = 8, FramesPerSecond = 20 };
        art[20] = Clip(171, ImpIdleFlags);
        art[31] = Clip(172, ImpIdleFlags);
        return imp with { ArtSlots = art, AlternateArt = Clip(129, 0x20001) };
    }

    private static SimRandom RngWhoseFirstCoinIs(int side)
    {
        for (ulong seed = 1; ; seed++)
            if (new SimRandom(seed).NextInt(2) == side) return new SimRandom(seed);
    }

    /// <summary>Plays a counted action as the runner and chooser do: begin and advance on turn 0,
    /// then each turn poll first and advance last. Returns the turn the action ends.</summary>
    private static int PlayLength(CreatureDef imp, byte slot, int coin)
    {
        var creature = new CreatureState();
        ActionAnimationClock.BeginCounted(creature, imp, slot, plays: 1, RngWhoseFirstCoinIs(coin));
        ActionAnimationClock.Advance(creature, imp);
        for (int turn = 1; turn < 100; turn++)
        {
            if (ActionAnimationClock.CountedActionEnded(creature)) return turn;
            ActionAnimationClock.Advance(creature, imp);
        }
        throw new InvalidOperationException("never ended");
    }

    [Theory]
    [InlineData(20, 0, 24)]
    [InlineData(31, 0, 25)]
    [InlineData(20, 1, 19)]
    [InlineData(31, 1, 19)]
    public void AnIdleActionLastsOnePlayOfItsClip_CapturedLengths(byte slot, int coin, int turns)
    {
        CreatureDef imp = ImpWithArt(Dk2TerrainFixture.Database().CreatureById(Dk2TerrainFixture.ImpCreatureId));
        Assert.Equal(turns, PlayLength(imp, slot, coin));
    }

    [Fact]
    public void TheSwapIsMarkedAndPlaysTheAlternateClip()
    {
        CreatureDef imp = ImpWithArt(Dk2TerrainFixture.Database().CreatureById(Dk2TerrainFixture.ImpCreatureId));
        var creature = new CreatureState();
        ActionAnimationClock.BeginCounted(creature, imp, 20, plays: 1, RngWhoseFirstCoinIs(1));
        Assert.Equal(238, creature.ActionAnimationRate);
        Assert.True((creature.ActionAnimationFlags & ActionAnimationFlags.Alternate) != 0);
        Assert.False((creature.ActionAnimationFlags & ActionAnimationFlags.Loop) != 0);
        Assert.Equal(2, creature.ActionCount);
    }

    // Dig art carries 0x8000 but not 0x400: a fresh install draws once and never swaps.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ADigInstallDrawsOnceAndNeverSwaps(int coin)
    {
        CreatureDef imp = ImpWithArt(Dk2TerrainFixture.Database().CreatureById(Dk2TerrainFixture.ImpCreatureId));
        SimRandom rng = RngWhoseFirstCoinIs(coin), twin = RngWhoseFirstCoinIs(coin);
        var creature = new CreatureState();
        ActionAnimationClock.Begin(creature, imp, 5, rng);
        twin.NextInt(2);
        Assert.Equal(twin.NextUInt64(), rng.NextUInt64());
        Assert.False((creature.ActionAnimationFlags & ActionAnimationFlags.Alternate) != 0);
        Assert.Equal(1365, creature.ActionAnimationRate);
    }

    [Fact]
    public void ArtWithoutTheDrawFlagsDrawsNothing_AndAKeptClipDrawsNothing()
    {
        CreatureDef imp = ImpWithArt(Dk2TerrainFixture.Database().CreatureById(Dk2TerrainFixture.ImpCreatureId))
            with { ArtSlots = [.. Enumerable.Repeat(Clip(171, 0x1), CreatureDef.ArtSlotCount)] };
        var rng = new SimRandom(7);
        var twin = new SimRandom(7);
        var creature = new CreatureState();
        ActionAnimationClock.Begin(creature, imp, 20, rng);
        Assert.Equal(twin.NextUInt64(), rng.NextUInt64());

        CreatureDef drawing = ImpWithArt(imp);
        var kept = new CreatureState();
        ActionAnimationClock.Begin(kept, drawing, 5, rng);
        twin.NextInt(2);
        ActionAnimationClock.Begin(kept, drawing, 5, rng);
        Assert.Equal(twin.NextUInt64(), rng.NextUInt64());
    }
}
