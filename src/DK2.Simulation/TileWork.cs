// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// One kind of work a worker can apply to a tile.
///
/// The list is not invented: it is a 1:1 reading of the ten health-delta
/// variables that DK2's own global balance table carries for tile work
/// (ids 4–13 of <see cref="VariableTypeId"/>). Anything DK2 has no variable for
/// is not in this enum.
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

    /// <summary>Restore damaged claimed floor. <c>RepairTileHealth</c>.</summary>
    RepairTile = 6,

    /// <summary>Turn rock into a reinforced wall. <c>ReinforceWallHealth</c>.</summary>
    Reinforce = 7,

    /// <summary>Restore a damaged reinforced wall. <c>RepairWallHealth</c>.</summary>
    RepairWall = 8,

    /// <summary>Fill a floor tile back in. <c>FillInHealth</c>.</summary>
    FillIn = 9,

    /// <summary>
    /// Take a room that belongs to somebody else — a rival's or a neutral one.
    /// <c>ConvertRoomHealth</c>.
    ///
    /// <para><b>The one kind of work whose outcome is not confined to the tile
    /// it is applied to.</b> CONFIRMED (owner, 2026-09-04): <i>"once an imp
    /// claims a portal then the entire portal gets claimed, all tiles that are
    /// assigned to that portal are immediately claimed … rooms are instantly
    /// claimed by imps, not tile by tile."</i> So one tile is worked and the
    /// whole room instance changes hands at once. <see cref="Apply"/> converts
    /// the worked tile; <see cref="Systems.TileWorkSystem"/> spreads it to the
    /// rest, because "the rest of the room" needs the world and these rules are
    /// pure.</para>
    ///
    /// <para><b>This is a fix, not a new feature.</b>
    /// <see cref="TileWorkKind.Claim"/> already applied to room terrain — room
    /// terrain is <c>Ownable</c> and not <c>Solid</c>, which is all the claim
    /// rule asked for — so rooms had been converting tile by tile since rooms
    /// existed, at <c>ClaimTileHealth</c>'s rate, and nothing checked it. The
    /// <c>Claim</c> rule now excludes room terrain and this kind takes it over,
    /// which is also what makes <c>ConvertRoomHealth</c> mean something: it had
    /// been in <see cref="VariableTypeId"/> unused. Q92.</para>
    /// </summary>
    ConvertRoom = 10,
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
/// <param name="GoldReleased">
/// Gold freed from the rock by this application.
///
/// <b>Credited to nobody here.</b> The rules are pure: who ends up holding this
/// is <see cref="Systems.TileWorkSystem"/>'s business, and it puts it into the
/// hands of the creature named on the order — or reports it as uncollected when
/// no creature swung. See docs/research/tile-work.md and Q31.
/// </param>
/// <param name="GoldSpilled">
/// Gold that was stored on the tile and is no longer, because the tile stopped
/// being somewhere gold can be kept. <b>Credited to nobody, and that is the
/// point:</b> in DK2 a destroyed treasury leaves its contents on the floor as
/// piles that belong to whoever fetches them. The caller turns this number into
/// a pile on the tile (<c>FloorGold</c>); this record only reports how much came
/// loose, because the rules here take a tile and not a world.
/// </param>
/// <param name="NewTerrainId">The tile's terrain type after the application.</param>
public readonly record struct TileWorkResult(
    TileWorkOutcome Outcome,
    int HealthDelta,
    int GoldReleased,
    int GoldSpilled,
    byte NewTerrainId)
{
    public bool Changed => Outcome is TileWorkOutcome.Progressed
        or TileWorkOutcome.Destroyed
        or TileWorkOutcome.Transformed;
}

