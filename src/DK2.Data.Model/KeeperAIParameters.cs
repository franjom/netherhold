// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>The tuning block of one computer keeper, as the original reads it out of a PLAYERS
/// record's 158-byte parameter block. <c>docs/KEEPER_AI.md</c> §2.2 has the offset-to-field map,
/// the consumers and ranges, and the nine parameters loaded and never read.
/// <para>Field names are the original's own, the retail executable's own debug-dump labels, so
/// Bullfrog's spellings (<c>TendancyMoney</c>, <c>Reseached</c>) are kept verbatim.</para>
/// <para><b>Read the way the game reads it, not the way the editor wrote it.</b>
/// <see cref="TilesPerImpRatio"/>, <see cref="SpecialRange"/> and
/// <see cref="WaitBeforeAttackingAgain"/> are 16-bit in the editor and a single byte to the game,
/// over garbage in the high byte, so two campaign keepers really do get <c>TilesPerImpRatio</c>
/// 232 (STRONGLY INFERRED from the loader). <see cref="EditorAiType"/> is the editor's
/// <c>aiType</c> at block offset 0, which the game ignores; bit-packed values are exposed
/// <i>unmasked</i>, the simulation's keeper applying the mask.</para></summary>
public sealed record KeeperAIParameters
{
    /// <summary>Block byte 0. The editor's personality dropdown; the game ignores it.</summary>
    public required byte EditorAiType { get; init; }

    /// <summary>Actions the keeper may take per tick — its whole speed knob. 1, 2 or 4 in the campaign.</summary>
    public required byte TendancySpeed { get; init; }

    /// <summary>Percent of a candidate room site's perimeter that may already be open.</summary>
    public required byte Openness { get; init; }

    public required byte RemoveCallToArmsOwnCreatures { get; init; }
    public required byte BuildLostRoomAfterMinimumTime { get; init; }

    public required bool CreateEmptyRoomShapeAreasWhenIdle { get; init; }
    public required bool AutomaticallyBuildBiggerLairWhenPortalTakenOver { get; init; }
    public required bool SellRecentlyTakenOverRoom { get; init; }

    public required byte MinimumRandomTimeBeforePlacingReseachedRoom { get; init; }

    /// <summary>Tiles per new room; the site finder uses ⌊√n⌋ as the side.</summary>
    public required byte DefaultRoomSize { get; init; }

    public required byte TilesLeftBetweenRooms { get; init; }
    public required bool PlaceRoomsNextToIdeal { get; init; }
    public required byte CorridorStyle { get; init; }

    /// <summary>0 expand the existing room, 1 build a new one, 2 do nothing.</summary>
    public required byte BuildNewRoomWhenMoreSpaceRequired { get; init; }

    public required byte NeutralRoomRangeFromHeart { get; init; }

    /// <summary>The build order: room ids tried in this sequence, then 16–26.</summary>
    public required IReadOnlyList<byte> RoomList { get; init; }

    public required byte Flexibility { get; init; }
    public required byte NeutralRoomRangeFromClaimedArea { get; init; }

    /// <summary>Ticks a Call to Arms stays up before the keeper removes it.</summary>
    public required ushort RemoveCallToArmsAfter { get; init; }

    public required bool BoulderTrapsOnLongCorridors { get; init; }
    public required bool BoulderTrapOnBreachRoute { get; init; }

    /// <summary>0 all traps aggressive … 4 all defensive.</summary>
    public required byte AggressiveVsDefensiveTrapPreference { get; init; }

    /// <summary>Compared against random(4): higher means more doors, fewer traps.</summary>
    public required byte TrapVsDoorPreference { get; init; }

    /// <summary>0 scan to the best door, 1 take the first, 2 fifty-fifty.</summary>
    public required byte DoorPreference { get; init; }

    /// <summary>Percent chance per tick that the trap-and-door step runs at all.</summary>
    public required byte TrapAndDoorUsage { get; init; }

    public required bool IAAllRoomsPlaced { get; init; }

    /// <summary>Raw byte; the game keeps only bit 0, so the campaign's 2 reads as 0.</summary>
    public required byte IAAllSpellsResearched { get; init; }

    public required bool IAOnlyAttackAttackers { get; init; }
    public required bool NeverAttack { get; init; }

    /// <summary>Creature level every non-imp must reach before the army is "ready".</summary>
    public required byte IACreatureSkillLevel { get; init; }

    /// <summary>Percent: attack only when our threat exceeds this much of the target's.</summary>
    public required byte IAThreatSuperiority { get; init; }

    /// <summary>First attack's end point: 0 enemy heart, 1 random room, 2 weakest room, 3 strongest.</summary>
    public required byte IAFirstEnd { get; init; }

