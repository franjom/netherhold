// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The Prison: capture, withering, skeletons and the jail break.
///
/// <para>The other branch of DK2's imp task <b>kind 13</b>. An imp that finds an
/// unconscious creature carries its keeper's own to a Lair and somebody else's
/// to a <b>Prison</b> — room 11, the pair of room ids read straight out of the
/// original's task handler (<c>imp-task-priority.md</c> §4). ADR-0063 built the
/// first branch and stopped at the second because there was no prison; this is
/// the second.</para>
///
/// <para><b>Only an imp captures.</b> CONFIRMED by the owner (2026-09-05):
/// <i>"only an imp can drag an enemy creature to prison, a hand of evil can then
/// pick up that creature from prison and drop it around the dungeon, back in
/// prison, or onto a torture rack in the torture chamber"</i>. So the keeper's
/// hand is not a way of taking a prisoner — it is a way of moving one that has
/// already been taken, and <see cref="HandRules.CanPickUp"/> says exactly that.
/// </para>
///
/// <para><b>What DK2 keeps, in its own words.</b> Its room debug dump names the
/// prison's fields: <c>SafeCreatureCapacity</c>, <c>PrisonJailBroken</c>,
/// <c>DoorTileX</c>/<c>DoorTileY</c>, <c>DoorOpen</c>,
/// <c>DelayAutoCloseDoorCount</c>, on top of every room's <c>TotalCapacity</c>,
/// <c>UsedCapacity</c> and <c>CreatureList</c>. Its creature dump names
/// <c>PrisonOwner</c> at offset <c>0x3cb</c> — the keeper whose prison holds
/// this creature, which is a different thing from who owns the creature, and is
/// the field this project reproduces as
/// <see cref="CreatureState.PrisonOwnerPlayerId"/>. Facts about the binary in
/// our own words, ADR-0046.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison.md")]
public static class PrisonRules
{
    /// <summary>Nobody's prisoner — the value <see cref="CreatureState.PrisonOwnerPlayerId"/> starts at.</summary>
    public const int NoPrison = -1;

    /// <summary>
    /// How many prisoners a prison holds: <b>one per tile</b>.
    ///
    /// <para><b>Ours, and the weakest inference in this file.</b> DK2's room
    /// carries <c>TotalCapacity</c> and <c>UsedCapacity</c> as stored fields and
    /// how it derives them has not been read; <c>Rooms.kwd</c> has no capacity
    /// column at all. One per tile is what every other occupancy rule here uses
    /// (a lair holds one bed per tile) and it is what makes a prison's size mean
    /// something. Q166, and the owner can settle it in a minute: build a 3x3
    /// prison, capture creatures until the imps stop, and count them.</para>
    /// </summary>
    public static int Capacity(RoomInstance prison) => prison.TileCount;

    /// <summary>Whether this creature is somebody's prisoner.</summary>
    public static bool IsImprisoned(CreatureState creature) =>
        creature.PrisonOwnerPlayerId != NoPrison;

    /// <summary>
    /// Whether <paramref name="room"/> is a prison belonging to
    /// <paramref name="playerId"/>.
    /// </summary>
    public static bool IsPrisonOf(RoomInstance room, byte playerId) =>
        room.OwnerPlayerId == playerId && room.Definition.TypeId == RoomTypeId.Prison;

    /// <summary>
    /// A free tile in <paramref name="playerId"/>'s nearest prison, or -1 when
    /// every prison is full and -1 when there is no prison at all.
    ///
    /// <para><b>Full means no delivery</b>, which is the manual's own rule read
    /// back: <i>"imps look for the next nearest prison with space in it"</i>,
    /// and when there is none <i>"imps stop collecting prisoners"</i>. Because
    /// <see cref="CarryRules.TryDestinationFor"/> is asked before the walk
    /// starts, an imp does not set off across the dungeon for a creature it has
    /// nowhere to put.</para>
    ///
    /// <para>Prisons are ranked by straight-line distance from
    /// <paramref name="from"/>, as every other room walk here ranks them (Q25),
    /// and within a prison the lowest free tile index wins — row-major order,
    /// stable, and the same tie-break <see cref="LairRules"/> uses.</para>
    /// </summary>
    public static int FreeTileIn(
        WorldState world, GameDatabase database, byte playerId, TilePos from)
    {
        int best = -1;
        long bestDistance = long.MaxValue;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!IsPrisonOf(room, playerId)) continue;
            if (PrisonerCount(world, room) >= Capacity(room)) continue;

            foreach (int tileIndex in room.TileIndices)
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

