// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Steering;

/// <summary>A creature turning on the spot to face a point, one tick at a time
/// (<c>0x004B2AE0</c>).</summary>
public static class CreatureTurn
{
    /// <summary>Turns the creature toward a world point by at most its turn rate and returns the
    /// angle still to turn, zero once it faces the point. The difference wraps as DK2's 32-bit
    /// subtraction does, which only a point far off the map can reach.</summary>
    public static int Toward(GameDatabase database, CreatureState creature, int pointX, int pointY)
    {
        int bearing = Bearing.Of(unchecked(pointX - creature.PosX), unchecked(pointY - creature.PosY));
        int turnRate = SimulationTiming.TurnRatePerTick(database.CreatureById(creature.CreatureTypeId));

        int heading = creature.Heading;
        int angleLeft = Bearing.TurnToward(ref heading, bearing, turnRate);
        creature.Heading = heading;
        return angleLeft;
    }
}
