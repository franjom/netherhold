// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
namespace DK2.Presentation;

public readonly record struct CreatureOverviewType(byte Value);
public enum CreatureOverviewFilter : byte
{
    All=0, Fighting=4, Idle=5, Busy=6, Working=20, Training=21, Guarding=22,
    OtherJobs=23, OtherFighting=24, Angry=25, Unhappy=26, Content=27,
}
public enum CreatureOverviewMode : byte { Total,Jobs,Fighting,Moods }
public enum CreatureOverviewSelection : byte { PickupNext,PickupLowestLevel,PickupHighestLevel,FocusNext }
public enum CreatureOverviewActionKind : byte { None,Pickup,Focus }
public readonly record struct CreatureOverviewWorkerCounts(int Total,int Idle,int Busy,int Fighting);
public readonly record struct CreatureOverviewAction(CreatureOverviewActionKind Kind,CreatureQueryTarget Target,FpPosition3 Position);
public sealed class CreatureOverviewEntry
{
    internal CreatureOverviewEntry(CreatureOverviewType type,string name) { Type=type;Name=name; }
    public CreatureOverviewType Type { get; }
    public string Name { get; }
    public int Count { get; internal set; }
}

[Dk2Behavior(Confidence.StronglyInferred,"docs/research/creature-overview.md")]
public sealed class CreatureOverview
{
    private readonly GameDatabase _database;
    private readonly PlayerId _viewer;
    private readonly CreatureOverviewEntry[] _all;
    private readonly List<CreatureOverviewEntry> _entries=new();
    private readonly int[] _owned=new int[256],_counts=new int[256];
    private readonly int[] _filteredCounts=new int[256*28];
    private readonly CreatureQueryTarget[] _lastPickup=new CreatureQueryTarget[256];
    private readonly Dictionary<int,GameTick> _pending=new();
    private readonly List<int> _settled=new();
    private CreatureQueryTarget _lastFocus;
    private readonly byte _worker;
    public IReadOnlyList<CreatureOverviewEntry> Entries => _entries;
    public CreatureOverviewWorkerCounts WorkerCounts { get; private set; }
    public int Count(CreatureOverviewType type,CreatureOverviewFilter filter) =>
        filter==CreatureOverviewFilter.All?_counts[type.Value]:_filteredCounts[type.Value*28+(int)filter];
    public static int Rows(CreatureOverviewMode mode) => mode==CreatureOverviewMode.Total?1:mode==CreatureOverviewMode.Jobs?4:3;
    public static CreatureOverviewFilter RowFilter(CreatureOverviewMode mode,int row) => mode switch
    {
        CreatureOverviewMode.Jobs => row switch { 0=>CreatureOverviewFilter.Idle,1=>CreatureOverviewFilter.Working,2=>CreatureOverviewFilter.Training,_=>CreatureOverviewFilter.OtherJobs },
        CreatureOverviewMode.Fighting => row switch { 0=>CreatureOverviewFilter.Fighting,1=>CreatureOverviewFilter.Guarding,_=>CreatureOverviewFilter.OtherFighting },
        CreatureOverviewMode.Moods => row switch { 0=>CreatureOverviewFilter.Content,1=>CreatureOverviewFilter.Unhappy,_=>CreatureOverviewFilter.Angry },
        _=>CreatureOverviewFilter.All,
    };
    public CreatureOverview(GameDatabase database,PlayerId viewingPlayer,Func<CreatureOverviewType,string,string> name,
        Func<CreatureOverviewType,int>? order=null)
    {
        _database=database;_viewer=viewingPlayer;
        var definitions=database.Creatures.Values.Where(d=>!d.Flags.HasFlag(CreatureFlags.IsUnique))
            .OrderBy(d=>order?.Invoke(new(d.CreatureId))??d.CreatureId).ThenBy(d=>d.CreatureId).ToArray();
        _worker=definitions.First(d=>(d.Flags&(CreatureFlags.IsEvil|CreatureFlags.IsWorker))==(CreatureFlags.IsEvil|CreatureFlags.IsWorker)).CreatureId;
        _all=definitions.OrderBy(d=>d.CreatureId==_worker?0:1).Select(d=>new CreatureOverviewEntry(new(d.CreatureId),name(new(d.CreatureId),d.Name))).ToArray();
    }
    public void QueuePickupPreview(CreatureQueryTarget target,GameTick appliesAt) => _pending[target.Value]=appliesAt;
    public void Capture(WorldState world)
    {
        _settled.Clear();
        foreach(var pair in _pending) if(pair.Value<=world.CurrentTick) _settled.Add(pair.Key);
        foreach(int id in _settled) _pending.Remove(id);
        Array.Clear(_owned,0,_owned.Length);Array.Clear(_counts,0,_counts.Length);
        Array.Clear(_filteredCounts,0,_filteredCounts.Length);
        int idle=0,busy=0,fighting=0;
        foreach(CreatureState creature in world.Creatures)
        {
            if(creature.OwnerPlayerId!=_viewer || !CreatureStateMachine.HasState(creature)) continue;
            byte type=ResolvedType(creature);
            _owned[type]++;
            if(creature.Dk2StateId!=193 && !Suppressed(creature))
            {
                _counts[type]++;
                int activity=CreatureActivityRules.Code(_database.CreatureById(creature.CreatureTypeId),creature);
                if(activity<28) _filteredCounts[type*28+activity]++;
                _filteredCounts[type*28+(creature.Mood==CreatureMood.Angry?25:creature.Mood==CreatureMood.Unhappy?26:27)]++;
                if(activity!=5 && activity!=20 && activity!=21) _filteredCounts[type*28+23]++;
                if(activity!=4 && activity!=22) _filteredCounts[type*28+24]++;
                if(type==_worker)
                {
                    if(creature.WorkerIdle || creature.LastTaskKind==ImpTaskKind.Reinforce) idle++;
                    else
                    {
                        CreatureStateCategory category=CreatureStateTable.Get(creature.Dk2StateId).Category;
                        if(category==CreatureStateCategory.Movement) category=CreatureStateTable.Get(creature.UtilityNewState).Category;
                        if(category==CreatureStateCategory.Combat) fighting++;else busy++;
                    }
                }
            }
        }
        WorkerCounts=new(_counts[_worker],idle,busy,fighting);
        _entries.Clear();
        foreach(CreatureOverviewEntry entry in _all)
            if(entry.Type.Value==_worker || _owned[entry.Type.Value]!=0)
            { entry.Count=_counts[entry.Type.Value];_entries.Add(entry); }
    }
    public CreatureOverviewAction Select(WorldState world,CreatureOverviewType type,CreatureOverviewSelection selection,CreatureOverviewFilter filter=CreatureOverviewFilter.All)
    {
        if(!world.HasPlayer(_viewer)) return default;
        bool focus=selection==CreatureOverviewSelection.FocusNext;
        CreatureQueryTarget previous=focus?_lastFocus:_lastPickup[type.Value];
        CreatureState? first=null,after=null,extreme=null;
        bool passed=previous==CreatureQueryTarget.None;
        foreach(CreatureState creature in world.OwnedCreatures(_viewer))
        {
            bool eligible=ResolvedType(creature)==type.Value && creature.Dk2StateId!=193 && !Suppressed(creature)
                && MatchesFilter(_database.CreatureById(creature.CreatureTypeId),creature,filter)
                && (focus || HandRules.CanPickUp(world,_database,creature,_viewer));
            if(eligible)
            {
                first??=creature;
                if(passed) after??=creature;
                if(extreme==null || selection==CreatureOverviewSelection.PickupLowestLevel && creature.Level<extreme.Level
                    || selection==CreatureOverviewSelection.PickupHighestLevel && creature.Level>extreme.Level) extreme=creature;
            }
            if(creature.Id==previous.Value) passed=true;
        }
        CreatureState? chosen=selection is CreatureOverviewSelection.PickupLowestLevel or CreatureOverviewSelection.PickupHighestLevel
            ?extreme:after??first;
        if(chosen==null) return default;
        var target=new CreatureQueryTarget(chosen.Id);
        if(focus) _lastFocus=target;else _lastPickup[type.Value]=target;
        return new(focus?CreatureOverviewActionKind.Focus:CreatureOverviewActionKind.Pickup,target,
            new(new(chosen.PosX),new(chosen.PosY),new(chosen.PosZ)));
    }
    private static bool MatchesFilter(CreatureDef definition,CreatureState creature,CreatureOverviewFilter filter)
    {
        if(filter==CreatureOverviewFilter.All) return true;
        int activity=CreatureActivityRules.Code(definition,creature);
        return filter switch
        {
            CreatureOverviewFilter.Busy => activity!=4 && activity!=5,
            CreatureOverviewFilter.Training => activity==21 || creature.CombatPitFighter,
            CreatureOverviewFilter.OtherJobs => activity!=5 && activity!=20 && activity!=21,
            CreatureOverviewFilter.OtherFighting => activity!=4 && activity!=22,
            CreatureOverviewFilter.Angry => creature.Mood==CreatureMood.Angry,
            CreatureOverviewFilter.Unhappy => creature.Mood==CreatureMood.Unhappy,
            CreatureOverviewFilter.Content => creature.Mood==CreatureMood.Content,
            _ => activity==(int)filter,
        };
    }
    private bool Suppressed(CreatureState creature) => creature.InHand || _pending.ContainsKey(creature.Id);
    private byte ResolvedType(CreatureState creature)
    {
        CreatureDef definition=_database.CreatureById(creature.CreatureTypeId);
        return definition.Flags.HasFlag(CreatureFlags.IsUnique)?definition.UniqueBaseCreatureId:definition.CreatureId;
    }
}
