namespace DK2.Data.Model;

/// <summary>
/// The keeper spells' names as the shipped data spells them.
///
/// <para>These exist so that no rule contains a bare string literal, the same
/// way <see cref="RoomTypeId"/> exists so that no rule contains a bare terrain
/// id. They are the data file's internal ASCII names, not the localised display
/// text, and the record's own id field has not been identified yet — see the
/// note on <see cref="GameDatabase.KeeperSpells"/> and Q55.</para>
///
/// <para>Only the spells something reads are listed. The other nine arrive with
/// spells proper at M7.</para>
/// </summary>
public static class KeeperSpellName
{
    /// <summary>Buys an imp, and every imp alive charges its keeper upkeep.</summary>
    public const string CreateImp = "Create Imp";

    /// <summary>Summons a Horned Reaper, the most expensive thing a keeper can keep.</summary>
    public const string SummonHorny = "Summon Horny";

    /// <summary>Take a creature over in first person.</summary>
    public const string Possession = "Possession";
}
