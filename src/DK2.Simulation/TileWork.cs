// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// One kind of work a worker can apply to a tile. Each names one of the tile-work
/// health-delta variables in DK2's balance table (ids 4–16 of <see cref="VariableTypeId"/>,
/// mapped by <see cref="TileWorkRules.Variable"/>); what DK2 has no variable for is
/// not here. Members' values are not variable ids, and 6 and 9 are vacant because
/// <c>RepairTile</c> and <c>FillIn</c> have no reader in the binary (Q219).
/// </summary>
public enum TileWorkKind
{
    /// <summary>Dig out solid, diggable, unowned rock. <c>DigRockHealth</c>.</summary>
    Dig = 0,

    /// <summary>Mine a gold seam or gem face. <c>MineGoldHealth</c>.</summary>
    MineGold = 1,

    /// <summary>Dig through a reinforced wall belonging to the digger. <c>DigOwnWallHealth</c>.</summary>
    DigOwnWall = 2,

    /// <summary>Dig through another player's reinforced wall. <c>DigEnemyWallHealth</c>.</summary>
    DigEnemyWall = 3,

    /// <summary>Claim unowned floor. <c>ClaimTileHealth</c>.</summary>
    Claim = 4,

    /// <summary>Damage another player's claimed floor. <c>AttackTileHealth</c>.</summary>
    AttackTile = 5,

    /// <summary>Turn rock into a reinforced wall. <c>ReinforceWallHealth</c>.</summary>
    Reinforce = 7,

    /// <summary>Restore a damaged reinforced wall. <c>RepairWallHealth</c>.</summary>
    RepairWall = 8,

    /// <summary>
    /// Take a room that belongs to somebody else — a rival's or a neutral one: action 10
    /// (<c>0x00497BF0</c>). <c>ConvertRoomHealth</c> (−4000), or <c>ConvertNeutralRoomHealth</c>
    /// (−20000) on a neutral tile, against the <b>room's</b> health, never the tile's.
    /// <para>Room-scoped: one tile is worked and the whole room changes hands when its health
    /// falls below 1 — CONFIRMED (owner, 2026-09-04) "rooms are instantly claimed by imps, not tile
    /// by tile", and in the captures. <see cref="Systems.TileWorkSystem"/> applies it through
    /// <see cref="RoomHealthRules"/>, not <see cref="TileWorkRules.Apply"/> (Q92, ADR-0336).</para>
    /// </summary>
    ConvertRoom = 10,

    /// <summary>
    /// Knock down a tile of somebody's room: action 11 (<c>0x00497DF0</c>), <c>AttackRoomHealth</c>
    /// (−1500) against the <b>tile's</b> health, which a hero applies ten times over with no rate.
    /// The tile falls to its destroyed terrain as if sold with no refund; the room goes tile by tile
    /// (room-health.md §4, ADR-0342). Started by heroes, never by an imp.
    /// </summary>
    AttackRoom = 11,

    /// <summary>
    /// Restore one's own room: action 12 (<c>0x00498120</c>), <c>RepairRoomHealth</c> (+20000) into
    /// the room's health while it is below MaxHealth × Size. Nothing in v1.70 starts action 12, so
    /// nothing here queues it (room-health.md §6, ADR-0342).
    /// </summary>
    RepairRoom = 12,
}

/// <summary>What one application of work did to a tile.</summary>
public enum TileWorkOutcome
{
    /// <summary>The work does not apply to this tile; nothing changed.</summary>
    Rejected = 0,

    /// <summary>Health changed; the tile is still the same terrain type.</summary>
    Progressed = 1,

    /// <summary>Health reached zero; the tile became its destroyed type.</summary>
    Destroyed = 2,

    /// <summary>Health reached maximum under upward work; the tile became its max-health type.</summary>
    Transformed = 3,

    /// <summary>The work applies but changed nothing (already at the bound it was heading for).</summary>
    NoChange = 4,
}