    /// <summary>First attack's start: 0 own room furthest from heart, 1 random, 2 heart, 3 nearest to target. Raw; the game masks to 2 bits.</summary>
    public required byte IAFirstStart { get; init; }

    /// <summary>Stored tunnels per enemy; the game keeps 4 bits and 8 slots.</summary>
    public required byte BreachAtXPoints { get; init; }

    public required byte IAUseCreatures { get; init; }
    public required ushort ManaValue { get; init; }

    /// <summary>Enemy threat near a fight above which the keeper casts Call to Arms.</summary>
    public required ushort BACallToArmsValue { get; init; }

    public required byte RemoveCallToArmsEnemyCreatures { get; init; }
    public required byte RemoveCallToArmsWithinRange { get; init; }
    public required bool BARemoveIfCantReinforce { get; init; }

    /// <summary>Percent of enemy threat the keeper wants to match when reinforcing.</summary>
    public required byte BAThreatSuperiority { get; init; }

    public required byte AttackingVsDefensiveSpellPreference { get; init; }

    /// <summary>Percent chance, per enemy creature per combat message, of marking it for imprisonment.</summary>
    public required byte ImprisonPercentage { get; init; }

    /// <summary>Health percent below which a creature is pulled back to the Lair.</summary>
    public required byte BARetreatHealth { get; init; }

    public required ushort GoldValue { get; init; }

    public required bool UnhappyCreaturesHappy { get; init; }
    public required bool AngryCreaturesHappy { get; init; }
    public required bool AngryCreaturesDisposeOf { get; init; }
    public required bool UnwantedCreatureDisposeOf { get; init; }

    /// <summary>0 drop the angry creature at a portal, 1 in the Temple.</summary>
    public required byte AngryDisposalMethod { get; init; }

    public required byte MaximumImps { get; init; }

    /// <summary>
    /// The editor's six behaviour toggles (bit0 build, bit1 dig gold, bit2 research,
    /// bit3 respond, bit4 imps, bit5 slap). <b>Both of the game's loaders overwrite
    /// this with 0xFFFF</b>, so the shipped values (garbage in most records) never
    /// take effect. Kept for fidelity; do not honour it.
    /// </summary>
    public required byte Flags { get; init; }

    /// <summary>The army size (non-imps) required before the keeper considers attacking.</summary>
    public required byte IAMinimumCreatures { get; init; }

    public required bool LightningInWater { get; init; }

    /// <summary>0 never, 1 anywhere at 2 %/tick, 2 enemy land only at 20 %/tick.</summary>
    public required byte UseSightOfEvil { get; init; }

    /// <summary>Compared against random(4): lower means battle spells more often.</summary>
    public required byte UseKeeperSpells { get; init; }

    /// <summary>Higher prefers dearer spells.</summary>
    public required byte SpellPreference { get; init; }

    /// <summary>0 creep the CTA toward the enemy heart, 1/2 place it near the heart.</summary>
    public required byte CallToArmsUsage { get; init; }

    /// <summary>Gold the keeper wants in the treasury before it stops chasing gold.</summary>
    public required ushort TendancyMoney { get; init; }

    /// <summary>In units of 4 ticks. The game reads one byte of the editor's u16 (§6.5).</summary>
    public required byte WaitBeforeAttackingAgain { get; init; }

    /// <summary>Added to the player's mana at load. 100 in every shipped record.</summary>
    public required int StartingMana { get; init; }

    /// <summary>Never read by the game. One byte of the editor's u16.</summary>
    public required byte SpecialRange { get; init; }

    /// <summary>Owned tiles per imp. One byte of the editor's u16, hence 232 on two Level17 keepers.</summary>
    public required byte TilesPerImpRatio { get; init; }

    /// <summary>Tile rectangle the keeper may dig and build in: min x, min y, max x, max y.</summary>
    public required ushort MaxBuildAreaMinX { get; init; }
    public required ushort MaxBuildAreaMinY { get; init; }
    public required ushort MaxBuildAreaMaxX { get; init; }
    public required ushort MaxBuildAreaMaxY { get; init; }

    /// <summary>
    /// Misnamed by the original: compared against random(3) to gate the step that
    /// drops a non-researching creature at a portal when the dungeon is at its
    /// creature cap. Nothing is ever moved <i>to</i> the library.
    /// </summary>
    public required byte ProbabilityOfMovingCreatureForResearch { get; init; }

    public required byte SpecialSearchChance { get; init; }
    public required byte SpecialFindChance { get; init; }

    /// <summary>What to do with an enemy prisoner: 0 Torture Chamber, 1 nothing, 2 Combat Pit, 3 Temple.</summary>
    public required byte ImprisonedCreatureAction { get; init; }

    /// <summary>The 158 bytes as shipped, for anything this type does not name.</summary>
    public required ReadOnlyMemory<byte> RawBlock { get; init; }
}
