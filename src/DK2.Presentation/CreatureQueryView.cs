// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The queried creature, kept between frames, and the details text it shows (<see cref="ClassicCreatureQuery"/>
/// with the creature's own name and blood group). A host sets the target on a query click and clears it on the query's
/// cancels; each <see cref="Capture"/> keeps the target the snapshot keeps, so it drops once the creature is gone or out
/// of every state.</summary>
public sealed class CreatureQueryView
{
    private readonly ClassicCreatureQuery _query;
    private readonly IClassicCreatureNames _names;

    public CreatureQueryView(GameDatabase database, Func<uint, string> resolveString, PlayerId viewingPlayer, IClassicCreatureNames names)
    {
        _query = new ClassicCreatureQuery(database, resolveString, viewingPlayer);
        _names = names ?? throw new ArgumentNullException(nameof(names));
    }

    /// <summary>The queried creature, <see cref="CreatureQueryTarget.None"/> while none is.</summary>
    public CreatureQueryTarget Target { get; private set; }

    /// <summary>The last <see cref="Capture"/>'s details, empty before the first.</summary>
    public CreatureQuerySnapshot Snapshot { get; private set; } = CreatureQuerySnapshot.Empty;

    public void Query(CreatureQueryTarget target)
    {
        Target = target;
    }

    public void Clear()
    {
        Target = CreatureQueryTarget.None;
    }

    /// <summary>The details for the target in <paramref name="world"/>, which become <see cref="Snapshot"/>.</summary>
    public CreatureQuerySnapshot Capture(WorldState world)
    {
        CreatureState? creature = world.CreatureById(Target.Value);
        string name = creature == null ? "" : _names.Name(new CreatureNameIndex(creature.NameIndex));
        string blood = creature == null ? "" : _names.BloodGroup(new CreatureBloodGroupIndex(creature.BloodGroupIndex));
        Snapshot = _query.Capture(world, Target, name, blood);
        Target = Snapshot.Target;
        return Snapshot;
    }
}