/// <summary>The result of one work application.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="HealthDelta">Health actually applied, after clamping.</param>
/// <param name="GoldReleased">Gold freed from the rock, credited to nobody here —
/// <see cref="Systems.TileWorkSystem"/> hands it to the order's creature or reports it uncollected (Q31).</param>
/// <param name="GoldSpilled">Gold the tile can no longer keep, also credited to nobody:
/// a destroyed DK2 treasury leaves it on the floor as piles (<see cref="FloorGold"/>).</param>
/// <param name="NewTerrainId">The tile's terrain type after the application.</param>
public readonly record struct TileWorkResult(
    TileWorkOutcome Outcome,
    int HealthDelta,
    int GoldReleased,
    int GoldSpilled,
    byte NewTerrainId)
{
}

public readonly record struct TileHealthDamage(int Amount);

/// <summary>
/// A queued unit of work: "player P applies one application of kind K to tile I",
/// consumed by <see cref="Systems.TileWorkSystem"/> in queue order.
/// </summary>
/// <param name="WorkerCreatureId">The creature that swung, or -1 when nothing
/// did: in DK2 the imp that freed the gold is the imp that carries it away
/// (Q31). Work with no worker has its gold reported and dropped.</param>
public readonly record struct TileWorkOrder(
    int TileIndex, TileWorkKind Kind, byte PlayerId, int WorkerCreatureId = -1)
{
    public void HashInto(ref Hash64 hash)
    {
        hash.Add(TileIndex);
        hash.Add((int)Kind);
        hash.Add(PlayerId);
        hash.Add(WorkerCreatureId);
    }
}

/// <summary>
/// The tile-work rules: how much health an application moves, and what a tile
/// becomes when its health reaches a bound. Pure and static — no world, no RNG,
/// no time; numbers read from <see cref="GameDatabase"/>, never written.
/// Evidence and confidence per rule: docs/research/tile-work.md.
/// </summary>
public static class TileWorkRules
{
    /// <summary>Health damage without mining yield, using the shared terrain conversion; the caller checks eligibility.</summary>
    public static TileWorkResult ApplyDamage(GameDatabase database, ref TileState tile, TileHealthDamage damage)
    {
        int applied = -Math.Min(damage.Amount, tile.Health);
        tile.Health += applied;
        if (tile.Health > 0)
            return new TileWorkResult(applied == 0 ? TileWorkOutcome.NoChange : TileWorkOutcome.Progressed,
                applied, 0, 0, tile.TerrainId);
        int spilled = Convert(database, ref tile, BridgeRules.RevertsTo(database, tile),
            tile.OwnerPlayerId, takeOwnership: false);
        return new TileWorkResult(TileWorkOutcome.Destroyed, applied, 0, spilled, tile.TerrainId);
    }

    /// <summary>The global variable each kind draws its per-application health
    /// delta from. CONFIRMED — these are DK2's own variable names.</summary>
    public static VariableTypeId Variable(TileWorkKind kind) => kind switch
    {
        TileWorkKind.Dig => VariableTypeId.DigRockHealth,
        TileWorkKind.MineGold => VariableTypeId.MineGoldHealth,
        TileWorkKind.DigOwnWall => VariableTypeId.DigOwnWallHealth,
        TileWorkKind.DigEnemyWall => VariableTypeId.DigEnemyWallHealth,
        TileWorkKind.Claim => VariableTypeId.ClaimTileHealth,
        TileWorkKind.AttackTile => VariableTypeId.AttackTileHealth,
        TileWorkKind.Reinforce => VariableTypeId.ReinforceWallHealth,
        TileWorkKind.RepairWall => VariableTypeId.RepairWallHealth,
        TileWorkKind.ConvertRoom => VariableTypeId.ConvertRoomHealth,
        TileWorkKind.AttackRoom => VariableTypeId.AttackRoomHealth,
        TileWorkKind.RepairRoom => VariableTypeId.RepairRoomHealth,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown tile work kind."),
    };

