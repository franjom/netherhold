// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
namespace DK2.Presentation;

public readonly record struct CreatureQueryTarget(int Value)
{
    public static CreatureQueryTarget None => new(0);
}
public readonly record struct CreatureQuerySnapshot(CreatureQueryTarget Target,string Text,FpPosition3 Position)
{
    public static CreatureQuerySnapshot Empty => new(CreatureQueryTarget.None,"",default);
}
[Dk2Behavior(Confidence.Confirmed,"docs/research/minimap-controls.md")]
public static class ClassicCreatureQueryRules
{
    public static Dk2Angle FollowHeading(Dk2Angle yaw) => new((ushort)(yaw.Value % 64));
}

[Dk2Behavior(Confidence.Confirmed,"docs/research/minimap-controls.md")]
public sealed class ClassicCreatureQuery
{
    private readonly GameDatabase _database;
    private readonly Func<uint,string> _resolve;
    private readonly PlayerId _viewer;
    private readonly Dictionary<int,string> _tokens = new();
    private readonly record struct Facts(CreatureQueryTarget Target,PlayerId Owner,byte Type,int Level,int Gold,string Name,string Blood);
    private Facts? _previous;
    private CreatureQuerySnapshot _snapshot=CreatureQuerySnapshot.Empty;
    public ClassicCreatureQuery(GameDatabase database,Func<uint,string> resolveString,PlayerId viewingPlayer)
    { _database=database; _resolve=resolveString; _viewer=viewingPlayer; }
    public CreatureQuerySnapshot Capture(WorldState world,CreatureQueryTarget target,string resolvedName,string resolvedBlood)
    {
        CreatureState? creature=world.CreatureById(target.Value);
        if (target==CreatureQueryTarget.None || creature==null || !CreatureStateMachine.HasState(creature))
        { _previous=null; return _snapshot=CreatureQuerySnapshot.Empty; }
        var position=new FpPosition3(new(creature.PosX),new(creature.PosY),new(creature.PosZ));
        var facts=new Facts(target,creature.OwnerPlayerId,creature.CreatureTypeId,creature.Level,creature.GoldHeld,resolvedName,resolvedBlood);
        if (_previous==facts)
            return _snapshot=new(target,_snapshot.Text,position);
        _previous=facts;
        CreatureDef definition=_database.CreatureById(creature.CreatureTypeId);
        _tokens.Clear(); _tokens[29]=resolvedName; _tokens[30]=_resolve(definition.NameStringId); _tokens[34]=resolvedBlood;
        _tokens[35]=PaydayRules.Wage(_database,definition,creature.Level).ToString(CultureInfo.InvariantCulture);
        _tokens[36]=creature.GoldHeld.ToString(CultureInfo.InvariantCulture);
        uint template=creature.OwnerPlayerId==PlayerIds.Good?0x96Cu:creature.OwnerPlayerId==PlayerIds.Neutral?0x96Fu
            :creature.OwnerPlayerId!=_viewer?0:definition.Flags.HasFlag(CreatureFlags.IsHorny)?0xAE9u:0x969u;
        string text=template==0?"":WorldHoverText.Substitute(_resolve(template),_tokens);
        return _snapshot=new(target,text,position);
    }
}
