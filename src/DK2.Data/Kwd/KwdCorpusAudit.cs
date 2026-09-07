// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Re-runs the M1 parser inventory over the shipped editor corpus. This is
/// shared by <c>dk2tools kwd corpus</c> and the install-gated regression test so
/// the status claim and the assertion cannot drift apart.
/// </summary>
public static class KwdCorpusAudit
{
    public sealed record Result(
        IReadOnlyCollection<KwdRecordType> ParsedTypes,
        IReadOnlyDictionary<KwdRecordType, int> RecordCounts,
        int Files);

    public static Result Run(string installRoot)
    {
        string editor = Path.Combine(installRoot, "Data", "editor");
        string maps = Path.Combine(editor, "maps");
        var files = Directory.GetFiles(editor, "*.kwd")
            .Concat(Directory.GetFiles(maps, "*.kwd"))
            .Concat(Directory.GetFiles(maps, "*.kld"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // GLOBALS is a LEVEL file-reference discriminator, not a standalone
        // chunk in the v1.70 corpus. KwdLevel parses that row shape and a
        // synthetic test exercises type 0; the shipped count is honestly zero.
        var types = new HashSet<KwdRecordType> { KwdRecordType.Globals };
        var counts = new Dictionary<KwdRecordType, int> { [KwdRecordType.Globals] = 0 };

        void Add(KwdRecordType type, int count)
        {
            types.Add(type);
            counts.TryGetValue(type, out int old);
            counts[type] = old + count;
        }

        foreach (string file in files)
        {
            foreach (KwdChunk chunk in KwdReader.ReadFile(file))
            {
                switch (chunk.Type)
                {
                    case KwdRecordType.Terrain: Add(chunk.Type, KwdTerrainRooms.ParseTerrain([chunk]).Count); break;
                    case KwdRecordType.Rooms: Add(chunk.Type, KwdTerrainRooms.ParseRooms([chunk]).Count); break;
                    case KwdRecordType.Creatures: Add(chunk.Type, KwdCreatures.Parse([chunk]).Count); break;
                    case KwdRecordType.Variables: _ = KwdVariables.Parse([chunk]); Add(chunk.Type, chunk.ItemCount); break;
                    case KwdRecordType.KeeperSpells: Add(chunk.Type, KwdKeeperSpells.Parse([chunk]).Count); break;
                    case KwdRecordType.Objects: Add(chunk.Type, KwdObjects.Parse([chunk]).Count); break;
                    case KwdRecordType.Doors: Add(chunk.Type, KwdDoors.Parse([chunk]).Count); break;
                    case KwdRecordType.Traps: Add(chunk.Type, KwdTraps.Parse([chunk]).Count); break;
                    case KwdRecordType.Map: _ = MapGrid.Parse([chunk]); Add(chunk.Type, chunk.Content.Length / 4); break;
                    case KwdRecordType.Players: Add(chunk.Type, KwdPlayers.Parse([chunk]).Count); break;
                    case KwdRecordType.CreatureSpells: AuditFixed(chunk, KwdAdditionalRecords.ParseCreatureSpells([chunk])); Add(chunk.Type, chunk.ItemCount); break;
                    case KwdRecordType.Shots: AuditFixed(chunk, KwdAdditionalRecords.ParseShots([chunk])); Add(chunk.Type, chunk.ItemCount); break;
                    case KwdRecordType.Effects: AuditFixed(chunk, KwdAdditionalRecords.ParseEffects([chunk])); Add(chunk.Type, chunk.ItemCount); break;
                    case KwdRecordType.EffectElements: AuditFixed(chunk, KwdAdditionalRecords.ParseEffectElements([chunk])); Add(chunk.Type, chunk.ItemCount); break;
                    case KwdRecordType.Things:
                        AuditTagged(chunk, KwdThings.Parse([chunk])); Add(chunk.Type, chunk.ItemCount); break;
                    case KwdRecordType.Triggers:
                        AuditTagged(chunk, KwdTriggers.Parse([chunk])); Add(chunk.Type, chunk.ItemCount); break;
                    case KwdRecordType.Level:
                        LevelDef level = KwdLevel.Parse([chunk]) ?? throw new InvalidDataException($"{file}: LEVEL did not parse.");
                        if (!level.ToBytes().SequenceEqual(chunk.Content)) throw new InvalidDataException($"{file}: LEVEL did not round-trip.");
                        Add(chunk.Type, 1);
                        int globals = level.Paths.Count(p => p.RecordType == KwdRecordType.Globals);
                        if (globals > 0) Add(KwdRecordType.Globals, globals);
                        break;
                    default: throw new InvalidDataException($"{file}: no parser for {chunk.Type}.");
                }
            }
        }

        return new Result(types, counts, files.Count);
    }

    private static void AuditFixed<T>(KwdChunk chunk, IReadOnlyList<T> records)
        where T : KwdRoundTripRecord
    {
        if (records.Count != chunk.ItemCount) throw new InvalidDataException($"{chunk.Type}: item count mismatch.");
        byte[] joined = records.SelectMany(r => r.ToBytes()).ToArray();
        if (!joined.SequenceEqual(chunk.Content)) throw new InvalidDataException($"{chunk.Type}: records did not round-trip.");
    }

    private static void AuditTagged<T>(KwdChunk chunk, IReadOnlyList<T> records)
        where T : KwdRoundTripRecord
    {
        List<KwdTaggedRecords.TaggedRecord> tagged = KwdTaggedRecords.Split(chunk);
        if (records.Count != tagged.Count) throw new InvalidDataException($"{chunk.Type}: item count mismatch.");
        for (int i = 0; i < records.Count; i++)
            if (!records[i].ToBytes().SequenceEqual(tagged[i].Payload.ToArray()))
                throw new InvalidDataException($"{chunk.Type}: record {i} did not round-trip.");
    }
}