    /// <summary>
    /// The ceiling DK2's tile applier clamps a tile's health to: <b>32000</b>.
    /// <para><c>FUN_00452f60</c>, <c>CMP EBX,0x7d00</c> — the engine's own guard,
    /// not a type limit (health is a 16-bit word). Nothing shipped reaches it:
    /// the largest terrain maximum is a room's 10000.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/tile-work.md")]
    public const int MaxTileHealth = 32000;

    /// <summary>The health one application of <paramref name="kind"/> moves at
    /// <paramref name="efficiency"/> 4096ths of work rate: the tabled per-second
    /// delta shifted down by <see cref="SimulationTiming.WorkApplicationShift"/>,
    /// carrying nothing (ADR-0133). Not the cadence, which is
    /// <see cref="SimulationTiming.WorkApplicationsPerSecond"/>.</summary>
    public static int PerApplication(
        GameDatabase database, TileWorkKind kind, int efficiency = EfficiencyRules.BaseModifier)
    {
        // Through ObservedCorrections: one variable in the table does not produce
        // the behaviour the original shows, and this is the single seam where
        // that correction enters the simulation (ADR-0023, Q20).
        int perSecond = ObservedCorrections.TileWorkPerSecond(database, Variable(kind));

        // An arithmetic shift, so this floors rather than truncating toward zero.
        return perSecond * efficiency >> SimulationTiming.WorkApplicationShift;
    }

    /// <summary>What a creature of the Good player applies in action 11's first arm: the cached
    /// <c>AttackRoomHealth</c> (the tabled value over variable 19, a truncating divide) times
    /// <see cref="HeroAttackRoomMultiplier"/>, with no rate: −1500 / 4 × 10 = −3750.</summary>
    public static int HeroAttackRoomPerApplication(GameDatabase database) =>
        database[VariableTypeId.AttackRoomHealth] / SimulationTiming.OriginalTicksPerGameSecond * HeroAttackRoomMultiplier;

    /// <summary>The engine's own ×10 in action 11's hero arm (<c>0x00497EC1</c>: LEA ×5, SHL 1).</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/room-health.md")]
    public const int HeroAttackRoomMultiplier = 10;

    /// <summary>
    /// Which callers can raise health into a different
    /// <see cref="TerrainDef.MaxHealthTypeTerrainId"/>. DK2's applier has no such
    /// guard; its callers make the distinction, and no positive path heals floor
    /// (Q215, docs/research/tile-work.md).
    /// </summary>
    public static bool CompletesUpward(TileWorkKind kind) =>
        kind is TileWorkKind.Claim or TileWorkKind.Reinforce;

    /// <summary>
    /// Whether the work ends by taking the room over rather than destroying the tile,
    /// despite running a health <i>down</i> to zero. Only
    /// <see cref="TileWorkKind.ConvertRoom"/> does; the mixed direction is the
    /// data's (<c>ClaimTileHealth</c> +825 up, <c>ConvertRoomHealth</c> −4000
    /// down). It runs the room's health down, not the tile's, so the terrain at
    /// completion is the terrain now.</summary>
    public static bool CompletesAsTakeover(TileWorkKind kind) =>
        kind is TileWorkKind.ConvertRoom;

    /// <summary>Kinds applied to a room's one health (<see cref="RoomHealthRules"/>) rather than
    /// to a tile's: they need the world, so <see cref="Apply"/> refuses them.</summary>
    public static bool IsRoomScoped(TileWorkKind kind) => kind is TileWorkKind.ConvertRoom or TileWorkKind.RepairRoom;

    /// <summary>The per-second health delta the balance table gives this kind of work,
    /// after <see cref="ObservedCorrections"/> and unscaled by Efficiency. The sign is
    /// what callers outside the application loop want — negative work ends at the
    /// terrain's <see cref="TerrainDef.DestroyedTypeTerrainId"/>.</summary>
    public static int PerSecond(GameDatabase database, TileWorkKind kind) =>
        ObservedCorrections.TileWorkPerSecond(database, Variable(kind));

