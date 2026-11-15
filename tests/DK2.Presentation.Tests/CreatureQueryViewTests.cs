// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The query target and details both adapters share (migration plan step 1b, moved from Unity's runner).</summary>
public sealed class CreatureQueryViewTests
{
    [InstallFact]
    public void InstallNamesAreTheShippedPoolsEntries()
    {
        var names = new InstallClassicCreatureNames(Dk2InstallGate.Path);
        Assert.Equal("Delver", names.Name(new CreatureNameIndex(0)));
        Assert.Contains("Rh", names.BloodGroup(new CreatureBloodGroupIndex(0)));
        Assert.Equal("", names.Name(new CreatureNameIndex(ushort.MaxValue)));
    }

    [InstallFact]
    public void TargetShowsTheCreaturesOwnNameAndDropsWhenItIsGone()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        CreatureState imp = world.SpawnCreature(db, db.CreatureByName("Imp")!.CreatureId, PlayerIds.FirstKeeper, new(8, 8));
        imp.NameIndex = 0; imp.BloodGroupIndex = 0; imp.GoldHeld = 17;
        var strings = Dk2StringTable.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Text", "Default", "Text.str"));
        var view = new CreatureQueryView(db, strings.Resolve, PlayerIds.FirstKeeper, new InstallClassicCreatureNames(Dk2InstallGate.Path));

        Assert.Equal(CreatureQueryTarget.None, view.Target);
        Assert.Equal("", view.Capture(world).Text);

        view.Query(new CreatureQueryTarget(imp.Id));
        CreatureQuerySnapshot shown = view.Capture(world);
        Assert.Contains("Delver the Imp", shown.Text);
        Assert.Contains("Gold Held: 17", shown.Text);
        Assert.Contains("Blood Group: " + new InstallClassicCreatureNames(Dk2InstallGate.Path).BloodGroup(new CreatureBloodGroupIndex(0)), shown.Text);
        Assert.Equal(shown, view.Snapshot);
        Assert.Equal(imp.Id, view.Target.Value);

        view.Clear();
        Assert.Equal("", view.Capture(world).Text);

        view.Query(new CreatureQueryTarget(imp.Id + 100000));
        view.Capture(world);
        Assert.Equal(CreatureQueryTarget.None, view.Target);
    }
}
