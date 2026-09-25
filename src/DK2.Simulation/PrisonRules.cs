// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The Prison: capture, withering, skeletons and the jail break — the other
/// branch of DK2's imp task <b>kind 13</b>, where an imp carries its keeper's own
/// unconscious creature to a Lair and somebody else's to a Prison (room 11, both ids
/// from the original's task handler; imp-task-priority.md §4, ADR-0063).
/// <para>Only an imp captures: CONFIRMED (owner, 2026-09-05) — the keeper's hand moves
/// a prisoner already taken, never takes one, which is what
/// <see cref="HandRules.CanPickUp"/> says. DK2's creature dump names <c>PrisonOwner</c>
/// at <c>0x3cb</c> — the keeper whose prison holds the creature, not who owns it —
/// reproduced as <see cref="CreatureState.PrisonOwnerPlayerId"/> (ADR-0046,
/// docs/research/prison.md).</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison.md")]
public static class PrisonRules
{
    /// <summary>Nobody's prisoner — the value <see cref="CreatureState.PrisonOwnerPlayerId"/> starts at.</summary>
    public const int NoPrison = -1;

    /// <summary>The captive's states, category 14 Prison (ADR-0191, <c>state-category-hooks.md</c>
    /// §3.2). An imp's admission (state 44) puts the captive in 114, which moves to 115 on an
    /// interior slab; a hand drop lands in <see cref="HandDropState"/>, which moves to 115 in a
    /// Prison.</summary>
    public const int AdmittedState = 114;

    /// <summary>115 decides: hungry, to 117; otherwise a walk in 6 to a slab nearby,
    /// resuming in <see cref="InTheCellState"/>.</summary>
    public const int DecidingState = 115;

    /// <summary>116: standing in the cell.</summary>
    public const int InTheCellState = 116;

    /// <summary>117: finds the nearest chicken in the prison and walks to it in 6, resuming
    /// in <see cref="EatingState"/>; with none, the hunger anger and back to 116.</summary>
    public const int SeekingFoodState = 117;

    /// <summary>118: eats the chicken underfoot, then <see cref="AfterEatingState"/>.</summary>
    public const int EatingState = 118;

    /// <summary>119: waits out the eating, then 115.</summary>
    public const int AfterEatingState = 119;

    /// <summary>77, the state a creature dropped from the hand takes (an idle state, category 7):
    /// its handler <c>0x0046F1F0</c> sends it to 115 in a Prison, 130 in a Torture Chamber and 63
    /// in a Workshop (ADR-0193). 120 reuses that handler through a thunk, but is a captive
    /// state, and from it 130 is refused.</summary>
    public const int HandDropState = 77;

    /// <summary>The chance, <b>per tick</b>, that a prisoner standing still takes
    /// another step — <b>20 in 100</b>. Read from DK2's state 116 (<c>0x74</c>), which
    /// rolls <c>random(100)</c> and enters the decision state 115 under 20. It rolls
    /// only while the prisoner is not already walking, so it is the pause between
    /// steps, about five ticks. A narrower path takes rolls 10..19 when an unread
    /// per-creature flag is set, halving it to 10 in 100 — not modelled, Q188.</summary>
    public const int WanderPercentPerTick = 20;