    /// <summary>The terrain this tile will be once <paramref name="kind"/> has run
    /// to completion — for a repair job, the terrain it already is. Answers what a
    /// job leads to without simulating it, which <see cref="WorkStance"/> needs:
    /// standing on a worked tile depends on the tile at completion, not only the
    /// tile now (Q49, ADR-0028). Not whether the job applies — <see cref="Applies"/>.</summary>
    public static byte OutcomeTerrainId(GameDatabase database, in TileState tile, TileWorkKind kind)
    {
        TerrainDef terrain = database.TerrainById(tile.TerrainId);

        if (CompletesUpward(kind)) return UpwardDestination(terrain, kind);

        // A takeover changes only who holds the tile, so the tile at completion
        // is the tile now — which is what puts an imp inside a rival's room to
        // claim it, where the original puts one.
        if (CompletesAsTakeover(kind)) return tile.TerrainId;

        if (PerSecond(database, kind) < 0) return terrain.DestroyedTypeTerrainId;
        return tile.TerrainId;
    }

    /// <summary>The terrain an upward job converts a tile into: normally the
    /// terrain's own <see cref="TerrainDef.MaxHealthTypeTerrainId"/>, but on a claim
    /// of ground already of the claimable kind the terrain stays and only the owner
    /// changes — following the field there would turn a rival's Claimed Path into
    /// Rock, an inert transition nothing in DK2 triggers (Q18, Q219). Keyed on
    /// <c>Ownable</c>, so floor, bridge and room read alike.</summary>
    private static byte UpwardDestination(TerrainDef terrain, TileWorkKind kind) =>
        kind == TileWorkKind.Claim && terrain.IsOwnable
            ? terrain.TerrainId
            : terrain.MaxHealthTypeTerrainId;

    /// <summary>
    /// Whether <paramref name="kind"/> can be applied to this tile at all.
    /// Structural on purpose: it asks the terrain's flags and the tile's owner,
    /// never a terrain id — a rule written against terrain id 2 would be a rule
    /// about the reference install, not about DK2.
    /// </summary>
    public static bool Applies(GameDatabase database, in TileState tile, TileWorkKind kind, byte playerId)
        => Applies(database, tile, kind, playerId, ruinedHeart: false);

    /// <summary>The same, knowing whether the tile's owner has lost their heart: a
    /// ruined heart is claimable like any room (Q93, ADR-0162), a live one never is.</summary>
    public static bool Applies(
        WorldState world, GameDatabase database, in TileState tile, TileWorkKind kind, byte playerId)
        => Applies(database, tile, kind, playerId,
                   world.HasPlayer(tile.OwnerPlayerId) && world.Player(tile.OwnerPlayerId).HeartRuined);

