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
/// being somewhere gold can be kept. <b>Also credited to nobody.</b> In DK2 a
/// destroyed treasury scatters its contents across the floor as gold piles;
/// piles are objects and objects are not parsed until M6, so the amount is
/// reported and dropped rather than quietly retained (Q31).
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown tile work kind."),
    };

    /// <summary>
    /// The health one application of <paramref name="kind"/> moves, and the
    /// fraction it leaves behind in <paramref name="remainder"/>.
    ///
    /// Work lands <see cref="SimulationTiming.WorkApplicationsPerSecond"/> times
    /// a second and the balance table's deltas are per second (Q19, Q47), so an
    /// application is a fifth of the tabled value. Eight of the ten divide
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
            TileWorkKind.Claim =>
                !terrain.IsSolid && !terrain.IsOwnable
                && terrain.MaxHealthTypeTerrainId != tile.TerrainId,

            TileWorkKind.AttackTile =>
                !terrain.IsSolid && terrain.IsOwnable && tile.OwnerPlayerId != playerId,

            TileWorkKind.RepairTile =>
                !terrain.IsSolid && terrain.IsOwnable && tile.OwnerPlayerId == playerId,

            // Reinforcing turns rock into a wall; the data says which rock can.
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

            int spilled = Convert(
                database, ref tile, terrain.DestroyedTypeTerrainId, playerId, takeOwnership: false);
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
                database, ref tile, terrain.MaxHealthTypeTerrainId, playerId, takeOwnership: true);
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

        tile.TerrainId = newTerrainId;
        tile.Health = next.StartingHealth;

        // The work was toward a conversion that has now happened; whatever the
        // tile becomes starts its own upward job from nothing.
        tile.UpwardWork = 0;
        tile.WorkRemainder = 0;
        if (!next.IsTaggable) tile.TaggedBy = 0;

        if (takeOwnership && next.IsOwnable) tile.OwnerPlayerId = playerId;

        int spilled = Math.Max(0, tile.GoldHeld - GoldStore.Capacity(database, tile));
        tile.GoldHeld -= spilled;
        return spilled;
    }
}