    /// <summary>The four orthogonal steps <b>in DK2's own table order</b> — north,
    /// east, south, west — as stored at <c>0x006BC350</c>. The order is behaviour, not
    /// cosmetics: the original draws a random starting index and walks the table
    /// forwards, so it decides which way a prisoner goes when blocked.</summary>
    private static readonly (int Dx, int Dy)[] Orthogonal =
        [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>How many directions a prisoner considers. DK2 passes 4.</summary>
    public const int WanderDirections = 4;

    /// <summary>Where a prisoner shuffles to next: <b>one tile, orthogonally, and never
    /// out of its own cell</b>. STRONGLY INFERRED from the original's instructions
    /// (<c>Room.cpp:1717</c>, which the decompiler garbles): it draws
    /// <c>random(count)</c> as a starting index, scans <c>(start + i) % count</c>
    /// forwards through the offset table, and <b>the first candidate that passes
    /// wins</b> — non-zero room id, same room instance, one terrain flag clear. The
    /// room-instance test is what keeps a prisoner in its cell, so no separate "do not
    /// leave" rule is needed, and <b>two prisoners may stand on one tile</b> as in the
    /// original, capacity never having been one per tile
    /// (<see cref="Capacity"/>).</summary>
    /// <param name="startDirection">The drawn starting index, <c>0</c>..<c>3</c>.</param>
    public static bool TryWanderStep(
        WorldState world,
        GameDatabase database,
        CreatureState creature,
        int startDirection,
        out TilePos step)
    {
        step = default;

        TilePos here = creature.Tile;
        RoomInstance? cell =
            RoomIndex.RoomAt(world, database, (here.Y * world.Width) + here.X);

        // A prisoner whose cell has stopped being a cell has bigger problems,
        // and PrisonSystem frees it on the same second.
        if (cell is null) return false;

        for (int i = 0; i < Orthogonal.Length; i++)
        {
            (int dx, int dy) = Orthogonal[
                ((startDirection % Orthogonal.Length) + Orthogonal.Length + i) % Orthogonal.Length];

            int x = here.X + dx;
            int y = here.Y + dy;
            if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) continue;

            // Inside the bars, not merely inside the room (ADR-0139).
            if (!cell.IsInterior((y * world.Width) + x)) continue;

            step = new TilePos(x, y);
            return true;
        }

        return false;
    }

    /// <summary>How much anger a prisoner takes each time it searches its cell for
    /// food and finds none: <b><c>AngerNoFood</c> divided by four</b>, floored at 1
    /// for any non-zero file value (the Imp, the Dwarf and Horny carry 1 and keep it).
    /// <para>The divide is the loader's, not the mechanic's: DK2 stores each
    /// <c>angerNo*</c> field as file value over
    /// <see cref="SimulationTiming.OriginalTicksPerGameSecond"/>, so the engine holds anger per DK2 tick and a per-tick adder delivers the tabled figure a second.
    /// This one is not per tick — state 117 adds one unit per failed search, gated to
    /// one a game second — so a hungry prisoner angers at a <b>quarter</b> of a
    /// bedless creature's rate. Shared arithmetic:
    /// <see cref="SimulationTiming.LoaderRatePerTick"/> (ADR-0083). The gate is
    /// <see cref="CreatureAngerRules.CanBeAngered"/> on
    /// <see cref="CreatureFlags2.NeverAngered"/>, where DK2 reads an unmapped flag bit: same intent, both excluding the Skeleton and Jack (Q210).</para></summary>
    public static int AngerPerFailedFoodSearch(CreatureDef creature)
    {
        if (!CreatureAngerRules.CanBeAngered(creature)) return 0;

        return SimulationTiming.LoaderRatePerTick(creature.AngerNoFood);
    }

    /// <summary>How many prisoners a prison holds: <b>the tiles the rim does not
    /// take, plus one</b> — so DK2's smallest working prison, a 3x3, holds <b>two</b>,
    /// and below 3x3 the count is zero, the "+1" never fires, and the room is not a
    /// prison. CONFIRMED off the game's status bar (owner, 2026-09-06): "Captives
    /// held: 0 / 2 … Maximum Skeletons: 2" on a 3x3 (Q166; this used to hold one per tile).
    /// <para>Read, not fitted: DK2's per-room-type capacity routine counts the tiles a
    /// "nothing is standing here" predicate accepts and adds one if the count is
    /// non-zero, into the field its dump calls <c>SafeCreatureCapacity</c>; the
    /// furniture takes the rim. Not a per-tile rule, so <see cref="FreeTileIn"/> giving
    /// each arrival its own tile is about where a body is dropped, not what limits the
    /// room. The rim is counted rather than derived from the bounding box
    /// (<see cref="RoomInstance.InteriorTileCount"/>) so an L-shape answers sensibly; whether DK2 furnishes one the same way is UNKNOWN.</para></summary>
    public static int Capacity(RoomInstance prison) =>
        prison.InteriorTileCount == 0 ? 0 : prison.InteriorTileCount + 1;

    /// <summary>Whether this prisoner's health drains at all: a neutral creature in an
    /// unclaimed prison waits there for ever (Q308, ADR-0142).</summary>
    public static bool Withers(CreatureState creature) =>
        creature.OwnerPlayerId != PlayerIds.Neutral
        || creature.PrisonOwnerPlayerId != PlayerIds.Neutral;

