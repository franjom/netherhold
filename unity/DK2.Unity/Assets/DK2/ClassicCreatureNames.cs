// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.IO;
using DK2.Data;
using DK2.Data.Model;

namespace DK2.Unity
{
    public interface IClassicCreatureNames
    {
        string Name(CreatureNameIndex index);
        string BloodGroup(CreatureBloodGroupIndex index);
    }

    public sealed class InstallClassicCreatureNames : IClassicCreatureNames
    {
        private readonly CreatureNamePool _pool;
        public InstallClassicCreatureNames(string install) => _pool = CreatureNamePool.Open(Path.Combine(install, "DKII.EXE"));
        public string Name(CreatureNameIndex index) => _pool.Name(index);
        public string BloodGroup(CreatureBloodGroupIndex index) => _pool.BloodGroup(index);
    }
}