    /// <summary>
    /// Puts <paramref name="creature"/> in <paramref name="playerId"/>'s prison
    /// on <paramref name="tileIndex"/>, and says whether it took.
    ///
    /// <para><b>A prisoner is alive, and barely.</b> It arrives unconscious —
    /// the only way it can arrive, because only an unconscious creature can be
    /// carried — and imprisonment ends the dying window rather than continuing
    /// it. It wakes at
    /// <see cref="CreatureHealthRules.CriticalHealth"/>: <b>20% of its maximum,
    /// which is two petals of the ten on the flower above its head</b>.
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-05, Q167): <i>"its flower
    /// gets two petals, and then that creature starts loosing health at some
    /// standard rate"</i>. A first draft of this gave a prisoner <i>full</i>
    /// health on nothing better than the shape of the mechanic, which would
    /// have made a Goblin take 500 game seconds to wither where it should take
    /// a hundred — the difference between the prison being a slow decision and
    /// an urgent one.</para>
    ///
    /// <para>The arithmetic rests on a ten-petal flower, which is inference
    /// rather than observation: <c>CreatureCriticalHealthPercentageOfMax</c> =
    /// <b>20</b> is the one shipped percentage that reads as two of ten, and
    /// <c>CreatureSleepsWhenBelowPercentHealth</c> = <b>50</b> reads as five of
    /// ten beside it. The owner's other half corroborates it from the far side:
    /// a chicken restores a Goblin 375 of 1500, a quarter, and they put it at
    /// <i>"2 more petals"</i>. Q171 asks for the petal count outright.</para>
    /// </summary>
    public static bool Imprison(
        WorldState world, GameDatabase database, CreatureState creature,
        byte playerId, int tileIndex)
    {
        if (tileIndex < 0 || tileIndex >= world.Width * world.Height) return false;

        LairRules.ReleaseBed(world, database, creature);

        creature.PrisonOwnerPlayerId = playerId;
        creature.PosX = CreatureState.CentreOf(tileIndex % world.Width);
        creature.PosY = CreatureState.CentreOf(tileIndex / world.Width);
        creature.MoveRemainder = 0;
        creature.Health = CreatureHealthRules.CriticalHealth(
            database, database.CreatureById(creature.CreatureTypeId), creature);
        creature.DyingTicks = 0;
        creature.SleepTicks = 0;
        creature.AwakeTicks = 0;
        creature.Activity = CreatureActivity.Imprisoned;
        creature.TargetTileIndex = -1;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
        creature.Path.Clear();
        creature.PathIndex = 0;
        return true;
    }

    /// <summary>
    /// Lets a prisoner out — used by the jail break, by losing the prison under
    /// it, and by the keeper dropping one somewhere that is not a prison.
    ///
    /// <para>The creature keeps its own keeper and everything else it had. It is
    /// simply a hostile creature standing in somebody's dungeon again, which is
    /// what the manual means by <i>"losing a prison frees every enemy in
    /// it"</i>.</para>
    /// </summary>
    public static void Release(CreatureState creature)
    {
        creature.PrisonOwnerPlayerId = NoPrison;
        if (creature.Activity == CreatureActivity.Imprisoned) creature.ClearJob();
    }

    /// <summary>
    /// The health a prison takes off a prisoner each game second:
    /// <c>PrisonModifyCreatureHealthPerSecond</c>, which ships at <b>-3</b>.
    ///
    /// <para>The manual says it in words — <i>"whilst in Prison, creatures
    /// gradually lose health as they wither away"</i> — and the variable is
    /// named for exactly this, which is the fourth time on this project that an
    /// owner-facing sentence and a shipped variable name have met.</para>
    ///
    /// <para><b>And it is hunger</b> (Q173, read 2026-09-06). The routine that
    /// applies it — <c>FUN_0047b7c0</c>, now known to be the prison state
    /// category's own per-tick hook and therefore the prison's beyond doubt —
    /// takes the health off <b>only when the creature's <c>HungerFill</c> is
    /// non-zero</b>. So the four creatures that never eat never wither: a
    /// <b>Skeleton</b> in a cell is stable for ever, which matters because a
    /// Skeleton is what a prison <i>makes</i>. The owner guessed this
    /// (2026-09-05: <i>"probably from hunger"</i>) and said he did not know; the
    /// binary says he was right.</para>
    ///
    /// <para><b>What is still ours is the cadence.</b> The original reloads a
    /// counter with <c>ticksPerSecond &lt;&lt; 2</c> and applies
    /// <c>PrisonModifyCreatureHealthPerSecond &lt;&lt; 2</c> — <b>-12 every four
    /// game seconds</b> rather than -3 every one. The average is identical and
    /// the granularity is not: a prisoner at 10 health survives three more
    /// seconds here and none there. It also applies
    /// <c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c> (150) under anger
    /// reason 9 in the same breath, which this project does not
    /// (<c>prison.md</c> §4b).</para>
    /// </summary>
    public static int WitherPerSecond(
        GameDatabase database, CreatureDef creature) =>
        CreatureHealthRules.CanGetHungry(creature)
            ? database[VariableTypeId.PrisonModifyCreatureHealthPerSecond]
            : 0;

    /// <summary>
    /// The chance in percent that an overcrowded prison breaks out this game
    /// second: <c>OvercrowdingJailBreakPercentagePerExtraCreature</c> — which
    /// ships at <b>50</b> — once per prisoner over capacity.
    ///
    /// <para><b>The cadence is ours.</b> The variable names a percentage per
    /// extra creature and says nothing about how often it is rolled; this rolls
    /// it once a game second, which is the clock every other repeated chance
    /// here rides (ADR-0043). Q168.</para>
    ///
    /// <para>A prison only becomes overcrowded because a keeper made it so —
    /// imps refuse to deliver into a full one — which is the manual's <i>"manual
    /// overcrowding"</i>, and it is also what happens when part of a prison in
    /// use is sold out from under its prisoners.</para>
    /// </summary>
    public static int JailBreakPercent(GameDatabase database, int over) =>
        over <= 0 ? 0 : over * database[VariableTypeId.OvercrowdingJailBreakPercentagePerExtraCreature];
}
