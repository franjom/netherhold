using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>Creature availability pool entry (variable id 1).</summary>
public sealed record CreaturePoolEntry(int CreatureId, int Value, int PlayerId);

/// <summary>Room/creature/trap/door/spell availability entry (variable id 2).</summary>
public sealed record AvailabilityEntry(ushort Kind, ushort PlayerId, int TypeId, int Value);

/// <summary>Temple sacrifice recipe (variable id 43).</summary>
public sealed record SacrificeEntry(
    byte Type1, byte Id1, byte Type2, byte Id2, byte Type3, byte Id3,
    byte RewardType, byte SpeechId, int RewardValue);

/// <summary>Per-creature, per-level stat override (variable ids 65 / 224).</summary>
public sealed record CreatureStatEntry(int StatId, int Value, int Level, bool FirstPerson);

/// <summary>Plain global rule value — the vast majority of VARIABLES records.</summary>
public sealed record MiscVariable(VariableTypeId Id, int Value, int Unk1, int Unk2);

/// <summary>
/// Parsed contents of VARIABLES(230) chunks — DK2's global game-rule table.
/// Record layout: 16 bytes each — u32 variable id + a 12-byte payload whose
/// shape depends on the id (CONFIRMED: 621 records * 16 bytes == contentSize
/// of the reference GlobalVariables.kwd; docs/research/kwd-variables.md).
/// </summary>
public sealed class KwdVariables
{
    public List<CreaturePoolEntry> CreaturePool { get; } = [];
    public List<AvailabilityEntry> Availabilities { get; } = [];
    public List<SacrificeEntry> Sacrifices { get; } = [];
    public List<CreatureStatEntry> CreatureStats { get; } = [];
    public Dictionary<VariableTypeId, MiscVariable> Misc { get; } = [];

    public int this[VariableTypeId id] => Misc[id].Value;

    public static KwdVariables Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new KwdVariables();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Variables) continue;

            ReadOnlySpan<byte> data = chunk.Content;
            if (data.Length != chunk.ItemCount * 16)
                throw new InvalidDataException(
                    $"VARIABLES content is {data.Length} bytes for {chunk.ItemCount} records (expected {chunk.ItemCount * 16}).");

            for (int i = 0; i < chunk.ItemCount; i++)
            {
                ReadOnlySpan<byte> r = data.Slice(i * 16, 16);
                var id = (VariableTypeId)BinaryPrimitives.ReadInt32LittleEndian(r);
                ReadOnlySpan<byte> p = r[4..];

                switch (id)
                {
                    case VariableTypeId.CreaturePool:
                        result.CreaturePool.Add(new CreaturePoolEntry(
                            BinaryPrimitives.ReadInt32LittleEndian(p),
                            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..])));
                        break;

                    case VariableTypeId.Availability:
                        result.Availabilities.Add(new AvailabilityEntry(
                            BinaryPrimitives.ReadUInt16LittleEndian(p),
                            BinaryPrimitives.ReadUInt16LittleEndian(p[2..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..])));
                        break;

                    case VariableTypeId.SacrificesId:
                        result.Sacrifices.Add(new SacrificeEntry(
                            p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7],
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..])));
                        break;

                    case VariableTypeId.CreatureStatsId:
                    case VariableTypeId.CreatureFirstPersonId:
                        result.CreatureStats.Add(new CreatureStatEntry(
                            BinaryPrimitives.ReadInt32LittleEndian(p),
                            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..]),
                            FirstPerson: id == VariableTypeId.CreatureFirstPersonId));
                        break;

                    default:
                        // Plain value. Last occurrence wins (files can carry repeats).
                        result.Misc[id] = new MiscVariable(
                            id,
                            BinaryPrimitives.ReadInt32LittleEndian(p),
                            BinaryPrimitives.ReadInt32LittleEndian(p[4..]),
                            BinaryPrimitives.ReadInt32LittleEndian(p[8..]));
                        break;
                }
            }
        }
        return result;
    }
}
