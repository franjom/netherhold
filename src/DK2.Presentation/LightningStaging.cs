// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
namespace DK2.Presentation;
/// <summary>A launch check using the existing creature-spawn hook and ordinary commands.
/// The fight supplies its own cast, shot and electrification; no simulation rule is changed.</summary>
public sealed class LightningStaging
{
    private readonly PlayerId _viewer;
    private readonly CreatureTypeId _targetType;
    private readonly GameTick _thunderboltAfter;
    private const byte IncreaseLevelSpecial = 117, ManaBoostSpecial = 122;
    private bool _thunderboltPosted;
    public CreatureId Caster { get; }
    public CreatureId Target { get; }
    public TilePos Focus { get; }
    public Dk2Angle CameraTurn => new(1024);
    public TilePos TargetTile { get; }
    private LightningStaging(PlayerId viewer, CreatureTypeId targetType, CreatureId caster, CreatureId target, TilePos focus,
        TilePos targetTile, GameTick thunderboltAfter)
    {
        _viewer = viewer; _targetType = targetType; Caster = caster; Target = target; Focus = focus;
        TargetTile = targetTile;
        _thunderboltAfter = thunderboltAfter;
    }
    /// <summary>Places a Mistress and an opposing Monk two tiles apart on the viewer's dry floor.
    /// Ordinary Increase Level specials unlock her level-four lightning; Mana Boost funds the later keeper cast.</summary>
    public static LightningStaging? Prepare(GameSimulation simulation, PlayerId viewer, List<PlayerCommand> commands)
    {
        WorldState world = simulation.World;
        GameDatabase database = simulation.Database;
        var heroes = new PlayerId(PlayerIds.Good);
        if (!world.HasPlayer(viewer) || !world.HasPlayer(heroes)) return null;
        CreatureDef? mistress = null, monk = null;
        foreach (CreatureDef definition in database.Creatures.Values)
        {
            if (definition.Name == "Mistress") mistress = definition;
            if (definition.Name == "Monk") monk = definition;
        }
        if (mistress is null || monk is null || database.ObjectById(IncreaseLevelSpecial)?.IsSpecial != true
            || database.ObjectById(ManaBoostSpecial)?.IsSpecial != true) return null;
        TilePos? start = null, end = null;
        // Prefer a target on plain floor in front of the room geometry, which can hide its crackle.
        for (int y = world.Height - 1; y >= 0 && !start.HasValue; y--)
            for (int x = 0; x < world.Width && !start.HasValue; x++)
            {
                var targetAt = new TilePos(x, y);
                if (!DryFloor(world, database, viewer, targetAt)
                    || database.RoomByTerrain(world.Tile(targetAt).TerrainId) is not null) continue;
                for (int side = 0; side < 4; side++)
                {
                    int dx = side == 0 ? 1 : side == 1 ? -1 : 0;
                    int dy = side == 2 ? 1 : side == 3 ? -1 : 0;
                    var middle = new TilePos(x + dx, y + dy);
                    var casterAt = new TilePos(x + 2 * dx, y + 2 * dy);
                    if (!DryFloor(world, database, viewer, middle) || !DryFloor(world, database, viewer, casterAt)) continue;
                    start = casterAt; end = targetAt; break;
                }
            }
        foreach (CreatureState creature in world.OwnedCreatures(viewer))
        {
            if (start.HasValue) break;
            if (!DryFloor(world, database, viewer, creature.Tile)) continue;
            for (int side = 0; side < 4; side++)
            {
                int dx = side == 0 ? 1 : side == 1 ? -1 : 0;
                int dy = side == 2 ? 1 : side == 3 ? -1 : 0;
                var middle = new TilePos(creature.Tile.X + dx, creature.Tile.Y + dy);
                var other = new TilePos(creature.Tile.X + 2 * dx, creature.Tile.Y + 2 * dy);
                if (!DryFloor(world, database, viewer, middle) || !DryFloor(world, database, viewer, other)) continue;
                start = creature.Tile; end = other; break;
            }
        }
        if (start is not TilePos a || end is not TilePos b) return null;
        CreatureState? caster = world.TrySpawnCreature(database, new CreatureTypeId(mistress.CreatureId), viewer, a);
        if (caster is null) return null;
        var targetType = new CreatureTypeId(monk.CreatureId);
        CreatureState? target = world.TrySpawnCreature(database, targetType, heroes, b);
        if (target is null) return null;
        for (int i = 0; i < 4; i++)
        {
            ObjectState special = world.SpawnObject(database, i == 3 ? ManaBoostSpecial : IncreaseLevelSpecial,
                viewer, a.Y * world.Width + a.X);
            commands.Add(new ActivateSpecialCommand(viewer, special.Id));
        }
        return new LightningStaging(viewer, targetType, new CreatureId(caster.Id), new CreatureId(target.Id), a,
            b, new GameTick(world.CurrentTick.Value + 30));
    }
    /// <summary>After thirty turns, when the keeper can pay, places a fresh enemy on the encounter's floor and
    /// posts one normal Thunderbolt command. Returns true only on that posting frame.</summary>
    public bool QueueThunderboltWhenReady(GameSimulation simulation, List<PlayerCommand> commands)
    {
        WorldState world = simulation.World;
        GameDatabase database = simulation.Database;
        KeeperSpellDef? spell = database.KeeperSpellById(KeeperSpellRules.Thunderbolt);
        if (_thunderboltPosted || world.CurrentTick.Value < _thunderboltAfter.Value
            || spell is null || world.Player(_viewer).Mana < spell.ManaCost
            || !DryFloor(world, database, _viewer, TargetTile)) return false;
        CreatureState? target = world.TrySpawnCreature(database, _targetType, new PlayerId(PlayerIds.Good), TargetTile);
        if (target is null) { _thunderboltPosted = true; return false; }
        var command = new CastSpellOnCreatureCommand(_viewer, KeeperSpellRules.Thunderbolt, target.Id);
        _thunderboltPosted = true;
        if (KeeperSpellRules.CanCast(world, database, simulation.Navigation, world.CurrentTick, command) != KeeperSpellRefusal.None)
            return false;
        commands.Add(command);
        return true;
    }
    private static bool DryFloor(WorldState world, GameDatabase database, PlayerId viewer, TilePos tile) =>
        world.Contains(tile) && world.Tile(tile).OwnerPlayerId == viewer
        && database.Terrain.TryGetValue(world.Tile(tile).TerrainId, out TerrainDef? terrain)
        && (terrain.Flags & (TerrainFlags.Solid | TerrainFlags.Water | TerrainFlags.Lava)) == 0;
}
