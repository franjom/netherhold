namespace DK2.Data.Model;

/// <summary>
/// DK2's entity ("thing") categories.
/// CONFIRMED: Flame `src/dk2/entities/entities_type.h`.
/// </summary>
public enum ThingTypeId : byte
{
    Creature = 0,
    Shot = 1,
    Object = 2,
    Trap = 3,
    Door = 4,
    ActionPoint = 5,
    DeadBody = 6,
    EffectGenerator = 7,
}
