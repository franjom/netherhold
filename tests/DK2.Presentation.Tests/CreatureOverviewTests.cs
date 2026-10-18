// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;
namespace DK2.Presentation.Tests;
public sealed class CreatureOverviewTests
{
    [InstallFact]
    public void AlternateViewsCountActualMoodAndKeepPitSelectionDistinctFromTrainingCount()
    {
        GameDatabase db=GameDatabaseLoader.Load(Dk2InstallGate.Path);
        WorldState world=WorldState.FromMap(db,GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path,"Level1")!,[PlayerIds.FirstKeeper],fogOfWarEnabled:false);
        CreatureDef goblin=db.CreatureByName("Goblin")!;
        CreatureState creature=world.SpawnCreature(db,goblin.CreatureId,PlayerIds.FirstKeeper,new(8,8));
        creature.Mood=CreatureMood.Angry;creature.CombatPitFighter=true;
        creature.Dk2StateId=CreatureStateTable.InCategory(CreatureStateCategory.Idle).First();
        var panel=new CreatureOverview(db,PlayerIds.FirstKeeper,(_,fallback)=>fallback);
        var type=new CreatureOverviewType(goblin.CreatureId);
        panel.Capture(world);
        Assert.Equal(1,panel.Count(type,CreatureOverviewFilter.Angry));
        Assert.Equal(0,panel.Count(type,CreatureOverviewFilter.Content));
        Assert.Equal(0,panel.Count(type,CreatureOverviewFilter.Training));
        Assert.Equal(creature.Id,panel.Select(world,type,CreatureOverviewSelection.FocusNext,CreatureOverviewFilter.Training).Target.Value);
        Assert.Equal(CreatureOverviewFilter.OtherJobs,CreatureOverview.RowFilter(CreatureOverviewMode.Jobs,3));
        Assert.Equal(3,CreatureOverview.Rows(CreatureOverviewMode.Moods));
    }
    [InstallFact]
    public void WorkerRemainsFirstAndHeldCreaturesStayInRosterButNotDisplayedCount()
    {
        GameDatabase db=GameDatabaseLoader.Load(Dk2InstallGate.Path);
        WorldState world=WorldState.FromMap(db,GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path,"Level1")!,[PlayerIds.FirstKeeper],fogOfWarEnabled:false);
        var panel=new CreatureOverview(db,PlayerIds.FirstKeeper,(_,fallback)=>fallback,_=>0);
        CreatureDef goblin=db.CreatureByName("Goblin")!;
        CreatureState first=world.SpawnCreature(db,goblin.CreatureId,PlayerIds.FirstKeeper,new(8,8));
        CreatureState second=world.SpawnCreature(db,goblin.CreatureId,PlayerIds.FirstKeeper,new(9,8));
        panel.Capture(world);
        Assert.True(db.CreatureById(panel.Entries[0].Type.Value).Flags.HasFlag(CreatureFlags.IsWorker));
        Assert.Equal(2,panel.Entries.Single(r=>r.Type.Value==goblin.CreatureId).Count);
        second.InHand=true;
        panel.Capture(world);
        Assert.Equal(1,panel.Entries.Single(r=>r.Type.Value==goblin.CreatureId).Count);
        first.InHand=true;
        panel.Capture(world);
        Assert.Equal(0,panel.Entries.Single(r=>r.Type.Value==goblin.CreatureId).Count);
    }
    [InstallFact]
    public void PickupCyclesNewestOwnershipFirstAndFocusDoesNotRequirePickupPermission()
    {
        GameDatabase db=GameDatabaseLoader.Load(Dk2InstallGate.Path);
        WorldState world=WorldState.FromMap(db,GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path,"Level1")!,[PlayerIds.FirstKeeper],fogOfWarEnabled:false);
        CreatureDef goblin=db.CreatureByName("Goblin")!;
        CreatureState first=world.SpawnCreature(db,goblin.CreatureId,PlayerIds.FirstKeeper,new(8,8));
        CreatureState second=world.SpawnCreature(db,goblin.CreatureId,PlayerIds.FirstKeeper,new(9,8));
        var panel=new CreatureOverview(db,PlayerIds.FirstKeeper,(_,fallback)=>fallback,_=>0);
        var type=new CreatureOverviewType(goblin.CreatureId);
        Assert.Equal(second.Id,panel.Select(world,type,CreatureOverviewSelection.PickupNext).Target.Value);
        Assert.Equal(first.Id,panel.Select(world,type,CreatureOverviewSelection.PickupNext).Target.Value);
        first.Level=2;second.Level=4;
        Assert.Equal(second.Id,panel.Select(world,type,CreatureOverviewSelection.PickupHighestLevel).Target.Value);
        Assert.Equal(first.Id,panel.Select(world,type,CreatureOverviewSelection.PickupLowestLevel).Target.Value);
        second.ManaRemovalStamp=1;
        Assert.Equal(second.Id,panel.Select(world,type,CreatureOverviewSelection.FocusNext).Target.Value);
    }
    [InstallFact]
    public void WorkerFiltersMatchDisplayedActivityCounts()
    {
        GameDatabase db=GameDatabaseLoader.Load(Dk2InstallGate.Path);
        WorldState world=WorldState.FromMap(db,GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path,"Level1")!,[PlayerIds.FirstKeeper],fogOfWarEnabled:false);
        CreatureDef worker=db.Creatures.Values.First(d=>(d.Flags&(CreatureFlags.IsEvil|CreatureFlags.IsWorker))==(CreatureFlags.IsEvil|CreatureFlags.IsWorker));
        CreatureState idle=world.SpawnCreature(db,worker.CreatureId,PlayerIds.FirstKeeper,new(8,8));
        idle.WorkerIdle=true;
        var panel=new CreatureOverview(db,PlayerIds.FirstKeeper,(_,fallback)=>fallback,_=>0);
        panel.Capture(world);
        Assert.True(panel.WorkerCounts.Idle>0);
        CreatureOverviewAction action=panel.Select(world,new(worker.CreatureId),CreatureOverviewSelection.FocusNext,CreatureOverviewFilter.Idle);
        Assert.Equal(CreatureOverviewActionKind.Focus,action.Kind);
        Assert.Equal(idle.Id,action.Target.Value);
    }

}
