// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using static DK2.Tools.Commands.CommandHelpers;

namespace DK2.Tools.Commands;

internal static partial class KwdCommands
{
    private static readonly IReadOnlyDictionary<string, Func<string[], int>> Subcommands =
        new Dictionary<string, Func<string[], int>>(StringComparer.Ordinal)
        {
            ["chunks"] = args => args.Length >= 3 ? RunChunks(args) : Program.Usage(),
            ["portals"] = args => args.Length >= 2 ? RunPortals(args) : Program.Usage(),
            ["alterterrain"] = args => args.Length >= 2 ? RunAlterTerrain(args) : Program.Usage(),
            ["tagged"] = args => args.Length >= 3 ? RunTagged(args) : Program.Usage(),
            ["records"] = args => args.Length >= 3 ? RunRecords(args) : Program.Usage(),
            ["things"] = args => args.Length >= 2 ? RunThings(args) : Program.Usage(),
            ["rewrite"] = args => args.Length >= 2 ? RunRewrite(args) : Program.Usage(),
            ["corpus"] = args => args.Length == 2 ? RunCorpus(args) : Program.Usage(),
            ["trigger-types"] = args => args.Length == 2 ? RunTriggerTypes(args) : Program.Usage(),
            ["oracle"] = args => args.Length is 2 or 3 ? RunOracle(args) : Program.Usage(),
            ["parties"] = args => args.Length == 2 ? RunParties(args) : Program.Usage(),
            ["unreached"] = args => args.Length is 2 or 3 ? RunUnreached(args) : Program.Usage(),
            ["roots"] = args => args.Length == 2 ? RunRoots(args) : Program.Usage(),
            ["triggers"] = args => args.Length >= 3 ? RunTriggers(args) : Program.Usage(),
            ["trigger-params"] = args => args.Length >= 4 ? RunTriggerParams(args) : Program.Usage(),
            ["terrain"] = args => args.Length >= 2 ? RunTerrain(args) : Program.Usage(),
            ["terrainmana"] = args => args.Length >= 2 ? RunTerrainMana(args) : Program.Usage(),
            ["terrainflags"] = args => args.Length >= 2 ? RunTerrainFlags(args) : Program.Usage(),
            ["roomflags"] = args => args.Length >= 2 ? RunRoomFlags(args) : Program.Usage(),
            ["rooms"] = args => args.Length >= 2 ? RunRooms(args) : Program.Usage(),
            ["mapbytes"] = args => args.Length >= 3 ? RunMapBytes(args) : Program.Usage(),
            ["map"] = args => args.Length >= 3 ? RunMap(args) : Program.Usage(),
            ["creature"] = args => args.Length >= 3 ? RunCreature(args) : Program.Usage(),
            ["research"] = args => args.Length >= 2 ? RunResearch(args) : Program.Usage(),
            ["creatures"] = args => args.Length >= 2 ? RunCreatures(args) : Program.Usage(),
            ["recscan"] = args => args.Length >= 4 ? RunRecScan(args) : Program.Usage(),
            ["creaturehex"] = args => args.Length >= 4 ? RunCreatureHex(args) : Program.Usage(),
            ["objecthex"] = args => args.Length >= 4 ? RunObjectHex(args) : Program.Usage(),
            ["creaturescan"] = args => args.Length >= 4 ? RunCreatureScan(args) : Program.Usage(),
            ["fieldsweep"] = args => args.Length >= 2 ? RunFieldSweep(args) : Program.Usage(),
            ["creaturebits"] = args => args.Length >= 3 ? RunCreatureBits(args) : Program.Usage(),
            ["objects"] = args => args.Length >= 2 ? RunObjects(args) : Program.Usage(),
            ["varsweep"] = args => args.Length >= 2 ? RunVarSweep(args) : Program.Usage(),
            ["vars"] = args => args.Length >= 2 ? RunVars(args) : Program.Usage(),
        };

    internal static int Run(string[] args) =>
        args.Length >= 2 && Subcommands.TryGetValue(args[1], out Func<string[], int>? run)
            ? run(args)
            : Program.Usage();

    internal static int RunChunks(string[] args)
    {
        foreach (KwdChunk c in KwdReader.ReadFile(ResolveEditor(args[2])))
        {
            // The stride is the structural check every record layout has to pass:
            // a field list that does not sum to it is wrong. Prints "varies" for
            // the two tagged types, whose records carry their own size instead.
            string stride =
                c.ItemCount <= 0 ? "n/a"
                : KwdTaggedRecords.IsTagged(c.Type) ? "varies (tagged records)"
                : c.Content.Length % c.ItemCount == 0
                    ? (c.Content.Length / c.ItemCount).ToString()
                    : $"NOT A WHOLE NUMBER ({c.Content.Length}/{c.ItemCount})";

            Console.WriteLine(
                $"{c.Type,-16} items={c.ItemCount,-6} content={c.Content.Length,-8} stride={stride}");
        }
        return 0;
    }