/// <summary>
/// A queued unit of work: "player P applies one application of kind K to tile I".
///
/// Produced by whatever is doing the work — a worker creature, a test, or the
/// scenario harness — and consumed by <see cref="Systems.TileWorkSystem"/> in
/// queue order.
/// </summary>
/// <param name="WorkerCreatureId">
/// The creature that swung, or -1 when nothing did.
///
/// Carried because gold has to go <i>into someone's hands</i>: in DK2 the imp
/// that freed gold from the rock is the imp that picks it up and carries it away
/// (Q31), so the resolver needs to know which one that was. Work queued by a
/// test or by the harness has no worker, and its gold is reported and dropped
/// exactly as before.
/// </param>
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
/// becomes when its health reaches a bound.
///
/// Pure and static on purpose — no world, no RNG, no time. Every number is read
/// from <see cref="GameDatabase"/>; none is written here. Evidence and
/// confidence for each rule: docs/research/tile-work.md.
/// </summary>
public static class TileWorkRules
{
    /// <summary>
    /// The global-variable each work kind draws its per-application health
    /// delta from. CONFIRMED — these are DK2's own variable names.
    /// </summary>
    public static VariableTypeId Variable(TileWorkKind kind) => kind switch
    {
        TileWorkKind.Dig => VariableTypeId.DigRockHealth,
        TileWorkKind.MineGold => VariableTypeId.MineGoldHealth,
        TileWorkKind.DigOwnWall => VariableTypeId.DigOwnWallHealth,
        TileWorkKind.DigEnemyWall => VariableTypeId.DigEnemyWallHealth,
        TileWorkKind.Claim => VariableTypeId.ClaimTileHealth,
        TileWorkKind.AttackTile => VariableTypeId.AttackTileHealth,
        TileWorkKind.RepairTile => VariableTypeId.RepairTileHealth,
        TileWorkKind.Reinforce => VariableTypeId.ReinforceWallHealth,
        TileWorkKind.RepairWall => VariableTypeId.RepairWallHealth,
        TileWorkKind.FillIn => VariableTypeId.FillInHealth,
        TileWorkKind.ConvertRoom => VariableTypeId.ConvertRoomHealth,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown tile work kind."),
    };

    /// <summary>
    /// The health one application of <paramref name="kind"/> moves, and the
    /// fraction it leaves behind in <paramref name="remainder"/>.
    ///
    /// Work lands <see cref="SimulationTiming.WorkApplicationsPerSecond"/> times
    /// a second and the balance table's deltas are per second (Q19, Q47), so an
    /// application is a fifth of the tabled value — which is the worker's
    /// Efficiency (80%) over four ticks a game second, ADR-0020's 2026-09-05
    /// amendment. Eight of the ten divide
    /// exactly; the other two carry their remainder into the next application so
    /// that the per-second rate stays exactly what the table says.
    ///
    /// <para>Mutates the remainder, which is why it takes it by reference and
    /// why <see cref="PeekPerApplication"/> exists for callers that only want to
    /// know what would happen.</para>
    /// </summary>
    public static int PerApplication(GameDatabase database, TileWorkKind kind, ref sbyte remainder)
    {
        // Through ObservedCorrections, not straight off the database: exactly
        // one variable in the table does not produce the behaviour the original
        // shows, and this is the single seam where that correction enters the
        // simulation. See ADR-0023 and Q20.
        int perSecond = ObservedCorrections.TileWorkPerSecond(database, Variable(kind));
        int rate = SimulationTiming.WorkApplicationsPerSecond;

        int whole = perSecond / rate;
        int carry = remainder + (perSecond % rate);

        if (Math.Abs(carry) >= rate)
        {
            whole += Math.Sign(carry);
            carry -= Math.Sign(carry) * rate;
        }

        remainder = (sbyte)carry;
        return whole;
    }

    /// <summary>What the next application would move, without moving it.</summary>
    public static int PeekPerApplication(GameDatabase database, TileWorkKind kind, sbyte remainder)
    {
        sbyte scratch = remainder;
        return PerApplication(database, kind, ref scratch);
    }

