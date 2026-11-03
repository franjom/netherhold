// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>Casts Create Imp: the free cast that keeps a keeper above the floor, and the
/// bought cast's permission, payment and shot (<see cref="CreateImpCastSystem"/> calls it). The
/// shot makes the imp, in <see cref="ImpCreationRules.FromShot"/>.</summary>
/// <remarks>Player initialization disables the native-turn deadline; the Heart opening arms it.
/// A keeper below MinimumImpThreshold receives free casts after the strict deadline, with
/// TimeBeforeFreeImpGeneratedSeconds between deadlines. See docs/research/imps-and-mana.md.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imps-and-mana.md")]
public sealed class ImpSupplySystem : ISimulationSystem
{
    private const string SystemName = "ImpSupply";

    public string Name => SystemName;

    /// <summary>The free supply, in the player update. Bought casts are
    /// <see cref="CreateImpCastSystem"/>'s, with the keeper actions.</summary>
    public void Execute(SimulationContext context) => TopUp(context);

    /// <summary>Hands a short-handed keeper a free imp when one is due.</summary>
    /// <remarks>The deadline stores native turns (our tick + 1). The Heart opening arms it;
    /// 0x004BB497 requires now strictly after it and advances it even when creation fails.
    /// Free casts bypass availability, mana permission and payment.</remarks>
    private static void TopUp(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int minimum = ImpSupplyRules.MinimumImps(database);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(database);
        GameTick now = new(ManaRemovalLifecycle.Turn(context.Tick));

        foreach (PlayerId playerId in world.ActivePlayerOrder)
        {
            ref PlayerState player = ref world.Player(playerId);

            // Only a keeper is topped up (Q91). Nobody, the hero side and the
            // neutral player own real terrain but keep no imp-staffed dungeon -
            // a rule nothing else enforces, so a level that gave the hero side a
            // heart would have staffed it. The deadline is deliberately NOT
            // cleared for anyone declined, which is what makes a full-strength
            // keeper's next replacement immediate.
            if (!PlayerIds.IsKeeper(playerId.Value)) continue;
            if (player.KilledByPlayerId != 0) continue;

            if (ImpSupplyRules.ImpCount(world, database, playerId) >= minimum) continue;

            if (now <= player.NextFreeImpTick) continue;

            player.NextFreeImpTick = new GameTick(unchecked((uint)(now.Value + interval)));

            // The free cast (0x004BB4A3): two tiles from DungeonHeartPosition at a drawn angle,
            // X + sin and Y - cos (angle 0 is north). A ruined-at-load Heart never set that
            // position, so the origin stays (1,1). The shot makes the imp the next turn, where
            // a point creation refuses still costs the draw and the deadline (ADR-0635).
            int angle = context.Rng.NextInt(0x800);
            int heart = player.HeartRuined && player.HeartLastAttackerPlayerId == 0 ? -1
                : HeartSites.PositionOf(world, database, playerId);
            int originX = heart < 0 ? FearRules.NoHeartTile : heart % world.Width;
            int originY = heart < 0 ? FearRules.NoHeartTile : heart / world.Width;
            var point = new FpPosition2(
                new Fp32(CreatureState.CentreOf(originX) + ((TrapRules.Sine(angle) * 0x2000) >> 16)),
                new Fp32(CreatureState.CentreOf(originY) - ((TrapRules.Cosine(angle) * 0x2000) >> 16)));
            if (point.X.Raw < 0 || point.Y.Raw < 0 || !world.Contains(point.Tile)) continue;
            if (Cast(context, playerId, point) is not { } shot) continue;

            context.Emit(SystemName, "imp_generated",
                $"player={playerId} shot={shot.Id} at=({point.Tile.X},{point.Tile.Y}) " +
                $"free (below MinimumImpThreshold {minimum})");
        }
    }