    private static bool Applies(
        GameDatabase database, in TileState tile, TileWorkKind kind, byte playerId, bool ruinedHeart)
    {
        TerrainDef terrain = database.TerrainById(tile.TerrainId);

        return kind switch
        {
            // Diggable rock: solid, taggable, not impenetrable, nobody's property,
            // and worth no gold (gold seams are mined, not dug).
            TileWorkKind.Dig =>
                terrain.IsSolid && terrain.IsTaggable && !terrain.IsImpenetrable
                && !terrain.IsOwnable && terrain.GoldValue == 0,

            TileWorkKind.MineGold =>
                terrain.IsSolid && terrain.IsTaggable && terrain.GoldValue > 0,

            TileWorkKind.DigOwnWall =>
                terrain.IsSolid && terrain.IsOwnable && tile.OwnerPlayerId == playerId,

            TileWorkKind.DigEnemyWall =>
                terrain.IsSolid && terrain.IsOwnable && tile.OwnerPlayerId != playerId,

            // Claiming takes non-solid ground not already yours. Hostile floor is
            // attacked down to Dirt Path first and claimed after (AttackTile,
            // Q68); a room changes hands in one step instead (ConvertRoom, Q92).
            // The last clause is the "nowhere to go" guard.
            TileWorkKind.Claim =>
                !terrain.IsSolid
                && !terrain.IsRoom
                && tile.OwnerPlayerId != playerId
                && !IsHostileFloor(terrain, tile, playerId)
                && (terrain.IsOwnable || terrain.MaxHealthTypeTerrainId != tile.TerrainId),

            // Somebody else's room, a rival's or a neutral one such as an unclaimed portal (ADR-0043).
            // A live heart is excluded by terrain identity, a ruined one is a room like any other
            // (Q93, ADR-0162). A room on non-Attackable terrain, a gate, converts only once ruined
            // (0x00454040), which trigger action 32 does (ADR-0299, ADR-0320).
            TileWorkKind.ConvertRoom =>
                !terrain.IsSolid
                && terrain.IsRoom
                && (terrain.IsAttackable || tile.RoomRuined)
                && tile.OwnerPlayerId != playerId
                && (ruinedHeart || terrain.TerrainId != ManaRules.DungeonHeartTerrainId(database)),

            TileWorkKind.AttackTile => IsHostileFloor(terrain, tile, playerId),

            // Action 11 asks only for a room whose definition is Buildable (def +0x43D bit 0x80),
            // whoever holds it; who may start it is the hero rule's (RoomAttackRules).
            TileWorkKind.AttackRoom => database.RoomByTerrain(tile.TerrainId) is { IsBuildable: true },

            // Action 12's tile part: a room of the worker's keeper; the room's health below its cap
            // is RoomHealthRules.NeedsRepair (0x004E6C00).
            TileWorkKind.RepairRoom => terrain.IsRoom && tile.OwnerPlayerId == playerId,

            // Reinforcing turns rock into a wall; the data says which rock can.
            // The original splits it by health — reinforce below half MaxHealth
            // (fresh Rock is 165 of 660), fix anything not at MaxHealth, both at
            // the same step of the imp's search. This clause is their union.
            // Q153, binary, 2026-09-05.
            TileWorkKind.Reinforce =>
                terrain.IsSolid && !terrain.IsImpenetrable && !terrain.IsOwnable
                && terrain.MaxHealthTypeTerrainId != tile.TerrainId,

            TileWorkKind.RepairWall =>
                terrain.IsSolid && terrain.IsOwnable && tile.OwnerPlayerId == playerId,

            _ => false,
        };
    }

    /// <summary>Ground an imp attacks rather than claims: owned by anyone but
    /// <paramref name="playerId"/>, the unowned player included, <c>Ownable</c> and
    /// <c>Attackable</c>, neither <c>Solid</c> nor <c>Room</c>. The original's own
    /// predicate (<c>0x00454000</c>), which both attack handlers re-test after every
    /// application. Claimed Path is the one shipped terrain it admits.</summary>
    private static bool IsHostileFloor(TerrainDef terrain, in TileState tile, byte playerId) =>
        tile.OwnerPlayerId != playerId
        && terrain.IsOwnable && terrain.IsAttackable
        && !terrain.IsSolid && !terrain.IsRoom;

    /// <summary>
    /// The kinds of work a dig marker can be asking for. The order is not a
    /// priority: on the shipped data the four are mutually exclusive — dig wants
    /// unowned rock worth no gold, mine wants gold, the wall jobs want ownable
    /// terrain — so at most one applies to a tile, asserted by
    /// <c>ImpsOnRealDataTests.NoTerrainInTheGame_AdmitsTwoKindsOfMarkedWork</c>
    /// so a data change cannot make it a silent priority rule.
    /// </summary>
    private static readonly TileWorkKind[] TaggedWorkKinds =
    [
        TileWorkKind.Dig,
        TileWorkKind.MineGold,
        TileWorkKind.DigOwnWall,
        TileWorkKind.DigEnemyWall,
    ];