    /// <summary>
    /// Whether reaching maximum health under this kind of work converts the tile
    /// to its <see cref="TerrainDef.MaxHealthTypeTerrainId"/>.
    ///
    /// STRONGLY INFERRED. The alternative reading — "any tile at full health
    /// becomes its max-health type" — is contradicted by the data: Claimed Path
    /// ships at 660/660 with a max-health type of Rock, so a level-triggered
    /// rule would turn every claimed floor tile in every level into rock on the
    /// first tick. Making the conversion a property of the *work* rather than of
    /// the health level resolves that, and gives each of the three upward jobs
    /// exactly the destination the data records:
    /// Rock --Reinforce--&gt; Reinforced Wall, Dirt Path --Claim--&gt; Claimed Path,
    /// Claimed Path --FillIn--&gt; Rock. See docs/research/tile-work.md and Q18.
    /// </summary>
    public static bool CompletesUpward(TileWorkKind kind) =>
        kind is TileWorkKind.Claim or TileWorkKind.Reinforce or TileWorkKind.FillIn;

    /// <summary>
    /// Whether this kind of work ends by <b>taking the tile over</b> rather than
    /// by destroying it, despite running its health <i>down</i> to zero.
    ///
    /// <para>Exactly one kind does: <see cref="TileWorkKind.ConvertRoom"/>. And
    /// the mixed direction is the data's, not a choice —
    /// <c>ClaimTileHealth</c> is <b>+825</b> and builds a floor tile up to its
    /// maximum, while <c>ConvertRoomHealth</c> is <b>−4000</b> and beats a room
    /// down. Two jobs that both end in "this is mine now", from opposite
    /// directions, because the balance table gives them opposite signs.</para>
    ///
    /// <para>Without this, the downward path would send a converted room to its
    /// <see cref="TerrainDef.DestroyedTypeTerrainId"/> — every room terrain in
    /// the data claims to leave Claimed Path — so claiming a rival's treasury
    /// would demolish it. That is <c>AttackRoomHealth</c>'s job (−1500), and it
    /// is a separate work kind that does not exist yet.</para>
    /// </summary>
    public static bool CompletesAsTakeover(TileWorkKind kind) =>
        kind is TileWorkKind.ConvertRoom;

    /// <summary>
    /// The per-second health delta the balance table gives this kind of work,
    /// after <see cref="ObservedCorrections"/>.
    ///
    /// The sign is the interesting part and is what callers outside the
    /// application loop want: negative work takes a tile apart and ends at its
    /// <see cref="TerrainDef.DestroyedTypeTerrainId"/>, positive work builds it
    /// up. <see cref="PerApplication"/> is the per-swing figure and carries a
    /// remainder; this one is the raw rate and carries nothing.
    /// </summary>
    public static int PerSecond(GameDatabase database, TileWorkKind kind) =>
        ObservedCorrections.TileWorkPerSecond(database, Variable(kind));

    /// <summary>
    /// The terrain this tile will be once <paramref name="kind"/> has run to
    /// completion on it — which for a repair job is the terrain it already is.
    ///
    /// <para>Exists so that a caller can ask what a job <i>leads to</i> without
    /// simulating it. <see cref="WorkStance"/> is the one that needs it: whether
    /// a worker may stand on the tile it is working depends on the tile it will
    /// be standing on when the job finishes, not only on the tile it is now, and
    /// filling a floor tile back in is the case where those two differ (Q49).</para>
    ///
    /// <para>Says nothing about whether the job applies — ask
    /// <see cref="Applies"/> for that.</para>
    /// </summary>
    public static byte OutcomeTerrainId(GameDatabase database, in TileState tile, TileWorkKind kind)
    {
        TerrainDef terrain = database.TerrainById(tile.TerrainId);

        if (CompletesUpward(kind)) return UpwardDestination(terrain, kind);

        // A takeover leaves the terrain exactly as it found it and changes only
        // who holds it, so the tile a worker will be standing on when the job
        // finishes is the tile it is standing on now. That is what puts an imp
        // *inside* a rival's room to claim it, which is where the original puts
        // one.
        if (CompletesAsTakeover(kind)) return tile.TerrainId;

        if (PerSecond(database, kind) < 0) return terrain.DestroyedTypeTerrainId;
        return tile.TerrainId;
    }