    /// <summary>
    /// Casts Create Imp. All or nothing: a keeper who cannot afford it gets
    /// nothing and is told why, because a half-paid imp would be a worse bug
    /// than a refused one — the same rule <see cref="GoldStore.TryWithdraw"/>
    /// follows for rooms.
    /// </summary>
    internal static void Buy(SimulationContext context, CreateImpCommand create)
    {
        PlayerId playerId = create.PlayerId;
        TilePos? at = create.At;
        WorldState world = context.World;
        if (!world.HasPlayer(playerId))
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} reason=no_such_player");
            return;
        }

        // Create Imp is a keeper spell, so a non-keeper cannot cast it however
        // much mana the id happens to be holding (Q91). Refused before the cost
        // is even looked up, and refused *distinctly* from "no such player":
        // Neutral exists, owns terrain, and still may not have imps.
        if (!PlayerIds.IsKeeper(playerId.Value))
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} reason=not_a_keeper");
            return;
        }

        int cost = ImpSupplyRules.CreateImpCost(world, context.Database, playerId);
        ref PlayerState player = ref world.Player(playerId);

        if (player.Mana < cost)
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} cost={cost} mana={player.Mana} reason=not_enough_mana");
            return;
        }

        // The position permission 0x004BB8A0 (keeper-spell-casting.md §3), as far as the data
        // selects it for spell 4: the keeper's row must be AVAILABLE, then location selector 1
        // and target selector 4 at the tile. Level 2 researches Create Imp, and DK2's seeded
        // replays make an imp only where the cast tile is the keeper's own claimed floor
        // (ADR-0312). A table without the row is read as available, as a database without
        // the spell is.
        if (context.Database.KeeperSpell(KeeperSpellName.CreateImp) is { } spell)
        {
            int row = player.Availabilities.FindIndex(
                a => a.Kind == AvailabilityKind.Spell && a.TypeId == spell.KeeperSpellId);
            if (row >= 0 && player.Availabilities[row].Value != ResearchTreeRules.Available)
            {
                context.Emit(SystemName, "create_imp_refused",
                    $"player={playerId} cost={cost} reason=not_available");
                return;
            }

            if (at is { } tile)
            {
                context.Navigation.Refresh();
                if (!KeeperSpellRules.PlaceAllowed(world, context.Database, context.Navigation, playerId, spell, tile))
                {
                    context.Emit(SystemName, "create_imp_refused",
                        $"player={playerId} cost={cost} at=({tile.X},{tile.Y}) reason=location");
                    return;
                }
            }
        }

        if (PointOf(context, playerId, create) is not { } point)
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} cost={cost} reason={(at is null ? "no_dungeon_heart" : "outside_the_map")}");
            return;
        }
        if (Cast(context, playerId, point) is not { } shot)
        {
            context.Emit(SystemName, "create_imp_refused", $"player={playerId} cost={cost} reason=no_create_imp_shot");
            return;
        }

        player.Mana -= cost;
        context.Emit(SystemName, "imp_created",
            $"player={playerId} shot={shot.Id} at=({point.Tile.X},{point.Tile.Y}) " +
            $"cost={cost} mana={player.Mana}");
    }

    /// <summary>Where a bought cast lands: the command's exact point, else its tile's centre, else
    /// the centre of this keeper's heart tile (a <see cref="CreateImpCommand"/> without a tile).
    /// Null for a point outside the map, or with no tile, for a keeper without a heart.</summary>
    private static FpPosition2? PointOf(SimulationContext context, PlayerId playerId, CreateImpCommand create)
    {
        WorldState world = context.World;
        TilePos tile;
        if (create.At is { } cast) tile = cast;
        else
        {
            int heart = ImpSupplyRules.HeartTileOf(world, context.Database, playerId);
            if (heart < 0) return null;
            tile = new TilePos(heart % world.Width, heart / world.Width);
        }
        if (!world.Contains(tile)) return null;
        return create.Position ?? new FpPosition2(new Fp32(CreatureState.CentreOf(tile.X)), new Fp32(CreatureState.CentreOf(tile.Y)));
    }

    /// <summary>Fires the Create Imp shot at <paramref name="point"/> (<c>0x004BBD30</c>): shot 2, or
    /// the bonus shot 60 once research has upgraded the spell, carrying the spell's data (the
    /// creature type, then the count or level). It stands where it is made and dies on its first
    /// update, when its death event runs the creation process (<see cref="ImpCreationRules.FromShot"/>):
    /// this turn for a bought cast, made before the thing pass, the next for a free one, made after
    /// it. Null for a database without the spell or shot.</summary>
    private static ShotState? Cast(SimulationContext context, PlayerId playerId, FpPosition2 point)
    {
        if (context.Database.KeeperSpell(KeeperSpellName.CreateImp) is not { } spell) return null;
        var effect = spell.Shot(IsUpgraded(context, playerId, spell));
        if (!context.Database.Shots.TryGetValue(effect.ShotTypeId, out ShotDef? definition)) return null;
        ShotState shot = ShotRules.Spawn(context.World, definition, playerId, point.X.Raw, point.Y.Raw, effect.Data1, effect.Data2,
            0, FightTargetKind.None, -1, playerId, -1, point.X.Raw, point.Y.Raw);
        // The constructor's 0xEF and 0xF2 (shot 2 has no category, so none), then 0x004BBEBD's cast sound in KEEPER_SPELL_CREATE_IMP.
        // No trail is requested: the process makes no effect (ADR-0635), so the shot sits at the default view height.
        ShotRules.RequestConstructionSounds(context, shot, definition);
        KeeperSpellRules.RequestPlaceCastSound(context, spell, definition, playerId, new FpPosition3(point.X, point.Y, shot.ViewHeight));
        return shot;
    }

    private static bool IsUpgraded(SimulationContext context, PlayerId playerId, KeeperSpellDef spell) =>
        context.World.IsUpgraded(playerId, ResearchTreeRules.AvailabilityKindOf(ResearchTreeRules.KindSpell),
            spell.KeeperSpellId);
}
