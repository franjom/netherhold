// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Shared typed decoding and overlay dispatch for the editor and rewrite audit.</summary>
public static class KwdTypedRecords
{
    public static IReadOnlyList<object> Read(KwdChunk chunk) => chunk.Type switch
    {
        KwdRecordType.Terrain => KwdTerrainRooms.ParseTerrain([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Rooms => KwdTerrainRooms.ParseRooms([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Creatures => KwdCreatures.Parse([chunk]).Cast<object>().ToArray(),
        KwdRecordType.KeeperSpells => KwdKeeperSpells.Parse([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Objects => KwdObjects.Parse([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Doors => KwdDoors.Parse([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Traps => KwdTraps.Parse([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Players => KwdPlayers.Parse([chunk]).Cast<object>().ToArray(),
        KwdRecordType.CreatureSpells => KwdAdditionalRecords.ParseCreatureSpells([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Shots => KwdAdditionalRecords.ParseShots([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Effects => KwdAdditionalRecords.ParseEffects([chunk]).Cast<object>().ToArray(),
        KwdRecordType.EffectElements => KwdAdditionalRecords.ParseEffectElements([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Things => KwdThings.Parse([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Triggers => KwdTriggers.Parse([chunk]).Cast<object>().ToArray(),
        KwdRecordType.Level => [KwdLevel.Parse([chunk])!],
        KwdRecordType.Variables => KwdRecordRoundTrip.Split(chunk).Select(i=>(object)KwdVariables.ParseRecord(i.ToBytes())).ToArray(),
        KwdRecordType.Map => MapGrid.Parse([chunk])!.Tiles.Cast<object>().ToArray(),
        _ => throw new InvalidDataException($"No established typed record layout for {chunk.Type}."),
    };

    public static byte[] Write(object row, KwdRecordImage template, KwdChunk chunk) => row switch
    {
        TerrainDef r => KwdTerrainRooms.WriteTerrainRecord(r,template),
        RoomDef r => KwdTerrainRooms.WriteRoomRecord(r,template),
        CreatureDef r => KwdCreatures.WriteRecord(r,template),
        KeeperSpellDef r => KwdKeeperSpells.WriteRecord(r,template),
        ObjectDef r => KwdObjects.WriteRecord(r,template),
        DoorDef r => KwdDoors.WriteRecord(r,template),
        TrapDef r => KwdTraps.WriteRecord(r,template),
        PlayerDef r => KwdPlayers.WriteRecord(r,template),
        CreatureSpellDef r => KwdAdditionalRecords.WriteCreatureSpellRecord(r,template),
        ShotDef r => KwdAdditionalRecords.WriteShotRecord(r,template),
        EffectDef r => KwdAdditionalRecords.WriteEffectRecord(r,template),
        EffectElementDef r => KwdAdditionalRecords.WriteEffectElementRecord(r,template),
        ThingRecord r => KwdThings.WriteRecord(r,template),
        TriggerRecord r => KwdTriggers.WriteRecord(r,template),
        LevelDef r when template.Type==KwdRecordType.Level && template.Tag is null =>
            KwdLevel.WriteRecord(r,chunk with { Content=template.ToBytes() }),
        KwdVariableEntry r => KwdVariables.WriteRecord(r,template),
        MapTile r => MapGrid.WriteTile(r,template),
        _ => throw new InvalidDataException("Typed record does not match an established overlay writer."),
    };

    public static KwdChunk Rewrite(KwdChunk chunk)
    {
        IReadOnlyList<object> rows=Read(chunk); var images=KwdRecordRoundTrip.Split(chunk);
        if(rows.Count!=images.Count) throw new InvalidDataException($"{chunk.Type}: typed projection and image counts differ.");
        KwdRecordImage[] rewritten=rows.Select((row,i)=>
            new KwdRecordImage(chunk.Type,Write(row,images[i],chunk),images[i].Tag)).ToArray();
        return chunk with { Content=KwdRecordRoundTrip.Join(chunk,rewritten) };
    }
}