    /// <summary>
    /// The terrain an upward job converts a tile into.
    ///
    /// <para>Normally the terrain's own <see cref="TerrainDef.MaxHealthTypeTerrainId"/>
    /// — Rock becomes a Reinforced Wall, Dirt Path becomes Claimed Path. The one
    /// exception is <b>claiming ground that is already the claimable kind</b>:
    /// taking a rival's Claimed Path must not follow that field, because Claimed
    /// Path's max-health type is <i>Rock</i> (it is the fill-in destination, Q18)
    /// and a claim would turn their floor into a wall.</para>
    ///
    /// <para>So for a claim the terrain stays put and only the owner changes,
    /// which is what taking somebody's ground means. Decided by the terrain's own
    /// <c>Ownable</c> flag rather than by comparing owners, so the rule reads the
    /// same for a rival's floor, a rival's bridge and a rival's room.</para>
    /// </summary>
    private static byte UpwardDestination(TerrainDef terrain, TileWorkKind kind) =>
        kind == TileWorkKind.Claim && terrain.IsOwnable
            ? terrain.TerrainId
            : terrain.MaxHealthTypeTerrainId;

    /// <summary>
    /// Whether <paramref name="kind"/> can be applied to this tile at all.
    ///
    /// Deliberately structural — it asks the terrain's own flags and the tile's
    /// ownership, never a hardcoded terrain id. A rule written against terrain
    /// id 2 would be a rule about the reference install, not about DK2.
    /// </summary>
    public static bool Applies(GameDatabase database, in TileState tile, TileWorkKind kind, byte playerId)
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

            // Claiming targets unowned floor that has somewhere to become.
            // Claiming takes ground that is not already yours — neutral OR a
            // rival's. CONFIRMED (Q68, owner 2026-09-03): a rival keeper's imp
            // claims your tile **directly**, with no neutral step in between,
            // which is what this replaces. Two clauses, and the second is the
            // "nowhere to go" guard: either the terrain is already the claimable
            // kind and only the owner changes, or it converts into something.
            // A room is NOT claimed this way: it is converted whole
            // (TileWorkKind.ConvertRoom), at its own rate, and this rule used to
            // take rooms tile by tile because room terrain satisfies every other
            // clause here. Q92.
            TileWorkKind.Claim =>
                !terrain.IsSolid
                && !terrain.IsRoom
                && tile.OwnerPlayerId != playerId
                && (terrain.IsOwnable || terrain.MaxHealthTypeTerrainId != tile.TerrainId),

            // Somebody else's room — a rival's or a neutral one, which is what
            // an unclaimed portal is (PlayerIds.Neutral, ADR-0043).
            //
            // The dungeon heart is the one exception, and it is STRONGLY
            // INFERRED rather than observed: a heart is beaten down, not taken,
            // and Q40's defeat rule — "rivals claim the rooms and gold" — is
            // what happens *after* it falls. Letting this apply to a heart would
            // send imps to steal one, which is visibly not DK2. By room identity
            // rather than by a flag because nothing in the record distinguishes
            // a heart from a portal: both are `ownable, room` terrain, and the
            // room flags differ only in Attackable and HasFlame. Q93.
            TileWorkKind.ConvertRoom =>
                !terrain.IsSolid
                && terrain.IsRoom
                && tile.OwnerPlayerId != playerId
                && terrain.TerrainId != ManaRules.DungeonHeartTerrainId(database),

            TileWorkKind.AttackTile =>
                !terrain.IsSolid && terrain.IsOwnable && tile.OwnerPlayerId != playerId,

            TileWorkKind.RepairTile =>
                !terrain.IsSolid && terrain.IsOwnable && tile.OwnerPlayerId == playerId,

