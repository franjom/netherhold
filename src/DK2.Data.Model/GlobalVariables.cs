namespace DK2.Data.Model;

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
/// DK2's global game-rule table, as decoded from VARIABLES(230) records.
///
/// This is the single source of every balance number in the simulation. A
/// gameplay constant hardcoded in simulation code is a bug — read it from here.
/// See docs/research/kwd-variables.md.
/// </summary>
public sealed class GlobalVariables
{
    public List<CreaturePoolEntry> CreaturePool { get; } = [];
    public List<AvailabilityEntry> Availabilities { get; } = [];
    public List<SacrificeEntry> Sacrifices { get; } = [];
    public List<CreatureStatEntry> CreatureStats { get; } = [];
    public Dictionary<VariableTypeId, MiscVariable> Misc { get; } = [];

    /// <summary>The value of a global rule variable. Throws if the data does not carry it.</summary>
    public int this[VariableTypeId id] => Misc.TryGetValue(id, out MiscVariable? v)
        ? v.Value
        : throw new KeyNotFoundException($"VARIABLES table carries no entry for {id} ({(int)id}).");

    public bool TryGet(VariableTypeId id, out int value)
    {
        if (Misc.TryGetValue(id, out MiscVariable? v)) { value = v.Value; return true; }
        value = 0;
        return false;
    }
}
