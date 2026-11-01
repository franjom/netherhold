// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>Makes imps: free ones to keep a keeper above the floor, bought ones when
/// a keeper casts Create Imp.</summary>
/// <remarks>Player initialization disables the native-turn deadline; the Heart opening arms it.
/// A keeper below MinimumImpThreshold receives free casts after the strict deadline, with
/// TimeBeforeFreeImpGeneratedSeconds between deadlines. See docs/research/imps-and-mana.md.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imps-and-mana.md")]
public sealed class ImpSupplySystem : ISimulationSystem
{
    private const string SystemName = "ImpSupply";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (CreateImpCommand create in context.Commands<CreateImpCommand>())
            Buy(context, create);

        TopUp(context);
    }

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

            // A ruined-at-load Heart leaves the free-cast origin at (1,1).
            // The cast still consumes a direction draw and its deadline on failure.
            TilePos? freeAt = null;
            if (player.HeartRuined && player.HeartLastAttackerPlayerId == 0)
            {
                int angle = context.Rng.NextInt(0x800);
                freeAt = new TilePos((0x1800 + ((TrapRules.Cosine(angle) * 0x2000) >> 16)) >> 12,
                    (0x1800 + ((TrapRules.Sine(angle) * 0x2000) >> 16)) >> 12);
                if (!world.Contains(freeAt.Value)
                    || (context.Navigation.NavByte(freeAt.Value.X, freeAt.Value.Y) & 8) != 0) continue;
            }
            if (Spawn(context, playerId, freeAt) is not { } imp) continue;

            context.Emit(SystemName, "imp_generated",
                $"player={playerId} imp={imp.Id} at=({imp.Tile.X},{imp.Tile.Y}) " +
                $"free (below MinimumImpThreshold {minimum})");
        }
    }

    /// <summary>
    /// Casts Create Imp. All or nothing: a keeper who cannot afford it gets
    /// nothing and is told why, because a half-paid imp would be a worse bug
    /// than a refused one — the same rule <see cref="GoldStore.TryWithdraw"/>
    /// follows for rooms.
    /// </summary>
    private static void Buy(SimulationContext context, CreateImpCommand create)
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

        if (Spawn(context, playerId, at) is not { } imp)
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} cost={cost} reason={(at is null ? "no_dungeon_heart" : "outside_the_map")}");
            return;
        }

        if (create.Position is { } position)
        {
            imp.PosX = imp.LastPosX = position.X.Raw;
            imp.PosY = imp.LastPosY = position.Y.Raw;
            ImpCreationRules.Begin(context, imp);
        }

        player.Mana -= cost;
        context.Emit(SystemName, "imp_created",
            $"player={playerId} imp={imp.Id} at=({imp.Tile.X},{imp.Tile.Y}) " +
            $"cost={cost} mana={player.Mana}");
    }

    /// <summary>Puts a new imp on the cast's tile, or on this keeper's dungeon heart when the
    /// cast names none (free imps and <see cref="CreateImpCommand"/> without a tile). Returns
    /// null for a tile outside the map, or with no tile, for a keeper without a heart.</summary>
    /// <remarks>The type is found by name, <c>Imp</c> — not by id, and not by the
    /// worker flag, because the Dwarf carries it too and is not what Create Imp
    /// makes.</remarks>
    private static CreatureState? Spawn(SimulationContext context, PlayerId playerId, TilePos? at = null)
    {
        WorldState world = context.World;
        TilePos tile;
        if (at is { } cast)
        {
            if (!world.Contains(cast)) return null;
            tile = cast;
        }
        else
        {
            int heart = ImpSupplyRules.HeartTileOf(world, context.Database, playerId);
            if (heart < 0) return null;
            tile = new TilePos(heart % world.Width, heart / world.Width);
        }

        CreatureDef? imp = context.Database.CreatureByName("Imp");
        if (imp is null) return null;

        CreatureState made = world.SpawnCreature(context.Database, imp.CreatureId, playerId, tile);

        // The shot's second datum is the level: 1 normally, 4 once research has upgraded
        // Create Imp (bonus shot 60, data (1, 4)). Free imps take the same cast and the same
        // branch (0x004BBD30), so they arrive upgraded too (research-tree.md §5).
        int level = ImpLevel(context, playerId);
        if (level != made.Level)
        {
            made.Level = level;
            made.Health = CreatureLevelRules.MaxHealth(context.Database, imp, level);
        }

        // The cast puts the imp down as the hand does: DK2's new imps pass 214, then 11 and
        // 10, and look around where they landed (Level 2 seed 04: four imps made at (14,24)
        // reinforce the walls beside them). The same fields the drop writes (HandSystem):
        // the one-shot look, the kind just done zeroed, no chooser cap. ADR-0312.
        made.JustDropped = true;
        made.LastTaskKind = ImpTaskKind.None;
        made.TimerTicks = 0;
        made.WorkerIdle = false;

        return made;
    }

    /// <summary>The level a Create Imp cast makes for <paramref name="playerId"/>: the second
    /// datum of the spell's shot, or of its bonus shot when the keeper's row is upgraded. Level
    /// 1 for a database without the spell.</summary>
    public static int ImpLevel(SimulationContext context, PlayerId playerId)
    {
        if (context.Database.KeeperSpell(KeeperSpellName.CreateImp) is not { } spell)
            return CreatureLevelRules.FirstLevel;

        bool upgraded = context.World.IsUpgraded(
            playerId, ResearchTreeRules.AvailabilityKindOf(ResearchTreeRules.KindSpell), spell.KeeperSpellId);
        return CreatureLevelRules.Clamp(spell.Shot(upgraded).Data2);
    }
}