    private static readonly TileWorkKind[] DigTaskWorkKinds =
    [
        TileWorkKind.Dig,
        TileWorkKind.DigOwnWall,
        TileWorkKind.DigEnemyWall,
    ];

    /// <summary>Every kind of work a dig marker can call for. Order carries no meaning.</summary>
    public static IReadOnlyList<TileWorkKind> TaggedWork => TaggedWorkKinds;

    /// <summary>Re-resolves a continuing kind-1 dig task against the tile as it
    /// exists now. DK2 selects the rock, own-wall or enemy-wall rate before every
    /// application, so breaking a reinforced wall does not end its task at Rock
    /// (Q297).</summary>
    public static bool TryResolveDigTask(
        GameDatabase database,
        in TileState tile,
        TileWorkKind requested,
        byte playerId,
        out TileWorkKind resolved)
    {
        if (requested is not (TileWorkKind.Dig or TileWorkKind.DigOwnWall or TileWorkKind.DigEnemyWall))
        {
            resolved = requested;
            return false;
        }

        foreach (TileWorkKind candidate in DigTaskWorkKinds)
        {
            if (!Applies(database, tile, candidate, playerId)) continue;
            resolved = candidate;
            return true;
        }

        resolved = requested;
        return false;
    }

    /// <summary>What work a tile marked by <paramref name="playerId"/> is asking
    /// for — the whole mapping from "the keeper marked this" to "an imp does that":
    /// marked rock is dug, marked gold mined, a marked wall dug through at one rate
    /// if it is yours and another if it is not, all from flags and owner.</summary>
    public static bool TryTaggedWork(
        GameDatabase database, in TileState tile, byte playerId, out TileWorkKind kind)
    {
        foreach (TileWorkKind candidate in TaggedWorkKinds)
        {
            if (!Applies(database, tile, candidate, playerId)) continue;
            kind = candidate;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>
    /// Applies one application of work to <paramref name="tile"/>: add the delta,
    /// and at or below zero the tile becomes its
    /// <see cref="TerrainDef.DestroyedTypeTerrainId"/>, at or above the terrain's
    /// maximum (upward work only) its <see cref="TerrainDef.MaxHealthTypeTerrainId"/>
    /// — both at that type's starting health, without dig tags (<see cref="Convert"/>).
    /// </summary>
    /// <param name="efficiency">Worker Efficiency in 4096ths, from
    /// <see cref="EfficiencyRules.Modifier"/>; a slap or a well-aimed drop adds one
    /// <see cref="EfficiencyRules.BonusModifier"/> (Q159).</param>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/tile-work.md")]
    public static TileWorkResult Apply(
        GameDatabase database,
        ref TileState tile,
        TileWorkKind kind,
        byte playerId,
        int efficiency = EfficiencyRules.BaseModifier)
    {
        if (IsRoomScoped(kind))
            throw new ArgumentException($"{kind} works a room's health; apply it through RoomHealthRules.", nameof(kind));

        if (TryResolveDigTask(database, tile, kind, playerId, out TileWorkKind currentKind))
            kind = currentKind;

        if (!Applies(database, tile, kind, playerId))
            return new TileWorkResult(TileWorkOutcome.Rejected, 0, 0, 0, tile.TerrainId);

        TerrainDef terrain = database.TerrainById(tile.TerrainId);

        int delta = kind == TileWorkKind.AttackRoom && playerId == PlayerIds.Good
            ? HeroAttackRoomPerApplication(database)
            : PerApplication(database, kind, efficiency);

        if (delta < 0)
        {
            int applied = Math.Max(delta, -tile.Health);
            int gold = GoldFor(
                database, terrain, kind, tile.Health, tile.Health + applied,
                tile.Health + applied <= 0);

            tile.Health += applied;
            if (tile.Health > 0)
            {
                return new TileWorkResult(
                    applied == 0 ? TileWorkOutcome.NoChange : TileWorkOutcome.Progressed,
                    applied, gold, 0, tile.TerrainId);
            }

            // Through BridgeRules, not straight to the destroyed-type field: a
            // bridge beaten down leaves the water or lava it spanned, though
            // every room terrain in the data claims to leave Claimed Path (Q64).
            // One place decides, so selling and destroying cannot disagree.
            int spilled = Convert(
                database, ref tile, BridgeRules.RevertsTo(database, tile), playerId,
                takeOwnership: false);
            return new TileWorkResult(TileWorkOutcome.Destroyed, applied, gold, spilled, tile.TerrainId);
        }

        // Upward work and repair are one path because in DK2 they are one store:
        // the tile applier (FUN_00452f60) adds the delta into the tile's own
        // health word, clamps at 32000, and converts the moment health reaches
        // the terrain record's MaxHealth. A conversion job therefore costs the
        // DEFICIT, not the whole maximum: reinforcing Rock is 660 - 165 = 495,
        // fifty applications and not sixty-six. Q62, ADR-0089.
        if (CompletesUpward(kind))
        {
            // DK2 adds the whole delta and clamps at 32000; it does not stop at
            // MaxHealth. The overshoot is invisible because Convert resets the
            // health from the destination terrain's record, but reproducing it
            // keeps the reported amount honest.
            tile.Health = Math.Min(tile.Health + delta, MaxTileHealth);
            if (tile.Health < terrain.MaxHealth)
            {
                return new TileWorkResult(
                    delta == 0 ? TileWorkOutcome.NoChange : TileWorkOutcome.Progressed,
                    delta, 0, 0, tile.TerrainId);
            }

            int converted = Convert(
                database, ref tile, UpwardDestination(terrain, kind), playerId, takeOwnership: true);
            return new TileWorkResult(TileWorkOutcome.Transformed, delta, 0, converted, tile.TerrainId);
        }

        int room = terrain.MaxHealth - tile.Health;
        if (room <= 0)
            return new TileWorkResult(TileWorkOutcome.NoChange, 0, 0, 0, tile.TerrainId);

        int repaired = Math.Min(delta, room);
        tile.Health += repaired;
        return new TileWorkResult(
            repaired == 0 ? TileWorkOutcome.NoChange : TileWorkOutcome.Progressed,
            repaired, 0, 0, tile.TerrainId);
    }

    /// <summary>
    /// How much gold one more application of <paramref name="kind"/> would free
    /// from this tile, without applying it. Lets a worker decide whether its
    /// hands have room for another swing before taking one (Q31); an estimate
    /// would either overfill its hands or leave gold it could have carried, both
    /// visible. Mutates nothing.
    /// </summary>
    public static int GoldYield(GameDatabase database, in TileState tile, TileWorkKind kind, byte playerId)
    {
        if (!Applies(database, tile, kind, playerId)) return 0;

        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        int delta = PerApplication(database, kind);
        if (delta >= 0) return 0;

        int applied = Math.Max(delta, -tile.Health);
        return GoldFor(
            database, terrain, kind, tile.Health, tile.Health + applied,
            tile.Health + applied <= 0);
    }

    /// <summary>
    /// Gold freed by taking a tile from <paramref name="healthBefore"/> to
    /// <paramref name="healthAfter"/>. Two rules (Q21, docs/research/tile-work.md):
    /// <list type="bullet">
    /// <item>A seam is finite and yields in proportion to the rock removed —
    /// CONFIRMED (owner, 2026-08-30): <c>goldValue</c> 3000 over 660 health, mined
    /// 60 a time, 273 a swing; summed from running totals (<see cref="ExtractedBy"/>).</item>
    /// <item>A gem face is infinite — impenetrable, destroyed type itself — so it
    /// collapses, reappears at full health and yields <c>GoldMinedFromGems</c> (25)
    /// each cycle. Endless but slow.</item></list>
    /// </summary>
    private static int GoldFor(
        GameDatabase database,
        TerrainDef terrain,
        TileWorkKind kind,
        int healthBefore,
        int healthAfter,
        bool destroyed)
    {
        if (kind != TileWorkKind.MineGold || terrain.GoldValue == 0) return 0;

        if (terrain.IsImpenetrable)
            return destroyed ? database[VariableTypeId.GoldMinedFromGems] : 0;

        if (terrain.MaxHealth <= 0) return 0;
        return ExtractedBy(terrain, healthAfter) - ExtractedBy(terrain, healthBefore);
    }

    /// <summary>
    /// How much of a seam's gold has come out by the time it is down to
    /// <paramref name="health"/>. From where the seam has got to, not from the
    /// last swing: truncating each swing loses a coin a swing. Differencing two
    /// running totals yields exactly <c>goldValue</c> over a full seam, stateless.
    /// On the shipped seam (3000 gold over 660 health, 60 a swing, Q47) that is
    /// 273 on eight of eleven swings and 272 on three — the owner saw 273.
    /// </summary>
    private static int ExtractedBy(TerrainDef terrain, int health) =>
        (int)((long)terrain.GoldValue * Math.Max(0, terrain.MaxHealth - health) / terrain.MaxHealth);

    /// <summary>
    /// Turns a tile into another terrain type at that type's starting health.
    /// <para>A dig mark survives only if the new terrain can carry one: rock that
    /// becomes floor loses it, a gem face (destroyed type itself) keeps it, which
    /// is why marking one sets imps mining indefinitely — STRONGLY INFERRED.
    /// A takeover of ownable terrain assigns the worker's player; conversion to
    /// unownable terrain assigns <c>PlayerIds.Neutral</c> (Q95, ADR-0045).
    /// Otherwise ownership stays. Gold the terrain cannot hold comes loose,
    /// so <see cref="GoldStore.Total"/> counts nothing unreachable.</para>
    /// </summary>
    /// <returns>Gold that was on the tile and could not stay.</returns>
    private static int Convert(
        GameDatabase database,
        ref TileState tile,
        byte newTerrainId,
        byte playerId,
        bool takeOwnership)
    {
        TerrainDef next = database.TerrainById(newTerrainId);
        bool terrainChanged = tile.TerrainId != newTerrainId;

        tile.TerrainId = newTerrainId;
        tile.Health = next.StartingHealth;

        // Only when the tile actually became something else: a rival claiming a
        // burning wooden bridge takes it fire and all, because putting the fire
        // out by taking the tile would reward the claim (Q67, Q68).
        if (terrainChanged) tile.BurnTicksRemaining = 0;
        if (!next.IsTaggable) tile.TaggedBy = 0;

        byte ownerBefore = tile.OwnerPlayerId;
        if (takeOwnership && next.IsOwnable) tile.OwnerPlayerId = playerId;

        // Tile_SetOwner (0x004559D0) hands the new owner the current terrain's ManaGain alone, so
        // a room tile changing hands brings its new owner nothing of the floor it was built on.
        if (tile.OwnerPlayerId != ownerBefore) tile.KeptManaGain = 0;

        // A portal changing hands restarts it: for both portal types DK2's
        // ownership-change handler stamps the clock and zeros the generation
        // tally, so a rival taking a worn-out portal gets the base interval
        // (evidence in PortalRules.OnOwnerChanged). Keyed on terrain rather than
        // a room lookup because this runs for every converted tile.
        if (tile.OwnerPlayerId != ownerBefore
            && PortalRules.IsPortalTerrain(database, tile.TerrainId))
        {
            PortalRules.OnOwnerChanged(ref tile);
        }

        // DK2's terrain conversion assigns the neutral player when the destination
        // cannot be owned (0x0044FC40); Level 1's tile triggers read that owner.
        // This also lets a keeper claim the ground dug out of their own wall.
        if (!next.IsOwnable) tile.OwnerPlayerId = PlayerIds.Neutral;

        int spilled = Math.Max(0, tile.GoldHeld - GoldStore.Capacity(database, tile));
        tile.GoldHeld -= spilled;
        return spilled;
    }
}