            // Reinforcing turns rock into a wall; the data says which rock can.
            // The original splits this by health: its reinforce kind takes rock
            // below half of MaxHealth (fresh Rock is 165 of 660), its fix kind
            // takes anything not at MaxHealth, both at the same step of the
            // imp's search. By type, this clause selects the union of the two.
            // Q153, read from the binary 2026-09-05.
            TileWorkKind.Reinforce =>
                terrain.IsSolid && !terrain.IsImpenetrable && !terrain.IsOwnable
                && terrain.MaxHealthTypeTerrainId != tile.TerrainId,

            TileWorkKind.RepairWall =>
                terrain.IsSolid && terrain.IsOwnable && tile.OwnerPlayerId == playerId,

            TileWorkKind.FillIn =>
                terrain.Flags.HasFlag(TerrainFlags.FillInable)
                && terrain.MaxHealthTypeTerrainId != tile.TerrainId,

            _ => false,
        };
    }

    /// <summary>
    /// The kinds of work a dig marker can be asking for.
    ///
    /// The order is not a priority. The four are mutually exclusive on the
    /// shipped data — <see cref="TileWorkKind.Dig"/> demands unowned rock worth
    /// no gold, <see cref="TileWorkKind.MineGold"/> demands gold, and the two
    /// wall jobs demand ownable terrain — so at most one of them ever applies to
    /// a tile. <c>DK2.Behavior.Tests</c> asserts that against the real terrain
    /// table, so that a data change which broke the exclusivity would show up as
    /// a failing test rather than as a silent priority rule nobody chose.
    /// </summary>
    private static readonly TileWorkKind[] TaggedWorkKinds =
    [
        TileWorkKind.Dig,
        TileWorkKind.MineGold,
        TileWorkKind.DigOwnWall,
        TileWorkKind.DigEnemyWall,
    ];

    /// <summary>Every kind of work a dig marker can call for. Order carries no meaning.</summary>
    public static IReadOnlyList<TileWorkKind> TaggedWork => TaggedWorkKinds;

    /// <summary>
    /// What work a tile marked by <paramref name="playerId"/> is asking for.
    ///
    /// This is the whole of the mapping from "the keeper marked this" to "an imp
    /// does that": marked rock is dug, marked gold is mined, a marked wall is
    /// dug through at one rate if it is yours and another if it is not. Which
    /// applies follows from the terrain's flags and the tile's owner, never from
    /// a terrain id.
    /// </summary>
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
    /// Applies exactly one application of work to <paramref name="tile"/>.
    ///
    /// The rule is uniform and entirely data-driven:
    /// <list type="number">
    /// <item>add the kind's health delta;</item>
    /// <item>at or below zero the tile becomes its
    /// <see cref="TerrainDef.DestroyedTypeTerrainId"/>;</item>
    /// <item>at or above maximum it clamps, and — for upward work only — becomes
    /// its <see cref="TerrainDef.MaxHealthTypeTerrainId"/>;</item>
    /// <item>the new tile starts at its own terrain's starting health.</item>
    /// </list>
    /// A tile that converts loses its dig tags. Ownership is only ever written
    /// by work that takes ownership (claiming, reinforcing); see
    /// <see cref="Convert"/> for why it is never cleared.
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/tile-work.md")]
    public static TileWorkResult Apply(
        GameDatabase database,
        ref TileState tile,
        TileWorkKind kind,
        byte playerId)
    {
        if (!Applies(database, tile, kind, playerId))
            return new TileWorkResult(TileWorkOutcome.Rejected, 0, 0, 0, tile.TerrainId);

        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        int delta = PerApplication(database, kind, ref tile.WorkRemainder);

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

            // A takeover ran the same way down and means the opposite: the
            // terrain stays exactly what it was and only the owner changes.
            // Converting to the tile's own terrain is what resets its health,
            // its accumulated work and its remainder, and keeps a burning
            // bridge burning (Q67, Q68) because the terrain did not change.
            if (CompletesAsTakeover(kind))
            {
                int kept = Convert(
                    database, ref tile, tile.TerrainId, playerId, takeOwnership: true);
                return new TileWorkResult(
                    TileWorkOutcome.Transformed, applied, gold, kept, tile.TerrainId);
            }

            // Through BridgeRules, not straight to the destroyed-type field: a
            // bridge beaten down leaves the water or lava it spanned, and every
            // room terrain in the data claims to leave Claimed Path (Q64). One
            // place decides that, so selling a bridge and destroying one cannot
            // disagree.
            int spilled = Convert(
                database, ref tile, BridgeRules.RevertsTo(database, tile), playerId,
                takeOwnership: false);
            return new TileWorkResult(TileWorkOutcome.Destroyed, applied, gold, spilled, tile.TerrainId);
        }

        // A conversion job accumulates toward the terrain's whole MaxHealth and
        // leaves the tile's health alone; a repair job restores health and stops
        // at the maximum. The two used to share one path, and sharing it was the
        // bug: it made reinforcing cost the GAP from a wall's starting health
        // (495, so 50 swings) where the original charges the whole MaxHealth
        // (660, so 66). See ADR-0024, Q62 and TileState.UpwardWork.
        if (CompletesUpward(kind))
        {
            tile.UpwardWork += delta;
            if (tile.UpwardWork < terrain.MaxHealth)
            {
                return new TileWorkResult(
                    delta == 0 ? TileWorkOutcome.NoChange : TileWorkOutcome.Progressed,
                    delta, 0, 0, tile.TerrainId);
            }

            int lost = Convert(
                database, ref tile, UpwardDestination(terrain, kind), playerId, takeOwnership: true);
            return new TileWorkResult(TileWorkOutcome.Transformed, delta, 0, lost, tile.TerrainId);
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
    /// from this tile — without applying it.
    ///
    /// Exists so that a worker can decide whether its hands have room for
    /// another swing before it takes one (Q31). It has to be this function and
    /// not an estimate: an imp that guessed would either overfill its hands or
    /// walk away from gold it could have carried, and both are visible
    /// behaviour. Nothing here mutates anything.
    /// </summary>
    public static int GoldYield(GameDatabase database, in TileState tile, TileWorkKind kind, byte playerId)
    {
        if (!Applies(database, tile, kind, playerId)) return 0;

        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        int delta = PeekPerApplication(database, kind, tile.WorkRemainder);
        if (delta >= 0) return 0;

        int applied = Math.Max(delta, -tile.Health);
        return GoldFor(
            database, terrain, kind, tile.Health, tile.Health + applied,
            tile.Health + applied <= 0);
    }

    /// <summary>
    /// Gold freed by taking a tile from <paramref name="healthBefore"/> down to
    /// <paramref name="healthAfter"/>.
    ///
    /// Two different rules because the data forces it:
    /// <list type="bullet">
    /// <item>A gold seam is finite, and gold comes out <b>in proportion to the
    /// rock removed</b> — CONFIRMED (2026-08-30). It carries <c>goldValue</c>
    /// 3000 over 660 health and is mined 60 health at a time, and the owner
    /// measured 273 gold a swing in the original: 273 × 660 / 3000 = 60.06,
    /// which is the proportional rule stated backwards. See
    /// <see cref="ExtractedBy"/> for why the sum is taken from running totals
    /// rather than per swing.</item>
    /// <item>A gem face is infinite: it is impenetrable and its destroyed type
    /// is itself, so it collapses and immediately reappears at full health. The
    /// separate <c>GoldMinedFromGems</c> variable (25) is what each of those
    /// cycles yields. This is why gems are endless but slow.</item>
    /// </list>
    /// See docs/research/tile-work.md and Q21.
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
    /// <paramref name="health"/>.
    ///
    /// Gold is computed from where the seam has got to, not from how much the
    /// last swing removed, and the difference is not pedantry: truncating each
    /// swing independently loses a coin per swing and a seam ends up worth less
    /// than the data says it is. Taking the difference of two running totals
    /// makes a fully mined seam yield exactly its <c>goldValue</c>, and it needs
    /// no state to do it, because the running total is a function of health.
    ///
    /// <para>It also reproduces the measurement. On the shipped seam — 3000 gold
    /// over 660 health, mined 60 health at a time (Q47) — this yields 273 on
    /// eight of the eleven swings and 272 on the other three, totalling 3000.
    /// The owner observed 273 (2026-08-30).</para>
    /// </summary>
    private static int ExtractedBy(TerrainDef terrain, int health) =>
        (int)((long)terrain.GoldValue * Math.Max(0, terrain.MaxHealth - health) / terrain.MaxHealth);

    /// <summary>
    /// Turns a tile into another terrain type at that type's starting health.
    ///
    /// Dig markers survive only if the new terrain can still carry one. Rock
    /// that becomes floor loses its mark, because floor cannot be marked; a gem
    /// face, whose destroyed type is itself, keeps it. STRONGLY INFERRED, and
    /// the reason it is not merely "always clear the mark": marking a gem face
    /// once in the original sets imps mining it indefinitely, which a rule that
    /// cleared the mark on every collapse would turn into a single application.
    ///
    /// Ownership is written only when the new terrain is ownable and the work
    /// was the kind that takes ownership. It is never <i>cleared</i>, and that
    /// is deliberate: DK2's map data gives every tile an owner id including
    /// solid rock, and which id means "nobody" is UNKNOWN (Q17). Leaving the
    /// byte alone lets whatever the level file says is neutral propagate through
    /// a dug-out tile on its own, instead of us inventing a neutral id.
    ///
    /// Gold stored on the tile leaves with the tile if the new terrain cannot
    /// keep any — a treasury beaten down to claimed floor does not carry on
    /// holding three thousand gold in a tile with no capacity for it. The amount
    /// is returned so the caller can report it; the alternative, leaving the
    /// number in place, would make <see cref="GoldStore.Total"/> count gold that
    /// nothing can reach.
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

        // The work was toward a conversion that has now happened; whatever the
        // tile becomes starts its own upward job from nothing.
        tile.UpwardWork = 0;
        tile.WorkRemainder = 0;

        // Only when the tile actually became something else. A rival claiming a
        // burning wooden bridge takes the bridge, fire and all — putting the fire
        // out by taking the tile would be a reward for it (Q67, Q68).
        if (terrainChanged) tile.BurnTicksRemaining = 0;
        if (!next.IsTaggable) tile.TaggedBy = 0;

        if (takeOwnership && next.IsOwnable) tile.OwnerPlayerId = playerId;

        // A tile that cannot be owned does not carry an owner. This used to keep
        // the byte, on a reason that has since expired twice over — it said
        // "which owner id means nobody is UNKNOWN (Q17), and every rule that
        // cares asks IsOwnable first, so a stale byte on unownable terrain is
        // inert."
        //
        // <b>Q17 is answered</b> (ADR-0043): 0 is unowned, confirmed three ways
        // across all 66 shipped levels. <b>And the byte was never inert.</b>
        // TileWorkKind.Claim asks `OwnerPlayerId != playerId` and does NOT ask
        // IsOwnable first, so a keeper who dug through their own reinforced wall
        // was left with Dirt Path still stamped with their id — which is neither
        // claimable by them (the owner matches) nor their own land (the terrain
        // is not ownable). Their territory could not grow through it, ever. On
        // level 1 that walls a keeper in permanently, because the only route out
        // of its dungeon heart is through the keeper's own reinforced wall.
        //
        // Found 2026-09-04 by a scenario that dug from level 1's heart toward
        // its portal and did nothing at all for 130 seconds. Q95, ADR-0045.
        if (!next.IsOwnable) tile.OwnerPlayerId = PlayerIds.Unowned;

        int spilled = Math.Max(0, tile.GoldHeld - GoldStore.Capacity(database, tile));
        tile.GoldHeld -= spilled;
        return spilled;
    }
}
