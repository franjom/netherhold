// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Simulation;
using DK2.TestSupport;
namespace DK2.Presentation.Tests;
public sealed class ClassicCreatureQueryTests
{
    [Theory]
    [InlineData(0,0)] [InlineData(63,63)] [InlineData(65,1)] [InlineData(2047,63)]
    public void QueryRetainsOnlyNativeYawRemainder(ushort yaw,ushort expected)
        => Assert.Equal(expected,ClassicCreatureQueryRules.FollowHeading(new(yaw)).Value);
    [InstallFact]
    public void QueryUsesOwnedCreatureTemplateAndRetainsSnapshotUntilSettledFactsChange()
    {
        GameDatabase db=GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world=WorldState.FromMap(db,GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path,"Level1")!,[PlayerIds.FirstKeeper],fogOfWarEnabled:false);
        CreatureState creature=world.SpawnCreature(db,db.CreatureByName("Imp")!.CreatureId,PlayerIds.FirstKeeper,new(8,8));
        creature.GoldHeld=17;
        var strings=Dk2StringTable.Open(Path.Combine(Dk2InstallGate.Path,"Data","Text","Default","Text.str"));
        var query=new ClassicCreatureQuery(db,strings.Resolve,PlayerIds.FirstKeeper);
        var target=new CreatureQueryTarget(creature.Id);
        var first=query.Capture(world,target,"Delver","A");
        Assert.Contains("Delver the Imp",first.Text);
        Assert.Contains("Blood Group: A",first.Text);
        Assert.Contains("Gold Held: 17",first.Text);
        Assert.Same(first.Text,query.Capture(world,target,"Delver","A").Text);
        creature.GoldHeld=23;
        Assert.Contains("Gold Held: 23",query.Capture(world,target,"Delver","A").Text);
        creature.OwnerPlayerId=4;
        Assert.Equal("",query.Capture(world,target,"Delver","A").Text);
    }
}
