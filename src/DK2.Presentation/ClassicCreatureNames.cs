// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>A creature's own name and blood group by the indices its state holds, for the query details.</summary>
public interface IClassicCreatureNames
{
    string Name(CreatureNameIndex index);
    string BloodGroup(CreatureBloodGroupIndex index);
}

/// <summary>The names and blood groups read in place from the install's <c>DKII.EXE</c> (<see cref="CreatureNamePool"/>),
/// shared by every engine adapter (migration plan step 1b).</summary>
public sealed class InstallClassicCreatureNames : IClassicCreatureNames
{
    private readonly CreatureNamePool _pool;
    public InstallClassicCreatureNames(string install)
    {
        _pool = CreatureNamePool.Open(Path.Combine(install, "DKII.EXE"));
    }

    public string Name(CreatureNameIndex index)
    {
        return _pool.Name(index);
    }

    public string BloodGroup(CreatureBloodGroupIndex index)
    {
        return _pool.BloodGroup(index);
    }
}
