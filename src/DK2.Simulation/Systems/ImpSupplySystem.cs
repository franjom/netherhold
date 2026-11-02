// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
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

            // The free cast (0x004BB4A3): two tiles from DungeonHeartPosition at a drawn angle,
            // X + sin and Y - cos (angle 0 is north). A ruined-at-load Heart never set that
            // position, so the origin stays (1,1). A point creation refuses still costs the draw
            // and the deadline.
            int angle = context.Rng.NextInt(0x800);
            int heart = player.HeartRuined && player.HeartLastAttackerPlayerId == 0 ? -1
                : HeartSites.PositionOf(world, database, playerId);
            int originX = heart < 0 ? FearRules.NoHeartTile : heart % world.Width;
            int originY = heart < 0 ? FearRules.NoHeartTile : heart / world.Width;
            var point = new FpPosition2(
                new Fp32(CreatureState.CentreOf(originX) + ((TrapRules.Sine(angle) * 0x2000) >> 16)),
                new Fp32(CreatureState.CentreOf(originY) - ((TrapRules.Cosine(angle) * 0x2000) >> 16)));
            if (point.X.Raw < 0 || point.Y.Raw < 0 || !world.Contains(point.Tile)) continue;
            context.Navigation.Refresh();
            if ((context.Navigation.NavByte(point.Tile.X, point.Tile.Y) & 8) != 0) continue;
            if (Spawn(context, playerId, point.Tile) is not { } imp) continue;
            Place(context, imp, point);

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

        if (create.Position is { } position) Place(context, imp, position);

        player.Mana -= cost;
        context.Emit(SystemName, "imp_created",
            $"player={playerId} imp={imp.Id} at=({imp.Tile.X},{imp.Tile.Y}) " +
            $"cost={cost} mana={player.Mana}");
    }

    /// <summary>Puts a new imp on the cast's tile, or on this keeper's dungeon heart when the
    /// cast names none (a <see cref="CreateImpCommand"/> without a tile). Returns
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

        CreatureState? made = world.TrySpawnCreature(context.Database, imp.CreatureId, playerId, tile);
        if (made is null) return null;

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

    /// <summary>Puts a cast's imp at the exact point, draws its heading and starts its fall (state
    /// 214), as the creation process's worker arm does for every Create Imp shot, bought or free:
    /// process 1 (0x004AD7A0) draws random(0x800), process 36 (0x004AFE50, the upgraded shot)
    /// one of four cardinal headings, random(4) &lt;&lt; 9.</summary>
    private static void Place(SimulationContext context, CreatureState imp, FpPosition2 point)
    {
        imp.PosX = imp.LastPosX = point.X.Raw;
        imp.PosY = imp.LastPosY = point.Y.Raw;
        imp.Heading = CreationProcess(context, imp.OwnerPlayerId) == CreateSingleCreatureProcess
            ? (context.Rng.NextInt(4) << 9) & 0x7FF
            : context.Rng.NextInt(0x800) & 0x7FF;
        ImpCreationRules.Begin(context, imp);
    }

    private const byte CreateSingleCreatureProcess = 36;

    /// <summary>The process type of the shot a Create Imp cast fires for <paramref name="playerId"/>.</summary>
    private static byte CreationProcess(SimulationContext context, PlayerId playerId) =>
        context.Database.KeeperSpell(KeeperSpellName.CreateImp) is { } spell
        && context.Database.Shots.TryGetValue(spell.Shot(IsUpgraded(context, playerId, spell)).ShotTypeId,
            out ShotDef? shot)
            ? shot.ProcessType : (byte)1;

    private static bool IsUpgraded(SimulationContext context, PlayerId playerId, KeeperSpellDef spell) =>
        context.World.IsUpgraded(playerId, ResearchTreeRules.AvailabilityKindOf(ResearchTreeRules.KindSpell),
            spell.KeeperSpellId);

    /// <summary>The level a Create Imp cast makes for <paramref name="playerId"/>: the second
    /// datum of the spell's shot, or of its bonus shot when the keeper's row is upgraded. Level
    /// 1 for a database without the spell.</summary>
    public static int ImpLevel(SimulationContext context, PlayerId playerId)
    {
        if (context.Database.KeeperSpell(KeeperSpellName.CreateImp) is not { } spell)
            return CreatureLevelRules.FirstLevel;

        return CreatureLevelRules.Clamp(spell.Shot(IsUpgraded(context, playerId, spell)).Data2);
    }
}
