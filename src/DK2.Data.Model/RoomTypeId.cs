namespace DK2.Data.Model;

/// <summary>
/// DK2's room type identifiers.
/// CONFIRMED: Flame `src/dk2/room_type.h` (derived from DKII.EXE v1.70).
/// Gaps (2, 7, 18-20, 22, 23) exist in the original numbering and are preserved.
/// </summary>
public enum RoomTypeId : byte
{
    Treasury = 1,
    Entrance = 3,
    Hatchery = 4,
    DungeonHeart = 5,
    Library = 6,
    WoodenBridge = 8,
    GuardPost = 9,
    Workshop = 10,
    Prison = 11,
    TortureRoom = 12,
    Temple = 13,
    Graveyard = 14,
    Casino = 15,
    CombatPit = 16,
    StoneBridge = 17,
    HeroGateFrontEnd = 21,
    MercenaryGate = 24,
    HeroPortal = 25,
    Crypt = 26,
}