    /// <summary>How many Skeletons this keeper's prisons may have standing at once: the
    /// <b>sum of every one of that keeper's prisons' capacities</b>. Per keeper and over
    /// their prisons, not per prison and not per map (Q307, ADR-0143).</summary>
    /// <remarks>DK2 walks the owner's own room list from <c>player+0x36</c>, keeps the
    /// rooms whose <c>TypeId</c> is 11, and adds each one's <c>SafeCreatureCapacity</c>
    /// (<c>0x004E3123</c>). Two prisons therefore raise more between them than either
    /// could alone, and selling one lowers the ceiling for the other.</remarks>
    public static int SkeletonCap(WorldState world, GameDatabase database, byte keeper)
    {
        int cap = 0;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
            if (IsPrisonOf(room, keeper)) cap += Capacity(room);

        return cap;
    }

    /// <summary>How many of that keeper's creatures are already of the Skeleton's type,
    /// <b>anywhere on the map</b> — DK2 counts the owner's whole creature list
    /// (<c>player+0x06</c>, <c>0x004E315C</c>), not the ones still in a cell. A Skeleton
    /// that walked out of the prison it rose in still occupies the ceiling.</summary>
    public static int SkeletonCount(WorldState world, byte keeper, byte skeletonTypeId)
    {
        int held = 0;
        foreach (CreatureState creature in world.Creatures)
            if (creature.OwnerPlayerId == keeper && creature.CreatureTypeId == skeletonTypeId)
                held++;

        return held;
    }

    /// <summary>Whether this creature is somebody's prisoner.</summary>
    public static bool IsImprisoned(CreatureState creature) =>
        creature.PrisonOwnerPlayerId != NoPrison;

    /// <summary>Whether <paramref name="room"/> is a prison belonging to <paramref name="playerId"/>.</summary>
    public static bool IsPrisonOf(RoomInstance room, byte playerId) =>
        room.OwnerPlayerId == playerId && room.Definition.TypeId == RoomTypeId.Prison;

    /// <summary>A free tile in <paramref name="playerId"/>'s nearest prison, or -1
    /// when every prison is full and -1 when there is no prison at all — the manual's
    /// "imps look for the next nearest prison with space in it". Prisons rank by
    /// straight-line distance from <paramref name="from"/> (Q25) and the lowest free
    /// tile index wins inside one — row-major, stable, the tie-break
    /// <see cref="LairRules"/> uses.
    /// <para>Not by itself a reason to set off: that is
    /// <see cref="DeliveryTileIn"/>, which walks to a full prison too.</para></summary>
    public static int FreeTileIn(
        WorldState world, GameDatabase database, byte playerId, TilePos from)
    {
        int best = -1;
        long bestDistance = long.MaxValue;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!IsPrisonOf(room, playerId)) continue;
            if (PrisonerCount(world, room) >= Capacity(room)) continue;

