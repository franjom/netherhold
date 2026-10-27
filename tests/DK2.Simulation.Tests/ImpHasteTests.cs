// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The walk wrapper's ability scan (<c>0x004651A0</c>, ADR-0385): an imp task walk casts a
/// ready speed slot (the Imp's Imp Haste, slot 0, level 4) before it walks; a ready teleport slot
/// earlier in the list ends the scan. The shipped Imp carries Imp Haste in slot 0 at level 4 and
/// Imp Teleport in slot 1 at level 8; this worker carries the fixture's flag carriers the same way.</summary>
public class ImpHasteTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte HastyImpId = 50;

    private static readonly CreatureSpellSlot Speed = new(Dk2TerrainFixture.SpeedTestSpellId, 4, new Fp32(4096), 1);
    private static readonly CreatureSpellSlot Teleport = new(Dk2TerrainFixture.TeleportTestSpellId, 8, new Fp32(4096), 1);

    [Fact]
    public void ALevelFourImpCastsHasteAsItSetsOffOnATask()
    {
        (ListEventSink sink, CreatureState imp) = Run(level: 4, Speed, Teleport);

        SimEvent cast = Assert.Single(sink.Events, e => e.Kind == "speed_changed");
        Assert.Contains($"creature={imp.Id} percent=150", cast.Detail);
        Assert.Equal(cast.Tick.Value, imp.SpellStamps[0]);
    }

    [Fact]
    public void ALevelThreeImpHasNoHasteYet()
    {
        (ListEventSink sink, CreatureState imp) = Run(level: 3, Speed, Teleport);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "speed_changed");
        Assert.Equal(CreatureState.NeverUsed, imp.SpellStamps[0]);
    }

    /// <summary>A ready teleport slot before the speed slot ends the scan (<c>0x004651A0</c> breaks
    /// on flag <c>0x20</c>), so nothing is cast; the teleport itself is not built (Q418).</summary>
    [Fact]
    public void AReadyTeleportSlotFirstEndsTheScan()
    {
        (ListEventSink sink, _) = Run(level: 8, Teleport, Speed);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "speed_changed");
    }

    private static (ListEventSink Sink, CreatureState Imp) Run(int level, params CreatureSpellSlot[] spells)
    {
        CreatureDef hasty = Dk2TerrainFixture.Imp() with { CreatureId = HastyImpId, Name = "Hasty Imp", Spells = spells };
        GameDatabase db = Dk2TerrainFixture.Database(hasty);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        CreatureState imp = world.SpawnCreature(db, HastyImpId, Keeper, new TilePos(1, 2));
        imp.Level = level;
        var sim = new GameSimulation(db, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Run(SimulationTiming.TicksFromSeconds(4));
        Assert.Contains(sink.Events, e => e.Kind is "job_taken" or "fetch_taken");
        return (sink, imp);
    }
}