    internal static int RunTagged(string[] args)
    {
        // Splits THINGS/TRIGGERS into their self-describing records and reports
        // what tags the file carries. See docs/research/kwd-container.md.
        foreach (KwdChunk c in KwdReader.ReadFile(ResolveEditor(args[2])))
        {
            if (!KwdTaggedRecords.IsTagged(c.Type)) continue;

            var byTag = new SortedDictionary<int, (int Count, SortedSet<int> Sizes)>();
            foreach (KwdTaggedRecords.TaggedRecord r in KwdTaggedRecords.Split(c))
            {
                if (!byTag.TryGetValue(r.Tag, out (int Count, SortedSet<int> Sizes) e))
                    byTag[r.Tag] = e = (0, []);
                e.Sizes.Add(r.Payload.Length);
                byTag[r.Tag] = (e.Count + 1, e.Sizes);
            }

            Console.WriteLine($"{c.Type} — {c.ItemCount} records, {c.Content.Length} bytes, all accounted for");
            foreach ((int tag, (int count, SortedSet<int> sizes)) in byTag)
            {
                string name = c.Type == KwdRecordType.Things
                    ? KwdTaggedRecords.ThingPayloadSizes.TryGetValue(tag, out var t) ? t.Name : "?"
                    : KwdTaggedRecords.TriggerPayloadSizes.TryGetValue(tag, out var g) ? g.Name : "?";
                Console.WriteLine($"  tag {tag,-4} {name,-16} {count,5} records   payload={string.Join(",", sizes)}");
            }
        }
        return 0;
    }

    internal static int RunRecords(string[] args)
    {
        // Re-checkable field-level counterpart to `kwd chunks` and `kwd tagged`.
        // It deliberately prints ids/types and consumed lengths rather than a
        // wall of presentation data; ToBytes() is covered byte-for-byte by the
        // round-trip tests.
        List<KwdChunk> chunks = KwdReader.ReadFile(ResolveEditor(args[2]));
        foreach (KwdChunk chunk in chunks)
        {
            switch (chunk.Type)
            {
                case KwdRecordType.CreatureSpells:
                    foreach (CreatureSpellDef record in KwdAdditionalRecords.ParseCreatureSpells([chunk]))
                        Console.WriteLine($"CREATURE_SPELL id={record.CreatureSpellId,3} name={record.Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Shots:
                    foreach (ShotDef record in KwdAdditionalRecords.ParseShots([chunk]))
                        Console.WriteLine($"SHOT id={record.ShotId,3} name={record.Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Effects:
                    foreach (EffectDef record in KwdAdditionalRecords.ParseEffects([chunk]))
                        Console.WriteLine($"EFFECT id={record.EffectId,3} name={record.Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.EffectElements:
                    foreach (EffectElementDef record in KwdAdditionalRecords.ParseEffectElements([chunk]))
                        Console.WriteLine($"EFFECT_ELEMENT id={record.EffectElementId,3} name={record.Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Things:
                    foreach (ThingRecord record in KwdThings.Parse([chunk]))
                        Console.WriteLine($"THING tag={record.Tag} type={record.GetType().Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Triggers:
                    foreach (TriggerRecord record in KwdTriggers.Parse([chunk]))
                        Console.WriteLine($"TRIGGER tag={record.Tag} id={record.TriggerId,4} type={record.Type,2} repeat={record.RepeatCount,3} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Level:
                    LevelDef level = KwdLevel.Parse([chunk])
                        ?? throw new InvalidDataException("LEVEL chunk did not produce a level.");
                    Console.WriteLine($"LEVEL name={level.Name} ticksPerSecond={level.TicksPerSecond} paths={level.Paths.Count} bytes={level.ConsumedLength}");
                    foreach (KwdLevelFilePath path in level.Paths)
                        Console.WriteLine($"  {path.RecordType,-16} {path.Path}");
                    break;
                default:
                    Console.WriteLine($"{chunk.Type}: use its existing typed dump; {chunk.ItemCount} records");
                    break;
            }
        }
        return 0;
    }

    internal static int RunRewrite(string[] args) =>
        KwdRewriteCommand.Run(args[2..],Console.Out,Console.Error);

    internal static int RunCorpus(string[] args)
    {
        KwdCorpusAudit.Result audit = KwdCorpusAudit.Run(Dk2Install.Require());
        Console.WriteLine($"{audit.Files} KWD/KLD files; {audit.ParsedTypes.Count}/18 record types parsed");
        foreach (KwdRecordType type in Enum.GetValues<KwdRecordType>())
        {
            audit.RecordCounts.TryGetValue(type, out int count);
            Console.WriteLine($"  {type,-16} {count,7} records  {(audit.ParsedTypes.Contains(type) ? "parsed" : "MISSING")}");
        }
        return audit.ParsedTypes.Count == 18 ? 0 : 1;
    }
}