            foreach (int tileIndex in room.InteriorTileIndices)
            {
                if (PrisonerOn(world, tileIndex) is not null) continue;

                long dx = (tileIndex % world.Width) - from.X;
                long dy = (tileIndex / world.Width) - from.Y;
                long distance = (dx * dx) + (dy * dy);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = tileIndex;
            }
        }

        return best;
    }

    /// <summary>Where a carried enemy is taken: a free cell when one of this keeper's
    /// prisons has one, and otherwise the <b>rim</b> of the nearest prison that has
    /// none. -1 only when the keeper has no prison at all.
    /// <para>DK2 checks capacity at the door, not before the pickup: an imp drags a
    /// downed enemy to a full prison, admission fails and the body is left bleeding on
    /// the rim, while a prison with space is preferred over one without (CONFIRMED,
    /// owner 2026-09-13; ADR-0148). Which rim tile and how ties between eligible
    /// prisons break are UNKNOWN; nearest by the same measure as everywhere else.</para></summary>
    public static int DeliveryTileIn(
        WorldState world, GameDatabase database, byte playerId, TilePos from)
    {
        int free = FreeTileIn(world, database, playerId, from);
        if (free >= 0) return free;

        int best = -1;
        long bestDistance = long.MaxValue;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!IsPrisonOf(room, playerId)) continue;

            foreach (int tileIndex in room.TileIndices)
            {
                if (room.IsInterior(tileIndex)) continue;

                long dx = (tileIndex % world.Width) - from.X;
                long dy = (tileIndex / world.Width) - from.Y;
                long distance = (dx * dx) + (dy * dy);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = tileIndex;
            }
        }

        return best;
    }

    /// <summary>Whether <paramref name="creature"/> is standing on a tile of one of
    /// <paramref name="playerId"/>'s prisons, cell or rim.</summary>
    public static bool StandsInPrisonOf(
        WorldState world, GameDatabase database, CreatureState creature, byte playerId)
    {
        int tileIndex = (creature.Tile.Y * world.Width) + creature.Tile.X;
        RoomInstance? room = RoomIndex.RoomAt(world, database, tileIndex);

        return room is not null && IsPrisonOf(room, playerId);
    }

    /// <summary>Whether a prisoner may be put into this tile <i>now</i>: it is a cell —
    /// an interior tile — of one of <paramref name="playerId"/>'s prisons, that prison
    /// is under <see cref="Capacity"/>, and nobody is standing on it. Asked at the door
    /// so that a prison which filled during the walk turns the body away.</summary>
    public static bool AdmitsAt(
        WorldState world, GameDatabase database, byte playerId, int tileIndex)
    {
        if (tileIndex < 0 || tileIndex >= world.Width * world.Height) return false;

        RoomInstance? room = RoomIndex.RoomAt(world, database, tileIndex);
        if (room is null || !IsPrisonOf(room, playerId)) return false;
        if (!room.IsInterior(tileIndex)) return false;
        if (PrisonerCount(world, room) >= Capacity(room)) return false;

        return PrisonerOn(world, tileIndex) is null;
    }

    /// <summary>How many prisoners stand in <paramref name="room"/>.</summary>
    public static int PrisonerCount(WorldState world, RoomInstance room)
    {
        int count = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            if (!IsImprisoned(creature)) continue;
            if (creature.PrisonOwnerPlayerId != room.OwnerPlayerId) continue;

            int tileIndex = (creature.Tile.Y * world.Width) + creature.Tile.X;
            if (room.TileIndices.Contains(tileIndex)) count++;
        }
        return count;
    }

    /// <summary>The prisoner standing on one tile, or null.</summary>
    public static CreatureState? PrisonerOn(WorldState world, int tileIndex)
    {
        foreach (CreatureState creature in world.Creatures)
        {
            if (!IsImprisoned(creature)) continue;
            if (HandRules.IsHeld(creature)) continue;
            if ((creature.Tile.Y * world.Width) + creature.Tile.X == tileIndex) return creature;
        }
        return null;
    }

    /// <summary>Puts <paramref name="creature"/> in <paramref name="playerId"/>'s
    /// prison on <paramref name="tileIndex"/>, and says whether it took. A prisoner
    /// arrives unconscious — the only way it can, since only an unconscious creature
    /// is carried — and imprisonment ends the dying window rather than continuing it.
    /// <para>It wakes at <see cref="CreatureHealthRules.KnockoutHealth"/>, <b>a sixth
    /// of maximum</b>, which is what DK2's damage path writes at the knockout (below
    /// zero and not already dying stores <c>max / 6</c> and the <c>CreatureDying</c>
    /// flag) rather than anything the prison does at the door. So a Goblin arrives at
    /// 250 of 1500 and lasts <b>83</b> game seconds. CONFIRMED (owner, 2026-09-05,
    /// Q167): "its flower gets two petals, and then that creature starts loosing
    /// health". Open, display only: a sixth is one petal of six, the owner counts
    /// two (Q167, Q171).</para></summary>
    public static bool Imprison(
        WorldState world, GameDatabase database, CreatureState creature,
        byte playerId, int tileIndex, bool dropped = false)
    {
        if (tileIndex < 0 || tileIndex >= world.Width * world.Height) return false;

        LairRules.ReleaseBed(world, database, creature);

        creature.PrisonOwnerPlayerId = playerId;
        creature.PosX = CreatureState.CentreOf(tileIndex % world.Width);
        creature.PosY = CreatureState.CentreOf(tileIndex / world.Width);
        creature.Body.Vel = Steering.Fixed16Vector.Zero;
        creature.Health = CreatureHealthRules.KnockoutHealth(
            database, database.CreatureById(creature.CreatureTypeId), creature);
        creature.DyingTicks = 0;
        creature.SleepTicks = 0;
        creature.AwakeTicks = 0;

        // Into the captive's states: 114 from an imp, 77 from the hand, then 115 (ADR-0191, ADR-0193).
        CreatureStateMachine.TrySetState(creature, dropped ? HandDropState : AdmittedState);
        CreatureStateMachine.TrySetState(creature, DecidingState);

        creature.TargetTileIndex = -1;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
        creature.StopMoving();
        return true;
    }

    /// <summary>Lets a prisoner out — used by the jail break, by losing the prison
    /// under it, and by the keeper dropping one somewhere that is not a prison. The
    /// creature keeps its keeper and everything else, becoming a hostile creature
    /// standing in somebody's dungeon again: the manual's "losing a prison frees every
    /// enemy in it".</summary>
    public static void Release(CreatureState creature)
    {
        bool wasCaptive = creature.PrisonOwnerPlayerId != NoPrison;
        creature.PrisonOwnerPlayerId = NoPrison;

        // A captive holds only the captive states and the walks between them; freed, it is idle.
        if (wasCaptive) CreatureStateMachine.TrySetState(creature, CreatureStateMachine.Idle);
        if (creature.Activity is CreatureActivity.Imprisoned or CreatureActivity.BeingTortured)
            creature.ClearJob();
    }

    /// <summary>The health a prison takes off a prisoner each game second:
    /// <c>PrisonModifyCreatureHealthPerSecond</c>, which ships at <b>-3</b>. The
    /// manual says it in words — "whilst in Prison, creatures gradually lose health as
    /// they wither away".
    /// <para>It is hunger: STRONGLY INFERRED (binary, 2026-09-06, Q173) — the hook takes
    /// the health off only when <c>HungerFill</c> is non-zero, so a <b>Skeleton</b> in a
    /// cell is stable for ever, which matters because a Skeleton is what a prison makes.
    /// What stops it entirely is <see cref="Withers"/>, not this. Charged in a lump every
    /// four seconds (ADR-0171), so <see cref="WitherPerApplication"/> is what lands.</para>
    /// </summary>
    public static int WitherPerSecond(
        GameDatabase database, CreatureDef creature) =>
        CreatureHealthRules.CanGetHungry(creature)
            ? database[VariableTypeId.PrisonModifyCreatureHealthPerSecond]
            : 0;

    /// <summary>Game seconds' worth in one bite: <b>four</b>, DK2's <c>variable19 &lt;&lt; 2</c>.</summary>
    public const int WitherIntervalSeconds = 4;

    /// <summary>The Prison hook's reload, <c>variable19 &lt;&lt; 2</c> = 16, so a bite every 17
    /// turns, the first on the fifth after entering the category (Q424, ADR-0192).</summary>
    public const int WitherReload = CreatureStateMachine.HookCountdownSeed * WitherIntervalSeconds;

    /// <summary>What one bite takes off — four seconds' worth in a lump, so -12.</summary>
    public static int WitherPerApplication(
        GameDatabase database, CreatureDef creature) =>
        WitherPerSecond(database, creature) * WitherIntervalSeconds;

    /// <summary>The anger a cell adds in the same breath as the health:
    /// <c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c> (150) into bucket
    /// <see cref="AngerBucket.Company"/>, DK2's complaint selector 9.
    /// <para>Not scaled by the interval — the hook passes the variable through whole,
    /// once every four seconds, so a prisoner gains 150 an application and not 600
    /// (ADR-0171; <c>state-category-hooks.md</c> §3). The name is the variable's, and
    /// the prison is a second reader of it; the mixed-company rule is the other.</para>
    /// </summary>
    public static int CellAngerPerApplication(GameDatabase database) =>
        database[VariableTypeId.ModifyAngerInCompanyOfHatedCreaturesPerSecond];

    /// <summary>The chance in percent that an overcrowded prison breaks out this game
    /// second: <c>OvercrowdingJailBreakPercentagePerExtraCreature</c>, which ships at
    /// <b>50</b>, once per prisoner over capacity. The cadence is ours — the variable
    /// says nothing about how often it is rolled, and this rolls it once a game second
    /// (ADR-0017). Q168. A prison only becomes overcrowded because a keeper made it so,
    /// imps refusing to deliver into a full one (the manual's "manual overcrowding"),
    /// or because part of a prison in use was sold under its prisoners.</summary>
    public static int JailBreakPercent(GameDatabase database, int over) =>
        over <= 0 ? 0 : over * database[VariableTypeId.OvercrowdingJailBreakPercentagePerExtraCreature];
}
